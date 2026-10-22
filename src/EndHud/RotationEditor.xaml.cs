using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using EndHud.Vision.Operators;
using EndHud.Vision.Rotations;
using Microsoft.Win32;
using Rotation = EndHud.Vision.Rotations.Rotation;

namespace EndHud;

/// <summary>
/// The rotation editor (排轴编辑器): make, change, delete, import and export rotations. Everything is
/// picked from lists (operators from the squad, actions, conditions, statuses); only names and notes
/// are typed. Saving checks the rotation and says what's missing in words.
/// </summary>
public partial class RotationEditor : Window
{
    private readonly RotationLibrary _store;
    private readonly Func<Task<RotationSyncResult?>> _restorePublished;
    private readonly OperatorLibrary _library;
    private readonly IReadOnlyList<string?> _currentSquad;
    private readonly Action<Rotation?> _changed;
    private readonly Dictionary<string, BitmapSource?> _icons = [];
    private IReadOnlyList<LibraryRotation> _rotations = [];
    private RotationDraft? _draft;
    private PhaseDraft? _phase;
    private StepDraft? _step;
    private bool _dirty;
    private bool _loading;

    /// <param name="currentSquad">The squad EndHud recognises now (for "use the current squad"), null slots unknown.</param>
    /// <param name="select">The rotation to open with, if any.</param>
    /// <param name="restorePublished">Bring back deleted recommended rotations and download them again.</param>
    /// <param name="changed">Called after a rotation is saved (with it) or deleted (with null).</param>
    internal RotationEditor(RotationLibrary store, OperatorLibrary library, IReadOnlyList<string?> currentSquad, string? select,
        Func<Task<RotationSyncResult?>> restorePublished, Action<Rotation?> changed)
    {
        _store = store;
        _restorePublished = restorePublished;
        _library = library;
        _currentSquad = currentSquad;
        _changed = changed;
        InitializeComponent();

        foreach (var action in Enum.GetValues<StepAction>()) StepActionPicker.Items.Add(Item(Loc.Get($"Rotation.Action.{action}"), action));
        foreach (var picker in SquadPickers) FillOperators(picker, _library.Operators);
        UseCurrentSquadButton.IsEnabled = currentSquad.Count == 4 && currentSquad.All(id => id is not null);
        UseCurrentSquadButton.ToolTip = UseCurrentSquadButton.IsEnabled ? null : Loc.Get("Editor.UseCurrentSquad.Unavailable");

        LoadRotations(select);
        Closing += (_, e) => e.Cancel = !ConfirmDiscard();
    }

    private ComboBox[] SquadPickers => [Squad0, Squad1, Squad2, Squad3];

    // ---- Which rotation ----

    private void LoadRotations(string? select)
    {
        var (rotations, unreadable) = _store.Load();
        _rotations = rotations;
        _loading = true;
        RotationPicker.Items.Clear();
        foreach (var (r, published) in rotations)
            RotationPicker.Items.Add(Item(Loc.Format(published ? "Editor.PublishedItem" : "Editor.OwnItem", r.Name, string.Join("·", r.Squad.Select(OperatorName))), r.Id));
        _loading = false;

        UnreadableText.Visibility = unreadable.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UnreadableText.Text = string.Join("\n", unreadable.Select(u =>
            Loc.Format("Editor.Unreadable", Path.GetFileName(u.File), Loc.Get($"Editor.ReadError.{u.Error}"))));

        var selected = rotations.FirstOrDefault(r => r.Rotation.Id == select)?.Rotation ?? rotations.FirstOrDefault()?.Rotation;
        if (selected is null)
        {
            ShowDraft(null);
            return;
        }
        RotationPicker.SelectedItem = RotationPicker.Items.Cast<ComboBoxItem>().First(i => (string)i.Tag == selected.Id);
    }

    private void RotationPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || RotationPicker.SelectedItem is not ComboBoxItem { Tag: string id }) return;
        if (_draft?.Id == id) return;
        if (!ConfirmDiscard())
        {
            Reselect(_draft?.Id);
            return;
        }
        if (_rotations.FirstOrDefault(r => r.Rotation.Id == id) is { } entry) ShowDraft(RotationDraft.From(entry.Rotation));
    }

    private void Reselect(string? id)
    {
        _loading = true;
        RotationPicker.SelectedItem = RotationPicker.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == id);
        _loading = false;
    }

    private void NewButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscard()) return;
        var draft = RotationDraft.New(Loc.Get("Editor.NewName"), _currentSquad.All(id => id is not null) ? _currentSquad : null);
        var phase = new PhaseDraft { Name = Loc.Get("Editor.NewPhaseName") };
        draft.Phases.Add(phase);
        ShowDraft(draft);
        MarkDirty();
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_draft is null || !ConfirmDiscard()) return;
        var copy = RotationDraft.From(RotationStore.AsCopy(_draft.ToRotation(), Loc.Format("Editor.CopyName", _draft.Name)));
        ShowDraft(copy);
        MarkDirty();
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e) => DeleteSelected(confirm: true);

    /// <summary>Delete the rotation shown (hide it, if it's a recommended one), asking first when <paramref name="confirm"/>.</summary>
    internal void DeleteSelected(bool confirm)
    {
        if (_draft is null) return;
        bool published = IsPublished(_draft.Id);
        if (!_store.Own.Exists(_draft.Id) && !published)
        {
            // Never saved: just drop it.
            _dirty = false;
            LoadRotations(null);
            return;
        }
        string question = Loc.Format(published ? "Editor.ConfirmHide" : "Editor.ConfirmDelete", _draft.Name);
        if (confirm && MessageBox.Show(this, question, Title, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        _store.Delete(_draft.Id);
        _dirty = false;
        _changed(null);
        LoadRotations(null);
        Status(Loc.Get("Editor.Deleted"));
    }

    // ---- Showing a rotation ----

    private void ShowDraft(RotationDraft? draft)
    {
        _draft = draft;
        _dirty = false;
        bool has = draft is not null;
        PublishedNote.Visibility = draft is not null && IsPublished(draft.Id) ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Visibility = _rotations.Count == 0 && !has ? Visibility.Visible : Visibility.Collapsed;
        Body.IsEnabled = has;
        NameBox.IsEnabled = CopyButton.IsEnabled = DeleteButton.IsEnabled = SaveButton.IsEnabled = has;
        foreach (var picker in SquadPickers) picker.IsEnabled = has;

        _loading = true;
        NameBox.Text = draft?.Name ?? "";
        for (int i = 0; i < 4; i++) Select(SquadPickers[i], draft?.Squad[i]);
        LastMatchWinsBox.IsChecked = draft?.LastMatchWins == true;
        _loading = false;

        _phase = draft?.Phases.FirstOrDefault();
        RefreshPhases();
        Problems();
        ProblemsText.Text = "";
    }

    private void LastMatchWins_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || _draft is null) return;
        _draft.LastMatchWins = LastMatchWinsBox.IsChecked == true;
        MarkDirty();
        Preview();
    }

    private void RefreshPhases()
    {
        RefreshPhaseList();
        _loading = true;
        PhaseNameBox.Text = _phase?.Name ?? "";
        PhaseNameBox.IsEnabled = AddGateButton.IsEnabled = CopyPhaseButton.IsEnabled = _phase is not null;
        _loading = false;
        BuildGateRows();
        StepsGroup.Header = _phase is null ? Loc.Get("Editor.Steps") : Loc.Format("Editor.StepsOf", _phase.Name);
        _step = _phase?.Steps.FirstOrDefault();
        RefreshSteps();
    }

    /// <summary>The phases in order; one with an entry condition says so.</summary>
    private void RefreshPhaseList()
    {
        _loading = true;
        PhaseList.Items.Clear();
        var phases = _draft?.Phases ?? [];
        for (int i = 0; i < phases.Count; i++)
            PhaseList.Items.Add(Item(Loc.Format(phases[i].When.Count > 0 ? "Editor.PhaseItemGated" : "Editor.PhaseItem", i + 1, phases[i].Name), phases[i].Id));
        PhaseList.SelectedItem = PhaseList.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == _phase?.Id);
        _loading = false;
    }

    private void RefreshSteps()
    {
        _loading = true;
        StepList.Items.Clear();
        if (_phase is not null)
        {
            for (int i = 0; i < _phase.Steps.Count; i++) StepList.Items.Add(Item(StepText(i, _phase.Steps[i]), i));
            if (_step is not null) StepList.SelectedIndex = _phase.Steps.IndexOf(_step);
        }
        _loading = false;
        ShowStep();
        Preview();
    }

    private string StepText(int index, StepDraft step)
    {
        string text = $"{index + 1}. {OperatorName(step.OperatorId)} {step.Label ?? Loc.Get($"Rotation.Action.{step.Action}")}";
        if (step.WithPrevious) text += $"  {Loc.Get("Editor.WithPreviousShort")}";
        if (step.Expect.Count > 0) text += $"  {Loc.Format("Editor.ConditionCount", step.Expect.Count)}";
        if (!string.IsNullOrWhiteSpace(step.Note)) text += $"  · {step.Note}";
        return text;
    }

    private void ShowStep()
    {
        StepDetails.IsEnabled = _step is not null;
        _loading = true;
        FillOperators(StepOperator, SquadOperators());
        Select(StepOperator, _step?.OperatorId);
        StepActionPicker.SelectedItem = StepActionPicker.Items.Cast<ComboBoxItem>().FirstOrDefault(i => _step is not null && (StepAction)i.Tag == _step.Action);
        StepWithPrevious.IsChecked = _step?.WithPrevious ?? false;
        StepWithPrevious.IsEnabled = _phase is not null && _step is not null && _phase.Steps.IndexOf(_step) > 0;
        StepLabel.Text = _step?.Label ?? "";
        StepNote.Text = _step?.Note ?? "";
        _loading = false;
        ShowBasicAttackHint();
        BuildConditionRows();
    }

    /// <summary>A basic attack isn't detected: say so under the action, and how its phase's condition decides when it shows.</summary>
    private void ShowBasicAttackHint() =>
        BasicAttackHint.Visibility = _step?.Action == StepAction.BasicAttack ? Visibility.Visible : Visibility.Collapsed;

    // ---- Name and squad ----

    private void NameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || _draft is null) return;
        _draft.Name = NameBox.Text;
        MarkDirty();
    }

    private void Squad_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _draft is null) return;
        int i = Array.IndexOf(SquadPickers, sender);
        _draft.Squad[i] = (SquadPickers[i].SelectedItem as ComboBoxItem)?.Tag as string;
        MarkDirty();
        ShowStep();
        RefreshSteps();
    }

    private void UseCurrentSquadButton_Click(object sender, RoutedEventArgs e)
    {
        if (_draft is null) return;
        _loading = true;
        for (int i = 0; i < 4; i++)
        {
            _draft.Squad[i] = _currentSquad[i];
            Select(SquadPickers[i], _currentSquad[i]);
        }
        _loading = false;
        MarkDirty();
        RefreshSteps();
    }

    // ---- Phases ----

    private void PhaseList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _draft is null || PhaseList.SelectedItem is not ComboBoxItem { Tag: string id }) return;
        _phase = _draft.Phases.FirstOrDefault(p => p.Id == id);
        RefreshPhases();
    }

    private void PhaseUp_Click(object sender, RoutedEventArgs e) => MovePhase(-1);
    private void PhaseDown_Click(object sender, RoutedEventArgs e) => MovePhase(1);

    private void MovePhase(int by)
    {
        if (_draft is null || _phase is null) return;
        int from = _draft.Phases.IndexOf(_phase), to = from + by;
        if (to < 0 || to >= _draft.Phases.Count) return;
        _draft.Phases.RemoveAt(from);
        _draft.Phases.Insert(to, _phase);
        MarkDirty();
        RefreshPhaseList();
        Preview();
    }

    private void PhaseNameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || _phase is null) return;
        _phase.Name = PhaseNameBox.Text;
        MarkDirty();
        StepsGroup.Header = Loc.Format("Editor.StepsOf", _phase.Name);
        // Keep the list's names in step without disturbing the text box.
        RefreshPhaseList();
        Preview();
    }

    private void NewPhase_Click(object sender, RoutedEventArgs e)
    {
        if (_draft is null) return;
        _phase = new PhaseDraft { Name = Loc.Format("Editor.NewPhaseNumbered", _draft.Phases.Count + 1) };
        _draft.Phases.Add(_phase);
        MarkDirty();
        RefreshPhases();
    }

    /// <summary>复制轴段: a copy of the selected phase (steps and entry condition), right after it, selected.</summary>
    private void CopyPhase_Click(object sender, RoutedEventArgs e)
    {
        if (_draft is null || _phase is null) return;
        var copy = _phase.Copy(Loc.Format("Editor.CopyName", _phase.Name));
        _draft.Phases.Insert(_draft.Phases.IndexOf(_phase) + 1, copy);
        _phase = copy;
        MarkDirty();
        RefreshPhases();
    }

    private void DeletePhase_Click(object sender, RoutedEventArgs e)
    {
        if (_draft is null || _phase is null) return;
        if (MessageBox.Show(this, Loc.Format("Editor.ConfirmDeletePhase", _phase.Name), Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        _draft.Phases.Remove(_phase);
        _phase = _draft.Phases.FirstOrDefault();
        MarkDirty();
        RefreshPhases();
    }

    // ---- Steps ----

    private void StepList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _phase is null || StepList.SelectedIndex < 0) return;
        _step = _phase.Steps[StepList.SelectedIndex];
        ShowStep();
    }

    private void AddStep_Click(object sender, RoutedEventArgs e)
    {
        if (_phase is null) return;
        // After the selected step, else at the end.
        int at = StepList.SelectedIndex >= 0 ? StepList.SelectedIndex + 1 : _phase.Steps.Count;
        _step = new StepDraft { OperatorId = SquadOperators().FirstOrDefault()?.Id ?? "", Action = StepAction.BattleSkill };
        _phase.Steps.Insert(at, _step);
        MarkDirty();
        RefreshSteps();
    }

    private void StepUp_Click(object sender, RoutedEventArgs e) => MoveStep(-1);
    private void StepDown_Click(object sender, RoutedEventArgs e) => MoveStep(1);

    private void MoveStep(int by)
    {
        if (_phase is null || _step is null) return;
        int from = _phase.Steps.IndexOf(_step), to = from + by;
        if (to < 0 || to >= _phase.Steps.Count) return;
        _phase.Steps.RemoveAt(from);
        _phase.Steps.Insert(to, _step);
        if (to == 0) _step.WithPrevious = false; // the first step has nothing before it
        MarkDirty();
        RefreshSteps();
    }

    private void DeleteStep_Click(object sender, RoutedEventArgs e)
    {
        if (_phase is null || _step is null) return;
        int index = _phase.Steps.IndexOf(_step);
        _phase.Steps.RemoveAt(index);
        if (index == 0 && _phase.Steps.Count > 0) _phase.Steps[0].WithPrevious = false;
        _step = _phase.Steps.Count == 0 ? null : _phase.Steps[Math.Min(index, _phase.Steps.Count - 1)];
        MarkDirty();
        RefreshSteps();
    }

    private void StepField_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || _step is null) return;
        if (StepOperator.SelectedItem is ComboBoxItem { Tag: string id }) _step.OperatorId = id;
        if (StepActionPicker.SelectedItem is ComboBoxItem { Tag: StepAction action }) _step.Action = action;
        _step.WithPrevious = StepWithPrevious.IsChecked == true;
        _step.Label = StepLabel.Text;
        _step.Note = StepNote.Text;
        ShowBasicAttackHint();
        MarkDirty();
        int index = _phase!.Steps.IndexOf(_step);
        _loading = true;
        if (index >= 0 && index < StepList.Items.Count) ((ComboBoxItem)StepList.Items[index]).Content = StepText(index, _step);
        _loading = false;
        Preview();
    }

    // ---- Conditions: a step's expected state, a phase's entry condition ----

    private static readonly string[] ConditionKinds = ["SkillPoints", "Combo", "ComboCharge", "BattleSkill", "Ultimate", "UltimateCharge", "UltimateActive", "Counter", "BossStatus", "Controlling", "PhaseShown", "Group"];

    private void AddCondition_Click(object sender, RoutedEventArgs e)
    {
        if (_step is null) return;
        _step.Expect.Add(DefaultCondition("SkillPoints"));
        StepConditionsChanged(rebuild: true);
    }

    private void AddGateCondition_Click(object sender, RoutedEventArgs e)
    {
        if (_phase is null) return;
        _phase.When.Add(DefaultCondition("Ultimate"));
        GateConditionsChanged(rebuild: true);
    }

    private StateCondition DefaultCondition(string kind)
    {
        string first = SquadOperators().FirstOrDefault()?.Id ?? "";
        return kind switch
        {
            "Combo" => new ComboCondition(first, true),
            "Ultimate" => new UltimateCondition(first, true),
            "UltimateCharge" => new UltimateChargeCondition(first, Comparison.AtLeast, 80),
            "UltimateActive" => new UltimateActiveCondition(first, true),
            "ComboCharge" => new ComboChargeCondition(first, Comparison.AtLeast, 80),
            "BattleSkill" => new BattleSkillCondition(first, true),
            "Counter" => Counters().FirstOrDefault() is { } c ? new CounterCondition(c.OperatorId, c.Id, Comparison.AtLeast, c.Max) : new SkillPointsCondition(Comparison.AtLeast, 2),
            "BossStatus" => new BossStatusCondition(_library.Statuses.FirstOrDefault()?.Id ?? "", true),
            "Controlling" => new ControllingCondition(first),
            // Another phase's count by default: a phase's entry condition can't use its own (Rotation.Problems).
            "PhaseShown" => new PhaseShownCondition((_draft?.Phases.FirstOrDefault(p => p != _phase) ?? _phase)?.Id ?? "", Comparison.AtLeast, 1),
            // A group is made to say "or": it starts as "any of" with one condition in it.
            "Group" => new AnyCondition([new UltimateCondition(first, true)]),
            _ => new SkillPointsCondition(Comparison.AtLeast, 2),
        };
    }

    /// <summary>Who, a comparison, and a percentage (0% to 100% by 1%; a value from a file is kept as it is). At most 0%: just used.</summary>
    private (ComboBox Who, ComboBox Compare, ComboBox Value) ChargePickers(Panel row, string operatorId, Comparison comparison, int percent)
    {
        var who = OperatorPicker(operatorId);
        var compare = ComparePicker(comparison);
        var percents = Enumerable.Range(0, 101).ToList();
        if (!percents.Contains(percent)) percents = [.. percents.Append(percent).Order()];
        var value = Picker(percents, v => $"{v}%", percent);
        row.Children.Add(who);
        row.Children.Add(compare);
        row.Children.Add(value);
        return (who, compare, value);
    }

    private static string KindOf(StateCondition c) => c switch
    {
        ComboCondition => "Combo",
        UltimateCondition => "Ultimate",
        UltimateChargeCondition => "UltimateCharge",
        UltimateActiveCondition => "UltimateActive",
        ComboChargeCondition => "ComboCharge",
        BattleSkillCondition => "BattleSkill",
        CounterCondition => "Counter",
        BossStatusCondition => "BossStatus",
        ControllingCondition => "Controlling",
        PhaseShownCondition => "PhaseShown",
        AllCondition or AnyCondition => "Group",
        _ => "SkillPoints",
    };

    private void BuildConditionRows()
    {
        ConditionRows.Children.Clear();
        if (_step is not null) AddConditionRows(ConditionRows, _step.Expect, StepConditionsChanged);
    }

    private void BuildGateRows()
    {
        GateRows.Children.Clear();
        if (_phase is not null) AddConditionRows(GateRows, _phase.When, GateConditionsChanged);
    }

    /// <param name="rebuild">The list changed shape (added, removed, another kind): draw the rows again.</param>
    private void StepConditionsChanged(bool rebuild)
    {
        MarkDirty();
        if (rebuild) BuildConditionRows();
        RefreshStepText();
    }

    private void GateConditionsChanged(bool rebuild)
    {
        MarkDirty();
        if (rebuild) BuildGateRows();
        RefreshPhaseList();
        Preview();
    }

    private void AddConditionRows(Panel into, List<StateCondition> list, Action<bool> changed)
    {
        for (int i = 0; i < list.Count; i++) into.Children.Add(ConditionRow(list, i, changed));
    }

    /// <summary>
    /// One condition as pickers: its kind, then what it needs, then a remove button. A group
    /// ("all of" / "any of") shows its conditions indented below it, with its own "add" button.
    /// </summary>
    private UIElement ConditionRow(List<StateCondition> list, int index, Action<bool> changed)
    {
        // A "not" is shown as its condition with 非 ticked.
        var stored = list[index];
        var condition = stored is NotCondition not ? not.Of : stored;
        var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        var negate = new CheckBox
        {
            Content = Loc.Get("Editor.Not"),
            ToolTip = Loc.Get("Editor.NotHint"),
            IsChecked = stored is NotCondition,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        };
        StateCondition Wrap(StateCondition c) => negate.IsChecked == true ? new NotCondition(c) : c;
        row.Children.Add(negate);
        var kind = new ComboBox();
        foreach (string k in ConditionKinds) kind.Items.Add(Item(Loc.Get($"Editor.Condition.{k}"), k));
        kind.SelectedItem = kind.Items.Cast<ComboBoxItem>().First(i => (string)i.Tag == KindOf(condition));
        row.Children.Add(kind);

        UIElement result = row;
        ComboBox? groupMode = null;
        Func<StateCondition> read;
        // A grey line under the row, for conditions that need a word of explanation.
        string? hint = null;
        switch (condition)
        {
            case AllCondition or AnyCondition:
            {
                var parts = new List<StateCondition>(condition is AllCondition all ? all.Of : ((AnyCondition)condition).Of);
                groupMode = Picker([false, true], isAll => Loc.Get(isAll ? "Editor.Group.All" : "Editor.Group.Any"), condition is AllCondition);
                row.Children.Add(groupMode);
                var mode = groupMode;
                read = () => (bool)Picked(mode) ? new AllCondition([.. parts]) : new AnyCondition([.. parts]);

                // Its conditions, indented under a line, with their own "add".
                var inner = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
                void PartsChanged(bool rebuild)
                {
                    list[index] = Wrap(read());
                    changed(rebuild);
                }
                AddConditionRows(inner, parts, PartsChanged);
                var add = new Button { Content = Loc.Get("Editor.AddToGroup"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 6) };
                add.Click += (_, _) =>
                {
                    parts.Add(DefaultCondition("Ultimate"));
                    PartsChanged(rebuild: true);
                };
                inner.Children.Add(add);
                mode.SelectionChanged += (_, _) =>
                {
                    list[index] = Wrap(read());
                    changed(false);
                };
                result = new StackPanel
                {
                    Children =
                    {
                        row,
                        new Border
                        {
                            BorderBrush = System.Windows.Media.Brushes.LightGray,
                            BorderThickness = new Thickness(2, 0, 0, 0),
                            Margin = new Thickness(8, 0, 0, 4),
                            Child = inner,
                        },
                    },
                };
                break;
            }
            case SkillPointsCondition c:
            {
                var compare = ComparePicker(c.Is);
                // 0.0 to 3.0 by 0.1: the bar is read to about ±0.05 (CombatHudTests), so a tenth is meaningful.
                // A finer value from a file (1.85) is kept, in its place in the list.
                var points = Enumerable.Range(0, 31).Select(i => Math.Round(i * 0.1, 1)).ToList();
                if (!points.Contains(c.Value)) points = [.. points.Append(c.Value).Order()];
                var value = Picker(points, v => v.ToString("0.0#", CultureInfo.InvariantCulture), c.Value);
                row.Children.Add(compare);
                row.Children.Add(value);
                read = () => new SkillPointsCondition((Comparison)Picked(compare), (double)Picked(value));
                break;
            }
            case ComboCondition c:
            {
                var who = OperatorPicker(c.OperatorId);
                var state = Picker([true, false], r => Loc.Get(r ? "Editor.Ready" : "Editor.Used"), c.Ready);
                row.Children.Add(who);
                row.Children.Add(state);
                read = () => new ComboCondition((string)Picked(who), (bool)Picked(state));
                break;
            }
            case UltimateCondition c:
            {
                var who = OperatorPicker(c.OperatorId);
                var state = Picker([true, false], r => Loc.Get(r ? "Editor.Ready" : "Editor.Used"), c.Ready);
                row.Children.Add(who);
                row.Children.Add(state);
                read = () => new UltimateCondition((string)Picked(who), (bool)Picked(state));
                break;
            }
            case UltimateActiveCondition c:
            {
                var who = OperatorPicker(c.OperatorId);
                var state = Picker([true, false], a => Loc.Get(a ? "Editor.Active" : "Editor.NotActive"), c.Active);
                row.Children.Add(who);
                row.Children.Add(state);
                read = () => new UltimateActiveCondition((string)Picked(who), (bool)Picked(state));
                break;
            }
            case UltimateChargeCondition c:
            {
                var (who, compare, value) = ChargePickers(row, c.OperatorId, c.Is, c.Percent);
                read = () => new UltimateChargeCondition((string)Picked(who), (Comparison)Picked(compare), (int)Picked(value));
                hint = Loc.Get("Editor.ChargeHint");
                break;
            }
            case ComboChargeCondition c:
            {
                var (who, compare, value) = ChargePickers(row, c.OperatorId, c.Is, c.Percent);
                read = () => new ComboChargeCondition((string)Picked(who), (Comparison)Picked(compare), (int)Picked(value));
                hint = Loc.Get("Editor.ChargeHint");
                break;
            }
            case BattleSkillCondition c:
            {
                var who = OperatorPicker(c.OperatorId);
                var state = Picker([true, false], r => Loc.Get(r ? "Editor.Ready" : "Editor.Cooling"), c.Ready);
                row.Children.Add(who);
                row.Children.Add(state);
                read = () => new BattleSkillCondition((string)Picked(who), (bool)Picked(state));
                break;
            }
            case CounterCondition c:
            {
                var counters = Counters().ToList();
                if (counters.Count == 0)
                {
                    row.Children.Add(new TextBlock { Text = Loc.Get("Editor.NoCounters"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
                    read = () => c;
                    break;
                }
                var which = Picker(counters, d => $"{OperatorName(d.OperatorId)} · {d.NameZh}",
                    counters.FirstOrDefault(d => d.OperatorId == c.OperatorId && d.Id == c.CounterId) ?? counters[0]);
                var compare = ComparePicker(c.Is);
                int max = ((CounterDefinition)Picked(which)).Max;
                var value = Picker(Enumerable.Range(0, max + 1).ToList(), v => v.ToString(), Math.Clamp(c.Value, 0, max));
                row.Children.Add(which);
                row.Children.Add(compare);
                row.Children.Add(value);
                read = () =>
                {
                    var d = (CounterDefinition)Picked(which);
                    return new CounterCondition(d.OperatorId, d.Id, (Comparison)Picked(compare), Math.Min((int)Picked(value), d.Max));
                };
                break;
            }
            case BossStatusCondition c:
            {
                var present = Picker([true, false], p => Loc.Get(p ? "Editor.Has" : "Editor.HasNot"), c.Present);
                var statuses = _library.Statuses.ToList();
                // A status the library doesn't list (not downloaded yet, or from a shared rotation) stays as it is.
                if (c.StatusId.Length > 0 && !statuses.Any(s => s.Id == c.StatusId)) statuses.Insert(0, new StatusInfo(c.StatusId, ""));
                if (statuses.Count == 0)
                {
                    row.Children.Add(new TextBlock { Text = Loc.Get("Editor.NoStatuses"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
                    read = () => c;
                    break;
                }
                var status = Picker(statuses, s => s.NameZh, statuses.FirstOrDefault(s => s.Id == c.StatusId) ?? statuses[0]);
                row.Children.Add(present);
                row.Children.Add(status);
                // Stack badges aren't read yet, so stacks aren't offered; an imported value is kept as it is.
                read = () => new BossStatusCondition(((StatusInfo)Picked(status)).Id, (bool)Picked(present), c.MinStacks);
                break;
            }
            case ControllingCondition c:
            {
                var who = OperatorPicker(c.OperatorId);
                row.Children.Add(who);
                read = () => new ControllingCondition((string)Picked(who));
                break;
            }
            case PhaseShownCondition c:
            {
                // The rotation's phases by the strip's own names ("充能轴 II"), never ids; a deleted one stays listed as missing.
                var rotation = _draft!.ToRotation();
                var phases = rotation.Phases.Select((p, i) => (p.Id, Label: rotation.PhaseLabel(i))).ToList();
                if (!phases.Any(p => p.Id == c.PhaseId)) phases.Insert(0, (c.PhaseId, Loc.Get("Editor.PhaseMissing")));
                var which = Picker(phases, p => p.Label, phases.First(p => p.Id == c.PhaseId));
                var compare = ComparePicker(c.Is);
                var times = Picker(Enumerable.Range(0, 10).ToList(), n => Loc.Format("Editor.Times", n), c.Times);
                row.Children.Add(which);
                row.Children.Add(compare);
                row.Children.Add(times);
                read = () => new PhaseShownCondition((((string Id, string Label))Picked(which)).Id, (Comparison)Picked(compare), (int)Picked(times));
                hint = Loc.Get("Editor.PhaseShownHint");
                kind.ToolTip = hint;
                break;
            }
            default:
                read = () => condition;
                break;
        }

        var remove = new Button { Content = "✕", ToolTip = Loc.Get("Editor.RemoveCondition") };
        remove.Click += (_, _) =>
        {
            list.RemoveAt(index);
            changed(true);
        };
        row.Children.Add(remove);
        if (hint is not null)
        {
            row.Margin = new Thickness(0);
            result = new StackPanel
            {
                Margin = new Thickness(0, 0, 0, 6),
                Children = { row, new TextBlock { Text = hint, Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x88, 0x88, 0x88)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) } },
            };
        }

        kind.SelectionChanged += (_, _) =>
        {
            if ((string)Picked(kind) == KindOf(condition)) return;
            list[index] = Wrap(DefaultCondition((string)Picked(kind)));
            changed(true);
        };
        negate.Checked += (_, _) => NegateChanged();
        negate.Unchecked += (_, _) => NegateChanged();
        void NegateChanged()
        {
            list[index] = Wrap(read());
            changed(false);
        }
        foreach (var picker in row.Children.OfType<ComboBox>().Where(p => p != kind && p != groupMode))
        {
            picker.SelectionChanged += (_, _) =>
            {
                list[index] = Wrap(read());
                changed(read() is CounterCondition); // the value range follows the counter
            };
        }
        return result;
    }

    private void RefreshStepText()
    {
        if (_phase is null || _step is null) return;
        int index = _phase.Steps.IndexOf(_step);
        _loading = true;
        if (index >= 0 && index < StepList.Items.Count) ((ComboBoxItem)StepList.Items[index]).Content = StepText(index, _step);
        _loading = false;
        Preview();
    }

    private ComboBox ComparePicker(Comparison selected) =>
        Picker([Comparison.AtLeast, Comparison.AtMost, Comparison.Exactly, Comparison.Below, Comparison.Above], c => Loc.Get($"Editor.Compare.{c}"), selected);

    private ComboBox OperatorPicker(string selected)
    {
        var picker = new ComboBox { MinWidth = 120 };
        FillOperators(picker, SquadOperators());
        Select(picker, selected);
        if (picker.SelectedIndex < 0 && picker.Items.Count > 0) picker.SelectedIndex = 0;
        return picker;
    }

    private static ComboBox Picker<T>(IReadOnlyList<T> values, Func<T, string> text, T? selected)
    {
        var picker = new ComboBox { MinWidth = 64 };
        foreach (var value in values) picker.Items.Add(Item(text(value), value!));
        // A value from a file that isn't in the list is added and kept, never swapped for the first item
        // (a 技力 1.8 used to save as 0.5).
        if (selected is not null && !values.Any(v => Equals(v, selected)))
            picker.Items.Add(Item(text(selected), selected));
        picker.SelectedItem = picker.Items.Cast<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, selected)) ?? picker.Items.Cast<ComboBoxItem>().FirstOrDefault();
        return picker;
    }

    private static object Picked(ComboBox picker) => ((ComboBoxItem)picker.SelectedItem).Tag;

    private IEnumerable<CounterDefinition> Counters() =>
        OperatorCounters.Definitions.Where(d => _draft?.Squad.Contains(d.OperatorId) == true);

    // ---- Preview ----

    private void Preview()
    {
        if (_phase is null || _phase.Steps.Count == 0)
        {
            PreviewHost.Content = new TextBlock { Text = Loc.Get("Editor.PreviewEmpty"), Foreground = System.Windows.Media.Brushes.White };
            return;
        }
        if (_draft is null) return;
        // Built as the game's strip is, so it shows the same: this phase, then where the flow goes next.
        PreviewHost.Content = RotationStrip.Build(RotationStripView.ForPhase(_draft.ToRotation(), _draft.Phases.IndexOf(_phase), _library, SkillIcon, Avatar));
    }

    private BitmapSource? Avatar(OperatorInfo op)
    {
        string key = "avatar:" + op.Id;
        if (_icons.TryGetValue(key, out var cached)) return cached;
        return _icons[key] = _library.FindAvatar(op.Id) is { } frame ? ComboBanner.ToBitmap(frame) : null;
    }

    private BitmapSource? SkillIcon(OperatorInfo op, StepAction action)
    {
        if (action == StepAction.BasicAttack) return null; // no skill icon: never borrow the battle skill's
        var kind = action switch { StepAction.Combo => SkillKind.Combo, StepAction.Ultimate => SkillKind.Ultimate, _ => SkillKind.BattleSkill };
        var icon = op.Skills.FirstOrDefault(s => s.Kind == kind);
        if (icon is null) return null;
        if (_icons.TryGetValue(icon.File, out var cached)) return cached;
        _icons[icon.File] = null;
        _ = LoadAsync();
        return null;

        async Task LoadAsync()
        {
            try
            {
                _icons[icon.File] = ComboBanner.ToBitmap(await _library.LoadSkillIconAsync(icon));
                Preview();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                DiagnosticLog.Warn($"couldn't load icon {icon.File}: {ex.Message}");
            }
        }
    }

    // ---- Save, import, export ----

    private bool Problems()
    {
        var problems = _draft?.Problems() ?? [];
        ProblemsText.Text = string.Join("\n", problems);
        return problems.Count == 0;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e) => Save();

    private bool Save()
    {
        if (_draft is null) return true;
        if (!Problems()) return false;
        var rotation = _draft.ToRotation();
        // A recommended rotation isn't changed in place: the player gets their own copy, and the original keeps updating.
        bool copied = IsPublished(rotation.Id);
        if (copied) rotation = RotationStore.AsCopy(rotation, Loc.Format("Editor.OwnCopyName", rotation.Name));
        _store.Own.Save(rotation);
        _dirty = false;
        _changed(rotation);
        LoadRotations(rotation.Id);
        Status(Loc.Get(copied ? "Editor.SavedAsOwn" : "Editor.Saved"));
        return true;
    }

    private void ImportFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = Loc.Get("Editor.FileFilter"), Title = Loc.Get("Editor.ImportFile") };
        if (dialog.ShowDialog(this) != true) return;
        Import(() => RotationFormat.FromJson(File.ReadAllText(dialog.FileName)));
    }

    private void PasteCode_Click(object sender, RoutedEventArgs e)
    {
        string text = Clipboard.ContainsText() ? Clipboard.GetText() : "";
        if (string.IsNullOrWhiteSpace(text))
        {
            Error(Loc.Get("Editor.ClipboardEmpty"));
            return;
        }
        Import(() => RotationFormat.FromShareCode(text));
    }

    private void Import(Func<Rotation> read)
    {
        if (!ConfirmDiscard()) return;
        Rotation rotation;
        try
        {
            rotation = read();
        }
        catch (RotationReadException ex)
        {
            Error(Loc.Get($"Editor.ImportError.{ex.Error}"));
            return;
        }
        catch (IOException ex)
        {
            Error(Loc.Format("Editor.ImportError.File", ex.Message));
            return;
        }

        var unknown = rotation.Squad.Concat(rotation.Phases.SelectMany(p => p.Steps.Select(s => s.OperatorId)))
            .Distinct().Where(id => _library.Find(id) is null).ToList();
        if (unknown.Count > 0)
        {
            Error(Loc.Format("Editor.ImportError.UnknownOperator", string.Join("、", unknown)));
            return;
        }

        // A recommended rotation shared back in becomes the player's own copy; the original stays as it is.
        if (IsPublished(rotation.Id)) rotation = RotationStore.AsCopy(rotation, rotation.Name);
        if (!_store.Own.Import(rotation))
        {
            var answer = MessageBox.Show(this, Loc.Format("Editor.ImportClash", rotation.Name), Title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Cancel) return;
            if (answer == MessageBoxResult.Yes) _store.Own.Save(rotation);
            else _store.Own.Save(rotation = RotationStore.AsCopy(rotation, Loc.Format("Editor.CopyName", rotation.Name)));
        }
        _dirty = false;
        _changed(rotation);
        LoadRotations(rotation.Id);
        Status(Loc.Format("Editor.Imported", rotation.Name));
    }

    private void ExportFile_Click(object sender, RoutedEventArgs e)
    {
        if (_draft is null || !Problems()) return;
        var dialog = new SaveFileDialog
        {
            Filter = Loc.Get("Editor.FileFilter"),
            FileName = string.Concat(_draft.Name.Split(Path.GetInvalidFileNameChars())) + ".endhud-rotation.json",
            Title = Loc.Get("Editor.ExportFile"),
        };
        if (dialog.ShowDialog(this) != true) return;
        File.WriteAllText(dialog.FileName, RotationFormat.ToJson(_draft.ToRotation()));
        Status(Loc.Format("Editor.Exported", Path.GetFileName(dialog.FileName)));
    }

    private void CopyCode_Click(object sender, RoutedEventArgs e)
    {
        if (_draft is null || !Problems()) return;
        Clipboard.SetText(RotationFormat.ToShareCode(_draft.ToRotation()));
        Status(Loc.Get("Editor.CodeCopied"));
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private async void RestorePublished_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscard()) return;
        RestorePublishedButton.IsEnabled = false;
        Status(Loc.Get("Editor.Restoring"));
        try
        {
            var result = await _restorePublished();
            LoadRotations(_draft?.Id);
            if (result is null) Error(Loc.Get("Editor.RestoreFailed"));
            else Status(Loc.Format("Editor.Restored", result.Total));
        }
        finally
        {
            RestorePublishedButton.IsEnabled = true;
        }
    }

    /// <summary>The recommended rotations were updated from GitHub while the editor is open.</summary>
    internal void PublishedChanged()
    {
        if (_dirty)
        {
            Status(Loc.Get("Editor.PublishedUpdatedLater"));
            return;
        }
        LoadRotations(_draft?.Id);
        Status(Loc.Get("Editor.PublishedUpdated"));
    }

    /// <summary>A recommended rotation (and not one of the player's own with the same id).</summary>
    private bool IsPublished(string id) => !_store.Own.Exists(id) && _store.IsPublished(id);

    /// <summary>With unsaved changes: save, discard, or stay. True to go on.</summary>
    private bool ConfirmDiscard()
    {
        if (!_dirty || _draft is null) return true;
        var answer = MessageBox.Show(this, Loc.Get("Editor.Unsaved"), Title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Cancel) return false;
        if (answer == MessageBoxResult.Yes) return Save();
        _dirty = false;
        return true;
    }

    // ---- Helpers ----

    private void MarkDirty()
    {
        _dirty = true;
        StatusText.Text = "";
        Problems();
    }

    private void Status(string text)
    {
        StatusText.Text = text;
        ProblemsText.Text = "";
    }

    private void Error(string text)
    {
        StatusText.Text = "";
        ProblemsText.Text = text;
    }

    private IEnumerable<OperatorInfo> SquadOperators() =>
        (_draft?.Squad ?? []).Where(id => id is not null).Select(id => _library.Find(id!)).Where(o => o is not null).Select(o => o!);

    private string OperatorName(string id) => _library.Find(id)?.NameZh ?? id;

    private void FillOperators(ComboBox picker, IEnumerable<OperatorInfo> operators)
    {
        picker.Items.Clear();
        foreach (var op in operators.OrderBy(o => o.NameZh, StringComparer.Ordinal))
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            if (Avatar(op) is { } avatar) content.Children.Add(new Image { Source = avatar, Width = 20, Height = 20, Margin = new Thickness(0, 0, 6, 0) });
            content.Children.Add(new TextBlock { Text = op.NameZh, VerticalAlignment = VerticalAlignment.Center });
            picker.Items.Add(new ComboBoxItem { Content = content, Tag = op.Id });
        }
    }

    private static void Select(ComboBox picker, string? id) =>
        picker.SelectedItem = picker.Items.Cast<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, id));

    private static ComboBoxItem Item(string text, object tag) => new() { Content = text, Tag = tag };
}
