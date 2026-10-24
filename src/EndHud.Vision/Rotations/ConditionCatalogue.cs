namespace EndHud.Vision.Rotations;

/// <summary>Where a list of conditions is used. Every host offers the same kinds and checks them the same way.</summary>
public enum ConditionHostKind
{
    /// <summary>A phase's entry condition.</summary>
    PhaseGate,

    /// <summary>A phase's priority rule ("if this holds, the priority is that").</summary>
    PriorityRule,

    /// <summary>A step's expected state.</summary>
    StepExpect,

    /// <summary>A condition step: the step is the condition.</summary>
    ConditionStep,

    /// <summary>An alert rule (战斗提示): no squad of its own, any rotation's phase.</summary>
    Alert,
}

/// <summary>Where conditions are used, as context for checking them (<see cref="ConditionRules"/>).</summary>
/// <param name="Squad">The rotation's squad: an operator outside it is a problem. Null (alerts): no squad; an operator not in the fight is unknown at run time.</param>
/// <param name="OwnerPhaseId">The phase the conditions belong to, if any.</param>
/// <param name="PhaseExists">Does the rotation have this phase. Null (alerts): any rotation's phase may be named, so it isn't checked.</param>
public sealed record ConditionHost(ConditionHostKind Kind, IReadOnlyCollection<string>? Squad = null, string? OwnerPhaseId = null, Func<string, bool>? PhaseExists = null)
{
    /// <summary>An alert rule's conditions.</summary>
    public static ConditionHost Alert { get; } = new(ConditionHostKind.Alert);
}

/// <summary>What a condition needs to be told at run time.</summary>
[Flags]
public enum ConditionNeeds
{
    None = 0,

    /// <summary>Names a phase of a rotation; known only while that rotation is followed.</summary>
    Phase = 1,

    /// <summary>The player reads their battle-skill keys ("phase played"); while not, it is ignored.</summary>
    KeysRead = 2,

    /// <summary>The boss's statuses must be read (they are read only when something asks).</summary>
    BossStatuses = 4,

    /// <summary>Only for operators whose battle-skill time is read (<see cref="SkillRing.Operators"/>).</summary>
    SkillTime = 8,
}

/// <summary>One kind of condition, as everything that handles conditions sees it.</summary>
/// <param name="Id">The kind's name in code, e.g. "UltimateCharge".</param>
/// <param name="Type">Its record type.</param>
/// <param name="JsonName">Its <c>kind</c> in rotation and alert files.</param>
/// <param name="EditorKind">What the editor's kind picker calls it ("Group" for both "all" and "any"); null for "not", which is a box on every row.</param>
/// <param name="RotationFormat">The lowest rotation file format that holds it.</param>
/// <param name="AlertFormat">The lowest alert file format that holds it.</param>
/// <param name="OperatorOf">The operator it is about, if it has one.</param>
/// <param name="ComparisonOf">Its comparison, if it has one.</param>
/// <param name="PhaseOf">The phase it names, if it names one.</param>
/// <param name="Hosts">Where it may be used.</param>
public sealed record ConditionKind(
    string Id,
    Type Type,
    string JsonName,
    string? EditorKind,
    int RotationFormat,
    int AlertFormat,
    ConditionNeeds Needs,
    Func<StateCondition, string?> OperatorOf,
    Func<StateCondition, Comparison?> ComparisonOf,
    Func<StateCondition, string?> PhaseOf,
    IReadOnlySet<ConditionHostKind> Hosts)
{
    /// <summary>The only operators it can be told for; null: anyone.</summary>
    public IReadOnlyCollection<string>? OnlyFor => null;
}

/// <summary>
/// Every kind of condition in one list: what it is called in files and in the editor, which format
/// holds it, whom and what it is about, what it needs and where it may be used. Everything that
/// handles conditions reads it, so a new kind is one entry here plus its evaluation and its words.
/// </summary>
public static class ConditionCatalogue
{
    public static IReadOnlyList<ConditionKind> Kinds { get; } = [];

    /// <summary>The kind of this condition.</summary>
    public static ConditionKind Of(StateCondition condition) => throw new NotImplementedException();

    /// <summary>The editor's kinds, in the order its picker lists them.</summary>
    public static IReadOnlyList<string> EditorKinds { get; } = [];

    /// <summary>The editor's kinds offered in this host.</summary>
    public static IReadOnlyList<string> OfferedIn(ConditionHostKind host) => [];
}

public enum ConditionProblemKind
{
    /// <summary>An "all"/"any" group with nothing in it.</summary>
    EmptyGroup,

    /// <summary>Names an operator not in the rotation's squad.</summary>
    OperatorNotInSquad,

    /// <summary>Names a phase the rotation doesn't have (deleted).</summary>
    MissingPhase,

    /// <summary>A phase's entry condition counts the phase's own showings (it would flip the moment the phase shows).</summary>
    PhaseShownOwnGate,

    /// <summary>A phase's entry condition requires the phase itself played some times: it can never open.</summary>
    PhasePlayedOwnGateNeverOpens,

    /// <summary>"Battle skill running" for an operator whose battle-skill time isn't read: it could never be told.</summary>
    SkillTimeNotRead,
}

/// <param name="Kind">What is wrong.</param>
/// <param name="Condition">The condition it is about.</param>
public sealed record ConditionProblem(ConditionProblemKind Kind, StateCondition Condition);

/// <summary>What is wrong with conditions, the same in every host (<see cref="ConditionHost"/>).</summary>
public static class ConditionRules
{
    public static IReadOnlyList<ConditionProblem> Check(IEnumerable<StateCondition> conditions, ConditionHost host) => [];
}
