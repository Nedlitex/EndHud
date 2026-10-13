using EndHud.Vision.Rotations;
using Xunit;

namespace EndHud.Tests;

/// <summary>The editor's working copy of a rotation: round trip, and what it says when something's missing.</summary>
public class RotationDraftTests
{
    private static Rotation Guide() => new(Rotation.CurrentFormat, "guide", "标准轴", ["Laevatain", "Camille", "Arcane", "Ardelia"],
    [
        new RotationPhase("charge", "充能轴",
        [
            new RotationStep("Ardelia", StepAction.BattleSkill, [new SkillPointsCondition(Comparison.AtLeast, 2)]),
            new RotationStep("Camille", StepAction.Combo),
            new RotationStep("Arcane", StepAction.Combo, WithPrevious: true),
            new RotationStep("Laevatain", StepAction.BattleSkill, [new BossStatusCondition("Heat Infliction", true)], Note: "飞天", Label: "战技·意"),
        ]),
        new RotationPhase("burst", "爆发轴", [new RotationStep("Laevatain", StepAction.Ultimate)],
            [new AnyCondition([new UltimateCondition("Laevatain", true), new AllCondition([new SkillPointsCondition(Comparison.AtLeast, 3), new ComboCondition("Camille", true)])])]),
    ]);

    [Fact]
    public void Editing_nothing_gives_back_the_same_rotation()
    {
        var guide = Guide();

        Assert.Equal(RotationFormat.ToJson(guide), RotationFormat.ToJson(RotationDraft.From(guide).ToRotation()));
    }

    [Fact]
    public void A_good_rotation_has_nothing_to_say()
    {
        Assert.Empty(RotationDraft.From(Guide()).Problems());
    }

    [Fact]
    public void A_new_rotation_says_what_it_still_needs()
    {
        var problems = RotationDraft.New("").Problems();

        Assert.Equal(["Rotation.Problem.NoName", "Rotation.Problem.Squad", "Rotation.Problem.NoPhases"], problems);
    }

    [Fact]
    public void Blank_notes_and_names_are_not_saved()
    {
        var draft = RotationDraft.From(Guide());
        draft.Phases[0].Steps[0].Note = "   ";
        draft.Phases[0].Steps[0].Label = "";

        var step = draft.ToRotation().Phases[0].Steps[0];

        Assert.Null(step.Note);
        Assert.Null(step.Label);
    }

    [Fact]
    public void Every_phase_behind_an_entry_condition_is_explained_in_words()
    {
        var draft = RotationDraft.From(Guide());
        draft.Phases[0].When.Add(new SkillPointsCondition(Comparison.AtLeast, 1));

        Assert.Contains("Rotation.Problem.NoDefaultPhase", draft.Problems());
    }

    [Fact]
    public void An_empty_group_in_an_entry_condition_is_explained_in_words()
    {
        var draft = RotationDraft.From(Guide());
        draft.Phases[1].When.Add(new AllCondition([]));

        Assert.Contains("Rotation.Problem.EmptyGateGroup", draft.Problems());
    }

    [Fact]
    public void An_operator_who_left_the_squad_is_pointed_out()
    {
        var draft = RotationDraft.From(Guide());
        draft.Squad[3] = "Perlica"; // Ardelia out: step 1 of 充能轴 is hers

        Assert.Contains("Rotation.Problem.StepOperator", draft.Problems());
    }
}
