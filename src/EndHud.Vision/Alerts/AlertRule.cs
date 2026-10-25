using System.Text.Json.Serialization;
using EndHud.Vision.Rotations;

namespace EndHud.Vision.Alerts;

/// <summary>
/// One alert rule: when every condition holds (they can be and/or groups and "not", as in
/// rotations), a pop-up with <see cref="Message"/> shows. Global: for every fight and every squad.
/// </summary>
/// <param name="Id">Kept for the rule's whole life: correcting a rule rewrites it under the same id.</param>
/// <param name="When">All must hold. Every condition kind a rotation has (<see cref="ConditionCatalogue"/>, the same
/// checks: <see cref="ConditionRules"/>). "Phase shown" and "phase played" name a phase of one of the player's
/// rotations: known while that rotation is the one followed.</param>
/// <param name="Message">What the pop-up says; empty: the condition itself in words.</param>
public sealed record AlertRule(string Id, IReadOnlyList<StateCondition> When, string Message)
{
    public IReadOnlyList<StateCondition> When { get; init; } = When ?? [];

    public string Message { get; init; } = Message ?? "";

    /// <summary>A new rule, under a new id.</summary>
    public static AlertRule New(IReadOnlyList<StateCondition> when, string message) => new(NewId(), when, message);

    public static string NewId() => Guid.NewGuid().ToString("N");

    /// <summary>The same conditions and the same words (the id aside): what an import skips as a duplicate.</summary>
    public bool SameAs(AlertRule other) => When.SequenceEqual(other.When) && Message == other.Message;

    public bool Equals(AlertRule? other) => other is not null && Id == other.Id && SameAs(other);

    public override int GetHashCode() => HashCode.Combine(Id, Message, When.Count);

    /// <summary>What's wrong with it, if anything (the editor turns these into sentences).</summary>
    public IReadOnlyList<AlertProblem> Problems()
    {
        var problems = new List<AlertProblem>();
        if (When.Count == 0) problems.Add(AlertProblem.NoConditions);
        // The shared check, with no squad (an operator not in the fight is unknown) and any rotation's phase.
        foreach (var problem in ConditionRules.Check(When, ConditionHost.Alert))
        {
            var kind = problem.Kind switch
            {
                ConditionProblemKind.EmptyGroup => AlertProblem.EmptyConditionGroup,
                ConditionProblemKind.SkillTimeNotRead => AlertProblem.SkillTimeNotRead,
                ConditionProblemKind.UltimateTimeNotRead => AlertProblem.UltimateTimeNotRead,
                _ => (AlertProblem?)null,
            };
            if (kind is { } k && !problems.Contains(k)) problems.Add(k);
        }
        return problems;
    }
}

public enum AlertProblem
{
    /// <summary>The rule has no condition: it would never be told when to show.</summary>
    NoConditions,

    /// <summary>An and/or group with nothing in it.</summary>
    EmptyConditionGroup,

    /// <summary>"Battle skill running" for an operator whose battle-skill time isn't read (only Si's): it could never be told.</summary>
    SkillTimeNotRead,

    /// <summary>"Time left of the ultimate" for an operator whose lasting ultimate isn't read: it could never be told.</summary>
    UltimateTimeNotRead,

    /// <summary>
    /// A kind that isn't for alerts ("the phase's steps lit": it is about a phase's own round, so it is only for a
    /// phase's finish condition). Only a hand-written file can hold one; the editor doesn't offer it.
    /// </summary>
    NotForAlerts,
}

/// <summary>The alerts file: every rule, in the player's order.</summary>
public sealed record AlertFile(int Format, IReadOnlyList<AlertRule> Rules)
{
    /// <summary>
    /// 4: "phase shown" conditions (<see cref="PhaseShownCondition"/>; before, the editor refused them), "time
    /// left of the ultimate" and "the current phase is" (<see cref="UltimateTimeLeftCondition"/>, <see cref="PhaseCurrentCondition"/>).
    /// 3: "battle skill running" conditions (<see cref="BattleSkillActiveCondition"/>).
    /// 2: "phase played" conditions (<see cref="PhasePlayedCondition"/>). 1: everything else. Rules are
    /// written in the lowest format that holds them (<see cref="FormatNeeded"/>), so a build from
    /// before format 2 still reads rules that use nothing new, and refuses the rest as "newer"
    /// (never renaming the file as unreadable).
    /// </summary>
    public const int CurrentFormat = 4;

    /// <summary>The lowest format that holds these rules: each condition's kind says (<see cref="ConditionCatalogue"/>).</summary>
    public static int FormatNeeded(IEnumerable<AlertRule> rules) =>
        ConditionWalk.Flatten(rules.SelectMany(r => r.When)).Select(ConditionCatalogue.AlertFormat).DefaultIfEmpty(1).Max();

    /// <summary>The most rules kept: "new" and imports stop here.</summary>
    public const int MaxRules = 100;

    [JsonIgnore]
    public bool Readable => Rules is not null && Rules.All(r => r is not null && r.Id is not null && r.When is not null);
}
