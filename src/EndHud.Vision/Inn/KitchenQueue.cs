namespace EndHud.Vision.Inn;

/// <summary>A dish in a kitchen's queue and how long until it's out, counting from when it starts cooking for one still waiting.</summary>
/// <param name="SecondsLeft">For the dish cooking (the first), what's left of it; for one waiting, its whole cook time in that kitchen.</param>
public sealed record QueuedDish(Dish Dish, double SecondsLeft);

/// <summary>
/// One kitchen's queue as the 派单 panel shows it: up to <see cref="Slots"/> dishes, the first
/// cooking and the others waiting their turn. Any slot can be taken out (the "−" on each); the
/// dish cooking loses what it had done (<see cref="RemovedDishKeepsProgress"/>).
/// </summary>
public sealed class KitchenQueue
{
    /// <summary>One cooking and two waiting (the 派单 panel; owner).</summary>
    public const int Slots = 3;

    /// <summary>
    /// Whether a cooking dish taken out keeps what it had cooked. Not known; taken as no, the safe
    /// assumption (plan S1, research note): a replace is only advised when the order still comes out sooner.
    /// </summary>
    public const bool RemovedDishKeepsProgress = false;

    private readonly List<QueuedDish> _dishes = [];

    /// <exception cref="ArgumentException">More than <see cref="Slots"/> dishes.</exception>
    public KitchenQueue(int kitchen, IEnumerable<QueuedDish>? dishes = null)
    {
        Kitchen = kitchen;
        foreach (var dish in dishes ?? [])
        {
            if (_dishes.Count == Slots) throw new ArgumentException($"A kitchen queues at most {Slots} dishes.", nameof(dishes));
            _dishes.Add(dish);
        }
    }

    /// <summary>The kitchen's number.</summary>
    public int Kitchen { get; }

    /// <summary>The queue in order: the first is cooking.</summary>
    public IReadOnlyList<QueuedDish> Dishes => _dishes;

    public int FreeSlots => Slots - _dishes.Count;

    public bool HasFreeSlot => FreeSlots > 0;

    /// <summary>Seconds until everything queued is out.</summary>
    public double SecondsUntilEmpty => _dishes.Sum(d => d.SecondsLeft);

    /// <summary>Seconds until the dish in <paramref name="slot"/> is out: it and everything ahead of it.</summary>
    public double SecondsUntilOut(int slot) => _dishes.Take(slot + 1).Sum(d => d.SecondsLeft);

    /// <summary>Queues <paramref name="dish"/> last; false (nothing queued) when the queue is full.</summary>
    public bool TryAdd(Dish dish, int cookSeconds)
    {
        if (!HasFreeSlot) return false;
        _dishes.Add(new QueuedDish(dish, cookSeconds));
        return true;
    }

    /// <summary>Takes the dish in <paramref name="slot"/> out. The next one starts from nothing; one taken out while cooking is lost.</summary>
    public QueuedDish RemoveAt(int slot)
    {
        var removed = _dishes[slot];
        _dishes.RemoveAt(slot);
        return removed;
    }

    /// <summary>
    /// Cooks for <paramref name="seconds"/>: the dishes that came out, in order. A finished dish
    /// leaves only while <paramref name="room"/> allows (the counter's free places); one that can't
    /// waits, done, at the head of the queue and the kitchen cooks nothing else meanwhile.
    /// </summary>
    public IReadOnlyList<Dish> Tick(double seconds, int room = int.MaxValue)
    {
        var done = new List<Dish>();
        while (_dishes.Count > 0)
        {
            var head = _dishes[0];
            if (head.SecondsLeft > seconds)
            {
                _dishes[0] = head with { SecondsLeft = head.SecondsLeft - seconds };
                break;
            }
            seconds -= head.SecondsLeft;
            if (done.Count == room)
            {
                _dishes[0] = head with { SecondsLeft = 0 };
                break;
            }
            done.Add(head.Dish);
            _dishes.RemoveAt(0);
        }
        return done;
    }
}

/// <summary>The counter (出餐台) finished dishes wait on until the player carries them to a table.</summary>
public sealed class InnCounter
{
    /// <summary>It holds 8 dishes (owner, 2026-10-18).</summary>
    public const int Capacity = 8;

    private readonly List<Dish> _dishes = [];

    public IReadOnlyList<Dish> Dishes => _dishes;

    public int Room => Capacity - _dishes.Count;

    /// <summary>False (nothing put) when the counter is full.</summary>
    public bool TryPut(Dish dish)
    {
        if (Room == 0) return false;
        _dishes.Add(dish);
        return true;
    }

    /// <summary>Takes one <paramref name="dish"/> off the counter; false when there's none.</summary>
    public bool TryTake(Dish dish)
    {
        int at = _dishes.FindIndex(d => d.Id == dish.Id);
        if (at < 0) return false;
        _dishes.RemoveAt(at);
        return true;
    }
}
