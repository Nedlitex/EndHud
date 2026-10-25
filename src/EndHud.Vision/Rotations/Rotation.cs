using System.Text.Json.Serialization;

namespace EndHud.Vision.Rotations;

/// <summary>What a step does: the three skills are tracked; a basic or heavy attack is only drawn.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StepAction
{
    /// <summary>Battle skill (战技): costs a skill point.</summary>
    BattleSkill,

    /// <summary>Combo skill (连携技).</summary>
    Combo,

    /// <summary>Ultimate (终结技).</summary>
    Ultimate,

    /// <summary>
    /// Basic attack: drawn only. EndHud doesn't detect basic attacks, so it's never "used" and
    /// its availability is unknown; the phase's entry condition says when it shows. Format 4.
    /// </summary>
    BasicAttack,

    /// <summary>Heavy attack: drawn only, as <see cref="BasicAttack"/>. Format 8.</summary>
    HeavyAttack,

    /// <summary>Plunge attack (下落攻击): drawn only, as <see cref="BasicAttack"/>. Format 9.</summary>
    PlungeAttack,

    /// <summary>
    /// A condition step: no skill, the step is its conditions (<see cref="RotationStep.Expect"/>, all
    /// must hold), e.g. "the enemy has Cryo Infliction at 3 stacks". No operator. Format 9.
    /// </summary>
    Condition,
}

public static class StepActions
{
    /// <summary>
    /// A basic, heavy or plunge attack: drawn only. EndHud never sees one used and can't tell if it's
    /// available, so following never waits on it and the next real cast passes it.
    /// </summary>
    public static bool IsDisplayOnly(this StepAction action) => action is StepAction.BasicAttack or StepAction.HeavyAttack or StepAction.PlungeAttack;

    /// <summary>The lowest rotation format that holds this step (its conditions aside).</summary>
    public static int FormatNeeded(RotationStep step) => step.OperatorId == RotationStep.Controlled ? 9 : step.Action switch
    {
        StepAction.Condition or StepAction.PlungeAttack => 9,
        StepAction.HeavyAttack => 8,
        StepAction.BasicAttack => 4,
        _ => 2,
    };
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Comparison
{
    AtLeast,
    AtMost,
    Exactly,

    /// <summary>Strictly less ("skill points below 2": 1.95 counts, 2 doesn't). Format 4.</summary>
    Below,

    /// <summary>Strictly more. Format 4.</summary>
    Above,
}

/// <summary>
/// One fact about the game that a step's expected state or a phase's entry condition can require,
/// or a group of them (<see cref="AllCondition"/>, <see cref="AnyCondition"/>), nestable.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(SkillPointsCondition), "skillPoints")]
[JsonDerivedType(typeof(ComboCondition), "combo")]
[JsonDerivedType(typeof(UltimateCondition), "ultimate")]
[JsonDerivedType(typeof(UltimateChargeCondition), "ultimateCharge")]
[JsonDerivedType(typeof(ComboChargeCondition), "comboCharge")]
[JsonDerivedType(typeof(BattleSkillCondition), "battleSkill")]
[JsonDerivedType(typeof(CounterCondition), "counter")]
[JsonDerivedType(typeof(BossStatusCondition), "bossStatus")]
[JsonDerivedType(typeof(ControllingCondition), "controlling")]
[JsonDerivedType(typeof(AllCondition), "all")]
[JsonDerivedType(typeof(AnyCondition), "any")]
[JsonDerivedType(typeof(NotCondition), "not")]
[JsonDerivedType(typeof(UltimateActiveCondition), "ultimateActive")]
[JsonDerivedType(typeof(PhaseShownCondition), "phaseShown")]
[JsonDerivedType(typeof(PhasePlayedCondition), "phasePlayed")]
[JsonDerivedType(typeof(BattleSkillActiveCondition), "battleSkillActive")]
[JsonDerivedType(typeof(UltimateTimeLeftCondition), "ultimateTimeLeft")]
[JsonDerivedType(typeof(PhaseCurrentCondition), "phaseCurrent")]
[JsonDerivedType(typeof(StepsLitCondition), "stepsLit")]
[JsonDerivedType(typeof(BattleSkillEmpoweredCondition), "battleSkillEmpowered")]
[JsonDerivedType(typeof(TeamBuffCondition), "teamBuff")]
public abstract record StateCondition;

/// <summary>Which steps a <see cref="StepsLitCondition"/> asks to be lit.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StepsLitWhich
{
    /// <summary>Every step of the phase that lights up (全部): not a basic, heavy or plunge attack.</summary>
    All,

    /// <summary>At least <see cref="StepsLitCondition.Count"/> of them (至少 N 步).</summary>
    AtLeast,

    /// <summary>These steps (指定步骤): <see cref="StepsLitCondition.Steps"/>.</summary>
    These,
}

/// <summary>
/// The phase's steps lit this round (本轴段步骤已点亮, <see cref="StepFollower"/>): all of them, at least
/// <paramref name="Count"/>, or the steps numbered <paramref name="Steps"/> (from 1, in the phase's written order).
/// Only in a phase's finish condition (<see cref="RotationPhase.Finish"/>): it is about that phase's own round.
/// Format 10.
/// </summary>
public sealed record StepsLitCondition(
    StepsLitWhich Which,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int Count = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<int>? Steps = null) : StateCondition
{
    public bool Equals(StepsLitCondition? other) =>
        other is not null && Which == other.Which && Count == other.Count && (Steps ?? []).SequenceEqual(other.Steps ?? []);

    public override int GetHashCode() => HashCode.Combine(Which, Count, Steps?.Count ?? 0);
}

/// <summary>The team's skill points (技力), 0 to the bar's segment count (usually 3, up to 20); a part-filled segment counts as a fraction.</summary>
public sealed record SkillPointsCondition(Comparison Is, double Value) : StateCondition;

/// <summary>An operator's combo skill is ready (or cooling, when <paramref name="Ready"/> is false).</summary>
public sealed record ComboCondition(string OperatorId, bool Ready) : StateCondition;

/// <summary>An operator's ultimate is ready (or not).</summary>
public sealed record UltimateCondition(string OperatorId, bool Ready) : StateCondition;

/// <summary>An operator's ultimate is charged to (at least, at most) this percentage; ready counts as 100.</summary>
public sealed record UltimateChargeCondition(string OperatorId, Comparison Is, int Percent) : StateCondition;

/// <summary>An operator's combo cooldown has refilled to (at least, at most) this percentage of its bar; ready counts as 100.</summary>
public sealed record ComboChargeCondition(string OperatorId, Comparison Is, int Percent) : StateCondition;

/// <summary>An operator's battle skill button is available (or cooling, when <paramref name="Ready"/> is false).
/// The countdown seconds on a cooling button aren't read.</summary>
public sealed record BattleSkillCondition(string OperatorId, bool Ready) : StateCondition;

/// <summary>An operator's own counter, e.g. Laevatain's 熔火 at 4 (see OperatorCounters).</summary>
public sealed record CounterCondition(string OperatorId, string CounterId, Comparison Is, int Value) : StateCondition;

/// <summary>The enemy has (or doesn't have) a status, by its wiki name, e.g. "Heat Infliction" at 2 or more stacks.</summary>
public sealed record BossStatusCondition(string StatusId, bool Present, int MinStacks = 1) : StateCondition;

/// <summary>This operator is the one being controlled.</summary>
public sealed record ControllingCondition(string OperatorId) : StateCondition;

/// <summary>Every one of these holds (and).</summary>
public sealed record AllCondition(IReadOnlyList<StateCondition> Of) : StateCondition
{
    public bool Equals(AllCondition? other) => other is not null && Of.SequenceEqual(other.Of);
    public override int GetHashCode() => Of.Count;
}

/// <summary>At least one of these holds (or).</summary>
public sealed record AnyCondition(IReadOnlyList<StateCondition> Of) : StateCondition
{
    public bool Equals(AnyCondition? other) => other is not null && Of.SequenceEqual(other.Of);
    public override int GetHashCode() => Of.Count;
}

/// <summary>
/// An operator's lasting ultimate is running (Laevatain's, Zhuang Fangyi's: the bar above the skill
/// points), or not, when <paramref name="Active"/> is false. Format 3.
/// </summary>
public sealed record UltimateActiveCondition(string OperatorId, bool Active) : StateCondition;

/// <summary>
/// An operator's lasting battle skill is running (Si's: from the swirl on her battle-skill button
/// until the butterfly is back, the purple countdown round her stacks' circle), or not, when
/// <paramref name="Active"/> is false. Only operators whose skill time is read
/// (<see cref="SkillRing.Operators"/>) can be told. Format 7.
/// </summary>
public sealed record BattleSkillActiveCondition(string OperatorId, bool Active) : StateCondition;

/// <summary>
/// An operator's battle skill is empowered (战技已强化), or not when <paramref name="Empowered"/> is false: the steady
/// white glow on the rim of that operator's battle-skill button (<see cref="BattleSkillFlash.Glow"/>), told over the last
/// half second of the corner sampled many times a second (<see cref="PartyState.Empowered"/>), with keyboard and mouse
/// and with a controller (either diamond). Any operator may be named: whose battle skill the game empowers, and when,
/// is the game's to say, not EndHud's. Format 11.
/// </summary>
public sealed record BattleSkillEmpoweredCondition(string OperatorId, bool Empowered) : StateCondition;

/// <summary>
/// How many stacks of a team buff the game shows left of the HP bar (<see cref="TeamBuffReader"/>), by the wiki's name, e.g.
/// Link (连击) at least 1: "连击层数 ≥ 1". A buff not shown in combat counts as 0; out of combat, or with its number not
/// read, it is unknown (<see cref="PartyState.BuffStacks"/>). Format 12.
/// </summary>
public sealed record TeamBuffCondition(string BuffId, Comparison Is, int Value) : StateCondition;

/// <summary>
/// How much of an operator's lasting ultimate is left to run, as a percentage of its time bar
/// (<see cref="LastingUltimateView.LeftShare"/>), e.g. "Laevatain's ultimate has at most 30% of its
/// time left". 0% while it isn't running. Only operators whose lasting ultimate is read
/// (<see cref="LastingUltimates.Operators"/>) can be told. Format 9.
/// </summary>
public sealed record UltimateTimeLeftCondition(string OperatorId, Comparison Is, int Percent) : StateCondition;

/// <summary>
/// The phase on the strip now is <paramref name="PhaseId"/> (as of the reading before this one, as
/// the phase selector sees its own showings). With <paramref name="PlayedThrough"/>, also whether its
/// finish condition has held since it last came onto the strip (<see cref="RotationPhase.Finish"/>; true:
/// played through this showing, 本次已打完; false: not yet). Format 9.
/// </summary>
public sealed record PhaseCurrentCondition(string PhaseId, bool? PlayedThrough = null) : StateCondition;

/// <summary>This doesn't hold (not), e.g. "not: Laevatain's ultimate charged to at most 50%". Unknown stays unknown. Format 3.</summary>
public sealed record NotCondition(StateCondition Of) : StateCondition;

/// <summary>
/// The phase <paramref name="PhaseId"/> has come onto the strip <paramref name="Is"/>
/// <paramref name="Times"/> times in this fight (counted when it comes on; back to 0 when the fight
/// ends). Only whatever shows the phases knows the count (<see cref="IPhaseHistory"/>). Format 4.
/// </summary>
public sealed record PhaseShownCondition(string PhaseId, Comparison Is, int Times) : StateCondition;

/// <summary>
/// The phase <paramref name="PhaseId"/> has been played through <paramref name="Is"/>
/// <paramref name="Times"/> times in this fight: its finish condition came true (none: every step that lights up
/// lit, in any order), as the step follower lights them from the screen (<see cref="StepFollower"/>; back to 0 when
/// the fight ends). Format 5.
/// </summary>
public sealed record PhasePlayedCondition(string PhaseId, Comparison Is, int Times) : StateCondition;

/// <summary>How many times each phase has been shown and played through this fight; null for a phase it doesn't know.</summary>
public interface IPhaseHistory
{
    int? TimesShown(string phaseId);

    /// <summary>Times the phase was played through this fight (<see cref="PhasePlayedCondition"/>); null when it can't be told.</summary>
    int? TimesPlayed(string phaseId);

    /// <summary>The phase on the strip (<see cref="PhaseCurrentCondition"/>); null when there is none or it can't be told.</summary>
    string? CurrentPhase => null;

    /// <summary>
    /// Has the step follower played this phase through since it last came onto the strip? False for a
    /// phase not on the strip; null when it can't be told (no rotation followed).
    /// </summary>
    bool? PlayedThroughThisShowing(string phaseId) => null;

    /// <summary>
    /// The steps of the phase on the strip this round, in written order (<see cref="StepsLitCondition"/>): true lit,
    /// false not yet, null a step that never lights (a basic, heavy or plunge attack). Null when it can't be told.
    /// </summary>
    IReadOnlyList<bool?>? StepsLit => null;
}

public static class PhaseHistory
{
    /// <summary>For checks made outside a phase selector (explanations): "phase shown" and "phase played" are unknown there.</summary>
    public static IPhaseHistory None { get; } = new NoHistory();

    private sealed class NoHistory : IPhaseHistory
    {
        public int? TimesShown(string phaseId) => null;

        public int? TimesPlayed(string phaseId) => null;
    }
}

/// <summary>One step of a rotation.</summary>
/// <param name="Expect">What the game should look like when it's time for this step (all must hold).
/// The step's own ability being available (combo or ultimate ready, a skill point for a battle
/// skill) is implied.</param>
/// <param name="Note">Shown under the icon, e.g. "飞天".</param>
/// <param name="Label">Replaces the action's name above the icon, e.g. "战技·意".</param>
/// <param name="WithPrevious">Used right after the previous step of the same phase (drawn overlapping it, like 连携技连携技).</param>
public sealed record RotationStep(
    string OperatorId,
    StepAction Action,
    IReadOnlyList<StateCondition>? Expect = null,
    string? Note = null,
    string? Label = null,
    bool WithPrevious = false)
{
    public IReadOnlyList<StateCondition> Expect { get; init; } = Expect ?? [];

    /// <summary>
    /// The <see cref="OperatorId"/> of a display-only step (a basic, heavy or plunge attack) done by
    /// whoever is controlled at the time ("当前操控"), not a named operator. Format 9.
    /// </summary>
    public const string Controlled = "controlled";
}

/// <summary>A part of a rotation, e.g. "充能轴".</summary>
/// <param name="When">The entry condition (all must hold). Empty: the phase is part of the default
/// flow, played in list order. Set: the phase is entered the moment the condition turns true (even in
/// the middle of another phase), played to its end, and never entered by the flow.</param>
public sealed record RotationPhase(string Id, string Name, IReadOnlyList<RotationStep> Steps, IReadOnlyList<StateCondition>? When = null)
{
    public IReadOnlyList<StateCondition> When { get; init; } = When ?? [];

    /// <summary>Entered only through its entry condition.</summary>
    [JsonIgnore]
    public bool Gated => When.Count > 0;

    /// <summary>
    /// The phase's priority when no rule of <see cref="PriorityRules"/> holds: among the phases whose
    /// entry condition holds, the one with the biggest priority is shown; equal priorities go by list
    /// order (<see cref="Rotation.LastMatchWins"/>). 0 when not set, so a rotation that sets none
    /// behaves as list order alone. Only ranks gated phases. Format 6 when not 0.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int Priority { get; init; }

    /// <summary>
    /// "If this holds, the priority is that", in order: the first rule whose condition holds sets the
    /// phase's priority, else <see cref="Priority"/> applies. A rule that can't be read doesn't hold. Format 6 when there is one.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<PriorityRule> PriorityRules { get; init; } = [];

    /// <summary>The rules as written in the file: left out when there are none.</summary>
    [JsonInclude]
    [JsonPropertyName("priorityRules")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    private IReadOnlyList<PriorityRule>? PriorityRulesInFile
    {
        get => PriorityRules.Count > 0 ? PriorityRules : null;
        init => PriorityRules = value ?? [];
    }

    /// <summary>The priority set: a base other than 0, or a rule.</summary>
    [JsonIgnore]
    public bool Prioritised => Priority != 0 || PriorityRules.Count > 0;

    /// <summary>
    /// The finish condition (完成条件, all must hold): when it comes true, the round is played through (phase played
    /// +1, "played through this showing") and every step's light goes out for a new round (<see cref="StepFollower"/>).
    /// Empty: every step that lights up is lit, in any order. Any kind of condition, and only here
    /// <see cref="StepsLitCondition"/>. Format 10 when there is one.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<StateCondition> Finish { get; init; } = [];

    /// <summary>The finish condition as written in the file: left out when there is none.</summary>
    [JsonInclude]
    [JsonPropertyName("finish")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    private IReadOnlyList<StateCondition>? FinishInFile
    {
        get => Finish.Count > 0 ? Finish : null;
        init => Finish = value ?? [];
    }
}

/// <summary>"If <paramref name="When"/> holds (all of it), the phase's priority is <paramref name="Priority"/>."</summary>
public sealed record PriorityRule(IReadOnlyList<StateCondition> When, int Priority)
{
    public IReadOnlyList<StateCondition> When { get; init; } = When ?? [];

    public bool Equals(PriorityRule? other) => other is not null && Priority == other.Priority && When.SequenceEqual(other.When);

    public override int GetHashCode() => HashCode.Combine(Priority, When.Count);
}

/// <summary>A step with where it comes from (for the strip and the tracker).</summary>
/// <param name="Index">Position in <see cref="Rotation.Played"/>, from 0.</param>
/// <param name="PhaseLabel">The phase as shown: its name and its place in the list, e.g. "充能轴 II".</param>
/// <param name="PhaseIndex">The phase's place in <see cref="Rotation.Phases"/>, from 0.</param>
/// <param name="StepInPhase">Position inside the phase, from 0.</param>
public sealed record PlayedStep(int Index, string PhaseLabel, int PhaseIndex, int StepInPhase, RotationStep Step);

/// <summary>
/// A rotation for one squad: phases in order. Phases without an entry condition are played one after
/// another, looping (e.g. 充能轴, 充能轴: charge twice, again and again); a phase with one (e.g. 爆发轴
/// when Laevatain's ultimate is ready) is entered the moment its condition turns true. There's no
/// start or reset: EndHud finds the step whose expected state matches the game best
/// (RotationTracker).
/// </summary>
/// <param name="Format">The file format's version; this build reads up to <see cref="CurrentFormat"/>.</param>
/// <param name="Squad">The four operators it's for, in any slot order.</param>
/// <param name="LastMatchWins">When several phases' conditions hold, show the last of them in the list instead of the first. Format 3.</param>
public sealed record Rotation(
    int Format,
    string Id,
    string Name,
    IReadOnlyList<string> Squad,
    IReadOnlyList<RotationPhase> Phases,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool LastMatchWins = false)
{
    /// <summary>
    /// 12: "team buff stacks" conditions (<see cref="TeamBuffCondition"/>).
    /// 11: "battle skill empowered" conditions (<see cref="BattleSkillEmpoweredCondition"/>).
    /// 10: phases' finish conditions (<see cref="RotationPhase.Finish"/>) and "the phase's steps lit" in them
    /// (<see cref="StepsLitCondition"/>).
    /// 9: "time left of the ultimate" and "the current phase is" conditions
    /// (<see cref="UltimateTimeLeftCondition"/>, <see cref="PhaseCurrentCondition"/>), condition steps
    /// (<see cref="StepAction.Condition"/>), plunge-attack steps (<see cref="StepAction.PlungeAttack"/>), and
    /// attack steps by whoever is controlled (<see cref="RotationStep.Controlled"/>).
    /// 8: heavy-attack steps (<see cref="StepAction.HeavyAttack"/>).
    /// 7: "battle skill running" conditions (<see cref="BattleSkillActiveCondition"/>).
    /// 6: phase priorities (<see cref="RotationPhase.Priority"/>, <see cref="RotationPhase.PriorityRules"/>).
    /// 5: "phase played" conditions (<see cref="PhasePlayedCondition"/>). 4: basic-attack steps, "phase shown" conditions, the "below"/"above" (<see cref="Comparison.Below"/>,
    /// <see cref="Comparison.Above"/>) comparisons. 3: "not" and "ultimate active" conditions, and <see cref="LastMatchWins"/>. 2: phases with entry conditions, all/any
    /// groups. 1 (read, converted): a fixed sequence. A rotation is saved in the lowest format that
    /// holds it (<see cref="FormatNeeded"/>), so one that uses nothing new stays readable by builds
    /// from before format 3.
    /// </summary>
    public const int CurrentFormat = 12;

    /// <summary>The lowest format that holds these phases and options.</summary>
    public static int FormatNeeded(IEnumerable<RotationPhase> phases, bool lastMatchWins)
    {
        var list = phases.ToList();
        int format = lastMatchWins ? 3 : 2;
        // Each condition's kind says which format holds it (ConditionCatalogue).
        foreach (var condition in ConditionWalk.Of(list)) format = Math.Max(format, ConditionCatalogue.RotationFormat(condition));
        if (list.Any(p => p.Prioritised)) format = Math.Max(format, 6);
        if (list.Any(p => p.Finish.Count > 0)) format = Math.Max(format, 10);
        foreach (var step in list.SelectMany(p => p.Steps)) format = Math.Max(format, StepActions.FormatNeeded(step));
        return format;
    }

    /// <summary>Is this rotation for this squad: the same four operators, in any order?</summary>
    public bool IsFor(IEnumerable<string?> squad)
    {
        var ids = squad.Where(id => id is not null).Select(id => id!).ToHashSet(StringComparer.Ordinal);
        return ids.Count == Squad.Count && Squad.All(ids.Contains);
    }

    public RotationPhase? Phase(string id) => Phases.FirstOrDefault(p => p.Id == id);

    /// <summary>A phase's name numbered by its place in the list: "充能轴 II".</summary>
    public string PhaseLabel(int phase) => $"{Phases[phase].Name} {Roman(phase + 1)}";

    /// <summary>
    /// Every step, phase by phase in list order. Phases are numbered by their place in the list, as
    /// guides do ("充能轴 I", "充能轴 II", "爆发轴 III").
    /// </summary>
    public IReadOnlyList<PlayedStep> Played()
    {
        var played = new List<PlayedStep>();
        for (int p = 0; p < Phases.Count; p++)
        {
            string label = PhaseLabel(p);
            for (int i = 0; i < Phases[p].Steps.Count; i++)
                played.Add(new PlayedStep(played.Count, label, p, i, Phases[p].Steps[i]));
        }
        return played;
    }

    /// <summary>
    /// The phase the flow goes to after <paramref name="phase"/>: the next phase without an entry
    /// condition in list order, wrapping (possibly itself). Null when every phase has one.
    /// </summary>
    public int? NextInFlow(int phase)
    {
        for (int k = 1; k <= Phases.Count; k++)
        {
            int candidate = (phase + k) % Phases.Count;
            if (!Phases[candidate].Gated && Phases[candidate].Steps.Count > 0) return candidate;
        }
        return null;
    }

    /// <summary>The phase the flow comes from before <paramref name="phase"/> (the previous phase without an entry condition).</summary>
    public int? PreviousInFlow(int phase)
    {
        for (int k = 1; k <= Phases.Count; k++)
        {
            int candidate = ((phase - k) % Phases.Count + Phases.Count) % Phases.Count;
            if (!Phases[candidate].Gated && Phases[candidate].Steps.Count > 0) return candidate;
        }
        return null;
    }

    /// <summary>What's wrong with it, if anything (the editor turns these into sentences).</summary>
    public IReadOnlyList<RotationProblem> Problems()
    {
        var problems = new List<RotationProblem>();
        if (Squad.Count != 4 || Squad.Distinct(StringComparer.Ordinal).Count() != 4)
            problems.Add(new RotationProblem(RotationProblemKind.SquadNotFourOperators));
        if (Phases.Count == 0)
            problems.Add(new RotationProblem(RotationProblemKind.NoPhases));
        foreach (var phase in Phases)
        {
            if (phase.Steps.Count == 0)
                problems.Add(new RotationProblem(RotationProblemKind.PhaseHasNoSteps, phase.Name));
            else if (phase.Steps[0].WithPrevious)
                problems.Add(new RotationProblem(RotationProblemKind.FirstStepWithPrevious, phase.Name, 1));
            CheckConditions(phase.When, ConditionHostKind.PhaseGate, phase, null, null, problems);
            // Priorities rank only the phases whose entry condition holds: on the otherwise phase they'd do nothing.
            if (!phase.Gated && phase.Prioritised)
                problems.Add(new RotationProblem(RotationProblemKind.PriorityWithoutEntryCondition, phase.Name));
            for (int r = 0; r < phase.PriorityRules.Count; r++)
            {
                var rule = phase.PriorityRules[r];
                if (rule.When.Count == 0)
                    problems.Add(new RotationProblem(RotationProblemKind.PriorityRuleWithoutCondition, phase.Name, null, r + 1));
                CheckConditions(rule.When, ConditionHostKind.PriorityRule, phase, null, r + 1, problems);
            }
            CheckConditions(phase.Finish, ConditionHostKind.PhaseFinish, phase, null, null, problems, finish: true);
            for (int i = 0; i < phase.Steps.Count; i++)
            {
                var step = phase.Steps[i];
                bool condition = step.Action == StepAction.Condition;
                // A condition step has no operator; it waits on its own, never in a group with another step.
                // An attack may be by whoever is controlled; a skill is followed by whose it is.
                if (step.OperatorId == RotationStep.Controlled && !step.Action.IsDisplayOnly())
                    problems.Add(new RotationProblem(RotationProblemKind.ControlledOperatorOnlyForAttacks, phase.Name, i + 1));
                else if (!condition && step.OperatorId != RotationStep.Controlled && !Squad.Contains(step.OperatorId))
                    problems.Add(new RotationProblem(RotationProblemKind.StepOperatorNotInSquad, phase.Name, i + 1));
                if (condition && step.Expect.Count == 0)
                    problems.Add(new RotationProblem(RotationProblemKind.ConditionStepEmpty, phase.Name, i + 1));
                if (i > 0 && step.WithPrevious && (condition || phase.Steps[i - 1].Action == StepAction.Condition))
                    problems.Add(new RotationProblem(RotationProblemKind.ConditionStepWithPrevious, phase.Name, i + 1));
                CheckConditions(step.Expect, condition ? ConditionHostKind.ConditionStep : ConditionHostKind.StepExpect, phase, i + 1, null, problems);
            }
        }
        return problems;
    }

    /// <summary>The shared check (<see cref="ConditionRules"/>), with this rotation as the host's context, as rotation problems.</summary>
    /// <param name="step">From 1; null for the phase's entry condition or a priority rule.</param>
    /// <param name="rule">From 1, for a priority rule's condition; null otherwise.</param>
    /// <param name="finish">The phase's finish condition.</param>
    private void CheckConditions(IEnumerable<StateCondition> conditions, ConditionHostKind host, RotationPhase owner, int? step, int? rule, List<RotationProblem> problems, bool finish = false)
    {
        var context = new ConditionHost(host, Squad, owner.Id, id => Phase(id) is not null, owner.Steps);
        foreach (var problem in ConditionRules.Check(conditions, context))
        {
            var kind = problem.Kind switch
            {
                ConditionProblemKind.EmptyGroup => RotationProblemKind.EmptyConditionGroup,
                ConditionProblemKind.OperatorNotInSquad => RotationProblemKind.ConditionOperatorNotInSquad,
                ConditionProblemKind.MissingPhase when problem.Condition is PhasePlayedCondition => RotationProblemKind.PhasePlayedMissingPhase,
                ConditionProblemKind.MissingPhase when problem.Condition is PhaseCurrentCondition => RotationProblemKind.PhaseCurrentMissingPhase,
                ConditionProblemKind.UltimateTimeNotRead => RotationProblemKind.UltimateTimeNotRead,
                ConditionProblemKind.MissingPhase => RotationProblemKind.PhaseShownMissingPhase,
                ConditionProblemKind.PhaseShownOwnGate => RotationProblemKind.PhaseShownOwnGate,
                ConditionProblemKind.PhasePlayedOwnGateNeverOpens => RotationProblemKind.PhasePlayedOwnGateNeverOpens,
                // The only kind not offered somewhere: "the phase's steps lit", for a finish condition only.
                ConditionProblemKind.NotOfferedHere => RotationProblemKind.StepsLitOutsideFinish,
                ConditionProblemKind.StepsLitNoSteps => RotationProblemKind.StepsLitNoSteps,
                ConditionProblemKind.StepsLitNotCountable => RotationProblemKind.StepsLitNotCountable,
                ConditionProblemKind.StepsLitCountOutOfRange => RotationProblemKind.StepsLitCountOutOfRange,
                _ => RotationProblemKind.SkillTimeNotRead,
            };
            problems.Add(new RotationProblem(kind, owner.Name, step, rule, finish));
        }
    }

    private static string Roman(int n)
    {
        (int Value, string Text)[] digits = [(10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I")];
        var text = new System.Text.StringBuilder();
        foreach (var (value, roman) in digits)
            for (; n >= value; n -= value) text.Append(roman);
        return text.ToString();
    }
}

public enum RotationProblemKind
{
    SquadNotFourOperators,
    NoPhases,
    PhaseHasNoSteps,
    /// <summary>A phase's first step is marked "with the previous step": there is none in its phase.</summary>
    FirstStepWithPrevious,
    StepOperatorNotInSquad,
    /// <summary>A condition names an operator not in the squad (Step is null for a phase's entry condition).</summary>
    ConditionOperatorNotInSquad,
    EmptyConditionGroup,

    /// <summary>A phase's entry condition counts the phase's own showings (it would flip the moment the phase shows).</summary>
    PhaseShownOwnGate,

    /// <summary>A "phase shown" condition names a phase that isn't in the rotation (deleted).</summary>
    PhaseShownMissingPhase,

    /// <summary>A "phase played" condition names a phase that isn't in the rotation (deleted).</summary>
    PhasePlayedMissingPhase,

    /// <summary>
    /// A phase's entry condition requires the phase itself played some times, and 0 doesn't do: it can
    /// never open (the phase must be shown to be played, and played to be shown).
    /// </summary>
    PhasePlayedOwnGateNeverOpens,

    /// <summary>A priority rule with no condition: it would always hold (set the phase's own priority instead).</summary>
    PriorityRuleWithoutCondition,

    /// <summary>
    /// A phase without an entry condition sets a priority or rules: it is the "otherwise" phase, shown
    /// when no other holds, and priorities only rank the phases whose entry condition holds.
    /// </summary>
    PriorityWithoutEntryCondition,

    /// <summary>
    /// A "battle skill running" condition names an operator whose battle-skill time EndHud doesn't read
    /// (only Si's, <see cref="SkillRing.Operators"/>): it could never be told.
    /// </summary>
    SkillTimeNotRead,

    /// <summary>A "time left of the ultimate" condition names an operator whose lasting ultimate isn't read (<see cref="LastingUltimates.Operators"/>).</summary>
    UltimateTimeNotRead,

    /// <summary>A "the current phase is" condition names a phase that isn't in the rotation (deleted).</summary>
    PhaseCurrentMissingPhase,

    /// <summary>A condition step without a condition: it would have nothing to wait for.</summary>
    ConditionStepEmpty,

    /// <summary>A condition step used together with another step ("with the previous"): it waits on its own.</summary>
    ConditionStepWithPrevious,

    /// <summary>
    /// A skill step (battle skill, combo, ultimate) names "whoever is controlled" (<see cref="RotationStep.Controlled"/>):
    /// only a basic, heavy or plunge attack can; a skill is followed by whose it is.
    /// </summary>
    ControlledOperatorOnlyForAttacks,

    /// <summary>"The phase's steps lit" outside a finish condition: it is about a phase's own round, so it is only for its finish.</summary>
    StepsLitOutsideFinish,

    /// <summary>"These steps lit" with no step picked: it would always hold.</summary>
    StepsLitNoSteps,

    /// <summary>"These steps lit" names a step the phase doesn't have, or one that never lights (a basic, heavy or plunge attack).</summary>
    StepsLitNotCountable,

    /// <summary>"At least N steps lit" with N below 1 or above the steps that light up: always or never holds.</summary>
    StepsLitCountOutOfRange,
}

/// <param name="Phase">The phase concerned (its name), if any.</param>
/// <param name="Step">The step concerned, from 1, if any (null: the phase's entry condition, or the phase itself).</param>
/// <param name="Rule">The phase's priority rule concerned, from 1, if any (then <paramref name="Step"/> is null).</param>
/// <param name="Finish">In the phase's finish condition (then <paramref name="Step"/> and <paramref name="Rule"/> are null).</param>
public sealed record RotationProblem(RotationProblemKind Kind, string? Phase = null, int? Step = null, int? Rule = null, bool Finish = false);
