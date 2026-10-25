using EndHud.Vision.Operators;
using EndHud.Vision.Rotations;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// Recommended rotations waiting for a release (<c>rotations/next-release/</c>): they need a newer
/// format than released builds read, so they can't be published yet (the published folder stays
/// readable by them: <see cref="RotationSyncTests.Published_rotations_stay_readable_by_released_builds"/>);
/// the sync never lists a subfolder. Each must already be valid for this build, so moving it up to
/// <c>rotations/</c> once a release reads its format is all that's left.
/// </summary>
public class NextReleaseRotationsTests
{
    private static string Folder() => Path.Combine(OperatorCache.RepositoryRoot(), "rotations", "next-release");

    [Fact]
    public void Every_rotation_waiting_for_a_release_is_valid_for_this_build()
    {
        var files = Directory.Exists(Folder()) ? Directory.GetFiles(Folder(), "*.json") : [];
        string user = Path.Combine(Path.GetTempPath(), "endhud-tests", Guid.NewGuid().ToString("N"));
        var operators = OperatorCache.LoadAsync(user).GetAwaiter().GetResult();

        Assert.NotEmpty(files);
        foreach (string file in files)
        {
            var rotation = RotationFormat.FromJson(File.ReadAllText(file));
            Assert.Equal(Path.GetFileNameWithoutExtension(file), rotation.Id);
            Assert.Matches("^[a-z0-9][a-z0-9-]*$", rotation.Id);
            Assert.Empty(rotation.Problems());
            Assert.Equal(Rotation.FormatNeeded(rotation.Phases, rotation.LastMatchWins), rotation.Format);
            // Published ones are at 3 or less: one that fits there belongs there.
            Assert.True(rotation.Format > 3, $"{Path.GetFileName(file)} is readable by released builds: move it to rotations/");
            var named = rotation.Squad.Concat(rotation.Phases.SelectMany(p => p.Steps
                .Where(s => s.Action != StepAction.Condition && s.OperatorId != RotationStep.Controlled).Select(s => s.OperatorId)));
            Assert.All(named, id => Assert.NotNull(operators.Find(id)));
        }
    }

    // ---- 祀 别礼 诀 汤汤, played through (owner-approved phases, 2026-10-24) ----

    private static readonly string[] Squad = ["Si", "Last_Rite", "Arcane", "Tangtang"];
    private const int Si = 1, LastRite = 2, Arcane = 3, Tangtang = 4;

    /// <summary>A reading: Si's battle skill running or not, whose combo was just used (its bar emptied), whose ultimate is ready, Si's and Tangtang's ultimate charge.</summary>
    private sealed record Reading(bool SiRunning)
    {
        public int? ComboUsed { get; init; }
        public int[] UltimateReady { get; init; } = [];
        public double SiCharge { get; init; } = 0.3;
        public double TangtangCharge { get; init; } = 0.3;

        public PartyState ToState()
        {
            var slots = Squad.Select((id, i) => new PartySlot(i + 1, null, 1.0, "", new Frame(1, 1, new byte[4]),
                Combo: ComboUsed == i + 1 ? new ComboReading(0.02, false) : new ComboReading(1, true),
                Ultimate: UltimateReady.Contains(i + 1) ? new UltimateReading(true, 1)
                    : new UltimateReading(false, id == "Si" ? SiCharge : id == "Tangtang" ? TangtangCharge : 0.3),
                Operator: new OperatorMatch(new OperatorInfo(id, id, id, id + ".png"), 1, true))
            {
                BattleSkill = BattleSkillState.Available,
            }).ToList();
            return new PartyState(slots, PanelMarks: 8)
            {
                SkillPoints = 2,
                ActiveSlot = 1,
                SkillRings = [new SkillRingReading("Si", SiRunning ? SkillButtonIcon.Changed : SkillButtonIcon.Original, SiRunning ? 0.5 : null)],
            };
        }
    }

    /// <summary>The overlay's order per reading: the selector, then the step follower inside its phase.</summary>
    private sealed class Fight(Rotation rotation)
    {
        public PhaseSelector Selector { get; } = new(rotation);
        public StepFollower Steps { get; } = new(rotation);
        private PartyState? _before;

        public string Read(Reading reading, params int[] presses)
        {
            var now = reading.ToState();
            var shown = Selector.Update(now, Steps);
            Steps.Update(shown.Step.PhaseIndex, _before, now, Casts.Seen(presses), Selector);
            _before = now;
            return shown.Step.PhaseLabel;
        }

        /// <summary>A charge phase's seven steps, Si's battle skill first (her skill runs from then on); the phase shown on each reading.</summary>
        public List<string> PlayCharge()
        {
            var running = new Reading(true);
            return
            [
                Read(running, Si),                                  // 祀 战技
                Read(running with { ComboUsed = Tangtang }),        // 汤汤 连携技
                Read(running with { ComboUsed = Arcane }),          // 诀 连携技·意
                Read(running, LastRite),                            // 别礼 战技
                Read(running, Tangtang),                            // 汤汤 战技
                Read(running with { ComboUsed = LastRite }),        // 别礼 连携技
                Read(running with { ComboUsed = Si }),              // 祀 连携技: played through
            ];
        }
    }

    private static Rotation Si_rotation() =>
        RotationFormat.FromJson(File.ReadAllText(Path.Combine(Folder(), "si-last-rite-arcane-tangtang.json")));

    [Fact]
    public void The_phases_are_labelled_as_the_owners_charts()
    {
        var rotation = Si_rotation();

        Assert.Equal("充能轴 I", rotation.PhaseLabel(0));
        Assert.Equal("充能轴 II", rotation.PhaseLabel(1));
        Assert.Equal("爆发轴 IV", rotation.PhaseLabel(rotation.Phases.ToList().FindIndex(p => p.Id == "burst")));
        // The plunge of the burst: by whoever is controlled, between Arcane's combo and Last Rite's.
        var burst = rotation.Phase("burst")!.Steps;
        int plunge = burst.ToList().FindIndex(s => s.Action == StepAction.PlungeAttack);
        Assert.Equal(new RotationStep(RotationStep.Controlled, StepAction.PlungeAttack, Note: "+1冰"), burst[plunge] with { Expect = [] });
        Assert.Equal(("Arcane", StepAction.Combo), (burst[plunge - 1].OperatorId, burst[plunge - 1].Action));
        Assert.Equal(("Last_Rite", StepAction.Combo), (burst[plunge + 1].OperatorId, burst[plunge + 1].Action));
    }

    /// <summary>
    /// 充能轴 I, played through, gives way to 普攻 while Si's battle skill runs; when it ends, 充能轴 II
    /// stays until played through; 普攻 again; then back to 充能轴 I.
    /// </summary>
    [Fact]
    public void The_charge_and_basic_attack_phases_take_turns()
    {
        var fight = new Fight(Si_rotation());

        Assert.Equal("充能轴 I", fight.Read(new Reading(false)));
        Assert.All(fight.PlayCharge(), shown => Assert.Equal("充能轴 I", shown));
        Assert.Equal("普攻轴 III", fight.Read(new Reading(true)));
        Assert.Equal("普攻轴 III", fight.Read(new Reading(true)));      // held by "the phase is me"
        Assert.Equal("充能轴 II", fight.Read(new Reading(false)));       // Si's skill over
        Assert.Equal("充能轴 II", fight.Read(new Reading(false)));       // held until played through
        Assert.All(fight.PlayCharge(), shown => Assert.Equal("充能轴 II", shown));
        Assert.Equal("普攻轴 V", fight.Read(new Reading(true)));
        Assert.Equal("充能轴 I", fight.Read(new Reading(false)));        // Si's skill over: round again
    }

    /// <summary>A charge phase played through with Si's skill already over goes straight to 充能轴 II.</summary>
    [Fact]
    public void Charge_one_played_and_the_skill_over_goes_straight_to_charge_two()
    {
        var fight = new Fight(Si_rotation());

        fight.Read(new Reading(false));
        fight.PlayCharge();

        Assert.Equal("充能轴 II", fight.Read(new Reading(false)));
    }

    /// <summary>
    /// Every ultimate charged (Si at least 90%, Tangtang at least 80%, Last Rite's and Arcane's ready):
    /// 爆发轴 IV at once, even in the middle of a charge phase, held until played through, whatever the
    /// ultimates do meanwhile; then the round starts at 充能轴 I.
    /// </summary>
    [Fact]
    public void The_burst_comes_when_every_ultimate_is_charged_and_holds_until_played_through()
    {
        var fight = new Fight(Si_rotation());
        var charged = new Reading(false) { UltimateReady = [Si, LastRite, Arcane], TangtangCharge = 0.85 };
        var spent = new Reading(true);

        Assert.Equal("充能轴 I", fight.Read(new Reading(false)));
        Assert.Equal("充能轴 I", fight.Read(new Reading(true), Si));
        Assert.Equal("爆发轴 IV", fight.Read(charged with { SiRunning = true }));
        Assert.Equal("爆发轴 IV", fight.Read(charged with { SiRunning = true }, Tangtang));                         // 汤汤 战技
        Assert.Equal("爆发轴 IV", fight.Read(charged with { SiRunning = true, ComboUsed = Tangtang }));             // 汤汤 连携技
        Assert.Equal("爆发轴 IV", fight.Read(charged with { SiRunning = true, UltimateReady = [LastRite, Arcane] })); // 祀 终结技
        Assert.Equal("爆发轴 IV", fight.Read(spent with { UltimateReady = [Arcane], TangtangCharge = 0.85 }));      // 别礼 终结技
        Assert.Equal("爆发轴 IV", fight.Read(spent with { UltimateReady = [Arcane, Tangtang] }));                  // 汤汤's ready …
        Assert.Equal("爆发轴 IV", fight.Read(spent with { UltimateReady = [Arcane] }));                            // … and used
        Assert.Equal("爆发轴 IV", fight.Read(spent));                                                              // 诀 终结技·意
        Assert.Equal("爆发轴 IV", fight.Read(spent with { ComboUsed = Arcane }));                                  // 诀 连携技·意, the plunge passed later
        Assert.Equal("爆发轴 IV", fight.Read(spent with { ComboUsed = LastRite }));                                // 别礼 连携技
        Assert.Equal("爆发轴 IV", fight.Read(spent with { ComboUsed = Si }));                                      // 祀 连携技: played through
        Assert.Equal("充能轴 I", fight.Read(spent));
    }

    /// <summary>
    /// The owner's example (2026-10-24): "in many burst rotations, you play 4 ults, the rotation says an order but in
    /// reality it can be arbitrary, but if user happens to play the last one first, then the rotation is immediately
    /// over". Here Arcane's ultimate (the last written) comes first and Si's combo (the burst's last step) early: each
    /// lights its own step, and the burst is played through only when every step that can be seen is lit.
    /// </summary>
    [Fact]
    public void The_burst_played_in_another_order_is_not_over_until_every_step_is_lit()
    {
        var fight = new Fight(Si_rotation());
        var charged = new Reading(true) { UltimateReady = [Si, LastRite, Arcane], TangtangCharge = 0.85 };

        Assert.Equal("爆发轴 IV", fight.Read(charged));
        Assert.Equal("爆发轴 IV", fight.Read(charged, Tangtang));                                                // 汤汤 战技
        Assert.Equal("爆发轴 IV", fight.Read(charged with { ComboUsed = Tangtang }));                            // 汤汤 连携技
        Assert.Equal("爆发轴 IV", fight.Read(charged with { UltimateReady = [Si, LastRite] }));                  // 诀 终结技·意, the last ultimate, first
        Assert.Equal("爆发轴 IV", fight.Read(charged with { UltimateReady = [Si, LastRite], ComboUsed = Si }));  // 祀 连携技, the burst's last step, early
        Assert.Equal("爆发轴 IV", fight.Read(charged with { UltimateReady = [LastRite] }));                      // 祀 终结技
        Assert.Equal("爆发轴 IV", fight.Read(charged with { UltimateReady = [Tangtang] }));                      // 别礼 终结技; 汤汤's ready …
        Assert.Equal("爆发轴 IV", fight.Read(charged with { UltimateReady = [] }));                              // … and used
        Assert.Equal("爆发轴 IV", fight.Read(charged with { UltimateReady = [], ComboUsed = Arcane }));          // 诀 连携技·意
        Assert.Equal(0, fight.Steps.TimesPlayed("burst"));                                                      // 别礼 连携技 still unlit
        Assert.Equal("爆发轴 IV", fight.Read(charged with { UltimateReady = [], ComboUsed = LastRite }));        // 别礼 连携技: every step lit
        Assert.Equal(1, fight.Steps.TimesPlayed("burst"));
        Assert.Equal("充能轴 I", fight.Read(new Reading(true)));
    }

    /// <summary>
    /// Nothing is skipped (owner, 2026-10-24), so no missed cast is allowed for: with Tangtang's battle skill not seen,
    /// the burst stays on the strip, its chevron on her battle skill, until she casts it; then it is played through.
    /// (Before, the follower jumped past it and allowed one missed cast a round.)
    /// </summary>
    [Fact]
    public void The_burst_with_tangtangs_battle_skill_not_seen_stays_until_she_casts_it()
    {
        var fight = new Fight(Si_rotation());
        var charged = new Reading(false) { UltimateReady = [Si, LastRite, Arcane], TangtangCharge = 0.85 };
        var spent = new Reading(true);

        Assert.Equal("爆发轴 IV", fight.Read(charged with { SiRunning = true }));
        Assert.Equal("爆发轴 IV", fight.Read(charged with { SiRunning = true }));                                   // Tangtang's battle skill: not seen
        Assert.Equal("爆发轴 IV", fight.Read(charged with { SiRunning = true, ComboUsed = Tangtang }));             // Tangtang's combo
        Assert.Equal("爆发轴 IV", fight.Read(charged with { SiRunning = true, UltimateReady = [LastRite, Arcane] })); // Si's ultimate
        Assert.Equal("爆发轴 IV", fight.Read(spent with { UltimateReady = [Arcane], TangtangCharge = 0.85 }));      // Last Rite's ultimate
        Assert.Equal("爆发轴 IV", fight.Read(spent with { UltimateReady = [Arcane, Tangtang] }));                  // Tangtang's ready …
        Assert.Equal("爆发轴 IV", fight.Read(spent with { UltimateReady = [Arcane] }));                            // … and used
        Assert.Equal("爆发轴 IV", fight.Read(spent));                                                              // Arcane's ultimate
        Assert.Equal("爆发轴 IV", fight.Read(spent with { ComboUsed = Arcane }));                                  // Arcane's combo
        Assert.Equal("爆发轴 IV", fight.Read(spent with { ComboUsed = LastRite }));                                // Last Rite's combo
        Assert.Equal("爆发轴 IV", fight.Read(spent with { ComboUsed = Si }));                                      // Si's combo
        Assert.Equal("爆发轴 IV", fight.Read(spent));                                                              // still waiting on her battle skill
        Assert.Equal(0, fight.Steps.TimesPlayed("burst"));
        Assert.Equal("爆发轴 IV", fight.Read(spent, Tangtang));                                                    // there it is: played through
        Assert.Equal("充能轴 I", fight.Read(spent));
    }

    // ---- 卡缪 莱万汀 祀 诀, played through (owner, 2026-10-25: "this rotation can be used to test empowered skills") ----

    private static readonly string[] CamilleSquad = ["Camille", "Laevatain", "Si", "Arcane"];
    private const int Camille = 1, Laevatain = 2, SiSlot = 3, ArcaneSlot = 4;

    /// <summary>
    /// A reading for the 卡缪 squad: Si's battle skill running or not, the skill points, whose combos were just used (their
    /// bars emptied), whose ultimates are ready, Laevatain's 熔火, Link's stacks (0: not shown), Laevatain's battle skill
    /// glowing (empowered), the enemy's heat infliction.
    /// </summary>
    private sealed record CamilleReading(bool SiRunning)
    {
        public double SkillPoints { get; init; } = 2;
        public int[] ComboUsed { get; init; } = [];
        public int[] UltimateReady { get; init; } = [];
        public int? Flame { get; init; }
        public int Link { get; init; }
        public bool LaevatainEmpowered { get; init; }
        public bool Heat { get; init; }
        /// <summary>莱万汀's lasting ultimate running (its time bar).</summary>
        public bool LaevatainUltimateRunning { get; init; }

        public PartyState ToState()
        {
            var slots = CamilleSquad.Select((id, i) => new PartySlot(i + 1, null, 1.0, "", new Frame(1, 1, new byte[4]),
                Combo: ComboUsed.Contains(i + 1) ? new ComboReading(0.02, false) : new ComboReading(1, true),
                Ultimate: UltimateReady.Contains(i + 1) ? new UltimateReading(true, 1) : new UltimateReading(false, 0.3),
                Operator: new OperatorMatch(new OperatorInfo(id, id, id, id + ".png"), 1, true))
            {
                BattleSkill = BattleSkillState.Available,
            }).ToList();
            return new PartyState(slots, PanelMarks: 8)
            {
                SkillPoints = SkillPoints,
                ActiveSlot = Laevatain,
                SkillRings = [new SkillRingReading("Si", SiRunning ? SkillButtonIcon.Changed : SkillButtonIcon.Original, SiRunning ? 0.5 : null)],
                Counters = Flame is { } flame ? [new CounterReading("Laevatain", "melting-flame", flame)] : [],
                Buffs = Link > 0 ? [new TeamBuff("Link", Link)] : [],
                Empowered = [false, LaevatainEmpowered, false, false],
                BossStatuses = Heat ? [new BossStatusReading("Heat Infliction", BossStatusReading.RingPosition, 1, 1)] : [],
                RunningUltimates = LaevatainUltimateRunning
                    ? new Dictionary<string, LastingUltimateView> { ["Laevatain"] = new("Laevatain", 0.5, false) }
                    : new Dictionary<string, LastingUltimateView>(),
            };
        }
    }

    /// <summary>The overlay's order per reading, as <see cref="Fight"/>, keeping the step follower's last position.</summary>
    private sealed class CamilleFight(Rotation rotation)
    {
        public PhaseSelector Selector { get; } = new(rotation);
        public StepFollower Steps { get; } = new(rotation);
        public StepPosition Last { get; private set; } = null!;
        private PartyState? _before;

        public string Read(CamilleReading reading, params int[] presses)
        {
            var now = reading.ToState();
            var shown = Selector.Update(now, Steps);
            Last = Steps.Update(shown.Step.PhaseIndex, _before, now, Casts.Seen(presses), Selector);
            _before = now;
            return shown.Step.PhaseLabel;
        }

        /// <summary>充能轴 I's steps, up to its 强化战技 (not cast): Si's skill runs from her battle skill on.</summary>
        public List<string> PlayChargeOneUpToTheEmpoweredSkill(CamilleReading start)
        {
            var running = start with { SiRunning = true };
            return
            [
                Read(start, Laevatain),                                   // 莱万汀 战技
                Read(start, Camille),                                     // 卡缪 战技 (技力 ≥ 2)
                Read(running, SiSlot),                                    // 祀 战技
                Read(running with { ComboUsed = [ArcaneSlot] }),          // 诀 连携技·意, then the 重击
                Read(running with { Flame = 4 }),                         // 莱万汀 熔火 = 4
                Read(running with { ComboUsed = [Camille] }),             // 卡缪 连携技
                Read(running with { ComboUsed = [SiSlot] }),              // 祀 连携技
            ];
        }

        /// <summary>充能轴 I played through: the steps up to its 强化战技, then 莱万汀's 强化战技 and 连携技.</summary>
        public List<string> PlayChargeOne(CamilleReading start)
        {
            var running = start with { SiRunning = true };
            var shown = PlayChargeOneUpToTheEmpoweredSkill(start);
            shown.Add(Read(running with { LaevatainEmpowered = true, Link = 1 }, Laevatain)); // 莱万汀 强化战技
            shown.Add(Read(running with { ComboUsed = [Laevatain] }));                        // 莱万汀 连携技: played through
            return shown;
        }

        /// <summary>充能轴 II played through: no 莱万汀 opener.</summary>
        public List<string> PlayChargeTwo(CamilleReading start)
        {
            var running = start with { SiRunning = true };
            return
            [
                Read(start, Camille),                                     // 卡缪 战技
                Read(running, SiSlot),                                    // 祀 战技
                Read(running with { ComboUsed = [ArcaneSlot] }),          // 诀 连携技·意, then the 重击
                Read(running with { Flame = 4 }),                         // 莱万汀 熔火 = 4
                Read(running with { ComboUsed = [Camille] }),             // 卡缪 连携技
                Read(running with { ComboUsed = [SiSlot] }),              // 祀 连携技
                Read(running with { LaevatainEmpowered = true, Link = 1 }, Laevatain), // 莱万汀 强化战技
                Read(running with { ComboUsed = [Laevatain] }),           // 莱万汀 连携技: played through
            ];
        }
    }

    private static Rotation Camille_rotation() =>
        RotationFormat.FromJson(File.ReadAllText(Path.Combine(Folder(), "camille-laevatain-si-arcane.json")));

    /// <summary>The phases numbered as the owner's charts (充能轴 I, 充能轴 II, 爆发轴 III), and the owner's conditions on their steps.</summary>
    [Fact]
    public void The_camille_phases_are_labelled_as_the_owners_charts_and_carry_the_owners_conditions()
    {
        var rotation = Camille_rotation();
        var empowered = new BattleSkillEmpoweredCondition("Laevatain", true);
        var link = new TeamBuffCondition("Link", Comparison.AtLeast, 1);
        var flame = new CounterCondition("Laevatain", "melting-flame", Comparison.Exactly, 4);

        Assert.Equal(["充能轴 I", "充能轴 II", "爆发轴 III", "普攻轴 IV", "普攻轴 V"], rotation.Phases.Select((_, i) => rotation.PhaseLabel(i)));
        Assert.Equal(12, rotation.Format);

        var one = rotation.Phase("charge-1")!.Steps;
        Assert.Equal(("Laevatain", StepAction.BattleSkill), (one[0].OperatorId, one[0].Action));          // 莱万汀 opens 充能轴 I only
        Assert.Equal([new SkillPointsCondition(Comparison.AtLeast, 2)], one[1].Expect);                     // the first 卡缪 战技
        foreach (var steps in new[] { one, rotation.Phase("charge-2")!.Steps })
        {
            // Right after the 重击 and grouped with it (owner 2026-10-25: "laevatine's check for counter condition is grouped
            // with heavy attack (as they are the same step)"): the condition step 熔火 = 4; then 莱万汀's 强化战技, empowered,
            // with 连击 at least 1.
            int heavy = steps.ToList().FindIndex(s => s.Action == StepAction.HeavyAttack);
            Assert.Equal(RotationStep.Controlled, steps[heavy].OperatorId);
            Assert.False(steps[heavy].WithPrevious);
            Assert.Equal(StepAction.Condition, steps[heavy + 1].Action);
            Assert.Equal([flame], steps[heavy + 1].Expect);
            Assert.True(steps[heavy + 1].WithPrevious);
            Assert.False(steps[heavy + 2].WithPrevious); // 卡缪's 连携技 comes after the pair
            var strong = steps.Single(s => s.Label == "强化战技");
            Assert.Equal(("Laevatain", StepAction.BattleSkill), (strong.OperatorId, strong.Action));
            Assert.Equal([empowered, link], strong.Expect);
        }
        Assert.Equal(StepAction.BattleSkill, rotation.Phase("charge-2")!.Steps[0].Action);
        Assert.Equal("Camille", rotation.Phase("charge-2")!.Steps[0].OperatorId);                          // no 莱万汀 opener in II
        Assert.Empty(rotation.Phase("charge-2")!.Steps[0].Expect);

        var burst = rotation.Phase("burst")!.Steps;
        Assert.Equal(["Camille", "Laevatain", "Arcane"], burst.Take(3).Select(s => s.OperatorId));          // 卡缪, then 莱万汀, then 诀
        Assert.All(burst.Take(3), s => Assert.Equal(StepAction.Ultimate, s.Action));
        Assert.Equal([new BossStatusCondition("Heat Infliction", true)], burst[2].Expect);
        var laevatainEmpowered = burst.Where(s => s.OperatorId == "Laevatain" && s.Label == "强化战技").ToList();
        Assert.Equal(2, laevatainEmpowered.Count);
        Assert.Equal([empowered, link], laevatainEmpowered[0].Expect);                                       // the next one: 连击 ≥ 1
        Assert.Equal([empowered], laevatainEmpowered[1].Expect);                                             // the last: no 连击
    }

    /// <summary>
    /// 充能轴 I played through gives way to 普攻 when 技力 is under 2 and Si's battle skill is over, held while it is the
    /// phase; 技力 back to 2: 充能轴 II, held until played through; 普攻 again; then back to 充能轴 I.
    /// </summary>
    [Fact]
    public void The_camille_charge_and_basic_attack_phases_take_turns()
    {
        var fight = new CamilleFight(Camille_rotation());
        var low = new CamilleReading(false) { SkillPoints = 1 };
        var enough = new CamilleReading(false);

        Assert.Equal("充能轴 I", fight.Read(enough));
        Assert.All(fight.PlayChargeOne(enough), shown => Assert.Equal("充能轴 I", shown));
        Assert.Equal(1, fight.Steps.TimesPlayed("charge-1"));
        Assert.Equal("普攻轴 IV", fight.Read(low));                    // 技力 < 2, Si's skill over
        Assert.Equal("普攻轴 IV", fight.Read(low));                    // held by "the phase is me"
        Assert.Equal("充能轴 II", fight.Read(enough));                 // 技力 back to 2
        Assert.Equal("充能轴 II", fight.Read(low with { SkillPoints = 1.5 })); // held until played through
        Assert.All(fight.PlayChargeTwo(enough), shown => Assert.Equal("充能轴 II", shown));
        Assert.Equal(1, fight.Steps.TimesPlayed("charge-2"));
        Assert.Equal("普攻轴 V", fight.Read(low));
        Assert.Equal("充能轴 I", fight.Read(enough));                  // round again
    }

    /// <summary>充能轴 I played through with the 普攻's conditions not met (Si's skill still running): straight to 充能轴 II.</summary>
    [Fact]
    public void Camille_charge_one_played_with_si_still_running_goes_straight_to_charge_two()
    {
        var fight = new CamilleFight(Camille_rotation());

        fight.Read(new CamilleReading(false));
        fight.PlayChargeOne(new CamilleReading(false));

        Assert.Equal("充能轴 II", fight.Read(new CamilleReading(true) { SkillPoints = 1 }));
    }

    /// <summary>The condition step after the 重击 lights on the reading 莱万汀's 熔火 is 4, not before (3), and stays lit.</summary>
    [Fact]
    public void The_melting_flame_step_lights_at_four()
    {
        var rotation = Camille_rotation();
        int condition = rotation.Phase("charge-1")!.Steps.ToList().FindIndex(s => s.Action == StepAction.Condition);
        var fight = new CamilleFight(rotation);
        var running = new CamilleReading(true);

        fight.Read(new CamilleReading(false), Laevatain);
        fight.Read(new CamilleReading(false), Camille);
        fight.Read(running with { Flame = 3 }, SiSlot);
        Assert.Equal(StepMark.Unlit, fight.Last.Marks[condition]);
        fight.Read(running with { Flame = 3, ComboUsed = [ArcaneSlot] });
        Assert.Equal(StepMark.Current, fight.Last.Marks[condition]);       // the chevron waits on it, after the 重击
        Assert.Equal([new ConditionCheck(CheckKind.OwnAbility, null, true),
            new ConditionCheck(CheckKind.Expected, new CounterCondition("Laevatain", "melting-flame", Comparison.Exactly, 4), false)], fight.Last.Checks);
        fight.Read(running with { Flame = 4 });
        Assert.Equal(StepMark.Lit, fight.Last.Marks[condition]);
        fight.Read(running with { Flame = 0 });                            // spent: the light stays for the round
        Assert.Equal(StepMark.Lit, fight.Last.Marks[condition]);
    }

    /// <summary>
    /// With the chevron on 莱万汀's 强化战技, it is ready only when her battle skill glows (战技已强化) and 连击 shows at least
    /// one stack: each missing one is a check that doesn't hold (the strip's reason).
    /// </summary>
    [Fact]
    public void Laevatains_empowered_skill_waits_for_the_glow_and_a_link_stack()
    {
        var fight = new CamilleFight(Camille_rotation());
        var running = new CamilleReading(true);
        var empowered = new BattleSkillEmpoweredCondition("Laevatain", true);
        var link = new TeamBuffCondition("Link", Comparison.AtLeast, 1);

        fight.Read(new CamilleReading(false));
        fight.PlayChargeOneUpToTheEmpoweredSkill(new CamilleReading(false));

        bool? Holds(StateCondition c) => fight.Last.Checks.Single(k => k.Condition == c).Holds;
        fight.Read(running);
        Assert.Equal(StepMark.Current, fight.Last.Marks[8]);
        Assert.False(fight.Last.Ready);
        Assert.Equal((false, false), (Holds(empowered), Holds(link)));
        fight.Read(running with { LaevatainEmpowered = true });
        Assert.False(fight.Last.Ready);
        Assert.Equal((true, false), (Holds(empowered), Holds(link)));
        fight.Read(running with { Link = 1 });
        Assert.False(fight.Last.Ready);
        Assert.Equal((false, true), (Holds(empowered), Holds(link)));
        fight.Read(running with { LaevatainEmpowered = true, Link = 2 });
        Assert.True(fight.Last.Ready);
        Assert.Equal((true, true), (Holds(empowered), Holds(link)));
    }

    /// <summary>
    /// 卡缪's, 莱万汀's and 诀's ultimates ready (祀's optional): 爆发轴 III at once, even mid-charge. It ends when 莱万汀's
    /// ultimate has run out (owner 2026-10-25: "the exit condition of the burst rotation should be when laevatine's ult is
    /// finished"): her ultimate step lit and her ultimate no longer running; not before she casts it, and not while it
    /// runs, however many steps are lit. Then 充能轴 I.
    /// </summary>
    [Fact]
    public void The_camille_burst_comes_when_three_ultimates_are_ready_and_ends_when_laevatains_ultimate_runs_out()
    {
        var fight = new CamilleFight(Camille_rotation());
        var running = new CamilleReading(true) { LaevatainUltimateRunning = true };

        Assert.Equal("充能轴 I", fight.Read(new CamilleReading(false)));
        Assert.Equal("充能轴 I", fight.Read(new CamilleReading(false) { UltimateReady = [Camille, Laevatain] }, Laevatain)); // 诀's not ready yet
        Assert.Equal("爆发轴 III", fight.Read(new CamilleReading(false) { UltimateReady = [Camille, Laevatain, ArcaneSlot] }));
        Assert.Equal("爆发轴 III", fight.Read(new CamilleReading(true) { UltimateReady = [Laevatain, ArcaneSlot] })); // 卡缪 终结技; hers not cast yet
        Assert.Equal("爆发轴 III", fight.Read(running with { UltimateReady = [ArcaneSlot], Heat = true }));          // 莱万汀 终结技: running
        Assert.Equal("爆发轴 III", fight.Read(running with { Heat = true }));                                        // 诀 终结技
        Assert.Equal("爆发轴 III", fight.Read(running with { ComboUsed = [ArcaneSlot, Camille, SiSlot] }));
        Assert.Equal("爆发轴 III", fight.Read(running with { LaevatainEmpowered = true, Link = 1 }, Laevatain));     // 莱万汀 强化战技
        Assert.Equal(0, fight.Steps.TimesPlayed("burst"));
        Assert.Equal("爆发轴 III", fight.Read(new CamilleReading(true)));                                            // her ultimate ran out
        Assert.Equal(1, fight.Steps.TimesPlayed("burst"));
        Assert.Equal("充能轴 I", fight.Read(new CamilleReading(true)));
    }

    /// <summary>
    /// The owner's example for bursts (2026-10-24): the ultimates in another order than written. Every step lit doesn't end
    /// it while 莱万汀's ultimate still runs; it ends when hers runs out, whatever is still unlit.
    /// </summary>
    [Fact]
    public void The_camille_burst_played_in_another_order_ends_only_when_laevatains_ultimate_runs_out()
    {
        var fight = new CamilleFight(Camille_rotation());
        var charged = new CamilleReading(true) { UltimateReady = [Camille, Laevatain, ArcaneSlot] };
        var running = new CamilleReading(true) { UltimateReady = [Camille], LaevatainUltimateRunning = true }; // 卡缪's kept for last

        Assert.Equal("爆发轴 III", fight.Read(charged));
        Assert.Equal("爆发轴 III", fight.Read(charged with { UltimateReady = [Camille, Laevatain] }));   // 诀 终结技 first
        Assert.Equal("爆发轴 III", fight.Read(running));                                                 // 莱万汀 终结技
        Assert.Equal("爆发轴 III", fight.Read(running with { ComboUsed = [Laevatain] }));                // 莱万汀 连携技 early
        Assert.Equal("爆发轴 III", fight.Read(running with { UltimateReady = [] }));                     // 卡缪 终结技 last
        Assert.Equal("爆发轴 III", fight.Read(running with { UltimateReady = [] }));
        Assert.Equal(0, fight.Steps.TimesPlayed("burst"));                                              // hers still running
        Assert.Equal("爆发轴 III", fight.Read(new CamilleReading(true)));                                 // ran out
        Assert.Equal(1, fight.Steps.TimesPlayed("burst"));
        Assert.Equal("充能轴 I", fight.Read(new CamilleReading(true)));
    }
}
