using System.Text.Json.Serialization;

namespace EndHud.Vision.Rotations;

/// <summary>What a step does: only these three are tracked (basic and heavy attacks aren't).</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StepAction
{
    /// <summary>Battle skill (战技): costs a skill point.</summary>
    BattleSkill,

    /// <summary>Combo skill (连携技).</summary>
    Combo,

    /// <summary>Ultimate (终结技).</summary>
    Ultimate,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Comparison
{
    AtLeast,
    AtMost,
    Exactly,
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
public abstract record StateCondition;

/// <summary>The team's skill points (技力), 0–3; a part-filled segment counts as a fraction.</summary>
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
public sealed record Rotation(
    int Format,
    string Id,
    string Name,
    IReadOnlyList<string> Squad,
    IReadOnlyList<RotationPhase> Phases)
{
    /// <summary>2: phases with entry conditions, all/any groups. 1 (read, converted): a fixed sequence.</summary>
    public const int CurrentFormat = 2;

    /// <summary>Is this rotation for this squad: the same four operators, in any order?</summary>
    public bool IsFor(IEnumerable<string?> squad)
    {
        var ids = squad.Where(id => id is not null).Select(id => id!).ToHashSet(StringComparer.Ordinal);
        return ids.Count == Squad.Count && Squad.All(ids.Contains);
    }

    public RotationPhase? Phase(string id) => Phases.FirstOrDefault(p => p.Id == id);

    /// <summary>
    /// Every step, phase by phase in list order. Phases are numbered by their place in the list, as
    /// guides do ("充能轴 I", "充能轴 II", "爆发轴 III").
    /// </summary>
    public IReadOnlyList<PlayedStep> Played()
    {
        var played = new List<PlayedStep>();
        for (int p = 0; p < Phases.Count; p++)
        {
            string label = $"{Phases[p].Name} {Roman(p + 1)}";
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
            CheckConditions(phase.When, phase.Name, null, problems);
            for (int i = 0; i < phase.Steps.Count; i++)
            {
                var step = phase.Steps[i];
                if (!Squad.Contains(step.OperatorId))
                    problems.Add(new RotationProblem(RotationProblemKind.StepOperatorNotInSquad, phase.Name, i + 1));
                CheckConditions(step.Expect, phase.Name, i + 1, problems);
            }
        }
        return problems;
    }

    private void CheckConditions(IEnumerable<StateCondition> conditions, string phase, int? step, List<RotationProblem> problems)
    {
        foreach (var condition in conditions)
        {
            switch (condition)
            {
                case AllCondition { Of.Count: 0 } or AnyCondition { Of.Count: 0 }:
                    problems.Add(new RotationProblem(RotationProblemKind.EmptyConditionGroup, phase, step));
                    break;
                case AllCondition all:
                    CheckConditions(all.Of, phase, step, problems);
                    break;
                case AnyCondition any:
                    CheckConditions(any.Of, phase, step, problems);
                    break;
                default:
                    string? who = condition switch
                    {
                        ComboCondition c => c.OperatorId,
                        UltimateCondition u => u.OperatorId,
                        UltimateChargeCondition u => u.OperatorId,
                        ComboChargeCondition c => c.OperatorId,
                        BattleSkillCondition b => b.OperatorId,
                        CounterCondition c => c.OperatorId,
                        ControllingCondition c => c.OperatorId,
                        _ => null,
                    };
                    if (who is not null && !Squad.Contains(who))
                        problems.Add(new RotationProblem(RotationProblemKind.ConditionOperatorNotInSquad, phase, step));
                    break;
            }
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
}

/// <param name="Phase">The phase concerned (its name), if any.</param>
/// <param name="Step">The step concerned, from 1, if any (null: the phase's entry condition, or the phase itself).</param>
public sealed record RotationProblem(RotationProblemKind Kind, string? Phase = null, int? Step = null);
