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

    /// <summary>A phase's finish condition (完成条件): when it comes true the round is played through.</summary>
    PhaseFinish,
}

/// <summary>Where conditions are used, as context for checking them (<see cref="ConditionRules"/>).</summary>
/// <param name="Squad">The rotation's squad: an operator outside it is a problem. Null (alerts): no squad; an operator not in the fight is unknown at run time.</param>
/// <param name="OwnerPhaseId">The phase the conditions belong to, if any.</param>
/// <param name="PhaseExists">Does the rotation have this phase. Null (alerts): any rotation's phase may be named, so it isn't checked.</param>
/// <param name="OwnerSteps">The steps of the phase the conditions belong to, for "the phase's steps lit"; null: not checked.</param>
public sealed record ConditionHost(ConditionHostKind Kind, IReadOnlyCollection<string>? Squad = null, string? OwnerPhaseId = null, Func<string, bool>? PhaseExists = null,
    IReadOnlyList<RotationStep>? OwnerSteps = null)
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

    /// <summary>The boss's statuses must be read (they are read only when something asks).</summary>
    BossStatuses = 4,

    /// <summary>Only for operators whose battle-skill time is read (<see cref="SkillRing.Operators"/>).</summary>
    SkillTime = 8,

    /// <summary>Only for operators whose lasting ultimate is read (<see cref="LastingUltimates.Operators"/>).</summary>
    UltimateTime = 16,
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
    public IReadOnlyCollection<string>? OnlyFor =>
        Needs.HasFlag(ConditionNeeds.SkillTime) ? SkillRing.Operators
        : Needs.HasFlag(ConditionNeeds.UltimateTime) ? LastingUltimates.Operators
        : null;
}

/// <summary>
/// Every kind of condition in one list: what it is called in files and in the editor, which format
/// holds it, whom and what it is about, what it needs and where it may be used. Everything that
/// handles conditions reads it (the file formats, the checks, the editor, the words), so a new kind is
/// one entry here plus its evaluation (<see cref="RotationTracker.Evaluate"/>) and its words.
/// </summary>
public static class ConditionCatalogue
{
    /// <summary>Every host: the owner's "the same full set everywhere" (2026-10-24).</summary>
    private static readonly IReadOnlySet<ConditionHostKind> Everywhere = Enum.GetValues<ConditionHostKind>().ToHashSet();

    /// <summary>
    /// A phase's finish condition only: "the phase's steps lit" is about that phase's own round. In an entry condition or
    /// a priority rule it would read whichever phase is on the strip (step numbers naming another phase's steps); in a
    /// condition step, its own light; in an alert, no phase at all ("the current phase, played through this showing"
    /// says "finished" there).
    /// </summary>
    private static readonly IReadOnlySet<ConditionHostKind> FinishOnly = new HashSet<ConditionHostKind> { ConditionHostKind.PhaseFinish };

    private static ConditionKind Kind<T>(string id, string json, string? editor, int rotationFormat, int alertFormat, ConditionNeeds needs = ConditionNeeds.None,
        Func<T, string?>? who = null, Func<T, Comparison?>? comparison = null, Func<T, string?>? phase = null, IReadOnlySet<ConditionHostKind>? hosts = null) where T : StateCondition =>
        new(id, typeof(T), json, editor, rotationFormat, alertFormat, needs,
            c => who?.Invoke((T)c), c => comparison?.Invoke((T)c), c => phase?.Invoke((T)c), hosts ?? Everywhere);

    /// <summary>In the order the editor's kind picker lists them.</summary>
    public static IReadOnlyList<ConditionKind> Kinds { get; } =
    [
        Kind<SkillPointsCondition>("SkillPoints", "skillPoints", "SkillPoints", 2, 1, comparison: c => c.Is),
        Kind<ComboCondition>("Combo", "combo", "Combo", 2, 1, who: c => c.OperatorId),
        Kind<ComboChargeCondition>("ComboCharge", "comboCharge", "ComboCharge", 2, 1, who: c => c.OperatorId, comparison: c => c.Is),
        Kind<BattleSkillCondition>("BattleSkill", "battleSkill", "BattleSkill", 2, 1, who: c => c.OperatorId),
        Kind<UltimateCondition>("Ultimate", "ultimate", "Ultimate", 2, 1, who: c => c.OperatorId),
        Kind<UltimateChargeCondition>("UltimateCharge", "ultimateCharge", "UltimateCharge", 2, 1, who: c => c.OperatorId, comparison: c => c.Is),
        Kind<UltimateActiveCondition>("UltimateActive", "ultimateActive", "UltimateActive", 3, 1, who: c => c.OperatorId),
        Kind<UltimateTimeLeftCondition>("UltimateTimeLeft", "ultimateTimeLeft", "UltimateTimeLeft", 9, 4, ConditionNeeds.UltimateTime, who: c => c.OperatorId, comparison: c => c.Is),
        Kind<BattleSkillActiveCondition>("BattleSkillActive", "battleSkillActive", "BattleSkillActive", 7, 3, ConditionNeeds.SkillTime, who: c => c.OperatorId),
        Kind<CounterCondition>("Counter", "counter", "Counter", 2, 1, who: c => c.OperatorId, comparison: c => c.Is),
        Kind<BossStatusCondition>("BossStatus", "bossStatus", "BossStatus", 2, 1, ConditionNeeds.BossStatuses),
        Kind<ControllingCondition>("Controlling", "controlling", "Controlling", 2, 1, who: c => c.OperatorId),
        // An alert names a phase of any of the player's rotations, told while that rotation is followed.
        Kind<PhaseShownCondition>("PhaseShown", "phaseShown", "PhaseShown", 4, 4, ConditionNeeds.Phase, comparison: c => c.Is, phase: c => c.PhaseId),
        Kind<PhasePlayedCondition>("PhasePlayed", "phasePlayed", "PhasePlayed", 5, 2, ConditionNeeds.Phase, comparison: c => c.Is, phase: c => c.PhaseId),
        Kind<PhaseCurrentCondition>("PhaseCurrent", "phaseCurrent", "PhaseCurrent", 9, 4, ConditionNeeds.Phase, phase: c => c.PhaseId),
        // Alert format: never written there (refused), so the current one.
        Kind<StepsLitCondition>("StepsLit", "stepsLit", "StepsLit", 10, 4, hosts: FinishOnly),
        Kind<AllCondition>("All", "all", "Group", 2, 1),
        Kind<AnyCondition>("Any", "any", "Group", 2, 1),
        Kind<NotCondition>("Not", "not", null, 3, 1),
    ];

    private static readonly Dictionary<Type, ConditionKind> ByType = Kinds.ToDictionary(k => k.Type);

    /// <summary>The kind of this condition.</summary>
    public static ConditionKind Of(StateCondition condition) => ByType[condition.GetType()];

    /// <summary>The editor's kinds, in the order its picker lists them ("all" and "any" are one "Group").</summary>
    public static IReadOnlyList<string> EditorKinds { get; } = Kinds.Select(k => k.EditorKind).OfType<string>().Distinct().ToList();

    /// <summary>The editor's kinds offered in this host.</summary>
    public static IReadOnlyList<string> OfferedIn(ConditionHostKind host) =>
        Kinds.Where(k => k.Hosts.Contains(host)).Select(k => k.EditorKind).OfType<string>().Distinct().ToList();

    /// <summary>The operator a condition is about, if any (not looking inside groups).</summary>
    public static string? OperatorOf(StateCondition condition) => Of(condition).OperatorOf(condition);

    /// <summary>The comparison a condition uses, if it has one.</summary>
    public static Comparison? ComparisonOf(StateCondition condition) => Of(condition).ComparisonOf(condition);

    /// <summary>The lowest rotation format that holds this condition (its parts aside): its kind's, or 4 for a "below"/"above" comparison.</summary>
    public static int RotationFormat(StateCondition condition) =>
        Math.Max(Of(condition).RotationFormat, ComparisonOf(condition) is Comparison.Below or Comparison.Above ? 4 : 0);

    /// <summary>The lowest alert format that holds this condition (its parts aside).</summary>
    public static int AlertFormat(StateCondition condition) => Of(condition).AlertFormat;
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

    /// <summary>"Time left of the ultimate" for an operator whose lasting ultimate isn't read: it could never be told.</summary>
    UltimateTimeNotRead,

    /// <summary>A kind not offered in this host (<see cref="ConditionKind.Hosts"/>): "the phase's steps lit" outside a finish condition.</summary>
    NotOfferedHere,

    /// <summary>"These steps lit" with no step picked.</summary>
    StepsLitNoSteps,

    /// <summary>"These steps lit" names a step the phase doesn't have, or one that never lights.</summary>
    StepsLitNotCountable,

    /// <summary>"At least N steps lit" with N below 1 or above the steps that light up.</summary>
    StepsLitCountOutOfRange,
}

/// <param name="Kind">What is wrong.</param>
/// <param name="Condition">The condition it is about.</param>
public sealed record ConditionProblem(ConditionProblemKind Kind, StateCondition Condition);

/// <summary>
/// What is wrong with conditions, the same in every host (<see cref="ConditionHost"/>): what differs is
/// only the context the host gives (a squad to check operators against, a rotation's phases, the phase
/// the conditions belong to).
/// </summary>
public static class ConditionRules
{
    public static IReadOnlyList<ConditionProblem> Check(IEnumerable<StateCondition> conditions, ConditionHost host)
    {
        var list = conditions.ToList();
        var problems = new List<ConditionProblem>();
        Walk(list, host, problems);
        // A gate requiring the phase itself played: it must be shown to be played, and played to be shown.
        if (host.Kind == ConditionHostKind.PhaseGate)
        {
            foreach (var own in Required(list).OfType<PhasePlayedCondition>())
                if (own.PhaseId == host.OwnerPhaseId && !Holds(0, own.Is, own.Times))
                    problems.Add(new ConditionProblem(ConditionProblemKind.PhasePlayedOwnGateNeverOpens, own));
        }
        return problems;
    }

    /// <summary>Each condition, at any depth: the first thing wrong with it.</summary>
    private static void Walk(IEnumerable<StateCondition> conditions, ConditionHost host, List<ConditionProblem> problems)
    {
        foreach (var condition in conditions)
        {
            switch (condition)
            {
                case AllCondition { Of.Count: 0 } or AnyCondition { Of.Count: 0 }:
                    problems.Add(new ConditionProblem(ConditionProblemKind.EmptyGroup, condition));
                    continue;
                case AllCondition all:
                    Walk(all.Of, host, problems);
                    continue;
                case AnyCondition any:
                    Walk(any.Of, host, problems);
                    continue;
                case NotCondition not:
                    Walk([not.Of], host, problems);
                    continue;
            }
            if (One(condition, host) is { } problem) problems.Add(new ConditionProblem(problem, condition));
        }
    }

    private static ConditionProblemKind? One(StateCondition condition, ConditionHost host)
    {
        var kind = ConditionCatalogue.Of(condition);
        if (!kind.Hosts.Contains(host.Kind)) return ConditionProblemKind.NotOfferedHere;
        if (condition is StepsLitCondition lit && host.OwnerSteps is { } steps && StepsLitProblem(lit, steps) is { } stepsProblem) return stepsProblem;
        if (kind.PhaseOf(condition) is { } phase && host.PhaseExists is { } exists && !exists(phase))
            return ConditionProblemKind.MissingPhase;
        // Counted when it comes onto the strip, a phase's own count changes the moment it shows: its
        // entry condition would flip on the very reading that shows it. A priority rule may count its
        // own phase: it only ranks the phase once its entry condition holds.
        if (condition is PhaseShownCondition shown && host.Kind == ConditionHostKind.PhaseGate && shown.PhaseId == host.OwnerPhaseId)
            return ConditionProblemKind.PhaseShownOwnGate;
        string? who = kind.OperatorOf(condition);
        if (who is not null && kind.OnlyFor is { } readable && !readable.Contains(who))
            return kind.Needs.HasFlag(ConditionNeeds.SkillTime) ? ConditionProblemKind.SkillTimeNotRead : ConditionProblemKind.UltimateTimeNotRead;
        if (who is not null && host.Squad is { } squad && !squad.Contains(who))
            return ConditionProblemKind.OperatorNotInSquad;
        return null;
    }

    /// <summary>
    /// "The phase's steps lit" against the phase's steps: these steps must be picked, each one of the phase's that lights
    /// up (not a basic, heavy or plunge attack); "at least N" must be 1 to the number that light up.
    /// </summary>
    private static ConditionProblemKind? StepsLitProblem(StepsLitCondition lit, IReadOnlyList<RotationStep> steps)
    {
        int countable = steps.Count(StepFollower.Countable);
        return lit.Which switch
        {
            StepsLitWhich.These when lit.Steps is not { Count: > 0 } => ConditionProblemKind.StepsLitNoSteps,
            StepsLitWhich.These when lit.Steps.Any(n => n < 1 || n > steps.Count || !StepFollower.Countable(steps[n - 1])) => ConditionProblemKind.StepsLitNotCountable,
            StepsLitWhich.AtLeast when lit.Count < 1 || lit.Count > countable => ConditionProblemKind.StepsLitCountOutOfRange,
            _ => null,
        };
    }

    /// <summary>The conditions that must all hold: the list's, and inside "all" groups (not inside an "or" or a "not").</summary>
    private static IEnumerable<StateCondition> Required(IEnumerable<StateCondition> conditions) =>
        conditions.SelectMany(c => c is AllCondition all ? Required(all.Of) : [c]);

    /// <summary>Does a whole count compare so (the comparisons of <see cref="RotationTracker"/>, on whole numbers)?</summary>
    private static bool Holds(int count, Comparison comparison, int target) => comparison switch
    {
        Comparison.AtLeast => count >= target,
        Comparison.AtMost => count <= target,
        Comparison.Below => count < target,
        Comparison.Above => count > target,
        _ => count == target,
    };
}
