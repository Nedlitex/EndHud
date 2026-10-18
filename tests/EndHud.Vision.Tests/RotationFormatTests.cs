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

    private static readonly RotationStep[] ChargeSteps =
    [
        new RotationStep("Ardelia", StepAction.BattleSkill, [new SkillPointsCondition(Comparison.AtLeast, 2)]),
        new RotationStep("Camille", StepAction.Combo),
        new RotationStep("Arcane", StepAction.Combo, WithPrevious: true),
        new RotationStep("Laevatain", StepAction.BattleSkill,
            [new BossStatusCondition("Heat Infliction", true, 2), new CounterCondition("Laevatain", "melting-flame", Comparison.AtMost, 3)],
            Note: "飞天", Label: "战技·意"),
    ];

    /// <summary>Two charging phases, then a burst phase, none with an entry condition: played in order, looping.</summary>
    internal static Rotation Guide() => new(
        Rotation.CurrentFormat, "guide", "标准轴", ["Laevatain", "Camille", "Arcane", "Ardelia"],
        [
            new RotationPhase("charge", "充能轴", ChargeSteps),
            new RotationPhase("charge-2", "充能轴", ChargeSteps),
            new RotationPhase("burst", "爆发轴",
            [
                new RotationStep("Laevatain", StepAction.Ultimate, [new ControllingCondition("Laevatain"), new UltimateCondition("Laevatain", true)]),
                new RotationStep("Ardelia", StepAction.Combo, [new ComboCondition("Ardelia", true)]),
            ]),
        ]);

    [Fact]
    public void Phases_are_listed_in_order_and_numbered_like_the_guides()
    {
        var played = Guide().Played();

        Assert.Equal(4 + 4 + 2, played.Count);
        Assert.Equal(["充能轴 I", "充能轴 II", "爆发轴 III"], played.Select(p => p.PhaseLabel).Distinct());
        Assert.Equal(Enumerable.Range(0, played.Count), played.Select(p => p.Index));
        Assert.Equal([0, 0, 0, 0, 1, 1, 1, 1, 2, 2], played.Select(p => p.PhaseIndex));
    }

    [Fact]
    public void The_flow_skips_phases_with_an_entry_condition_and_wraps_around()
    {
        var guide = Guide();
        var gated = guide with { Phases = [guide.Phases[0], guide.Phases[2] with { When = [new UltimateCondition("Laevatain", true)] }, guide.Phases[1]] };

        Assert.Equal(1, guide.NextInFlow(0));
        Assert.Equal(0, guide.NextInFlow(2)); // the last phase wraps to the first
        Assert.Equal(2, gated.NextInFlow(0)); // over the burst phase: only its condition enters it
        Assert.Equal(2, gated.NextInFlow(1)); // and after it, on to the next phase in the flow
        Assert.Equal(2, gated.PreviousInFlow(0));
        var alone = guide with { Phases = [guide.Phases[0]] };
        Assert.Equal(0, alone.NextInFlow(0)); // a single phase follows itself
    }

    [Fact]
    public void A_rotation_from_before_entry_conditions_plays_the_same_phases_in_the_same_order()
    {
        // Format 1: a fixed sequence (充能轴 ×2, then 爆发轴), with a phase whose id a copy would take.
        string v1 = """
            {"format":1,"id":"old","name":"旧轴","squad":["Laevatain","Camille","Arcane","Ardelia"],
             "phases":[{"id":"charge","name":"充能轴","steps":[{"operatorId":"Camille","action":"Combo"}]},
                       {"id":"charge-2","name":"另一个","steps":[{"operatorId":"Arcane","action":"Combo"}]},
                       {"id":"burst","name":"爆发轴","steps":[{"operatorId":"Laevatain","action":"Ultimate"}]}],
             "sequence":[{"phaseId":"charge","times":2},{"phaseId":"burst","times":1},{"phaseId":"charge-2","times":1}]}
            """;

        var rotation = RotationFormat.FromJson(v1);

        Assert.Equal(2, rotation.Format); // the lowest format that holds it: nothing in it needs format 3
        Assert.Equal(["充能轴", "充能轴", "爆发轴", "另一个"], rotation.Phases.Select(p => p.Name));
        Assert.Equal(["charge", "charge-3", "burst", "charge-2"], rotation.Phases.Select(p => p.Id)); // "charge-2" was taken
        Assert.All(rotation.Phases, p => Assert.False(p.Gated));
        Assert.Empty(rotation.Problems());
        Assert.Equal(rotation.Phases.Count, rotation.Phases.Select(p => p.Id).Distinct().Count());
        Assert.Equal(RotationFormat.ToJson(rotation), RotationFormat.ToJson(RotationFormat.FromShareCode(RotationFormat.ToShareCode(rotation))));
    }

    [Fact]
    public void Entry_conditions_and_nested_and_or_groups_survive_a_file_and_a_share_code()
    {
        var guide = Guide();
        var gate = new AnyCondition([new UltimateCondition("Laevatain", true),
            new AllCondition([new SkillPointsCondition(Comparison.AtLeast, 3), new CounterCondition("Laevatain", "melting-flame", Comparison.Exactly, 4)])]);
        var gated = guide with { Phases = [guide.Phases[0], guide.Phases[2] with { When = [gate] }] };

        string json = RotationFormat.ToJson(gated);
        var back = RotationFormat.FromJson(json);

        Assert.Contains("\"kind\": \"any\"", json);
        Assert.Contains("\"kind\": \"all\"", json);
        Assert.Equal(gate, Assert.Single(back.Phases[1].When));
        Assert.Equal(json, RotationFormat.ToJson(RotationFormat.FromShareCode(RotationFormat.ToShareCode(gated))));
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
            Phases =
            [
                .. guide.Phases,
                new RotationPhase("empty", "空轴", []),
                new RotationPhase("odd", "怪轴", [new RotationStep("Camille", StepAction.Combo, WithPrevious: true)], [new AnyCondition([])]),
            ],
        };
        var kinds = broken.Problems().Select(p => p.Kind).ToList();

        Assert.Contains(RotationProblemKind.SquadNotFourOperators, kinds);
        Assert.Contains(RotationProblemKind.PhaseHasNoSteps, kinds);
        Assert.Contains(RotationProblemKind.FirstStepWithPrevious, kinds);
        Assert.Contains(new RotationProblem(RotationProblemKind.EmptyConditionGroup, "怪轴", null), broken.Problems());
        Assert.Contains(new RotationProblem(RotationProblemKind.StepOperatorNotInSquad, "充能轴", 1), broken.Problems()); // Ardelia left the squad
    }

    [Fact]
    public void Every_phase_may_have_a_condition()
    {
        // The phase shown stays when none holds (see PhaseSelector), so a phase without one isn't needed.
        var guide = Guide();
        var allGated = guide with { Phases = [.. guide.Phases.Select(p => p with { When = [new SkillPointsCondition(Comparison.AtLeast, 1)] })] };

        Assert.Empty(allGated.Problems());
    }

    [Fact]
    public void An_operator_outside_the_squad_in_an_entry_condition_or_a_group_is_pointed_out()
    {
        var guide = Guide();
        var gated = guide with { Phases = [guide.Phases[0], guide.Phases[2] with { When = [new AnyCondition([new UltimateCondition("Perlica", true)])] }] };

        Assert.Contains(new RotationProblem(RotationProblemKind.ConditionOperatorNotInSquad, "爆发轴", null), gated.Problems());
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
    [InlineData("""{ "format": 4, "id": "x" }""", RotationReadError.NewerFormat)]
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
        Assert.Equal(4 + 2 + 1, rotation.Played().Count); // 充能轴 (4 steps), 充能轴 (2), 爆发轴 (1)
        Assert.Equal([false, false, true], rotation.Phases.Select(p => p.Gated));
        var gate = Assert.IsType<AnyCondition>(Assert.Single(rotation.Phases[2].When));
        Assert.IsType<AllCondition>(gate.Of[1]);
        var flame = rotation.Phases[0].Steps[3];
        Assert.Equal([new BossStatusCondition("Heat Infliction", true), new CounterCondition("Laevatain", "melting-flame", Comparison.AtMost, 3)], flame.Expect);
        Assert.True(rotation.Phases[0].Steps[2].WithPrevious);
    }

    /// <summary>
    /// Format 3 adds "not" conditions and "last phase wins". A rotation is saved in the lowest format
    /// that holds it, so one that uses neither stays readable by builds from before format 3 (and its
    /// file has nothing new in it).
    /// </summary>
    [Fact]
    public void Not_conditions_and_last_match_wins_need_format_3_and_nothing_else_does()
    {
        var plain = Guide().Phases;
        RotationPhase[] negated =
        [
            plain[0] with { When = [new NotCondition(new UltimateChargeCondition("Laevatain", Comparison.AtMost, 50))] },
            .. plain.Skip(1),
        ];
        RotationPhase[] nested = [plain[0] with { When = [new AnyCondition([new NotCondition(new UltimateCondition("Laevatain", true))])] }];

        Assert.Equal(2, Rotation.FormatNeeded(plain, lastMatchWins: false));
        Assert.Equal(3, Rotation.FormatNeeded(plain, lastMatchWins: true));
        Assert.Equal(3, Rotation.FormatNeeded(negated, lastMatchWins: false));
        Assert.Equal(3, Rotation.FormatNeeded(nested, lastMatchWins: false));
        Assert.DoesNotContain("lastMatchWins", RotationFormat.ToJson(Guide()));
    }

    [Fact]
    public void Not_conditions_and_last_match_wins_survive_a_round_trip()
    {
        var guide = Guide();
        var rotation = guide with
        {
            Format = 3,
            LastMatchWins = true,
            Phases = [guide.Phases[0] with { When = [new NotCondition(new UltimateChargeCondition("Laevatain", Comparison.AtMost, 50))] }, .. guide.Phases.Skip(1)],
        };

        var back = RotationFormat.FromJson(RotationFormat.ToJson(rotation));

        Assert.True(back.LastMatchWins);
        Assert.Equal(new NotCondition(new UltimateChargeCondition("Laevatain", Comparison.AtMost, 50)), Assert.Single(back.Phases[0].When));
        Assert.Contains("\"kind\": \"not\"", RotationFormat.ToJson(rotation));
    }
}
