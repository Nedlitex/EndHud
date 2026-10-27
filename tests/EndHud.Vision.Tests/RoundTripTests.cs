using System.Globalization;
using EndHud.Vision.Alerts;
using EndHud.Vision.Rotations;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// GitHub issue 6 (2026-10-27): an exported rotation would not import again. The rotation and alert rules that hold
/// everything (<see cref="EverythingRotation"/>) are checked to be honest here (no problems, every kind in every host it is
/// offered in), and written and read back by the formats unchanged in any culture. The editors' own export and import
/// are in EndHud.Tests (ExportImportRoundTripTests).
/// </summary>
public class RoundTripTests
{
    /// <summary>A kind added to the catalogue needs a sample, or the round trips would silently leave it out.</summary>
    [Fact]
    public void Every_condition_kind_has_samples()
    {
        var missing = ConditionCatalogue.Kinds.Where(k => !EverythingRotation.Samples.ContainsKey(k.Type)).Select(k => k.Id).ToList();

        Assert.True(missing.Count == 0, $"no sample in EverythingRotation.Samples for: {string.Join(", ", missing)}");
        Assert.Equal(ConditionCatalogue.Kinds.Select(k => k.Type).ToHashSet(), EverythingRotation.Samples.Keys.ToHashSet());
    }

    /// <summary>A kind with a comparison is sampled with every comparison (a "below"/"above" needs format 4: written and read the same).</summary>
    [Fact]
    public void Every_comparison_is_sampled()
    {
        var place = new SamplePlace("Si", "p", [1, 2]);
        foreach (var kind in ConditionCatalogue.Kinds)
        {
            var samples = EverythingRotation.Samples[kind.Type](place);
            Assert.True(samples.Count > 0, kind.Id);
            Assert.All(samples, s => Assert.Equal(kind.Type, s.GetType()));
            var comparisons = samples.Select(kind.ComparisonOf).OfType<Comparison>().ToHashSet();
            if (comparisons.Count > 0) Assert.True(comparisons.SetEquals(Enum.GetValues<Comparison>()), kind.Id);
        }
    }

    /// <summary>
    /// The rotation that holds everything is one the editor would save: no problems. And it does hold everything: every
    /// kind in every host the catalogue offers it in, every action, an attack by whoever is controlled, a condition step
    /// used with another step, priorities, "the last phase that holds wins", in the newest format.
    /// </summary>
    [Fact]
    public void The_everything_rotation_has_no_problems_and_holds_every_kind_in_every_host()
    {
        var rotation = EverythingRotation.Build();

        Assert.Empty(rotation.Problems());
        var steps = rotation.Phases.SelectMany(p => p.Steps).ToList();
        var inHost = new Dictionary<ConditionHostKind, IEnumerable<StateCondition>>
        {
            [ConditionHostKind.PhaseGate] = rotation.Phases.SelectMany(p => p.When),
            [ConditionHostKind.PriorityRule] = rotation.Phases.SelectMany(p => p.PriorityRules.SelectMany(r => r.When)),
            [ConditionHostKind.PhaseFinish] = rotation.Phases.SelectMany(p => p.Finish),
            [ConditionHostKind.StepExpect] = steps.Where(s => s.Action != StepAction.Condition).SelectMany(s => s.Expect),
            [ConditionHostKind.ConditionStep] = steps.Where(s => s.Action == StepAction.Condition).SelectMany(s => s.Expect),
        };
        foreach (var (host, conditions) in inHost)
        {
            var held = ConditionWalk.Flatten(conditions).Select(c => ConditionCatalogue.Of(c).Id).ToHashSet();
            var offered = ConditionCatalogue.Kinds.Where(k => k.Hosts.Contains(host)).Select(k => k.Id);
            Assert.True(offered.All(held.Contains), $"{host} lacks {string.Join(", ", offered.Where(k => !held.Contains(k)))}");
        }
        Assert.Equal(Enum.GetValues<StepAction>().ToHashSet(), steps.Select(s => s.Action).ToHashSet());
        Assert.Contains(steps, s => s.OperatorId == RotationStep.Controlled);
        Assert.Contains(rotation.Phases, p => p.Steps.Where((s, i) => i > 0 && s.WithPrevious && s.Action == StepAction.Condition).Any());
        Assert.Contains(rotation.Phases, p => p.Priority != 0 && p.PriorityRules.Count > 1);
        Assert.True(rotation.LastMatchWins);
        Assert.True(rotation.Format == Rotation.CurrentFormat, $"format {rotation.Format}: a newer format's feature is missing from EverythingRotation");
    }

    /// <summary>The alert rules that hold everything: no problems, every kind offered in alerts, rules tied to a squad, as many as are kept, in the newest format.</summary>
    [Fact]
    public void The_everything_alert_rules_have_no_problems_and_hold_every_alert_kind()
    {
        var rules = EverythingRotation.AlertRules();

        Assert.All(rules, r => Assert.Empty(r.Problems()));
        var held = ConditionWalk.Flatten(rules.SelectMany(r => r.When)).Select(c => ConditionCatalogue.Of(c).Id).ToHashSet();
        Assert.All(ConditionCatalogue.Kinds.Where(k => k.Hosts.Contains(ConditionHostKind.Alert)), k => Assert.Contains(k.Id, held));
        Assert.Contains(rules, r => r.Squad is { Count: 2 });
        Assert.Equal(AlertFile.MaxRules, rules.Count);
        Assert.Equal(AlertFile.CurrentFormat, AlertFile.FormatNeeded(rules));
    }

    public static TheoryData<string> Cultures() => new() { "de-DE", "zh-CN", "en-US" };

    /// <summary>
    /// The rotation that holds everything, written and read back, as a file and as a share code, in a culture with comma
    /// decimals (de-DE) and the players' own (zh-CN): the same text again.
    /// </summary>
    [Theory]
    [MemberData(nameof(Cultures))]
    public void The_everything_rotation_reads_back_the_same_in_any_culture(string culture)
    {
        InCulture(culture, () =>
        {
            var rotation = EverythingRotation.Build();
            string json = RotationFormat.ToJson(rotation);

            Assert.Equal(json, RotationFormat.ToJson(RotationFormat.FromJson(json)));
            Assert.Equal(json, RotationFormat.ToJson(RotationFormat.FromShareCode(RotationFormat.ToShareCode(rotation))));
            Assert.Equal(rotation.Phases.SelectMany(p => p.Steps), RotationFormat.FromJson(json).Phases.SelectMany(p => p.Steps), StepComparer.Instance);
        });
    }

    /// <summary>The alert rules that hold everything, written and read back, as a file and as a share code, in any culture: the same text again.</summary>
    [Theory]
    [MemberData(nameof(Cultures))]
    public void The_everything_alert_rules_read_back_the_same_in_any_culture(string culture)
    {
        InCulture(culture, () =>
        {
            var rules = EverythingRotation.AlertRules();
            string json = AlertFormat.ToJson(rules);

            Assert.Equal(json, AlertFormat.ToJson(AlertFormat.FromJson(json)));
            Assert.Equal(json, AlertFormat.ToJson(AlertFormat.FromShareCode(AlertFormat.ToShareCode(rules))));
            Assert.Equal(rules, AlertFormat.FromJson(json));
        });
    }

    private static void InCulture(string name, Action act)
    {
        var (culture, ui) = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);
            act();
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = (culture, ui);
        }
    }

    /// <summary>Steps by value: <see cref="RotationStep"/>'s own equality compares its condition lists by reference.</summary>
    private sealed class StepComparer : IEqualityComparer<RotationStep>
    {
        public static readonly StepComparer Instance = new();

        private static readonly StateCondition[] None = [];

        public bool Equals(RotationStep? a, RotationStep? b) =>
            a is not null && b is not null && a with { Expect = None } == b with { Expect = None } && a.Expect.SequenceEqual(b.Expect);

        public int GetHashCode(RotationStep step) => HashCode.Combine(step.OperatorId, step.Action);
    }
}
