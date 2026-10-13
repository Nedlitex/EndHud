using EndHud.Vision.Rotations;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>The rotation format: phases and sequence, squad matching, problems, files, share codes, the store.</summary>
public sealed class RotationFormatTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "endhud-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    /// <summary>Shaped like the guide: two charging phases, then a burst phase.</summary>
    internal static Rotation Guide() => new(
        Rotation.CurrentFormat, "guide", "标准轴", ["Laevatain", "Camille", "Arcane", "Ardelia"],
        [
            new RotationPhase("charge", "充能轴",
            [
                new RotationStep("Ardelia", StepAction.BattleSkill, [new SkillPointsCondition(Comparison.AtLeast, 2)]),
                new RotationStep("Camille", StepAction.Combo),
                new RotationStep("Arcane", StepAction.Combo, WithPrevious: true),
                new RotationStep("Laevatain", StepAction.BattleSkill,
                    [new BossStatusCondition("Heat Infliction", true, 2), new CounterCondition("Laevatain", "melting-flame", Comparison.AtMost, 3)],
                    Note: "飞天", Label: "战技·意"),
            ]),
            new RotationPhase("burst", "爆发轴",
            [
                new RotationStep("Laevatain", StepAction.Ultimate, [new ControllingCondition("Laevatain"), new UltimateCondition("Laevatain", true)]),
                new RotationStep("Ardelia", StepAction.Combo, [new ComboCondition("Ardelia", true)]),
            ]),
        ],
        [new PhaseUse("charge", 2), new PhaseUse("burst", 1)]);

    [Fact]
    public void Phases_are_played_in_sequence_and_numbered_across_the_whole_rotation()
    {
        var played = Guide().Played();

        Assert.Equal(4 + 4 + 2, played.Count);
        Assert.Equal(["充能轴 I", "充能轴 II", "爆发轴 III"], played.Select(p => p.PhaseLabel).Distinct());
        Assert.Equal(Enumerable.Range(0, played.Count), played.Select(p => p.Index));
        Assert.Same(played[0].Step, played[4].Step); // the same phase, used twice: one definition
    }

    [Fact]
    public void Editing_a_phase_changes_every_use_of_it()
    {
        var rotation = Guide();
        var charge = rotation.Phase("charge")!;
        var edited = rotation with
        {
            Phases = [charge with { Steps = [.. charge.Steps, new RotationStep("Arcane", StepAction.Ultimate)] }, rotation.Phase("burst")!],
        };

        var played = edited.Played();
        Assert.Equal(5 + 5 + 2, played.Count);
        Assert.Equal(StepAction.Ultimate, played[4].Step.Action);
        Assert.Equal(StepAction.Ultimate, played[9].Step.Action);
    }

    [Theory]
    [InlineData(new[] { "Laevatain", "Camille", "Arcane", "Ardelia" }, true)]
    [InlineData(new[] { "Ardelia", "Arcane", "Laevatain", "Camille" }, true)] // any slot order
    [InlineData(new[] { "Laevatain", "Camille", "Arcane", "Perlica" }, false)]
    [InlineData(new[] { "Laevatain", "Camille", "Arcane" }, false)]
    public void A_rotation_is_for_the_same_four_operators_in_any_order(string[] squad, bool expected) =>
        Assert.Equal(expected, Guide().IsFor(squad));

    [Fact]
    public void An_unrecognised_slot_never_matches()
    {
        Assert.False(Guide().IsFor(["Laevatain", "Camille", "Arcane", null]));
    }

    [Fact]
    public void A_good_rotation_has_no_problems_and_a_broken_one_lists_each()
    {
        Assert.Empty(Guide().Problems());

        var guide = Guide();
        var broken = guide with
        {
            Squad = ["Laevatain", "Camille", "Arcane"],
            Phases = [.. guide.Phases, new RotationPhase("empty", "空轴", [])],
            Sequence = [.. guide.Sequence, new PhaseUse("missing"), new PhaseUse("burst", 0)],
        };
        var kinds = broken.Problems().Select(p => p.Kind).ToList();

        Assert.Contains(RotationProblemKind.SquadNotFourOperators, kinds);
        Assert.Contains(RotationProblemKind.PhaseHasNoSteps, kinds);
        Assert.Contains(RotationProblemKind.UnknownPhase, kinds);
        Assert.Contains(RotationProblemKind.TimesOutOfRange, kinds);
        Assert.Contains(new RotationProblem(RotationProblemKind.StepOperatorNotInSquad, "充能轴", 1), broken.Problems()); // Ardelia left the squad
    }

    [Fact]
    public void A_file_and_a_share_code_both_give_back_the_same_rotation()
    {
        var guide = Guide();

        var fromFile = RotationFormat.FromJson(RotationFormat.ToJson(guide));
        var fromCode = RotationFormat.FromShareCode(RotationFormat.ToShareCode(guide));

        Assert.Equal(RotationFormat.ToJson(guide), RotationFormat.ToJson(fromFile));
        Assert.Equal(RotationFormat.ToJson(guide), RotationFormat.ToJson(fromCode));
        Assert.Contains("标准轴", RotationFormat.ToJson(guide)); // readable, not escaped
    }

    [Fact]
    public void A_share_code_survives_being_wrapped_in_a_chat()
    {
        string code = RotationFormat.ToShareCode(Guide());
        string wrapped = string.Join("\n", code.Chunk(40).Select(c => new string(c)));

        Assert.Equal("标准轴", RotationFormat.FromShareCode(wrapped).Name);
        Assert.True(code.Length < 1000, $"{code.Length} characters");
    }

    [Theory]
    [InlineData("hello", RotationReadError.NotARotation)]
    [InlineData("EHR2:abc", RotationReadError.NewerFormat)]
    public void Something_that_isnt_a_share_code_is_refused_with_a_reason(string code, RotationReadError expected) =>
        Assert.Equal(expected, Assert.Throws<RotationReadException>(() => RotationFormat.FromShareCode(code)).Error);

    [Fact]
    public void A_share_code_cut_short_is_refused_as_damaged()
    {
        string code = RotationFormat.ToShareCode(Guide());
        var error = Assert.Throws<RotationReadException>(() => RotationFormat.FromShareCode(code[..(code.Length / 2)])).Error;
        Assert.Equal(RotationReadError.DamagedShareCode, error);
    }

    [Theory]
    [InlineData("""{ "name": "x" }""", RotationReadError.NotARotation)]
    [InlineData("not json", RotationReadError.NotARotation)]
    [InlineData("""{ "format": 2, "id": "x" }""", RotationReadError.NewerFormat)]
    public void A_file_that_isnt_a_rotation_this_build_can_read_is_refused(string json, RotationReadError expected) =>
        Assert.Equal(expected, Assert.Throws<RotationReadException>(() => RotationFormat.FromJson(json)).Error);

    [Fact]
    public void The_store_saves_corrects_lists_and_deletes()
    {
        var store = new RotationStore(_folder);
        var guide = Guide();

        store.Save(guide);
        store.Save(guide with { Name = "改过的轴" }); // correcting rewrites the same file

        var (rotations, unreadable) = store.Load();
        Assert.Equal("改过的轴", Assert.Single(rotations).Name);
        Assert.Empty(unreadable);

        Assert.True(store.Delete(guide.Id));
        Assert.Empty(store.Load().Rotations);
        Assert.False(store.Delete(guide.Id));
    }

    [Fact]
    public void An_import_never_overwrites_and_a_copy_keeps_both()
    {
        var store = new RotationStore(_folder);
        store.Save(Guide());

        var incoming = Guide() with { Name = "别人的轴" };
        Assert.False(store.Import(incoming)); // same id: the player chooses
        Assert.Equal("标准轴", Assert.Single(store.Load().Rotations).Name);

        Assert.True(store.Import(RotationStore.AsCopy(incoming, "别人的轴（副本）")));
        Assert.Equal(["别人的轴（副本）", "标准轴"], store.Load().Rotations.Select(r => r.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_file_that_cant_be_read_is_listed_with_why_not_dropped()
    {
        var store = new RotationStore(_folder);
        store.Save(Guide());
        File.WriteAllText(Path.Combine(_folder, "broken.json"), "{ not json");

        var (rotations, unreadable) = store.Load();

        Assert.Single(rotations);
        var bad = Assert.Single(unreadable);
        Assert.EndsWith("broken.json", bad.File);
        Assert.Equal(RotationReadError.NotARotation, bad.Error);
    }

    [Fact]
    public void The_example_in_the_developer_guide_is_a_valid_rotation()
    {
        string guide = File.ReadAllText(Path.Combine(OperatorCache.RepositoryRoot(), "DEVELOPER.md"));
        int start = guide.IndexOf("```json", StringComparison.Ordinal) + "```json".Length;
        string json = guide[start..guide.IndexOf("```", start, StringComparison.Ordinal)];

        var rotation = RotationFormat.FromJson(json);

        Assert.Empty(rotation.Problems());
        Assert.Equal(["Laevatain", "Camille", "Arcane", "Ardelia"], rotation.Squad);
        Assert.Equal(9, rotation.Played().Count); // 充能轴 (4 steps) twice, then 爆发轴 (1 step)
        var flame = rotation.Phases[0].Steps[3];
        Assert.Equal([new BossStatusCondition("Heat Infliction", true), new CounterCondition("Laevatain", "melting-flame", Comparison.AtMost, 3)], flame.Expect);
        Assert.True(rotation.Phases[0].Steps[2].WithPrevious);
    }
}
