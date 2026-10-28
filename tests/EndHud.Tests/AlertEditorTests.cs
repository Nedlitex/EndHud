using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using EndHud.Vision.Alerts;
using EndHud.Vision.Operators;
using EndHud.Vision.Rotations;
using Xunit;
using Rotation = EndHud.Vision.Rotations.Rotation;

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

    /// <summary>
    /// The sharing, file and save buttons are at the top, above the alerts, as in other programs (a player,
    /// 2026-10-27); what they say shows just under them.
    /// </summary>
    [Fact]
    public void The_file_and_save_buttons_are_at_the_top()
    {
        (double Save, double Paste, double Said, double Alerts) at = default;
        OnEditor(Store(), () => { }, editor =>
            at = (Top(editor, "SaveButton"), Top(editor, "PasteCodeButton"), Top(editor, "ProblemsText"), Top(editor, "RuleList")));

        Assert.True(at.Save < at.Alerts && at.Paste < at.Alerts, $"buttons at {at.Save}/{at.Paste}, the alerts at {at.Alerts}");
        Assert.InRange(at.Said, at.Save, at.Alerts);
    }

    /// <summary>How far down the editor the element is, laid out at the editor's own size (not shown).</summary>
    private static double Top(Window editor, string name)
    {
        var content = (FrameworkElement)editor.Content;
        content.Measure(new Size(editor.Width, editor.Height));
        content.Arrange(new Rect(0, 0, editor.Width, editor.Height));
        return ((FrameworkElement)editor.FindName(name)).TranslatePoint(new Point(0, 0), content).Y;
    }

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

    /// <summary>The rule list takes a left click, as the rotation editor's lists do (they took only a right click).</summary>
    [Fact]
    public void A_left_click_picks_a_rule()
    {
        var first = AlertRule.New([new SkillPointsCondition(Comparison.AtLeast, 1.8)], "First");
        var second = AlertRule.New([new BossStatusCondition("Cryo Infliction", true, 3)], "Second");
        Store().Save([first, second]);

        OnEditor(Store(), () => { }, editor =>
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
                var item = (UIElement)((ListBox)editor.FindName("RuleList")).Items[1];
                item.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
                item.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
                editor.DeleteSelected(confirm: false);
            }
            finally
            {
                editor.Close();
            }
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

    /// <summary>
    /// A hand-written alert file holding "time in phase" (the editor never offers it): refused with its own sentence, not
    /// the one naming "the phase's steps lit".
    /// </summary>
    [Fact]
    public void An_alert_on_time_in_phase_is_refused_with_its_own_sentence()
    {
        Store().Save([AlertRule.New([new TimeInPhaseCondition(Comparison.AtLeast, 3)], "x")]);
        string written = File.ReadAllText(Store().File);

        string said = "";
        OnEditor(Store(), () => { }, editor =>
        {
            Click(editor, "SaveButton");
            said = Said(editor);
        });

        Assert.Contains("Alerts.Problem.TimeInPhaseNotForAlerts", said);
        Assert.DoesNotContain("Alerts.Problem.NotForAlerts", said);
        Assert.True(Translated("Alerts.Problem.TimeInPhaseNotForAlerts", withPlaceholder: true));
        Assert.Equal(written, File.ReadAllText(Store().File)); // nothing written
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

    /// <summary>
    /// The same full set as a rotation (owner, 2026-10-24): "phase shown" too, naming a phase of any
    /// rotation, known while that rotation is followed, as "phase played".
    /// </summary>
    [Fact]
    public void Every_kind_is_offered_phase_shown_included()
    {
        List<object> kinds = [];
        OnEditor(Store(), () => { }, editor =>
        {
            Click(editor, "NewButton");
            kinds = Pickers(editor)[0].Items.Cast<ComboBoxItem>().Select(i => i.Tag).ToList();
        });

        // Every kind but those about a rotation phase's own round: the phase's steps lit, and time in phase.
        Assert.Equal(ConditionCatalogue.EditorKinds.Where(k => k is not "StepsLit" and not "TimeInPhase"), kinds.Cast<string>());
        Assert.DoesNotContain("StepsLit", kinds);
        Assert.DoesNotContain("TimeInPhase", kinds);
        Assert.Contains("PhaseShown", kinds);
        Assert.Contains("BossStatus", kinds);
        Assert.Contains("Counter", kinds);
    }

    private static Rotation Charging(string id, string name) => new(Rotation.CurrentFormat, id, name, ["Laevatain", "Camille", "Si", "Ardelia"],
    [
        new RotationPhase(id + "-charge", "Charge", [new RotationStep("Ardelia", StepAction.BattleSkill)]),
        new RotationPhase(id + "-burst", "Burst", [new RotationStep("Laevatain", StepAction.Ultimate)]),
    ]);

    /// <summary>
    /// The owner's ask (2026-10-23): "phase played" in alerts. An alert has no rotation of its own, so
    /// the phases offered are every rotation's, by the rotation's name and the strip's phase name.
    /// </summary>
    [Fact]
    public void Phase_played_picks_a_phase_of_any_rotation_and_saves()
    {
        List<string> offered = [];
        bool hint = false;
        string said = "";
        OnEditor(Store(), () => { }, editor =>
        {
            Click(editor, "NewButton");
            Pick(Pickers(editor)[0], "PhasePlayed");
            var pickers = Pickers(editor);                            // kind, phase, compare, times
            offered = pickers[1].Items.Cast<ComboBoxItem>().Select(i => (string)i.Content).ToList();
            pickers[1].SelectedItem = pickers[1].Items.Cast<ComboBoxItem>().Single(i => (string)i.Content == "Mine · Burst II");
            Pick(Pickers(editor)[3], 3);
            hint = ((StackPanel)editor.FindName("ConditionRows")).Children[0] is StackPanel { Children: [_, TextBlock { Text: "Editor.PhasePlayedHint" }] };
            Click(editor, "SaveButton");
            said = Said(editor);
        }, rotations: [Charging("a", "Mine"), Charging("b", "Other")]);

        Assert.Equal(["Mine · Charge I", "Mine · Burst II", "Other · Charge I", "Other · Burst II"], offered);
        Assert.True(hint);
        var rule = Assert.Single(Store().Load().Rules);
        Assert.True(rule.When.Count == 1, said);
        Assert.Equal(new PhasePlayedCondition("a-burst", Comparison.AtLeast, 3), rule.When[0]);
    }

    /// <summary>"Phase played" is offered in the alert editor whether the battle-skill keys are read or not (it is followed from the screen); one in a rule says what it counts.</summary>
    [Fact]
    public void Phase_played_is_offered_in_alerts_and_says_what_it_counts()
    {
        Store().Save([AlertRule.New([new PhasePlayedCondition("a-charge", Comparison.AtLeast, 1)], "x")]);

        (bool Enabled, object ToolTip)? offered = null;
        bool hint = false;
        OnEditor(Store(), () => { }, editor =>
        {
            hint = ((StackPanel)editor.FindName("ConditionRows")).Children[0] is StackPanel { Children: [_, TextBlock { Text: "Editor.PhasePlayedHint" }] };
            Click(editor, "NewButton");
            var item = Pickers(editor)[0].Items.Cast<ComboBoxItem>().Single(i => Equals(i.Tag, "PhasePlayed"));
            offered = (item.IsEnabled, item.ToolTip);
        }, rotations: [Charging("a", "Mine")]);

        Assert.True(hint);
        Assert.Equal((true, (object?)null), offered);
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

    // ---- A rule's squad: every squad, or one (owner, 2026-10-25) ----

    private static ComboBox ScopePicker(AlertEditor editor, int i) => (ComboBox)editor.FindName($"ScopeOperator{i}");

    private static void Check(AlertEditor editor, string radio) =>
        ((RadioButton)editor.FindName(radio)).IsChecked = true;

    /// <summary>The scope a rule's line in the list shows: its badge's words, and how many avatars.</summary>
    private static (string Words, int Avatars) Badge(AlertEditor editor, int rule = 0)
    {
        var line = (Panel)((ListBoxItem)((ListBox)editor.FindName("RuleList")).Items[rule]).Content;
        var badge = (Panel)line.Children[0];
        return (string.Concat(badge.Children.OfType<TextBlock>().Select(t => t.Text)), badge.Children.OfType<Image>().Count());
    }

    [Fact]
    public void A_new_rule_runs_for_every_squad_and_the_list_says_so()
    {
        bool allChecked = false, pickersShown = true;
        (string, int) badge = default;
        OnEditor(Store(), () => { }, editor =>
        {
            Click(editor, "NewButton");
            allChecked = ((RadioButton)editor.FindName("ScopeAllRadio")).IsChecked == true;
            pickersShown = ((FrameworkElement)editor.FindName("ScopeSquadPanel")).Visibility == Visibility.Visible;
            badge = Badge(editor);
            Click(editor, "SaveButton");
        });

        Assert.True(allChecked);
        Assert.False(pickersShown);
        Assert.Equal(("Alerts.Scope.AllShort", 0), badge);
        Assert.Null(Assert.Single(Store().Load().Rules).Squad);
        Assert.True(Translated("Alerts.Scope.All") && Translated("Alerts.Scope.Squad") && Translated("Alerts.Scope.AllShort") && Translated("Alerts.Scope.Hint"));
    }

    [Fact]
    public void A_rule_tied_to_two_operators_is_saved_with_them_and_listed_with_their_avatars()
    {
        (string, int) badge = default;
        bool pickersShown = false;
        OnEditor(Store(), () => { }, editor =>
        {
            Click(editor, "NewButton");
            Check(editor, "ScopeSquadRadio");
            pickersShown = ((FrameworkElement)editor.FindName("ScopeSquadPanel")).Visibility == Visibility.Visible;
            Pick(ScopePicker(editor, 0), "Camille");
            Pick(ScopePicker(editor, 2), "Laevatain");
            badge = Badge(editor);
            Click(editor, "SaveButton");
        });

        Assert.True(pickersShown);
        Assert.Equal(["Camille", "Laevatain"], Assert.Single(Store().Load().Rules).Squad);
        Assert.Equal(2, badge.Item2);
    }

    [Fact]
    public void A_squad_with_nobody_picked_is_refused_in_words()
    {
        string said = "";
        OnEditor(Store(), () => { }, editor =>
        {
            Click(editor, "NewButton");
            Check(editor, "ScopeSquadRadio");
            Click(editor, "SaveButton");
            said = Said(editor);
        });

        Assert.Contains("Alerts.Problem.EmptySquad", said);
        Assert.False(File.Exists(Store().File));
        Assert.True(Translated("Alerts.Problem.EmptySquad", withPlaceholder: true));
    }

    [Fact]
    public void A_squad_naming_an_operator_twice_is_refused_in_words()
    {
        string said = "";
        OnEditor(Store(), () => { }, editor =>
        {
            Click(editor, "NewButton");
            Check(editor, "ScopeSquadRadio");
            Pick(ScopePicker(editor, 0), "Si");
            Pick(ScopePicker(editor, 1), "Si");
            Click(editor, "SaveButton");
            said = Said(editor);
        });

        Assert.Contains("Alerts.Problem.SquadOperatorRepeated", said);
        Assert.False(File.Exists(Store().File));
        Assert.True(Translated("Alerts.Problem.SquadOperatorRepeated", withPlaceholder: true));
    }

    [Fact]
    public void Use_current_squad_fills_the_pickers_from_the_recognised_squad()
    {
        bool enabled = false;
        OnEditor(Store(), () => { }, editor =>
        {
            Click(editor, "NewButton");
            Check(editor, "ScopeSquadRadio");
            enabled = ((Button)editor.FindName("UseCurrentSquadButton")).IsEnabled;
            Click(editor, "UseCurrentSquadButton");
            Click(editor, "SaveButton");
        }, currentSquad: ["Si", "Ardelia", "Laevatain", "Camille"]);

        Assert.True(enabled);
        Assert.Equal(["Si", "Ardelia", "Laevatain", "Camille"], Assert.Single(Store().Load().Rules).Squad);
        Assert.True(Translated("Alerts.Scope.UseCurrent"));
    }

    [Fact]
    public void Use_current_squad_waits_until_every_operator_is_recognised()
    {
        static bool Enabled(AlertEditor editor)
        {
            Click(editor, "NewButton");
            Check(editor, "ScopeSquadRadio");
            return ((Button)editor.FindName("UseCurrentSquadButton")).IsEnabled;
        }
        bool partly = true, none = true, whole = false, small = false;
        OnEditor(Store(), () => { }, editor => partly = Enabled(editor), currentSquad: ["Si", null, "Laevatain", "Camille"]);
        OnEditor(Store(), () => { }, editor => none = Enabled(editor));
        OnEditor(Store(), () => { }, editor => whole = Enabled(editor), currentSquad: ["Si", "Ardelia", "Laevatain", "Camille"]);
        // A squad of three (the fourth place empty, left out): known, so it can be used.
        OnEditor(Store(), () => { }, editor => small = Enabled(editor), currentSquad: ["Si", "Laevatain", "Camille"]);

        Assert.False(partly);
        Assert.False(none);
        Assert.True(whole);
        Assert.True(small);
    }

    [Fact]
    public void A_saved_squad_rule_opens_with_its_squad_and_back_to_every_squad_saves_none()
    {
        Store().Save([AlertRule.New([new SkillPointsCondition(Comparison.AtLeast, 1.8)], "x") with { Squad = ["Si", "Camille"] }]);
        bool squadChecked = false;
        string?[] picked = [];
        OnEditor(Store(), () => { }, editor =>
        {
            squadChecked = ((RadioButton)editor.FindName("ScopeSquadRadio")).IsChecked == true;
            picked = [.. Enumerable.Range(0, 4).Select(i => (ScopePicker(editor, i).SelectedItem as ComboBoxItem)?.Tag as string)];
            Check(editor, "ScopeAllRadio");
            Click(editor, "SaveButton");
        });

        Assert.True(squadChecked);
        Assert.Equal(["Si", "Camille", "", ""], picked.Select(p => p ?? ""));
        Assert.Null(Assert.Single(Store().Load().Rules).Squad);
    }

    /// <summary>Tied to a squad: a condition's operator picker offers just that squad.</summary>
    [Fact]
    public void A_condition_on_a_squad_rule_offers_just_the_squad()
    {
        List<string> offered = [];
        OnEditor(Store(), () => { }, editor =>
        {
            Click(editor, "NewButton");
            Check(editor, "ScopeSquadRadio");
            Pick(ScopePicker(editor, 0), "Camille");
            Pick(ScopePicker(editor, 1), "Si");
            Pick(Pickers(editor)[0], "Combo");
            offered = [.. Pickers(editor)[1].Items.Cast<ComboBoxItem>().Select(i => (string)i.Tag)];
        });

        Assert.Equal(["Camille", "Si"], offered.Order());
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

    private void OnEditor(AlertStore store, Action saved, Action<AlertEditor> act, IReadOnlyList<Rotation>? rotations = null,
        IReadOnlyList<string?>? currentSquad = null)
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
                act(new AlertEditor(store, library, () => rotations ?? [], currentSquad ?? [], saved));
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
