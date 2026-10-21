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
    private string? _panel;
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
        string why = _session.CallChanges.Count > 0 ? $"{input}; calls: {string.Join("; ", _session.CallChanges)}" : input;
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

    // ---- Report ----

    /// <summary>Changes per minute of the session, by display: "banner" → 12.3.</summary>
    public IReadOnlyDictionary<string, double> PerMinute()
    {
        double minutes = Math.Max(SessionSpan.TotalMinutes, 1e-9);
        return new[] { "banner", "mark", "gourmand" }.ToDictionary(d => d, d => Changes.Count(c => c.Display == d) / minutes);
    }

    public string Report()
    {
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"# session span {SessionSpan.TotalSeconds:F1} s");
        foreach (var (display, rate) in PerMinute())
            sb.AppendLine(CultureInfo.InvariantCulture, $"# {display}: {Changes.Count(c => c.Display == display)} changes, {rate:F1}/min");
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
