using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using EndHud.Vision.Alerts;
using EndHud.Vision.Operators;
using EndHud.Vision.Rotations;
using Xunit;

namespace EndHud.Tests;

/// <summary>
/// The alert rules editor through its own buttons and lists: building the owner's two
/// examples, correcting a rule in place, deleting, refusing a rule without conditions in words, the
/// empty state, pasting a share code, and Si's counter with no maximum. Outside the app the strings
/// are their keys.
/// </summary>
[Collection(WpfCollection.Name)]
public sealed class AlertEditorTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "endhud-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private AlertStore Store() => new(Path.Combine(_folder, "alerts.json"));

    private static void Click(Window window, string name) =>
        ((Button)window.FindName(name)).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    private static void Pick(ComboBox picker, object value) =>
        picker.SelectedItem = picker.Items.Cast<ComboBoxItem>().Single(i => Equals(i.Tag, value));

    /// <summary>The row of pickers of a condition (a row with a grey hint under it is wrapped in a panel).</summary>
    private static Panel RowOf(UIElement shown) => shown as WrapPanel ?? ((Panel)shown).Children.OfType<WrapPanel>().First();

    private static List<ComboBox> Pickers(AlertEditor editor, int row = 0) =>
        RowOf(((StackPanel)editor.FindName("ConditionRows")).Children[row]).Children.OfType<ComboBox>().ToList();

    private static string Said(AlertEditor editor) => ((TextBlock)editor.FindName("ProblemsText")).Text;

    /// <summary>The owner's first example: skill points at least 1.8 → "Cast now", built with the New button and the pickers, saved in alerts.json.</summary>
    [Fact]
    public void Builds_a_skill_point_alert_and_saves_it()
    {
        int saves = 0;
        string said = "";
        OnEditor(Store(), () => saves++, editor =>
        {
            Click(editor, "NewButton");
            var pickers = Pickers(editor);                            // kind, compare, value
            Assert.Equal("SkillPoints", ((ComboBoxItem)pickers[0].SelectedItem).Tag);
            Pick(pickers[2], 1.8);
            ((TextBox)editor.FindName("MessageTextBox")).Text = "Cast now";
            Click(editor, "SaveButton");
            said = Said(editor);
        });

        var rule = Assert.Single(Store().Load().Rules);
        Assert.True(rule.When.Count == 1, said);
        Assert.Equal(new SkillPointsCondition(Comparison.AtLeast, 1.8), rule.When[0]);
        Assert.Equal("Cast now", rule.Message);
        Assert.Equal(1, saves); // the overlay reloads
    }

    /// <summary>The owner's second example: the enemy has Cryo Infliction at 3 or more stacks, picked from lists.</summary>
    [Fact]
    public void Builds_a_cryo_three_stacks_alert()
    {
        OnEditor(Store(), () => { }, editor =>
        {
            Click(editor, "NewButton");
            Pick(Pickers(editor)[0], "BossStatus");
            var status = Pickers(editor)[2];                          // kind, has, status
            status.SelectedItem = status.Items.Cast<ComboBoxItem>().Single(i => ((StatusInfo)i.Tag).Id == "Cryo Infliction");
            Pick(Pickers(editor)[3], 3);                              // the count picker the status brought
            Click(editor, "SaveButton");
        });

        Assert.Equal(new BossStatusCondition("Cryo Infliction", true, 3), Assert.Single(Assert.Single(Store().Load().Rules).When));
    }

    /// <summary>Correcting a rule rewrites it in place under its id: never a second rule.</summary>
    [Fact]
    public void Correcting_a_rule_keeps_one_rule()
    {
        var original = AlertRule.New([new SkillPointsCondition(Comparison.AtLeast, 1.8)], "Cast now");
        Store().Save([original]);

        OnEditor(Store(), () => { }, editor =>
        {
            Pick(Pickers(editor)[2], 2.0);
            Click(editor, "SaveButton");
        });

        var rule = Assert.Single(Store().Load().Rules);
        Assert.Equal(original.Id, rule.Id);
        Assert.Equal(new SkillPointsCondition(Comparison.AtLeast, 2.0), Assert.Single(rule.When));
        Assert.Equal("Cast now", rule.Message);
    }

    [Fact]
    public void Deleting_removes_only_the_selected_rule()
    {
        var first = AlertRule.New([new SkillPointsCondition(Comparison.AtLeast, 1.8)], "First");
        var second = AlertRule.New([new BossStatusCondition("Cryo Infliction", true, 3)], "Second");
        Store().Save([first, second]);

        OnEditor(Store(), () => { }, editor =>
        {
            ((ListBox)editor.FindName("RuleList")).SelectedIndex = 1;
            editor.DeleteSelected(confirm: false);
        });

        Assert.Equal([first], Store().Load().Rules);
    }

    [Fact]
    public void Empty_state_shows_the_example_sentence()
    {
        Visibility shown = Visibility.Collapsed, afterNew = Visibility.Visible;
        bool detailsEnabled = true;
        OnEditor(Store(), () => { }, editor =>
        {
            shown = ((TextBlock)editor.FindName("EmptyText")).Visibility;
            detailsEnabled = ((StackPanel)editor.FindName("RuleDetails")).IsEnabled;
            Click(editor, "NewButton");
            afterNew = ((TextBlock)editor.FindName("EmptyText")).Visibility;
        });

        Assert.Equal(Visibility.Visible, shown);
        Assert.False(detailsEnabled);
        Assert.Equal(Visibility.Collapsed, afterNew);
        Assert.True(Translated("Alerts.Empty"));
    }

    [Fact]
    public void A_rule_without_conditions_is_refused_in_words()
    {
        string said = "";
        OnEditor(Store(), () => { }, editor =>
        {
            Click(editor, "NewButton");
            var remove = RowOf(((StackPanel)editor.FindName("ConditionRows")).Children[0]).Children.OfType<Button>().Last(); // ✕
            remove.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Click(editor, "SaveButton");
            said = Said(editor);
        });

        Assert.Contains("Alerts.Problem.NoConditions", said);
        Assert.False(File.Exists(Store().File)); // nothing written
        Assert.True(Translated("Alerts.Problem.NoConditions", withPlaceholder: true));
    }

    [Fact]
    public void Paste_code_adds_and_reports_the_count()
    {
        var mine = AlertRule.New([new SkillPointsCondition(Comparison.AtLeast, 1.8)], "Cast now");
        Store().Save([mine]);
        var theirs = AlertRule.New([new BossStatusCondition("Cryo Infliction", true, 3)], "Freeze ready");
        string code = AlertFormat.ToShareCode([mine, theirs]); // one already here

        string status = "";
        OnEditor(Store(), () => { }, editor =>
        {
            editor.ImportCode(code);
            status = ((TextBlock)editor.FindName("StatusText")).Text;
        });

        var rules = Store().Load().Rules;
        Assert.Equal(2, rules.Count);
        Assert.Equal(mine, rules[0]);
        Assert.True(rules[1].SameAs(theirs));
        Assert.Contains("Alerts.Imported", status);
        Assert.Contains("Alerts.ImportDuplicates", status);
    }

    [Fact]
    public void A_damaged_code_is_refused_in_words()
    {
        string said = "";
        OnEditor(Store(), () => { }, editor =>
        {
            editor.ImportCode("EHA1:abc");
            said = Said(editor);
        });

        Assert.Equal("Alerts.ImportError.DamagedShareCode", said);
        Assert.Empty(Store().Load().Rules);
    }

    /// <summary>An alert has no rotation: "phase shown" isn't offered.</summary>
    [Fact]
    public void Phase_shown_is_not_offered()
    {
        List<object> kinds = [];
        OnEditor(Store(), () => { }, editor =>
        {
            Click(editor, "NewButton");
            kinds = Pickers(editor)[0].Items.Cast<ComboBoxItem>().Select(i => i.Tag).ToList();
        });

        Assert.DoesNotContain("PhaseShown", kinds);
        Assert.Contains("BossStatus", kinds);
        Assert.Contains("Counter", kinds);
    }

    /// <summary>
    /// Si's damage stacks have no maximum (owner: "si has no limit"): the value is picked from 0 to 200,
    /// and a larger value from a file is kept, never cut to a maximum.
    /// </summary>
    [Fact]
    public void Si_stacks_are_picked_from_a_long_list_and_a_larger_value_is_kept()
    {
        Store().Save([AlertRule.New([new CounterCondition("Si", "damage-stacks", Comparison.AtLeast, 150)], "Big"),
                      AlertRule.New([new CounterCondition("Si", "damage-stacks", Comparison.AtLeast, 260)], "Bigger")]);

        List<int> offered = [];
        object? selected = null;
        OnEditor(Store(), () => { }, editor =>
        {
            var pickers = Pickers(editor);                            // kind, counter, compare, value
            offered = pickers[3].Items.Cast<ComboBoxItem>().Select(i => (int)i.Tag).ToList();
            selected = ((ComboBoxItem)pickers[3].SelectedItem).Tag;
            Pick(pickers[2], Comparison.Above);
            ((ListBox)editor.FindName("RuleList")).SelectedIndex = 1;
            Pick(Pickers(editor)[2], Comparison.Above);
            Click(editor, "SaveButton");
        });

        Assert.Equal(Enumerable.Range(0, ConditionEditor.UncappedCounterChoices + 1), offered);
        Assert.Equal(150, selected);
        var rules = Store().Load().Rules;
        Assert.Equal(new CounterCondition("Si", "damage-stacks", Comparison.Above, 150), rules[0].When[0]);
        Assert.Equal(new CounterCondition("Si", "damage-stacks", Comparison.Above, 260), rules[1].When[0]);
    }

    /// <summary>A file from a newer EndHud: the window says so and can't save over it.</summary>
    [Fact]
    public void A_newer_file_is_said_and_never_saved_over()
    {
        Directory.CreateDirectory(_folder);
        const string newer = "{ \"format\": 9, \"rules\": [] }";
        File.WriteAllText(Store().File, newer);

        string said = "";
        bool saveEnabled = true;
        OnEditor(Store(), () => { }, editor =>
        {
            said = ((TextBlock)editor.FindName("StoreText")).Text;
            saveEnabled = ((Button)editor.FindName("SaveButton")).IsEnabled;
            Click(editor, "NewButton");
            Click(editor, "SaveButton");
        });

        Assert.Equal("Alerts.Newer", said);
        Assert.False(saveEnabled);
        Assert.Equal(newer, File.ReadAllText(Store().File));
    }

    /// <summary>The sentence the player reads is in the zh-CN strings, in Chinese.</summary>
    private static bool Translated(string key, bool withPlaceholder = false)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string file = Path.Combine(dir.FullName, "src", "EndHud", "i18n", "zh-CN.xaml");
            if (!File.Exists(file)) continue;
            var match = Regex.Match(File.ReadAllText(file), $"x:Key=\"{Regex.Escape(key)}\">([^<]*)<");
            return match.Success && (!withPlaceholder || match.Groups[1].Value.Contains("{0}")) && match.Groups[1].Value.Any(c => c >= '\u4e00' && c <= '\u9fff');
        }
        throw new DirectoryNotFoundException("src/EndHud/i18n");
    }

    private void OnEditor(AlertStore store, Action saved, Action<AlertEditor> act)
    {
        // Operators (with Si, whose counter has no maximum) and three statuses, without any art: enough for the pickers.
        string reference = Path.Combine(_folder, "reference");
        Directory.CreateDirectory(Path.Combine(reference, "icons"));
        Directory.CreateDirectory(Path.Combine(reference, "statuses"));
        string[] operators = ["Laevatain", "Camille", "Si", "Ardelia"];
        File.WriteAllText(Path.Combine(reference, "operators.json"), System.Text.Json.JsonSerializer.Serialize(
            operators.Select(id => new OperatorInfo(id, id, id, id + ".png"))));
        byte[] pixel = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
        foreach (string id in operators) File.WriteAllBytes(Path.Combine(reference, "icons", id + ".png"), pixel);
        string[] statuses = ["Cryo Infliction", "Corrosion", "Vulnerable"];
        File.WriteAllText(Path.Combine(reference, "statuses.json"), System.Text.Json.JsonSerializer.Serialize(statuses.Select(id => new StatusInfo(id, id + ".png"))));
        foreach (string id in statuses) File.WriteAllBytes(Path.Combine(reference, "statuses", id + ".png"), pixel);
        // Decoded here, not on the editor's thread (the image decoder fails on it).
        var library = OperatorLibrary.LoadAsync(Path.Combine(_folder, "user"), reference).GetAwaiter().GetResult();
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (Application.Current is null) _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                act(new AlertEditor(store, library, saved));
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
