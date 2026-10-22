namespace EndHud.Vision.Inn;

/// <summary>A dish on the session's menu and how many more can be cooked (可烹饪 N).</summary>
/// <param name="Stock">Null: unlimited (可烹饪∞).</param>
public sealed record MenuDish(Dish Dish, int? Stock);

/// <summary>A dish a table ordered and hasn't been given yet.</summary>
/// <param name="Table">1 to <see cref="InnState.MaxTables"/>.</param>
public sealed record OrderedDish(int Table, Dish Dish);

/// <summary>
/// What the advisor works from, at one moment of a session: each kitchen's queue, the counter,
/// what the player carries, the dishes tables are still waiting for (oldest order first) and the menu.
/// </summary>
/// <param name="Queues">One per kitchen in play, by kitchen number.</param>
/// <param name="Counter">
/// The dishes taken to be on the counter. Live, an estimate (the counter is never read off the screen), so it never
/// caps cooking ahead (owner, 2026-10-21), and a dish cooked ahead on it covers no order: only the last call hands its
/// dishes out, and cooking ahead prefers dishes there are fewer of.
/// </param>
/// <param name="OutForOrders">
/// Of <paramref name="Counter"/>, the dishes that came out of a kitchen for an order still waiting (they were that
/// order's dishes in the queue): each covers its order, as it did while queued. No default: a caller says what it knows.
/// </param>
/// <param name="Outstanding">Ordered and not yet given to the table, oldest first. Drinks aren't in it: they need no kitchen.</param>
/// <param name="ServingSecondsLeft">
/// Seconds the inn still serves: the timer's time left + <see cref="ServingAfterCloseSeconds"/> before
/// it closes, and what's left of those 18 s after (<see cref="ServingLeft"/>, <see cref="ServingLeftAfterClose"/>);
/// null when the timer hasn't been read. No default: a caller says what it knows.
/// </param>
/// <param name="QueuedAsCalled">
/// Dishes in the queues the player put there because a call said so (put in while a call named the dish, in that kitchen
/// or another), by kitchen: the advice never suggests taking one out again while it still comes out before serving ends
/// (owner's live session
/// 2026-10-21, test_yuanyuan5 at 04:15: "厨房二 预做：界龙肉卷" followed, then 10 s later "先拿掉界龙肉卷，再排这个"). No default:
/// a caller says what it knows (none, where nothing is shown to a player).
/// </param>
/// <param name="ThemeItems">
/// What the session's theme favours (<see cref="InnPlanner.ThemeItems"/>: its hot traits and dish types and its text's guest
/// preference): cooking ahead picks the dishes matching it first (owner, 2026-10-20: "always prefer the matching ones").
/// No default: a caller says what it knows (empty when no theme was seen).
/// </param>
public sealed record InnState(IReadOnlyList<KitchenQueue> Queues, IReadOnlyList<Dish> Counter, IReadOnlyList<Dish> OutForOrders, IReadOnlyList<Dish> Carried,
    IReadOnlyList<OrderedDish> Outstanding, IReadOnlyList<MenuDish> Menu, double? ServingSecondsLeft, IReadOnlyList<QueuedAsCalled> QueuedAsCalled,
    IReadOnlyCollection<string> ThemeItems)
{
    /// <summary>The timer has reached 00:00: no new guests, the seated ones served for what's left of the 18 s.</summary>
    public bool Closed => ServingSecondsLeft <= ServingAfterCloseSeconds;

    /// <summary>The inn has at most six tables (owner).</summary>
    public const int MaxTables = 6;

    /// <summary>
    /// After the 5:00 session timer reaches 00:00 the inn keeps serving the guests already seated for
    /// this long, then the session ends (owner, 2026-10-19: "after 5min, there is 18s for the left over time").
    /// </summary>
    public const double ServingAfterCloseSeconds = 18;

    /// <summary>Seconds the inn still serves, the timer showing <paramref name="timerSecondsLeft"/> before it closes.</summary>
    public static double ServingLeft(double timerSecondsLeft) => Math.Max(0, timerSecondsLeft) + ServingAfterCloseSeconds;

    /// <summary>Seconds the inn still serves, <paramref name="secondsSinceClosed"/> after the timer first read 00:00.</summary>
    public static double ServingLeftAfterClose(double secondsSinceClosed) => Math.Max(0, ServingAfterCloseSeconds - Math.Max(0, secondsSinceClosed));
}

/// <summary>A dish the player queued in <paramref name="Kitchen"/> because a call told them to (<see cref="InnState.QueuedAsCalled"/>).</summary>
public sealed record QueuedAsCalled(int Kitchen, Dish Dish);

/// <summary>A dish to put in a kitchen's queue.</summary>
/// <param name="Table">The table it's for; null for a dish cooked ahead (预做).</param>
/// <param name="ReadyIn">Seconds until it's out, if queued now.</param>
public sealed record QueueAdvice(Dish Dish, int? Table, double ReadyIn)
{
    public bool CookAhead => Table is null;
}

/// <summary>Take a dish cooked ahead out of a kitchen's queue and queue a table's dish instead.</summary>
/// <param name="Slot">The queue slot to empty as the queue is now (before any other swap), 0 = the dish cooking.</param>
/// <param name="ReadyIn">Seconds until <paramref name="Put"/> is out after the swap.</param>
/// <param name="OtherwiseIn">Seconds until it would be out in the best kitchen without a swap; null when no kitchen has room.</param>
public sealed record ReplaceAdvice(int Kitchen, int Slot, Dish Remove, Dish Put, int Table, double ReadyIn, double? OtherwiseIn);

/// <summary>
/// A dish the player has been told to queue next in a kitchen (a call on screen): for <paramref name="Table"/>'s order,
/// or to cook ahead when null. The advice keeps it there while it's still possible (<see cref="DispatchAdvisor.Advise(InnState, bool, IReadOnlyList{KeptCall})"/>).
/// </summary>
/// <param name="Remove">For a swap shown, the dish it takes out (kept as a swap while that dish is there to take out); null otherwise. No default: a caller says which.</param>
public sealed record KeptCall(int Kitchen, Dish Dish, int? Table, Dish? Remove);

/// <summary>What to do in one kitchen: dishes to queue, in order, and any swap.</summary>
public sealed record KitchenAdvice(int Kitchen, IReadOnlyList<QueueAdvice> Queue, IReadOnlyList<ReplaceAdvice> Replaces);

/// <summary>The last call: give a dish left on the counter (or carried) to a table still waiting for one.</summary>
/// <param name="Ordered">The dish the table is waiting for, which <paramref name="Dish"/> stands in for.</param>
public sealed record LeftoverAdvice(Dish Dish, int Table, Dish Ordered)
{
    /// <summary>It's the dish the table ordered; otherwise a wrong dish, which still earns some money (owner).</summary>
    public bool Matches => Dish.Id == Ordered.Id;
}

/// <param name="Waiting">Ordered dishes no kitchen has room for now (and no swap helps).</param>
/// <param name="OutOfStock">Ordered dishes that can't be cooked: none left.</param>
/// <param name="Leftovers">
/// The last call: what's left to hand out to tables whose own dish can't come out before the inn stops
/// serving, and once it has closed the dishes on the counter to the tables that ordered them; the right dishes first.
/// </param>
public sealed record DispatchAdvice(IReadOnlyList<KitchenAdvice> Kitchens, IReadOnlyList<OrderedDish> Waiting, IReadOnlyList<OrderedDish> OutOfStock,
    IReadOnlyList<LeftoverAdvice> Leftovers)
{
    public KitchenAdvice For(int kitchen) => Kitchens.First(k => k.Kitchen == kitchen);
}

/// <summary>
/// Which kitchen each ordered dish should go to, what to cook ahead, and when to swap a dish cooked
/// ahead for an order (plan S5). The player sends every dish by hand; this only says which.
/// <list type="bullet">
/// <item><b>Dispatch:</b> each dish a table is still waiting for, not already carried, queued or out of a
/// kitchen for it (<see cref="InnState.OutForOrders"/>), goes to the kitchen that has it out soonest (owner, 2026-10-19: what it is cooking
/// counted from its progress, the seconds it has left, + the dishes waiting behind it + this dish's
/// cook time there; an idle kitchen wins only when that makes it the earliest), among kitchens with
/// a free slot; on a tie, the one with the better choice-dish chance. A dish cooked ahead taken to be on
/// the counter doesn't stop it, nor does the counter being full (owner, 2026-10-21: the counter is only
/// estimated, and the estimate drifting to "full" on test_yuanyuan4 hid real orders).</item>
/// <item><b>Cook ahead:</b> the dishes matching the theme first (<see cref="InnState.ThemeItems"/>, two matches
/// before one; owner, 2026-10-20: "always prefer the matching ones"), then dishes not yet cooked ahead before a second of one that
/// is (spread over the menu), then those whose type the kitchen's chefs speed up (owner, 2026-10-21: a 主食 kitchen in a
/// 主食/汤羹 theme was told to cook 金丝小贝, a dessert, ahead, test_yuanyuan6 201 s), then the slowest. Each goes, like an order, to the kitchen where it is out first. Never one with no stock.
/// No counter cap (owner, 2026-10-21: a dish not used gives its ingredients back, so cooking ahead is
/// never a waste). Normally only kitchens nothing was ordered for, filled to all but one slot, so a
/// new order never waits behind three dishes; at the session's start and when Full Blaze is near,
/// every free slot of every kitchen.</item>
/// <item><b>Replace:</b> a dish cooked ahead (one no order claims) is suggested for removal only
/// when queueing the order in its place has it out sooner than any kitchen could otherwise, by more
/// than <see cref="ReplaceMarginSeconds"/>. A dish cooking is lost when taken out, so one is only
/// worth taking out when it has barely started (<see cref="BarelyStartedSeconds"/>). A dish the player queued because a
/// call said so is never suggested for removal while it still comes out in time (<see cref="InnState.QueuedAsCalled"/>):
/// the advice doesn't undo its own word.</item>
/// <item>A kitchen with no chef cannot cook (<see cref="KitchenMath.Cooks"/>): it gets no advice of
/// any kind and isn't listed.</item>
/// <item><b>The end</b> (owner, 2026-10-19): the inn serves <see cref="InnState.ServingAfterCloseSeconds"/>
/// after the timer's 00:00, and every rule here counts that time (<see cref="InnState.ServingSecondsLeft"/>).
/// Nothing is cooked ahead in the last <see cref="NoCookAheadSeconds"/> of serving, nor ever a dish that
/// wouldn't come out before serving ends. The last call is per table and can come before 00:00: a table
/// waiting for a dish whose own dish can no longer come out in time (what's queued ahead of it + its
/// cook time, or no stock) is handed a dish left on the counter or carried, its own dish if one is
/// there, else a wrong one, which still earns some money; oldest table first. A table whose dish can
/// still make it waits for it. Drinks need no kitchen and aren't outstanding dishes, so a table waiting
/// only for a drink is never handed a dish (it wouldn't be served). Once closed, every table whose own
/// dish is on the counter or carried is handed it. Besides a dish out for an order covering that order,
/// the last call is the one rule the counter decides.</item>
/// </list>
/// </summary>
public sealed class DispatchAdvisor(InnKitchens kitchens)
{
    /// <summary>
    /// A swap must win by more than this. An assumption, not measured: big enough that a queue time
    /// read a second or two off doesn't flip the advice from frame to frame.
    /// </summary>
    public const double ReplaceMarginSeconds = 5;

    /// <summary>
    /// A cooking dish is only worth taking out if it has cooked no longer than this (its progress is
    /// lost, <see cref="KitchenQueue.RemovedDishKeepsProgress"/>). An assumption, not measured.
    /// </summary>
    public const double BarelyStartedSeconds = 5;

    /// <summary>
    /// No dish is cooked ahead with less than this left before the inn stops serving (owner, 2026-10-19:
    /// "the last ~40 s"; counted against the serving time, 18 s past the timer's 00:00, as the owner's
    /// "precook likewise counts the 18 s" says).
    /// </summary>
    public const double NoCookAheadSeconds = 40;

    public InnKitchens Kitchens { get; } = kitchens;

    /// <summary>The advice for <paramref name="state"/>, with nothing shown to the player yet.</summary>
    /// <param name="fillEverySlot">
    /// Cook ahead into every free slot of every kitchen, not only idle ones: at the session's start
    /// (C16) and from Full Blaze's "get ready" (<see cref="FullBlazeAdvice.GetReady"/>).
    /// </param>
    /// <exception cref="ArgumentException">A queue for a kitchen not in play, or a table out of range.</exception>
    public DispatchAdvice Advise(InnState state, bool fillEverySlot = false) => Advise(state, fillEverySlot, []);

    /// <summary>The advice for <paramref name="state"/>, keeping the calls on screen (<paramref name="kept"/>) while they're possible.</summary>
    public DispatchAdvice Advise(InnState state, bool fillEverySlot, IReadOnlyList<KeptCall> kept)
    {
        var plan = new Plan(this, state);
        var waiting = new List<OrderedDish>();
        var outOfStock = new List<OrderedDish>();
        var leftovers = state.ServingSecondsLeft is { } servingLeft ? plan.HandOut(servingLeft, state.Closed) : [];

        // The orders on screen first: each stays in its kitchen while it is still possible there (owner, 2026-10-21: "the
        // recommendation should stay steady"; the soonest kitchen decides only when an order is first shown): still
        // waiting for a kitchen, in stock, a free slot there, and out there before serving ends. Another kitchen having
        // it out sooner since is no reason to move it (until 2026-10-21 an order moved when another kitchen had it out 8 s
        // sooner: 9 of the jumps on the owner's two recorded sessions).
        var pending = plan.Pending.ToList();
        var onScreen = new HashSet<int>();
        foreach (var keep in kept.Where(k => k.Table is not null))
        {
            int i = pending.FindIndex(o => o.Table == keep.Table && o.Dish.Id == keep.Dish.Id);
            if (i < 0 || !plan.InStock(pending[i].Dish)) continue;
            // A swap shown stays a swap while the dish it takes out is there to take out and the order is still out in time; as a
            // plain order it would wait behind that dish, which is what the swap was shown to avoid (test_yuanyuan5: a swap and a
            // plain order 0.3 s apart, five times).
            if (keep.Remove is { } remove && plan.TrySwap(keep.Kitchen, remove, pending[i], state.ServingSecondsLeft) && onScreen.Add(keep.Kitchen))
            {
                pending.RemoveAt(i);
                continue;
            }
            if (!plan.CanQueue(keep.Kitchen) || !onScreen.Add(keep.Kitchen)) continue;
            if (state.ServingSecondsLeft is { } left && plan.ReadyIn(keep.Kitchen, keep.Dish) > left)
            {
                onScreen.Remove(keep.Kitchen);
                continue;
            }
            plan.Queue(keep.Kitchen, pending[i].Dish, pending[i].Table);
            pending.RemoveAt(i);
        }

        foreach (var order in pending)
        {
            if (!plan.InStock(order.Dish))
            {
                outOfStock.Add(order);
                continue;
            }
            var best = plan.Soonest(order.Dish);
            var swap = plan.BestSwap(order.Dish);
            if (swap is { } s && (best is null || s.ReadyIn + ReplaceMarginSeconds < best.Value.ReadyIn))
                plan.Replace(s, order, best?.ReadyIn);
            else if (best is { } b)
                plan.Queue(b.Kitchen, order.Dish, order.Table);
            else
                waiting.Add(order);
        }

        plan.CookAhead(everySlot: fillEverySlot, [.. kept.Where(k => k.Table is null)]);

        return new DispatchAdvice(
            [.. Cooking.Select(k => new KitchenAdvice(k.Number, plan.QueueFor(k.Number), plan.ReplacesFor(k.Number)))],
            waiting, outOfStock, leftovers);
    }

    /// <summary>The kitchens that cook (<see cref="KitchenMath.Cooks"/>), by number: the only ones advised.</summary>
    private IEnumerable<Kitchen> Cooking => Kitchens.Kitchens.Where(KitchenMath.Cooks).OrderBy(k => k.Number);

    /// <summary>The opening (C16): what to cook ahead in every kitchen before the first guest orders.</summary>
    /// <param name="themeItems">What the theme favours (<see cref="InnState.ThemeItems"/>).</param>
    public DispatchAdvice Opening(IReadOnlyList<MenuDish> menu, IReadOnlyCollection<string> themeItems) =>
        Advise(new InnState([.. Cooking.Select(k => new KitchenQueue(k.Number))], Counter: [], OutForOrders: [], Carried: [], Outstanding: [], menu,
            InnState.ServingLeft(InnSimulator.SessionSeconds), QueuedAsCalled: [], themeItems), fillEverySlot: true);

    /// <summary>
    /// The kitchen that would have <paramref name="dish"/> out soonest if queued now, and in how
    /// many seconds; null when no kitchen has a free slot.
    /// </summary>
    public (int Kitchen, double ReadyIn)? SoonestOut(InnState state, Dish dish) => new Plan(this, state).Soonest(dish);

    /// <summary>The advice worked out step by step over a copy of the state.</summary>
    private sealed class Plan
    {
        private readonly DispatchAdvisor _advisor;
        private readonly InnState _state;
        private readonly Dictionary<int, List<Slot>> _queues = [];
        private readonly Dictionary<int, List<QueueAdvice>> _advice = [];
        private readonly Dictionary<int, List<ReplaceAdvice>> _replaces = [];
        private readonly Dictionary<string, int?> _stock = [];

        /// <summary>
        /// A queue slot: what's in it, its real slot (null for one advised now), whether an order claims it, and whether the
        /// player put it there because a call said so (<see cref="InnState.QueuedAsCalled"/>).
        /// </summary>
        private sealed record Slot(Dish Dish, double SecondsLeft, int? RealSlot, bool Claimed, bool Cooking, bool AsCalled = false);

        public Plan(DispatchAdvisor advisor, InnState state)
        {
            _advisor = advisor;
            _state = state;
            foreach (var kitchen in advisor.Cooking)
            {
                _queues[kitchen.Number] = [];
                _advice[kitchen.Number] = [];
                _replaces[kitchen.Number] = [];
            }
            foreach (var queue in state.Queues)
            {
                if (!_queues.TryGetValue(queue.Kitchen, out var slots))
                {
                    // A kitchen with no chef has nothing cooking: its empty queue is no matter.
                    if (queue.Dishes.Count == 0 && advisor.Kitchens.Kitchens.Any(k => k.Number == queue.Kitchen)) continue;
                    throw new ArgumentException($"A queue for kitchen {queue.Kitchen}, which isn't in play or has no chef.", nameof(state));
                }
                slots.AddRange(queue.Dishes.Select((d, i) => new Slot(d.Dish, d.SecondsLeft, i, false, i == 0)));
            }
            // The dishes queued as a call said, latest in the queue first (the one queued most recently is the one told).
            foreach (var group in state.QueuedAsCalled.GroupBy(q => (q.Kitchen, q.Dish.Id)))
            {
                if (!_queues.TryGetValue(group.Key.Kitchen, out var slots)) continue;
                int left = group.Count();
                for (int i = slots.Count - 1; i >= 0 && left > 0; i--)
                {
                    if (slots[i].Dish.Id != group.Key.Id || slots[i].AsCalled) continue;
                    slots[i] = slots[i] with { AsCalled = true };
                    left--;
                }
            }
            foreach (var menu in state.Menu) _stock[menu.Dish.Id] = menu.Stock;
            if (state.Outstanding.FirstOrDefault(o => o.Table is < 1 or > InnState.MaxTables) is { } bad)
                throw new ArgumentException($"Table {bad.Table}; the inn has tables 1 to {InnState.MaxTables}.", nameof(state));
            Pending = Cover();
        }

        /// <summary>Ordered dishes nothing covers yet: not carried, queued or out for them (nor handed a leftover, <see cref="HandOut"/>).</summary>
        public IReadOnlyList<OrderedDish> Pending { get; private set; }

        /// <summary>Orders a dish carried or out for them covers, oldest first.</summary>
        private readonly List<OrderedDish> _byHand = [];

        /// <summary>Dishes the last call can hand out: those carried or on the counter that no order claims.</summary>
        private readonly List<Dish> _unclaimed = [];

        /// <summary>
        /// Orders a kitchen still has to cook for, oldest first: queued (with when their dish is out)
        /// or not yet (QueuedOut null: they're <see cref="Pending"/>).
        /// </summary>
        private readonly List<(OrderedDish Order, double? QueuedOut)> _fromKitchen = [];

        /// <summary>
        /// The last call (owner, 2026-10-19), with <paramref name="servingLeft"/> seconds of serving
        /// left: each order whose own dish can't come out by then gets a dish carried or on the counter
        /// that no order claims, its own dish if one is there, else (oldest order first) the first one
        /// left: a wrong dish still earns money. Those not yet queued are no longer sent to a kitchen.
        /// Once <paramref name="closed"/>, each dish carried is listed first, for the order it covers, and every order whose
        /// own dish is on the counter is handed it, whether or not its own could still come out. The right dishes are
        /// listed before the wrong ones, each oldest order first.
        /// </summary>
        public List<LeftoverAdvice> HandOut(double servingLeft, bool closed)
        {
            var handed = closed ? _byHand.Select(o => new LeftoverAdvice(o.Dish, o.Table, o.Dish)).ToList() : [];
            var late = Enumerable.Range(0, _fromKitchen.Count)
                .Where(i => (_fromKitchen[i].QueuedOut ?? SoonestPossible(_fromKitchen[i].Order.Dish)) is not { } outIn || outIn > servingLeft)
                .ToList();
            var left = new List<Dish>(_unclaimed);
            var right = new SortedDictionary<int, Dish>();
            var wrong = new SortedDictionary<int, Dish>();
            // The right dishes first, then a wrong one each, oldest order first.
            foreach (int i in closed ? Enumerable.Range(0, _fromKitchen.Count) : late)
            {
                int own = left.FindIndex(d => d.Id == _fromKitchen[i].Order.Dish.Id);
                if (own < 0) continue;
                right[i] = left[own];
                left.RemoveAt(own);
            }
            // A wrong dish by the dish, not by where it stands among what's left: until 2026-10-21 each table took the first dish
            // left, so one dish leaving the hand gave every other table another (test_yuanyuan7 375.3 s: table 4 redbloom-cheesecake ->
            // eight-treasure-rice, table 1 eight-treasure-rice -> immortal-galore-of-fortune, and back 0.6 s later). Now a table's wrong dish changes only when that dish is gone.
            foreach (int i in late.Where(i => !right.ContainsKey(i)))
            {
                if (left.Count == 0) break;
                int pick = left.IndexOf(left.OrderBy(d => d.Id, StringComparer.Ordinal).First());
                wrong[i] = left[pick];
                left.RemoveAt(pick);
            }
            var pending = Pending.ToList();
            foreach (var (i, dish) in right.Concat(wrong))
            {
                var (order, queuedOut) = _fromKitchen[i];
                handed.Add(new LeftoverAdvice(dish, order.Table, order.Dish));
                if (queuedOut is null) pending.Remove(order);
            }
            Pending = pending;
            return handed;
        }

        /// <summary>
        /// The soonest <paramref name="dish"/> could be out if queued now: what's queued in a kitchen +
        /// its cook time there, in the best kitchen (a full queue only delays the queueing, not the
        /// dish: queued as a slot frees, it still comes out right after the dishes ahead of it); null
        /// with no stock left or no kitchen cooking.
        /// </summary>
        private double? SoonestPossible(Dish dish) =>
            InStock(dish) && _queues.Count > 0 ? _queues.Keys.Min(k => Left(k) + Cook(k, dish)) : null;

        /// <summary>
        /// Gives each ordered dish, oldest first, what's carried, then a dish out of a kitchen for an order, then the
        /// queued one out soonest. What's left of the queues was cooked ahead. A dish cooked ahead on the counter covers no
        /// order (owner, 2026-10-21): the counter is only estimated, and a dish taken to be there that isn't left the
        /// order without a call (test_yuanyuan4, from 250 s).
        /// </summary>
        private List<OrderedDish> Cover()
        {
            var carried = _state.Carried.Select(d => d.Id).ToList();
            var outFor = _state.OutForOrders.Select(d => d.Id).ToList();
            var pending = new List<OrderedDish>();
            foreach (var order in _state.Outstanding)
            {
                if (carried.Remove(order.Dish.Id) || outFor.Remove(order.Dish.Id))
                {
                    _byHand.Add(order);
                    continue;
                }
                var queued = _queues
                    .SelectMany(q => q.Value.Select((s, i) => (Kitchen: q.Key, Index: i, Slot: s, Out: OutIn(q.Key, i))))
                    .Where(x => !x.Slot.Claimed && x.Slot.Dish.Id == order.Dish.Id)
                    .OrderBy(x => x.Out)
                    .FirstOrDefault();
                if (queued.Slot is not null)
                {
                    _queues[queued.Kitchen][queued.Index] = queued.Slot with { Claimed = true };
                    _fromKitchen.Add((order, queued.Out));
                }
                else
                {
                    pending.Add(order);
                    _fromKitchen.Add((order, null));
                }
            }
            foreach (string id in carried) _unclaimed.Add(_state.Carried.First(d => d.Id == id));
            // The counter's dishes, less those out for an order that took them.
            var used = _state.OutForOrders.Select(d => d.Id).ToList();
            foreach (string id in outFor) used.Remove(id);
            foreach (var dish in _state.Counter)
                if (!used.Remove(dish.Id)) _unclaimed.Add(dish);
            return pending;
        }

        private double OutIn(int kitchen, int index) => _queues[kitchen].Take(index + 1).Sum(s => s.SecondsLeft);

        private double Left(int kitchen) => _queues[kitchen].Sum(s => s.SecondsLeft);

        private bool HasFreeSlot(int kitchen) => _queues[kitchen].Count < KitchenQueue.Slots;

        /// <summary>The kitchen cooks and has a free slot.</summary>
        public bool CanQueue(int kitchen) => _queues.ContainsKey(kitchen) && HasFreeSlot(kitchen);

        /// <summary>Seconds until <paramref name="dish"/> is out in <paramref name="kitchen"/> if queued there now.</summary>
        public double ReadyIn(int kitchen, Dish dish) => Left(kitchen) + Cook(kitchen, dish);

        /// <summary>A dish cooked ahead out in <paramref name="readyIn"/> seconds is still worth it: not in the last <see cref="NoCookAheadSeconds"/> of serving, and out before serving ends.</summary>
        private bool InTime(double readyIn) => _state.ServingSecondsLeft is not { } left || (left >= NoCookAheadSeconds && readyIn <= left);

        private bool AheadInTime(int kitchen, Dish dish) => InTime(Left(kitchen) + Cook(kitchen, dish));

        public bool InStock(Dish dish) => !_stock.TryGetValue(dish.Id, out int? left) || left is not { } n || n > 0;

        private void UseStock(Dish dish)
        {
            if (_stock.TryGetValue(dish.Id, out int? left) && left is { } n) _stock[dish.Id] = n - 1;
        }

        private int Cook(int kitchen, Dish dish) => _advisor.Kitchens.CookSeconds(kitchen, dish);

        private int Choice(int kitchen, Dish dish) => _advisor.Kitchens.ChoicePercent(kitchen, dish);

        /// <summary>Soonest out among kitchens with a free slot; ties to the better choice chance, then the lower number.</summary>
        public (int Kitchen, double ReadyIn)? Soonest(Dish dish) =>
            _queues.Keys
                .Where(HasFreeSlot)
                .Select(k => (Kitchen: k, ReadyIn: Left(k) + Cook(k, dish)))
                .OrderBy(x => x.ReadyIn).ThenByDescending(x => Choice(x.Kitchen, dish)).ThenBy(x => x.Kitchen)
                .Select(x => ((int, double)?)x)
                .FirstOrDefault();

        public void Queue(int kitchen, Dish dish, int? table)
        {
            double readyIn = Left(kitchen) + Cook(kitchen, dish);
            _queues[kitchen].Add(new Slot(dish, Cook(kitchen, dish), null, table is not null, _queues[kitchen].Count == 0));
            _advice[kitchen].Add(new QueueAdvice(dish, table, readyIn));
            UseStock(dish);
        }

        /// <summary>The swap that has <paramref name="dish"/> out soonest: a dish cooked ahead taken out, this one queued last.</summary>
        public (int Kitchen, int Index, double ReadyIn)? BestSwap(Dish dish)
        {
            (int Kitchen, int Index, double ReadyIn)? best = null;
            foreach (var (kitchen, slots) in _queues)
            {
                for (int i = 0; i < slots.Count; i++)
                {
                    var slot = slots[i];
                    if (slot.Claimed || slot.RealSlot is null) continue;
                    if (slot.Cooking && Cook(kitchen, slot.Dish) - slot.SecondsLeft > BarelyStartedSeconds) continue;
                    // Never undo what a call told the player to queue while it still comes out in time: it still serves an
                    // order to come (owner's live session 2026-10-21, test_yuanyuan5 at 04:15 and 00:44).
                    if (slot.AsCalled && (_state.ServingSecondsLeft is not { } serving || OutIn(kitchen, i) <= serving)) continue;
                    double readyIn = Left(kitchen) - slot.SecondsLeft + Cook(kitchen, dish);
                    if (best is null || readyIn < best.Value.ReadyIn) best = (kitchen, i, readyIn);
                }
            }
            return best;
        }

        /// <summary>
        /// The swap shown in <paramref name="kitchen"/> made again: <paramref name="remove"/> taken out (a dish cooked ahead there,
        /// the same rules as <see cref="BestSwap"/>: not claimed, barely started if cooking, not queued as a call said while it
        /// comes out in time) and <paramref name="order"/> queued instead, if that has it out before serving ends. False: it can't be.
        /// </summary>
        public bool TrySwap(int kitchen, Dish remove, OrderedDish order, double? servingLeft)
        {
            if (!_queues.TryGetValue(kitchen, out var slots)) return false;
            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                if (slot.Dish.Id != remove.Id || slot.Claimed || slot.RealSlot is null) continue;
                if (slot.Cooking && Cook(kitchen, slot.Dish) - slot.SecondsLeft > BarelyStartedSeconds) continue;
                if (slot.AsCalled && (servingLeft is not { } serving || OutIn(kitchen, i) <= serving)) continue;
                double readyIn = Left(kitchen) - slot.SecondsLeft + Cook(kitchen, order.Dish);
                if (servingLeft is { } left && readyIn > left) return false;
                Replace((kitchen, i, readyIn), order, Soonest(order.Dish)?.ReadyIn);
                return true;
            }
            return false;
        }

        public void Replace((int Kitchen, int Index, double ReadyIn) swap, OrderedDish order, double? otherwise)
        {
            var slots = _queues[swap.Kitchen];
            var removed = slots[swap.Index];
            slots.RemoveAt(swap.Index);
            if (slots.Count > 0 && removed.Cooking) slots[0] = slots[0] with { Cooking = true };
            _replaces[swap.Kitchen].Add(new ReplaceAdvice(swap.Kitchen, removed.RealSlot!.Value, removed.Dish, order.Dish, order.Table, swap.ReadyIn, otherwise));
            slots.Add(new Slot(order.Dish, Cook(swap.Kitchen, order.Dish), null, true, slots.Count == 0));
            UseStock(order.Dish);
        }

        /// <summary>
        /// Dishes cooked ahead (spare on the counter or in a queue) of each kind: only to spread what's cooked ahead over
        /// the menu, never a cap on it.
        /// </summary>
        private Dictionary<string, int> Spares()
        {
            var spares = new Dictionary<string, int>();
            var onCounter = _state.Carried.Concat(_state.Counter).Select(d => d.Id).ToList();
            foreach (var order in _state.Outstanding) onCounter.Remove(order.Dish.Id);
            foreach (string id in onCounter) spares[id] = spares.GetValueOrDefault(id) + 1;
            foreach (var slot in _queues.Values.SelectMany(s => s).Where(s => !s.Claimed))
                spares[slot.Dish.Id] = spares.GetValueOrDefault(slot.Dish.Id) + 1;
            return spares;
        }

        /// <summary>
        /// Dishes cooked ahead into the kitchens open to them, with no counter cap (owner, 2026-10-21):
        /// with <paramref name="everySlot"/> every free slot of every kitchen; otherwise kitchens nothing
        /// was ordered for this time, up to all but one slot (a new order never waits behind three).
        /// The dish: one matching the theme (the more matches the better), then the one cooked ahead least so far (spread
        /// over the menu: guests pick at random), then one whose type the kitchen's chefs speed up, then the slowest.
        /// Until 2026-10-21 the theme and the kitchen's bonuses weren't asked, and "the slowest" put the dishes a kitchen
        /// is NOT set up for first: 金丝小贝 (a dessert, 35 s) in a kitchen speeding 主食 by 40 %, whose 主食 dishes took 18-25 s. Its kitchen: where it is out first
        /// (<see cref="FirstOut"/>), as for an order (owner, 2026-10-19), not where it cooks fastest
        /// in an empty kitchen nor the lowest-numbered one: a busy kitchen is never stacked while
        /// another would have the dish out sooner.
        /// <para>A dish to cook ahead on screen (<paramref name="kept"/>) is picked once and kept: it goes first, into its
        /// own kitchen, while that kitchen has a free slot and nothing ordered for it, and the dish is in stock and out in
        /// time; the other dishes fill what's left. Leaving a slot free for orders decides only a new dish to cook ahead:
        /// one shown isn't taken back for it (test_yuanyuan3, 141.6 s).</para>
        /// </summary>
        public void CookAhead(bool everySlot, IReadOnlyList<KeptCall> kept)
        {
            var spares = Spares();
            foreach (var keep in kept)
            {
                int k = keep.Kitchen;
                if (!_queues.ContainsKey(k) || !HasFreeSlot(k) || (!everySlot && !NothingOrderedFor(k)) || !InStock(keep.Dish) || !AheadInTime(k, keep.Dish)) continue;
                Queue(k, keep.Dish, null);
                spares[keep.Dish.Id] = spares.GetValueOrDefault(keep.Dish.Id) + 1;
            }
            // Each pass fills a free slot, so this ends.
            while (true)
            {
                var open = _queues.Keys.Where(k => everySlot ? HasFreeSlot(k) : OpenToCookAhead(k)).ToList();
                var pick = _state.Menu
                    .Where(m => InStock(m.Dish))
                    .Select(m => (m.Dish, Kitchen: FirstOut(open, m.Dish)))
                    .Where(p => p.Kitchen is not null)
                    .OrderByDescending(p => InnPlanner.ThemeMatches(_state.ThemeItems, p.Dish))
                    .ThenBy(p => spares.GetValueOrDefault(p.Dish.Id))
                    .ThenByDescending(p => _advisor.Kitchens.SpeedsType(p.Kitchen!.Value, p.Dish))
                    .ThenByDescending(p => Cook(p.Kitchen!.Value, p.Dish))
                    .ThenBy(p => p.Dish.Id, StringComparer.Ordinal)
                    .FirstOrDefault();
                if (pick.Kitchen is not { } kitchen) break;
                Queue(kitchen, pick.Dish, null);
                spares[pick.Dish.Id] = spares.GetValueOrDefault(pick.Dish.Id) + 1;
            }
        }

        /// <summary>A kitchen nothing was ordered for this time (nor swapped), with more than one slot free.</summary>
        private bool OpenToCookAhead(int kitchen) => NothingOrderedFor(kitchen) && _queues[kitchen].Count < KitchenQueue.Slots - 1;

        /// <summary>Nothing was ordered for (nor swapped into) the kitchen this time.</summary>
        private bool NothingOrderedFor(int kitchen) => !_advice[kitchen].Any(a => !a.CookAhead) && _replaces[kitchen].Count == 0;

        /// <summary>
        /// Of <paramref name="kitchens"/>, the one that would have <paramref name="dish"/> out first if
        /// queued now (what it's cooking, from its progress, + the dishes waiting + this one's cook
        /// time there), among those that would have it out in time; ties to the better choice chance,
        /// then the lower number. Null when none would.
        /// </summary>
        private int? FirstOut(IEnumerable<int> kitchens, Dish dish) =>
            kitchens.Where(k => AheadInTime(k, dish))
                .OrderBy(k => Left(k) + Cook(k, dish)).ThenByDescending(k => Choice(k, dish)).ThenBy(k => k)
                .Select(k => (int?)k)
                .FirstOrDefault();

        public IReadOnlyList<QueueAdvice> QueueFor(int kitchen) => _advice[kitchen];

        public IReadOnlyList<ReplaceAdvice> ReplacesFor(int kitchen) => _replaces[kitchen];
    }
}
