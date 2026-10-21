using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EndHud.Vision.Inn;
using static EndHud.Tests.InnTestKit;

namespace EndHud.Tests;

/// <summary>One event of the reading loop, as the overlay got it: an inn reading (fresh or held), or null for leaving the inn.</summary>
/// <param name="At">When, on the recording's clock (the fake <see cref="InnClock"/>).</param>
internal sealed record InnReplayEvent(TimeSpan At, InnReading? Reading);

/// <summary>Something one of the session's overlays did that the player sees.</summary>
/// <param name="Display">"banner", "mark" or "gourmand" (a gourmand's banner).</param>
/// <param name="Change">"+" appeared, "-" went, "~" drawn again with other content.</param>
/// <param name="Why">What changed in the input on that step (screen, panel, tables, the call hold's reasons).</param>
internal sealed record InnViewChange(TimeSpan At, string Display, string Change, string What, string Why);

/// <summary>
/// The inn's session overlays replayed over a recorded sequence of readings, with the real session, call hold and
/// views (<see cref="InnDisplay"/>, as the overlay drives them) on a fake clock taken from the readings' times, and the
/// overlay's half-second tick between them. Every appearance, disappearance and change of a banner, a mark on the
/// dispatch panel and a gourmand's banner is recorded with the input that changed: text only.
/// </summary>
internal sealed class InnReplay
{
    /// <summary>The overlay's slow loop (OverlayWindow's DispatcherTimer): the inn is drawn again this often between readings.</summary>
    public static readonly TimeSpan TickEvery = TimeSpan.FromMilliseconds(500);

    private readonly InnSession _session = NewSession();
    private readonly InnDisplay _display = new();
    private HashSet<string> _banners = [], _marks = [], _gourmands = [];
    private Dictionary<string, string> _gourmandContent = [];
    private bool _panelShown;
    private InnReading? _last;
    private TimeSpan? _sessionFrom, _sessionTo;

    public List<InnViewChange> Changes { get; } = [];

    /// <summary>The span the session's overlays could show (first to last session reading), for the rate per minute.</summary>
    public TimeSpan SessionSpan => _sessionFrom is { } from && _sessionTo is { } to ? to - from : TimeSpan.Zero;

    public static InnReplay Run(IEnumerable<InnReplayEvent> events)
    {
        var replay = new InnReplay();
        TimeSpan? lastTick = null;
        foreach (var e in events)
        {
            // The overlay's ticks since the last event.
            for (var tick = (lastTick ?? e.At) + TickEvery; tick < e.At; tick += TickEvery)
            {
                replay.Tick(tick);
                lastTick = tick;
            }
            lastTick ??= e.At;
            replay.Step(e);
        }
        return replay;
    }

    private void Step(InnReplayEvent e)
    {
        string why = Describe(_last, e.Reading);
        _display.Show(_session, e.Reading, e.At);
        if (e.Reading is { InSession: true })
        {
            _sessionFrom ??= e.At;
            _sessionTo = e.At;
        }
        _last = e.Reading;
        Draw(e.At, why);
    }

    private void Tick(TimeSpan at)
    {
        _session.Tick(at);
        Draw(at, "tick");
    }

    private void Draw(TimeSpan at, string input)
    {
        var views = _display.Views(_session, at, callsEnabled: true, Text);
        bool panel = _display.LastAdvice?.Panel is not null;
        if (panel != _panelShown) input = $"{input}; panel taken as {(panel ? "open" : "closed")}";
        _panelShown = panel;
        string why = _session.CallChanges.Count > 0 ? $"{input}; calls: {string.Join("; ", _session.CallChanges)}" : input;
        TakeCalls(at, _display.LastAdvice, why);
        var banners = InnCallView.Of(views.Calls, Text).Select(b => $"{b.Key} ({b.Title})").ToHashSet();
        Diff("banner", _banners, banners, at, why);
        _banners = banners;
        var marks = views.Marks.Select(m => m.Key).ToHashSet();
        Diff("mark", _marks, marks, at, why);
        _marks = marks;
        var drawn = InnGourmandView.Banners(views.Wants, Text);
        var gourmands = drawn.Select(b => b.Key).ToHashSet();
        Diff("gourmand", _gourmands, gourmands, at, why);
        foreach (var banner in drawn)
            if (_gourmandContent.TryGetValue(banner.Key, out var before) && before != banner.Content) Changes.Add(new InnViewChange(at, "gourmand", "~", banner.Content, why));
        _gourmandContent = drawn.ToDictionary(b => b.Key, b => b.Content);
        _gourmands = gourmands;
    }

    private void Diff(string display, HashSet<string> before, HashSet<string> now, TimeSpan at, string why)
    {
        foreach (string gone in before.Except(now)) Changes.Add(new InnViewChange(at, display, "-", gone, why));
        foreach (string came in now.Except(before)) Changes.Add(new InnViewChange(at, display, "+", came, why));
    }

    /// <summary>What changed in the input from one event to the next, in words.</summary>
    private static string Describe(InnReading? before, InnReading? now)
    {
        if (now is null) return "left the inn";
        var parts = new List<string>();
        if (!now.Fresh) parts.Add($"held {now.Screen} (missed frame)");
        else
        {
            if (before?.Screen != now.Screen || before?.Fresh == false) parts.Add($"{(before is null ? "-" : before.Fresh ? before.Screen.ToString() : "held")} -> {now.Screen}");
            if (before?.Fresh != false && before?.Panel?.Kitchen != now.Panel?.Kitchen && before?.Screen == now.Screen) parts.Add($"panel kitchen {before?.Panel?.Kitchen} -> {now.Panel?.Kitchen}");
            if (before is { Fresh: true } && before.TableCards.Count == now.TableCards.Count)
                for (int i = 0; i < now.TableCards.Count; i++)
                    if (Cell(before.TableCards[i]) != Cell(now.TableCards[i])) parts.Add($"table {i + 1} {Cell(before.TableCards[i])} -> {Cell(now.TableCards[i])}");
            if (now.Bubble is not null && before?.Bubble is null) parts.Add("bubble");
        }
        return parts.Count == 0 ? "no input change" : string.Join(", ", parts);
    }

    internal static string Cell(TableCard? card) => card switch
    {
        null => "?",
        { State: TableState.Order } => string.Join("+", card.Items.Select(i => (i.Id ?? "?") + (i.Served ? "*" : ""))),
        _ => card.State.ToString(),
    };

    // ---- The kitchens' calls, banners shown or not ----

    /// <summary>A flash: a call gone and back in its kitchen within this long. A jump: a kitchen's call switching to another within it.</summary>
    public const double UnsteadyWithinSeconds = 2;

    /// <summary>One moment of the kitchens' calls (whether the banners show them or the open panel's marks do).</summary>
    /// <param name="Kitchens">Each kitchen's call: a dish to queue for an order, to cook ahead, or a swap.</param>
    /// <param name="Open">The orders a kitchen is still to cook for and nothing covers yet: (dish, table).</param>
    /// <param name="Queues">What each kitchen's queue holds (<see cref="InnSession.QueueSignature"/>).</param>
    /// <param name="CooksAhead">The advice cooks something ahead somewhere (no: the counter full, no stock, the end).</param>
    internal sealed record CallStep(TimeSpan At, IReadOnlyDictionary<int, InnCall> Kitchens, IReadOnlySet<(string Dish, int Table)> Open, string Queues,
        bool CooksAhead, string Why);

    /// <summary>A call that flashed (gone and back) or jumped (switched dish or kitchen with nothing done to cause it).</summary>
    internal sealed record Unsteady(TimeSpan At, int Kitchen, string What, string Why);

    public List<CallStep> Steps { get; } = [];

    private void TakeCalls(TimeSpan at, InnAdvice? advice, string why)
    {
        var kitchens = advice?.Calls.Where(c => c.Kind is InnCallKind.Queue or InnCallKind.CookAhead or InnCallKind.Swap && c.Kitchen is not null)
            .ToDictionary(c => c.Kitchen!.Value) ?? [];
        var open = new HashSet<(string, int)>();
        bool ahead = false;
        if (advice?.Dispatch is { } dispatch)
        {
            foreach (var k in dispatch.Kitchens)
            {
                foreach (var q in k.Queue)
                {
                    if (q.Table is { } table) open.Add((q.Dish.Id, table));
                    else ahead = true;
                }
                foreach (var s in k.Replaces) open.Add((s.Put.Id, s.Table));
            }
            foreach (var o in dispatch.Waiting.Concat(dispatch.OutOfStock)) open.Add((o.Dish.Id, o.Table));
        }
        string queues = advice is null ? "" : _session.QueueSignature();
        var before = Steps.Count > 0 ? Steps[^1].Kitchens : new Dictionary<int, InnCall>();
        string state = $"{why} [queues {queues}; open {string.Join(",", open.Select(o => $"{o.Item1}@{o.Item2}"))}; counter {(advice is null ? "" : _session.CounterSignature())}; advised {Advised(advice)}]";
        foreach (int k in before.Keys.Union(kitchens.Keys).Order())
        {
            string? was = before.GetValueOrDefault(k)?.Key, now = kitchens.GetValueOrDefault(k)?.Key;
            if (was == now) continue;
            if (was is not null) Changes.Add(new InnViewChange(at, "call", "-", was, state));
            if (now is not null) Changes.Add(new InnViewChange(at, "call", "+", now, state));
        }
        Steps.Add(new CallStep(at, kitchens, open, queues, ahead, why));
    }

    /// <summary>This moment's advice per kitchen, before the hold: "1:dish@table(ready in),… 2:…".</summary>
    private static string Advised(InnAdvice? advice) => advice?.Dispatch is not { } d ? "" : string.Join(" ", d.Kitchens.Select(k =>
        $"{k.Kitchen}:{string.Join(",", k.Replaces.Select(s => $"swap {s.Remove.Id}->{s.Put.Id}@{s.Table}").Concat(k.Queue.Select(q => $"{q.Dish.Id}@{q.Table?.ToString(CultureInfo.InvariantCulture) ?? "ahead"}({q.ReadyIn:F0})")))}"));

    /// <summary>
    /// The kitchens' calls that flashed or jumped (owner, 2026-10-21: "the recommended dish flashes heavily and it jumps
    /// around ... the recommendation should stay steady"), over the whole replay:
    /// <list type="bullet">
    /// <item><b>flash:</b> a kitchen's call gone and back within <see cref="UnsteadyWithinSeconds"/>;</item>
    /// <item><b>jump:</b> a kitchen's call switching to another dish (directly or after a gap that short), or the same
    /// order (or dish cooked ahead) switching kitchen, when the call that went was still possible and nothing was done to
    /// it: not queued, its order not served or gone, its kitchen not full, its kitchen's queue not added to by the player,
    /// and (for a dish cooked ahead) cooking ahead not stopped altogether. A dish cooked ahead giving way to an order that
    /// no other kitchen was showing is the order's banner coming up, not a jump.</item>
    /// </list>
    /// </summary>
    public (List<Unsteady> Flashes, List<Unsteady> Jumps) Steadiness()
    {
        var flashes = new List<Unsteady>();
        var jumps = new List<Unsteady>();
        var window = TimeSpan.FromSeconds(UnsteadyWithinSeconds);
        // Every call that went: its kitchen, when, and whether something done or impossible took it.
        var gone = new List<(int Kitchen, InnCall Call, TimeSpan At, bool Justified)>();
        for (int i = 1; i < Steps.Count; i++)
        {
            var step = Steps[i];
            var prior = Steps[i - 1];
            gone.RemoveAll(g => step.At - g.At > window);
            var cameNow = new List<(int Kitchen, InnCall Call)>();
            foreach (int k in prior.Kitchens.Keys.Union(step.Kitchens.Keys).Order())
            {
                var was = prior.Kitchens.GetValueOrDefault(k);
                var now = step.Kitchens.GetValueOrDefault(k);
                if (was?.Key == now?.Key) continue;
                if (was is not null) gone.Add((k, was, step.At, Justified(was, k, i)));
                if (now is not null) cameNow.Add((k, now));
            }
            foreach (var (k, now) in cameNow)
            {
                // Back in its kitchen after going a moment ago.
                if (gone.Any(g => g.Kitchen == k && g.Call.Key == now.Key && g.At < step.At))
                {
                    flashes.Add(new Unsteady(step.At, k, $"{now.Key} back", step.Why));
                    continue;
                }
                // The kitchen's last call went a moment ago (or now) though still possible, and another took its place.
                var last = gone.LastOrDefault(g => g.Kitchen == k);
                bool orderComingUp = last.Call?.Kind == InnCallKind.CookAhead && now.Kind is InnCallKind.Queue or InnCallKind.Swap && !ShownElsewhere(now, k, i, window);
                if (last.Call is not null && !last.Justified && last.Call.Key != now.Key && !orderComingUp)
                    jumps.Add(new Unsteady(step.At, k, $"{last.Call.Key} -> {now.Key}", step.Why));
                // The same order, or dish cooked ahead, moving here from another kitchen.
                foreach (var g in gone.Where(g => g.Kitchen != k && !g.Justified && g.Call.Kind == now.Kind && g.Call.Dish?.Id == now.Dish?.Id && g.Call.Table == now.Table))
                    jumps.Add(new Unsteady(step.At, k, $"{g.Call.Key} moved to kitchen {k}", step.Why));
            }
        }
        return (flashes, jumps);
    }

    /// <summary>The order <paramref name="call"/> is for was shown in another kitchen within <paramref name="window"/> before step <paramref name="i"/>.</summary>
    private bool ShownElsewhere(InnCall call, int kitchen, int i, TimeSpan window)
    {
        for (int j = i - 1; j >= 0 && Steps[i].At - Steps[j].At <= window; j--)
            if (Steps[j].Kitchens.Any(x => x.Key != kitchen && x.Value.Dish?.Id == call.Dish?.Id && x.Value.Table == call.Table)) return true;
        return false;
    }

    /// <summary>
    /// The call that went at step <paramref name="i"/> went for something done or because it became impossible. A call no
    /// longer valid stays its grace (<see cref="InnCallHold.GraceSeconds"/>) first, so the player's queueing that made it
    /// go may be up to that long before.
    /// </summary>
    private bool Justified(InnCall call, int kitchen, int i)
    {
        var step = Steps[i];
        if (step.Why.Contains($"{call.Key} dropped: done", StringComparison.Ordinal)) return true;
        if (step.Why.Contains($"{call.Key} dropped: the kitchen", StringComparison.Ordinal)) return true;
        if (call.Kind is InnCallKind.Queue or InnCallKind.Swap && !step.Open.Contains((call.Dish!.Id, call.Table!.Value))) return true;
        if (call.Kind == InnCallKind.CookAhead && !step.CooksAhead) return true;
        // The player put something in this kitchen's queue, within the grace.
        var since = step.At - InnCallHold.GraceSeconds - TimeSpan.FromSeconds(0.5);
        for (int j = i; j > 0 && Steps[j].At >= since; j--)
        {
            var before = QueueOf(Steps[j - 1].Queues, kitchen);
            var after = QueueOf(Steps[j].Queues, kitchen);
            if (after.Count > before.Count || after.Except(before).Any()) return true;
        }
        return false;
    }

    private static List<string> QueueOf(string queues, int kitchen) =>
        queues.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(q => q.StartsWith($"{kitchen}:", StringComparison.Ordinal))
            .SelectMany(q => q[(q.IndexOf(':') + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries)).ToList();

    /// <summary>Per minute of the session: the kitchens' call changes, flashes and jumps.</summary>
    public (double Changes, double Flashes, double Jumps) CallRates()
    {
        double minutes = Math.Max(SessionSpan.TotalMinutes, 1e-9);
        var (flashes, jumps) = Steadiness();
        return (Changes.Count(c => c.Display == "call") / minutes, flashes.Count / minutes, jumps.Count / minutes);
    }

    // ---- Report ----

    /// <summary>Changes per minute of the session, by display: "banner" → 12.3.</summary>
    public IReadOnlyDictionary<string, double> PerMinute()
    {
        double minutes = Math.Max(SessionSpan.TotalMinutes, 1e-9);
        return new[] { "banner", "mark", "gourmand", "call" }.ToDictionary(d => d, d => Changes.Count(c => c.Display == d) / minutes);
    }

    public string Report()
    {
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"# session span {SessionSpan.TotalSeconds:F1} s");
        foreach (var (display, rate) in PerMinute())
            sb.AppendLine(CultureInfo.InvariantCulture, $"# {display}: {Changes.Count(c => c.Display == display)} changes, {rate:F1}/min");
        var (flashes, jumps) = Steadiness();
        var (changes, flashRate, jumpRate) = CallRates();
        sb.AppendLine(CultureInfo.InvariantCulture, $"# calls: {Changes.Count(c => c.Display == "call")} changes ({changes:F1}/min), {flashes.Count} flashes ({flashRate:F2}/min), {jumps.Count} jumps ({jumpRate:F2}/min)");
        foreach (var f in flashes) sb.AppendLine(CultureInfo.InvariantCulture, $"# flash {f.At.TotalSeconds,7:F1} kitchen {f.Kitchen}: {f.What}  <= {f.Why}");
        foreach (var j in jumps) sb.AppendLine(CultureInfo.InvariantCulture, $"# jump  {j.At.TotalSeconds,7:F1} kitchen {j.Kitchen}: {j.What}  <= {j.Why}");
        foreach (var c in Changes)
            sb.AppendLine(CultureInfo.InvariantCulture, $"{c.At.TotalSeconds,7:F1} {c.Display,-6} {c.Change} {c.What}  <= {c.Why}");
        return sb.ToString();
    }

    // ---- Readings saved as text ----

    private static readonly JsonSerializerOptions Json = new()
    {
        IncludeFields = true, // the menu's counters are tuples
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Writes the events as gzipped JSON lines (text: the readings, never a picture).</summary>
    public static void Save(string path, IEnumerable<InnReplayEvent> events)
    {
        using var file = File.Create(path);
        using var zip = new GZipStream(file, CompressionLevel.SmallestSize);
        using var writer = new StreamWriter(zip, new UTF8Encoding(false));
        foreach (var e in events) writer.WriteLine(JsonSerializer.Serialize(e, Json));
    }

    public static IReadOnlyList<InnReplayEvent> Load(string path)
    {
        using var file = File.OpenRead(path);
        using var zip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(zip, Encoding.UTF8);
        var events = new List<InnReplayEvent>();
        while (reader.ReadLine() is { Length: > 0 } line) events.Add(JsonSerializer.Deserialize<InnReplayEvent>(line, Json)!);
        return events;
    }
}
