using System.Diagnostics;
using EndHud.Vision.Inn;
using EndHud.Vision.Operators;

namespace EndHud;

/// <summary>The clock the inn's readings and advice share: it keeps running, unlike the session's timer.</summary>
internal static class InnClock
{
    private static readonly long Start = Stopwatch.GetTimestamp();

    public static TimeSpan Now => Stopwatch.GetElapsedTime(Start);
}

/// <summary>What the helper can't advise without, said to the player instead of a guess.</summary>
internal enum InnGap
{
    /// <summary>No kitchen seen: neither the setup screen nor the dispatch panel.</summary>
    NoKitchens,

    /// <summary>No dish of the menu seen: neither the menu screen, the dispatch panel nor an order.</summary>
    NoMenu,
}

/// <summary>What to tell the player about one gourmand.</summary>
/// <param name="Advice">The requests in plain words and what meets them; null when the request wasn't recognised.</param>
/// <param name="UnreadText">The bubble's text as read, when no request matched it.</param>
/// <param name="DrinksUnknown">A request is for a drink, and no drink of the menu has been seen yet.</param>
internal sealed record InnGourmandNote(int Table, GourmandAdvice? Advice, string? UnreadText, bool DrinksUnknown);

/// <summary>
/// The advice for one moment of a session, before it's put in words (<see cref="InnStripView"/>).
/// </summary>
/// <param name="Dispatch">What to queue in each kitchen (null when a <paramref name="Gap"/> stops it).</param>
/// <param name="Blaze">When to start Full Blaze; null for nothing to say (or while it runs).</param>
/// <param name="UnreadableTables">Tables whose order couldn't be read: left out, never guessed.</param>
/// <param name="ExactKitchens">Kitchens the open dispatch panel shows right now; the others' queues are estimated from cook times.</param>
/// <param name="Opening">No guest has ordered yet: every kitchen is being filled ahead.</param>
internal sealed record InnAdvice(DispatchAdvice? Dispatch, InnGap? Gap, FullBlazeAdvice? Blaze, bool BlazeRunning,
    IReadOnlyList<InnGourmandNote> Gourmands, IReadOnlyList<int> UnreadableTables, IReadOnlySet<int> ExactKitchens, bool Opening)
{
    /// <summary>
    /// What to tell the player now, each held steady until done or impossible (a dish to cook ahead: or an order comes to its kitchen)
    /// (<see cref="InnCallHold"/>): the next dish per kitchen, a swap, the last call, Full Blaze. Shown as banners.
    /// </summary>
    public IReadOnlyList<InnCall> Calls { get; init; } = [];

    /// <summary>The dispatch panel open on the last session frame read, as read; null when it's closed.</summary>
    public DispatchPanel? Panel { get; init; }

    /// <summary>
    /// The calls shown as banners: all of them, except while the dispatch panel is open, when none (owner, live
    /// 2026-10-20: banners must not cover the dispatch panel); the panel's marks take over (<see cref="InnPanelMarks"/>).
    /// </summary>
    public IReadOnlyList<InnCall> BannerCalls => Panel is null ? Calls : [];

    /// <summary>
    /// The panel the marks go on (<see cref="InnPanelMarks"/>): <see cref="Panel"/> only while the last fresh session frame
    /// read it; null the moment a frame reads it closed (owner, live 2026-10-21: a mark over a closed panel floated over the
    /// dining room). The banners' hold (<see cref="InnSession.PanelClosedAfter"/>) is another decision: a banner coming back
    /// over an open panel covers it, a mark left over a closed one points at nothing.
    /// </summary>
    public DispatchPanel? MarkedPanel { get; init; }

    /// <summary>
    /// The dispatch panel's card each dish was read on this session (the same in every kitchen): a kitchen's tab is marked
    /// only when its call's dish has a card known, so opening that tab shows the card's mark (<see cref="InnPanelMarks"/>).
    /// </summary>
    public IReadOnlyDictionary<string, int> Cards { get; init; } = new Dictionary<string, int>();
}

/// <summary>
/// What the helper knows of the inn, in memory only (plan S5/S7). Before a session: the setup as
/// chosen so far (<see cref="InnSetupMemory"/>: theme, kitchens and chefs, menu), kept across pages
/// and into the session, forgotten on the theme list or when no inn screen has shown for
/// <see cref="ForgetAfter"/>. During a session: each kitchen's chefs and queue (read off the
/// dispatch panel whenever it shows that kitchen; otherwise moved on by cook times since, an
/// estimate), the table cards' orders, what's ready, the Full Blaze meter, and each gourmand's
/// request. A session ends when no session frame has been seen for <see cref="EndAfter"/>, or a
/// setup screen shows, and is forgotten.
/// </summary>
/// <param name="readingTime">Time spent reading the game at a moment of <see cref="InnClock"/>
/// (<see cref="ReadingClock.At"/>): what leaving the inn is measured in, so time the game was in
/// the background (reading off, no frames) never counts as leaving.</param>
internal sealed class InnSession(InnData data, IReadOnlyList<OperatorInfo> roster, Func<TimeSpan, TimeSpan> readingTime,
    Action<string>? log = null, Action<string>? warn = null)
{
    /// <summary>A session is over when none of its frames has been seen for this long (the results screen, leaving), in time spent reading the game.</summary>
    public static readonly TimeSpan EndAfter = TimeSpan.FromSeconds(15);

    /// <summary>
    /// A table card that couldn't be read is taken to be as it was for this long; an order's card keeps its order until a
    /// reading says otherwise (owner, 2026-10-21: a suggestion stays until the order is served or gone).
    /// </summary>
    public static readonly TimeSpan TableKeptFor = TimeSpan.FromSeconds(3);

    /// <summary>
    /// An order with a dish not yet served whose card reads empty or ordering is taken as gone only once every reading has
    /// said so for this long (owner, 2026-10-20: guests never leave, so a table empties only after eating and paying; the
    /// HUD fading reads every card empty, measured on test_yuanyuan3 and test_yuanyuan4 for up to 3.3 s, each time
    /// followed by the same order).
    /// </summary>
    public static readonly TimeSpan OrderGoneAfter = TimeSpan.FromSeconds(5);

    /// <summary>
    /// A gourmand's table read in another state (paying, empty, a new guest) for this long, with no gourmand state
    /// between, is done with its gourmand (the eating card can be missed). Longer than the HUD's fade, when every
    /// card reads empty (session-hud-faded.jpg).
    /// </summary>
    public static readonly TimeSpan GourmandGoneAfter = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Fresh session frames in a row that must read the dispatch panel before it counts as open (owner, live
    /// 2026-10-20: "it is still flashing"): a panel read on one frame only would take the banners down and put them up
    /// again a frame later.
    /// </summary>
    public const int PanelFramesToOpen = 2;

    /// <summary>
    /// How long no fresh frame may read the dispatch panel before it counts as closed. Measured on the
    /// owner's 2026-10-20 recording (InnReplayTests): with the panel open, the reading said "closed" for one frame 48
    /// times and for 2–5 frames in a row a dozen times more, between frames reading it open; each put every banner up
    /// for a moment. Closing it a little late keeps the banners down meanwhile, which is how they were.
    /// </summary>
    public static readonly TimeSpan PanelClosedAfter = TimeSpan.FromSeconds(1.5);

    /// <summary>
    /// A served gourmand's words read off a bubble again within this long are theirs, not a new gourmand's
    /// (<see cref="TakeBubble"/>): a bubble stays a moment as they eat, and shows again as the camera passes.
    /// </summary>
    public static readonly TimeSpan GourmandEchoFor = TimeSpan.FromSeconds(30);

    /// <summary>
    /// A dish leaving the player's hand is matched with a table read served with it within this long (the card and the tray
    /// are read on the same frames, a frame or two apart as each draws); otherwise it was put back on the counter. An
    /// assumption, not measured: a few readings at the live loop's 0.3 s.
    /// </summary>
    public static readonly TimeSpan HandSettlesAfter = TimeSpan.FromSeconds(3);

    /// <summary>The timer going up by more than this means a new session started.</summary>
    private static readonly TimeSpan NewSessionJump = TimeSpan.FromSeconds(10);

    private readonly Action<string> _log = log ?? DiagnosticLog.Info;
    private readonly Action<string> _warn = warn ?? (m => DiagnosticLog.Warn(m));

    // The setup as chosen so far, kept across pages until the theme list or leaving the inn.
    private readonly InnSetupMemory _memory = new(data, roster);
    private TimeSpan? _lastInnReading; // in reading time (readingTime): when an inn screen last showed

    // The session.
    private TimeSpan _lastSessionFrame, _advancedTo;
    private TimeSpan _lastSessionRead; // _lastSessionFrame in reading time: what ending the session is measured in
    private SessionClock? _clock;
    // When the timer first read 00:00: it shows 00:00 from then on, and the inn serves
    // InnState.ServingAfterCloseSeconds more, counted from here.
    private TimeSpan? _closedAt;
    private readonly SortedDictionary<int, KitchenMemory> _kitchens = [];
    private int _panelKitchens;
    private readonly TableMemory?[] _tables = new TableMemory?[TableCardReader.Tables];
    private readonly TableCard?[] _tableChange = new TableCard?[TableCardReader.Tables]; // an order's card read otherwise once, awaiting a second reading
    private readonly TimeSpan?[] _tableEmptySince = new TimeSpan?[TableCardReader.Tables]; // an open order's card read empty since then (OrderGoneAfter)
    // On the counter, by dish: cooked ahead (no order claimed it), and out for an order (it was that order's dish in a
    // queue, so it covers that order until served). Both are an estimate: the counter is never read off the screen.
    private readonly Dictionary<string, int> _spares = [], _outForOrders = [], _cooked = [], _servedBefore = [];
    private readonly HashSet<string> _dishesSeen = [], _drinksSeen = [];
    // The dispatch panel's card each dish was read on. The same in every kitchen (measured: on all four recorded sessions every
    // dish was read on one card, in all three kitchens), and the menu doesn't change in a session.
    private readonly Dictionary<string, int> _cards = [];
    // The dishes in the player's hand (the tray, TakeTray), a tray read otherwise once awaiting a second reading, dishes
    // that left the hand not yet read served on a table, and dishes read served while the tray still showed them.
    private readonly List<string> _carried = [];
    private List<string>? _trayChange;
    // Dishes in a kitchen's queue the player put there because a call said so (InnState.QueuedAsCalled), by kitchen and dish.
    private readonly Dictionary<(int Kitchen, string Id), int> _asCalled = [];
    // What each kitchen's Queued count was at the last advice (a dish added since is the player's doing), and the calls then shown.
    private readonly Dictionary<(int Kitchen, string Id), int> _queuedSeen = [];
    private IReadOnlyList<InnCall> _shownCalls = [];
    // _lateTray: dishes the tray still showed after they were read served (a tray a few frames late).
    private readonly List<(string Id, TimeSpan At)> _leftHand = [], _servedFromHand = [], _lateTray = [];
    private readonly Dictionary<int, GourmandMemory> _gourmands = [];
    private readonly Dictionary<int, TimeSpan> _gourmandLeaving = []; // a gourmand's table read in another state since then
    private readonly List<(IReadOnlyList<GourmandRequest> Requests, TimeSpan At)> _gourmandsDone = []; // served gourmands' words, and when (TakeBubble)
    private readonly HashSet<string> _unreadLogged = [];
    private readonly HashSet<int> _unreadableLogged = []; // tables whose unreadable order was logged this session
    private readonly HashSet<InnGap> _gapsLogged = []; // what the advice waited for, logged once per session (never shown)
    private readonly InnCallHold _calls = new();
    private readonly FullBlazeTracker _blaze = new();
    private int? _fire;
    private bool _ordersSeen;
    private DispatchPanel? _panelOpen; // the dispatch panel as shown: open once PanelFramesToOpen frames read it, until none has for PanelClosedAfter
    private int _panelOpening; // fresh session frames in a row that read the panel while it's shown closed
    private TimeSpan _panelReadAt; // the last fresh frame that read the panel
    private TimeSpan? _panelSeenClosedAt; // the last fresh frame that showed the panel closed for sure (PanelOnScreen)

    private sealed class KitchenMemory(int number)
    {
        public string?[] Chefs = [null, null];
        public bool ChefsSeen;
        public KitchenQueue Queue = new(number);
        public readonly Dictionary<string, int> PanelCookSeconds = [];

        /// <summary>The queue's dishes as the panel last read them for this kitchen (taken or not).</summary>
        public List<string>? LastRead;

        /// <summary>Dishes the estimate cooked out of this queue since the panel last showed it, and when, in order (<see cref="Align"/>).</summary>
        public readonly List<(string Id, TimeSpan At)> OutSinceRead = [];

        /// <summary>Dishes cooked out of this queue since the session began, by id (the estimate, put right by each panel reading).</summary>
        public readonly Dictionary<string, int> Out = [];

        /// <summary>Dishes of an id ever put in this queue: those in it and those cooked out (<see cref="InnKitchenView.Queued"/>).</summary>
        public int Queued(string id) => Queue.Dishes.Count(d => d.Dish.Id == id) + Out.GetValueOrDefault(id);
    }

    /// <param name="OrderSince">When this order first showed: older orders are served first.</param>
    private sealed record TableMemory(TableCard Card, TimeSpan SeenAt, TimeSpan OrderSince);

    private sealed record GourmandMemory(IReadOnlyList<GourmandRequest> Requests, string? Unread);

    /// <summary>
    /// No inn screen for this long means the inn was left (a fight, the open world): the setup is
    /// forgotten. Longer than a screen's fade or a missed read, shorter than a trip out and back.
    /// Counted in time spent reading the game (<see cref="ReadingClock"/>), never time it was in the background.
    /// </summary>
    public static readonly TimeSpan ForgetAfter = TimeSpan.FromSeconds(30);

    /// <summary>The dishes and drinks on the menu, as the menu screen last showed them.</summary>
    public IReadOnlyCollection<string> OnMenu => [.. _memory.Menu.Keys];

    /// <summary>The kitchen whose chef picker was last read; null when none.</summary>
    public int? PickerKitchen => _memory.PickerKitchen;

    /// <summary>What the chefs' advice works from (<see cref="InnSetupMemory.ForSetup"/>).</summary>
    public InnSetupInputs SetupInputs() => _memory.ForSetup();

    /// <summary>What the menu advice works from (<see cref="InnSetupMemory.ForMenu"/>).</summary>
    public InnSetupInputs MenuInputs() => _memory.ForMenu();

    /// <summary>What the last <see cref="Advice"/> changed of the calls shown, and why (<see cref="InnCallHold.Changes"/>).</summary>
    public IReadOnlyList<string> CallChanges => _calls.Changes;

    /// <summary>What each kitchen's queue holds, by dish, as "1:a,b 2:c" (the replay's trace: a queue changing is the player's doing, its times moving on aren't).</summary>
    public string QueueSignature() =>
        string.Join(" ", _kitchens.Values.Select(k => $"{k.Queue.Kitchen}:{string.Join(",", k.Queue.Dishes.Select(d => d.Dish.Id))}"));

    /// <summary>The dishes the estimate has on the counter, cooked ahead or out for an order, and those in hand ("@hand"; the replay's trace).</summary>
    public string CounterSignature() =>
        string.Join(",", _spares.Keys.Union(_outForOrders.Keys).Select(id => (id, n: _spares.GetValueOrDefault(id) + _outForOrders.GetValueOrDefault(id)))
            .Where(c => c.n > 0).Select(c => $"{c.id}x{c.n}")
            .Concat(_carried.GroupBy(id => id).Select(g => $"{g.Key}x{g.Count()}@hand")));

    /// <summary>
    /// Seconds until a dish would be out in a kitchen if queued now, as the calls count it (what's in its queue + the dish's
    /// cook time there); null when that kitchen has no free slot or doesn't cook (the replay's trace).
    /// </summary>
    internal double? ReadyIn(int kitchen, string dishId)
    {
        if (Kitchens() is not { } kitchens || !kitchens.Kitchens.Any(k => k.Number == kitchen) || DishOf(dishId) is not { } dish) return null;
        var queue = _kitchens.TryGetValue(kitchen, out var seen) ? seen.Queue : new KitchenQueue(kitchen);
        return queue.HasFreeSlot ? queue.SecondsUntilEmpty + kitchens.CookSeconds(kitchen, dish) : null;
    }

    /// <summary>A session is on (its frames seen within <see cref="EndAfter"/>).</summary>
    public bool Active { get; private set; }

    /// <summary>Takes in one reading of the inn.</summary>
    public void Update(InnReading reading)
    {
        // Long after the last, in time spent reading other screens: the inn was left in between, and
        // what was chosen is gone. Time the game was in the background doesn't count (ReadingClock).
        var read = readingTime(reading.At);
        if (_lastInnReading is { } last && read - last > ForgetAfter) _memory.Forget();
        _lastInnReading = read;
        switch (reading.Screen)
        {
            case InnScreenKind.ThemeList:
                // Choosing a theme begins a new setup.
                LeaveSession();
                _memory.Forget();
                return;
            case InnScreenKind.Setup or InnScreenKind.Menu or InnScreenKind.ChefPicker:
                LeaveSession();
                _memory.Take(reading);
                return;
        }

        if (!reading.Fresh)
        {
            // A frame the session's screen was missed on, held by the reading loop
            // (PartyMonitor.SessionHold): it says nothing new, only that time went on.
            if (Active) Advance(reading.At);
            return;
        }
        if (!Active || (reading.Clock is { } clock && _clock is { } before && clock.Left - before.Left > NewSessionJump))
            StartSession(reading.At);
        Advance(reading.At);
        _lastSessionFrame = reading.At;
        _lastSessionRead = read;
        if (reading.Clock is not null) _clock = reading.Clock;
        if (reading.Clock is { Closed: true } && _closedAt is null) _closedAt = reading.At;
        // A panel read with fewer tabs than this session has read is uncertain: a frame the panel was missed on (the
        // tabs misread on an open panel; on older readings, the dark bar behind the theme line taken for a panel).
        var panelRead = reading.Panel is { } seenPanel && seenPanel.Kitchens >= _panelKitchens ? seenPanel : null;
        if (panelRead is not null) TakePanel(panelRead, reading.At);
        // Closed for sure: something the open panel covers or moves was read. The Full Blaze meter; the timer in its pill under
        // the "结束经营" button (the open panel puts it at the top); the tray of held dishes. The meter alone (until 2026-10-21)
        // fades in last after the panel closes, and wasn't read at all through Full Blaze: a mark stayed over the dining room
        // (test_yuanyuan5 252.6 s).
        bool closedForSure = reading.Panel is null && (reading.Fire is not null || reading.Clock is { BelowButton: true } || reading.Tray is not null);
        TakePanelOpen(panelRead, reading.At, closedForSure);
        if (reading.TableCards.Count > 0) TakeTables(reading.TableCards, reading.At);
        if (reading.Tray is { } tray) TakeTray(tray, reading.At);
        SettleHand(reading.At);
        // While Full Blaze runs the meter shows its countdown, not the fire: the fire is unknown until read again, never
        // the full meter from before it was started.
        if (reading.Fire is { } fire) _fire = fire.BlazeLeft is not null ? null : fire.Ready ? FullBlazeAdvisor.Full : fire.Fire ?? _fire;
        _blaze.Update(reading.At, reading.Fire?.Ready, reading.Fire?.BlazeLeft, inSession: true);
        if (reading.Bubble is { } bubble) TakeBubble(bubble, reading.At);
    }

    /// <summary>
    /// A frame that isn't the inn, or time passing: ends a session not seen for <see cref="EndAfter"/>,
    /// and forgets the setup when no inn screen has shown for <see cref="ForgetAfter"/>, both counted
    /// in time spent reading the game (an alt-tab with the reading off is neither).
    /// </summary>
    public void Tick(TimeSpan now)
    {
        var read = readingTime(now);
        if (Active && read - _lastSessionRead > EndAfter) LeaveSession();
        if (_lastInnReading is { } last && read - last > ForgetAfter)
        {
            _memory.Forget();
            _lastInnReading = null;
        }
    }

    private void StartSession(TimeSpan at)
    {
        ClearSession();
        Active = true;
        _advancedTo = at;
        _lastSessionFrame = at;
        _lastSessionRead = readingTime(at);
        _log($"inn: session started ({(_memory.Setup is null ? "setup not seen" : $"theme {_memory.Setup.ThemeName}")}, {_memory.Menu.Count} menu items seen)");
    }

    private void LeaveSession()
    {
        if (!Active) return;
        _log("inn: session over");
        ClearSession();
    }

    private void ClearSession()
    {
        Active = false;
        _clock = null;
        _closedAt = null;
        _kitchens.Clear();
        _panelKitchens = 0;
        Array.Clear(_tables);
        Array.Clear(_tableChange);
        Array.Clear(_tableEmptySince);
        _spares.Clear();
        _outForOrders.Clear();
        _cooked.Clear();
        _servedBefore.Clear();
        _dishesSeen.Clear();
        _drinksSeen.Clear();
        _cards.Clear();
        _carried.Clear();
        _trayChange = null;
        _leftHand.Clear();
        _servedFromHand.Clear();
        _lateTray.Clear();
        _asCalled.Clear();
        _queuedSeen.Clear();
        _shownCalls = [];
        _gourmands.Clear();
        _gourmandLeaving.Clear();
        _gourmandsDone.Clear();
        _blaze.Update(TimeSpan.Zero, null, null, inSession: false);
        _fire = null;
        _ordersSeen = false;
        _panelOpen = null;
        _panelOpening = 0;
        _panelSeenClosedAt = null;
        _calls.Clear();
        _unreadableLogged.Clear();
        _gapsLogged.Clear();
    }

    private KitchenMemory Kitchen(int number)
    {
        if (!_kitchens.TryGetValue(number, out var kitchen)) _kitchens[number] = kitchen = new KitchenMemory(number);
        return kitchen;
    }

    private Dish? DishOf(string? id) => id is null ? null : data.Dishes.FirstOrDefault(d => d.Id == id);

    private bool IsDrink(string id) => data.Drinks.Any(d => d.Id == id);

    /// <summary>Moves every kitchen's queue on to <paramref name="now"/>: what comes out is on the counter (<see cref="PutOnCounter"/>).</summary>
    private void Advance(TimeSpan now)
    {
        double seconds = (now - _advancedTo).TotalSeconds;
        if (seconds <= 0) return;
        _advancedTo = now;
        foreach (var kitchen in _kitchens.Values)
        {
            foreach (var dish in kitchen.Queue.Tick(seconds))
            {
                kitchen.OutSinceRead.Add((dish.Id, now));
                kitchen.Out[dish.Id] = kitchen.Out.GetValueOrDefault(dish.Id) + 1;
                _cooked[dish.Id] = _cooked.GetValueOrDefault(dish.Id) + 1;
                PutOnCounter(dish.Id);
            }
        }
    }

    /// <summary>
    /// A dish out of a kitchen onto the counter. A queue is first in, first out, and an order is covered by the queued dish
    /// of its kind out soonest, oldest order first, so this dish was the next order's still waiting for one not yet out
    /// (<see cref="WaitingFor"/>): out for that order, it covers it until served. With no such order, it was cooked ahead.
    /// </summary>
    private void PutOnCounter(string id)
    {
        if (_outForOrders.GetValueOrDefault(id) < WaitingFor(id)) _outForOrders[id] = _outForOrders.GetValueOrDefault(id) + 1;
        else _spares[id] = _spares.GetValueOrDefault(id) + 1;
    }

    /// <summary>A dish off the counter (served, or a panel reading putting it back in its queue): one out for an order first.</summary>
    private bool TakeOffCounter(string id)
    {
        if (_outForOrders.GetValueOrDefault(id) > 0) _outForOrders[id]--;
        else if (_spares.GetValueOrDefault(id) > 0) _spares[id]--;
        else return false;
        return true;
    }

    /// <summary>Dishes of an id the open orders are still waiting for (not served).</summary>
    private int WaitingFor(string id) =>
        LiveTables(_lastSessionFrame).Where(t => t.Card.State == TableState.Order).Sum(t => Unserved(t.Card).Count(i => i.Id == id));

    /// <summary>
    /// The dispatch panel shown as open or closed (<see cref="PanelFramesToOpen"/>, <see cref="PanelClosedAfter"/>): while
    /// open, the latest reading of it, or the last one while frames read it closed.
    /// </summary>
    /// <param name="closedForSure">The frame shows what the open panel covers: the Full Blaze meter was read.</param>
    private void TakePanelOpen(DispatchPanel? panel, TimeSpan at, bool closedForSure)
    {
        if (closedForSure) _panelSeenClosedAt = at;
        if (panel is not null)
        {
            _panelReadAt = at;
            _panelOpening = _panelOpen is null ? _panelOpening + 1 : 0;
            if (_panelOpen is not null || _panelOpening >= PanelFramesToOpen)
            {
                _panelOpen = panel;
                _panelOpening = 0;
            }
            return;
        }
        _panelOpening = 0;
        if (_panelOpen is not null && at - _panelReadAt >= PanelClosedAfter) _panelOpen = null;
    }

    /// <summary>
    /// The panel shown as open is on screen now, for its marks (<see cref="InnAdvice.MarkedPanel"/>): no fresh frame since
    /// it was last read has shown it closed for sure, i.e. read the Full Blaze meter it covers (owner, live 2026-10-21:
    /// a mark left over the dining room). A frame merely missing it (its tabs misread for a frame while it's open: 48
    /// times on the 2026-10-20 recording) doesn't take the marks away; a frame showing the meter does, at once.
    /// </summary>
    private bool PanelOnScreen() => _panelOpen is not null && !(_panelSeenClosedAt > _panelReadAt);

    /// <summary>
    /// The panel as shown, each dish's card as read on it before (in any kitchen: the cards are the same in each) when this
    /// reading missed it (<see cref="_cards"/>): a kitchen opened for the first time is marked on its first frame.
    /// </summary>
    private DispatchPanel? PanelShown()
    {
        if (_panelOpen is not { } panel) return null;
        var read = panel.Dishes.Where(d => d.DishId is not null).Select(d => d.DishId!).ToHashSet();
        // A card read as no dish on this frame takes the dish it was read as before; a card read as another dish doesn't.
        var known = _cards.Where(c => !read.Contains(c.Key) && panel.Dishes.All(d => d.DishId is null || d.Card != c.Value) && DishOf(c.Key) is not null)
            .Select(c => new KitchenDish(c.Key, DishOf(c.Key)!.Zh, null) { Card = c.Value }).ToList();
        return known.Count == 0 ? panel : panel with { Dishes = [.. panel.Dishes.Where(d => d.DishId is not null || known.All(k => k.Card != d.Card)), .. known] };
    }

    private void TakePanel(DispatchPanel panel, TimeSpan at)
    {
        _panelKitchens = Math.Max(_panelKitchens, panel.Kitchens);
        var kitchen = Kitchen(panel.Kitchen);
        if (panel.Chefs.Any(c => c is not null))
        {
            kitchen.Chefs = [.. panel.Chefs.Take(KitchenMath.MaxChefs)];
            kitchen.ChefsSeen = true;
        }
        foreach (var dish in panel.Dishes)
        {
            if (dish.DishId is null) continue;
            _dishesSeen.Add(dish.DishId);
            if (dish.CookSeconds is { } s) kitchen.PanelCookSeconds[dish.DishId] = s;
            if (dish.Card is >= 0 and < DispatchPanelReader.Cards)
            {
                // A card read as another dish now: the old one's place is forgotten.
                foreach (var other in _cards.Where(c => c.Value == dish.Card && c.Key != dish.DishId).Select(c => c.Key).ToList()) _cards.Remove(other);
                _cards[dish.DishId] = dish.Card;
            }
        }
        // The queue as shown, if every dish in it is known; otherwise the estimate stays.
        var filled = panel.Queue.Where(q => q.Filled).ToList();
        if (filled.Any(q => DishOf(q.DishId) is null)) return;
        var kitchens = Kitchens();
        // The dish cooking with its seconds not read on this frame (test_yuanyuan4, 179.1 s): as the estimate has it, when the
        // estimate has that dish cooking; only otherwise its whole cook time.
        var cookingNow = kitchen.Queue.Dishes.FirstOrDefault();
        var dishes = filled.Select((q, i) =>
        {
            var dish = DishOf(q.DishId)!;
            double cook = kitchens?.Kitchens.Any(k => k.Number == panel.Kitchen) == true ? kitchens.CookSeconds(panel.Kitchen, dish) : dish.CookSeconds;
            double left = i > 0 ? cook : q.SecondsLeft ?? (cookingNow?.Dish.Id == dish.Id ? cookingNow.SecondsLeft : cook);
            return new QueuedDish(dish, left);
        }).Take(KitchenQueue.Slots).ToList();
        // The estimate put right. A queue is first in, first out, so the panel's queue reads as: dishes the estimate cooked
        // out too early (at its front: the last ones it cooked out since this kitchen was last read), then what the
        // estimate still has (after any dishes it kept too long, which came out), then what the player queued since. The
        // dishes come off and onto the counter with it.
        var estimate = kitchen.Queue.Dishes.Select(d => d.Dish.Id).ToList();
        var seen = dishes.Select(d => d.Dish.Id).ToList();
        double? frontLeft = filled.Count > 0 ? filled[0].SecondsLeft : null;
        double frontCook = dishes.Count == 0 ? 0
            : kitchen.PanelCookSeconds.TryGetValue(dishes[0].Dish.Id, out int shown) ? shown
            : kitchens?.Kitchens.Any(k => k.Number == panel.Kitchen) == true ? kitchens.CookSeconds(panel.Kitchen, dishes[0].Dish) : dishes[0].Dish.CookSeconds;
        var (cameOut, notOut, fits) = Align(estimate, seen, kitchen.OutSinceRead, frontLeft, frontCook, at);
        // Dishes gone from the queue, or a queue that doesn't line up, are taken once the next reading of this kitchen
        // agrees: on its first frame the panel reads its queue empty as it draws (test_yuanyuan3: seven times, each taking
        // dishes 30 s from done for out). Dishes added (the player queueing) are taken at once.
        bool confirmed = kitchen.LastRead is { } last && last.SequenceEqual(seen);
        kitchen.LastRead = seen;
        if ((cameOut > 0 || !fits) && !confirmed) return;
        if (!fits) cameOut = 0; // a dish taken out, or a slot misread: nothing put on the counter for it
        foreach (string id in estimate.Take(cameOut))
        {
            kitchen.Out[id] = kitchen.Out.GetValueOrDefault(id) + 1;
            PutOnCounter(id);
            _cooked[id] = _cooked.GetValueOrDefault(id) + 1;
        }
        foreach (string id in seen.Take(notOut))
        {
            kitchen.Out[id] = kitchen.Out.GetValueOrDefault(id) - 1;
            TakeOffCounter(id);
            _cooked[id] = _cooked.GetValueOrDefault(id) - 1;
        }
        kitchen.OutSinceRead.Clear();
        kitchen.Queue = new KitchenQueue(panel.Kitchen, dishes);
    }

    /// <summary>
    /// How the panel's queue (<paramref name="seen"/>) lines up with the estimate's: the first <c>NotOut</c> dishes the panel
    /// shows are ones the estimate cooked out too early (the last of <paramref name="outSinceRead"/>, in order), or the
    /// first <c>CameOut</c> dishes of the estimate are out (it kept them too long); what the estimate has left then begins
    /// the panel's queue, and the rest of it the player queued. Dishes at the front matching ones just cooked out are taken
    /// as not out yet when the seconds left on the first (<paramref name="frontLeft"/>) say it can't be one queued since:
    /// that would have at least its whole cook time (<paramref name="frontCook"/>) less the time since the estimate cooked
    /// the other out. <c>Fits</c> false: nothing lines up (a dish taken out, a slot misread).
    /// </summary>
    internal static (int CameOut, int NotOut, bool Fits) Align(IReadOnlyList<string> estimate, IReadOnlyList<string> seen,
        IReadOnlyList<(string Id, TimeSpan At)> outSinceRead, double? frontLeft, double frontCook, TimeSpan now)
    {
        bool Follows(int cameOut, int notOut)
        {
            var left = estimate.Skip(cameOut).ToList();
            return left.Count <= seen.Count - notOut && seen.Skip(notOut).Take(left.Count).SequenceEqual(left);
        }
        for (int notOut = Math.Min(outSinceRead.Count, seen.Count); notOut > 0; notOut--)
        {
            var early = outSinceRead.Skip(outSinceRead.Count - notOut).ToList();
            if (!seen.Take(notOut).SequenceEqual(early.Select(e => e.Id))) continue;
            // A dish queued after the first of these came out would have at least this long left (1 s for the reading's rounding).
            double queuedSinceAtLeast = frontCook - (now - early[0].At).TotalSeconds - 1;
            if (frontLeft is { } left && left >= queuedSinceAtLeast) continue;
            if (Follows(0, notOut)) return (0, notOut, true);
        }
        for (int cameOut = 0; cameOut <= estimate.Count; cameOut++)
            if (Follows(cameOut, 0)) return (cameOut, 0, true);
        return (estimate.Count, 0, false);
    }

    private void TakeTables(IReadOnlyList<TableCard?> cards, TimeSpan at)
    {
        for (int i = 0; i < Math.Min(cards.Count, _tables.Length); i++)
        {
            if (cards[i] is not { } card) continue; // kept as it was for a while (TableKeptFor)
            var before = _tables[i];
            bool sameOrder = before is not null && SameOrder(before.Card, card);
            if (!sameOrder && before?.Card is { State: TableState.Order } open && open.Items.Any(item => !item.Served)
                && card.State is TableState.Empty or TableState.Ordering)
            {
                // A dish not yet served and the table read empty: the HUD fading (guests never leave). Gone only once
                // every reading has said so for OrderGoneAfter.
                _tableEmptySince[i] ??= at;
                if (at - _tableEmptySince[i]!.Value < OrderGoneAfter) continue;
                _tableChange[i] = card; // read so long enough: confirmed below
            }
            else _tableEmptySince[i] = null;
            if (!sameOrder && before is not null && HoldsOrder(before.Card))
            {
                // An open order read otherwise (the HUD fading reads every card empty; a picture misread): the next
                // reading must agree before the order is taken as gone or changed. Meanwhile the card is as it was,
                // not seen anew (TableKeptFor still applies).
                bool confirmed = _tableChange[i] is { } change && SameOrder(change, card);
                _tableChange[i] = confirmed ? null : card;
                if (!confirmed) continue;
            }
            _tableChange[i] = null;
            _tables[i] = new TableMemory(card, at, sameOrder ? before!.OrderSince : at);
            if (card.State is TableState.Order or TableState.GourmandOrder) _ordersSeen = true;
            TakeGourmandTable(card, at);
            foreach (var item in card.Items)
            {
                if (item.Id is null) continue;
                if (IsDrink(item.Id)) _drinksSeen.Add(item.Id);
                else if (DishOf(item.Id) is not null) _dishesSeen.Add(item.Id);
            }
        }

        // A dish turning served came off the counter (out for an order, or cooked ahead) or straight out of a kitchen the
        // estimate still has it cooking in: it's no longer on the counter, nor cooking.
        var served = new Dictionary<string, int>();
        foreach (var item in LiveTables(at).Where(t => t.Card.State == TableState.Order).SelectMany(t => t.Card.Items))
            if (item.Served && item.Id is { } id && !IsDrink(id)) served[id] = served.GetValueOrDefault(id) + 1;
        foreach (var (id, count) in served)
        {
            int more = count - _servedBefore.GetValueOrDefault(id);
            for (int n = 0; n < more; n++) ServedOne(id, at);
        }
        _servedBefore.Clear();
        foreach (var (id, count) in served) _servedBefore[id] = count;
    }

    /// <summary>
    /// A gourmand's request is kept from the moment it's read until their order is fulfilled (owner, live 2026-10-20:
    /// "if you cannot see but the order is not fulfilled, just keep showing"): at once when their card reads them
    /// eating; when it reads another state (paying, empty, a new guest) on every reading for
    /// <see cref="GourmandGoneAfter"/>, since a faded HUD reads every card empty for a moment. A card not read changes nothing.
    /// </summary>
    private void TakeGourmandTable(TableCard card, TimeSpan at)
    {
        if (!_gourmands.ContainsKey(card.Table)) return;
        switch (card.State)
        {
            case TableState.GourmandArrived or TableState.GourmandOrdering or TableState.GourmandOrder:
                _gourmandLeaving.Remove(card.Table);
                return;
            case TableState.GourmandEating:
                Forget();
                return;
        }
        if (!_gourmandLeaving.TryGetValue(card.Table, out var since)) _gourmandLeaving[card.Table] = at;
        else if (at - since >= GourmandGoneAfter) Forget();

        void Forget()
        {
            if (_gourmands.TryGetValue(card.Table, out var done) && done.Requests.Count > 0) _gourmandsDone.Add((done.Requests, at));
            _gourmands.Remove(card.Table);
            _gourmandLeaving.Remove(card.Table);
        }
    }

    /// <summary>The estimate still has this dish cooking, but it's out: take its first place in a queue away.</summary>
    private void TakeOutOfQueue(string id)
    {
        foreach (var kitchen in _kitchens.Values)
        {
            int slot = kitchen.Queue.Dishes.ToList().FindIndex(d => d.Dish.Id == id);
            if (slot < 0) continue;
            kitchen.Queue.RemoveAt(slot);
            kitchen.Out[id] = kitchen.Out.GetValueOrDefault(id) + 1;
            _cooked[id] = _cooked.GetValueOrDefault(id) + 1;
            return;
        }
    }

    /// <summary>
    /// A dish read newly served on a table: from the player's hand when it just left the tray (or the tray still shows it:
    /// the tray read a frame later than the card), else off the counter or straight out of a kitchen the estimate still has
    /// it cooking in.
    /// </summary>
    private void ServedOne(string id, TimeSpan at)
    {
        int left = _leftHand.FindIndex(h => h.Id == id);
        if (left >= 0)
        {
            _leftHand.RemoveAt(left);
            return;
        }
        if (_carried.Remove(id))
        {
            _servedFromHand.Add((id, at));
            return;
        }
        if (!TakeOffCounter(id)) TakeOutOfQueue(id);
    }

    /// <summary>
    /// The tray of what the player holds (owner's live session 2026-10-21, test_yuanyuan5 258 s: 至臻鳞肉卷 in hand and the
    /// advice still sending one for table 4 to a kitchen). A change is taken once the next reading agrees (a frame read as
    /// the tray draws or fades); a tray with a picture not told apart says nothing. A dish coming into the hand was taken off
    /// the counter (or out of a kitchen the estimate still has it cooking in); one leaving it was served (<see cref="ServedOne"/>)
    /// or, when no table is read served with it within <see cref="HandSettlesAfter"/>, put back on the counter (the game's
    /// "放回出餐台").
    /// </summary>
    private void TakeTray(IReadOnlyList<HeldDish?> slots, TimeSpan at)
    {
        if (slots.Any(s => s is { Id: null })) return;
        var held = slots.OfType<HeldDish>().Select(h => h.Id!).Where(id => DishOf(id) is not null).Order(StringComparer.Ordinal).ToList();
        if (held.SequenceEqual(Shown()))
        {
            _trayChange = null;
            return;
        }
        if (_trayChange is null || !_trayChange.SequenceEqual(held))
        {
            _trayChange = held;
            return;
        }
        _trayChange = null;
        var shown = Shown();
        foreach (string id in held.Union(shown).Distinct().ToList())
        {
            int more = held.Count(h => h == id) - shown.Count(c => c == id);
            for (int n = 0; n < more; n++)
            {
                // The dish just read served while the tray still showed it, the tray a few frames late: the same dish, not
                // one in hand (it covers no order).
                int served = _servedFromHand.FindIndex(s => s.Id == id);
                if (served >= 0)
                {
                    _servedFromHand.RemoveAt(served);
                    _lateTray.Add((id, at));
                    continue;
                }
                // Picked up: off the counter, or out of a kitchen the estimate still has it cooking in.
                if (!TakeOffCounter(id)) TakeOutOfQueue(id);
                _carried.Add(id);
            }
            for (int n = 0; n < -more; n++)
            {
                int late = _lateTray.FindIndex(s => s.Id == id);
                if (late >= 0)
                {
                    _lateTray.RemoveAt(late);
                    continue;
                }
                _carried.Remove(id);
                int served = _servedFromHand.FindIndex(s => s.Id == id);
                if (served >= 0) _servedFromHand.RemoveAt(served);
                else _leftHand.Add((id, at));
            }
        }
    }

    /// <summary>What the tray is taken to show: the dishes in hand, and those it still shows a moment after they were served.</summary>
    private List<string> Shown() => [.. _carried.Concat(_lateTray.Select(l => l.Id)).Order(StringComparer.Ordinal)];

    /// <summary>
    /// A dish that left the hand with no table read served with it for <see cref="HandSettlesAfter"/> was put back on the
    /// counter; a dish read served while the tray still showed it, the tray never dropping it since, is forgotten.
    /// </summary>
    private void SettleHand(TimeSpan at)
    {
        foreach (var (id, since) in _leftHand.Where(h => at - h.At >= HandSettlesAfter).ToList())
        {
            _leftHand.Remove((id, since));
            PutOnCounter(id);
        }
        _servedFromHand.RemoveAll(s => at - s.At >= HandSettlesAfter);
        // A dish the tray kept showing after it was read served, for longer than a late frame: a second one, picked up.
        foreach (var (id, since) in _lateTray.Where(h => at - h.At >= HandSettlesAfter).ToList())
        {
            _lateTray.Remove((id, since));
            if (!TakeOffCounter(id)) TakeOutOfQueue(id);
            _carried.Add(id);
        }
    }

    private static IEnumerable<OrderItem> Unserved(TableCard card) => card.Items.Where(i => !i.Served);

    /// <summary>The same state and the same items (served or not).</summary>
    private static bool SameOrder(TableCard a, TableCard b) => a.State == b.State && a.Items.Select(x => x.Id).SequenceEqual(b.Items.Select(x => x.Id));

    /// <summary>A card with an order the advice works from: a guest's order, or a gourmand ordering or asking.</summary>
    private static bool HoldsOrder(TableCard card) =>
        card.State is TableState.Order or TableState.GourmandArrived or TableState.GourmandOrdering or TableState.GourmandOrder;

    /// <summary>
    /// Tables seen recently enough to go by, oldest order first. Counted against the last session
    /// frame read, not the clock: frames the session was missed on (held) say nothing about a card.
    /// </summary>
    private IEnumerable<TableMemory> LiveTables(TimeSpan now) =>
        _tables.OfType<TableMemory>().Where(t => t.Card.State == TableState.Order || _lastSessionFrame - t.SeenAt <= TableKeptFor)
            .OrderBy(t => t.OrderSince).ThenBy(t => t.Card.Table);

    /// <summary>
    /// A gourmand's bubble, given to the table whose gourmand said it (owner, live 2026-10-21: table 4's circles showed
    /// another gourmand's dessert and soup for 46 s while it asked for a soup and a drink). The bubble doesn't say whose it
    /// is, so its words do:
    /// <list type="number">
    /// <item>A table holding any of these words: theirs (the fuller reading kept: a half-faded bubble names fewer).</item>
    /// <item>Words of a gourmand served within <see cref="GourmandEchoFor"/>: their bubble read again, nobody's new request.</item>
    /// <item>Otherwise a gourmand speaking anew: the newest gourmand table with no words yet; else the newest one asking
    /// (arrived or choosing), whose words were someone else's or an earlier gourmand's at that table, replaced.</item>
    /// </list>
    /// Words not recognised go only to a table with none (and are logged once).
    /// </summary>
    private void TakeBubble(GourmandBubble bubble, TimeSpan at)
    {
        var asking = _tables.OfType<TableMemory>()
            .Where(t => t.Card.State is TableState.GourmandArrived or TableState.GourmandOrdering or TableState.GourmandOrder)
            .OrderByDescending(t => t.OrderSince).ThenBy(t => t.Card.Table)
            .ToList();
        var requests = bubble.Requests(data).Select(m => m.Request).Take(GourmandAdvisor.MaxRequests).ToList();
        bool Words(int table) => _gourmands.TryGetValue(table, out var g) && g.Requests.Count > 0;
        if (requests.Count > 0)
        {
            if (_gourmands.FirstOrDefault(g => g.Value.Requests.Intersect(requests).Any()) is { Value: { } said } owner)
            {
                if (said.Requests.Count < requests.Count) _gourmands[owner.Key] = new GourmandMemory(requests, null);
                return;
            }
            if (_gourmandsDone.Any(d => at - d.At <= GourmandEchoFor && d.Requests.Intersect(requests).Any())) return;
            var table = asking.FirstOrDefault(t => !Words(t.Card.Table))
                        ?? asking.FirstOrDefault(t => t.Card.State is TableState.GourmandArrived or TableState.GourmandOrdering)
                        ?? asking.FirstOrDefault();
            if (table is null) return;
            if (Words(table.Card.Table)) _log($"inn: table {table.Card.Table}'s gourmand says other words now; the earlier ones were another gourmand's");
            _gourmands[table.Card.Table] = new GourmandMemory(requests, null);
            _gourmandLeaving.Remove(table.Card.Table);
            return;
        }
        if (asking.FirstOrDefault(t => !Words(t.Card.Table)) is not { } waiting) return;
        _gourmands[waiting.Card.Table] = new GourmandMemory([], bubble.Text);
        if (_unreadLogged.Add(bubble.Text)) _warn($"inn: a gourmand's request wasn't recognised; read \"{bubble.Text}\"");
    }

    /// <summary>
    /// The kitchens that cook, with their chefs, or null when none has been seen. A kitchen the setup
    /// screen showed with no chef in any place cannot cook (<see cref="KitchenMath.Cooks"/>) and is
    /// left out, unless the dispatch panel has since named its chefs. A kitchen whose chefs weren't
    /// seen (no setup screen, an unread name, a panel naming nobody) is taken to cook, at base speed.
    /// </summary>
    private InnKitchens? Kitchens()
    {
        var numbers = new SortedSet<int>();
        var empty = new HashSet<int>();
        foreach (var k in _memory.Setup?.Kitchens ?? [])
        {
            bool named = k.Chefs.Any(c => c is not null) || k.ChefNames.Any(n => n is not null);
            if (named || k.ChefsPlaced > 0) numbers.Add(k.Number);
            else if (k.ChefsPlaced == 0) empty.Add(k.Number);
        }
        if (numbers.Count == 0)
            for (int k = 1; k <= _panelKitchens; k++) numbers.Add(k);
        foreach (int k in _kitchens.Keys) numbers.Add(k);
        numbers.RemoveWhere(k => k is < 1 or > KitchenMath.MaxKitchens || (empty.Contains(k) && !(_kitchens.TryGetValue(k, out var seen) && seen.ChefsSeen)));
        if (numbers.Count == 0) return null;

        var kitchens = numbers.Select(n =>
        {
            IEnumerable<string?> ids = _kitchens.TryGetValue(n, out var seen) && seen.ChefsSeen ? seen.Chefs
                : _memory.Setup?.Kitchens?.FirstOrDefault(k => k.Number == n)?.Chefs ?? [];
            var chefs = ids.OfType<string>().Select(id => roster.FirstOrDefault(o => o.Id == id)).OfType<OperatorInfo>().Take(KitchenMath.MaxChefs).ToList();
            // Known to have a chef, or not known at all: either way it cooks (known-empty kitchens are out above).
            return new Kitchen(n, chefs) { ChefsUnknown = chefs.Count == 0 };
        }).ToList();
        return new InnKitchens(data, kitchens, Rating(kitchens), siQualityPercent: 0);
    }

    /// <summary>
    /// The Inn Rating: the theme's, when the setup screen was seen. Otherwise the highest rating
    /// whose cook times agree with those the dispatch panel showed (the rating only changes them
    /// through the flavor bonuses), else the highest rating known.
    /// </summary>
    private int Rating(IReadOnlyList<Kitchen> kitchens)
    {
        if (_memory.Setup?.Theme is { } theme) return Math.Clamp(theme.Rating, 1, KitchenMath.MaxRating);
        for (int rating = KitchenMath.MaxRating; rating >= 1; rating--)
        {
            bool agrees = true;
            foreach (var kitchen in kitchens)
            {
                if (!_kitchens.TryGetValue(kitchen.Number, out var seen)) continue;
                foreach (var (id, seconds) in seen.PanelCookSeconds)
                    if (DishOf(id) is { } dish && KitchenMath.CookSeconds(data, kitchen, dish, rating) != seconds) agrees = false;
            }
            if (agrees) return rating;
        }
        return KitchenMath.MaxRating;
    }

    /// <summary>
    /// What the theme seen on the setup screen favours (<see cref="InnPlanner.ThemeItems"/>: its hot items and its text's
    /// guest preference), as the setup's plan counts it; empty when no theme was seen (nothing guessed).
    /// </summary>
    private IReadOnlyCollection<string> ThemeItems() => _memory.Setup is not { } setup ? []
        : [.. InnPlanner.HotTraitsOf(setup.HotTraits, setup.Theme).Union(InnPlanner.PreferenceOf(setup.Description, setup.HotTraits, setup.Theme, data))];

    private IReadOnlyList<MenuDish> MenuDishes()
    {
        var ids = _memory.Menu.Keys.Where(id => DishOf(id) is not null).Concat(_dishesSeen).Distinct().ToList();
        return [.. ids.Select(id =>
        {
            int? stock = _memory.Menu.TryGetValue(id, out var card) && !card.Unlimited && card.Stock is { } n ? Math.Max(0, n - _cooked.GetValueOrDefault(id)) : null;
            return new MenuDish(DishOf(id)!, stock);
        })];
    }

    private IReadOnlyList<MenuDrink> MenuDrinks()
    {
        var ids = _memory.Menu.Keys.Where(IsDrink).Concat(_drinksSeen).Distinct();
        return [.. ids.Select(id =>
        {
            var drink = data.Drinks.First(d => d.Id == id);
            int? stock = !drink.Unlimited && _memory.Menu.TryGetValue(id, out var card) && !card.Unlimited ? card.Stock : null;
            return new MenuDrink(drink, stock);
        })];
    }

    /// <summary>
    /// Seconds the inn still serves at <paramref name="now"/>: the timer's time left + 18 s before it
    /// closes, then 18 s from when it first read 00:00 (<see cref="InnState.ServingAfterCloseSeconds"/>);
    /// null when no timer has been read.
    /// </summary>
    private double? ServingLeft(TimeSpan now) =>
        _closedAt is { } closedAt ? InnState.ServingLeftAfterClose((now - closedAt).TotalSeconds)
        : _clock is { } clock ? InnState.ServingLeft(clock.Left.TotalSeconds)
        : null;

    /// <summary>The advice now, the kitchens moved on to <paramref name="now"/>; null when no session is on.</summary>
    public InnAdvice? Advice(TimeSpan now)
    {
        Tick(now);
        if (!Active) return null;
        Advance(now);

        var unreadable = new List<int>();
        foreach (var table in _tables.OfType<TableMemory>())
        {
            // An order's card not read since is still that order (LiveTables): only a picture not told apart makes it unreadable.
            bool unknownItem = table.Card.State == TableState.Order && table.Card.Items.Any(i => i.Id is null);
            if (unknownItem) unreadable.Add(table.Card.Table);
        }
        // Not shown to the player (nothing they can do about it): logged once per table and session.
        foreach (int table in unreadable)
            if (_unreadableLogged.Add(table)) _log($"inn: table {table}'s order can't be read; it's left out of the advice");

        // Every gourmand whose order isn't fulfilled, seen on this frame or not (TakeGourmandTable).
        var gourmandTables = _gourmands.Keys.Order().ToList();
        var exact = _panelOpen is { } open ? new HashSet<int> { open.Kitchen } : [];
        var kitchens = Kitchens();
        var menu = MenuDishes();
        InnGap? gap = kitchens is null ? InnGap.NoKitchens : menu.Count == 0 ? InnGap.NoMenu : null;
        if (gap is { } missing && _gapsLogged.Add(missing))
            _log($"inn: no advice yet, {(missing == InnGap.NoKitchens ? "no kitchen" : "no dish of the menu")} seen (not shown to the player)");
        if (gap is not null)
            return new InnAdvice(null, gap, null, _blaze.Running, [.. gourmandTables.Select(t => new InnGourmandNote(t, null, _gourmands[t].Unread, false)).Where(n => n.UnreadText is not null)],
                unreadable, exact, !_ordersSeen);

        var queues = kitchens!.Kitchens.Select(k => Kitchen(k.Number).Queue).ToList();
        // A dish the player put in a queue while a call named it (in that kitchen or another: test_yuanyuan5 at 00:44, 福寿全
        // called in kitchen 2 and queued in kitchen 1, then "先拿掉福寿全，再排这个" there) was queued as called.
        foreach (var kitchen in kitchens!.Kitchens)
        {
            var memory = Kitchen(kitchen.Number);
            foreach (string id in memory.Queue.Dishes.Select(d => d.Dish.Id).Distinct().ToList())
            {
                int queued = memory.Queued(id), before = _queuedSeen.GetValueOrDefault((kitchen.Number, id));
                if (queued > before && _shownCalls.Any(c => c.Kind is InnCallKind.Queue or InnCallKind.CookAhead or InnCallKind.Swap && c.Dish?.Id == id))
                    _asCalled[(kitchen.Number, id)] = _asCalled.GetValueOrDefault((kitchen.Number, id)) + queued - before;
                _queuedSeen[(kitchen.Number, id)] = queued;
            }
        }
        // Of the dishes queued as a call said, those still in their queue (a dish taken out, or the estimate's queue put
        // right by the panel, leaves fewer).
        foreach (var ((k, id), n) in _asCalled.ToList())
        {
            int still = Math.Min(n, _kitchens.TryGetValue(k, out var memory) ? memory.Queue.Dishes.Count(d => d.Dish.Id == id) : 0);
            if (still > 0) _asCalled[(k, id)] = still;
            else _asCalled.Remove((k, id));
        }
        var asCalled = _asCalled.SelectMany(a => Enumerable.Repeat(new QueuedAsCalled(a.Key.Kitchen, DishOf(a.Key.Id)!), a.Value)).ToList();
        var outstanding = new List<OrderedDish>();
        foreach (var table in LiveTables(now).Where(t => t.Card.State == TableState.Order && !unreadable.Contains(t.Card.Table)))
        {
            foreach (var item in Unserved(table.Card))
                if (DishOf(item.Id) is { } dish) outstanding.Add(new OrderedDish(table.Card.Table, dish)); // a drink: no kitchen
        }
        // A dish out for an order no longer waiting for one (the order gone, or misread) was cooked ahead after all.
        foreach (var (id, n) in _outForOrders.ToList())
        {
            int excess = n - outstanding.Count(o => o.Dish.Id == id);
            if (excess <= 0) continue;
            _outForOrders[id] = n - excess;
            _spares[id] = _spares.GetValueOrDefault(id) + excess;
        }
        // On the counter: the dishes out for an order, which cover it (owner, 2026-10-21: it was that order's dish in its
        // queue), and those cooked ahead, which cover no order: they're where the estimate drifts.
        var outForOrders = _outForOrders.Where(s => s.Value > 0).SelectMany(s => Enumerable.Repeat(DishOf(s.Key)!, s.Value)).ToList();
        var counter = outForOrders.Concat(_spares.Where(s => s.Value > 0).SelectMany(s => Enumerable.Repeat(DishOf(s.Key)!, s.Value))).ToList();
        // In hand: what the tray shows (a dish in hand covers an order of it first, DispatchAdvisor's Cover).
        var carried = _carried.Select(id => DishOf(id)!).ToList();
        var state = new InnState(queues, counter, outForOrders, carried, outstanding, menu, ServingLeft(now), asCalled, ThemeItems());

        var left = _clock?.Left ?? TimeSpan.FromMinutes(5);
        var nextOut = queues.Where(q => q.Dishes.Count > 0).Select(q => (TimeSpan?)TimeSpan.FromSeconds(q.Dishes[0].SecondsLeft)).Min();
        FullBlazeAdvice? blaze = _fire is { } fire || _blaze.Running
            ? FullBlazeAdvisor.Advise(_fire ?? 0, _blaze.Running, left, state.Counter.Count + state.Carried.Count, nextOut, _blaze.FullFor)
            : null;
        bool fillEverySlot = !_ordersSeen || blaze == FullBlazeAdvice.GetReady || _blaze.Running;

        var advisor = new DispatchAdvisor(kitchens);
        var view = new InnKitchenView((k, id) => Kitchen(k).Queued(id), k => Kitchen(k).Queue.HasFreeSlot,
            (k, dish) => Kitchen(k).Queue.SecondsUntilEmpty + kitchens.CookSeconds(k, dish));
        // The calls on screen are kept while possible: the advice plans around them (owner, 2026-10-21: "the recommendation
        // should stay steady").
        var dispatch = advisor.Advise(state, fillEverySlot, _calls.Kept(view));

        var drinks = MenuDrinks();
        var notes = new List<InnGourmandNote>();
        foreach (int table in gourmandTables)
        {
            var memory = _gourmands[table];
            if (memory.Requests.Count == 0)
            {
                notes.Add(new InnGourmandNote(table, null, memory.Unread, false));
                continue;
            }
            bool drinksUnknown = drinks.Count == 0 && memory.Requests.Any(r => GourmandCondition.Parse(r.When, data).ForDrink);
            notes.Add(new InnGourmandNote(table, new GourmandAdvisor(advisor).Advise(memory.Requests, state, drinks), null, drinksUnknown));
        }
        var calls = _calls.Update(dispatch, blaze, view, now);
        _shownCalls = calls;
        return new InnAdvice(dispatch, null, blaze, _blaze.Running, notes, unreadable, exact, !_ordersSeen)
        {
            Calls = calls,
            Panel = PanelShown(),
            MarkedPanel = PanelOnScreen() ? PanelShown() : null,
            Cards = new Dictionary<string, int>(_cards),
        };
    }
}
