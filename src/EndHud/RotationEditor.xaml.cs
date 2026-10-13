using System.Diagnostics;
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
    private readonly RotationStore _store;
    private readonly OperatorLibrary _library;
    private readonly IReadOnlyList<string?> _currentSquad;
    private readonly Action<Rotation?> _changed;
    private readonly Dictionary<string, BitmapSource?> _icons = [];
    private IReadOnlyList<Rotation> _rotations = [];
    private RotationDraft? _draft;
    private PhaseDraft? _phase;
    private StepDraft? _step;
    private bool _dirty;
    private bool _loading;

    /// <param name="currentSquad">The squad EndHud recognises now (for "use the current squad"), null slots unknown.</param>
    /// <param name="select">The rotation to open with, if any.</param>
    /// <param name="changed">Called after a rotation is saved (with it) or deleted (with null).</param>
    internal RotationEditor(RotationStore store, OperatorLibrary library, IReadOnlyList<string?> currentSquad, string? select,
        Action<Rotation?> changed)
    {
        _store = store;
        _library = library;
        _currentSquad = currentSquad;
        _changed = changed;
        InitializeComponent();

        foreach (var action in Enum.GetValues<StepAction>()) StepActionPicker.Items.Add(Item(Loc.Get($"Rotation.Action.{action}"), action));
        for (int times = 1; times <= Rotation.MaxTimes; times++) TimesPicker.Items.Add(Item($"×{times}", times));
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
        foreach (var r in rotations)
            RotationPicker.Items.Add(Item($"{r.Name}（{string.Join("·", r.Squad.Select(OperatorName))}）", r.Id));
        _loading = false;

        UnreadableText.Visibility = unreadable.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UnreadableText.Text = string.Join("\n", unreadable.Select(u =>
            Loc.Format("Editor.Unreadable", Path.GetFileName(u.File), Loc.Get($"Editor.ReadError.{u.Error}"))));

        var selected = rotations.FirstOrDefault(r => r.Id == select) ?? rotations.FirstOrDefault();
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
        if (_rotations.FirstOrDefault(r => r.Id == id) is { } rotation) ShowDraft(RotationDraft.From(rotation));
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
        draft.Sequence.Add(new PhaseUseDraft { PhaseId = phase.Id, Times = 1 });
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

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_draft is null) return;
        if (!_store.Exists(_draft.Id))
        {
            // Never saved: just drop it.
            _dirty = false;
            LoadRotations(null);
            return;
        }
        if (MessageBox.Show(this, Loc.Format("Editor.ConfirmDelete", _draft.Name), Title, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
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
        EmptyText.Visibility = _rotations.Count == 0 && !has ? Visibility.Visible : Visibility.Collapsed;
        Body.IsEnabled = has;
        NameBox.IsEnabled = CopyButton.IsEnabled = DeleteButton.IsEnabled = SaveButton.IsEnabled = has;
        foreach (var picker in SquadPickers) picker.IsEnabled = has;

        _loading = true;
        NameBox.Text = draft?.Name ?? "";
        for (int i = 0; i < 4; i++) Select(SquadPickers[i], draft?.Squad[i]);
        _loading = false;

        _phase = draft?.Phases.FirstOrDefault();
        RefreshPhases();
        RefreshSequence();
        Problems();
        ProblemsText.Text = "";
    }

    private void RefreshSequence(int? select = null)
    {
        _loading = true;
        SequenceList.Items.Clear();
        AddPhasePicker.Items.Clear();
        if (_draft is not null)
        {
            for (int i = 0; i < _draft.Sequence.Count; i++)
            {
                var use = _draft.Sequence[i];
                string name = _draft.Phases.FirstOrDefault(p => p.Id == use.PhaseId)?.Name ?? Loc.Get("Editor.MissingPhase");
                SequenceList.Items.Add(Item($"{i + 1}. {name} ×{use.Times}", i));
            }
            foreach (var phase in _draft.Phases) AddPhasePicker.Items.Add(Item(phase.Name, phase.Id));
            if (AddPhasePicker.Items.Count > 0) AddPhasePicker.SelectedIndex = 0;
            if (select is { } s && s < SequenceList.Items.Count) SequenceList.SelectedIndex = s;
        }
        _loading = false;
        SyncTimes();
    }

    private void RefreshPhases()
    {
        _loading = true;
        PhasePicker.Items.Clear();
        foreach (var phase in _draft?.Phases ?? []) PhasePicker.Items.Add(Item(phase.Name, phase.Id));
        PhasePicker.SelectedItem = PhasePicker.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == _phase?.Id);
        PhaseNameBox.Text = _phase?.Name ?? "";
        PhaseNameBox.IsEnabled = _phase is not null;
        _loading = false;
        StepsGroup.Header = _phase is null ? Loc.Get("Editor.Steps") : Loc.Format("Editor.StepsOf", _phase.Name);
        _step = _phase?.Steps.FirstOrDefault();
        RefreshSteps();
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
        BuildConditionRows();
    }

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

    // ---- The full rotation ----

    private void SequenceList_SelectionChanged(object sender, SelectionChangedEventArgs e) => SyncTimes();

    private void SyncTimes()
    {
        var use = SelectedUse();
        _loading = true;
        TimesPicker.SelectedItem = TimesPicker.Items.Cast<ComboBoxItem>().FirstOrDefault(i => use is not null && (int)i.Tag == use.Times);
        TimesPicker.IsEnabled = use is not null;
        _loading = false;
    }

    private PhaseUseDraft? SelectedUse() =>
        _draft is not null && SequenceList.SelectedIndex is int i and >= 0 && i < _draft.Sequence.Count ? _draft.Sequence[i] : null;

    private void TimesPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || SelectedUse() is not { } use || TimesPicker.SelectedItem is not ComboBoxItem { Tag: int times }) return;
        use.Times = times;
        MarkDirty();
        RefreshSequence(SequenceList.SelectedIndex);
    }

    private void SequenceUp_Click(object sender, RoutedEventArgs e) => MoveUse(-1);
    private void SequenceDown_Click(object sender, RoutedEventArgs e) => MoveUse(1);

    private void MoveUse(int by)
    {
        if (_draft is null || SelectedUse() is not { } use) return;
        int from = _draft.Sequence.IndexOf(use), to = from + by;
        if (to < 0 || to >= _draft.Sequence.Count) return;
        _draft.Sequence.RemoveAt(from);
        _draft.Sequence.Insert(to, use);
        MarkDirty();
        RefreshSequence(to);
    }

    private void SequenceRemove_Click(object sender, RoutedEventArgs e)
    {
        if (_draft is null || SelectedUse() is not { } use) return;
        _draft.Sequence.Remove(use);
        MarkDirty();
        RefreshSequence();
    }

    private void AddToSequence_Click(object sender, RoutedEventArgs e)
    {
        if (_draft is null || AddPhasePicker.SelectedItem is not ComboBoxItem { Tag: string id }) return;
        _draft.Sequence.Add(new PhaseUseDraft { PhaseId = id, Times = 1 });
        MarkDirty();
        RefreshSequence(_draft.Sequence.Count - 1);
    }

    // ---- Phases ----

    private void PhasePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _draft is null || PhasePicker.SelectedItem is not ComboBoxItem { Tag: string id }) return;
        _phase = _draft.Phases.FirstOrDefault(p => p.Id == id);
        RefreshPhases();
    }

    private void PhaseNameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || _phase is null) return;
        _phase.Name = PhaseNameBox.Text;
        MarkDirty();
        StepsGroup.Header = Loc.Format("Editor.StepsOf", _phase.Name);
        // Keep the lists' names in step without disturbing the text box.
        _loading = true;
        foreach (ComboBoxItem item in PhasePicker.Items)
            if ((string)item.Tag == _phase.Id) item.Content = _phase.Name;
        _loading = false;
        RefreshSequence(SequenceList.SelectedIndex);
    }

    private void NewPhase_Click(object sender, RoutedEventArgs e)
    {
        if (_draft is null) return;
        _phase = new PhaseDraft { Name = Loc.Format("Editor.NewPhaseNumbered", _draft.Phases.Count + 1) };
        _draft.Phases.Add(_phase);
        MarkDirty();
        RefreshPhases();
        RefreshSequence();
    }

    private void DeletePhase_Click(object sender, RoutedEventArgs e)
    {
        if (_draft is null || _phase is null) return;
        if (MessageBox.Show(this, Loc.Format("Editor.ConfirmDeletePhase", _phase.Name), Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        _draft.Phases.Remove(_phase);
        _draft.Sequence.RemoveAll(u => u.PhaseId == _phase.Id);
        _phase = _draft.Phases.FirstOrDefault();
        MarkDirty();
        RefreshPhases();
        RefreshSequence();
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
        MarkDirty();
        int index = _phase!.Steps.IndexOf(_step);
        _loading = true;
        if (index >= 0 && index < StepList.Items.Count) ((ComboBoxItem)StepList.Items[index]).Content = StepText(index, _step);
        _loading = false;
        Preview();
    }

    // ---- Expected state ----

    private static readonly string[] ConditionKinds = ["SkillPoints", "Combo", "Ultimate", "Counter", "BossStatus", "Controlling"];

    private void AddCondition_Click(object sender, RoutedEventArgs e)
    {
        if (_step is null) return;
        _step.Expect.Add(DefaultCondition("SkillPoints"));
        MarkDirty();
        BuildConditionRows();
        RefreshStepText();
    }

    private StateCondition DefaultCondition(string kind)
    {
        string first = SquadOperators().FirstOrDefault()?.Id ?? "";
        return kind switch
        {
            "Combo" => new ComboCondition(first, true),
            "Ultimate" => new UltimateCondition(first, true),
            "Counter" => Counters().FirstOrDefault() is { } c ? new CounterCondition(c.OperatorId, c.Id, Comparison.AtLeast, c.Max) : new SkillPointsCondition(Comparison.AtLeast, 2),
            "BossStatus" => new BossStatusCondition(_library.Statuses.FirstOrDefault()?.Id ?? "", true),
            "Controlling" => new ControllingCondition(first),
            _ => new SkillPointsCondition(Comparison.AtLeast, 2),
        };
    }

    private static string KindOf(StateCondition c) => c switch
    {
        ComboCondition => "Combo",
        UltimateCondition => "Ultimate",
        CounterCondition => "Counter",
        BossStatusCondition => "BossStatus",
        ControllingCondition => "Controlling",
        _ => "SkillPoints",
    };

    private void BuildConditionRows()
    {
        ConditionRows.Children.Clear();
        if (_step is null) return;
        for (int i = 0; i < _step.Expect.Count; i++) ConditionRows.Children.Add(ConditionRow(i));
    }

    /// <summary>One condition as pickers: its kind, then what it needs, then a remove button.</summary>
    private UIElement ConditionRow(int index)
    {
        var condition = _step!.Expect[index];
        var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        var kind = new ComboBox();
        foreach (string k in ConditionKinds) kind.Items.Add(Item(Loc.Get($"Editor.Condition.{k}"), k));
        kind.SelectedItem = kind.Items.Cast<ComboBoxItem>().First(i => (string)i.Tag == KindOf(condition));
        row.Children.Add(kind);

        Func<StateCondition> read;
        switch (condition)
        {
            case SkillPointsCondition c:
            {
                var compare = ComparePicker(c.Is);
                var value = Picker([0.5, 1, 1.5, 2, 2.5, 3], v => $"{v:0.#}", c.Value);
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
            default:
                read = () => condition;
                break;
        }

        var remove = new Button { Content = "✕", ToolTip = Loc.Get("Editor.RemoveCondition") };
        remove.Click += (_, _) =>
        {
            _step.Expect.RemoveAt(index);
            MarkDirty();
            BuildConditionRows();
            RefreshStepText();
        };
        row.Children.Add(remove);

        kind.SelectionChanged += (_, _) =>
        {
            if ((string)Picked(kind) == KindOf(condition)) return;
            _step.Expect[index] = DefaultCondition((string)Picked(kind));
            MarkDirty();
            BuildConditionRows();
            RefreshStepText();
        };
        foreach (var picker in row.Children.OfType<ComboBox>().Where(p => p != kind))
        {
            picker.SelectionChanged += (_, _) =>
            {
                _step.Expect[index] = read();
                MarkDirty();
                if (_step.Expect[index] is CounterCondition) BuildConditionRows(); // the value range follows the counter
                Preview();
            };
        }
        return row;
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
        Picker([Comparison.AtLeast, Comparison.AtMost, Comparison.Exactly], c => Loc.Get($"Editor.Compare.{c}"), selected);

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
        var steps = _phase.Steps.Select((s, i) =>
        {
            var op = _library.Find(s.OperatorId);
            return new StripStep(op is null ? null : SkillIcon(op, s.Action), op is null ? null : Avatar(op),
                string.IsNullOrWhiteSpace(s.Label) ? Loc.Get($"Rotation.Action.{s.Action}") : s.Label,
                string.IsNullOrWhiteSpace(s.Note) ? null : s.Note,
                i == 0 ? StripStepState.Ready : StripStepState.Upcoming, i > 0 && s.WithPrevious);
        }).ToList();
        PreviewHost.Content = RotationStrip.Build(new StripView($"{_phase.Name} I", steps, []));
    }

    private BitmapSource? Avatar(OperatorInfo op)
    {
        string key = "avatar:" + op.Id;
        if (_icons.TryGetValue(key, out var cached)) return cached;
        return _icons[key] = _library.FindAvatar(op.Id) is { } frame ? ComboBanner.ToBitmap(frame) : null;
    }

    private BitmapSource? SkillIcon(OperatorInfo op, StepAction action)
    {
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
                Debug.WriteLine($"EndHud: couldn't load {icon.File}: {ex.Message}");
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
        _store.Save(rotation);
        _dirty = false;
        _changed(rotation);
        string id = rotation.Id;
        LoadRotations(id);
        Status(Loc.Get("Editor.Saved"));
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

        if (!_store.Import(rotation))
        {
            var answer = MessageBox.Show(this, Loc.Format("Editor.ImportClash", rotation.Name), Title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Cancel) return;
            if (answer == MessageBoxResult.Yes) _store.Save(rotation);
            else _store.Save(rotation = RotationStore.AsCopy(rotation, Loc.Format("Editor.CopyName", rotation.Name)));
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
