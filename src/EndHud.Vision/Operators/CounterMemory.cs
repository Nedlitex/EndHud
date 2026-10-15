namespace EndHud.Vision.Operators;

/// <param name="Value">The count as last seen; null when it hasn't been seen yet.</param>
/// <param name="Live">Seen in this reading. False: it's the last value seen, from when the operator was controlled.</param>
public sealed record RememberedCounter(string OperatorId, CounterDefinition Counter, int? Value, bool Live);

/// <summary>
/// Every squad operator's own counters (<see cref="OperatorCounters"/>) across readings. The game
/// draws a counter only while its operator is controlled, so after a switch the last value seen is
/// kept, marked as not live: the count may have changed since. An operator who leaves the squad is
/// forgotten; <see cref="Clear"/> forgets everyone (the battle ended).
/// </summary>
public sealed class CounterMemory
{
    private readonly Dictionary<(string OperatorId, string CounterId), int> _last = [];

    /// <summary>The squad's counters in slot order, after taking in this reading.</summary>
    public IReadOnlyList<RememberedCounter> Update(PartyState state)
    {
        var squad = state.Slots.Select(s => s.Operator?.Operator.Id).OfType<string>().ToList();
        foreach (var key in _last.Keys.Where(k => !squad.Contains(k.OperatorId)).ToList()) _last.Remove(key);

        // A null reading (an effect over the counter) isn't a new value: the last one stays, not live.
        var live = new HashSet<(string, string)>();
        foreach (var reading in state.Counters)
        {
            if (reading.Value is not { } value || !squad.Contains(reading.OperatorId)) continue;
            _last[(reading.OperatorId, reading.CounterId)] = value;
            live.Add((reading.OperatorId, reading.CounterId));
        }

        var counters = new List<RememberedCounter>();
        foreach (string id in squad)
        foreach (var counter in OperatorCounters.For(id))
            counters.Add(new RememberedCounter(id, counter, _last.TryGetValue((id, counter.Id), out int v) ? v : null, live.Contains((id, counter.Id))));
        return counters;
    }

    public void Clear() => _last.Clear();
}
