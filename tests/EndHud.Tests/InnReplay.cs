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
/// <param name="Display">"banner", "mark", "gourmand" (a gourmand's banner), "call" (a kitchen's call, shown or hidden), "move" (a
/// banner standing in another row) or "away" (the stacks hidden in place over the open dispatch panel).</param>
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

    private readonly InnSession _session;
    private TimeSpan _now; // the replay's clock, for the session's log lines

    /// <summary>The session's "inn: corrected …" log lines, each with when (the estimate put right by a reading).</summary>
    public List<string> CorrectionLog { get; } = [];

    private InnReplay() =>
        _session = new InnSession(Inn, Library.Operators, ReadAllAlong,
            log: m => { if (m.StartsWith("inn: corrected", StringComparison.Ordinal)) CorrectionLog.Add($"{_now.TotalSeconds,7:F1} {m}"); });
    private readonly InnDisplay _display = new();
    private HashSet<string> _banners = [], _marks = [], _gourmands = [];
    private Dictionary<string, string> _gourmandContent = [];
    private bool _panelShown, _away;
    private readonly InnBannerRows _rows = new();
    private Dictionary<string, int> _bannerRows = [];
    private InnReading? _last;
    private TimeSpan? _sessionFrom, _sessionTo;

    public List<InnViewChange> Changes { get; } = [];

    /// <summary>The span the session's overlays could show (first to last session reading), for the rate per minute.</summary>
    public TimeSpan SessionSpan => _sessionFrom is { } from && _sessionTo is { } to ? to - from : TimeSpan.Zero;

    /// <param name="suggestSwaps">The swaps setting (HudConfig.InnSwapsEnabled), as the overlay hands it to the session.</param>
    public static InnReplay Run(IEnumerable<InnReplayEvent> events, bool suggestSwaps = true)
    {
        var replay = new InnReplay();
        replay._session.SuggestSwaps = suggestSwaps;
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
        _now = e.At;
        string why = Describe(_last, e.Reading);
        _display.Show(_session, e.Reading, e.At);
        if (e.Reading is { Fresh: true, InSession: true } fresh)
        {
            for (int i = 0; i < Math.Min(fresh.TableCards.Count, _cards.Length); i++)
                if (fresh.TableCards[i] is { } card)
                {
                    _cards[i] = card;
                    // A gourmand's requests read resolved on their pips, since their order (a fade's empty cards aside).
                    if (card.State == TableState.GourmandOrder)
                        foreach (int k in Enumerable.Range(0, card.Served.Count).Where(k => card.Served[k])) _pipsResolved[i].Add(k);
                    else if (card.State != TableState.Empty) _pipsResolved[i].Clear();
                }
            if (fresh.Tray is { } tray && tray.All(s => s is null or { Id: not null })) _tray = [.. tray.OfType<HeldDish>().Select(h => h.Id!)];
        }
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
        _now = at;
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
        // The banners as the overlay draws them: every call in its row (InnBannerRows), the stack hidden in place while the
        // dispatch panel is open (no banner goes or comes for that); a banner standing in another row than before moved.
        if (!views.Session) _rows.Clear();
        var rows = views.Session ? _rows.Rows(views.Kitchens, views.Calls, Text) : [];
        var placed = rows.Select((r, i) => (r.Banner, Row: i)).Where(r => r.Banner is not null).ToDictionary(r => $"{r.Banner!.Key} ({r.Banner.Title})", r => r.Row);
        var banners = placed.Keys.ToHashSet();
        Diff("banner", _banners, banners, at, why);
        foreach (var (banner, row) in placed)
            if (_bannerRows.TryGetValue(banner, out int was) && was != row) Changes.Add(new InnViewChange(at, "move", "~", $"{banner} row {was} -> {row}", why));
        _bannerRows = placed;
        _banners = banners;
        if (views.Away != _away) Changes.Add(new InnViewChange(at, "away", views.Away ? "-" : "+", "the banner stacks, hidden in place", why));
        _away = views.Away;
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
        TakeDraw(at, views);
        TakeGourmandsShown(at, views);
    }

    // ---- What each gourmand's tile says, against what the gourmands asked (the recordings' ground truth) ----

    /// <summary>At one draw, the requests a gourmand's tile shows (each by the gourmand data's own request), table by table.</summary>
    internal sealed record GourmandsShown(TimeSpan At, IReadOnlyDictionary<int, IReadOnlySet<GourmandRequest>> Tables);

    public List<GourmandsShown> GourmandDraws { get; } = [];

    private void TakeGourmandsShown(TimeSpan at, InnViews views)
    {
        if (!views.Session) return;
        var notes = _display.LastAdvice?.Gourmands ?? [];
        var tables = new Dictionary<int, IReadOnlySet<GourmandRequest>>();
        foreach (var group in views.Wants.Where(w => w.Description is not null).GroupBy(w => w.Table))
        {
            var advice = notes.FirstOrDefault(n => n.Table == group.Key)?.Advice;
            if (advice is null) continue;
            tables[group.Key] = group.Where(w => w.Request < advice.Requests.Count).Select(w => advice.Requests[w.Request].Request).ToHashSet();
        }
        GourmandDraws.Add(new GourmandsShown(at, tables));
    }

    /// <summary>One request a gourmand made, as read off the recording by eye: their table, when the bubble first said it, and when its
    /// icon turned into a face (served right, imperfect, or failed).</summary>
    internal sealed record TruthRequest(string Label, int Table, double From, double Resolved, GourmandRequest Request);

    /// <summary>How one true request fared on the tiles.</summary>
    /// <param name="ShownAfter">Seconds from the bubble to the request first on its own table's tile; null: never shown there before it was resolved.</param>
    /// <param name="MissingAfterShown">Seconds, from first shown to resolved, its table's tile didn't show it.</param>
    /// <param name="ElsewhereSeconds">Seconds, between the bubble and resolved, another table's tile showed it (to a table with no such gourmand then).</param>
    /// <param name="OthersSeconds">Seconds, between the bubble and resolved, its table's tile showed a request this gourmand didn't make.</param>
    internal sealed record TruthOutcome(TruthRequest Truth, double? ShownAfter, double MissingAfterShown, double ElsewhereSeconds, double OthersSeconds);

    /// <summary>Each true request against the tiles drawn (<see cref="GourmandDraws"/>), draw by draw, each draw standing until the next.</summary>
    public List<TruthOutcome> AgainstTruth(IReadOnlyList<TruthRequest> truth)
    {
        var outcomes = new List<TruthOutcome>();
        foreach (var t in truth)
        {
            var mine = truth.Where(o => o.Table == t.Table && o.From < t.Resolved && o.Resolved > t.From).Select(o => o.Request).ToHashSet();
            double? first = null;
            double missing = 0, elsewhere = 0, others = 0;
            for (int i = 0; i < GourmandDraws.Count; i++)
            {
                var d = GourmandDraws[i];
                double at = d.At.TotalSeconds;
                double until = i + 1 < GourmandDraws.Count ? GourmandDraws[i + 1].At.TotalSeconds : at;
                if (until <= t.From || at >= t.Resolved) continue;
                double span = Math.Min(until, t.Resolved) - Math.Max(at, t.From);
                bool shown = d.Tables.TryGetValue(t.Table, out var here) && here.Contains(t.Request);
                if (shown) first ??= Math.Max(at, t.From) - t.From;
                else if (first is not null) missing += span;
                foreach (var (table, requests) in d.Tables)
                    if (table != t.Table && requests.Contains(t.Request) && !truth.Any(o => o.Table == table && o.Request == t.Request && o.From <= at && o.Resolved > at))
                        elsewhere += span;
                if (here is not null && here.Any(r => !mine.Contains(r))) others += span;
            }
            outcomes.Add(new TruthOutcome(t, first, missing, elsewhere, others));
        }
        return outcomes;
    }

    // ---- One picture: the circles, the panel's marks and the gourmands' banners drawn together ----

    /// <summary>The latest fresh reading of each table's card (null: never read), for the gourmands' lifetimes.</summary>
    private readonly TableCard?[] _cards = new TableCard?[TableCardReader.Tables];

    /// <summary>The dishes the tray last read clearly held (every slot told apart): what the player really carries.</summary>
    private List<string> _tray = [];

    /// <summary>
    /// What the player saw at one draw: the panel's kitchen (null: no marks, the panel not on screen), each kitchen's call
    /// (whatever shows it), the tabs marked "a dish to queue", whether a card was marked, where the circles were drawn,
    /// the gourmands with a banner, whether those banners were hidden over the panel, and each table's last card.
    /// </summary>
    internal sealed record DrawStep(TimeSpan At, int? PanelKitchen, IReadOnlyDictionary<int, InnCall> Calls, IReadOnlySet<int> Tabs, bool CardMark,
        bool CircleDrawn, IReadOnlySet<int> Gourmands, bool GourmandsHidden, IReadOnlyList<TableState?> Tables)
    {
        /// <summary>Each table's gourmand order read with every request resolved on its pips (served, imperfect or failed).</summary>
        public IReadOnlyList<bool> Resolved { get; init; } = [];
    }

    /// <summary>Each table's gourmand pips read resolved since their order.</summary>
    private readonly HashSet<int>[] _pipsResolved = [.. Enumerable.Range(0, TableCardReader.Tables).Select(_ => new HashSet<int>())];

    public List<DrawStep> Draws { get; } = [];

    /// <summary>The kitchens' circles are on screen: above the tray, or below the open panel (until 2026-10-21 hidden while it was open).</summary>
    private static bool CirclesDrawn(InnViews views) => views.Circles is InnCirclePlace.AboveTray or InnCirclePlace.BelowPanel;

    private void TakeDraw(TimeSpan at, InnViews views)
    {
        if (!views.Session) return;
        var calls = views.Calls.Where(c => c.ForKitchen && c.Dish is not null).GroupBy(c => c.Kitchen!.Value).ToDictionary(g => g.Key, g => g.First());
        var tabs = views.Marks.Where(m => !m.OnCard)
            .Select(m => Enumerable.Range(1, KitchenMath.MaxKitchens).FirstOrDefault(k => DispatchPanelReader.TabArea(k) == m.Area)).Where(k => k > 0).ToHashSet();
        Draws.Add(new DrawStep(at, _display.LastAdvice?.MarkedPanel?.Kitchen, calls, tabs, views.Marks.Any(m => m.OnCard), CirclesDrawn(views),
            views.Wants.Select(w => w.Table).ToHashSet(), false /* never hidden: each tile at its table's fixed spot, the panel open or not (InnGourmandLayout) */, [.. _cards.Select(c => c?.State)]))
        {
            Resolved = [.. Enumerable.Range(0, TableCardReader.Tables).Select(i => _cards[i] is { State: TableState.GourmandOrder } c && c.Served.Count > 0 && _pipsResolved[i].Count >= c.Served.Count)],
        };
    }

    /// <summary>
    /// The picture's parts agreeing (owner, 2026-10-21, test_yuanyuan6: "there are MANY cases where the kitchen shows 有菜要排,
    /// BUT there is no circle under ... also there are many cases where the kitchen shows 有菜要排, but when we go to the
    /// kitchen, there is no recommendation"), counted as episodes (a run of draws in a row) and seconds:
    /// <list type="bullet">
    /// <item><b>TabWithoutCircle:</b> a tab marked while that kitchen's circle isn't drawn;</item>
    /// <item><b>CallWithoutCard:</b> the panel on screen showing a kitchen with a call, and no card marked;</item>
    /// <item><b>SwitchNoCard:</b> the player switched to a tab marked on the draw before, and no card was marked in the next second;</item>
    /// <item><b>GourmandHidden:</b> a gourmand's banner up but hidden (the panel open over it);</item>
    /// <item><b>CookAheadHidden:</b> a dish to cook ahead called while the circles aren't drawn (the cyan circle gone and back
    /// with the panel: "the blue circle just shows very indeterministically").</item>
    /// </list>
    /// </summary>
    public (Episodes TabWithoutCircle, Episodes CallWithoutCard, List<TimeSpan> SwitchNoCard, Episodes GourmandHidden, Episodes CookAheadHidden) Agreement()
    {
        var tabNoCircle = new Episodes();
        var callNoCard = new Episodes();
        var hidden = new Episodes();
        var switchNoCard = new List<TimeSpan>();
        var aheadHidden = new Episodes();
        for (int i = 0; i < Draws.Count; i++)
        {
            var d = Draws[i];
            var before = i > 0 ? Draws[i - 1] : null;
            double step = before is null ? 0 : (d.At - before.At).TotalSeconds;
            tabNoCircle.Take(d.At, step, d.Tabs.Any(k => !d.CircleDrawn || !d.Calls.ContainsKey(k)));
            callNoCard.Take(d.At, step, d.PanelKitchen is { } k2 && d.Calls.ContainsKey(k2) && !d.CardMark);
            hidden.Take(d.At, step, d.GourmandsHidden && d.Gourmands.Count > 0);
            aheadHidden.Take(d.At, step, !d.CircleDrawn && d.Calls.Values.Any(c => c.Kind == InnCallKind.CookAhead));
            // A call done on that very frame (its dish read queued as the tab opened) has nothing left to mark; a call that
            // went for anything else (the panel's queue read otherwise than the estimate had it) left a tab promising a mark.
            if (before is { PanelKitchen: { } was } && d.PanelKitchen is { } now && now != was && before.Tabs.Contains(now)
                && (d.Calls.ContainsKey(now) || !DoneAt(d.At, before.Calls.GetValueOrDefault(now))))
            {
                bool marked = false;
                for (int j = i; j < Draws.Count && Draws[j].At - d.At <= TimeSpan.FromSeconds(1) && Draws[j].PanelKitchen == now; j++)
                    marked |= Draws[j].CardMark;
                if (!marked) switchNoCard.Add(d.At);
            }
        }
        return (tabNoCircle, callNoCard, switchNoCard, hidden, aheadHidden);
    }

    /// <summary>The call went as done (its dish queued) at <paramref name="at"/>.</summary>
    private bool DoneAt(TimeSpan at, InnCall? call) =>
        call is not null && Steps.Any(s => s.At == at && s.Why.Contains($"{call.Key} dropped: done", StringComparison.Ordinal));

    /// <summary>Runs of a condition over the draws: how many began, their seconds in all, and when each began.</summary>
    internal sealed class Episodes
    {
        private bool _on;

        public int Count => Starts.Count;

        public double Seconds { get; private set; }

        public List<TimeSpan> Starts { get; } = [];

        public void Take(TimeSpan at, double step, bool on)
        {
            if (on && _on) Seconds += step;
            if (on && !_on) Starts.Add(at);
            _on = on;
        }

        public override string ToString() => $"{Count} ({Seconds:F1} s){(Count > 0 ? ": " + string.Join(", ", Starts.Take(12).Select(s => s.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture))) : "")}";
    }

    /// <summary>
    /// The dishes to cook ahead as called (owner, 2026-10-21, test_yuanyuan6: "the blue circle just shows very
    /// indeterministically"): each cook-ahead call shown in a kitchen, how long it stood, and how it ended. A flicker is
    /// one that went with nothing done to it: not queued (dropped: done), not displaced by an order in its kitchen, the
    /// kitchen not full, cooking ahead not over (the end of serving).
    /// </summary>
    public (int Shown, int Flickers, List<(TimeSpan At, int Kitchen, string Dish, double Lasted, string Why)> List) CookAheadFlicker()
    {
        var list = new List<(TimeSpan, int, string, double, string)>();
        int shown = 0;
        var since = new Dictionary<int, (InnCall Call, TimeSpan At)>();
        for (int i = 0; i < Steps.Count; i++)
        {
            var step = Steps[i];
            foreach (var (k, held) in since.ToList())
            {
                var now = step.Kitchens.GetValueOrDefault(k);
                if (now?.Key == held.Call.Key) continue;
                since.Remove(k);
                bool done = step.Why.Contains($"{held.Call.Key} dropped: done", StringComparison.Ordinal);
                bool order = now is { Kind: InnCallKind.Queue or InnCallKind.Swap };
                bool full = step.Why.Contains($"{held.Call.Key} dropped: the kitchen", StringComparison.Ordinal);
                bool gaveWay = GaveWayToSameDish(step, held.Call);
                bool over = !step.CooksAhead || step.AheadTooLate.Contains(k);
                if (!done && !order && !full && !gaveWay && !over)
                    list.Add((step.At, k, held.Call.Dish!.Id, (step.At - held.At).TotalSeconds, now is null ? "gone" : $"-> {now.Key}"));
            }
            foreach (var (k, call) in step.Kitchens)
                if (call.Kind == InnCallKind.CookAhead && !since.ContainsKey(k))
                {
                    since[k] = (call, step.At);
                    shown++;
                }
        }
        return (shown, list.Count, list);
    }

    /// <summary>
    /// Each gourmand's banner (owner, 2026-10-21: "gourmand's requirement window still flashes always ... hasnt been
    /// fulfilled but the requirement is gone"): how many times one went up and came down, how long each stood, and those
    /// taken down while that table's card still read a gourmand waiting (arrived, ordering or ordered: not served).
    /// </summary>
    public (int Up, int Down, int DownUnserved, List<string> Lives) GourmandLives()
    {
        int up = 0, down = 0, unserved = 0;
        var lives = new List<string>();
        var since = new Dictionary<int, TimeSpan>();
        for (int i = 0; i < Draws.Count; i++)
        {
            var d = Draws[i];
            foreach (int table in d.Gourmands.Where(t => !since.ContainsKey(t)))
            {
                since[table] = d.At;
                up++;
            }
            // The session's last draws: it ended (the results screen), not the gourmand.
            if (Draws[^1].At - d.At < TimeSpan.FromSeconds(1)) continue;
            foreach (var (table, from) in since.Where(s => !d.Gourmands.Contains(s.Key)).ToList())
            {
                since.Remove(table);
                down++;
                var state = table - 1 < d.Tables.Count ? d.Tables[table - 1] : null;
                // Waiting: their order still open, not every request read resolved on the pips (then nothing is left to serve).
                bool waiting = state is TableState.GourmandArrived or TableState.GourmandOrdering
                               || (state is TableState.GourmandOrder && !(table - 1 < d.Resolved.Count && d.Resolved[table - 1]));
                if (waiting) unserved++;
                lives.Add($"table {table} {from.TotalSeconds:F1}-{d.At.TotalSeconds:F1} s ({state}){(waiting ? " UNSERVED" : "")}");
            }
        }
        foreach (var (table, from) in since) lives.Add($"table {table} {from.TotalSeconds:F1}- (to the end)");
        return (up, down, unserved, lives);
    }

    /// <summary>
    /// One dish in two circles at once (owner, 2026-10-21, test_yuanyuan7 at 00:46: eight-treasure-rice called for table 3's order in kitchen 1
    /// and to cook ahead in kitchen 3 at the same moment, "the recommended queue will be duplicated in multiple kitchens"). A
    /// dish may show in two circles only when each is an order (or a swap) naming its own table: two tables genuinely waiting
    /// for it. Anything else (an order and a dish to cook ahead, two dishes to cook ahead, one table twice) is a duplicate.
    /// Counted as episodes (a dish duplicated over steps in a row is one) with their seconds.
    /// </summary>
    public (Episodes Episodes, List<string> List) DuplicateCircles()
    {
        var episodes = new Episodes();
        var list = new List<string>();
        string? before = null;
        for (int i = 0; i < Steps.Count; i++)
        {
            var step = Steps[i];
            double gap = i > 0 ? (step.At - Steps[i - 1].At).TotalSeconds : 0;
            var dupes = step.Kitchens.Values.Where(c => c.Dish is not null).GroupBy(c => c.Dish!.Id)
                .Where(g => g.Count() > 1 && !(g.All(c => c.Kind is InnCallKind.Queue or InnCallKind.Swap) && g.Select(c => c.Table).Distinct().Count() == g.Count()))
                .Select(g => string.Join(" + ", g.OrderBy(c => c.Kitchen).Select(c => c.Key)))
                .ToList();
            string? now = dupes.Count > 0 ? string.Join("; ", dupes) : null;
            episodes.Take(step.At, gap, now is not null);
            if (now is not null && now != before) list.Add($"{step.At.TotalSeconds,7:F1} {now}");
            before = now;
        }
        return (episodes, list);
    }

    /// <summary>The picture's numbers in one block (the replay test's output, the trace's head).</summary>
    public string Consistency()
    {
        var (tabNoCircle, callNoCard, switchNoCard, hidden, aheadHidden) = Agreement();
        var (shown, flickers, list) = CookAheadFlicker();
        var (up, down, unserved, lives) = GourmandLives();
        var (dupes, dupeList) = DuplicateCircles();
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"# one dish in two circles (not two tables' orders): {dupes}");
        foreach (var d in dupeList) sb.AppendLine(CultureInfo.InvariantCulture, $"#   duplicate {d}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"# corrections (the estimate put right by a reading): {string.Join(", ", new[] { "queue", "cover", "counter", "gourmand", "timer" }.Select(k => $"{k} {_session.Corrections.GetValueOrDefault(k)}"))}");
        foreach (string line in CorrectionLog) sb.AppendLine(CultureInfo.InvariantCulture, $"#   {line}");
        var uncalled = UncalledWaits(UncalledAfterSeconds);
        var counted = uncalled.Where(u => u.Counts).ToList();
        sb.AppendLine(CultureInfo.InvariantCulture, $"# a dish tables wait for, not in hand nor queued, no circle naming it for over {UncalledAfterSeconds:F0} s: {counted.Count} ({counted.Sum(u => u.Seconds):F1} s); ended by the dish picked up or served (it was on the counter): {uncalled.Count - counted.Count}");
        foreach (var u in uncalled) sb.AppendLine(CultureInfo.InvariantCulture, $"#   {(u.Counts ? "uncalled" : "covered ")} {u.From.TotalSeconds,7:F1} {u.Dish} for {u.Seconds:F1} s, ended: {u.Ended}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"# tab marked, no circle drawn for it: {tabNoCircle}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"# panel on a kitchen with a call, no card marked: {callNoCard}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"# switched to a marked tab, no card marked within 1 s: {switchNoCard.Count}{(switchNoCard.Count > 0 ? ": " + string.Join(", ", switchNoCard.Select(t => t.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture))) : "")}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"# a dish to cook ahead called with the circles not drawn: {aheadHidden}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"# cook-ahead calls shown: {shown}, flickers (gone with nothing done): {flickers}");
        foreach (var f in list) sb.AppendLine(CultureInfo.InvariantCulture, $"#   flicker {f.At.TotalSeconds,7:F1} kitchen {f.Kitchen} {f.Dish} after {f.Lasted:F1} s {f.Why}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"# gourmand banners up {up}, down {down}, down while the table still waited {unserved}; hidden over the panel: {hidden}");
        foreach (var l in lives) sb.AppendLine(CultureInfo.InvariantCulture, $"#   gourmand {l}");
        return sb.ToString();
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
    /// <param name="State">The queues, open orders, counter and advice, in words (the trace's state lines).</param>
    /// <param name="Sendable">The orders the advice sends to a kitchen now (not waiting for room nor out of stock).</param>
    /// <param name="FreeFor">
    /// Of <paramref name="Sendable"/>, those a kitchen not showing another order could take now: it has a free slot and has
    /// the dish out no more than <see cref="InnCallHold.ShowElsewhereWithinSeconds"/> after the kitchen the advice sends it to
    /// (the hold's rule for a kitchen with no order to show).
    /// </param>
    internal sealed record CallStep(TimeSpan At, IReadOnlyDictionary<int, InnCall> Kitchens, IReadOnlySet<(string Dish, int Table)> Open, string Queues,
        bool CooksAhead, string Why, string State = "")
    {
        public IReadOnlySet<(string Dish, int Table)> Sendable { get; init; } = new HashSet<(string, int)>();

        public IReadOnlySet<(string Dish, int Table)> FreeFor { get; init; } = new HashSet<(string, int)>();

        /// <summary>Dishes tables wait for that neither the hand nor a queue holds, a kitchen could take (<see cref="InnReplay.Unattended"/>).</summary>
        public IReadOnlyDictionary<string, int> Unattended { get; init; } = new Dictionary<string, int>();

        /// <summary>Per dish tables wait for: unserved on the cards, in hand, in the queues (<see cref="InnReplay.Supply"/>).</summary>
        public IReadOnlyDictionary<string, (int Waiting, int Hand, int Queued)> Supply { get; init; } = new Dictionary<string, (int, int, int)>();

        /// <summary>
        /// Kitchens whose dish to cook ahead, as called on the step before, could no longer come out there before serving ends
        /// (<see cref="DispatchAdvisor.NoCookAheadSeconds"/>, or its queue + its cook time past the serving left): the end
        /// rule took it, nothing else.
        /// </summary>
        public IReadOnlySet<int> AheadTooLate { get; init; } = new HashSet<int>();
    }

    /// <summary>The kitchens of <paramref name="calls"/> whose dish to cook ahead can no longer come out in time at <paramref name="at"/> (<see cref="CallStep.AheadTooLate"/>).</summary>
    private HashSet<int> AheadTooLate(IReadOnlyDictionary<int, InnCall> calls, TimeSpan at)
    {
        var late = new HashSet<int>();
        if (_session.ServingLeft(at) is not { } serving) return late;
        foreach (var (k, call) in calls)
            if (call is { Kind: InnCallKind.CookAhead, Dish: { } dish }
                && (serving < DispatchAdvisor.NoCookAheadSeconds || _session.ReadyIn(k, dish.Id) is { } readyIn && readyIn > serving))
                late.Add(k);
        return late;
    }

    /// <summary>A call that flashed (gone and back) or jumped (switched dish or kitchen with nothing done to cause it).</summary>
    internal sealed record Unsteady(TimeSpan At, int Kitchen, string What, string Why);

    public List<CallStep> Steps { get; } = [];

    private void TakeCalls(TimeSpan at, InnAdvice? advice, string why)
    {
        var kitchens = advice?.Calls.Where(c => c.Kind is InnCallKind.Queue or InnCallKind.CookAhead or InnCallKind.Swap && c.Kitchen is not null)
            .ToDictionary(c => c.Kitchen!.Value) ?? [];
        var open = new HashSet<(string, int)>();
        var sendable = new HashSet<(string, int)>();
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
                sendable.UnionWith(open);
            }
            foreach (var o in dispatch.Waiting.Concat(dispatch.OutOfStock)) open.Add((o.Dish.Id, o.Table));
        }
        string queues = advice is null ? "" : _session.QueueSignature();
        var before = Steps.Count > 0 ? Steps[^1].Kitchens : new Dictionary<int, InnCall>();
        string state = $"{why} [queues {queues}; open {string.Join(",", open.Select(o => $"{o.Item1}@{o.Item2}"))}; counter {(advice is null ? "" : _session.CounterSignature())}; covering {(advice is null ? "" : _session.CoverSignature())}; advised {Advised(advice)}]";
        foreach (int k in before.Keys.Union(kitchens.Keys).Order())
        {
            string? was = before.GetValueOrDefault(k)?.Key, now = kitchens.GetValueOrDefault(k)?.Key;
            if (was == now) continue;
            if (was is not null) Changes.Add(new InnViewChange(at, "call", "-", was, state));
            if (now is not null) Changes.Add(new InnViewChange(at, "call", "+", now, state));
        }
        var freeFor = new HashSet<(string, int)>();
        foreach (var q in advice?.Dispatch?.Kitchens.SelectMany(k => k.Queue) ?? [])
        {
            if (q.Table is not { } table) continue;
            if (advice!.Dispatch!.Kitchens.Any(k => kitchens.GetValueOrDefault(k.Kitchen) is not { Kind: InnCallKind.Queue or InnCallKind.Swap }
                                                    && _session.ReadyIn(k.Kitchen, q.Dish.Id) is { } readyIn && readyIn <= q.ReadyIn + InnCallHold.ShowElsewhereWithinSeconds))
                freeFor.Add((q.Dish.Id, table));
        }
        Steps.Add(new CallStep(at, kitchens, open, queues, ahead, why, state[(state.LastIndexOf('[') + 1)..^1]) { Sendable = sendable, FreeFor = freeFor, Unattended = advice is null ? [] : Unattended(queues, at), Supply = advice is null ? [] : Supply(queues), AheadTooLate = AheadTooLate(before, at) });
    }

    /// <summary>
    /// The dishes tables are waiting for that nothing real is bringing: each dish's unserved items on the order cards as last
    /// read (a guest's order; a gourmand's card shows no pips), less those in the player's hand as the tray last read them and
    /// those in the kitchens' queues (the estimate, put right by every panel reading), when a kitchen has a free slot for it
    /// and it is in stock. The counter is NOT counted: it is never read off the screen, and a dish taken to be there that
    /// isn't is how an order goes uncalled (test_yuanyuan4 from 250 s; test_yuanyuan7's soups, 01:14–00:20).
    /// </summary>
    private Dictionary<string, int> Unattended(string queues, TimeSpan at)
    {
        double? serving = _session.ServingLeft(at);
        var result = new Dictionary<string, int>();
        foreach (var (id, supply) in Supply(queues))
        {
            int left = supply.Waiting - supply.Hand - supply.Queued;
            if (left > 0 && _session.InStock(id) && Enumerable.Range(1, KitchenMath.MaxKitchens).Any(k => _session.ReadyIn(k, id) is { } readyIn && !(readyIn > serving)))
                result[id] = left;
        }
        return result;
    }

    /// <summary>Per dish tables wait for: its unserved items on the order cards as last read, in hand as the tray last read, in the queues.</summary>
    private Dictionary<string, (int Waiting, int Hand, int Queued)> Supply(string queues)
    {
        var queued = queues.Split(' ', StringSplitOptions.RemoveEmptyEntries).SelectMany(q => q[(q.IndexOf(':') + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries)).ToList();
        return _cards.OfType<TableCard>().Where(c => c.State == TableState.Order)
            .SelectMany(c => c.Items).Where(i => !i.Served && i.Id is not null && Inn.Dishes.Any(d => d.Id == i.Id))
            .GroupBy(i => i.Id!)
            .ToDictionary(g => g.Key, g => (g.Count(), _tray.Count(t => t == g.Key), queued.Count(q => q == g.Key)));
    }

    /// <summary>How long a dish may wait uncalled (<see cref="UncalledWaits"/>) before the replay counts it.</summary>
    public const double UncalledAfterSeconds = 10;

    /// <summary>
    /// Orders left waiting with no circle (owner, 2026-10-21, test_yuanyuan7: "we ended up stuck behind a lot of soups but the
    /// kitchen recommendation is not even picking that up"): a dish <see cref="Unattended"/> (tables wait for it, it isn't in
    /// hand nor in a queue, a kitchen could have it out before serving ends) while no kitchen's circle names it, for more than
    /// <paramref name="seconds"/> in a row. Each run is listed once, from when it began, with how long it lasted and how it
    /// ended. A run the player ended by picking the dish up or serving it was covered by a dish really on the counter (the
    /// advice's out-for-an-order, which needs no circle): listed, not counted. Every other end counts: a call naming it at
    /// last, the player queueing it on their own, the time running out, or the end of the replay.
    /// </summary>
    public List<(TimeSpan From, string Dish, double Seconds, string Ended, bool Counts)> UncalledWaits(double seconds)
    {
        var list = new List<(TimeSpan, string, double, string, bool)>();
        var since = new Dictionary<string, TimeSpan>();
        CallStep? prior = null;
        foreach (var step in Steps.Append(null))
        {
            var now = step is null ? [] : step.Unattended.Keys.Where(id => !step.Kitchens.Values.Any(c => c.Dish?.Id == id)).ToHashSet();
            foreach (var (id, from) in since.ToList())
            {
                if (now.Contains(id)) continue;
                since.Remove(id);
                double lasted = ((step?.At ?? Steps[^1].At) - from).TotalSeconds;
                if (lasted <= seconds) continue;
                var (was, then) = (prior!.Supply.GetValueOrDefault(id), step?.Supply.GetValueOrDefault(id) ?? default);
                string ended = step is null ? "the replay's end"
                    : step.Kitchens.Values.Any(c => c.Dish?.Id == id) ? "a call named it"
                    : then.Hand > was.Hand ? "picked up"
                    : then.Waiting < was.Waiting ? "served"
                    : then.Queued > was.Queued ? "queued by the player with no call"
                    : "no longer possible in time";
                list.Add((from, id, lasted, ended, ended is not ("picked up" or "served")));
            }
            if (step is null) break;
            foreach (string id in now) since.TryAdd(id, step.At);
            prior = step;
        }
        return [.. list.OrderBy(l => l.Item1)];
    }

    /// <summary>This moment's advice per kitchen, before the hold: "1:dish@table(ready in),… 2:…".</summary>
    private static string Advised(InnAdvice? advice) => advice?.Dispatch is not { } d ? "" : string.Join(" ", d.Kitchens.Select(k =>
        $"{k.Kitchen}:{string.Join(",", k.Replaces.Select(s => $"swap {s.Remove.Id}->{s.Put.Id}@{s.Table}").Concat(k.Queue.Select(q => $"{q.Dish.Id}@{q.Table?.ToString(CultureInfo.InvariantCulture) ?? "ahead"}({q.ReadyIn:F0})")))}")
        .Concat(d.Leftovers.Count > 0 ? [$"last call {string.Join(",", d.Leftovers.Select(l => $"{l.Dish.Id}@{l.Table}"))}"] : []));

    /// <summary>
    /// The kitchens' calls that flashed or jumped (owner, 2026-10-21: "the recommended dish flashes heavily and it jumps
    /// around ... the recommendation should stay steady"), over the whole replay:
    /// <list type="bullet">
    /// <item><b>flash:</b> a kitchen's call gone and back within <see cref="UnsteadyWithinSeconds"/>, nothing done to it
    /// meanwhile (queued and then advised again is the next dish, not a flash);</item>
    /// <item><b>jump:</b> a kitchen's call switching to another dish (directly or after a gap that short), or the same
    /// order (or dish cooked ahead) switching kitchen, when the call that went was still possible and nothing was done to
    /// it: not queued, its order not served or gone, its kitchen not full, its kitchen's queue not added to by the player,
    /// and (for a dish cooked ahead) cooking ahead not stopped altogether. A dish cooked ahead giving way to an order that
    /// no other kitchen was showing, or one that left its kitchen for something done, is the order's banner coming up, not
    /// a jump.</item>
    /// </list>
    /// </summary>
    public (List<Unsteady> Flashes, List<Unsteady> Jumps) Steadiness()
    {
        var flashes = new List<Unsteady>();
        var jumps = new List<Unsteady>();
        var window = TimeSpan.FromSeconds(UnsteadyWithinSeconds);
        // Every call that went: its kitchen, when, and whether something done or impossible took it.
        var gone = new List<(int Kitchen, InnCall Call, TimeSpan At, bool Justified, bool Done)>();
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
                if (was is not null) gone.Add((k, was, step.At, Justified(was, k, i), step.Why.Contains($"{was.Key} dropped: done", StringComparison.Ordinal)));
                if (now is not null) cameNow.Add((k, now));
            }
            foreach (var (k, now) in cameNow)
            {
                // Back in its kitchen after going a moment ago, unless the player queued it (then advised again: the
                // next of the same dish, new advice).
                // Nor when the player picked its order's dish up and put it elsewhere meanwhile (test_yuanyuan7 208.8-210.6 s: immortal-galore-of-fortune
                // picked up covered table 5's order, then went to a gourmand): the tray read it, two things done.
                if (gone.Any(g => g.Kitchen == k && g.Call.Key == now.Key && g.At < step.At && !g.Done && !HandMoved(g.At, i, now.Dish?.Id)))
                {
                    flashes.Add(new Unsteady(step.At, k, $"{now.Key} back", step.Why));
                    continue;
                }
                // The kitchen's last call went a moment ago (or now) though still possible, and another took its place.
                var last = gone.LastOrDefault(g => g.Kitchen == k);
                bool orderComingUp = last.Call?.Kind == InnCallKind.CookAhead && now.Kind is InnCallKind.Queue or InnCallKind.Swap
                                     && (!ShownElsewhere(now, k, i, window) || gone.Any(g => g.Kitchen != k && g.Justified && g.Call.Dish?.Id == now.Dish?.Id && g.Call.Table == now.Table));
                // The same order in the same kitchen (a swap shown as its order once nothing is left to take out) is no switch.
                bool sameOrder = last.Call is { Table: not null } && last.Call.Table == now.Table && last.Call.Dish?.Id == now.Dish?.Id;
                if (last.Call is not null && !last.Justified && last.Call.Key != now.Key && !orderComingUp && !sameOrder)
                    jumps.Add(new Unsteady(step.At, k, $"{last.Call.Key} -> {now.Key}", step.Why));
                // A dish to cook ahead an order coming up displaced went for that order: it may show in another kitchen.
                if (orderComingUp) gone[gone.FindLastIndex(g => g.Kitchen == k)] = last with { Justified = true };
                // The same order, or dish cooked ahead, moving here from another kitchen.
                foreach (var g in gone.Where(g => g.Kitchen != k && !g.Justified && g.Call.Kind == now.Kind && g.Call.Dish?.Id == now.Dish?.Id && g.Call.Table == now.Table))
                    jumps.Add(new Unsteady(step.At, k, $"{g.Call.Key} moved to kitchen {k}", step.Why));
            }
        }
        return (flashes, jumps);
    }

    /// <summary>
    /// The tray's count of <paramref name="dish"/> changed between the grace before <paramref name="from"/> and step
    /// <paramref name="i"/>: the player picked it up or put it down (<see cref="CallStep.Supply"/>, as the tray read it).
    /// </summary>
    private bool HandMoved(TimeSpan from, int i, string? dish)
    {
        if (dish is null) return false;
        var since = from - InnCallHold.GraceSeconds - TimeSpan.FromSeconds(0.5);
        for (int j = i; j > 0 && Steps[j].At >= since; j--)
            if (Steps[j].Supply.GetValueOrDefault(dish).Hand != Steps[j - 1].Supply.GetValueOrDefault(dish).Hand) return true;
        return false;
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
        if (GaveWayToSameDish(step, call)) return true;
        // The advice stopped altogether (the inn closing: the last call).
        if (step.Open.Count == 0 && !step.CooksAhead && call.Kind is InnCallKind.Queue or InnCallKind.Swap) return true;
        // Its order: its dish out on the counter now, or served (a table's card read with that dish newly served), or the
        // order gone (its table read eating or paying), or its dish queued by the player in any kitchen (the order no longer
        // waits for one), within the grace. An order merely read away (a card misread as the HUD fades: every card reads
        // empty) is not something done.
        var grace = step.At - InnCallHold.GraceSeconds - TimeSpan.FromSeconds(0.5);
        if (call.Kind is InnCallKind.Queue or InnCallKind.Swap && !step.Open.Contains((call.Dish!.Id, call.Table!.Value))
            && (OnCounter(step, call.Dish.Id) || ServedWithin(i, call.Table.Value, call.Dish.Id) || DoneWithin(i, call.Table.Value)
                || QueuedWithin(i, grace, kitchen: null, dish: call.Dish.Id))) return true;
        if (call.Kind == InnCallKind.CookAhead && (!step.CooksAhead || step.AheadTooLate.Contains(kitchen))) return true;
        // Nothing done elsewhere excuses a call going (owner, 2026-10-21: "the recommendation should stay steady"): a dish
        // served to another table or queued in another kitchen leaves this call as possible as it was, so its going is a jump.
        // Another dish the player queued in this very kitchen: the advice suggests swapping it out for the order while it
        // has barely started (DispatchAdvisor.BarelyStartedSeconds), and once it has cooked longer the order goes where it
        // is out first; so the call may go that long after.
        return QueuedWithin(i, step.At - TimeSpan.FromSeconds(DispatchAdvisor.BarelyStartedSeconds) - InnCallHold.GraceSeconds - TimeSpan.FromSeconds(0.5), kitchen);
    }

    /// <summary>
    /// A dish to cook ahead gave way on that step because another kitchen's circle shows the same dish (one dish in one circle,
    /// <see cref="InnCallHold.DishShown"/>; owner, 2026-10-21, test_yuanyuan7): the hold's own words, for that call only.
    /// </summary>
    private static bool GaveWayToSameDish(CallStep step, InnCall call) =>
        call.Kind == InnCallKind.CookAhead
        && (step.Why.Contains($"{call.Key} dropped: {InnCallHold.DishShown}", StringComparison.Ordinal)
            || System.Text.RegularExpressions.Regex.IsMatch(step.Why, $@"{System.Text.RegularExpressions.Regex.Escape(call.Key)} replaced by \S+: {InnCallHold.DishShown}"));

    /// <summary>
    /// A queue (<paramref name="kitchen"/>'s, or any) added to between <paramref name="since"/> and step <paramref name="i"/>:
    /// <paramref name="dish"/> put in it, or (null) any dish.
    /// </summary>
    private bool QueuedWithin(int i, TimeSpan since, int? kitchen, string? dish = null)
    {
        for (int j = i; j > 0 && Steps[j].At >= since; j--)
        {
            foreach (int k in kitchen is { } only ? [only] : Enumerable.Range(1, KitchenMath.MaxKitchens))
            {
                var before = QueueOf(Steps[j - 1].Queues, k);
                var after = QueueOf(Steps[j].Queues, k);
                if (dish is { } id ? after.Count(d => d == id) > before.Count(d => d == id) : after.Count > before.Count || after.Except(before).Any()) return true;
            }
        }
        return false;
    }

    /// <summary>The estimate has <paramref name="dish"/> out on the counter at <paramref name="step"/>.</summary>
    private static bool OnCounter(CallStep step, string dish)
    {
        int at = step.State.IndexOf("counter ", StringComparison.Ordinal);
        if (at < 0) return false;
        int end = step.State.IndexOf(';', at);
        return step.State[at..(end < 0 ? step.State.Length : end)].Contains($"{dish}x", StringComparison.Ordinal);
    }

    /// <summary>
    /// A reading within the grace before step <paramref name="i"/> read <paramref name="table"/>'s <paramref name="dish"/>
    /// newly served (null: any table, any dish).
    /// </summary>
    private bool ServedWithin(int i, int? table, string? dish)
    {
        var since = Steps[i].At - InnCallHold.GraceSeconds - TimeSpan.FromSeconds(0.5);
        for (int j = i; j >= 0 && Steps[j].At >= since; j--)
        {
            foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(Steps[j].Why, $@"table {(table is { } t ? t.ToString(CultureInfo.InvariantCulture) : @"\d")} (\S+?),? -> (\S+?)[,;]?(?=\s|$)"))
            {
                var before = match.Groups[1].Value.Split('+');
                var served = match.Groups[2].Value.Split('+').Where(x => x.EndsWith('*') && !before.Contains(x));
                if (dish is null ? served.Any() : served.Contains(dish + "*")) return true;
            }
        }
        return false;
    }

    /// <summary>A reading within the grace before step <paramref name="i"/> read <paramref name="table"/> eating or paying: its order is over.</summary>
    private bool DoneWithin(int i, int table)
    {
        var since = Steps[i].At - InnCallHold.GraceSeconds - TimeSpan.FromSeconds(0.5);
        for (int j = i; j >= 0 && Steps[j].At >= since; j--)
            if (System.Text.RegularExpressions.Regex.IsMatch(Steps[j].Why, $@"table {table} \S+ -> (Eating|Paying|GourmandEating)\b")) return true;
        return false;
    }

    private static List<string> QueueOf(string queues, int kitchen) =>
        queues.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(q => q.StartsWith($"{kitchen}:", StringComparison.Ordinal))
            .SelectMany(q => q[(q.IndexOf(':') + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries)).ToList();

    /// <summary>
    /// A new order a kitchen can take (first among the orders the advice sends to a kitchen), and how long until a kitchen's
    /// call showed it (null: never, covered or served before); <paramref name="KitchenFree"/>: some kitchen not showing
    /// another order could take it then (<see cref="CallStep.FreeFor"/>; only then can it show at once: one call a kitchen,
    /// and an order shows in another kitchen than the advice's only when that kitchen has it out no more than
    /// <see cref="InnCallHold.ShowElsewhereWithinSeconds"/> later).
    /// </summary>
    internal sealed record NewOrder(TimeSpan At, string Dish, int Table, double? ShownAfter, bool KitchenFree);

    /// <summary>Every new order of the replay (<see cref="NewOrder"/>).</summary>
    public List<NewOrder> NewOrders()
    {
        var list = new List<NewOrder>();
        var before = new HashSet<(string Dish, int Table)>();
        int kitchens = Steps.Count == 0 ? 0 : Steps.Max(s => s.Kitchens.Count == 0 ? 0 : s.Kitchens.Keys.Max());
        for (int i = 0; i < Steps.Count; i++)
        {
            var step = Steps[i];
            foreach (var order in step.Sendable.Where(o => !before.Contains(o)))
            {
                bool free = step.FreeFor.Contains(order);
                double? shown = null;
                for (int j = i; j < Steps.Count && Steps[j].Open.Contains(order); j++)
                {
                    if (!Steps[j].Kitchens.Values.Any(c => c.Kind is InnCallKind.Queue or InnCallKind.Swap && c.Dish?.Id == order.Dish && c.Table == order.Table)) continue;
                    shown = (Steps[j].At - step.At).TotalSeconds;
                    break;
                }
                list.Add(new NewOrder(step.At, order.Dish, order.Table, shown, free));
            }
            before = [.. step.Sendable];
        }
        return list;
    }

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
        return new[] { "banner", "mark", "gourmand", "call", "move" }.ToDictionary(d => d, d => Changes.Count(c => c.Display == d) / minutes);
    }

    public string Report()
    {
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"# session span {SessionSpan.TotalSeconds:F1} s");
        sb.Append(Consistency());
        foreach (var (display, rate) in PerMinute())
            sb.AppendLine(CultureInfo.InvariantCulture, $"# {display}: {Changes.Count(c => c.Display == display)} changes, {rate:F1}/min");
        var (flashes, jumps) = Steadiness();
        var (changes, flashRate, jumpRate) = CallRates();
        sb.AppendLine(CultureInfo.InvariantCulture, $"# calls: {Changes.Count(c => c.Display == "call")} changes ({changes:F1}/min), {flashes.Count} flashes ({flashRate:F2}/min), {jumps.Count} jumps ({jumpRate:F2}/min)");
        foreach (var f in flashes) sb.AppendLine(CultureInfo.InvariantCulture, $"# flash {f.At.TotalSeconds,7:F1} kitchen {f.Kitchen}: {f.What}  <= {f.Why}");
        foreach (var j in jumps) sb.AppendLine(CultureInfo.InvariantCulture, $"# jump  {j.At.TotalSeconds,7:F1} kitchen {j.Kitchen}: {j.What}  <= {j.Why}");
        var orders = NewOrders();
        sb.AppendLine(CultureInfo.InvariantCulture, $"# new orders a kitchen can take: {orders.Count}; with a kitchen free of another order's call: {orders.Count(o => o.KitchenFree)}, shown at once: {orders.Count(o => o.KitchenFree && o.ShownAfter == 0)}");
        foreach (var o in orders.Where(o => o.ShownAfter != 0))
            sb.AppendLine(CultureInfo.InvariantCulture, $"# order {o.At.TotalSeconds,7:F1} {o.Dish}@{o.Table}: {(o.ShownAfter is { } after ? $"shown after {after:F1} s" : "never shown")}{(o.KitchenFree ? "" : " (every kitchen showing another order)")}");
        foreach (var c in Changes)
            sb.AppendLine(CultureInfo.InvariantCulture, $"{c.At.TotalSeconds,7:F1} {c.Display,-6} {c.Change} {c.What}  <= {c.Why}");
        // Every 10 s of the session: what the calls were worked out from; every step within ENDHUD_REPLAY_DETAIL ("from-to", s).
        double next = 0;
        var detail = Environment.GetEnvironmentVariable("ENDHUD_REPLAY_DETAIL") is { } d && d.Split('-') is [var from, var to]
            ? (From: double.Parse(from, CultureInfo.InvariantCulture), To: double.Parse(to, CultureInfo.InvariantCulture)) : (From: -1.0, To: -1.0);
        foreach (var step in Steps.Where(s => s.State.Length > 0 && s.Queues.Length > 0))
        {
            bool inDetail = step.At.TotalSeconds >= detail.From && step.At.TotalSeconds <= detail.To;
            if (step.At.TotalSeconds < next && !inDetail) continue;
            next = Math.Floor(step.At.TotalSeconds / 10) * 10 + 10;
            sb.AppendLine(CultureInfo.InvariantCulture, $"{step.At.TotalSeconds,7:F1} state  calls {string.Join(" ", step.Kitchens.Values.Select(c => c.Key))}; {step.State}");
        }
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
