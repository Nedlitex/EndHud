namespace EndHud.Vision.Rotations;

/// <summary>Every condition inside conditions: groups ("all"/"any") and "not" opened up, at any depth.</summary>
public static class ConditionWalk
{
    /// <summary>The condition, then everything inside it (a group or a "not" is listed before its parts).</summary>
    public static IEnumerable<StateCondition> Flatten(StateCondition condition) => condition switch
    {
        AllCondition all => all.Of.SelectMany(Flatten).Prepend(condition),
        AnyCondition any => any.Of.SelectMany(Flatten).Prepend(condition),
        NotCondition not => Flatten(not.Of).Prepend(condition),
        _ => [condition],
    };

    public static IEnumerable<StateCondition> Flatten(IEnumerable<StateCondition> conditions) => conditions.SelectMany(Flatten);

    /// <summary>Every condition of a rotation: phases' entry conditions and steps' expected states.</summary>
    public static IEnumerable<StateCondition> Of(IEnumerable<RotationPhase> phases) =>
        Flatten(phases.SelectMany(p => p.When.Concat(p.Steps.SelectMany(s => s.Expect))));

    /// <summary>Does anything in this rotation ask about the boss's statuses (so they must be read)?</summary>
    public static bool NeedsBossStatuses(Rotation? rotation) => rotation is not null && Of(rotation.Phases).Any(c => c is BossStatusCondition);

    /// <summary>The comparison a condition uses, if it has one.</summary>
    public static Comparison? ComparisonOf(StateCondition c) => c switch
    {
        SkillPointsCondition s => s.Is,
        UltimateChargeCondition u => u.Is,
        ComboChargeCondition k => k.Is,
        CounterCondition n => n.Is,
        PhaseShownCondition p => p.Is,
        PhasePlayedCondition p => p.Is,
        _ => null,
    };

    /// <summary>
    /// Is this condition made of nothing but "phase played" parts (a "not" of one, a group whose every
    /// part is one, at any depth)? While the battle-skill keys aren't read such a condition is ignored,
    /// as if it weren't there. An empty group isn't: it is a problem of its own.
    /// </summary>
    public static bool OnlyPlayed(StateCondition condition) => false;
}
