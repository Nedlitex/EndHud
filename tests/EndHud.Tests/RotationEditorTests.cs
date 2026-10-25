using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using EndHud.Vision.Operators;
using EndHud.Vision.Rotations;
using Xunit;
using Rotation = EndHud.Vision.Rotations.Rotation;

namespace EndHud.Tests;

/// <summary>The editor and recommended rotations: never changed in place, and deleting one hides it.</summary>
[Collection(WpfCollection.Name)]
public sealed class RotationEditorTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "endhud-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private static Rotation Published() => new(Rotation.CurrentFormat, "laevatain-standard", "标准轴", ["Laevatain", "Camille", "Arcane", "Ardelia"],
        [new RotationPhase("p", "充能轴", [new RotationStep("Ardelia", StepAction.BattleSkill)])]);

    [Fact]
    public void Saving_a_recommended_rotation_saves_the_players_own_copy_and_leaves_it_as_it_was()
    {
        var library = new RotationLibrary(_folder);
        library.Published.Save(Published());
        Rotation? saved = null;

        OnEditor(library, saved_ => saved = saved_, editor =>
        {
            Assert.Equal(Visibility.Visible, ((TextBlock)editor.FindName("PublishedNote")).Visibility);
            ((TextBox)editor.FindName("NameBox")).Text = "标准轴 改";
            Click(editor, "SaveButton");
        });

        var own = Assert.Single(library.Own.Load().Rotations);
        Assert.NotEqual("laevatain-standard", own.Id);
        Assert.Equal(own.Id, saved!.Id); // and it becomes the one used for the squad
        Assert.Equal("标准轴", Assert.Single(library.Published.Load().Rotations).Name);
        Assert.Equal(2, library.Load().Rotations.Count);
    }

    [Fact]
    public void Deleting_a_recommended_rotation_hides_it()
    {
        var library = new RotationLibrary(_folder);
        library.Published.Save(Published());

        OnEditor(library, _ => { }, editor => editor.DeleteSelected(confirm: false));

        Assert.Empty(library.Load().Rotations);
        Assert.Contains("laevatain-standard", library.Hidden());
    }

    [Fact]
    public void An_or_entry_condition_built_with_the_pickers_is_saved()
    {
        var library = new RotationLibrary(_folder);
        var two = Published() with { Id = "mine" };
        library.Own.Save(two with { Phases = [.. two.Phases, new RotationPhase("burst", "爆发轴", [new RotationStep("Laevatain", StepAction.Ultimate)])] });

        string said = "";
        OnEditor(library, _ => { }, editor =>
        {
            ((ListBox)editor.FindName("PhaseList")).SelectedIndex = 1;             // 爆发轴
            Click(editor, "AddGateButton");                                   // one condition: an operator's ultimate ready
            var gate = (StackPanel)editor.FindName("GateRows");
            var kind = ((Panel)gate.Children[0]).Children.OfType<ComboBox>().First();
            kind.SelectedItem = kind.Items.Cast<ComboBoxItem>().Single(i => (string)i.Tag == "Group"); // → a group: "any of"
            var group = (StackPanel)gate.Children[0];
            var inner = (StackPanel)((Border)group.Children[1]).Child;
            ((Button)inner.Children[^1]).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); // a second condition in it
            Click(editor, "SaveButton");
            said = ((TextBlock)editor.FindName("ProblemsText")).Text;
        });

        Assert.True(library.Own.Load().Rotations.Count == 1, $"not saved: {said}");
        var saved = Assert.Single(library.Own.Load().Rotations);
        Assert.True(saved.Phases[1].When.Count == 1, $"editor said: {said}");
        var phase = saved.Phases[1];
        var any = Assert.IsType<AnyCondition>(Assert.Single(phase.When));
        Assert.Equal(2, any.Of.Count);
        Assert.True(phase.Gated);
    }

    /// <summary>
    /// A player's case (2026-10-18), built with the editor's own controls: 爆发轴 shown "while not
    /// Laevatain's ultimate charged to at most 0%" (非 ticked; 0% is offered), and "the last phase
    /// that holds wins" ticked. Saved in format 3, which these need.
    /// </summary>
    [Fact]
    public void A_negated_0_percent_condition_and_last_phase_wins_are_saved()
    {
        var library = new RotationLibrary(_folder);
        var two = Published() with { Id = "mine" };
        library.Own.Save(two with { Phases = [.. two.Phases, new RotationPhase("burst", "爆发轴", [new RotationStep("Laevatain", StepAction.Ultimate)])] });

        string said = "";
        OnEditor(library, _ => { }, editor =>
        {
            ((CheckBox)editor.FindName("LastMatchWinsBox")).IsChecked = true;
            ((ListBox)editor.FindName("PhaseList")).SelectedIndex = 1;
            Click(editor, "AddGateButton");
            var gate = (StackPanel)editor.FindName("GateRows");
            var kind = ((Panel)gate.Children[0]).Children.OfType<ComboBox>().First();
            kind.SelectedItem = kind.Items.Cast<ComboBoxItem>().Single(i => (string)i.Tag == "UltimateCharge");
            var row = RowOf(gate.Children[0]);
            var pickers = row.Children.OfType<ComboBox>().ToList(); // kind, who, compare, percent
            pickers[2].SelectedItem = pickers[2].Items.Cast<ComboBoxItem>().Single(i => Equals(i.Tag, Comparison.AtMost));
            pickers[3].SelectedItem = pickers[3].Items.Cast<ComboBoxItem>().Single(i => Equals(i.Tag, 0));
            row.Children.OfType<CheckBox>().Single().IsChecked = true;
            Click(editor, "SaveButton");
            said = ((TextBlock)editor.FindName("ProblemsText")).Text;
        });

        var saved = Assert.Single(library.Own.Load().Rotations);
        Assert.True(saved.LastMatchWins, $"editor said: {said}");
        Assert.Equal(3, saved.Format);
        Assert.Equal(new NotCondition(new UltimateChargeCondition("Laevatain", Comparison.AtMost, 0)), Assert.Single(saved.Phases[1].When));
    }

    /// <summary>
    /// BUG1: a rotation file holding "skill points at least 1.8" opened in the editor, and once the row was touched
    /// it saved back as 0.5 (the picker fell back to its first item: 1.8 wasn't in its list).
    /// </summary>
    [Fact]
    public void A_skill_point_value_not_in_the_old_list_survives_open_and_save()
    {
        var library = new RotationLibrary(_folder);
        var mine = Published() with { Id = "mine" };
        library.Own.Save(mine with
        {
            Phases =
            [
                .. mine.Phases,
                new RotationPhase("burst", "Burst", [new RotationStep("Laevatain", StepAction.Ultimate)],
                    [new SkillPointsCondition(Comparison.AtLeast, 1.8), new UltimateChargeCondition("Laevatain", Comparison.AtLeast, 37),
                     new SkillPointsCondition(Comparison.AtLeast, 1.85)]), // finer than the list: kept too
            ],
        });

        string said = "";
        OnEditor(library, _ => { }, editor =>
        {
            ((ListBox)editor.FindName("PhaseList")).SelectedIndex = 1;
            var gate = (StackPanel)editor.FindName("GateRows");
            foreach (var shown in gate.Children.OfType<UIElement>().ToList())
            {
                // The player changes the comparison (at least -> at most): the value must stay what the file said.
                var compare = RowOf(shown).Children.OfType<ComboBox>().First(p => p.Items.Cast<ComboBoxItem>().Any(i => Equals(i.Tag, Comparison.AtMost)));
                compare.SelectedItem = compare.Items.Cast<ComboBoxItem>().Single(i => Equals(i.Tag, Comparison.AtMost));
            }
            Click(editor, "SaveButton");
            said = ((TextBlock)editor.FindName("ProblemsText")).Text;
        });

        var saved = Assert.Single(library.Own.Load().Rotations);
        Assert.True(saved.Phases[1].When.Count == 3, $"editor said: {said}");
        Assert.Equal(new SkillPointsCondition(Comparison.AtMost, 1.8), saved.Phases[1].When[0]);
        Assert.Equal(new UltimateChargeCondition("Laevatain", Comparison.AtMost, 37), saved.Phases[1].When[1]);
        Assert.Equal(new SkillPointsCondition(Comparison.AtMost, 1.85), saved.Phases[1].When[2]);
    }

    /// <summary>
    /// The owner's ask (2026-10-24): "why do we need to right click in the rotation menu for the steps?
    /// support left click as well". The phase and step lists held drop-down items, which a list selects
    /// only on a right click. A left click now picks a phase and a step; a right click still does.
    /// </summary>
    [Fact]
    public void A_left_click_picks_a_phase_and_a_step_and_a_right_click_still_does()
    {
        var library = new RotationLibrary(_folder);
        var mine = Published() with { Id = "mine" };
        library.Own.Save(mine with
        {
            Phases =
            [
                .. mine.Phases,
                new RotationPhase("burst", "Burst", [new RotationStep("Laevatain", StepAction.Ultimate), new RotationStep("Ardelia", StepAction.Combo)]),
            ],
        });

        int phaseAfterLeft = -1, stepAfterLeft = -1, stepAfterRight = -1;
        object? actionAfterLeft = null;
        OnEditor(library, _ => { }, editor =>
        {
            // Shown off-screen, without taking focus: a list item only takes a click in a shown window.
            editor.WindowStartupLocation = WindowStartupLocation.Manual;
            editor.Left = -20000;
            editor.Top = -20000;
            editor.ShowActivated = false;
            editor.Show();
            try
            {
                editor.UpdateLayout();
                var phases = (ListBox)editor.FindName("PhaseList");
                LeftClick((UIElement)phases.Items[1]);
                phaseAfterLeft = phases.SelectedIndex;
                editor.UpdateLayout();
                var steps = (ListBox)editor.FindName("StepList");
                if (steps.Items.Count < 2) return; // the phase wasn't picked: Burst's steps aren't listed
                LeftClick((UIElement)steps.Items[1]);
                stepAfterLeft = steps.SelectedIndex;
                actionAfterLeft = ((ComboBoxItem?)((ComboBox)editor.FindName("StepActionPicker")).SelectedItem)?.Tag;
                Press((UIElement)steps.Items[0], MouseButton.Right, UIElement.MouseRightButtonDownEvent);
                stepAfterRight = steps.SelectedIndex;
            }
            finally
            {
                editor.Close();
            }
        });

        Assert.Equal(1, phaseAfterLeft);
        Assert.Equal(1, stepAfterLeft);
        Assert.Equal(StepAction.Combo, actionAfterLeft);
        Assert.Equal(0, stepAfterRight);
    }

    private static void LeftClick(UIElement item)
    {
        Press(item, MouseButton.Left, UIElement.MouseLeftButtonDownEvent);
        Press(item, MouseButton.Left, UIElement.MouseLeftButtonUpEvent);
    }

    private static void Press(UIElement item, MouseButton button, RoutedEvent routed) =>
        item.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, button) { RoutedEvent = routed });

    private static void Pick(ComboBox picker, object value) =>
        picker.SelectedItem = picker.Items.Cast<ComboBoxItem>().Single(i => Equals(i.Tag, value));

    /// <summary>
    /// The owner's example (2026-10-22), built with the editor's own buttons and lists: a phase of basic
    /// attacks shown while skill points are below 2, holding one basic-attack step. Saved in format 4.
    /// </summary>
    [Fact]
    public void A_basic_attack_phase_gated_on_less_than_two_skill_points_is_built_with_the_pickers()
    {
        var library = new RotationLibrary(_folder);
        library.Own.Save(Published() with { Id = "mine" });

        string said = "";
        bool hintShown = false;
        OnEditor(library, _ => { }, editor =>
        {
            Click(editor, "NewPhaseButton");
            ((TextBox)editor.FindName("PhaseNameBox")).Text = "Basic attacks";
            Click(editor, "AddGateButton");
            var gate = (StackPanel)editor.FindName("GateRows");
            Pick(((Panel)gate.Children[0]).Children.OfType<ComboBox>().First(), "SkillPoints");
            var pickers = RowOf(gate.Children[0]).Children.OfType<ComboBox>().ToList(); // kind, compare, value
            Pick(pickers[1], Comparison.Below);
            Pick(pickers[2], 2.0);
            Click(editor, "AddStepButton");
            Pick((ComboBox)editor.FindName("StepActionPicker"), StepAction.BasicAttack);
            hintShown = ((TextBlock)editor.FindName("BasicAttackHint")).Visibility == Visibility.Visible;
            Click(editor, "SaveButton");
            said = ((TextBlock)editor.FindName("ProblemsText")).Text;
        });

        var saved = Assert.Single(library.Own.Load().Rotations);
        Assert.True(saved.Phases.Count == 2, $"editor said: {said}");
        Assert.True(hintShown);
        var phase = saved.Phases[1];
        Assert.Equal("Basic attacks", phase.Name);
        Assert.Equal(new SkillPointsCondition(Comparison.Below, 2), Assert.Single(phase.When));
        Assert.Equal(StepAction.BasicAttack, Assert.Single(phase.Steps).Action);
        Assert.Equal(4, saved.Format);
        string file = File.ReadAllText(Directory.GetFiles(_folder, "*.json").Single());
        Assert.Contains("\"format\": 4", file);
        Assert.Contains("\"action\": \"BasicAttack\"", file);
        Assert.Contains("\"is\": \"Below\"", file);
    }

    private static readonly string[] SiSquad = ["Si", "Last_Rite", "Arcane", "Tangtang"];

    /// <summary>
    /// The owner's "keep basic attacking while Si's battle skill hasn't finished", built with the pickers:
    /// "battle skill running" offers only Si (the one operator whose skill time is read), says so under
    /// the row, and saves in format 7.
    /// </summary>
    [Fact]
    public void A_phase_gated_on_Sis_battle_skill_running_is_built_with_the_pickers()
    {
        var library = new RotationLibrary(_folder);
        library.Own.Save(new Rotation(Rotation.CurrentFormat, "mine", "Si", SiSquad,
        [
            new RotationPhase("charge", "Charge", [new RotationStep("Si", StepAction.BattleSkill)]),
            new RotationPhase("basic", "Basic", [new RotationStep("Si", StepAction.BasicAttack)], [new SkillPointsCondition(Comparison.Below, 1)]),
        ]));

        string said = "";
        List<object> offered = [];
        bool hint = false;
        OnEditor(library, _ => { }, editor =>
        {
            ((ListBox)editor.FindName("PhaseList")).SelectedIndex = 1;
            var gate = (StackPanel)editor.FindName("GateRows");
            Pick(RowOf(gate.Children[0]).Children.OfType<ComboBox>().First(), "BattleSkillActive");
            var pickers = RowOf(gate.Children[0]).Children.OfType<ComboBox>().ToList(); // kind, who, running or not
            offered = pickers[1].Items.Cast<ComboBoxItem>().Select(i => i.Tag).ToList();
            Pick(pickers[2], true);
            hint = gate.Children[0] is StackPanel { Children: [_, TextBlock { Text: "Editor.SkillTimeHint" }] };
            Click(editor, "SaveButton");
            said = ((TextBlock)editor.FindName("ProblemsText")).Text;
        }, squad: SiSquad);

        Assert.Equal(["Si"], offered);
        Assert.True(hint);
        var saved = Assert.Single(library.Own.Load().Rotations);
        Assert.True(saved.Phases[1].When.Count == 1 && saved.Phases[1].When[0] is BattleSkillActiveCondition, $"editor said: {said}");
        Assert.Equal(new BattleSkillActiveCondition("Si", true), saved.Phases[1].When[0]);
        Assert.Equal(7, saved.Format);
        Assert.Contains("\"kind\": \"battleSkillActive\"", File.ReadAllText(Directory.GetFiles(_folder, "*.json").Single()));
    }

    /// <summary>
    /// 战技已强化 built with the pickers (owner, 2026-10-24: "empowered skill should be a condition as well"): any squad
    /// operator, empowered or not (已强化 / 未强化), a line under the row saying what is read, saved in format 11.
    /// </summary>
    [Fact]
    public void A_phase_gated_on_a_battle_skill_empowered_is_built_with_the_pickers()
    {
        var library = new RotationLibrary(_folder);
        library.Own.Save(new Rotation(Rotation.CurrentFormat, "mine", "Si", SiSquad,
        [
            new RotationPhase("charge", "Charge", [new RotationStep("Si", StepAction.BattleSkill)]),
            new RotationPhase("burst", "Burst", [new RotationStep("Tangtang", StepAction.BattleSkill)], [new SkillPointsCondition(Comparison.AtLeast, 1)]),
        ]));

        string said = "";
        List<object> offered = [], states = [];
        bool hint = false;
        OnEditor(library, _ => { }, editor =>
        {
            ((ListBox)editor.FindName("PhaseList")).SelectedIndex = 1;
            var gate = (StackPanel)editor.FindName("GateRows");
            Pick(RowOf(gate.Children[0]).Children.OfType<ComboBox>().First(), "BattleSkillEmpowered");
            var pickers = RowOf(gate.Children[0]).Children.OfType<ComboBox>().ToList(); // kind, who, empowered or not
            offered = pickers[1].Items.Cast<ComboBoxItem>().Select(i => i.Tag).ToList();
            states = pickers[2].Items.Cast<ComboBoxItem>().Select(i => i.Content).ToList();
            Pick(pickers[1], "Tangtang");
            Pick(pickers[2], false);
            hint = gate.Children[0] is StackPanel { Children: [_, TextBlock { Text: "Editor.EmpoweredHint" }] };
            Click(editor, "SaveButton");
            said = ((TextBlock)editor.FindName("ProblemsText")).Text;
        }, squad: SiSquad);

        Assert.Equal(SiSquad.Order(), offered.Cast<string>().Order());
        Assert.Equal(["Editor.Empowered", "Editor.NotEmpowered"], states);
        Assert.True(hint);
        var saved = Assert.Single(library.Own.Load().Rotations);
        Assert.True(saved.Phases[1].When.Count == 1, $"editor said: {said}");
        Assert.Equal(new BattleSkillEmpoweredCondition("Tangtang", false), saved.Phases[1].When[0]);
        Assert.Equal(11, saved.Format);
        Assert.Contains("\"kind\": \"battleSkillEmpowered\"", File.ReadAllText(Directory.GetFiles(_folder, "*.json").Single()));
    }

    /// <summary>
    /// 队伍增益层数 built with the pickers (owner, 2026-10-25: "a condition that 连击>=1"): the buff picked by its Chinese
    /// name (连击), a comparison and its stacks 0 to 4, a line under the row saying what is read, saved in format 12.
    /// </summary>
    [Fact]
    public void A_phase_gated_on_team_buff_stacks_is_built_with_the_pickers()
    {
        var library = new RotationLibrary(_folder);
        library.Own.Save(new Rotation(Rotation.CurrentFormat, "mine", "Si", SiSquad,
        [
            new RotationPhase("charge", "Charge", [new RotationStep("Si", StepAction.BattleSkill)]),
            new RotationPhase("burst", "Burst", [new RotationStep("Tangtang", StepAction.BattleSkill)], [new SkillPointsCondition(Comparison.AtLeast, 1)]),
        ]));

        string said = "";
        List<object> buffs = [], values = [];
        List<string> buffNames = [];
        bool hint = false;
        OnEditor(library, _ => { }, editor =>
        {
            ((ListBox)editor.FindName("PhaseList")).SelectedIndex = 1;
            var gate = (StackPanel)editor.FindName("GateRows");
            Pick(RowOf(gate.Children[0]).Children.OfType<ComboBox>().First(), "TeamBuff");
            var pickers = RowOf(gate.Children[0]).Children.OfType<ComboBox>().ToList(); // kind, buff, comparison, stacks
            buffs = pickers[1].Items.Cast<ComboBoxItem>().Select(i => i.Tag).ToList();
            buffNames = pickers[1].Items.Cast<ComboBoxItem>().Select(i => i.Content?.ToString() ?? "").ToList();
            values = pickers[3].Items.Cast<ComboBoxItem>().Select(i => i.Tag).ToList();
            Pick(pickers[2], Comparison.AtLeast);
            Pick(pickers[3], 1);
            hint = gate.Children[0] is StackPanel { Children: [_, TextBlock { Text: "Editor.TeamBuffHint" }] };
            Click(editor, "SaveButton");
            said = ((TextBlock)editor.FindName("ProblemsText")).Text;
        }, squad: SiSquad);

        Assert.Equal(["Link"], buffs);
        Assert.Equal(["连击"], buffNames);
        Assert.Equal([0, 1, 2, 3, 4], values.Cast<int>());
        Assert.True(hint);
        var saved = Assert.Single(library.Own.Load().Rotations);
        Assert.True(saved.Phases[1].When.Count == 1, $"editor said: {said}");
        Assert.Equal(new TeamBuffCondition("Link", Comparison.AtLeast, 1), saved.Phases[1].When[0]);
        Assert.Equal(12, saved.Format);
        Assert.Contains("\"kind\": \"teamBuff\"", File.ReadAllText(Directory.GetFiles(_folder, "*.json").Single()));
    }

    /// <summary>A squad without Si: "battle skill running" is listed but greyed out, saying whose skill time is read.</summary>
    [Fact]
    public void Without_Si_battle_skill_running_is_greyed_out_and_says_why()
    {
        var library = TwoPhases(new SkillPointsCondition(Comparison.AtLeast, 2));

        (bool Enabled, object ToolTip)? offered = null;
        OnEditor(library, _ => { }, editor =>
        {
            ((ListBox)editor.FindName("PhaseList")).SelectedIndex = 1;
            var gate = (StackPanel)editor.FindName("GateRows");
            var item = RowOf(gate.Children[0]).Children.OfType<ComboBox>().First().Items.Cast<ComboBoxItem>().Single(i => Equals(i.Tag, "BattleSkillActive"));
            offered = (item.IsEnabled, item.ToolTip);
        });

        Assert.Equal((false, (object)"Editor.SkillTimeNoOperator"), offered);
    }

    /// <summary>
    /// The owner's ask (2026-10-24): a heavy attack action, ignored when following as a basic attack
    /// is. The action picker offers it, the "not detected" hint shows under it, and it's saved in format 8.
    /// </summary>
    [Fact]
    public void A_heavy_attack_step_is_picked_and_saved()
    {
        var library = new RotationLibrary(_folder);
        library.Own.Save(Published() with { Id = "mine" });

        string said = "";
        bool hintShown = false;
        OnEditor(library, _ => { }, editor =>
        {
            Click(editor, "AddStepButton");
            Pick((ComboBox)editor.FindName("StepActionPicker"), StepAction.HeavyAttack);
            hintShown = ((TextBlock)editor.FindName("BasicAttackHint")).Visibility == Visibility.Visible;
            Click(editor, "SaveButton");
            said = ((TextBlock)editor.FindName("ProblemsText")).Text;
        });

        var saved = Assert.Single(library.Own.Load().Rotations);
        Assert.True(saved.Phases[0].Steps.Count == 2, $"editor said: {said}");
        Assert.True(hintShown);
        Assert.Equal(StepAction.HeavyAttack, saved.Phases[0].Steps[1].Action);
        Assert.Equal(8, saved.Format);
        Assert.Contains("\"action\": \"HeavyAttack\"", File.ReadAllText(Directory.GetFiles(_folder, "*.json").Single()));
    }

    /// <summary>
    /// A plunge attack by whoever is controlled, from the editor: for an attack the operator picker
    /// also offers 当前操控; for a skill it doesn't (and a step left on it moves to a squad operator).
    /// </summary>
    [Fact]
    public void A_plunge_by_whoever_is_controlled_is_built_in_the_editor()
    {
        var library = new RotationLibrary(_folder);
        library.Own.Save(Published() with { Id = "mine" });

        string said = "";
        List<object> forAttack = [], forSkill = [];
        bool hintShown = false;
        OnEditor(library, _ => { }, editor =>
        {
            Click(editor, "AddStepButton");
            var who = (ComboBox)editor.FindName("StepOperator");
            var action = (ComboBox)editor.FindName("StepActionPicker");
            forSkill = who.Items.Cast<ComboBoxItem>().Select(i => i.Tag).ToList();
            Pick(action, StepAction.PlungeAttack);
            hintShown = ((TextBlock)editor.FindName("BasicAttackHint")).Visibility == Visibility.Visible;
            forAttack = who.Items.Cast<ComboBoxItem>().Select(i => i.Tag).ToList();
            Pick(who, RotationStep.Controlled);
            Click(editor, "SaveButton");
            said = ((TextBlock)editor.FindName("ProblemsText")).Text;
        });

        Assert.DoesNotContain(RotationStep.Controlled, forSkill);
        Assert.Contains(RotationStep.Controlled, forAttack);
        Assert.True(hintShown);
        Assert.Equal("", said);
        var step = Assert.Single(library.Own.Load().Rotations).Phases[0].Steps[1];
        Assert.Equal(new RotationStep(RotationStep.Controlled, StepAction.PlungeAttack), step with { Expect = [] });
        // Its words (Rotation.Action.PlungeAttack, Editor.ControlledOperator) are checked by I18nTests; the refusal here.
        Assert.True(Translated("Rotation.Problem.ControlledOperatorOnlyForAttacks"));
    }

    /// <summary>
    /// A condition step from the editor (owner, 2026-10-24): picking 条件 hides the operator and "with
    /// the previous step", seeds one condition on the shared pickers, and saves in format 9; with its
    /// condition removed, saving is refused with a sentence.
    /// </summary>
    [Fact]
    public void A_condition_step_is_built_in_the_editor()
    {
        var library = new RotationLibrary(_folder);
        library.Own.Save(Published() with { Id = "mine" });

        string said = "", refused = "";
        bool operatorHidden = false, withPreviousHidden = false, hintShown = false;
        int rows = 0;
        OnEditor(library, _ => { }, editor =>
        {
            Click(editor, "AddStepButton");
            Pick((ComboBox)editor.FindName("StepActionPicker"), StepAction.Condition);
            operatorHidden = ((FrameworkElement)editor.FindName("StepOperator")).Visibility != Visibility.Visible;
            withPreviousHidden = ((FrameworkElement)editor.FindName("StepWithPrevious")).Visibility != Visibility.Visible;
            hintShown = ((TextBlock)editor.FindName("ConditionStepHint")).Visibility == Visibility.Visible;
            var conditions = (StackPanel)editor.FindName("ConditionRows");
            rows = conditions.Children.Count;
            Click(editor, "SaveButton");
            said = ((TextBlock)editor.FindName("ProblemsText")).Text;
            // Remove its one condition: nothing to wait for.
            RowOf(conditions.Children[0]).Children.OfType<Button>().Last().RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Click(editor, "SaveButton");
            refused = ((TextBlock)editor.FindName("ProblemsText")).Text;
        });

        Assert.True(operatorHidden);
        Assert.True(withPreviousHidden);
        Assert.True(hintShown);
        Assert.Equal(1, rows);
        Assert.Equal("", said);
        Assert.Contains("Rotation.Problem.ConditionStepEmpty", refused);
        Assert.True(Translated("Rotation.Problem.ConditionStepEmpty"));
        var saved = Assert.Single(library.Own.Load().Rotations);
        var step = saved.Phases[0].Steps[1];
        Assert.Equal(StepAction.Condition, step.Action);
        Assert.Equal("", step.OperatorId);
        Assert.Single(step.Expect);
        Assert.Equal(9, saved.Format);
    }

    /// <summary>
    /// The kinds added on 2026-10-24, from the pickers: "the current phase is Charge, not played
    /// through yet" and "Laevatain's ultimate has at most 30% of its time left" (only the operators
    /// whose lasting ultimate is read are offered). Saved in format 9.
    /// </summary>
    [Fact]
    public void The_current_phase_and_the_ultimate_time_left_are_built_with_the_pickers()
    {
        var library = TwoPhases();

        string said = "";
        List<object> whoOffered = [];
        OnEditor(library, _ => { }, editor =>
        {
            ((ListBox)editor.FindName("PhaseList")).SelectedIndex = 1;               // Burst
            Click(editor, "AddGateButton");
            var gate = (StackPanel)editor.FindName("GateRows");
            Pick(RowOf(gate.Children[0]).Children.OfType<ComboBox>().First(), "PhaseCurrent");
            var current = RowOf(gate.Children[0]).Children.OfType<ComboBox>().ToList(); // kind, phase, played through
            Pick(current[1], ("p", "Charge I"));
            Pick(current[2], 2);
            Click(editor, "AddGateButton");
            Pick(RowOf(gate.Children[1]).Children.OfType<ComboBox>().First(), "UltimateTimeLeft");
            var left = RowOf(gate.Children[1]).Children.OfType<ComboBox>().ToList();   // kind, who, compare, percent
            whoOffered = left[1].Items.Cast<ComboBoxItem>().Select(i => i.Tag).ToList();
            Click(editor, "SaveButton");
            said = ((TextBlock)editor.FindName("ProblemsText")).Text;
        });

        Assert.Equal(["Laevatain"], whoOffered);
        var saved = Assert.Single(library.Own.Load().Rotations);
        Assert.True(saved.Phases[1].When.Count == 2, $"editor said: {said}");
        Assert.Equal(new PhaseCurrentCondition("p", PlayedThrough: false), saved.Phases[1].When[0]);
        Assert.Equal(new UltimateTimeLeftCondition("Laevatain", Comparison.AtMost, 30), saved.Phases[1].When[1]);
        Assert.Equal(9, saved.Format);
    }

    /// <summary>"mine": Charge (id p), then Burst (id burst) with this entry condition.</summary>
    private RotationLibrary TwoPhases(params StateCondition[] burstGate)
    {
        var library = new RotationLibrary(_folder);
        library.Own.Save(Published() with
        {
            Id = "mine",
            Phases =
            [
                new RotationPhase("p", "Charge", [new RotationStep("Ardelia", StepAction.BattleSkill)]),
                new RotationPhase("burst", "Burst", [new RotationStep("Laevatain", StepAction.Ultimate)], burstGate),
            ],
        });
        return library;
    }

    /// <summary>The owner's ask (2026-10-22): Burst only once Charge has been shown in this fight, picked from lists (the phase by its strip name).</summary>
    [Fact]
    public void A_phase_shown_gate_built_with_the_pickers_is_saved()
    {
        var library = TwoPhases();

        string said = "";
        List<string> offered = [];
        bool hint = false;
        OnEditor(library, _ => { }, editor =>
        {
            ((ListBox)editor.FindName("PhaseList")).SelectedIndex = 1;               // Burst
            Click(editor, "AddGateButton");
            var gate = (StackPanel)editor.FindName("GateRows");
            Pick(((Panel)gate.Children[0]).Children.OfType<ComboBox>().First(), "PhaseShown");
            var pickers = RowOf(gate.Children[0]).Children.OfType<ComboBox>().ToList(); // kind, phase, compare, times
            offered = pickers[1].Items.Cast<ComboBoxItem>().Select(i => (string)i.Content).ToList();
            hint = gate.Children[0] is StackPanel { Children: [_, TextBlock { Text: "Editor.PhaseShownHint" }] };
            Click(editor, "SaveButton");
            said = ((TextBlock)editor.FindName("ProblemsText")).Text;
        });

        Assert.Equal(["Charge I", "Burst II"], offered);
        Assert.True(hint);
        var saved = Assert.Single(library.Own.Load().Rotations);
        Assert.True(saved.Phases[1].When.Count == 1, $"editor said: {said}");
        Assert.Equal(new PhaseShownCondition("p", Comparison.AtLeast, 1), saved.Phases[1].When[0]);
        Assert.Equal(4, saved.Format);
    }

    /// <summary>
    /// The owner's ask (2026-10-23): "phase played" built with the pickers while the battle-skill keys
    /// are read: the phases by their strip names, a hint saying what is counted, saved at format 5.
    /// </summary>
    [Fact]
    public void A_phase_played_gate_built_with_the_pickers_is_saved()
    {
        var library = TwoPhases();

        string said = "";
        List<string> offered = [];
        bool hint = false, pickable = false;
        OnEditor(library, _ => { }, editor =>
        {
            ((ListBox)editor.FindName("PhaseList")).SelectedIndex = 1;               // Burst
            Click(editor, "AddGateButton");
            var gate = (StackPanel)editor.FindName("GateRows");
            var kind = ((Panel)gate.Children[0]).Children.OfType<ComboBox>().First();
            pickable = kind.Items.Cast<ComboBoxItem>().Single(i => Equals(i.Tag, "PhasePlayed")).IsEnabled;
            Pick(kind, "PhasePlayed");
            var pickers = RowOf(gate.Children[0]).Children.OfType<ComboBox>().ToList(); // kind, phase, compare, times
            offered = pickers[1].Items.Cast<ComboBoxItem>().Select(i => (string)i.Content).ToList();
            Pick(pickers[3], 2);
            hint = gate.Children[0] is StackPanel { Children: [_, TextBlock { Text: "Editor.PhasePlayedHint" }] };
            Click(editor, "SaveButton");
            said = ((TextBlock)editor.FindName("ProblemsText")).Text;
        });

        Assert.True(pickable);
        Assert.Equal(["Charge I", "Burst II"], offered);
        Assert.True(hint);
        var saved = Assert.Single(library.Own.Load().Rotations);
        Assert.True(saved.Phases[1].When.Count == 1, $"editor said: {said}");
        Assert.Equal(new PhasePlayedCondition("p", Comparison.AtLeast, 2), saved.Phases[1].When[0]);
        Assert.Equal(5, saved.Format);
    }

    /// <summary>
    /// "Phase played" is offered whether the battle-skill keys are read or not (it is followed from the screen); one in
    /// the rotation says what it counts.
    /// </summary>
    [Fact]
    public void Phase_played_is_offered_and_says_what_it_counts()
    {
        var library = TwoPhases(new PhasePlayedCondition("p", Comparison.AtLeast, 1));

        (bool Enabled, object Content, object ToolTip)? offered = null;
        object? kept = null;
        bool hint = false;
        OnEditor(library, _ => { }, editor =>
        {
            ((ListBox)editor.FindName("PhaseList")).SelectedIndex = 1;
            var gate = (StackPanel)editor.FindName("GateRows");
            kept = ((ComboBoxItem)RowOf(gate.Children[0]).Children.OfType<ComboBox>().First().SelectedItem).Tag;
            hint = gate.Children[0] is StackPanel { Children: [_, TextBlock { Text: "Editor.PhasePlayedHint" }] };
            Click(editor, "AddGateButton");
            var item = RowOf(gate.Children[1]).Children.OfType<ComboBox>().First().Items.Cast<ComboBoxItem>().Single(i => Equals(i.Tag, "PhasePlayed"));
            offered = (item.IsEnabled, item.Content, item.ToolTip);
        });

        Assert.Equal("PhasePlayed", kept);
        Assert.True(hint);
        Assert.Equal((true, (object)"Editor.Condition.PhasePlayed", (object?)null), offered);
    }

    /// <summary>A gate needing its own phase played before it can show is refused in words.</summary>
    [Fact]
    public void A_played_gate_that_can_never_open_is_refused_in_words()
    {
        var library = TwoPhases(new PhasePlayedCondition("burst", Comparison.AtLeast, 1));

        string said = "";
        OnEditor(library, _ => { }, editor =>
        {
            ((ListBox)editor.FindName("PhaseList")).SelectedIndex = 1;
            Click(editor, "SaveButton");
            said = ((TextBlock)editor.FindName("ProblemsText")).Text;
        });

        Assert.Contains("Rotation.Problem.PhasePlayedOwnGate", said);
        Assert.True(Translated("Rotation.Problem.PhasePlayedOwnGate") && Translated("Rotation.Problem.PhasePlayedMissingPhase"));
    }

    /// <summary>A gate on its own phase's count is refused in words (it would flip the moment the phase shows).</summary>
    [Fact]
    public void A_gate_on_its_own_count_is_refused_in_words()
    {
        var library = TwoPhases();

        string said = "";
        OnEditor(library, _ => { }, editor =>
        {
            ((ListBox)editor.FindName("PhaseList")).SelectedIndex = 1;
            Click(editor, "AddGateButton");
            var gate = (StackPanel)editor.FindName("GateRows");
            Pick(((Panel)gate.Children[0]).Children.OfType<ComboBox>().First(), "PhaseShown");
            var phase = RowOf(gate.Children[0]).Children.OfType<ComboBox>().ToList()[1];
            phase.SelectedItem = phase.Items.Cast<ComboBoxItem>().Single(i => (string)i.Content == "Burst II"); // itself
            Click(editor, "SaveButton");
            said = ((TextBlock)editor.FindName("ProblemsText")).Text;
        });

        Assert.Contains("Rotation.Problem.PhaseShownOwnGate", said);
        Assert.Empty(library.Own.Load().Rotations.Single().Phases[1].When); // not saved
        Assert.True(Translated("Rotation.Problem.PhaseShownOwnGate"));
    }

    /// <summary>A count of a phase that was deleted: the picker says so instead of showing an id, and saving explains what to do.</summary>
    [Fact]
    public void A_count_of_a_deleted_phase_shows_the_missing_sentence()
    {
        var library = TwoPhases(new PhaseShownCondition("gone", Comparison.AtLeast, 12));

        string said = "", shown = "";
        object? times = null;
        OnEditor(library, _ => { }, editor =>
        {
            ((ListBox)editor.FindName("PhaseList")).SelectedIndex = 1;
            var gate = (StackPanel)editor.FindName("GateRows");
            var pickers = RowOf(gate.Children[0]).Children.OfType<ComboBox>().ToList();
            shown = (string)((ComboBoxItem)pickers[1].SelectedItem).Content;
            times = ((ComboBoxItem)pickers[3].SelectedItem).Tag; // off the 0-9 list: kept, not swapped for 0
            Click(editor, "SaveButton");
            said = ((TextBlock)editor.FindName("ProblemsText")).Text;
        });

        Assert.Equal("Editor.PhaseMissing", shown);
        Assert.Equal(12, times);
        Assert.Contains("Rotation.Problem.PhaseShownMissingPhase", said);
        Assert.True(Translated("Rotation.Problem.PhaseShownMissingPhase"));
    }

    /// <summary>The owner's ask (2026-10-22, "rotations can be copied"): the copy-phase button puts a copy of the selected phase right after it.</summary>
    [Fact]
    public void Copying_a_phase_puts_the_copy_after_it_and_saves_both()
    {
        var library = TwoPhases(new UltimateCondition("Laevatain", true));

        string said = "";
        List<string> listed = [];
        OnEditor(library, _ => { }, editor =>
        {
            Assert.NotNull(editor.FindName("CopyPhaseButton"));
            ((ListBox)editor.FindName("PhaseList")).SelectedIndex = 0;               // Charge
            Click(editor, "CopyPhaseButton");
            listed = ((ListBox)editor.FindName("PhaseList")).Items.Cast<ListBoxItem>().Select(i => (string)i.Content).ToList();
            Click(editor, "SaveButton");
            said = ((TextBlock)editor.FindName("ProblemsText")).Text;
        });

        // Outside the app the list shows its keys; the copy's name is Editor.CopyName.
        Assert.Equal(3, listed.Count);
        var saved = Assert.Single(library.Own.Load().Rotations);
        Assert.True(saved.Phases.Count == 3, $"editor said: {said}");
        Assert.Equal(["Charge", "Editor.CopyName", "Burst"], saved.Phases.Select(p => p.Name));
        Assert.NotEqual(saved.Phases[0].Id, saved.Phases[1].Id);
        Assert.Equal(Shape(saved.Phases[0]), Shape(saved.Phases[1]));
        Assert.Equal(saved.Phases[0].When, saved.Phases[1].When);
    }

    /// <summary>The copy of a gated phase keeps its entry condition.</summary>
    [Fact]
    public void A_copied_gated_phase_keeps_its_entry_condition()
    {
        var library = TwoPhases(new UltimateCondition("Laevatain", true));

        OnEditor(library, _ => { }, editor =>
        {
            ((ListBox)editor.FindName("PhaseList")).SelectedIndex = 1;               // Burst
            Click(editor, "CopyPhaseButton");
            Click(editor, "SaveButton");
        });

        var saved = Assert.Single(library.Own.Load().Rotations);
        Assert.Equal(3, saved.Phases.Count);
        Assert.Equal(new UltimateCondition("Laevatain", true), Assert.Single(saved.Phases[2].When));
        Assert.Equal(Shape(saved.Phases[1]), Shape(saved.Phases[2]));
    }

    /// <summary>A phase's steps as comparable text (a step's list of conditions compares by reference).</summary>
    private static List<string> Shape(RotationPhase phase) =>
        phase.Steps.Select(s => $"{s.OperatorId}|{s.Action}|{s.Label}|{s.Note}|{s.WithPrevious}|{string.Join(",", s.Expect)}").ToList();

    /// <summary>
    /// The sentence the player reads is in the zh-CN strings, in Chinese, naming the phase (tests run
    /// without the app's resources, so Loc gives keys).
    /// </summary>
    private static bool Translated(string key)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string file = Path.Combine(dir.FullName, "src", "EndHud", "i18n", "zh-CN.xaml");
            if (!File.Exists(file)) continue;
            var match = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(file), $"x:Key=\"{System.Text.RegularExpressions.Regex.Escape(key)}\">([^<]*)<");
            return match.Success && match.Groups[1].Value.Contains("{0}") && match.Groups[1].Value.Any(c => c >= '\u4e00' && c <= '\u9fff');
        }
        throw new DirectoryNotFoundException("src/EndHud/i18n");
    }

    /// <summary>The items (text, value) of the last picker of a new entry condition of this kind, on the second phase (Burst).</summary>
    private List<(string Text, object Value)> NewGateValues(string kind)
    {
        var library = new RotationLibrary(_folder);
        var mine = Published() with { Id = "mine" };
        library.Own.Save(mine with { Phases = [.. mine.Phases, new RotationPhase("burst", "Burst", [new RotationStep("Laevatain", StepAction.Ultimate)])] });
        List<(string Text, object Value)> values = [];
        OnEditor(library, _ => { }, editor =>
        {
            ((ListBox)editor.FindName("PhaseList")).SelectedIndex = 1;
            Click(editor, "AddGateButton");
            var gate = (StackPanel)editor.FindName("GateRows");
            var picker = ((Panel)gate.Children[0]).Children.OfType<ComboBox>().First();
            picker.SelectedItem = picker.Items.Cast<ComboBoxItem>().Single(i => (string)i.Tag == kind);
            var value = RowOf(gate.Children[0]).Children.OfType<ComboBox>().Last();
            values = value.Items.Cast<ComboBoxItem>().Select(i => ((string)i.Content, i.Tag)).ToList();
        });
        return values;
    }

    /// <summary>The row of pickers of a condition (a row with a grey hint under it is wrapped in a panel).</summary>
    private static Panel RowOf(UIElement shown) => shown as WrapPanel ?? ((Panel)shown).Children.OfType<WrapPanel>().First();

    // ---- Finish condition (owner, 2026-10-24: "define a finish condition for the rotation similar to entry condition") ----

    /// <summary>Ardelia's battle skill, Laevatain's battle skill, an Arcane basic attack, Camille's combo: steps 1, 2 and 4 light up.</summary>
    private RotationLibrary FourSteps(params StateCondition[] finish)
    {
        var library = new RotationLibrary(_folder);
        library.Own.Save(Published() with
        {
            Id = "mine",
            Phases =
            [
                new RotationPhase("p", "充能轴",
                [
                    new RotationStep("Ardelia", StepAction.BattleSkill),
                    new RotationStep("Laevatain", StepAction.BattleSkill),
                    new RotationStep("Arcane", StepAction.BasicAttack),
                    new RotationStep("Camille", StepAction.Combo),
                ]) { Finish = finish },
            ],
        });
        return library;
    }

    /// <summary>The phase's finish condition rows, the first one's pickers, and the steps it offers to tick.</summary>
    private static (List<ComboBox> Pickers, List<CheckBox> Ticks) FinishRow(RotationEditor editor)
    {
        var row = RowOf(((StackPanel)editor.FindName("FinishRows")).Children[0]);
        var ticks = row.Children.OfType<WrapPanel>().SelectMany(p => p.Children.OfType<CheckBox>()).ToList();
        return (row.Children.OfType<ComboBox>().ToList(), ticks);
    }

    /// <summary>
    /// Under the entry condition, 完成条件: "add condition" starts with 本轴段步骤已点亮 · 全部 (the default written out:
    /// it follows the steps as they are added or removed); 至少 N 步 offers 1 to the steps that light up (the basic attack
    /// doesn't light), and is saved.
    /// </summary>
    [Fact]
    public void A_finish_condition_of_at_least_n_steps_lit_is_built_with_the_pickers_and_saved()
    {
        var library = FourSteps();

        string said = "";
        object? kind = null, which = null;
        List<object> counts = [];
        bool hint = false;
        OnEditor(library, _ => { }, editor =>
        {
            Click(editor, "AddFinishButton");
            var (pickers, _) = FinishRow(editor); // kind, which
            kind = ((ComboBoxItem)pickers[0].SelectedItem).Tag;
            which = ((ComboBoxItem)pickers[1].SelectedItem).Tag;
            Pick(pickers[1], StepsLitWhich.AtLeast);
            pickers = FinishRow(editor).Pickers; // kind, which, count
            counts = pickers[2].Items.Cast<ComboBoxItem>().Select(i => i.Tag).ToList();
            Pick(pickers[2], 2);
            hint = ((StackPanel)editor.FindName("FinishRows")).Children[0] is StackPanel { Children: [_, TextBlock { Text: "Editor.StepsLitHint" }] };
            Click(editor, "SaveButton");
            said = ((TextBlock)editor.FindName("ProblemsText")).Text;
        });

        Assert.Equal(("StepsLit", (object)StepsLitWhich.All), (kind, which));
        Assert.Equal([1, 2, 3], counts.Cast<int>());
        Assert.True(hint);
        var saved = Assert.Single(library.Own.Load().Rotations);
        Assert.True(saved.Phases[0].Finish.Count == 1, $"editor said: {said}");
        Assert.Equal(new StepsLitCondition(StepsLitWhich.AtLeast, Count: 2), saved.Phases[0].Finish[0]);
        Assert.Equal(10, saved.Format);
    }

    /// <summary>指定步骤已点亮: the phase's own steps that light up are ticked (all at first); untick one and save.</summary>
    [Fact]
    public void These_steps_lit_are_ticked_from_the_phases_own_steps()
    {
        var library = FourSteps();

        List<string> offered = [];
        OnEditor(library, _ => { }, editor =>
        {
            Click(editor, "AddFinishButton");
            Pick(FinishRow(editor).Pickers[1], StepsLitWhich.These);
            var ticks = FinishRow(editor).Ticks;
            offered = ticks.Select(t => $"{t.Content}{(t.IsChecked == true ? "+" : "-")}").ToList();
            ticks.Single(t => ((string)t.Content).StartsWith("2.", StringComparison.Ordinal)).IsChecked = false;
            Click(editor, "SaveButton");
        });

        Assert.Equal(["1. Ardelia Rotation.Action.BattleSkill+", "2. Laevatain Rotation.Action.BattleSkill+", "4. Camille Rotation.Action.Combo+"], offered);
        Assert.Equal(new StepsLitCondition(StepsLitWhich.These, Steps: [1, 4]), Assert.Single(Assert.Single(library.Own.Load().Rotations).Phases[0].Finish));
    }

    /// <summary>Moving or deleting a step keeps "these steps lit" on the same steps, not on the same numbers.</summary>
    [Fact]
    public void Moving_or_deleting_a_step_keeps_these_steps_on_their_steps()
    {
        var library = FourSteps(new StepsLitCondition(StepsLitWhich.These, Steps: [1, 4]));

        OnEditor(library, _ => { }, editor =>
        {
            var steps = (ListBox)editor.FindName("StepList");
            steps.SelectedIndex = 0;              // Ardelia's battle skill, step 1 …
            Click(editor, "StepDownButton");      // … is step 2 now
            steps.SelectedIndex = 0;              // Laevatain's battle skill, step 1 now
            Click(editor, "DeleteStepButton");    // gone: Ardelia step 1, Camille step 3
            Click(editor, "SaveButton");
        });

        var phase = Assert.Single(Assert.Single(library.Own.Load().Rotations).Phases);
        Assert.Equal([("Ardelia", StepAction.BattleSkill), ("Arcane", StepAction.BasicAttack), ("Camille", StepAction.Combo)], phase.Steps.Select(s => (s.OperatorId, s.Action)));
        Assert.Equal(new StepsLitCondition(StepsLitWhich.These, Steps: [1, 3]), Assert.Single(phase.Finish));
    }

    /// <summary>A ticked step turned into a basic attack (which never lights) leaves "these steps lit".</summary>
    [Fact]
    public void A_ticked_step_turned_into_an_attack_is_unticked()
    {
        var library = FourSteps(new StepsLitCondition(StepsLitWhich.These, Steps: [1, 2]));

        OnEditor(library, _ => { }, editor =>
        {
            ((ListBox)editor.FindName("StepList")).SelectedIndex = 1; // Laevatain's battle skill
            Pick((ComboBox)editor.FindName("StepActionPicker"), StepAction.HeavyAttack);
            Click(editor, "SaveButton");
        });

        Assert.Equal(new StepsLitCondition(StepsLitWhich.These, Steps: [1]), Assert.Single(Assert.Single(library.Own.Load().Rotations).Phases[0].Finish));
    }

    /// <summary>A step that never lights (a basic attack) ticked in a file is kept and shown, and saving is refused in words.</summary>
    [Fact]
    public void These_steps_naming_a_step_that_never_lights_is_refused_in_words()
    {
        var library = FourSteps(new StepsLitCondition(StepsLitWhich.These, Steps: [1, 3]));

        string said = "";
        List<string> offered = [];
        OnEditor(library, _ => { }, editor =>
        {
            offered = FinishRow(editor).Ticks.Where(t => t.IsChecked == true).Select(t => (string)t.Content).ToList();
            Click(editor, "SaveButton");
            said = ((TextBlock)editor.FindName("ProblemsText")).Text;
        });

        Assert.Equal(["1. Ardelia Rotation.Action.BattleSkill", "Editor.StepsLit.Missing"], offered);
        Assert.Contains("Rotation.Problem.StepsLitNotCountable", said);
        Assert.True(Translated("Rotation.Problem.StepsLitNotCountable") && Translated("Rotation.Problem.StepsLitNoSteps")
            && Translated("Rotation.Problem.StepsLitCountOutOfRange") && Translated("Rotation.Problem.FinishOperator"));
    }

    /// <summary>The entry condition and priority rules don't offer 本轴段步骤已点亮: it is about the phase's own round.</summary>
    [Fact]
    public void Steps_lit_is_offered_in_the_finish_condition_only()
    {
        var library = FourSteps();

        List<object> gate = [], finish = [];
        OnEditor(library, _ => { }, editor =>
        {
            Click(editor, "AddGateButton");
            gate = RowOf(((StackPanel)editor.FindName("GateRows")).Children[0]).Children.OfType<ComboBox>().First().Items.Cast<ComboBoxItem>().Select(i => i.Tag).ToList();
            Click(editor, "AddFinishButton");
            finish = FinishRow(editor).Pickers[0].Items.Cast<ComboBoxItem>().Select(i => i.Tag).ToList();
        });

        Assert.DoesNotContain("StepsLit", gate);
        Assert.Contains("StepsLit", finish);
        Assert.Equal(gate, finish.Where(k => !Equals(k, "StepsLit")));
    }

    [Fact]
    public void Copying_a_phase_keeps_its_finish_condition()
    {
        var draft = new PhaseDraft { Name = "p", Finish = [new StepsLitCondition(StepsLitWhich.AtLeast, Count: 2)] };

        var copy = draft.Copy("p 副本");
        Assert.Equal(draft.Finish, copy.Finish);
        copy.Finish.Clear(); // its own list

        Assert.Single(draft.Finish);
    }

    /// <summary>
    /// 0.0 to 20.0 by tenths: the bar has three segments in most fights and more in some (seven seen, 2026-10-25), and
    /// the reader takes up to 20 (owner: "if we need a max, use 20").
    /// </summary>
    [Fact]
    public void The_skill_point_picker_offers_0_to_20_by_tenths()
    {
        var items = NewGateValues("SkillPoints");
        Assert.Equal(201, items.Count);
        Assert.Equal("0.0", items[0].Text);
        Assert.Contains(items, i => i.Text == "7.0");
        Assert.Equal("20.0", items[^1].Text);
        // What the list holds is the very number a file holds, so a file's 1.8 is found in it.
        Assert.Contains(items, i => (double)i.Value == double.Parse("1.8", System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(1.8, Math.Round(18 * 0.1, 1));
    }

    [Fact]
    public void The_charge_picker_offers_every_percent()
    {
        var items = NewGateValues("UltimateCharge");
        Assert.Equal(101, items.Count);
        Assert.Equal(Enumerable.Range(0, 101), items.Select(i => (int)i.Value));
        Assert.Equal("37%", items[37].Text);
    }

    /// <summary>A new entry condition on Burst, switched to "enemy status" and to this status; returns its row's pickers.</summary>
    private static List<ComboBox> StatusGate(RotationEditor editor, string statusId)
    {
        ((ListBox)editor.FindName("PhaseList")).SelectedIndex = 1;
        Click(editor, "AddGateButton");
        var gate = (StackPanel)editor.FindName("GateRows");
        Pick(((Panel)gate.Children[0]).Children.OfType<ComboBox>().First(), "BossStatus");
        var status = RowOf(gate.Children[0]).Children.OfType<ComboBox>().ToList()[2];   // kind, has, status
        status.SelectedItem = status.Items.Cast<ComboBoxItem>().Single(i => ((StatusInfo)i.Tag).Id == statusId);
        return RowOf(gate.Children[0]).Children.OfType<ComboBox>().ToList();
    }

    /// <summary>The owner's example (2026-10-22): "the enemy has Cryo Infliction at 3 or more stacks", picked from lists.</summary>
    [Fact]
    public void A_cryo_at_least_three_stacks_gate_is_built_with_the_pickers()
    {
        var library = TwoPhases();

        string said = "";
        List<(string Text, object Value)> offered = [];
        OnEditor(library, _ => { }, editor =>
        {
            var pickers = StatusGate(editor, "Cryo Infliction");
            Assert.Equal(4, pickers.Count);                                            // kind, has, status, stacks
            offered = pickers[3].Items.Cast<ComboBoxItem>().Select(i => ((string)i.Content, i.Tag)).ToList();
            Pick(pickers[3], 3);
            Click(editor, "SaveButton");
            said = ((TextBlock)editor.FindName("ProblemsText")).Text;
        });

        Assert.Equal([1, 2, 3, 4], offered.Select(o => (int)o.Value));
        Assert.All(offered, o => Assert.Equal("Editor.StacksAtLeast", o.Text)); // outside the app Loc gives keys
        var saved = Assert.Single(library.Own.Load().Rotations);
        Assert.True(saved.Phases[1].When.Count == 1, $"editor said: {said}");
        Assert.Equal(new BossStatusCondition("Cryo Infliction", true, 3), saved.Phases[1].When[0]);
        Assert.True(Translated("Editor.StacksAtLeast"));
        Assert.True(Translated("Editor.LevelAtLeast"));
    }

    /// <summary>A reaction's count is its level (I-IV), worded as a level.</summary>
    [Fact]
    public void A_reaction_offers_its_level()
    {
        var library = TwoPhases();

        List<string> offered = [];
        OnEditor(library, _ => { }, editor =>
            offered = StatusGate(editor, "Corrosion")[3].Items.Cast<ComboBoxItem>().Select(i => (string)i.Content).ToList());

        Assert.Equal(Enumerable.Repeat("Editor.LevelAtLeast", 4), offered);
    }

    /// <summary>
    /// Vulnerable's stacks are read (the arcs around its shield): "Vulnerable at least 3 stacks" is picked from
    /// lists like an infliction's, worded as stacks, with no grey "not read" line.
    /// </summary>
    [Fact]
    public void Vulnerable_offers_its_stacks()
    {
        var library = TwoPhases();

        List<(string Text, object Value)> offered = [];
        bool line = true;
        OnEditor(library, _ => { }, editor =>
        {
            var pickers = StatusGate(editor, "Vulnerable");
            Assert.Equal(4, pickers.Count); // kind, has, status, stacks
            offered = pickers[3].Items.Cast<ComboBoxItem>().Select(i => ((string)i.Content, i.Tag)).ToList();
            var gate = (StackPanel)editor.FindName("GateRows");
            line = gate.Children[0] is StackPanel { Children: [_, TextBlock { Text: "Editor.StacksNotRead" }] };
            Pick(pickers[3], 3);
            Click(editor, "SaveButton");
        });

        Assert.Equal([1, 2, 3, 4], offered.Select(o => (int)o.Value));
        Assert.All(offered, o => Assert.Equal("Editor.StacksAtLeast", o.Text));
        Assert.False(line);
        var saved = Assert.Single(library.Own.Load().Rotations);
        Assert.Equal(new BossStatusCondition("Vulnerable", true, 3), Assert.Single(saved.Phases[1].When));
    }

    /// <summary>A status whose count isn't read (Combustion: its level badge has never been seen): no count picker, and a grey line says only "has it" is checked.</summary>
    [Fact]
    public void A_status_whose_count_is_not_read_offers_no_count()
    {
        var library = TwoPhases();

        int pickers = 0;
        bool line = false;
        OnEditor(library, _ => { }, editor =>
        {
            pickers = StatusGate(editor, "Combustion").Count;
            var gate = (StackPanel)editor.FindName("GateRows");
            line = gate.Children[0] is StackPanel { Children: [_, TextBlock { Text: "Editor.StacksNotRead" }] };
        });

        Assert.Equal(3, pickers); // kind, has, status
        Assert.True(line);
    }

    /// <summary>Switching from a counted status to one without a count drops the count picker (the row is rebuilt).</summary>
    [Fact]
    public void Switching_to_a_status_without_a_count_drops_the_count()
    {
        var library = TwoPhases(new BossStatusCondition("Cryo Infliction", true, 3));

        string said = "";
        int after = 0;
        OnEditor(library, _ => { }, editor =>
        {
            ((ListBox)editor.FindName("PhaseList")).SelectedIndex = 1;
            var gate = (StackPanel)editor.FindName("GateRows");
            var status = RowOf(gate.Children[0]).Children.OfType<ComboBox>().ToList()[2];
            status.SelectedItem = status.Items.Cast<ComboBoxItem>().Single(i => ((StatusInfo)i.Tag).Id == "Combustion");
            after = RowOf(gate.Children[0]).Children.OfType<ComboBox>().Count();
            Click(editor, "SaveButton");
            said = ((TextBlock)editor.FindName("ProblemsText")).Text;
        });

        Assert.Equal(3, after);
        var saved = Assert.Single(library.Own.Load().Rotations);
        Assert.Equal(new BossStatusCondition("Combustion", true), Assert.Single(saved.Phases[1].When));
    }

    // ---- Priorities: a number per phase, and rules "if this holds, the priority is that" ----

    /// <summary>Charge (no entry condition) and Burst (Laevatain's ultimate ready) with this priority and these rules.</summary>
    private RotationLibrary Ranked(int burstPriority = 0, int chargePriority = 0, params PriorityRule[] burstRules)
    {
        var library = new RotationLibrary(_folder);
        library.Own.Save(Published() with
        {
            Id = "mine",
            Phases =
            [
                new RotationPhase("p", "Charge", [new RotationStep("Ardelia", StepAction.BattleSkill)]) { Priority = chargePriority },
                new RotationPhase("burst", "Burst", [new RotationStep("Laevatain", StepAction.Ultimate)], [new UltimateCondition("Laevatain", true)])
                {
                    Priority = burstPriority,
                    PriorityRules = burstRules,
                },
            ],
        });
        return library;
    }

    /// <summary>Everything under an element in the logical tree.</summary>
    private static IEnumerable<DependencyObject> Inside(DependencyObject root) =>
        LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>().SelectMany(c => Inside(c).Prepend(c));

    /// <summary>The rule's own controls (its number, its buttons): not those of its conditions.</summary>
    private static List<FrameworkElement> Rules(RotationEditor editor) =>
        ((Panel)editor.FindName("PriorityRuleRows")).Children.OfType<FrameworkElement>().ToList();

    private static TextBox RuleNumber(FrameworkElement rule) => Inside(rule).OfType<TextBox>().First();

    private static void ClickRule(FrameworkElement rule, string tag) =>
        Inside(rule).OfType<Button>().First(b => Equals(b.Tag, tag)).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    private static Panel RuleConditions(FrameworkElement rule) => Inside(rule).OfType<Panel>().First(p => Equals(p.Tag, "Conditions"));

    private static RotationPhase SavedBurst(RotationLibrary library) => Assert.Single(library.Own.Load().Rotations).Phases[1];

    /// <summary>
    /// The owner's ask (2026-10-24): "if condition, priority is x". A number for the phase and a rule,
    /// built in the editor, are saved; a new rule starts one above the phase's number, with a condition
    /// to change.
    /// </summary>
    [Fact]
    public void A_priority_and_a_rule_built_in_the_editor_are_saved()
    {
        var library = Ranked();

        string said = "", started = "";
        OnEditor(library, _ => { }, editor =>
        {
            ((ListBox)editor.FindName("PhaseList")).SelectedIndex = 1;               // Burst
            ((TextBox)editor.FindName("PriorityBox")).Text = "5";
            Click(editor, "AddPriorityRuleButton");
            var rule = Assert.Single(Rules(editor));
            started = RuleNumber(rule).Text;
            Assert.Single(RuleConditions(rule).Children);                             // one condition to start with
            RuleNumber(rule).Text = "-3";
            Click(editor, "SaveButton");
            said = ((TextBlock)editor.FindName("ProblemsText")).Text;
        });

        Assert.Equal("6", started);
        var burst = SavedBurst(library);
        Assert.True(burst.Priority == 5, $"editor said: {said}");
        Assert.Equal([new PriorityRule([new UltimateCondition("Laevatain", true)], -3)], burst.PriorityRules);
        Assert.Equal(6, Assert.Single(library.Own.Load().Rotations).Format);
    }

    /// <summary>A number that isn't a whole number is refused with a sentence, under the box and on saving; nothing is saved.</summary>
    [Fact]
    public void A_priority_that_isnt_a_whole_number_is_refused_in_words()
    {
        var library = Ranked();

        List<(Visibility Shown, string Text, string Said)> refused = [];
        (Visibility Shown, string Said) fullWidth = default;
        string ruleSaid = "";
        OnEditor(library, _ => { }, editor =>
        {
            ((ListBox)editor.FindName("PhaseList")).SelectedIndex = 1;
            var box = (TextBox)editor.FindName("PriorityBox");
            var error = (TextBlock)editor.FindName("PriorityError");
            foreach (string bad in new[] { "abc", "1.5", "", "99999999999" })
            {
                box.Text = bad;
                Click(editor, "SaveButton");
                refused.Add((error.Visibility, error.Text, ((TextBlock)editor.FindName("ProblemsText")).Text));
            }
            Assert.Equal(0, SavedBurst(library).Priority); // not saved

            box.Text = "－２";                                                       // typed with a Chinese input method
            Click(editor, "SaveButton");
            fullWidth = (error.Visibility, ((TextBlock)editor.FindName("ProblemsText")).Text);

            Click(editor, "AddPriorityRuleButton");
            RuleNumber(Rules(editor)[0]).Text = "x";
            Click(editor, "SaveButton");
            ruleSaid = ((TextBlock)editor.FindName("ProblemsText")).Text;
        });

        Assert.All(refused, r => Assert.Equal((Visibility.Visible, "Editor.PriorityNotNumber"), (r.Shown, r.Text)));
        Assert.All(refused, r => Assert.Contains("Rotation.Problem.PriorityNotNumber", r.Said));
        Assert.Equal(Visibility.Collapsed, fullWidth.Shown);
        Assert.DoesNotContain("Rotation.Problem", fullWidth.Said);
        Assert.Contains("Rotation.Problem.RuleNotNumber", ruleSaid);
        Assert.Equal(-2, SavedBurst(library).Priority);                              // saved before the bad rule
        Assert.Empty(SavedBurst(library).PriorityRules);
        Assert.True(Translated("Rotation.Problem.PriorityNotNumber") && Translated("Rotation.Problem.RuleNotNumber"));
    }

    /// <summary>Rules are added, moved up and down, given conditions and removed; they're saved in their order.</summary>
    [Fact]
    public void Rules_are_added_reordered_and_removed()
    {
        var library = Ranked();

        string said = "";
        OnEditor(library, _ => { }, editor =>
        {
            ((ListBox)editor.FindName("PhaseList")).SelectedIndex = 1;
            for (int i = 1; i <= 3; i++)
            {
                Click(editor, "AddPriorityRuleButton");
                RuleNumber(Rules(editor)[i - 1]).Text = i.ToString();
            }
            ClickRule(Rules(editor)[2], "Up");                                       // 1, 3, 2
            ClickRule(Rules(editor)[0], "Down");                                     // 3, 1, 2
            ClickRule(Rules(editor)[1], "Remove");                                   // 3, 2
            ClickRule(Rules(editor)[0], "AddCondition");                             // the first rule: two conditions
            Assert.Equal(["3", "2"], Rules(editor).Select(r => RuleNumber(r).Text));
            Click(editor, "SaveButton");
            said = ((TextBlock)editor.FindName("ProblemsText")).Text;
        });

        var burst = SavedBurst(library);
        Assert.True(burst.PriorityRules.Count == 2, $"editor said: {said}");
        Assert.Equal([3, 2], burst.PriorityRules.Select(r => r.Priority));
        Assert.Equal([2, 1], burst.PriorityRules.Select(r => r.When.Count));
    }

    /// <summary>
    /// No rules: just the number, with the line saying how priorities work. On a phase without an entry
    /// condition (the otherwise phase, not ranked) the number and rules are greyed out, saying why.
    /// </summary>
    [Fact]
    public void The_priority_is_offered_only_where_it_ranks()
    {
        var library = Ranked();

        (bool Box, bool Add, Visibility Why, int Rules) charge = default, burst = default;
        string hint = "";
        OnEditor(library, _ => { }, editor =>
        {
            var phases = (ListBox)editor.FindName("PhaseList");
            (bool, bool, Visibility, int) Now() => (((TextBox)editor.FindName("PriorityBox")).IsEnabled, ((Button)editor.FindName("AddPriorityRuleButton")).IsEnabled,
                ((TextBlock)editor.FindName("PriorityNeedsGate")).Visibility, Rules(editor).Count);
            phases.SelectedIndex = 0;
            charge = Now();
            phases.SelectedIndex = 1;
            burst = Now();
            hint = ((TextBlock)editor.FindName("PriorityHint")).Text;
        });

        Assert.Equal((false, false, Visibility.Visible, 0), charge);
        Assert.Equal((true, true, Visibility.Collapsed, 0), burst);
        Assert.Equal("Editor.PriorityHint", hint);
    }

    /// <summary>
    /// A priority left on a phase without an entry condition is refused in words, and can be put back
    /// to 0 there (the box stays usable while it is set).
    /// </summary>
    [Fact]
    public void A_priority_on_the_otherwise_phase_is_refused_and_can_be_reset()
    {
        var library = Ranked(chargePriority: 2);

        string said = "", after = "";
        bool usable = false;
        OnEditor(library, _ => { }, editor =>
        {
            ((ListBox)editor.FindName("PhaseList")).SelectedIndex = 0;
            Click(editor, "SaveButton");
            said = ((TextBlock)editor.FindName("ProblemsText")).Text;
            var box = (TextBox)editor.FindName("PriorityBox");
            usable = box.IsEnabled;
            box.Text = "0";
            Click(editor, "SaveButton");
            after = ((TextBlock)editor.FindName("ProblemsText")).Text;
        });

        Assert.Contains("Rotation.Problem.PriorityWithoutGate", said);
        Assert.True(usable);
        Assert.DoesNotContain("Rotation.Problem", after);
        Assert.Equal(0, Assert.Single(library.Own.Load().Rotations).Phases[0].Priority);
        Assert.True(Translated("Rotation.Problem.PriorityWithoutGate"));
    }

    /// <summary>A rule's problems are said in words naming its phase and the rule's number: a deleted phase, no condition left.</summary>
    [Fact]
    public void A_rules_problems_are_refused_in_words()
    {
        var library = Ranked(0, 0, new PriorityRule([new PhaseShownCondition("gone", Comparison.AtLeast, 1)], 1));

        string missing = "", empty = "";
        OnEditor(library, _ => { }, editor =>
        {
            ((ListBox)editor.FindName("PhaseList")).SelectedIndex = 1;
            Click(editor, "SaveButton");
            missing = ((TextBlock)editor.FindName("ProblemsText")).Text;
            var rule = Rules(editor)[0];
            Inside(RuleConditions(rule)).OfType<Button>().First(b => (string)b.Content == "✕").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Click(editor, "SaveButton");
            empty = ((TextBlock)editor.FindName("ProblemsText")).Text;
        });

        Assert.Contains("Rotation.Problem.RuleMissingPhase", missing);
        Assert.Contains("Rotation.Problem.RuleWithoutCondition", empty);
        Assert.All(new[] { "RuleMissingPhase", "RuleWithoutCondition", "RuleOperator", "EmptyRuleGroup" },
            key => Assert.True(Translated($"Rotation.Problem.{key}"), key));
    }

    private static void Click(Window window, string name) =>
        ((Button)window.FindName(name)).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    private void OnEditor(RotationLibrary library, Action<Rotation?> changed, Action<RotationEditor> act, string[]? squad = null)
    {
        // The squad's operators, without any art: enough for the pickers.
        string reference = Path.Combine(_folder, "reference");
        Directory.CreateDirectory(reference);
        squad ??= ["Laevatain", "Camille", "Arcane", "Ardelia"];
        File.WriteAllText(Path.Combine(reference, "operators.json"), System.Text.Json.JsonSerializer.Serialize(
            squad.Select(id => new OperatorInfo(id, id, id, id + ".png"))));
        Directory.CreateDirectory(Path.Combine(reference, "icons"));
        byte[] pixel = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
        foreach (string id in squad) File.WriteAllBytes(Path.Combine(reference, "icons", id + ".png"), pixel);
        // Four statuses: an infliction and Vulnerable (stacks read), a reaction (level read), and one whose count isn't read.
        string[] statuses = ["Cryo Infliction", "Corrosion", "Vulnerable", "Combustion"];
        Directory.CreateDirectory(Path.Combine(reference, "statuses"));
        File.WriteAllText(Path.Combine(reference, "statuses.json"), System.Text.Json.JsonSerializer.Serialize(statuses.Select(id => new StatusInfo(id, id + ".png"))));
        foreach (string id in statuses) File.WriteAllBytes(Path.Combine(reference, "statuses", id + ".png"), pixel);
        // Decoded here, not on the editor's thread (the image decoder fails on it).
        var operators = OperatorLibrary.LoadAsync(Path.Combine(_folder, "user"), reference).GetAwaiter().GetResult();
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (Application.Current is null) _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                var editor = new RotationEditor(library, operators, [null, null, null, null], null, () => Task.FromResult<RotationSyncResult?>(null), changed);
                act(editor);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw failure;
    }
}
