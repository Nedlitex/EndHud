namespace EndHud.Vision;

/// <param name="LeftShare">The part of the skill's time still to run, 0–1.</param>
/// <param name="Estimated">The ring couldn't be read this time: the time left comes from when the icon changed (shown "≈").</param>
public sealed record SkillTimerView(double LeftShare, double SecondsLeft, bool Estimated);

/// <summary>
/// One operator's lasting battle skill across readings. Shown only on readings where the changed
/// icon is seen. The ring, when read, gives the time left and re-anchors the start; when it can't
/// be read, the time left is estimated from the start (the icon change, or the ring's last anchor)
/// and the duration. The icon going back (or the button cooling) ends it.
/// </summary>
public sealed class SkillTimer(TimeSpan duration)
{
    private TimeSpan? _startedAt;

    public TimeSpan Duration { get; } = duration;

    /// <returns>What to show for this reading; null when nothing is (not running, or not seen).</returns>
    public SkillTimerView? Update(SkillRingReading? reading, TimeSpan now)
    {
        switch (reading?.Icon)
        {
            case SkillButtonIcon.Changed:
                if (reading.LeftShare is { } left)
                {
                    _startedAt = now - (1 - left) * Duration; // the ring is the better clock
                    return new SkillTimerView(left, left * Duration.TotalSeconds, Estimated: false);
                }
                _startedAt ??= now;                            // first sight of the swirl
                double share = 1 - (now - _startedAt.Value) / Duration;
                return share > 0 ? new SkillTimerView(share, share * Duration.TotalSeconds, Estimated: true) : null;
            case SkillButtonIcon.Original or SkillButtonIcon.Cooling:
                _startedAt = null;                             // it ended
                return null;
            default:
                return null;                                   // not seen: not shown; the start is kept
        }
    }

    /// <summary>Forget the running skill (the fight ended).</summary>
    public void Clear() => _startedAt = null;
}

/// <summary>
/// The squad's lasting battle skills (Si's), one <see cref="SkillTimer"/> per operator, across
/// readings: what the gauges show for each now. One per game window; cleared when a battle ends.
/// </summary>
public sealed class SkillTimers
{
    private readonly Dictionary<string, SkillTimer> _timers = [];

    /// <returns>Each running skill's view, by operator id.</returns>
    public Dictionary<string, SkillTimerView> Update(PartyState state, TimeSpan now)
    {
        var views = new Dictionary<string, SkillTimerView>();
        foreach (string id in state.Slots.Select(s => s.Operator?.Operator.Id).OfType<string>().Where(id => id == "Si").Distinct())
        {
            if (!_timers.TryGetValue(id, out var timer)) _timers[id] = timer = new SkillTimer(SkillRing.SiDuration);
            if (timer.Update(state.SkillRings.FirstOrDefault(r => r.OperatorId == id), now) is { } view) views[id] = view;
        }
        return views;
    }

    /// <summary>Forget every running skill (the battle ended).</summary>
    public void Clear() => _timers.Clear();
}
