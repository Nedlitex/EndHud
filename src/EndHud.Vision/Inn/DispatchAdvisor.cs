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
/// <param name="Outstanding">Ordered and not yet given to the table, oldest first. Drinks aren't in it: they need no kitchen.</param>
/// <param name="ServingSecondsLeft">
/// Seconds the inn still serves: the timer's time left + <see cref="ServingAfterCloseSeconds"/> before
/// it closes, and what's left of those 18 s after (<see cref="ServingLeft"/>, <see cref="ServingLeftAfterClose"/>);
/// null when the timer hasn't been read. No default: a caller says what it knows.
/// </param>
public sealed record InnState(IReadOnlyList<KitchenQueue> Queues, IReadOnlyList<Dish> Counter, IReadOnlyList<Dish> Carried,
    IReadOnlyList<OrderedDish> Outstanding, IReadOnlyList<MenuDish> Menu, double? ServingSecondsLeft)
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
/// <item><b>Dispatch:</b> each dish a table is still waiting for, not already on the counter, carried
/// or queued, goes to the kitchen that has it out soonest (what's queued there + its cook time
/// there), among kitchens with a free slot; on a tie, the one with the better choice-dish chance.</item>
/// <item><b>Cook ahead:</b> a kitchen with nothing ordered for it gets the menu dishes that save
/// the most waiting: guests pick at random, so a dish's worth is its chance of being ordered next
/// times its cook time there, and dishes not yet cooked ahead come before a second of one that
/// is (spread over the menu). Never one with no stock, never more than the counter has room for.
/// Normally a kitchen is filled to all but one slot, so a new order never waits behind three dishes;
/// at the session's start and when Full Blaze is near, every free slot is filled.</item>
/// <item><b>Replace:</b> a dish cooked ahead (one no order claims) is suggested for removal only
/// when queueing the order in its place has it out sooner than any kitchen could otherwise, by more
/// than <see cref="ReplaceMarginSeconds"/>. A dish cooking is lost when taken out, so one is only
/// worth taking out when it has barely started (<see cref="BarelyStartedSeconds"/>).</item>
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
/// only for a drink is never handed a dish (it wouldn't be served). Once closed, the dishes on the
/// counter for the tables that ordered them are listed too.</item>
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

    /// <summary>The advice for <paramref name="state"/>.</summary>
    /// <param name="fillEverySlot">
    /// Cook ahead into every free slot of every kitchen, not only idle ones: at the session's start
    /// (C16) and from Full Blaze's "get ready" (<see cref="FullBlazeAdvice.GetReady"/>).
    /// </param>
    /// <exception cref="ArgumentException">A queue for a kitchen not in play, or a table out of range.</exception>
    public DispatchAdvice Advise(InnState state, bool fillEverySlot = false)
    {
        var plan = new Plan(this, state);
        var waiting = new List<OrderedDish>();
        var outOfStock = new List<OrderedDish>();
        var leftovers = state.ServingSecondsLeft is { } servingLeft ? plan.HandOut(servingLeft, state.Closed) : [];

        foreach (var order in plan.Pending)
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

        if (fillEverySlot) plan.CookAheadEverywhere();
        else plan.CookAheadInIdleKitchens();

        return new DispatchAdvice(
            [.. Cooking.Select(k => new KitchenAdvice(k.Number, plan.QueueFor(k.Number), plan.ReplacesFor(k.Number)))],
            waiting, outOfStock, leftovers);
    }

    /// <summary>The kitchens that cook (<see cref="KitchenMath.Cooks"/>), by number: the only ones advised.</summary>
    private IEnumerable<Kitchen> Cooking => Kitchens.Kitchens.Where(KitchenMath.Cooks).OrderBy(k => k.Number);

    /// <summary>The opening (C16): what to cook ahead in every kitchen before the first guest orders.</summary>
    public DispatchAdvice Opening(IReadOnlyList<MenuDish> menu) =>
        Advise(new InnState([.. Cooking.Select(k => new KitchenQueue(k.Number))], [], [], [], menu, InnState.ServingLeft(InnSimulator.SessionSeconds)), fillEverySlot: true);

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
        private int _counterUsed;

        /// <summary>A queue slot: what's in it, its real slot (null for one advised now), and whether an order claims it.</summary>
        private sealed record Slot(Dish Dish, double SecondsLeft, int? RealSlot, bool Claimed, bool Cooking);

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
            foreach (var menu in state.Menu) _stock[menu.Dish.Id] = menu.Stock;
            if (state.Outstanding.FirstOrDefault(o => o.Table is < 1 or > InnState.MaxTables) is { } bad)
                throw new ArgumentException($"Table {bad.Table}; the inn has tables 1 to {InnState.MaxTables}.", nameof(state));
            Pending = Cover();
        }

        /// <summary>Ordered dishes nothing covers yet: not carried, on the counter or queued (nor handed a leftover, <see cref="HandOut"/>).</summary>
        public IReadOnlyList<OrderedDish> Pending { get; private set; }

        /// <summary>Orders a dish carried or on the counter covers, oldest first.</summary>
        private readonly List<OrderedDish> _byHand = [];

        /// <summary>Dishes carried or on the counter that no order claims.</summary>
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
        /// Once <paramref name="closed"/>, each dish carried or on the counter is listed first, for the order it covers.
        /// </summary>
        public List<LeftoverAdvice> HandOut(double servingLeft, bool closed)
        {
            var handed = closed ? _byHand.Select(o => new LeftoverAdvice(o.Dish, o.Table, o.Dish)).ToList() : [];
            var late = Enumerable.Range(0, _fromKitchen.Count)
                .Where(i => (_fromKitchen[i].QueuedOut ?? SoonestPossible(_fromKitchen[i].Order.Dish)) is not { } outIn || outIn > servingLeft)
                .ToList();
            var left = new List<Dish>(_unclaimed);
            var given = new SortedDictionary<int, Dish>();
            // The right dishes first, then a wrong one each, oldest order first.
            foreach (int i in late)
            {
                int own = left.FindIndex(d => d.Id == _fromKitchen[i].Order.Dish.Id);
                if (own < 0) continue;
                given[i] = left[own];
                left.RemoveAt(own);
            }
            foreach (int i in late.Where(i => !given.ContainsKey(i)))
            {
                if (left.Count == 0) break;
                given[i] = left[0];
                left.RemoveAt(0);
            }
            var pending = Pending.ToList();
            foreach (var (i, dish) in given)
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
        /// Gives each ordered dish, oldest first, what's carried, then the counter, then the queued
        /// one out soonest. What's left of the counter and the queues was cooked ahead.
        /// </summary>
        private List<OrderedDish> Cover()
        {
            var carried = _state.Carried.Select(d => d.Id).ToList();
            var counter = _state.Counter.Select(d => d.Id).ToList();
            var pending = new List<OrderedDish>();
            foreach (var order in _state.Outstanding)
            {
                if (carried.Remove(order.Dish.Id) || counter.Remove(order.Dish.Id))
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
            _counterUsed = _state.Counter.Count;
            var byHand = _state.Carried.Concat(_state.Counter).ToList();
            foreach (string id in carried.Concat(counter)) _unclaimed.Add(byHand.First(d => d.Id == id));
            return pending;
        }

        private double OutIn(int kitchen, int index) => _queues[kitchen].Take(index + 1).Sum(s => s.SecondsLeft);

        private double Left(int kitchen) => _queues[kitchen].Sum(s => s.SecondsLeft);

        private bool HasFreeSlot(int kitchen) => _queues[kitchen].Count < KitchenQueue.Slots;

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
                    double readyIn = Left(kitchen) - slot.SecondsLeft + Cook(kitchen, dish);
                    if (best is null || readyIn < best.Value.ReadyIn) best = (kitchen, i, readyIn);
                }
            }
            return best;
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

        /// <summary>Dishes cooked ahead (spare on the counter or in a queue) of each kind, and the counter's room left for more.</summary>
        private (Dictionary<string, int> Spares, int Room) Spares()
        {
            var spares = new Dictionary<string, int>();
            var onCounter = _state.Carried.Concat(_state.Counter).Select(d => d.Id).ToList();
            foreach (var order in _state.Outstanding) onCounter.Remove(order.Dish.Id);
            foreach (string id in onCounter) spares[id] = spares.GetValueOrDefault(id) + 1;
            int queuedSpare = 0;
            foreach (var slot in _queues.Values.SelectMany(s => s).Where(s => !s.Claimed))
            {
                spares[slot.Dish.Id] = spares.GetValueOrDefault(slot.Dish.Id) + 1;
                queuedSpare++;
            }
            return (spares, InnCounter.Capacity - _counterUsed - queuedSpare);
        }

        /// <summary>The menu dish worth cooking ahead in <paramref name="kitchen"/>, or null for none.</summary>
        private Dish? WorthCookingAhead(int kitchen, Dictionary<string, int> spares)
        {
            int menuSize = _state.Menu.Count;
            if (menuSize == 0) return null;
            double orderedNext = 1.0 / menuSize; // guests pick dishes at random (owner)
            return _state.Menu
                .Where(m => InStock(m.Dish) && AheadInTime(kitchen, m.Dish))
                .OrderBy(m => spares.GetValueOrDefault(m.Dish.Id))
                .ThenByDescending(m => orderedNext * Cook(kitchen, m.Dish))
                .ThenBy(m => m.Dish.Id, StringComparer.Ordinal)
                .Select(m => m.Dish)
                .FirstOrDefault();
        }

        /// <summary>Kitchens nothing was ordered for get dishes cooked ahead, up to all but one slot.</summary>
        public void CookAheadInIdleKitchens()
        {
            var (spares, room) = Spares();
            foreach (int kitchen in _queues.Keys.Order())
            {
                if (_advice[kitchen].Any(a => !a.CookAhead) || _replaces[kitchen].Count > 0) continue;
                while (room > 0 && _queues[kitchen].Count < KitchenQueue.Slots - 1 && WorthCookingAhead(kitchen, spares) is { } dish)
                {
                    Queue(kitchen, dish, null);
                    spares[dish.Id] = spares.GetValueOrDefault(dish.Id) + 1;
                    room--;
                }
            }
        }

        /// <summary>
        /// Every free slot of every kitchen, as many as the counter has room for: the dish worth
        /// most next (spread over the menu, slowest first), each in the free kitchen that cooks it fastest.
        /// </summary>
        public void CookAheadEverywhere()
        {
            var (spares, room) = Spares();
            while (room > 0)
            {
                var free = _queues.Keys.Where(HasFreeSlot).Order().ToList();
                if (free.Count == 0) break;
                // The dish: fewest already cooked ahead, then slowest where it cooks fastest; only one
                // some free kitchen would have out in time.
                var dish = _state.Menu
                    .Where(m => InStock(m.Dish) && free.Any(k => AheadInTime(k, m.Dish)))
                    .Select(m => m.Dish)
                    .OrderBy(d => spares.GetValueOrDefault(d.Id))
                    .ThenByDescending(d => free.Min(k => Cook(k, d)))
                    .ThenBy(d => d.Id, StringComparer.Ordinal)
                    .FirstOrDefault();
                if (dish is null) break;
                int kitchen = free
                    .Where(k => AheadInTime(k, dish))
                    .OrderBy(k => Cook(k, dish)).ThenBy(k => Left(k) + Cook(k, dish)).ThenByDescending(k => Choice(k, dish)).ThenBy(k => k)
                    .First();
                Queue(kitchen, dish, null);
                spares[dish.Id] = spares.GetValueOrDefault(dish.Id) + 1;
                room--;
            }
        }

        public IReadOnlyList<QueueAdvice> QueueFor(int kitchen) => _advice[kitchen];

        public IReadOnlyList<ReplaceAdvice> ReplacesFor(int kitchen) => _replaces[kitchen];
    }
}
