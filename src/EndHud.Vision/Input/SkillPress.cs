namespace EndHud.Vision.Input;

/// <summary>
/// The player cast a battle skill: which squad slot (1-4) and when, on the reading clock (the app's
/// monotonic clock; a recording's own time in endhud-cli). Nothing else about the key or button
/// pressed exists anywhere: this is the input code's only output.
/// </summary>
public readonly record struct SkillPress(int Slot, TimeSpan At);

/// <summary>Where presses come from: the keyboard/controller reader, or a file of presses for a recording.</summary>
public interface ISkillPressSource
{
    /// <summary>Every press made at or before <paramref name="readAt"/>, oldest first, removed from the source.</summary>
    IReadOnlyList<SkillPress> TakeUntil(TimeSpan readAt);
}
