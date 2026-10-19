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

/// <summary>What the player does at one moment, given the inn as it is.</summary>
public delegate IReadOnlyList<InnAction> InnPolicy(InnSimulator inn);

/// <summary>A replayed session: every order and when it was served, and the actions the inn refused.</summary>
/// <param name="Refused">Queueing into a full queue, or a dish with no stock left.</param>
public sealed record InnRun(IReadOnlyList<ServedOrder> Orders, IReadOnlyList<InnAction> Refused, int CookedAheadWasted)
{
    public int Served => Orders.Count(o => o.ServedAt is not null);
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

    /// <summary>The inn as the advisor sees it.</summary>
    public InnState State => new(Queues, Counter.Dishes, [], _outstanding,
        [.. Menu.Select(m => m with { Stock = _stock[m.Dish.Id] })]);

    /// <summary>
    /// Plays <paramref name="orders"/> with <paramref name="policy"/> until every order is served, or
    /// <paramref name="overtime"/> seconds after the session's end (the inn closes to new guests at
    /// 0:00 but serves those seated).
    /// </summary>
    public InnRun Run(IReadOnlyList<InnOrder> orders, InnPolicy policy, double overtime = 120)
    {
        var byTable = new List<(InnOrder Order, List<Dish> Missing)>();
        var served = new Dictionary<InnOrder, double>();
        var refused = new List<InnAction>();
        var queue = new Queue<InnOrder>(orders.OrderBy(o => o.At));

        while (Now <= SessionSeconds + overtime && (queue.Count > 0 || served.Count < orders.Count))
        {
            while (queue.Count > 0 && queue.Peek().At <= Now)
            {
                var order = queue.Dequeue();
                byTable.Add((order, [.. order.Dishes]));
                _outstanding.AddRange(order.Dishes.Select(d => new OrderedDish(order.Table, d)));
                if (order.Dishes.Count == 0) served[order] = Now;
            }

            foreach (var action in policy(this))
                if (!Apply(action)) refused.Add(action);

            foreach (var kitchen in Queues)
                foreach (var dish in kitchen.Tick(Step, Counter.Room))
                    Counter.TryPut(dish);

            Now += Step;
            Serve(byTable, served);
        }

        int wasted = Counter.Dishes.Count + Queues.Sum(q => q.Dishes.Count);
        return new InnRun([.. orders.Select(o => new ServedOrder(o, served.TryGetValue(o, out double at) ? at : null))], refused, wasted);
    }

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

    /// <summary>Dishes on the counter go to the oldest order waiting for them.</summary>
    private void Serve(List<(InnOrder Order, List<Dish> Missing)> byTable, Dictionary<InnOrder, double> served)
    {
        foreach (var (order, missing) in byTable)
        {
            if (served.ContainsKey(order)) continue;
            for (int i = missing.Count - 1; i >= 0; i--)
            {
                if (!Counter.TryTake(missing[i])) continue;
                int at = _outstanding.FindIndex(o => o.Table == order.Table && o.Dish.Id == missing[i].Id);
                _outstanding.RemoveAt(at);
                missing.RemoveAt(i);
            }
            if (missing.Count == 0) served[order] = Now;
        }
    }

    /// <summary>The player follows <paramref name="advisor"/>: every swap, dish to send and dish to cook ahead.</summary>
    /// <param name="openingUntil">Until this many seconds in, every free slot is filled (the opening, C16): by default the first second.</param>
    public static InnPolicy Following(DispatchAdvisor advisor, double openingUntil = 1) => inn =>
    {
        var advice = advisor.Advise(inn.State, fillEverySlot: inn.Now < openingUntil);
        var actions = new List<InnAction>();
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
