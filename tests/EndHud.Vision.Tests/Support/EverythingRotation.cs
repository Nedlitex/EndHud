using EndHud.Vision.Alerts;
using EndHud.Vision.Operators;
using EndHud.Vision.Rotations;

namespace EndHud.Vision.Tests;

/// <summary>Where a sample condition is put: whom it names, which phase, and the owning phase's steps that light up (numbered from 1).</summary>
internal sealed record SamplePlace(string Who, string Phase, IReadOnlyList<int> LitSteps);

/// <summary>
/// A rotation, and alert rules, holding everything the files can hold (GitHub issue 6, 2026-10-27: an exported rotation
/// would not import again). Generated from the condition catalogue (<see cref="ConditionCatalogue.Kinds"/>): every kind,
/// in every value its fields take, in every host the catalogue offers it in (an entry condition, priority rules, a finish
/// condition, a condition step, a step's expected state, an alert), and groups nested in each other; every step action
/// (<see cref="StepAction"/>), attacks by whoever is controlled, condition steps used with the step before, notes and labels
/// with quotes, backslashes, an emoji and Chinese characters. A kind added to the catalogue is put everywhere it is offered
/// without a change here, once it has a sample (<see cref="Samples"/>; a test fails while one is missing).
/// Shared by both test assemblies (EndHud.Tests links this file).
/// </summary>
internal static class EverythingRotation
{
    /// <summary>Si (whose battle-skill time is read) and Laevatain (whose lasting ultimate is read), so every kind can name someone.</summary>
    public static readonly string[] Squad = ["Si", "Laevatain", "Arcane", "Tangtang"];

    /// <summary>
    /// Text that has broken serializers before: quotes, a backslash, a path, markup, an emoji, and Chinese (the escapes are
    /// "burst phase" and "flying" in Chinese, as players write them).
    /// </summary>
    public const string Awkward = "say \"now\" \\ C:\\temp\\ </script> & 'x' \U0001F525 \u7206\u53D1\u8F74 \u98DE\u5929";

    private const string Opener = "opener", Burst = "burst", Loop = "loop";

    private static readonly Comparison[] Comparisons = Enum.GetValues<Comparison>();

    /// <summary>One condition per comparison, each with another number (from <paramref name="number"/>).</summary>
    private static IReadOnlyList<StateCondition> EveryComparison(Func<Comparison, int, StateCondition> make) =>
        Comparisons.Select((c, i) => make(c, i)).ToList();

    /// <summary>
    /// What each kind of condition looks like, in every value its fields take (every comparison, true and false, null where
    /// it may be). Keyed by the kind's type: a kind in the catalogue without an entry here fails
    /// <c>Every_condition_kind_has_samples</c>, so a new kind can't be left out of the round trips.
    /// </summary>
    public static IReadOnlyDictionary<Type, Func<SamplePlace, IReadOnlyList<StateCondition>>> Samples { get; } =
        new Dictionary<Type, Func<SamplePlace, IReadOnlyList<StateCondition>>>
        {
            [typeof(SkillPointsCondition)] = _ => EveryComparison((c, i) => new SkillPointsCondition(c, i * 0.75 + 0.25)),
            [typeof(ComboCondition)] = p => [new ComboCondition(p.Who, true), new ComboCondition(p.Who, false)],
            [typeof(ComboChargeCondition)] = p => EveryComparison((c, i) => new ComboChargeCondition(p.Who, c, i * 25)),
            [typeof(BattleSkillCondition)] = p => [new BattleSkillCondition(p.Who, true), new BattleSkillCondition(p.Who, false)],
            [typeof(UltimateCondition)] = p => [new UltimateCondition(p.Who, true), new UltimateCondition(p.Who, false)],
            [typeof(UltimateChargeCondition)] = p => EveryComparison((c, i) => new UltimateChargeCondition(p.Who, c, 100 - i * 20)),
            [typeof(UltimateActiveCondition)] = p => [new UltimateActiveCondition(p.Who, true), new UltimateActiveCondition(p.Who, false)],
            [typeof(UltimateTimeLeftCondition)] = p => EveryComparison((c, i) => new UltimateTimeLeftCondition(p.Who, c, i * 15)),
            [typeof(BattleSkillActiveCondition)] = p => [new BattleSkillActiveCondition(p.Who, true), new BattleSkillActiveCondition(p.Who, false)],
            [typeof(BattleSkillEmpoweredCondition)] = p => [new BattleSkillEmpoweredCondition(p.Who, true), new BattleSkillEmpoweredCondition(p.Who, false)],
            // A counter the squad has (Si's damage stacks, Laevatain's Melting Flame), whoever the place names.
            [typeof(CounterCondition)] = _ => OperatorCounters.Definitions.Where(d => Squad.Contains(d.OperatorId))
                .SelectMany(d => EveryComparison((c, i) => new CounterCondition(d.OperatorId, d.Id, c, i))).ToList(),
            [typeof(TeamBuffCondition)] = _ => TeamBuffReader.Buffs.SelectMany(b => EveryComparison((c, i) => new TeamBuffCondition(b, c, i))).ToList(),
            [typeof(BossStatusCondition)] = _ => [new BossStatusCondition("Heat Infliction", true, 3), new BossStatusCondition("Cryo Infliction", false)],
            [typeof(StaggerProgressCondition)] = _ => EveryComparison((c, i) => new StaggerProgressCondition(c, i * 20)),
            [typeof(StaggerNodesCondition)] = _ => EveryComparison((c, i) => new StaggerNodesCondition(c, i)),
            [typeof(StaggeredCondition)] = _ => [new StaggeredCondition(true), new StaggeredCondition(false)],
            [typeof(StaggerTimeLeftCondition)] = _ => EveryComparison((c, i) => new StaggerTimeLeftCondition(c, 100 - i * 10)),
            [typeof(ControllingCondition)] = p => [new ControllingCondition(p.Who)],
            [typeof(PhaseShownCondition)] = p => EveryComparison((c, i) => new PhaseShownCondition(p.Phase, c, i + 1)),
            [typeof(PhasePlayedCondition)] = p => EveryComparison((c, i) => new PhasePlayedCondition(p.Phase, c, i)),
            [typeof(PhaseCurrentCondition)] = p => [new PhaseCurrentCondition(p.Phase), new PhaseCurrentCondition(p.Phase, true), new PhaseCurrentCondition(p.Phase, false)],
            [typeof(StepsLitCondition)] = p =>
                [new StepsLitCondition(StepsLitWhich.All), new StepsLitCondition(StepsLitWhich.AtLeast, 1),
                 new StepsLitCondition(StepsLitWhich.AtLeast, p.LitSteps.Count), new StepsLitCondition(StepsLitWhich.These, Steps: p.LitSteps)],
            // Groups in groups, five deep, and "not" around a group and inside one.
            [typeof(AllCondition)] = p =>
                [new AllCondition([new SkillPointsCondition(Comparison.AtLeast, 1.5), new NotCondition(new ComboCondition(p.Who, true))]),
                 new AllCondition([new AnyCondition([new NotCondition(new AllCondition([new StaggeredCondition(true), new NotCondition(new BossStatusCondition("Corrosion", true, 2))]))]),
                     new ControllingCondition(p.Who)])],
            [typeof(AnyCondition)] = p =>
                [new AnyCondition([new UltimateCondition(p.Who, true), new AllCondition([new SkillPointsCondition(Comparison.Below, 2), new BattleSkillCondition(p.Who, false)])])],
            [typeof(NotCondition)] = p =>
                [new NotCondition(new SkillPointsCondition(Comparison.Above, 2.5)),
                 new NotCondition(new AnyCondition([new AllCondition([new ComboCondition(p.Who, false), new NotCondition(new StaggerNodesCondition(Comparison.AtLeast, 1))])]))],
        };

    /// <summary>Whom a kind's samples name: someone it can be told for (<see cref="ConditionKind.OnlyFor"/>), else a squad member by the kind's place in the catalogue.</summary>
    private static string Who(ConditionKind kind, int index) =>
        kind.OnlyFor is { } only ? Squad.First(only.Contains) : Squad[index % Squad.Length];

    /// <summary>Every kind offered in <paramref name="host"/>, in every sample, in the catalogue's order.</summary>
    public static IReadOnlyList<StateCondition> Conditions(ConditionHostKind host, string phase, IReadOnlyList<int>? litSteps = null) =>
        ConditionCatalogue.Kinds.Select((kind, i) => (kind, i)).Where(k => k.kind.Hosts.Contains(host))
            .SelectMany(k => SamplesOf(k.kind, new SamplePlace(Who(k.kind, k.i), phase, litSteps ?? []))).ToList();

    private static IReadOnlyList<StateCondition> SamplesOf(ConditionKind kind, SamplePlace place) =>
        Samples.TryGetValue(kind.Type, out var make) ? make(place)
            : throw new InvalidOperationException($"No sample of the condition kind {kind.Id}: add one to {nameof(EverythingRotation)}.{nameof(Samples)}.");

    /// <summary>The steps of a phase that light up (not a basic, heavy or plunge attack), numbered from 1.</summary>
    private static IReadOnlyList<int> Lit(IReadOnlyList<RotationStep> steps) =>
        steps.Select((s, i) => (s, i)).Where(x => !x.s.Action.IsDisplayOnly()).Select(x => x.i + 1).ToList();

    /// <summary>
    /// Three phases: the opener (in the flow: every action, attacks by whoever is controlled, a condition step used with the
    /// step before, a step expecting every kind, a finish condition of every kind), the burst (an entry condition of every
    /// kind, a priority and two rules, one of every kind), and a loop (in the flow, named awkwardly). "The last phase that holds wins" on.
    /// </summary>
    public static Rotation Build()
    {
        var opener = new List<RotationStep>();
        int n = 0;
        foreach (var action in Enum.GetValues<StepAction>())
        {
            if (action == StepAction.Condition)
            {
                // A condition step of every kind offered there, used with the step before it (format 12).
                opener.Add(new RotationStep("", action, Conditions(ConditionHostKind.ConditionStep, Burst), Label: Awkward, WithPrevious: opener.Count > 0));
                continue;
            }
            opener.Add(new RotationStep(Squad[n++ % Squad.Length], action, Note: $"{action} {Awkward}", Label: $"{action} \u6218\u6280"));
            // An attack by whoever is controlled, used with the step before.
            if (action.IsDisplayOnly()) opener.Add(new RotationStep(RotationStep.Controlled, action, Note: Awkward, WithPrevious: true));
        }
        opener.Add(new RotationStep(Squad[1], StepAction.Ultimate, Conditions(ConditionHostKind.StepExpect, Burst), Note: "\u2014\u00A0\t\u3000end"));
        // A condition step alone, and the step after it used with it.
        opener.Add(new RotationStep("", StepAction.Condition, [new BossStatusCondition("Vulnerable", true, 4)]));
        opener.Add(new RotationStep(RotationStep.Controlled, StepAction.HeavyAttack, WithPrevious: true));

        var burst = new List<RotationStep>
        {
            new("", StepAction.Condition, [new StaggeredCondition(true)], Note: "first"),
            new(RotationStep.Controlled, StepAction.PlungeAttack, WithPrevious: true),
            new(Squad[1], StepAction.Ultimate, [new UltimateCondition(Squad[1], true)]),
            new(Squad[0], StepAction.Combo, WithPrevious: true),
        };

        RotationPhase[] phases =
        [
            new RotationPhase(Opener, "Opener " + Awkward, opener) { Finish = Conditions(ConditionHostKind.PhaseFinish, Burst, Lit(opener)) },
            new RotationPhase(Burst, "\u7206\u53D1\u8F74", burst, Conditions(ConditionHostKind.PhaseGate, Opener))
            {
                Priority = 5,
                PriorityRules =
                [
                    new PriorityRule(Conditions(ConditionHostKind.PriorityRule, Opener), 10),
                    new PriorityRule([new NotCondition(new AnyCondition([new UltimateActiveCondition(Squad[1], true), new PhaseCurrentCondition(Loop, true)]))], -3),
                ],
            },
            new RotationPhase(Loop, "\u5FAA\u73AF \"loop\" \\", [new RotationStep(RotationStep.Controlled, StepAction.BasicAttack, Note: Awkward)]),
        ];
        return new Rotation(Rotation.FormatNeeded(phases, lastMatchWins: true), "everything", "Everything " + Awkward, Squad, phases, LastMatchWins: true);
    }

    /// <summary>
    /// Alert rules: one per kind offered in alerts (its every sample), a rule tied to two operators and one to a whole squad,
    /// one with no words (said by its conditions), and plain ones up to the most kept (<see cref="AlertFile.MaxRules"/>).
    /// </summary>
    public static IReadOnlyList<AlertRule> AlertRules()
    {
        var rules = ConditionCatalogue.Kinds.Select((kind, i) => (kind, i)).Where(k => k.kind.Hosts.Contains(ConditionHostKind.Alert))
            .Select(k => new AlertRule($"kind-{k.kind.Id}", SamplesOf(k.kind, new SamplePlace(Who(k.kind, k.i), Opener, [])), $"{k.kind.Id}: {Awkward}"))
            .ToList();
        rules.Add(new AlertRule("two", [new ComboCondition(Squad[0], true)], "Si and Laevatain only " + Awkward) { Squad = [Squad[0], Squad[1]] });
        rules.Add(new AlertRule("four", [new UltimateCondition(Squad[1], true)], "this squad only") { Squad = [.. Squad] });
        rules.Add(new AlertRule("wordless", [new StaggeredCondition(true)], ""));
        for (int i = 0; rules.Count < AlertFile.MaxRules; i++)
            rules.Add(new AlertRule($"plain-{i}", [new SkillPointsCondition(Comparisons[i % Comparisons.Length], i % 4)], $"Rule {i}"));
        return rules;
    }
}
