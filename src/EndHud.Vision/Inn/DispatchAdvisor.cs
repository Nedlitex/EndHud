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
public sealed record InnState(IReadOnlyList<KitchenQueue> Queues, IReadOnlyList<Dish> Counter, IReadOnlyList<Dish> Carried,
    IReadOnlyList<OrderedDish> Outstanding, IReadOnlyList<MenuDish> Menu)
{
    /// <summary>The inn has at most six tables (owner).</summary>
    public const int MaxTables = 6;
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

/// <param name="Waiting">Ordered dishes no kitchen has room for now (and no swap helps).</param>
/// <param name="OutOfStock">Ordered dishes that can't be cooked: none left.</param>
public sealed record DispatchAdvice(IReadOnlyList<KitchenAdvice> Kitchens, IReadOnlyList<OrderedDish> Waiting, IReadOnlyList<OrderedDish> OutOfStock)
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
            [.. Kitchens.Kitchens.Select(k => new KitchenAdvice(k.Number, plan.QueueFor(k.Number), plan.ReplacesFor(k.Number)))],
            waiting, outOfStock);
    }

    /// <summary>The opening (C16): what to cook ahead in every kitchen before the first guest orders.</summary>
    public DispatchAdvice Opening(IReadOnlyList<MenuDish> menu) =>
        Advise(new InnState([.. Kitchens.Kitchens.Select(k => new KitchenQueue(k.Number))], [], [], [], menu), fillEverySlot: true);

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
            foreach (var kitchen in advisor.Kitchens.Kitchens)
            {
                _queues[kitchen.Number] = [];
                _advice[kitchen.Number] = [];
                _replaces[kitchen.Number] = [];
            }
            foreach (var queue in state.Queues)
            {
                if (!_queues.TryGetValue(queue.Kitchen, out var slots))
                    throw new ArgumentException($"A queue for kitchen {queue.Kitchen}, which isn't in play.", nameof(state));
                slots.AddRange(queue.Dishes.Select((d, i) => new Slot(d.Dish, d.SecondsLeft, i, false, i == 0)));
            }
            foreach (var menu in state.Menu) _stock[menu.Dish.Id] = menu.Stock;
            if (state.Outstanding.FirstOrDefault(o => o.Table is < 1 or > InnState.MaxTables) is { } bad)
                throw new ArgumentException($"Table {bad.Table}; the inn has tables 1 to {InnState.MaxTables}.", nameof(state));
            Pending = Cover();
        }

        /// <summary>Ordered dishes nothing covers yet: not carried, on the counter or queued.</summary>
        public IReadOnlyList<OrderedDish> Pending { get; }

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
                if (carried.Remove(order.Dish.Id) || counter.Remove(order.Dish.Id)) continue;
                var queued = _queues
                    .SelectMany(q => q.Value.Select((s, i) => (Kitchen: q.Key, Index: i, Slot: s, Out: OutIn(q.Key, i))))
                    .Where(x => !x.Slot.Claimed && x.Slot.Dish.Id == order.Dish.Id)
                    .OrderBy(x => x.Out)
                    .FirstOrDefault();
                if (queued.Slot is not null) _queues[queued.Kitchen][queued.Index] = queued.Slot with { Claimed = true };
                else pending.Add(order);
            }
            _counterUsed = _state.Counter.Count;
            return pending;
        }

        private double OutIn(int kitchen, int index) => _queues[kitchen].Take(index + 1).Sum(s => s.SecondsLeft);

        private double Left(int kitchen) => _queues[kitchen].Sum(s => s.SecondsLeft);

        private bool HasFreeSlot(int kitchen) => _queues[kitchen].Count < KitchenQueue.Slots;

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
                .Where(m => InStock(m.Dish))
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
                // The dish: fewest already cooked ahead, then slowest where it cooks fastest.
                var dish = _state.Menu
                    .Where(m => InStock(m.Dish))
                    .Select(m => m.Dish)
                    .OrderBy(d => spares.GetValueOrDefault(d.Id))
                    .ThenByDescending(d => free.Min(k => Cook(k, d)))
                    .ThenBy(d => d.Id, StringComparer.Ordinal)
                    .FirstOrDefault();
                if (dish is null) break;
                int kitchen = free
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
