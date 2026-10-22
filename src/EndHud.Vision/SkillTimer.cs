namespace EndHud.Vision;

/// <param name="LeftShare">The part of the skill's time still to run, 0–1.</param>
/// <param name="Estimated">The ring couldn't be read this time: the time left comes from when the icon changed (shown "≈").</param>
public sealed record SkillTimerView(double LeftShare, double SecondsLeft, bool Estimated);

/// <summary>One operator's lasting battle skill across readings. Not built yet.</summary>
public sealed class SkillTimer(TimeSpan duration)
{
    public TimeSpan Duration { get; } = duration;

    public SkillTimerView? Update(SkillRingReading? reading, TimeSpan now) => null;

    public void Clear() { }
}
