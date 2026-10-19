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

/// <summary>What the setup panel can't recommend without, said to the player instead of a guess.</summary>
internal enum InnSetupGap
{
    /// <summary>The setup screen shows, but its theme couldn't be read (something covers it).</summary>
    ThemeUnreadable,

    /// <summary>No setup screen seen yet (EndHud started on the menu, say).</summary>
    NoSetup,

    /// <summary>The theme list was seen, not 营业设定 with its kitchens.</summary>
    NoKitchens,

    /// <summary>No chef picker read yet, or it named nobody.</summary>
    NoChefs,

    /// <summary>No dish of the menu screen seen.</summary>
    NoMenu,

    /// <summary>The menu needs drinks and no drink of the menu screen has been seen (its 饮品 tab).</summary>
    NoDrinks,

    /// <summary>The menu screen was seen, its 菜单要求 not read whole.</summary>
    MenuRulesUnreadable,
}

/// <summary>What the setup panel works from: the planner's input, or what's missing.</summary>
/// <param name="Input">Null when anything is missing (<paramref name="Gaps"/>): no plan from half a reading.</param>
/// <param name="Key">Changes when the input does: the plan is worked out again only then.</param>
internal sealed record InnSetupInputs(IReadOnlyList<InnSetupGap> Gaps, InnPlanInput? Input, string Key);

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
    IReadOnlyList<InnGourmandNote> Gourmands, IReadOnlyList<int> UnreadableTables, IReadOnlySet<int> ExactKitchens, bool Opening);

/// <summary>
/// What the helper knows of the inn, in memory only (plan S5/S7). Before a session: the setup
/// screen's theme and kitchens, and the menu screen's dishes and drinks, kept until the next setup.
/// During a session: each kitchen's chefs and queue (read off the dispatch panel whenever it shows
/// that kitchen; otherwise moved on by cook times since, an estimate), the table cards' orders,
/// what's ready, the Full Blaze meter, and each gourmand's request. It ends when no session frame
/// has been seen for <see cref="EndAfter"/>, or a setup or menu screen shows, and is forgotten.
/// </summary>
internal sealed class InnSession(InnData data, IReadOnlyList<OperatorInfo> roster, Action<string>? log = null, Action<string>? warn = null)
{
    /// <summary>A session is over when none of its frames has been seen for this long (the results screen, leaving).</summary>
    public static readonly TimeSpan EndAfter = TimeSpan.FromSeconds(15);

    /// <summary>A table card that couldn't be read is taken to be as it was for this long, then as unreadable.</summary>
    public static readonly TimeSpan TableKeptFor = TimeSpan.FromSeconds(3);

    /// <summary>The timer going up by more than this means a new session started.</summary>
    private static readonly TimeSpan NewSessionJump = TimeSpan.FromSeconds(10);

    private readonly Action<string> _log = log ?? DiagnosticLog.Info;
    private readonly Action<string> _warn = warn ?? (m => DiagnosticLog.Warn(m));

    // Before the session: kept until the next setup screen after a session.
    private InnSetup? _setup;
    private readonly Dictionary<string, MenuCard> _menu = [];
    private bool _sessionSinceSetup;
    // For the setup panel: every dish and drink card the menu screen showed (on the menu or not),
    // its requirements and counters, whether the setup screen's last reading failed, and the chefs
    // seen (cleared when the setup screens close: a session starts, or none shows for EndAfter).
    private readonly Dictionary<string, MenuCard> _offered = [];
    private MenuRequirements? _requirements;
    private (int Chosen, int Allowed)? _dishCounter, _drinkCounter;
    private bool _setupUnreadable;
    private readonly ChefPickerMemory _chefs = new();
    private TimeSpan? _setupScreensAt;

    // The session.
    private TimeSpan _lastSessionFrame, _advancedTo;
    private SessionClock? _clock;
    private readonly SortedDictionary<int, KitchenMemory> _kitchens = [];
    private int _panelKitchens;
    private readonly TableMemory?[] _tables = new TableMemory?[TableCardReader.Tables];
    private readonly Dictionary<string, int> _spares = [], _cooked = [], _readyBefore = [];
    private readonly HashSet<string> _dishesSeen = [], _drinksSeen = [];
    private readonly Dictionary<int, GourmandMemory> _gourmands = [];
    private readonly HashSet<string> _unreadLogged = [];
    private readonly FullBlazeTracker _blaze = new();
    private int? _fire;
    private bool _ordersSeen;
    private int? _panelOpen; // the kitchen the dispatch panel shows on the last session frame

    private sealed class KitchenMemory(int number)
    {
        public string?[] Chefs = [null, null];
        public bool ChefsSeen;
        public KitchenQueue Queue = new(number);
        public readonly Dictionary<string, int> PanelCookSeconds = [];
    }

    /// <param name="OrderSince">When this order first showed: older orders are served first.</param>
    private sealed record TableMemory(TableCard Card, TimeSpan SeenAt, TimeSpan OrderSince);

    private sealed record GourmandMemory(IReadOnlyList<GourmandRequest> Requests, string? Unread);

    /// <summary>A session is on (its frames seen within <see cref="EndAfter"/>).</summary>
    public bool Active { get; private set; }

    /// <summary>Takes in one reading of the inn.</summary>
    public void Update(InnReading reading)
    {
        if (reading.OnSetupScreens) _setupScreensAt = reading.At;
        // The theme is unreadable only while the setup screen showing it is; elsewhere the last good reading stands.
        if (reading.Screen != InnScreenKind.Setup) _setupUnreadable = false;
        switch (reading.Screen)
        {
            case InnScreenKind.Setup:
                LeaveSession();
                if (reading.Fresh) _setupUnreadable = reading.Setup is null;
                if (reading.Setup is { } setup)
                {
                    if (_sessionSinceSetup)
                    {
                        // Setting up the next session: the last one's menu may change.
                        _menu.Clear();
                        _offered.Clear();
                        _requirements = null;
                        _dishCounter = _drinkCounter = null;
                        _setup = null;
                        _sessionSinceSetup = false;
                    }
                    // The theme list shows no kitchens; keep the ones the kitchens' screen showed.
                    _setup = setup.Kitchens is null && _setup?.Kitchens is { } kitchens ? setup with { Kitchens = kitchens } : setup;
                    if (setup.Kitchens is { } placed) _chefs.TakePlaced(placed.SelectMany(k => k.Chefs));
                }
                return;
            case InnScreenKind.Menu:
                LeaveSession();
                foreach (var card in reading.Menu?.Cards ?? [])
                {
                    if (card.Id is null) continue;
                    _offered[card.Id] = card;
                    if (card.OnMenu) _menu[card.Id] = card;
                    else _menu.Remove(card.Id);
                }
                if (reading.Menu?.Dishes is { } dishes) _dishCounter = dishes;
                if (reading.Menu?.Drinks is { } drinks) _drinkCounter = drinks;
                if (reading.Requirements is { } requirements) _requirements = Merge(_requirements, requirements);
                return;
            case InnScreenKind.ChefPicker:
                LeaveSession();
                if (reading.Picker is { } picker) _chefs.Take(picker);
                return;
        }

        if (!Active || (reading.Clock is { } clock && _clock is { } before && clock.Left - before.Left > NewSessionJump))
            StartSession(reading.At);
        Advance(reading.At);
        _lastSessionFrame = reading.At;
        if (reading.Clock is not null) _clock = reading.Clock;
        _panelOpen = reading.Panel?.Kitchen;
        if (reading.Panel is { } panel) TakePanel(panel);
        if (reading.TableCards.Count > 0) TakeTables(reading.TableCards, reading.At);
        if (reading.Fire is { } fire) _fire = fire.Ready ? FullBlazeAdvisor.Full : fire.Fire ?? _fire;
        _blaze.Update(reading.At, reading.Fire?.Ready, inSession: true);
        if (reading.Bubble is { } bubble) TakeBubble(bubble);
    }

    /// <summary>A frame that isn't the inn, or time passing: ends a session not seen for <see cref="EndAfter"/>.</summary>
    public void Tick(TimeSpan now)
    {
        if (Active && now - _lastSessionFrame > EndAfter) LeaveSession();
        // The setup screens closed without a session (the player left): the chefs are seen afresh next time.
        if (!Active && _setupScreensAt is { } at && now - at > EndAfter)
        {
            _chefs.Clear();
            _setupScreensAt = null;
        }
    }

    /// <summary>A newer reading of the requirements, what it missed taken from the older (the numbers don't change, only their colours).</summary>
    private static MenuRequirements Merge(MenuRequirements? old, MenuRequirements read) => old is null ? read : new MenuRequirements(
        read.Rating ?? old.Rating, read.MinTypes ?? old.MinTypes, read.MinDishes ?? old.MinDishes, read.MinDrinks ?? old.MinDrinks,
        read.TraitRules.Count > 0 ? read.TraitRules : old.TraitRules, read.Lines);

    private void StartSession(TimeSpan at)
    {
        // The setup screens closed into a session.
        _chefs.Clear();
        _setupScreensAt = null;
        ClearSession();
        Active = true;
        _sessionSinceSetup = true;
        _advancedTo = at;
        _lastSessionFrame = at;
        _log($"inn: session started ({(_setup is null ? "setup not seen" : $"theme {_setup.ThemeName}")}, {_menu.Count} menu items seen)");
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
        _kitchens.Clear();
        _panelKitchens = 0;
        Array.Clear(_tables);
        _spares.Clear();
        _cooked.Clear();
        _readyBefore.Clear();
        _dishesSeen.Clear();
        _drinksSeen.Clear();
        _gourmands.Clear();
        _blaze.Update(TimeSpan.Zero, null, inSession: false);
        _fire = null;
        _ordersSeen = false;
        _panelOpen = null;
    }

    private KitchenMemory Kitchen(int number)
    {
        if (!_kitchens.TryGetValue(number, out var kitchen)) _kitchens[number] = kitchen = new KitchenMemory(number);
        return kitchen;
    }

    private Dish? DishOf(string? id) => id is null ? null : data.Dishes.FirstOrDefault(d => d.Id == id);

    private bool IsDrink(string id) => data.Drinks.Any(d => d.Id == id);

    /// <summary>Moves every kitchen's queue on to <paramref name="now"/>: what comes out is on the counter, cooked ahead until an order claims it.</summary>
    private void Advance(TimeSpan now)
    {
        double seconds = (now - _advancedTo).TotalSeconds;
        if (seconds <= 0) return;
        _advancedTo = now;
        foreach (var kitchen in _kitchens.Values)
        {
            foreach (var dish in kitchen.Queue.Tick(seconds))
            {
                _cooked[dish.Id] = _cooked.GetValueOrDefault(dish.Id) + 1;
                _spares[dish.Id] = _spares.GetValueOrDefault(dish.Id) + 1;
            }
        }
    }

    private void TakePanel(DispatchPanel panel)
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
        }
        // The queue as shown, if every dish in it is known; otherwise the estimate stays.
        var filled = panel.Queue.Where(q => q.Filled).ToList();
        if (filled.Any(q => DishOf(q.DishId) is null)) return;
        var kitchens = Kitchens();
        var dishes = filled.Select((q, i) =>
        {
            var dish = DishOf(q.DishId)!;
            double cook = kitchens?.Kitchens.Any(k => k.Number == panel.Kitchen) == true ? kitchens.CookSeconds(panel.Kitchen, dish) : dish.CookSeconds;
            return new QueuedDish(dish, i == 0 && q.SecondsLeft is { } left ? left : cook);
        }).Take(KitchenQueue.Slots);
        kitchen.Queue = new KitchenQueue(panel.Kitchen, dishes);
    }

    private void TakeTables(IReadOnlyList<TableCard?> cards, TimeSpan at)
    {
        for (int i = 0; i < Math.Min(cards.Count, _tables.Length); i++)
        {
            if (cards[i] is not { } card) continue; // kept as it was for a while (TableKeptFor)
            var before = _tables[i];
            bool sameOrder = before is not null && before.Card.State == card.State
                             && before.Card.Items.Select(x => x.Id).SequenceEqual(card.Items.Select(x => x.Id));
            _tables[i] = new TableMemory(card, at, sameOrder ? before!.OrderSince : at);
            if (card.State is TableState.Order or TableState.GourmandOrder) _ordersSeen = true;
            if (card.State is not (TableState.GourmandArrived or TableState.GourmandOrdering or TableState.GourmandOrder))
                _gourmands.Remove(card.Table);
            foreach (var item in card.Items)
            {
                if (item.Id is null) continue;
                if (IsDrink(item.Id)) _drinksSeen.Add(item.Id);
                else if (DishOf(item.Id) is not null) _dishesSeen.Add(item.Id);
            }
        }

        // A dish turning ready came out of a kitchen: it's no longer cooking, nor spare on the counter.
        var ready = new Dictionary<string, int>();
        foreach (var item in LiveTables(at).Where(t => t.Card.State == TableState.Order).SelectMany(t => Unserved(t.Card)))
            if (item.Progress == ItemProgress.Ready && item.Id is { } id && !IsDrink(id)) ready[id] = ready.GetValueOrDefault(id) + 1;
        foreach (var (id, count) in ready)
        {
            int more = count - _readyBefore.GetValueOrDefault(id);
            for (int n = 0; n < more; n++)
            {
                if (_spares.GetValueOrDefault(id) > 0) _spares[id]--;
                else TakeOutOfQueue(id);
            }
        }
        _readyBefore.Clear();
        foreach (var (id, count) in ready) _readyBefore[id] = count;
    }

    /// <summary>The estimate still has this dish cooking, but it's out: take its first place in a queue away.</summary>
    private void TakeOutOfQueue(string id)
    {
        foreach (var kitchen in _kitchens.Values)
        {
            int slot = kitchen.Queue.Dishes.ToList().FindIndex(d => d.Dish.Id == id);
            if (slot < 0) continue;
            kitchen.Queue.RemoveAt(slot);
            _cooked[id] = _cooked.GetValueOrDefault(id) + 1;
            return;
        }
    }

    private static IEnumerable<OrderItem> Unserved(TableCard card) =>
        card.Items.Where((_, i) => !(card.Served.Count == card.Items.Count && card.Served[i]));

    /// <summary>Tables seen recently enough to go by, oldest order first.</summary>
    private IEnumerable<TableMemory> LiveTables(TimeSpan now) =>
        _tables.OfType<TableMemory>().Where(t => now - t.SeenAt <= TableKeptFor).OrderBy(t => t.OrderSince).ThenBy(t => t.Card.Table);

    private void TakeBubble(GourmandBubble bubble)
    {
        var table = _tables.OfType<TableMemory>()
            .Where(t => t.Card.State is TableState.GourmandArrived or TableState.GourmandOrdering or TableState.GourmandOrder)
            .OrderBy(t => _gourmands.TryGetValue(t.Card.Table, out var g) && g.Requests.Count > 0 ? 1 : 0)
            .ThenByDescending(t => t.OrderSince)
            .FirstOrDefault();
        if (table is null) return;
        var requests = bubble.Requests(data).Select(m => m.Request).Take(GourmandAdvisor.MaxRequests).ToList();
        _gourmands.TryGetValue(table.Card.Table, out var known);
        if (requests.Count > 0)
        {
            // A bubble read half-faded may name fewer requests than it has: keep the fuller reading.
            if (known is null || known.Requests.Count < requests.Count) _gourmands[table.Card.Table] = new GourmandMemory(requests, null);
            return;
        }
        if (known is { Requests.Count: > 0 }) return;
        _gourmands[table.Card.Table] = new GourmandMemory([], bubble.Text);
        if (_unreadLogged.Add(bubble.Text)) _warn($"inn: a gourmand's request wasn't recognised; read \"{bubble.Text}\"");
    }

    /// <summary>The kitchens in play with their chefs, or null when none has been seen.</summary>
    private InnKitchens? Kitchens()
    {
        var numbers = new SortedSet<int>();
        if (_setup?.Kitchens is { } setupKitchens)
            foreach (var k in setupKitchens.Where(k => k.Chefs.Any(c => c is not null) || k.ChefNames.Any(n => n is not null))) numbers.Add(k.Number);
        if (numbers.Count == 0)
            for (int k = 1; k <= _panelKitchens; k++) numbers.Add(k);
        foreach (int k in _kitchens.Keys) numbers.Add(k);
        numbers.RemoveWhere(k => k is < 1 or > KitchenMath.MaxKitchens);
        if (numbers.Count == 0) return null;

        var kitchens = numbers.Select(n =>
        {
            IEnumerable<string?> ids = _kitchens.TryGetValue(n, out var seen) && seen.ChefsSeen ? seen.Chefs
                : _setup?.Kitchens?.FirstOrDefault(k => k.Number == n)?.Chefs ?? [];
            var chefs = ids.OfType<string>().Select(id => roster.FirstOrDefault(o => o.Id == id)).OfType<OperatorInfo>().Take(KitchenMath.MaxChefs).ToList();
            return new Kitchen(n, chefs);
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
        if (_setup?.Theme is { } theme) return Math.Clamp(theme.Rating, 1, KitchenMath.MaxRating);
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

    private IReadOnlyList<MenuDish> MenuDishes()
    {
        var ids = _menu.Keys.Where(id => DishOf(id) is not null).Concat(_dishesSeen).Distinct().ToList();
        return [.. ids.Select(id =>
        {
            int? stock = _menu.TryGetValue(id, out var card) && !card.Unlimited && card.Stock is { } n ? Math.Max(0, n - _cooked.GetValueOrDefault(id)) : null;
            return new MenuDish(DishOf(id)!, stock);
        })];
    }

    private IReadOnlyList<MenuDrink> MenuDrinks()
    {
        var ids = _menu.Keys.Where(IsDrink).Concat(_drinksSeen).Distinct();
        return [.. ids.Select(id =>
        {
            var drink = data.Drinks.First(d => d.Id == id);
            int? stock = !drink.Unlimited && _menu.TryGetValue(id, out var card) && !card.Unlimited ? card.Stock : null;
            return new MenuDrink(drink, stock);
        })];
    }

    /// <summary>
    /// What the setup panel works from (plan S6): the theme's hot traits and kitchens (营业设定), the
    /// chefs the picker showed and those placed, the dishes and drinks the menu screen showed with
    /// their stock, the menu's requirements and the Inn Rating (菜单要求). Anything missing is a gap
    /// and there's no input: nothing is planned from half a reading.
    /// </summary>
    public InnSetupInputs SetupInputs()
    {
        // A setup screen that can't be read says only that.
        if (_setupUnreadable) return new InnSetupInputs([InnSetupGap.ThemeUnreadable], null, "theme-unreadable");
        var gaps = new List<InnSetupGap>();
        // A kitchen's places as read; when they couldn't be told, the chefs placed there (a kitchen
        // showing neither may be locked: left out, the cautious side).
        var kitchens = (_setup?.Kitchens ?? []).Select(k => new PlanKitchen(k.Number, Math.Min(KitchenMath.MaxChefs, k.ChefSlots ?? k.Chefs.Count(c => c is not null))))
            .Where(k => k.Places > 0).Take(KitchenMath.MaxKitchens).ToList();
        if (_setup is null) gaps.Add(InnSetupGap.NoSetup);
        else if (kitchens.Count == 0) gaps.Add(InnSetupGap.NoKitchens);
        var chefs = _chefs.Available.Select(id => roster.FirstOrDefault(o => o.Id == id)).OfType<OperatorInfo>().ToList();
        if (!_chefs.PickerSeen || chefs.Count == 0) gaps.Add(InnSetupGap.NoChefs);
        var dishes = _offered.Values.Where(c => DishOf(c.Id) is not null).Select(c => new MenuDish(DishOf(c.Id)!, c.Unlimited ? null : c.Stock)).ToList();
        var drinks = _offered.Values.Where(c => IsDrink(c.Id!)).Select(c =>
        {
            var drink = data.Drinks.First(d => d.Id == c.Id);
            return new MenuDrink(drink, drink.Unlimited || c.Unlimited ? null : c.Stock);
        }).ToList();
        var r = _requirements;
        if (dishes.Count == 0) gaps.Add(InnSetupGap.NoMenu);
        else if (r is not { MinDishes: not null, MinDrinks: { } minDrinks, MinTypes: not null }) gaps.Add(InnSetupGap.MenuRulesUnreadable);
        else if (minDrinks > 0 && drinks.Count == 0) gaps.Add(InnSetupGap.NoDrinks);
        if (gaps.Count > 0) return new InnSetupInputs(gaps, null, string.Join(",", gaps));

        var setup = _setup!;
        // The theme's trait rules: as 营业设定 states them, and as the menu screen does.
        var traitRules = setup.MenuRules.Select(line => TraitRule.Parse(line, data)).OfType<TraitRule>().Concat(r!.TraitRules)
            .DistinctBy(t => $"{t.Count}:{string.Join("/", t.AnyOf)}").ToList();
        int minD = r.MinDishes!.Value, minK = r.MinDrinks!.Value;
        // The counters' "N/8" give the most allowed; unread, as few as required (a menu that fits for sure).
        var rules = new MenuRules(minD, Math.Max(minD, _dishCounter?.Allowed ?? minD), minK, Math.Max(minK, _drinkCounter?.Allowed ?? minK),
            r.MinTypes!.Value, traitRules);
        int rating = Math.Clamp(r.Rating ?? setup.Theme?.Rating ?? KitchenMath.MaxRating, 1, KitchenMath.MaxRating);
        var input = new InnPlanInput(InnPlanner.HotTraitsOf(setup.HotTraits, setup.Theme), rating, SiQualityPercent: 0, InnState.MaxTables,
            kitchens, chefs, dishes, drinks, rules);
        string key = string.Join("|",
            string.Join(",", input.HotTraits), rating, string.Join(",", kitchens.Select(k => $"{k.Number}:{k.Places}")),
            string.Join(",", chefs.Select(c => c.Id).Order()),
            string.Join(",", dishes.Select(d => $"{d.Dish.Id}:{d.Stock}").Order()), string.Join(",", drinks.Select(d => $"{d.Drink.Id}:{d.Stock}").Order()),
            $"{rules.MinDishes}-{rules.MaxDishes}/{rules.MinDrinks}-{rules.MaxDrinks}/{rules.MinTypes}",
            string.Join(",", traitRules.Select(t => $"{t.Count}:{string.Join("/", t.AnyOf)}")));
        return new InnSetupInputs([], input, key);
    }

    /// <summary>The advice now, the kitchens moved on to <paramref name="now"/>; null when no session is on.</summary>
    public InnAdvice? Advice(TimeSpan now)
    {
        Tick(now);
        if (!Active) return null;
        Advance(now);

        var unreadable = new List<int>();
        foreach (var table in _tables.OfType<TableMemory>())
        {
            bool stale = now - table.SeenAt > TableKeptFor;
            bool unknownItem = table.Card.State == TableState.Order && table.Card.Items.Any(i => i.Id is null);
            if (unknownItem || (stale && table.Card.State == TableState.Order)) unreadable.Add(table.Card.Table);
        }

        var gourmandTables = LiveTables(now).Where(t => _gourmands.ContainsKey(t.Card.Table)).Select(t => t.Card.Table).ToList();
        var exact = _panelOpen is { } open ? new HashSet<int> { open } : [];
        var kitchens = Kitchens();
        var menu = MenuDishes();
        InnGap? gap = kitchens is null ? InnGap.NoKitchens : menu.Count == 0 ? InnGap.NoMenu : null;
        if (gap is not null)
            return new InnAdvice(null, gap, null, _blaze.Running, [.. gourmandTables.Select(t => new InnGourmandNote(t, null, _gourmands[t].Unread, false)).Where(n => n.UnreadText is not null)],
                unreadable, exact, !_ordersSeen);

        var queues = kitchens!.Kitchens.Select(k => Kitchen(k.Number).Queue).ToList();
        var outstanding = new List<OrderedDish>();
        var counter = new List<Dish>();
        foreach (var table in LiveTables(now).Where(t => t.Card.State == TableState.Order && !unreadable.Contains(t.Card.Table)))
        {
            foreach (var item in Unserved(table.Card))
            {
                if (DishOf(item.Id) is not { } dish) continue; // a drink: no kitchen
                if (item.Progress == ItemProgress.Ready) counter.Add(dish);
                // Being made somewhere the estimate doesn't know of: nothing to send.
                if (item.Progress == ItemProgress.Making && !queues.Any(q => q.Dishes.Any(d => d.Dish.Id == dish.Id))) continue;
                outstanding.Add(new OrderedDish(table.Card.Table, dish));
            }
        }
        var spares = _spares.Where(s => s.Value > 0).SelectMany(s => Enumerable.Repeat(DishOf(s.Key)!, s.Value)).ToList();
        var state = new InnState(queues, [.. counter, .. spares], [], outstanding, menu);

        var left = _clock?.Left ?? TimeSpan.FromMinutes(5);
        var nextOut = queues.Where(q => q.Dishes.Count > 0).Select(q => (TimeSpan?)TimeSpan.FromSeconds(q.Dishes[0].SecondsLeft)).Min();
        FullBlazeAdvice? blaze = _fire is { } fire || _blaze.Running
            ? FullBlazeAdvisor.Advise(_fire ?? 0, _blaze.Running, left, state.Counter.Count, nextOut, _blaze.FullFor)
            : null;
        bool fillEverySlot = !_ordersSeen || blaze == FullBlazeAdvice.GetReady || _blaze.Running;

        var advisor = new DispatchAdvisor(kitchens);
        var dispatch = advisor.Advise(state, fillEverySlot);

        // For a gourmand only spare dishes count as on the counter: the ready ones are other tables'.
        var forGourmand = state with { Counter = spares };
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
            notes.Add(new InnGourmandNote(table, new GourmandAdvisor(advisor).Advise(memory.Requests, forGourmand, drinks), null, drinksUnknown));
        }
        return new InnAdvice(dispatch, null, blaze, _blaze.Running, notes, unreadable, exact, !_ordersSeen);
    }
}
