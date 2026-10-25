using EndHud.Vision.Alerts;
using EndHud.Vision.Operators;
using EndHud.Vision.Rotations;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// "战技已强化" (owner, 2026-10-24: "empowered skill should be a condition as well"): an operator's battle skill is
/// empowered, the steady white glow on the rim of that operator's battle-skill button, told from the corner sampled many
/// times a second (<see cref="FlashSampler.Empowered"/>, carried on the reading as <see cref="PartyState.Empowered"/>).
/// One kind of the shared catalogue: every host, its own formats, unknown whenever the button can't be told.
/// </summary>
public class BattleSkillEmpoweredConditionTests
{
    private static readonly string[] Squad = ["Camille", "Laevatain", "Si", "Arcane"];

    private static PartyState Reading(bool?[] empowered, HudLayout layout = HudLayout.KeyboardMouse, bool panel = true, BattleSkillState? button = BattleSkillState.Available)
    {
        var slots = Squad.Select((id, i) => new PartySlot(i + 1, null, 1.0, "", new Frame(1, 1, new byte[4]),
            Combo: new ComboReading(0.5, false),
            Operator: new OperatorMatch(new OperatorInfo(id, id, id, id + ".png"), 1, true)) { BattleSkill = button }).ToList();
        return new PartyState(slots, PanelMarks: panel ? 8 : 0)
        {
            SkillPoints = panel ? 2 : null, ActiveSlot = panel ? 1 : null, Layout = layout, Empowered = empowered,
        };
    }

    private static bool? Holds(StateCondition c, PartyState s) => RotationTracker.Evaluate(c, s, PhaseHistory.None);

    [Fact]
    public void Its_slots_glow_says_empowered_or_not()
    {
        var state = Reading([false, true, false, null]);
        Assert.Equal(true, Holds(new BattleSkillEmpoweredCondition("Laevatain", true), state));
        Assert.Equal(false, Holds(new BattleSkillEmpoweredCondition("Laevatain", false), state));
        Assert.Equal(false, Holds(new BattleSkillEmpoweredCondition("Camille", true), state));
        Assert.Equal(true, Holds(new BattleSkillEmpoweredCondition("Camille", false), state));
        // Slot 4's glow couldn't be told: unknown either way.
        Assert.Null(Holds(new BattleSkillEmpoweredCondition("Arcane", true), state));
        Assert.Null(Holds(new BattleSkillEmpoweredCondition("Arcane", false), state));
    }

    [Theory]
    [InlineData(HudLayout.Controller)]
    [InlineData(HudLayout.ControllerSkillsHeld)]
    public void With_a_controller_too_though_its_buttons_are_not_read(HudLayout layout)
    {
        var state = Reading([false, true, false, false], layout, button: null);
        Assert.Equal(true, Holds(new BattleSkillEmpoweredCondition("Laevatain", true), state));
        Assert.Equal(false, Holds(new BattleSkillEmpoweredCondition("Si", true), state));
    }

    [Fact]
    public void Unknown_when_it_cant_be_told()
    {
        var empowered = new bool?[] { false, true, false, false };
        // Nothing sampled (the corner isn't tapped: no window capture).
        Assert.Null(Holds(new BattleSkillEmpoweredCondition("Laevatain", true), Reading([])));
        // The squad panel out of sight (a cutscene).
        Assert.Null(Holds(new BattleSkillEmpoweredCondition("Laevatain", true), Reading(empowered, panel: false)));
        // With keyboard and mouse, the button itself not read in this reading (an effect or a display over it).
        Assert.Null(Holds(new BattleSkillEmpoweredCondition("Laevatain", true), Reading(empowered, button: null)));
        Assert.Null(Holds(new BattleSkillEmpoweredCondition("Si", true), Reading(empowered, button: null)));
        // An operator not in this fight.
        Assert.Null(Holds(new BattleSkillEmpoweredCondition("Tangtang", true), Reading(empowered)));
    }

    [Fact]
    public void It_is_one_kind_of_the_shared_catalogue_offered_everywhere()
    {
        var kind = ConditionCatalogue.Of(new BattleSkillEmpoweredCondition("Laevatain", true));
        Assert.Equal(("battleSkillEmpowered", "BattleSkillEmpowered"), (kind.JsonName, kind.EditorKind));
        Assert.Equal("Laevatain", ConditionCatalogue.OperatorOf(new BattleSkillEmpoweredCondition("Laevatain", false)));
        foreach (var host in Enum.GetValues<ConditionHostKind>())
            Assert.Contains("BattleSkillEmpowered", ConditionCatalogue.OfferedIn(host));
        // Anyone may be named: whose battle skill can be empowered is the game's, not EndHud's, to say.
        Assert.Null(kind.OnlyFor);
        Assert.Empty(ConditionRules.Check([new BattleSkillEmpoweredCondition("Arcane", true)], ConditionHost.Alert));
        Assert.Equal(ConditionProblemKind.OperatorNotInSquad,
            Assert.Single(ConditionRules.Check([new BattleSkillEmpoweredCondition("Tangtang", true)], new ConditionHost(ConditionHostKind.PhaseGate, Squad))).Kind);
    }

    [Fact]
    public void Its_formats_are_raised_only_when_it_is_used()
    {
        Assert.Equal(11, Rotation.CurrentFormat);
        Assert.Equal(5, AlertFile.CurrentFormat);
        var plain = new RotationPhase("p", "p", [new RotationStep("Laevatain", StepAction.BattleSkill)]);
        var gated = plain with { When = [new NotCondition(new BattleSkillEmpoweredCondition("Laevatain", true))] };
        Assert.Equal(2, Rotation.FormatNeeded([plain], false));
        Assert.Equal(11, Rotation.FormatNeeded([gated], false));
        Assert.Equal(1, AlertFile.FormatNeeded([AlertRule.New([new SkillPointsCondition(Comparison.AtLeast, 1)], "x")]));
        Assert.Equal(5, AlertFile.FormatNeeded([AlertRule.New([new AnyCondition([new BattleSkillEmpoweredCondition("Laevatain", true)])], "x")]));
    }

    [Fact]
    public void It_round_trips_through_a_rotation_file()
    {
        var phase = new RotationPhase("p", "p", [new RotationStep("Laevatain", StepAction.BattleSkill, Expect: [new BattleSkillEmpoweredCondition("Laevatain", true)])]);
        var rotation = new Rotation(Rotation.FormatNeeded([phase], false), "r", "r", Squad, [phase]);
        string json = RotationFormat.ToJson(rotation);
        Assert.Contains("\"kind\": \"battleSkillEmpowered\"", json);
        Assert.Contains("\"empowered\": true", json);
        var back = RotationFormat.FromJson(json);
        Assert.Equal(new BattleSkillEmpoweredCondition("Laevatain", true), Assert.Single(back.Phases[0].Steps[0].Expect));
    }
}
