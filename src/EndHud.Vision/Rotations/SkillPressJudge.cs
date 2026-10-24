using EndHud.Vision.Input;

namespace EndHud.Vision.Rotations;

/// <summary>What became of a press (<see cref="SkillPressJudge"/>).</summary>
public enum PressVerdict
{
    /// <summary>The screen says it could cast: it counts as the skill used.</summary>
    Counted,

    /// <summary>The reading before it wasn't in battle (no skill-point bar): a digit typed in a chat window or a menu.</summary>
    NotInBattle,

    /// <summary>No operator in that slot.</summary>
    EmptySlot,

    /// <summary>The slot's battle-skill button was cooling down.</summary>
    Cooling,

    /// <summary>The slot already cast since that reading: a mashed key casts once.</summary>
    Repeat,

    /// <summary>No skill point left once the presses already counted had spent theirs.</summary>
    NoSkillPoints,
}

/// <summary>A press and what became of it.</summary>
public sealed record JudgedPress(SkillPress Press, PressVerdict Verdict);

/// <summary>
/// Did a press cast the skill? Judged against the reading taken before it: in combat, the slot's
/// battle-skill button not cooling down, a skill point left once the presses already counted
/// since that reading have spent theirs, and at most one cast per slot between two readings (a
/// mashed key casts once). A battle skill costs a skill point (<see cref="StepAction.BattleSkill"/>).
/// With a controller the button isn't read (null): the skill points decide.
/// </summary>
public static class SkillPressJudge
{
    public static IReadOnlyList<JudgedPress> Judge(PartyState? before, IReadOnlyList<SkillPress> presses)
    {
        var judged = new List<JudgedPress>(presses.Count);
        double points = before?.SkillPoints ?? 0;
        var cast = new HashSet<int>();
        foreach (var press in presses.OrderBy(p => p.At))
        {
            var slot = before is { InCombat: true } && press.Slot >= 1 && press.Slot <= before.Slots.Count ? before.Slots[press.Slot - 1] : null;
            var verdict = before is not { InCombat: true } ? PressVerdict.NotInBattle
                : slot is null || slot.Empty ? PressVerdict.EmptySlot
                : slot.BattleSkill == BattleSkillState.Cooling ? PressVerdict.Cooling
                : cast.Contains(press.Slot) ? PressVerdict.Repeat
                : points < 1 - 1e-6 ? PressVerdict.NoSkillPoints
                : PressVerdict.Counted;
            if (verdict == PressVerdict.Counted)
            {
                cast.Add(press.Slot);
                points -= 1;
            }
            judged.Add(new JudgedPress(press, verdict));
        }
        return judged;
    }
}
