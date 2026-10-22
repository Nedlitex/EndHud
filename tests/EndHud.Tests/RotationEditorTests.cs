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
    /// BUG1: a rotation file holding 技力 至少 1.8 opened in the editor, and once the row was touched
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
                new RotationPhase("burst", "爆发轴", [new RotationStep("Laevatain", StepAction.Ultimate)],
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
                // The player changes the comparison (至少 → 至多): the value must stay what the file said.
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
    /// The owner's example (2026-10-22), built with the editor's own buttons and lists: a phase 普攻轴
    /// shown while 技力 is less than 2, holding one 普攻 (basic attack) step. Saved in format 4.
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
            ((TextBox)editor.FindName("PhaseNameBox")).Text = "普攻轴";
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
        Assert.Equal("普攻轴", phase.Name);
        Assert.Equal(new SkillPointsCondition(Comparison.Below, 2), Assert.Single(phase.When));
        Assert.Equal(StepAction.BasicAttack, Assert.Single(phase.Steps).Action);
        Assert.Equal(4, saved.Format);
        string file = File.ReadAllText(Directory.GetFiles(_folder, "*.json").Single());
        Assert.Contains("\"format\": 4", file);
        Assert.Contains("\"action\": \"BasicAttack\"", file);
        Assert.Contains("\"is\": \"Below\"", file);
    }

    /// <summary>"mine": 充能轴 (id p), then 爆发轴 (id burst) with this entry condition.</summary>
    private RotationLibrary TwoPhases(params StateCondition[] burstGate)
    {
        var library = new RotationLibrary(_folder);
        var mine = Published() with { Id = "mine" };
        library.Own.Save(mine with { Phases = [.. mine.Phases, new RotationPhase("burst", "爆发轴", [new RotationStep("Laevatain", StepAction.Ultimate)], burstGate)] });
        return library;
    }

    /// <summary>The owner's ask (2026-10-22): 爆发轴 only once 充能轴 has been shown in this fight, picked from lists (the phase by its strip name).</summary>
    [Fact]
    public void A_phase_shown_gate_built_with_the_pickers_is_saved()
    {
        var library = TwoPhases();

        string said = "";
        List<string> offered = [];
        bool hint = false;
        OnEditor(library, _ => { }, editor =>
        {
            ((ListBox)editor.FindName("PhaseList")).SelectedIndex = 1;               // 爆发轴
            Click(editor, "AddGateButton");
            var gate = (StackPanel)editor.FindName("GateRows");
            Pick(((Panel)gate.Children[0]).Children.OfType<ComboBox>().First(), "PhaseShown");
            var pickers = RowOf(gate.Children[0]).Children.OfType<ComboBox>().ToList(); // kind, phase, compare, times
            offered = pickers[1].Items.Cast<ComboBoxItem>().Select(i => (string)i.Content).ToList();
            hint = gate.Children[0] is StackPanel { Children: [_, TextBlock { Text: "Editor.PhaseShownHint" }] };
            Click(editor, "SaveButton");
            said = ((TextBlock)editor.FindName("ProblemsText")).Text;
        });

        Assert.Equal(["充能轴 I", "爆发轴 II"], offered);
        Assert.True(hint);
        var saved = Assert.Single(library.Own.Load().Rotations);
        Assert.True(saved.Phases[1].When.Count == 1, $"editor said: {said}");
        Assert.Equal(new PhaseShownCondition("p", Comparison.AtLeast, 1), saved.Phases[1].When[0]);
        Assert.Equal(4, saved.Format);
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
            phase.SelectedItem = phase.Items.Cast<ComboBoxItem>().Single(i => (string)i.Content == "爆发轴 II"); // itself
            Click(editor, "SaveButton");
            said = ((TextBlock)editor.FindName("ProblemsText")).Text;
        });

        Assert.Contains("Rotation.Problem.PhaseShownOwnGate", said);
        Assert.Empty(library.Own.Load().Rotations.Single().Phases[1].When); // not saved
        Assert.Contains("轴段“{0}”的进入条件用了它自己的显示次数：它一出现，次数就变了，条件也跟着变。请改用其他轴段的次数。", ZhCn());
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
        Assert.Contains("轴段“{0}”的条件引用了一个已删除的轴段，请重新选择轴段或删除这个条件。", ZhCn());
    }

    /// <summary>The player-facing strings, as written (tests run without the app's resources, so Loc gives keys).</summary>
    private static string ZhCn()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "EndHud.sln"))) return File.ReadAllText(Path.Combine(dir.FullName, "src", "EndHud", "i18n", "zh-CN.xaml"));
        throw new DirectoryNotFoundException("EndHud.sln");
    }

    /// <summary>The items (text, value) of the last picker of a new entry condition of this kind, on the second phase (爆发轴).</summary>
    private List<(string Text, object Value)> NewGateValues(string kind)
    {
        var library = new RotationLibrary(_folder);
        var mine = Published() with { Id = "mine" };
        library.Own.Save(mine with { Phases = [.. mine.Phases, new RotationPhase("burst", "爆发轴", [new RotationStep("Laevatain", StepAction.Ultimate)])] });
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

    private static void Click(Window window, string name) =>
        ((Button)window.FindName(name)).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    private void OnEditor(RotationLibrary library, Action<Rotation?> changed, Action<RotationEditor> act)
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
