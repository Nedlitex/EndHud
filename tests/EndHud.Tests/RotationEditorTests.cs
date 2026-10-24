using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
    /// While the battle-skill keys aren't read, "phase played" is listed but greyed out, saying it needs
    /// them; one already in the rotation stays, with that same sentence under it.
    /// </summary>
    [Fact]
    public void Without_the_keys_phase_played_is_greyed_out_and_says_why()
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
            hint = gate.Children[0] is StackPanel { Children: [_, TextBlock { Text: "Editor.PhasePlayedNeedsKeys" }] };
            Click(editor, "AddGateButton");
            var item = RowOf(gate.Children[1]).Children.OfType<ComboBox>().First().Items.Cast<ComboBoxItem>().Single(i => Equals(i.Tag, "PhasePlayed"));
            offered = (item.IsEnabled, item.Content, item.ToolTip);
        }, keysRead: false);

        Assert.Equal("PhasePlayed", kept);
        Assert.True(hint);
        Assert.Equal((false, (object)"Editor.Condition.PhasePlayed.NeedsKeys", (object)"Editor.PhasePlayedNeedsKeys"), offered);
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
            listed = ((ListBox)editor.FindName("PhaseList")).Items.Cast<ComboBoxItem>().Select(i => (string)i.Content).ToList();
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

    [Fact]
    public void The_skill_point_picker_offers_0_to_3_by_tenths()
    {
        var items = NewGateValues("SkillPoints");
        Assert.Equal(31, items.Count);
        Assert.Equal("0.0", items[0].Text);
        Assert.Equal("3.0", items[^1].Text);
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

    private static void Click(Window window, string name) =>
        ((Button)window.FindName(name)).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    private void OnEditor(RotationLibrary library, Action<Rotation?> changed, Action<RotationEditor> act, bool keysRead = true)
    {
        // The squad's operators, without any art: enough for the pickers.
        string reference = Path.Combine(_folder, "reference");
        Directory.CreateDirectory(reference);
        string[] squad = ["Laevatain", "Camille", "Arcane", "Ardelia"];
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
                var editor = new RotationEditor(library, operators, [null, null, null, null], null, () => Task.FromResult<RotationSyncResult?>(null), changed, () => keysRead);
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
