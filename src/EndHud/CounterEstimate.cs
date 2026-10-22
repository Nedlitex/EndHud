namespace EndHud;

/// <summary>
/// The dishes the session takes to be on the counter: an estimate, since the counter is never read off the screen.
/// Each dish is kept with when it came out of its kitchen and whether it came out for an order still waiting (it was that
/// order's dish in the queue, so it covers that order until served) or was cooked ahead. Dishes come on as the kitchens'
/// estimate cooks them out and go as a table is read served or the tray reads them picked up; what nothing observed takes
/// off stays, which is how the estimate drifts (<see cref="InnSession"/> puts it right where a reading contradicts it).
/// </summary>
internal sealed class CounterEstimate
{
    /// <param name="Id">The dish.</param>
    /// <param name="OutAt">When it came out of its kitchen (or was put back from the hand), on the inn's clock.</param>
    /// <param name="ForOrder">It came out for an order still waiting, and covers it.</param>
    internal sealed record Entry(string Id, TimeSpan OutAt, bool ForOrder);

    // Oldest first.
    private readonly List<Entry> _entries = [];

    public IReadOnlyList<Entry> Entries => _entries;

    public int Count => _entries.Count;

    public void Clear() => _entries.Clear();

    public int ForOrders(string id) => _entries.Count(e => e.Id == id && e.ForOrder);

    public int Spares(string id) => _entries.Count(e => e.Id == id && !e.ForOrder);

    public void Put(string id, TimeSpan at, bool forOrder) => _entries.Add(new Entry(id, at, forOrder));

    /// <summary>One dish of <paramref name="id"/> off the counter, one out for an order first (the oldest); false when none is there.</summary>
    public bool TakeOff(string id)
    {
        int at = _entries.FindIndex(e => e.Id == id && e.ForOrder);
        if (at < 0) at = _entries.FindIndex(e => e.Id == id);
        if (at < 0) return false;
        _entries.RemoveAt(at);
        return true;
    }

    /// <summary>Takes this very entry off (a correction).</summary>
    public void Remove(Entry entry) => _entries.Remove(entry);

    /// <summary>
    /// Dishes out for an order beyond the orders still waiting for them were cooked ahead after all (the order gone, or
    /// misread): the newest of them turn spare. The number turned, per dish.
    /// </summary>
    public void KeepForOrders(string id, int waiting)
    {
        int excess = ForOrders(id) - waiting;
        for (int i = _entries.Count - 1; i >= 0 && excess > 0; i--)
        {
            if (_entries[i].Id != id || !_entries[i].ForOrder) continue;
            _entries[i] = _entries[i] with { ForOrder = false };
            excess--;
        }
    }

    /// <summary>The ids of the dishes out for an order, one per dish.</summary>
    public IEnumerable<string> OutForOrders => _entries.Where(e => e.ForOrder).Select(e => e.Id);

    /// <summary>The ids of every dish on it, one per dish, those out for an order first.</summary>
    public IEnumerable<string> All => OutForOrders.Concat(_entries.Where(e => !e.ForOrder).Select(e => e.Id));
}
