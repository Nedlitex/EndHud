namespace EndHud.Vision.Inn;

/// <summary>A table's order, as the session brings it.</summary>
/// <param name="At">Seconds into the session.</param>
/// <param name="Dishes">The dishes (1 or 2); a drink needs no kitchen and isn't modelled.</param>
public sealed record InnOrder(double At, int Table, IReadOnlyList<Dish> Dishes);

/// <summary>An order and when its last dish reached the table; null when it never did.</summary>
public sealed record ServedOrder(InnOrder Order, double? ServedAt)
{
    public double? Waited => ServedAt - Order.At;
}

/// <summary>What the player does in a kitchen: queue a dish, or take one out of a slot.</summary>
public abstract record InnAction(int Kitchen);

public sealed record QueueDish(int Kitchen, Dish Dish) : InnAction(Kitchen);

public sealed record RemoveDish(int Kitchen, int Slot) : InnAction(Kitchen);

/// <summary>
/// Give a dish on the counter to a table in place of the dish it waits for (the last call,
/// <see cref="LeftoverAdvice"/>): that item counts as served. Not a kitchen's action (Kitchen 0).
/// </summary>
public sealed record GiveLeftover(int Table, Dish Dish, Dish Ordered) : InnAction(0);

/// <summary>What the player does at one moment, given the inn as it is.</summary>
public delegate IReadOnlyList<InnAction> InnPolicy(InnSimulator inn);

/// <summary>A dish carried to a table: the order it was for, the dish ordered (another when a leftover stood in), and the kitchen that cooked it.</summary>
public sealed record DeliveredDish(InnOrder Order, Dish Dish, Dish Ordered, int Kitchen)
{
    /// <summary>The dish the table ordered, not a leftover in its place.</summary>
    public bool AsOrdered => Dish.Id == Ordered.Id;
}

/// <summary>A replayed session: every order and when it was served, and the actions the inn refused.</summary>
/// <param name="Refused">Queueing into a full queue, or a dish with no stock left.</param>
public sealed record InnRun(IReadOnlyList<ServedOrder> Orders, IReadOnlyList<InnAction> Refused, int CookedAheadWasted)
{
    public int Served => Orders.Count(o => o.ServedAt is not null);

    /// <summary>
    /// Seconds a kitchen had nothing in its queue while the timer ran (5:00 to 00:00), summed over
    /// the kitchens: time the inn could have cooked and didn't (owner, 2026-10-19: "it does not keep
    /// the kitchen busy").
    /// </summary>
    public double IdleKitchenSeconds { get; init; }

    /// <summary>Every dish carried to a table, in the order they were, with the kitchen that cooked it.</summary>
    public IReadOnlyList<DeliveredDish> Delivered { get; init; } = [];
}

/// <summary>
/// How guests come and go, for a session played from its guests rather than a recorded order
/// stream (<see cref="InnSimulator.RunGuests"/>). Measured on test_yuanyuan1 (research note, plan
/// S1): the first guest sits 7 s in and orders 16 s in; while tables are free a guest comes every
/// 12 s (the first seven orders came 15, 12, 12, 12, 12, 12 s apart with tables free: guests come
/// on a clock, not as fast as tables free up); ordering takes 6–12 s, eating 10–15 s, paying 3–15 s
/// (until the player collects), and a freed table gets the next waiting guest in 3–6 s. Each is
/// taken at the middle of its range. Guests never leave, however long they wait
/// (<see cref="PatienceSeconds"/>). Not modelled: gourmands (their orders are taken as anyone's)
/// and Full Blaze's faster guests.
/// </summary>
/// <param name="Tables">1 to <see cref="InnState.MaxTables"/>.</param>
public sealed record InnGuests(int Tables)
{
    public const double FirstSeatedSeconds = 7;
    public const double IntervalSeconds = 12;
    public const double OrderingSeconds = 9;
    public const double EatingSeconds = 12.5;
    public const double PayingSeconds = 9;
    public const double NextGuestSeconds = 4.5;

    /// <summary>
    /// How long a seated guest waits for their dishes before giving up: forever. Owner, 2026-10-20, asked
    /// whether guests leave when they wait too long: "no". So a slow menu costs only the guests it keeps
    /// from being seated (a table stays taken until its order is served), never an order walked out on.
    /// </summary>
    public const double PatienceSeconds = double.PositiveInfinity;

    /// <summary>A table's turn with no wait for food: the next guest sits, orders, eats and pays.</summary>
    public const double TableTurnSeconds = NextGuestSeconds + OrderingSeconds + EatingSeconds + PayingSeconds;

    /// <summary>Guests the clock brings in a session: one at <see cref="FirstSeatedSeconds"/>, then one every <see cref="IntervalSeconds"/> until 00:00.</summary>
    public const double Arrivals = (InnSimulator.SessionSeconds - FirstSeatedSeconds) / IntervalSeconds + 1;
}

/// <summary>
/// Replays a session's order stream through the kitchens, the counter and a policy (what the player
/// does), to compare ways of dispatching. A model, so it leaves out what isn't known or isn't the
/// kitchens' doing:
/// <list type="bullet">
/// <item>Carrying is instant: a dish on the counter that a table waits for reaches it at once
/// (the player's walking isn't modelled; it's the same whichever kitchen cooked the dish).</item>
/// <item>A finished dish leaves its kitchen only when the counter has room; until then the kitchen
/// waits (an assumption: what the game does with a full counter isn't known).</item>
/// <item>A dish taken out of a queue gives no stock back (an assumption, the safe side).</item>
/// <item>Full Blaze's speed-up isn't modelled.</item>
/// </list>
/// </summary>
public sealed class InnSimulator
{
    /// <summary>The session is 5:00 (recording).</summary>
    public const double SessionSeconds = 300;

    private readonly List<OrderedDish> _outstanding = [];
    private readonly List<DeliveredDish> _delivered = [];
    // The kitchens that cooked the dishes on the counter, per dish, oldest first.
    private readonly Dictionary<string, Queue<int>> _madeBy = [];
    private double _servingAfterClose = InnState.ServingAfterCloseSeconds;

    // Kitchen-seconds with nothing queued while the timer ran, over the kitchens that can cook (a
    // kitchen with no chef can't: it isn't idle, it isn't a kitchen). Counted in Play.
    private double _idle;
    private HashSet<int> _cooking = [];
    private readonly Dictionary<string, int?> _stock;

    /// <param name="step">Seconds per step; the policy is asked once a step.</param>
    public InnSimulator(InnKitchens kitchens, IReadOnlyList<MenuDish> menu, double step = 0.5)
    {
        Kitchens = kitchens;
        Menu = menu;
        Step = step;
        Queues = [.. kitchens.Kitchens.Select(k => new KitchenQueue(k.Number))];
        _stock = menu.ToDictionary(m => m.Dish.Id, m => m.Stock);
    }

    public InnKitchens Kitchens { get; }
    public IReadOnlyList<MenuDish> Menu { get; }
    public double Step { get; }

    /// <summary>Seconds since the session started.</summary>
    public double Now { get; private set; }

    public IReadOnlyList<KitchenQueue> Queues { get; }
    public InnCounter Counter { get; } = new();

    /// <summary>Ordered dishes the tables are still waiting for, oldest first.</summary>
    public IReadOnlyList<OrderedDish> Outstanding => _outstanding;

    /// <summary>The inn as the advisor sees it: the serving time left counts the time served after the timer's 00:00.</summary>
    public InnState State => new(Queues, Counter.Dishes, [], _outstanding,
        [.. Menu.Select(m => m with { Stock = _stock[m.Dish.Id] })], Math.Max(0, SessionSeconds + _servingAfterClose - Now));

    /// <summary>
    /// Plays <paramref name="orders"/> with <paramref name="policy"/> until every order is served, or
    /// the inn stops serving: <paramref name="servingAfterClose"/> seconds after the timer's 00:00
    /// (no new guests from 00:00; the seated ones are served until then).
    /// </summary>
    /// <param name="servingAfterClose">
    /// The game's <see cref="InnState.ServingAfterCloseSeconds"/>. A longer time is only for comparing
    /// ways of dispatching over an order stream that runs past the end (the advisor is told it too).
    /// </param>
    public InnRun Run(IReadOnlyList<InnOrder> orders, InnPolicy policy, double servingAfterClose = InnState.ServingAfterCloseSeconds)
    {
        _servingAfterClose = servingAfterClose;
        var byTable = new List<(InnOrder Order, List<Dish> Missing)>();
        var served = new Dictionary<InnOrder, double>();
        var refused = new List<InnAction>();
        var queue = new Queue<InnOrder>(orders.OrderBy(o => o.At));
        StartIdleCount();

        double end = SessionSeconds + servingAfterClose;
        while (Now <= end && (queue.Count > 0 || served.Count < orders.Count))
        {
            while (queue.Count > 0 && queue.Peek().At <= Now)
            {
                var order = queue.Dequeue();
                byTable.Add((order, [.. order.Dishes]));
                _outstanding.AddRange(order.Dishes.Select(d => new OrderedDish(order.Table, d)));
                if (order.Dishes.Count == 0) served[order] = Now;
            }

            Play(policy, byTable, refused);
            Now += Step;
            if (Now > end) break; // the inn has stopped serving
            Serve(byTable, served);
        }

        int wasted = Counter.Dishes.Count + Queues.Sum(q => q.Dishes.Count);
        return new InnRun([.. orders.Select(o => new ServedOrder(o, served.TryGetValue(o, out double at) ? at : null))], refused, wasted)
        {
            Delivered = [.. _delivered],
            IdleKitchenSeconds = _idle,
        };
    }

    /// <summary>
    /// Plays a session from its guests: the n-th guest comes at <see cref="InnGuests.FirstSeatedSeconds"/>
    /// + n × <see cref="InnGuests.IntervalSeconds"/>, sits at the first free table (or waits for
    /// one), orders <paramref name="orders"/>[n] after ordering time, waits however long it takes
    /// (<see cref="InnGuests.PatienceSeconds"/>), and once every dish is served eats, pays and leaves
    /// the table for the next. No guest sits after 00:00; those seated are
    /// served until the inn stops serving (<see cref="InnState.ServingAfterCloseSeconds"/> later). So
    /// a menu that keeps guests waiting seats fewer of them: the session's orders follow from the menu
    /// and the kitchens, not from a recording.
    /// </summary>
    /// <param name="orders">What each guest orders, in the order they come (as many as may come).</param>
    /// <exception cref="ArgumentException">Tables outside 1 to <see cref="InnState.MaxTables"/>.</exception>
    public InnRun RunGuests(InnGuests guests, IReadOnlyList<IReadOnlyList<Dish>> orders, InnPolicy policy)
    {
        if (guests.Tables is < 1 or > InnState.MaxTables)
            throw new ArgumentException($"{guests.Tables} tables; the inn has 1 to {InnState.MaxTables}.", nameof(guests));
        _servingAfterClose = InnState.ServingAfterCloseSeconds;
        StartIdleCount();
        var byTable = new List<(InnOrder Order, List<Dish> Missing)>();
        var served = new Dictionary<InnOrder, double>();
        var placed = new List<InnOrder>();
        var refused = new List<InnAction>();
        var freeAt = new double[guests.Tables];
        var ordering = new List<(int Table, double OrderAt, int Guest)>();
        int next = 0;

        double end = SessionSeconds + InnState.ServingAfterCloseSeconds;
        while (Now <= end)
        {
            // Guests come on the clock and sit at the first table free; none sits after 00:00.
            while (next < orders.Count && Now <= SessionSeconds && InnGuests.FirstSeatedSeconds + next * InnGuests.IntervalSeconds <= Now)
            {
                int table = Array.FindIndex(freeAt, at => at <= Now);
                if (table < 0) break;
                freeAt[table] = double.PositiveInfinity;
                ordering.Add((table + 1, Now + InnGuests.OrderingSeconds, next++));
            }
            for (int i = ordering.Count - 1; i >= 0; i--)
            {
                var guest = ordering[i];
                if (guest.OrderAt > Now) continue;
                ordering.RemoveAt(i);
                var order = new InnOrder(Now, guest.Table, orders[guest.Guest]);
                placed.Add(order);
                byTable.Add((order, [.. order.Dishes]));
                _outstanding.AddRange(order.Dishes.Select(d => new OrderedDish(order.Table, d)));
                if (order.Dishes.Count == 0) Leave(order, Now);
            }

            Play(policy, byTable, refused);
            Now += Step;
            if (Now > end) break;
            int before = served.Count;
            Serve(byTable, served);
            if (served.Count == before) continue;
            foreach (var (order, at) in served.Where(s => s.Value == Now && double.IsPositiveInfinity(freeAt[s.Key.Table - 1])).ToList())
                Leave(order, at);
        }

        int wasted = Counter.Dishes.Count + Queues.Sum(q => q.Dishes.Count);
        return new InnRun([.. placed.Select(o => new ServedOrder(o, served.TryGetValue(o, out double at) ? at : null))], refused, wasted)
        {
            Delivered = [.. _delivered],
            IdleKitchenSeconds = _idle,
        };

        // A table served eats, pays, and takes the next guest.
        void Leave(InnOrder order, double at)
        {
            served[order] = at;
            freeAt[order.Table - 1] = at + InnGuests.EatingSeconds + InnGuests.PayingSeconds + InnGuests.NextGuestSeconds;
        }
    }

    /// <summary>One step: the policy's actions, then the kitchens cook, finished dishes going to the counter.</summary>
    private void StartIdleCount()
    {
        _idle = 0;
        _cooking = Kitchens.Kitchens.Where(KitchenMath.Cooks).Select(k => k.Number).ToHashSet();
    }

    private void Play(InnPolicy policy, List<(InnOrder Order, List<Dish> Missing)> byTable, List<InnAction> refused)
    {
        foreach (var action in policy(this))
            if (!(action is GiveLeftover give ? Give(give, byTable) : Apply(action))) refused.Add(action);

        foreach (var kitchen in Queues)
        {
            // A kitchen with nothing queued while the timer runs is time the inn could have cooked.
            if (Now < SessionSeconds && kitchen.Dishes.Count == 0 && _cooking.Contains(kitchen.Kitchen)) _idle += Math.Min(Step, SessionSeconds - Now);
            foreach (var dish in kitchen.Tick(Step, Counter.Room))
            {
                if (!Counter.TryPut(dish)) continue;
                if (!_madeBy.TryGetValue(dish.Id, out var made)) _madeBy[dish.Id] = made = new Queue<int>();
                made.Enqueue(kitchen.Kitchen);
            }
        }
    }

    /// <summary>The kitchen that cooked the oldest <paramref name="dish"/> on the counter, as it's taken off.</summary>
    private int TakenFrom(Dish dish) => _madeBy.TryGetValue(dish.Id, out var made) && made.Count > 0 ? made.Dequeue() : 0;

    private bool Apply(InnAction action)
    {
        var kitchen = Queues.First(q => q.Kitchen == action.Kitchen);
        switch (action)
        {
            case QueueDish q:
                if (_stock.TryGetValue(q.Dish.Id, out int? left) && left is <= 0) return false;
                if (!kitchen.TryAdd(q.Dish, Kitchens.CookSeconds(q.Kitchen, q.Dish))) return false;
                if (left is { } n) _stock[q.Dish.Id] = n - 1;
                return true;
            case RemoveDish r:
                if (r.Slot >= kitchen.Dishes.Count) return false;
                kitchen.RemoveAt(r.Slot);
                return true;
            default:
                return false;
        }
    }

    /// <summary>A dish on the counter given to a table in place of one it waits for (a wrong dish when they differ).</summary>
    private bool Give(GiveLeftover give, List<(InnOrder Order, List<Dish> Missing)> byTable)
    {
        int at = _outstanding.FindIndex(o => o.Table == give.Table && o.Dish.Id == give.Ordered.Id);
        if (at < 0 || !Counter.TryTake(give.Dish)) return false;
        _outstanding.RemoveAt(at);
        var (order, missing) = byTable.First(t => t.Order.Table == give.Table && t.Missing.Any(d => d.Id == give.Ordered.Id));
        missing.RemoveAt(missing.FindIndex(d => d.Id == give.Ordered.Id));
        _delivered.Add(new DeliveredDish(order, give.Dish, give.Ordered, TakenFrom(give.Dish)));
        return true;
    }

    /// <summary>Dishes on the counter go to the oldest order waiting for them.</summary>
    private void Serve(List<(InnOrder Order, List<Dish> Missing)> byTable, Dictionary<InnOrder, double> served)
    {
        foreach (var (order, missing) in byTable)
        {
            if (served.ContainsKey(order)) continue;
            for (int i = missing.Count - 1; i >= 0; i--)
            {
                if (!Counter.TryTake(missing[i])) continue;
                _delivered.Add(new DeliveredDish(order, missing[i], missing[i], TakenFrom(missing[i])));
                int at = _outstanding.FindIndex(o => o.Table == order.Table && o.Dish.Id == missing[i].Id);
                _outstanding.RemoveAt(at);
                missing.RemoveAt(i);
            }
            if (missing.Count == 0) served[order] = Now;
        }
    }

    /// <summary>The player follows <paramref name="advisor"/>: every swap, dish to send and dish to cook ahead, and every leftover the last call hands out.</summary>
    /// <param name="openingUntil">Until this many seconds in, every free slot is filled (the opening, C16): by default the first second.</param>
    /// <param name="actEvery">
    /// How often the player acts on the advice, in seconds; by default every step. A player in the
    /// game opens the 派单 panel now and then and queues what the advice says at that moment.
    /// </param>
    public static InnPolicy Following(DispatchAdvisor advisor, double openingUntil = 1, double actEvery = 0)
    {
        double? actedAt = null;
        return inn =>
    {
        if (actedAt is { } last && inn.Now - last < actEvery) return [];
        actedAt = inn.Now;
        var advice = advisor.Advise(inn.State, fillEverySlot: inn.Now < openingUntil);
        // The last call's wrong dishes; the right ones are served off the counter anyway.
        var actions = new List<InnAction>(advice.Leftovers.Where(l => !l.Matches).Select(l => new GiveLeftover(l.Table, l.Dish, l.Ordered)));
        foreach (var kitchen in advice.Kitchens)
        {
            // Highest slot first, so taking one out doesn't move the others still to be taken.
            foreach (var replace in kitchen.Replaces.OrderByDescending(r => r.Slot)) actions.Add(new RemoveDish(kitchen.Kitchen, replace.Slot));
            foreach (var replace in kitchen.Replaces) actions.Add(new QueueDish(kitchen.Kitchen, replace.Put));
            actions.AddRange(kitchen.Queue.Select(q => new QueueDish(kitchen.Kitchen, q.Dish)));
        }
        return actions;
    };
    }
}
