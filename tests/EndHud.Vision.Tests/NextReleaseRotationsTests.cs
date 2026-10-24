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

        /// <summary>The player hasn't turned on reading their battle-skill keys: no step follower.</summary>
        public bool KeysOff { get; init; }

        public string Read(Reading reading, params int[] presses)
        {
            var now = reading.ToState();
            var shown = Selector.Update(now, KeysOff ? null : Steps);
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
    /// Without the keys read, "the phase now is X and this showing is (not) played through" is ignored as a whole, like
    /// "phase played": the rotation is 充能轴 I with the burst while every ultimate is charged. (Ignoring only the
    /// played-through part left "the phase now is 爆发轴", which held the burst for the rest of the fight, and showed
    /// 普攻 straight after 祀's first battle skill.)
    /// </summary>
    [Fact]
    public void Without_the_keys_it_is_charge_one_and_the_burst_while_the_ultimates_are_charged()
    {
        var fight = new Fight(Si_rotation()) { KeysOff = true };
        var charged = new Reading(false) { UltimateReady = [Si, LastRite, Arcane], TangtangCharge = 0.85 };

        Assert.Equal("充能轴 I", fight.Read(new Reading(false)));
        Assert.Equal("充能轴 I", fight.Read(new Reading(true)));   // 祀's skill running: no 普攻 without the keys
        Assert.Equal("充能轴 I", fight.Read(new Reading(false)));
        Assert.Equal("爆发轴 IV", fight.Read(charged));
        Assert.Equal("充能轴 I", fight.Read(new Reading(true) { UltimateReady = [LastRite, Arcane] }));   // 祀 终结技 cast
    }
}
