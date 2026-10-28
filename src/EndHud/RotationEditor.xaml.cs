using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using EndHud.Vision;
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
    private readonly HudLayout _currentLayout;
    private readonly Action<Rotation?> _changed;
    private readonly Dictionary<string, BitmapSource?> _icons = [];
    private IReadOnlyList<LibraryRotation> _rotations = [];
    private RotationDraft? _draft;
    private PhaseDraft? _phase;
    private StepDraft? _step;
    private bool _dirty;
    private bool _loading;
    /// <summary>The condition pickers of a phase's entry condition, its priority rules, a step's expected state: the same kinds, each host its own.</summary>
    private readonly ConditionEditor _gateConditions, _ruleConditions, _stepConditions, _conditionStepConditions, _finishConditions;

    /// <param name="currentSquad">The squad EndHud recognises now (for "use the current squad"), null slots unknown.</param>
    /// <param name="currentLayout">The HUD layout EndHud read last (keyboard or controller): the preview's battle-skill keys are drawn for it.</param>
    /// <param name="select">The rotation to open with, if any.</param>
    /// <param name="restorePublished">Bring back deleted recommended rotations and download them again.</param>
    /// <param name="changed">Called after a rotation is saved (with it) or deleted (with null).</param>
    internal RotationEditor(RotationLibrary store, OperatorLibrary library, IReadOnlyList<string?> currentSquad, HudLayout currentLayout, string? select,
        Func<Task<RotationSyncResult?>> restorePublished, Action<Rotation?> changed)
    {
        _store = store;
        _restorePublished = restorePublished;
        _library = library;
        _currentSquad = currentSquad;
        _currentLayout = currentLayout;
        _changed = changed;
        // Conditions offer the rotation's squad, its counters and its phases (by the strip's names).
        ConditionEditor Conditions(ConditionHostKind host) => new(library, SquadOperators, Counters, PhaseChoices, host, Avatar,
            () => (_draft?.Phases.FirstOrDefault(p => p != _phase) ?? _phase)?.Id);
        _gateConditions = Conditions(ConditionHostKind.PhaseGate);
        _ruleConditions = Conditions(ConditionHostKind.PriorityRule);
        _stepConditions = Conditions(ConditionHostKind.StepExpect);
        _conditionStepConditions = Conditions(ConditionHostKind.ConditionStep);
        // The finish condition offers "the phase's steps lit", picked from the selected phase's own steps.
        _finishConditions = new(library, SquadOperators, Counters, PhaseChoices, ConditionHostKind.PhaseFinish, Avatar,
            () => (_draft?.Phases.FirstOrDefault(p => p != _phase) ?? _phase)?.Id, FinishStepChoices);
        InitializeComponent();
        PriorityError.Text = Loc.Get("Editor.PriorityNotNumber");
        PriorityNeedsGate.Text = Loc.Get("Editor.PriorityNeedsGate");
        PriorityHint.Text = Loc.Get("Editor.PriorityHint");
        PriorityRulesHint.Text = Loc.Get("Editor.PriorityRulesHint");

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
        PhaseNameBox.IsEnabled = AddGateButton.IsEnabled = AddFinishButton.IsEnabled = CopyPhaseButton.IsEnabled = _phase is not null;
        _loading = false;
        BuildGateRows();
        ShowPriority();
        BuildFinishRows();
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
            PhaseList.Items.Add(ListItem(Loc.Format(phases[i].When.Count > 0 ? "Editor.PhaseItemGated" : "Editor.PhaseItem", i + 1, phases[i].Name), phases[i].Id));
        PhaseList.SelectedItem = PhaseList.Items.Cast<ListBoxItem>().FirstOrDefault(i => (string)i.Tag == _phase?.Id);
        _loading = false;
    }

    private void RefreshSteps()
    {
        _loading = true;
        StepList.Items.Clear();
        if (_phase is not null)
        {
            for (int i = 0; i < _phase.Steps.Count; i++) StepList.Items.Add(ListItem(StepText(i, _phase.Steps[i]), i));
            if (_step is not null) StepList.SelectedIndex = _phase.Steps.IndexOf(_step);
        }
        _loading = false;
        ShowStep();
        BuildFinishRows(); // "these steps lit" names the steps as the list does
        Preview();
    }

    private string StepText(int index, StepDraft step)
    {
        // A condition step has no operator: its conditions in words name it.
        if (step.Action == StepAction.Condition)
        {
            string words = $"{index + 1}. {step.Label ?? Loc.Get("Rotation.Action.Condition")} · {ConditionWords.Statements(step.Expect, _library, id => PhaseChoices().FirstOrDefault(p => p.Id == id).Label)}";
            return string.IsNullOrWhiteSpace(step.Note) ? words : $"{words}  · {step.Note}";
        }
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
        FillStepOperators();
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

    /// <summary>
    /// The step's operator picker: the squad, and 当前操控 (whoever is controlled at the time). It is offered whatever
    /// the action, so it can be picked first: picking it on a skill makes the step a basic attack (StepField_Changed).
    /// </summary>
    private void FillStepOperators()
    {
        FillOperators(StepOperator, SquadOperators());
        StepOperator.Items.Add(Item(Loc.Get("Editor.ControlledOperator"), RotationStep.Controlled));
    }

    /// <summary>
    /// A basic or heavy attack isn't detected: say so under the action, and how its phase's condition
    /// decides when it shows. A condition step has no operator: that is hidden, and its conditions are shown as the step
    /// itself. It may be used with the previous step (e.g. the check that goes with a heavy attack), so that stays.
    /// </summary>
    private void ShowBasicAttackHint()
    {
        BasicAttackHint.Visibility = _step?.Action.IsDisplayOnly() == true ? Visibility.Visible : Visibility.Collapsed;
        bool condition = _step?.Action == StepAction.Condition;
        var hidden = condition ? Visibility.Collapsed : Visibility.Visible;
        StepOperatorRow.Visibility = StepOperator.Visibility = hidden;
        ConditionStepHint.Visibility = condition ? Visibility.Visible : Visibility.Collapsed;
        ExpectSection.Visibility = condition ? Visibility.Visible : Visibility.Collapsed;
        ExpectTitle.Text = Loc.Get(condition ? "Editor.ConditionStepExpect" : "Editor.Expect");
        ExpectHintText.Text = Loc.Get(condition ? "Editor.ConditionStepExpectHint" : "Editor.ExpectHint");
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

    // ---- Phases ----

    private void PhaseList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _draft is null || PhaseList.SelectedItem is not ListBoxItem { Tag: string id }) return;
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

    /// <summary>Copy phase: a copy of the selected phase (steps and entry condition), right after it, selected.</summary>
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
        _phase.RenumberFinishSteps(n => n > at ? n + 1 : n); // numbered from 1: the new step is number at + 1
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
        // The two steps swap numbers (from 1).
        _phase.RenumberFinishSteps(n => n == from + 1 ? to + 1 : n == to + 1 ? from + 1 : n);
        if (to == 0) _step.WithPrevious = false; // the first step has nothing before it
        MarkDirty();
        RefreshSteps();
    }

    private void DeleteStep_Click(object sender, RoutedEventArgs e)
    {
        if (_phase is null || _step is null) return;
        int index = _phase.Steps.IndexOf(_step);
        _phase.Steps.RemoveAt(index);
        // Numbered from 1: the deleted step leaves the finish condition, the ones after it move up.
        _phase.RenumberFinishSteps(n => n == index + 1 ? null : n > index + 1 ? n - 1 : n);
        if (index == 0 && _phase.Steps.Count > 0) _phase.Steps[0].WithPrevious = false;
        _step = _phase.Steps.Count == 0 ? null : _phase.Steps[Math.Min(index, _phase.Steps.Count - 1)];
        MarkDirty();
        RefreshSteps();
    }

    private void StepField_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || _step is null) return;
        var was = _step.Action;
        // A condition step has no operator: the operator picker is hidden then, and what it still holds isn't the step's.
        if (was != StepAction.Condition && StepOperator.SelectedItem is ComboBoxItem { Tag: string id }) _step.OperatorId = id;
        if (StepActionPicker.SelectedItem is ComboBoxItem { Tag: StepAction action }) _step.Action = action;
        // 当前操控 picked first, on a skill: it is for attacks, so the step becomes a basic attack.
        if (sender == StepOperator && _step.OperatorId == RotationStep.Controlled && !_step.Action.IsDisplayOnly())
        {
            _step.Action = StepAction.BasicAttack;
            _loading = true;
            StepActionPicker.SelectedItem = StepActionPicker.Items.Cast<ComboBoxItem>().First(i => (StepAction)i.Tag == StepAction.BasicAttack);
            _loading = false;
        }
        if (_step.Action.IsDisplayOnly() != was.IsDisplayOnly())
        {
            // 当前操控 is offered for an attack only: a skill left on it moves to the first squad operator.
            if (!_step.Action.IsDisplayOnly() && _step.OperatorId == RotationStep.Controlled) _step.OperatorId = SquadOperators().FirstOrDefault()?.Id ?? "";
            // An attack never lights: it leaves "these steps lit" (numbered from 1).
            int number = _phase!.Steps.IndexOf(_step) + 1;
            if (_step.Action.IsDisplayOnly()) _phase.RenumberFinishSteps(n => n == number ? null : n);
            _loading = true;
            FillStepOperators();
            Select(StepOperator, _step.OperatorId);
            _loading = false;
        }
        _step.WithPrevious = StepWithPrevious.IsChecked == true;
        _step.Label = StepLabel.Text;
        _step.Note = StepNote.Text;
        if (_step.Action == StepAction.Condition && was != StepAction.Condition)
        {
            // A condition step: no operator, and one condition to start from (the owner's example: an enemy status).
            _step.OperatorId = "";
            _step.Expect = [_conditionStepConditions.Default("BossStatus")];
        }
        else if (_step.Action != StepAction.Condition && was == StepAction.Condition)
        {
            _step.OperatorId = SquadOperators().FirstOrDefault()?.Id ?? "";
            _step.Expect = [];
            _loading = true;
            Select(StepOperator, _step.OperatorId);
            _loading = false;
        }
        ShowBasicAttackHint();
        if (_step.Action == StepAction.Condition || was == StepAction.Condition) BuildConditionRows();
        MarkDirty();
        int index = _phase!.Steps.IndexOf(_step);
        _loading = true;
        if (index >= 0 && index < StepList.Items.Count) ((ListBoxItem)StepList.Items[index]).Content = StepText(index, _step);
        _loading = false;
        BuildFinishRows(); // a step's name, or whether it lights up at all, changed
        Preview();
    }

    // ---- Conditions: a step's expected state, a phase's entry condition ----

    private void AddCondition_Click(object sender, RoutedEventArgs e)
    {
        if (_step is null) return;
        _step.Expect.Add((_step.Action == StepAction.Condition ? _conditionStepConditions : _stepConditions).Default(_step.Action == StepAction.Condition ? "BossStatus" : "SkillPoints"));
        StepConditionsChanged(rebuild: true);
    }

    private void AddGateCondition_Click(object sender, RoutedEventArgs e)
    {
        if (_phase is null) return;
        _phase.When.Add(_gateConditions.Default("Ultimate"));
        GateConditionsChanged(rebuild: true);
    }

    private void BuildConditionRows()
    {
        ConditionRows.Children.Clear();
        if (_step is not null) (_step.Action == StepAction.Condition ? _conditionStepConditions : _stepConditions).AddRows(ConditionRows, _step.Expect, StepConditionsChanged);
    }

    private void BuildGateRows()
    {
        GateRows.Children.Clear();
        if (_phase is not null) _gateConditions.AddRows(GateRows, _phase.When, GateConditionsChanged);
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
        ShowPriority(); // an entry condition added or removed: the priority starts or stops ranking
        Preview();
    }

    // ---- Finish condition: when it holds, the round is played through ----

    private void AddFinishCondition_Click(object sender, RoutedEventArgs e)
    {
        if (_phase is null) return;
        // The owner's own: the phase's steps lit, all of them (the default written out), unless the phase has none that light.
        _phase.Finish.Add(_finishConditions.Default(FinishStepChoices().Count > 0 ? "StepsLit" : "SkillPoints"));
        FinishConditionsChanged(rebuild: true);
    }

    private void BuildFinishRows()
    {
        FinishRows.Children.Clear();
        if (_phase is not null) _finishConditions.AddRows(FinishRows, _phase.Finish, FinishConditionsChanged);
    }

    private void FinishConditionsChanged(bool rebuild)
    {
        MarkDirty();
        if (rebuild) BuildFinishRows();
    }

    /// <summary>The selected phase's steps that light up (not basic, heavy or plunge attacks), by number from 1 and as the step list names them.</summary>
    private IReadOnlyList<(int Number, string Label)> FinishStepChoices() =>
        _phase is null ? [] : _phase.Steps.Select((s, i) => (Step: s, Number: i + 1)).Where(s => !s.Step.Action.IsDisplayOnly())
            .Select(s => (s.Number, s.Step.Action == StepAction.Condition
                ? $"{s.Number}. {s.Step.Label ?? Loc.Get("Rotation.Action.Condition")}"
                : $"{s.Number}. {OperatorName(s.Step.OperatorId)} {s.Step.Label ?? Loc.Get($"Rotation.Action.{s.Step.Action}")}")).ToList();

    // ---- Priority: a whole number, and rules "if this holds, the priority is that" ----

    /// <summary>
    /// The phase's number and rules. Offered where they rank: a phase with an entry condition. The
    /// otherwise phase isn't ranked, so there they're greyed out, saying why, unless one is already set
    /// (so it can be put back; saving refuses it meanwhile).
    /// </summary>
    private void ShowPriority()
    {
        _loading = true;
        PriorityBox.Text = _phase is null ? "" : _phase.PriorityTyped ?? _phase.Priority.ToString(CultureInfo.InvariantCulture);
        _loading = false;
        bool gated = _phase?.When.Count > 0;
        bool set = _phase is not null && (_phase.Priority != 0 || _phase.PriorityTyped is not null || _phase.PriorityRules.Count > 0);
        PriorityBox.IsEnabled = AddPriorityRuleButton.IsEnabled = gated || set;
        PriorityNeedsGate.Visibility = _phase is not null && !gated ? Visibility.Visible : Visibility.Collapsed;
        PriorityError.Visibility = _phase?.PriorityTyped is not null ? Visibility.Visible : Visibility.Collapsed;
        BuildRuleRows();
    }

    private void PriorityBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || _phase is null) return;
        if (WholeNumber(PriorityBox.Text) is { } priority) (_phase.Priority, _phase.PriorityTyped) = (priority, null);
        else _phase.PriorityTyped = PriorityBox.Text;
        PriorityError.Visibility = _phase.PriorityTyped is not null ? Visibility.Visible : Visibility.Collapsed;
        MarkDirty();
    }

    /// <summary>A new rule: one above the phase's number (a rule usually raises it), with a condition to change.</summary>
    private void AddPriorityRule_Click(object sender, RoutedEventArgs e)
    {
        if (_phase is null) return;
        int above = _phase.Priority == int.MaxValue ? int.MaxValue : _phase.Priority + 1;
        _phase.PriorityRules.Add(new PriorityRuleDraft { When = [_ruleConditions.Default("Ultimate")], Priority = above });
        RulesChanged(rebuild: true);
    }

    private void BuildRuleRows()
    {
        PriorityRuleRows.Children.Clear();
        if (_phase is null) return;
        for (int i = 0; i < _phase.PriorityRules.Count; i++) PriorityRuleRows.Children.Add(RuleRow(_phase.PriorityRules, i));
    }

    /// <summary>
    /// One rule: "rule 2: when all of these hold, the priority is [n]", its ↑ ↓ and remove, then its
    /// conditions as rows of pickers (the same as an entry condition's) and their "add", under a line.
    /// </summary>
    private FrameworkElement RuleRow(List<PriorityRuleDraft> rules, int index)
    {
        var rule = rules[index];
        var number = new TextBox
        {
            Width = 60,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
            Text = rule.PriorityTyped ?? rule.Priority.ToString(CultureInfo.InvariantCulture),
        };
        var error = new TextBlock
        {
            Text = Loc.Get("Editor.PriorityNotNumber"),
            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xB0, 0x40, 0x30)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4),
            Visibility = rule.PriorityTyped is not null ? Visibility.Visible : Visibility.Collapsed,
        };
        number.TextChanged += (_, _) =>
        {
            if (WholeNumber(number.Text) is { } priority) (rule.Priority, rule.PriorityTyped) = (priority, null);
            else rule.PriorityTyped = number.Text;
            error.Visibility = rule.PriorityTyped is not null ? Visibility.Visible : Visibility.Collapsed;
            MarkDirty();
        };

        var header = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        header.Children.Add(new TextBlock
        {
            Text = Loc.Format("Editor.PriorityRuleThen", index + 1),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        });
        header.Children.Add(number);
        header.Children.Add(RuleButton("↑", "Up", Loc.Get("Editor.MoveUp"), () => MoveRule(rules, index, -1)));
        header.Children.Add(RuleButton("↓", "Down", Loc.Get("Editor.MoveDown"), () => MoveRule(rules, index, 1)));
        header.Children.Add(RuleButton(Loc.Get("Editor.RemovePriorityRule"), "Remove", null, () =>
        {
            rules.RemoveAt(index);
            RulesChanged(rebuild: true);
        }));

        var conditions = new StackPanel { Tag = "Conditions" };
        _ruleConditions.AddRows(conditions, rule.When, RulesChanged);
        var add = RuleButton(Loc.Get("Editor.AddCondition"), "AddCondition", null, () =>
        {
            rule.When.Add(_ruleConditions.Default("Ultimate"));
            RulesChanged(rebuild: true);
        });
        add.HorizontalAlignment = HorizontalAlignment.Left;
        add.Margin = new Thickness(0, 0, 0, 2);

        return new Border
        {
            BorderBrush = System.Windows.Media.Brushes.LightGray,
            BorderThickness = new Thickness(2, 0, 0, 0),
            Padding = new Thickness(6, 0, 0, 0),
            Margin = new Thickness(0, 0, 0, 8),
            Child = new StackPanel { Children = { header, error, conditions, add } },
        };
    }

    private static Button RuleButton(string content, string tag, string? toolTip, Action click)
    {
        var button = new Button { Content = content, Tag = tag, ToolTip = toolTip, Margin = new Thickness(0, 0, 6, 0) };
        button.Click += (_, _) => click();
        return button;
    }

    private void MoveRule(List<PriorityRuleDraft> rules, int index, int by)
    {
        int to = index + by;
        if (to < 0 || to >= rules.Count) return;
        (rules[index], rules[to]) = (rules[to], rules[index]);
        RulesChanged(rebuild: true);
    }

    /// <param name="rebuild">The rules changed shape (added, removed, moved, a condition of another kind): draw them again.</param>
    private void RulesChanged(bool rebuild)
    {
        MarkDirty();
        if (rebuild) BuildRuleRows();
    }

    /// <summary>
    /// A whole number as typed, or null when it isn't one (a word, a fraction, too big). Full-width
    /// digits and signs, which a Chinese input method types, count as the plain ones.
    /// </summary>
    internal static int? WholeNumber(string text)
    {
        var plain = new System.Text.StringBuilder(text.Length);
        foreach (char c in text)
        {
            plain.Append(c switch
            {
                >= '０' and <= '９' => (char)('0' + (c - '０')),
                '－' or '−' => '-',
                '＋' => '+',
                _ => c,
            });
        }
        return int.TryParse(plain.ToString().Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value) ? value : null;
    }

    private void RefreshStepText()
    {
        if (_phase is null || _step is null) return;
        int index = _phase.Steps.IndexOf(_step);
        _loading = true;
        if (index >= 0 && index < StepList.Items.Count) ((ListBoxItem)StepList.Items[index]).Content = StepText(index, _step);
        _loading = false;
        Preview();
    }

    private IEnumerable<CounterDefinition> Counters() =>
        OperatorCounters.Definitions.Where(d => _draft?.Squad.Contains(d.OperatorId) == true);

    /// <summary>The rotation's phases by the strip's own names (name and Roman numeral), for "phase shown" conditions.</summary>
    private IReadOnlyList<(string Id, string Label)> PhaseChoices()
    {
        if (_draft is null) return [];
        var rotation = _draft.ToRotation();
        return rotation.Phases.Select((p, i) => (p.Id, rotation.PhaseLabel(i))).ToList();
    }

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
        // The battle skills' keys only while the squad recognised now is this rotation's, by its slots in the game.
        var rotation = _draft.ToRotation();
        PreviewHost.Content = RotationStrip.Build(RotationStripView.ForPhase(rotation, _draft.Phases.IndexOf(_phase), _library, SkillIcon, Avatar, statusIcon: StatusIcon,
            keys: StripKeys.ForPreview(rotation, _currentSquad, _currentLayout)));
    }

    private BitmapSource? Avatar(OperatorInfo op)
    {
        string key = "avatar:" + op.Id;
        if (_icons.TryGetValue(key, out var cached)) return cached;
        return _icons[key] = _library.FindAvatar(op.Id) is { } frame ? ComboBanner.ToBitmap(frame) : null;
    }

    private BitmapSource? StatusIcon(string id)
    {
        string key = "status:" + id;
        if (_icons.TryGetValue(key, out var cached)) return cached;
        return _icons[key] = _library.StatusIcon(id) is { } frame ? ComboBanner.ToBitmap(frame) : null;
    }

    private BitmapSource? SkillIcon(OperatorInfo op, StepAction action)
    {
        if (action.IsDisplayOnly()) return null; // no skill icon: never borrow the battle skill's
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
        ImportCode(text);
    }

    internal void ImportCode(string code) => Import(() => RotationFormat.FromShareCode(code));

    internal void ImportJson(string json) => Import(() => RotationFormat.FromJson(json));

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

        var unknown = rotation.NamedOperators().Where(id => _library.Find(id) is null).ToList();
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

    /// <summary>What "export to file" writes: the rotation shown, as JSON. Null when there is none, or when it has a problem (then said).</summary>
    internal string? ExportJson() => _draft is null || !Problems() ? null : RotationFormat.ToJson(_draft.ToRotation());

    /// <summary>What "copy share code" puts on the clipboard. Null when there is no rotation, or when it has a problem (then said).</summary>
    internal string? ExportCode() => _draft is null || !Problems() ? null : RotationFormat.ToShareCode(_draft.ToRotation());

    private void ExportFile_Click(object sender, RoutedEventArgs e)
    {
        if (_draft is null || ExportJson() is not { } json) return;
        var dialog = new SaveFileDialog
        {
            Filter = Loc.Get("Editor.FileFilter"),
            FileName = string.Concat(_draft.Name.Split(Path.GetInvalidFileNameChars())) + ".endhud-rotation.json",
            Title = Loc.Get("Editor.ExportFile"),
        };
        if (dialog.ShowDialog(this) != true) return;
        File.WriteAllText(dialog.FileName, json);
        Status(Loc.Format("Editor.Exported", Path.GetFileName(dialog.FileName)));
    }

    private void CopyCode_Click(object sender, RoutedEventArgs e)
    {
        if (ExportCode() is not { } code) return;
        Clipboard.SetText(code);
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

    private string OperatorName(string id) => id == RotationStep.Controlled ? Loc.Get("Editor.ControlledOperator") : _library.Find(id)?.NameZh ?? id;

    private void FillOperators(ComboBox picker, IEnumerable<OperatorInfo> operators) => ConditionEditor.FillOperators(picker, operators, Avatar);

    private static void Select(ComboBox picker, string? id) => ConditionEditor.Select(picker, id);

    private static ComboBoxItem Item(string text, object tag) => ConditionEditor.Item(text, tag);

    /// <summary>
    /// An entry of the phase or step list. Not a drop-down's item: a list selects those only on a right
    /// click (their left button is kept for a drop-down that isn't there).
    /// </summary>
    private static ListBoxItem ListItem(string text, object tag) => new() { Content = text, Tag = tag };
}
