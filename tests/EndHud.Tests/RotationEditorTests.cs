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
            var row = (Panel)gate.Children[0];
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
                    [new SkillPointsCondition(Comparison.AtLeast, 1.8), new UltimateChargeCondition("Laevatain", Comparison.AtLeast, 37)]),
            ],
        });

        string said = "";
        OnEditor(library, _ => { }, editor =>
        {
            ((ListBox)editor.FindName("PhaseList")).SelectedIndex = 1;
            var gate = (StackPanel)editor.FindName("GateRows");
            foreach (var row in gate.Children.OfType<Panel>())
            {
                // The player changes the comparison (至少 → 至多): the value must stay what the file said.
                var compare = row.Children.OfType<ComboBox>().First(p => p.Items.Cast<ComboBoxItem>().Any(i => Equals(i.Tag, Comparison.AtMost)));
                compare.SelectedItem = compare.Items.Cast<ComboBoxItem>().Single(i => Equals(i.Tag, Comparison.AtMost));
            }
            Click(editor, "SaveButton");
            said = ((TextBlock)editor.FindName("ProblemsText")).Text;
        });

        var saved = Assert.Single(library.Own.Load().Rotations);
        Assert.True(saved.Phases[1].When.Count == 2, $"editor said: {said}");
        Assert.Equal(new SkillPointsCondition(Comparison.AtMost, 1.8), saved.Phases[1].When[0]);
        Assert.Equal(new UltimateChargeCondition("Laevatain", Comparison.AtMost, 37), saved.Phases[1].When[1]);
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
