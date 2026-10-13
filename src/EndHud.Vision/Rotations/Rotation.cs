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

/// <summary>One fact about the game that a step's expected state can require.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(SkillPointsCondition), "skillPoints")]
[JsonDerivedType(typeof(ComboCondition), "combo")]
[JsonDerivedType(typeof(UltimateCondition), "ultimate")]
[JsonDerivedType(typeof(CounterCondition), "counter")]
[JsonDerivedType(typeof(BossStatusCondition), "bossStatus")]
[JsonDerivedType(typeof(ControllingCondition), "controlling")]
public abstract record StateCondition;

/// <summary>The team's skill points (技力), 0–3; a part-filled segment counts as a fraction.</summary>
public sealed record SkillPointsCondition(Comparison Is, double Value) : StateCondition;

/// <summary>An operator's combo skill is ready (or cooling, when <paramref name="Ready"/> is false).</summary>
public sealed record ComboCondition(string OperatorId, bool Ready) : StateCondition;

/// <summary>An operator's ultimate is ready (or not).</summary>
public sealed record UltimateCondition(string OperatorId, bool Ready) : StateCondition;

/// <summary>An operator's own counter, e.g. Laevatain's 熔火 at 4 (see OperatorCounters).</summary>
public sealed record CounterCondition(string OperatorId, string CounterId, Comparison Is, int Value) : StateCondition;

/// <summary>The enemy has (or doesn't have) a status, by its wiki name, e.g. "Heat Infliction" at 2 or more stacks.</summary>
public sealed record BossStatusCondition(string StatusId, bool Present, int MinStacks = 1) : StateCondition;

/// <summary>This operator is the one being controlled.</summary>
public sealed record ControllingCondition(string OperatorId) : StateCondition;

/// <summary>One step of a rotation.</summary>
/// <param name="Expect">What the game should look like when it's time for this step. The step's own
/// ability being available (combo or ultimate ready, a skill point for a battle skill) is implied.</param>
/// <param name="Note">Shown under the icon, e.g. "飞天".</param>
/// <param name="Label">Replaces the action's name above the icon, e.g. "战技·意".</param>
/// <param name="WithPrevious">Used right after the previous step (drawn overlapping it, like 连携技连携技).</param>
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

/// <summary>A reusable part of a rotation, e.g. "充能轴".</summary>
public sealed record RotationPhase(string Id, string Name, IReadOnlyList<RotationStep> Steps);

/// <summary>Play a phase this many times in a row.</summary>
public sealed record PhaseUse(string PhaseId, int Times = 1);

/// <summary>A step in the order it's played, with where it comes from (for the strip and the tracker).</summary>
/// <param name="Index">Position in the full rotation, from 0.</param>
/// <param name="PhaseLabel">The phase as shown: its name and its place in the full rotation, e.g. "充能轴 II".</param>
/// <param name="StepInPhase">Position inside the phase, from 0.</param>
public sealed record PlayedStep(int Index, string PhaseLabel, int StepInPhase, RotationStep Step);

/// <summary>
/// A rotation for one squad: reusable phases, played in a sequence (e.g. 充能轴 ×2, then 爆发轴 ×1),
/// over and over. There's no start or reset: EndHud finds the step whose expected state matches the
/// game best (RotationTracker).
/// </summary>
/// <param name="Format">The file format's version; this build reads <see cref="CurrentFormat"/>.</param>
/// <param name="Squad">The four operators it's for, in any slot order.</param>
public sealed record Rotation(
    int Format,
    string Id,
    string Name,
    IReadOnlyList<string> Squad,
    IReadOnlyList<RotationPhase> Phases,
    IReadOnlyList<PhaseUse> Sequence)
{
    public const int CurrentFormat = 1;
    public const int MaxTimes = 9;

    /// <summary>Is this rotation for this squad: the same four operators, in any order?</summary>
    public bool IsFor(IEnumerable<string?> squad)
    {
        var ids = squad.Where(id => id is not null).Select(id => id!).ToHashSet(StringComparer.Ordinal);
        return ids.Count == Squad.Count && Squad.All(ids.Contains);
    }

    public RotationPhase? Phase(string id) => Phases.FirstOrDefault(p => p.Id == id);

    /// <summary>
    /// Every step in the order it's played: the phases of <see cref="Sequence"/>, each repeated its
    /// number of times. Phases are numbered by their place in the whole rotation, as guides do
    /// ("充能轴 I", "充能轴 II", "爆发轴 III").
    /// </summary>
    public IReadOnlyList<PlayedStep> Played()
    {
        var played = new List<PlayedStep>();
        int phaseNumber = 0;
        foreach (var use in Sequence)
        {
            if (Phase(use.PhaseId) is not { } phase) continue;
            for (int time = 0; time < use.Times; time++)
            {
                string label = $"{phase.Name} {Roman(++phaseNumber)}";
                for (int i = 0; i < phase.Steps.Count; i++)
                    played.Add(new PlayedStep(played.Count, label, i, phase.Steps[i]));
            }
        }
        return played;
    }

    /// <summary>What's wrong with it, if anything (the editor turns these into sentences).</summary>
    public IReadOnlyList<RotationProblem> Problems()
    {
        var problems = new List<RotationProblem>();
        if (Squad.Count != 4 || Squad.Distinct(StringComparer.Ordinal).Count() != 4)
            problems.Add(new RotationProblem(RotationProblemKind.SquadNotFourOperators));
        if (Sequence.Count == 0)
            problems.Add(new RotationProblem(RotationProblemKind.NoPhasesInSequence));
        foreach (var use in Sequence)
        {
            if (Phase(use.PhaseId) is null)
                problems.Add(new RotationProblem(RotationProblemKind.UnknownPhase, use.PhaseId));
            if (use.Times is < 1 or > MaxTimes)
                problems.Add(new RotationProblem(RotationProblemKind.TimesOutOfRange, Phase(use.PhaseId)?.Name ?? use.PhaseId));
        }
        foreach (var phase in Phases)
        {
            if (phase.Steps.Count == 0)
                problems.Add(new RotationProblem(RotationProblemKind.PhaseHasNoSteps, phase.Name));
            for (int i = 0; i < phase.Steps.Count; i++)
            {
                var step = phase.Steps[i];
                if (!Squad.Contains(step.OperatorId))
                    problems.Add(new RotationProblem(RotationProblemKind.StepOperatorNotInSquad, phase.Name, i + 1));
                foreach (var condition in step.Expect)
                {
                    string? who = condition switch
                    {
                        ComboCondition c => c.OperatorId,
                        UltimateCondition u => u.OperatorId,
                        CounterCondition c => c.OperatorId,
                        ControllingCondition c => c.OperatorId,
                        _ => null,
                    };
                    if (who is not null && !Squad.Contains(who))
                        problems.Add(new RotationProblem(RotationProblemKind.ConditionOperatorNotInSquad, phase.Name, i + 1));
                }
            }
        }
        return problems;
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
    NoPhasesInSequence,
    UnknownPhase,
    TimesOutOfRange,
    PhaseHasNoSteps,
    StepOperatorNotInSquad,
    ConditionOperatorNotInSquad,
}

/// <param name="Phase">The phase concerned (its name), if any.</param>
/// <param name="Step">The step concerned, from 1, if any.</param>
public sealed record RotationProblem(RotationProblemKind Kind, string? Phase = null, int? Step = null);
