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
    [InlineData("""{ "format": 9, "id": "x" }""", RotationReadError.NewerFormat)]
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
        Assert.Equal(3, Rotation.FormatNeeded([plain[0] with { When = [new UltimateActiveCondition("Laevatain", true)] }], lastMatchWins: false));
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

    /// <summary>
    /// Format 4 adds basic-attack steps, "phase shown" conditions and the below/above comparisons. A
    /// rotation that uses none of them is still saved as format 2 or 3.
    /// </summary>
    [Fact]
    public void A_basic_attack_step_needs_format_4()
    {
        var plain = Guide().Phases;
        RotationPhase[] basic = [plain[0] with { Steps = [.. plain[0].Steps, new RotationStep("Laevatain", StepAction.BasicAttack)] }];

        Assert.Equal(4, Rotation.FormatNeeded(basic, lastMatchWins: false));
        var back = RotationFormat.FromJson(RotationFormat.ToJson(Guide() with { Format = 4, Phases = basic }));
        Assert.Equal(StepAction.BasicAttack, back.Phases[0].Steps[^1].Action);
        Assert.Contains("\"action\": \"BasicAttack\"", RotationFormat.ToJson(Guide() with { Format = 4, Phases = basic }));
    }

    /// <summary>
    /// Format 8 adds heavy-attack steps: a build before it can't read the action, so a rotation using
    /// one says it's from a newer EndHud there. One without a heavy attack keeps its lower format.
    /// </summary>
    [Fact]
    public void A_heavy_attack_step_needs_format_8_and_round_trips()
    {
        var plain = Guide().Phases;
        RotationPhase[] heavy = [plain[0] with { Steps = [.. plain[0].Steps, new RotationStep("Laevatain", StepAction.HeavyAttack)] }];
        RotationPhase[] basic = [plain[0] with { Steps = [.. plain[0].Steps, new RotationStep("Laevatain", StepAction.BasicAttack)] }];

        Assert.Equal(8, Rotation.FormatNeeded(heavy, lastMatchWins: false));
        Assert.Equal(4, Rotation.FormatNeeded(basic, lastMatchWins: false));
        Assert.True(Rotation.CurrentFormat >= 8);
        string json = RotationFormat.ToJson(Guide() with { Format = 8, Phases = heavy });
        Assert.Contains("\"action\": \"HeavyAttack\"", json);
        Assert.Equal(StepAction.HeavyAttack, RotationFormat.FromJson(json).Phases[0].Steps[^1].Action);
    }

    [Fact]
    public void Below_needs_format_4_and_round_trips()
    {
        var plain = Guide().Phases;
        var below = new AnyCondition([new NotCondition(new SkillPointsCondition(Comparison.Below, 2))]);
        RotationPhase[] phases = [plain[0] with { When = [below] }];
        RotationPhase[] above = [plain[0] with { When = [new UltimateChargeCondition("Laevatain", Comparison.Above, 37)] }];

        Assert.Equal(4, Rotation.FormatNeeded(phases, lastMatchWins: false));
        Assert.Equal(4, Rotation.FormatNeeded(above, lastMatchWins: false));
        var back = RotationFormat.FromJson(RotationFormat.ToJson(Guide() with { Format = 4, Phases = phases }));
        Assert.Equal(below, Assert.Single(back.Phases[0].When));
        Assert.Contains("\"is\": \"Below\"", RotationFormat.ToJson(Guide() with { Format = 4, Phases = phases }));
    }

    [Fact]
    public void A_rotation_using_nothing_new_still_saves_as_format_2()
    {
        var plain = Guide().Phases;

        Assert.Equal(2, Rotation.FormatNeeded(plain, lastMatchWins: false));
        Assert.Equal(3, Rotation.FormatNeeded(plain, lastMatchWins: true));
        Assert.Equal(8, Rotation.CurrentFormat);
    }

    [Fact]
    public void A_nested_phase_shown_condition_needs_format_4_and_round_trips()
    {
        var guide = Guide();
        var shown = new AllCondition([new NotCondition(new PhaseShownCondition("charge", Comparison.AtLeast, 1))]);
        var rotation = guide with { Format = 4, Phases = [guide.Phases[0], guide.Phases[1], guide.Phases[2] with { When = [shown] }] };

        Assert.Equal(4, Rotation.FormatNeeded(rotation.Phases, lastMatchWins: false));
        var back = RotationFormat.FromJson(RotationFormat.ToJson(rotation));
        Assert.Equal(shown, Assert.Single(back.Phases[2].When));
        Assert.Contains("\"kind\": \"phaseShown\"", RotationFormat.ToJson(rotation));
        Assert.Empty(back.Problems());
    }

    /// <summary>
    /// A phase gated on its own count would flip the moment it shows (it's counted on arrival), and
    /// a count of a deleted phase can never be known: both are pointed out.
    /// </summary>
    [Fact]
    public void A_gate_on_its_own_count_and_a_count_of_a_missing_phase_are_problems()
    {
        var guide = Guide();
        var rotation = guide with
        {
            Phases =
            [
                guide.Phases[0],
                guide.Phases[1] with { Steps = [guide.Phases[1].Steps[0] with { Expect = [new PhaseShownCondition("gone", Comparison.AtLeast, 1)] }] },
                guide.Phases[2] with { When = [new AnyCondition([new PhaseShownCondition("burst", Comparison.AtLeast, 1), new UltimateCondition("Laevatain", true)])] },
            ],
        };

        var problems = rotation.Problems();

        Assert.Contains(new RotationProblem(RotationProblemKind.PhaseShownOwnGate, guide.Phases[2].Name), problems);
        Assert.Contains(new RotationProblem(RotationProblemKind.PhaseShownMissingPhase, guide.Phases[1].Name, 1), problems);
        Assert.Equal(2, problems.Count);
        // A step may count its own phase (only an entry condition flips on it).
        var own = guide with { Phases = [guide.Phases[0] with { Steps = [guide.Phases[0].Steps[0] with { Expect = [new PhaseShownCondition("charge", Comparison.AtLeast, 2)] }] }] };
        Assert.Empty(own.Problems());
    }

    /// <summary>"Phase played" is new in format 5: only a rotation that uses it needs it, and it round-trips at any depth.</summary>
    [Fact]
    public void A_nested_phase_played_condition_needs_format_5_and_round_trips()
    {
        var guide = Guide();
        var played = new AnyCondition([new NotCondition(new PhasePlayedCondition("charge", Comparison.Below, 2)), new UltimateCondition("Laevatain", true)]);
        var rotation = guide with { Phases = [guide.Phases[0], guide.Phases[1], guide.Phases[2] with { When = [played] }] };

        Assert.Equal(5, Rotation.FormatNeeded(rotation.Phases, lastMatchWins: false));
        Assert.Equal(4, Rotation.FormatNeeded([guide.Phases[0] with { When = [new PhaseShownCondition("charge-2", Comparison.AtLeast, 1)] }], lastMatchWins: false));
        string json = RotationFormat.ToJson(rotation with { Format = Rotation.FormatNeeded(rotation.Phases, false) });
        Assert.Contains("\"kind\": \"phasePlayed\"", json);
        var back = RotationFormat.FromJson(json);
        Assert.Equal(played, Assert.Single(back.Phases[2].When));
        Assert.Empty(back.Problems());
    }

    /// <summary>A format 4 file (from the build before "phase played") still loads as it was.</summary>
    [Fact]
    public void A_format_4_file_still_loads()
    {
        const string json = """
            { "format": 4, "id": "old", "name": "旧轴", "squad": ["Laevatain", "Camille", "Arcane", "Ardelia"],
              "phases": [
                { "id": "charge", "name": "充能轴", "steps": [ { "operatorId": "Ardelia", "action": "BasicAttack" } ] },
                { "id": "burst", "name": "爆发轴", "when": [ { "kind": "phaseShown", "phaseId": "charge", "is": "AtLeast", "times": 2 } ],
                  "steps": [ { "operatorId": "Laevatain", "action": "Ultimate" } ] } ] }
            """;

        var rotation = RotationFormat.FromJson(json);

        Assert.Equal(4, rotation.Format);
        Assert.Equal(new PhaseShownCondition("charge", Comparison.AtLeast, 2), Assert.Single(rotation.Phases[1].When));
    }

    /// <summary>
    /// A count of a deleted phase can never be known, and a gate that needs its own phase played
    /// before it can show can never open (it must be shown to be played): both are pointed out. A
    /// gate on its own plays that holds at 0 ("at most twice") is fine: it leaves after so many rounds.
    /// </summary>
    [Fact]
    public void A_played_count_of_a_missing_phase_and_a_gate_that_can_never_open_are_problems()
    {
        var guide = Guide();
        var rotation = guide with
        {
            Phases =
            [
                guide.Phases[0],
                guide.Phases[1] with { Steps = [guide.Phases[1].Steps[0] with { Expect = [new PhasePlayedCondition("gone", Comparison.AtLeast, 1)] }] },
                guide.Phases[2] with { When = [new AllCondition([new PhasePlayedCondition("burst", Comparison.AtLeast, 1), new UltimateCondition("Laevatain", true)])] },
            ],
        };

        var problems = rotation.Problems();

        Assert.Contains(new RotationProblem(RotationProblemKind.PhasePlayedOwnGateNeverOpens, guide.Phases[2].Name), problems);
        Assert.Contains(new RotationProblem(RotationProblemKind.PhasePlayedMissingPhase, guide.Phases[1].Name, 1), problems);
        Assert.Equal(2, problems.Count);
        var leaves = guide with { Phases = [guide.Phases[0], guide.Phases[2] with { When = [new PhasePlayedCondition("burst", Comparison.AtMost, 2)] }] };
        Assert.Empty(leaves.Problems());
        var orElse = guide with { Phases = [guide.Phases[0], guide.Phases[2] with { When = [new AnyCondition([new PhasePlayedCondition("burst", Comparison.AtLeast, 1), new UltimateCondition("Laevatain", true)])] }] };
        Assert.Empty(orElse.Problems());
    }

    /// <summary>A build that reads up to format 3 refuses a format 4 file as "newer" (simulated one format up, as this build reads 4).</summary>
    [Fact]
    public void Format_4_file_is_refused_by_a_build_reading_3()
    {
        string json = RotationFormat.ToJson(Guide() with { Format = Rotation.CurrentFormat + 1 });

        Assert.Equal(RotationReadError.NewerFormat, Assert.Throws<RotationReadException>(() => RotationFormat.FromJson(json)).Error);
    }

    // ---- Priorities (format 6) ----

    /// <summary>Burst gated on Laevatain's ultimate, with this base priority and these rules.</summary>
    private static Rotation Prioritised(int priority, params PriorityRule[] rules)
    {
        var guide = Guide();
        return guide with
        {
            Phases =
            [
                guide.Phases[0],
                guide.Phases[1],
                guide.Phases[2] with { When = [new UltimateCondition("Laevatain", true)], Priority = priority, PriorityRules = rules },
            ],
        };
    }

    /// <summary>A priority or a rule is new in format 6: only a rotation that uses one needs it; it round-trips, rules in order.</summary>
    [Fact]
    public void A_priority_needs_format_6_and_round_trips()
    {
        var rules = new[]
        {
            new PriorityRule([new AnyCondition([new BossStatusCondition("Heat Infliction", true, 2), new NotCondition(new ComboCondition("Ardelia", true))])], 7),
            new PriorityRule([new PhaseShownCondition("charge", Comparison.AtLeast, 2)], -4),
        };
        var rotation = Prioritised(3, rules);

        Assert.True(Rotation.CurrentFormat >= 6);
        Assert.Equal(6, Rotation.FormatNeeded(rotation.Phases, lastMatchWins: false));
        Assert.Equal(6, Rotation.FormatNeeded(Prioritised(-2).Phases, lastMatchWins: false));          // a base number alone
        Assert.Equal(6, Rotation.FormatNeeded(Prioritised(0, rules[1]).Phases, lastMatchWins: false)); // a rule alone
        string json = RotationFormat.ToJson(rotation with { Format = 6 });
        Assert.Contains("\"priority\": 3", json);
        Assert.Contains("\"priorityRules\"", json);
        var back = RotationFormat.FromJson(json);
        Assert.Equal(3, back.Phases[2].Priority);
        Assert.Equal(rules, back.Phases[2].PriorityRules);
        Assert.Equal(0, back.Phases[0].Priority);
        Assert.Empty(back.Phases[0].PriorityRules);
        Assert.Empty(back.Problems());
        // And through a share code.
        Assert.Equal(rules, RotationFormat.FromShareCode(RotationFormat.ToShareCode(rotation with { Format = 6 })).Phases[2].PriorityRules);
    }

    /// <summary>A rotation without priorities stays in its old format, with no priority in the file (older builds read it as before).</summary>
    [Fact]
    public void A_rotation_without_priorities_keeps_its_format_and_writes_none()
    {
        var plain = Prioritised(0);

        Assert.Equal(2, Rotation.FormatNeeded(plain.Phases, lastMatchWins: false));
        string json = RotationFormat.ToJson(plain with { Format = 2 });
        Assert.DoesNotContain("priority", json, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A format 5 file (from the build before priorities) still loads, every phase at 0 with no rules.</summary>
    [Fact]
    public void A_format_5_file_still_loads_with_no_priorities()
    {
        const string json = """
            { "format": 5, "id": "old", "name": "旧轴", "squad": ["Laevatain", "Camille", "Arcane", "Ardelia"],
              "phases": [
                { "id": "charge", "name": "充能轴", "steps": [ { "operatorId": "Ardelia", "action": "BattleSkill" } ] },
                { "id": "burst", "name": "爆发轴", "when": [ { "kind": "phasePlayed", "phaseId": "charge", "is": "AtLeast", "times": 2 } ],
                  "steps": [ { "operatorId": "Laevatain", "action": "Ultimate" } ] } ] }
            """;

        var rotation = RotationFormat.FromJson(json);

        Assert.Equal(5, rotation.Format);
        Assert.All(rotation.Phases, p => Assert.Equal((0, 0), (p.Priority, p.PriorityRules.Count)));
    }

    /// <summary>A boss status asked only by a priority rule is still read.</summary>
    [Fact]
    public void A_status_in_a_priority_rule_is_read()
    {
        var noStatus = Prioritised(0);
        noStatus = noStatus with { Phases = [.. noStatus.Phases.Select(p => p with { Steps = [new RotationStep(p.Steps[0].OperatorId, p.Steps[0].Action)] })] };
        var burst = noStatus.Phases[2];

        Assert.False(ConditionWalk.NeedsBossStatuses(noStatus));
        Assert.True(ConditionWalk.NeedsBossStatuses(noStatus with
        {
            Phases = [noStatus.Phases[0], noStatus.Phases[1], burst with { PriorityRules = [new PriorityRule([new BossStatusCondition("Heat Infliction", true)], 1)] }],
        }));
    }

    /// <summary>
    /// A rule's conditions are checked like an entry condition's (a deleted phase, an operator not in
    /// the squad, an empty group), each pointing at its rule; a rule with no condition is a problem of
    /// its own (it would always hold).
    /// </summary>
    [Fact]
    public void Problems_in_priority_rules_point_at_the_rule()
    {
        var rotation = Prioritised(0,
            new PriorityRule([new UltimateCondition("Laevatain", true)], 1),
            new PriorityRule([new PhaseShownCondition("gone", Comparison.AtLeast, 1)], 2),
            new PriorityRule([new PhasePlayedCondition("gone", Comparison.AtLeast, 1)], 3),
            new PriorityRule([new ComboCondition("Si", true)], 4),
            new PriorityRule([new AnyCondition([])], 5),
            new PriorityRule([], 6));
        string burst = rotation.Phases[2].Name;

        var problems = rotation.Problems();

        Assert.Equal(
        [
            new RotationProblem(RotationProblemKind.PhaseShownMissingPhase, burst, null, 2),
            new RotationProblem(RotationProblemKind.PhasePlayedMissingPhase, burst, null, 3),
            new RotationProblem(RotationProblemKind.ConditionOperatorNotInSquad, burst, null, 4),
            new RotationProblem(RotationProblemKind.EmptyConditionGroup, burst, null, 5),
            new RotationProblem(RotationProblemKind.PriorityRuleWithoutCondition, burst, null, 6),
        ], problems);
        // A rule may count its own phase: it only ranks the phase once its entry condition holds.
        Assert.Empty(Prioritised(0, new PriorityRule([new PhaseShownCondition("burst", Comparison.AtLeast, 1)], 1)).Problems());
    }

    /// <summary>The otherwise phase isn't ranked: a priority or a rule on a phase without an entry condition would do nothing, so it's pointed out.</summary>
    [Fact]
    public void A_priority_on_a_phase_without_an_entry_condition_is_a_problem()
    {
        var guide = Guide();
        var based = guide with { Phases = [guide.Phases[0] with { Priority = 2 }, guide.Phases[1], guide.Phases[2]] };
        var ruled = guide with { Phases = [guide.Phases[0], guide.Phases[1] with { PriorityRules = [new PriorityRule([new UltimateCondition("Laevatain", true)], 2)] }, guide.Phases[2]] };

        Assert.Equal([new RotationProblem(RotationProblemKind.PriorityWithoutEntryCondition, guide.Phases[0].Name)], based.Problems());
        Assert.Equal([new RotationProblem(RotationProblemKind.PriorityWithoutEntryCondition, guide.Phases[1].Name)], ruled.Problems());
    }
}
