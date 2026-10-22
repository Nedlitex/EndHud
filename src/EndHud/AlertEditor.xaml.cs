using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using EndHud.Vision.Alerts;
using EndHud.Vision.Operators;
using EndHud.Vision.Rotations;
using Microsoft.Win32;

namespace EndHud;

/// <summary>
/// The alert rules editor: one list of rules for every fight. A rule's conditions are picked
/// with the rotation editor's pickers (<see cref="ConditionEditor"/>); only its text is typed. Every
/// way the store changes is here: new, copy, correct (edit and save: same id, same place), delete
/// (asks first), and share codes and files in and out (imports never replace).
/// </summary>
public partial class AlertEditor : Window
{
    /// <summary>Every kind a rotation offers, except "phase shown": an alert has no rotation.</summary>
    internal static readonly string[] AlertKinds = [.. ConditionEditor.AllKinds.Where(k => k != "PhaseShown")];

    private readonly AlertStore _store;
    private readonly OperatorLibrary _library;
    private readonly Action _saved;
    private readonly ConditionEditor _conditions;
    private readonly Dictionary<string, BitmapSource?> _avatars = [];
    private readonly List<RuleDraft> _rules = [];
    private RuleDraft? _rule;
    private bool _dirty;
    private bool _loading;
    private bool _canSave = true;

    /// <summary>A rule being edited: its id stays, its conditions and text change in place.</summary>
    private sealed class RuleDraft(string id, IEnumerable<StateCondition> when, string message)
    {
        public string Id { get; } = id;
        public List<StateCondition> When { get; } = [.. when];
        public string Message { get; set; } = message;

        public static RuleDraft From(AlertRule rule) => new(rule.Id, rule.When, rule.Message);

        public AlertRule ToRule() => new(Id, [.. When], Message.Trim());
    }

    /// <param name="store">The rules' file.</param>
    /// <param name="saved">Called after the rules were saved (the overlay reloads them).</param>
    internal AlertEditor(AlertStore store, OperatorLibrary library, Action saved)
    {
        _store = store;
        _library = library;
        _saved = saved;
        // Alerts are global: every operator in the library, every operator resource, no phases.
        _conditions = new ConditionEditor(library, () => _library.Operators, () => OperatorCounters.Definitions, () => [], AlertKinds, Avatar);
        InitializeComponent();
        Load();
        Closing += (_, e) => e.Cancel = !ConfirmDiscard();
    }

    // ---- The list ----

    private void Load()
    {
        var load = _store.Load();
        _rules.Clear();
        _rules.AddRange(load.Rules.Select(RuleDraft.From));
        // Never lost, never overwritten: say what happened to the file, and refuse to save over one we can't read.
        _canSave = !load.Newer && load.Failed is null;
        string? said = (load.RenamedUnreadable ?? _store.RenamedEarlier) is { } renamed ?Loc.Format("Alerts.Unreadable", Path.GetFileName(renamed))
            : load.Newer ? Loc.Get("Alerts.Newer")
            : load.Failed is { } why ? Loc.Format("Alerts.OpenFailed", why)
            : null;
        StoreText.Text = said ?? "";
        StoreText.Visibility = said is null ? Visibility.Collapsed : Visibility.Visible;
        SaveButton.IsEnabled = _canSave;
        _dirty = false;
        Show(_rules.FirstOrDefault());
    }

    /// <summary>The words a rule is listed (and named in messages) by: its text, or its conditions and "(no text)".</summary>
    private string RuleName(RuleDraft rule) =>
        rule.Message.Trim().Length > 0 ? rule.Message.Trim()
        : rule.When.Count == 0 ? Loc.Format("Alerts.Numbered", _rules.IndexOf(rule) + 1)
        : Loc.Format("Alerts.NoText", AlertText.Describe(rule.When, _library));

    /// <summary>A rule's line in the list: one line, cut with "…" when long (the whole text is its tooltip).</summary>
    private TextBlock ListText(RuleDraft rule) => new() { Text = RuleName(rule), TextTrimming = TextTrimming.CharacterEllipsis };

    private void RefreshList()
    {
        _loading = true;
        RuleList.Items.Clear();
        foreach (var rule in _rules) RuleList.Items.Add(new ComboBoxItem { Content = ListText(rule), ToolTip = RuleName(rule), Tag = rule.Id });
        RuleList.SelectedItem = RuleList.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == _rule?.Id);
        _loading = false;
        EmptyText.Visibility = _rules.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CopyAllCodeButton.IsEnabled = ExportFileButton.IsEnabled = _rules.Count > 0;
    }

    private void Show(RuleDraft? rule)
    {
        _rule = rule;
        RefreshList();
        bool has = rule is not null;
        RuleDetails.IsEnabled = CopyButton.IsEnabled = DeleteButton.IsEnabled = CopySelectedCodeButton.IsEnabled = has;
        _loading = true;
        MessageTextBox.Text = rule?.Message ?? "";
        _loading = false;
        BuildConditionRows();
    }

    private void RuleList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || RuleList.SelectedItem is not ComboBoxItem { Tag: string id }) return;
        Show(_rules.FirstOrDefault(r => r.Id == id));
    }

    private void NewButton_Click(object sender, RoutedEventArgs e)
    {
        if (!HasRoom()) return;
        // A first condition to change: skill points at least 2.0.
        var rule = new RuleDraft(AlertRule.NewId(), [_conditions.Default("SkillPoints")], "");
        _rules.Add(rule);
        MarkDirty();
        Show(rule);
    }

    /// <summary>Copy: the selected rule again, right after it, under a new id.</summary>
    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_rule is null || !HasRoom()) return;
        var copy = new RuleDraft(AlertRule.NewId(), _rule.When, _rule.Message);
        _rules.Insert(_rules.IndexOf(_rule) + 1, copy);
        MarkDirty();
        Show(copy);
    }

    private bool HasRoom()
    {
        if (_rules.Count < AlertFile.MaxRules) return true;
        Error(Loc.Get("Alerts.Full"));
        return false;
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e) => DeleteSelected(confirm: true);

    /// <summary>Delete the selected rule, asking first when <paramref name="confirm"/>; saved at once.</summary>
    internal void DeleteSelected(bool confirm)
    {
        if (_rule is null) return;
        if (confirm && MessageBox.Show(this, Loc.Format("Alerts.ConfirmDelete", RuleName(_rule)), Loc.Get("Alerts.Title"),
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        int at = _rules.IndexOf(_rule);
        _rules.Remove(_rule);
        _rule = _rules.Count == 0 ? null : _rules[Math.Min(at, _rules.Count - 1)];
        MarkDirty();
        if (Save()) Status(Loc.Get("Alerts.Deleted"));
        Show(_rule);
    }

    // ---- The selected rule ----

    private void MessageTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || _rule is null) return;
        _rule.Message = MessageTextBox.Text;
        MarkDirty();
        RefreshListText();
    }

    private void AddCondition_Click(object sender, RoutedEventArgs e)
    {
        if (_rule is null) return;
        _rule.When.Add(_conditions.Default("BossStatus"));
        ConditionsChanged(rebuild: true);
    }

    private void BuildConditionRows()
    {
        ConditionRows.Children.Clear();
        if (_rule is not null) _conditions.AddRows(ConditionRows, _rule.When, ConditionsChanged);
    }

    private void ConditionsChanged(bool rebuild)
    {
        MarkDirty();
        if (rebuild) BuildConditionRows();
        RefreshListText();
    }

    /// <summary>The selected rule's line in the list follows its text and conditions, without disturbing the editing.</summary>
    private void RefreshListText()
    {
        if (_rule is null) return;
        int index = _rules.IndexOf(_rule);
        _loading = true;
        if (index >= 0 && index < RuleList.Items.Count) ((ComboBoxItem)RuleList.Items[index]).Content = ListText(_rule);
        if (index >= 0 && index < RuleList.Items.Count) ((ComboBoxItem)RuleList.Items[index]).ToolTip = RuleName(_rule);
        _loading = false;
    }

    // ---- Save, import, export ----

    /// <summary>What's wrong, in words (each rule named); empty when it can be saved.</summary>
    private List<string> Problems() =>
        _rules.SelectMany(r => r.ToRule().Problems().Select(p => Loc.Format($"Alerts.Problem.{p}", RuleName(r)))).ToList();

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (Save()) Status(Loc.Get("Alerts.Saved"));
    }

    /// <summary>Writes every rule; refuses, in words, while one has a problem. True when saved.</summary>
    private bool Save()
    {
        if (!_canSave) return false;
        var problems = Problems();
        if (problems.Count > 0)
        {
            Error(string.Join("\n", problems));
            return false;
        }
        try
        {
            _store.Save([.. _rules.Select(r => r.ToRule())]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Error(Loc.Format("Alerts.SaveFailed", ex.Message));
            return false;
        }
        _dirty = false;
        _saved();
        return true;
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

    /// <summary>Adds the rules of a share code (what the paste-code button does with the clipboard's text).</summary>
    internal void ImportCode(string code) => Import(() => AlertFormat.FromShareCode(code));

    private void ImportFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = Loc.Get("Alerts.FileFilter"), Title = Loc.Get("Editor.ImportFile") };
        if (dialog.ShowDialog(this) != true) return;
        Import(() => AlertFormat.FromJson(File.ReadAllText(dialog.FileName)));
    }

    /// <summary>Adds the rules read, never replacing one (new ids, duplicates skipped, up to 100), and saves.</summary>
    private void Import(Func<IReadOnlyList<AlertRule>> read)
    {
        if (!_canSave) return;
        IReadOnlyList<AlertRule> incoming;
        try
        {
            incoming = read();
        }
        catch (AlertReadException ex)
        {
            Error(Loc.Get($"Alerts.ImportError.{ex.Error}"));
            return;
        }
        catch (IOException ex)
        {
            Error(Loc.Format("Editor.ImportError.File", ex.Message));
            return;
        }
        var import = AlertStore.Import([.. _rules.Select(r => r.ToRule())], incoming);
        var added = import.Rules.Skip(_rules.Count).Select(RuleDraft.From).ToList();
        _rules.AddRange(added);
        MarkDirty();
        Show(added.FirstOrDefault() ?? _rule);
        if (!Save()) return;
        var said = new List<string> { Loc.Format("Alerts.Imported", import.Added) };
        if (import.Duplicates > 0) said.Add(Loc.Format("Alerts.ImportDuplicates", import.Duplicates));
        if (import.OverLimit > 0) said.Add(Loc.Format("Alerts.ImportOverLimit", import.OverLimit));
        Status(string.Concat(said));
    }

    private void CopySelectedCode_Click(object sender, RoutedEventArgs e)
    {
        if (_rule is not null) CopyCode([_rule]);
    }

    private void CopyAllCode_Click(object sender, RoutedEventArgs e) => CopyCode(_rules);

    private void CopyCode(IReadOnlyList<RuleDraft> rules)
    {
        if (ShareCode(rules) is { } code)
        {
            Clipboard.SetText(code);
            Status(Loc.Get("Alerts.CodeCopied"));
        }
    }

    /// <summary>The share code of these rules (what the copy buttons put on the clipboard); null, with the problems said, while one has a problem.</summary>
    private string? ShareCode(IReadOnlyList<RuleDraft> rules)
    {
        var problems = Problems();
        if (problems.Count > 0)
        {
            Error(string.Join("\n", problems));
            return null;
        }
        return AlertFormat.ToShareCode(rules.Select(r => r.ToRule()));
    }

    private void ExportFile_Click(object sender, RoutedEventArgs e)
    {
        if (_rules.Count == 0) return;
        if (Problems() is { Count: > 0 } problems)
        {
            Error(string.Join("\n", problems));
            return;
        }
        var dialog = new SaveFileDialog
        {
            Filter = Loc.Get("Alerts.FileFilter"),
            FileName = Loc.Get("Alerts.Title") + ".endhud-alerts.json",
            Title = Loc.Get("Editor.ExportFile"),
        };
        if (dialog.ShowDialog(this) != true) return;
        File.WriteAllText(dialog.FileName, AlertFormat.ToJson(_rules.Select(r => r.ToRule())));
        Status(Loc.Format("Editor.Exported", Path.GetFileName(dialog.FileName)));
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>With unsaved changes: save, discard, or stay. True to go on.</summary>
    private bool ConfirmDiscard()
    {
        if (!_dirty || !_canSave) return true;
        var answer = MessageBox.Show(this, Loc.Get("Alerts.Unsaved"), Loc.Get("Alerts.Title"), MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
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
        ProblemsText.Text = "";
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

    private BitmapSource? Avatar(OperatorInfo op)
    {
        if (_avatars.TryGetValue(op.Id, out var cached)) return cached;
        return _avatars[op.Id] = _library.FindAvatar(op.Id) is { } frame ? ComboBanner.ToBitmap(frame) : null;
    }
}
