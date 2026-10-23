using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EndHud.Vision;
using EndHud.Vision.Operators;
using EndHud.Vision.Rotations;

namespace EndHud;

/// <summary>
/// Conditions as rows of pickers: the "not" box, the kind, then what it needs, then ✕; groups ("all of" / "any
/// of") shown indented under their row with their own "add". Everything is picked from lists, never
/// typed. Used by the rotation editor (a step's expected state, a phase's entry condition) and the
/// alert editor (a rule's conditions).
/// </summary>
/// <param name="operators">Who the operator pickers offer: a rotation's squad, or every operator for alerts.</param>
/// <param name="counters">The operator resources offered: a rotation squad's, or all of them for alerts.</param>
/// <param name="phases">The phases a "phase shown" condition can name, by the strip's own names (none for alerts).</param>
/// <param name="kinds">The kinds offered (a stored condition of another kind is still shown, as itself).</param>
/// <param name="avatar">An operator's avatar for the pickers, if there is one.</param>
/// <param name="defaultPhaseId">The phase a new "phase shown" condition names.</param>
internal sealed class ConditionEditor(
    OperatorLibrary library,
    Func<IEnumerable<OperatorInfo>> operators,
    Func<IEnumerable<CounterDefinition>> counters,
    Func<IReadOnlyList<(string Id, string Label)>> phases,
    IReadOnlyList<string> kinds,
    Func<OperatorInfo, BitmapSource?> avatar,
    Func<string?>? defaultPhaseId = null)
{
    /// <summary>Every kind, in the order the picker lists them.</summary>
    public static readonly string[] AllKinds = ["SkillPoints", "Combo", "ComboCharge", "BattleSkill", "Ultimate", "UltimateCharge", "UltimateActive", "Counter", "BossStatus", "Controlling", "PhaseShown", "Group"];

    /// <summary>The values offered for a counter with no maximum (Si's damage stacks): 0 to this, picked; a file's larger value is kept.</summary>
    public const int UncappedCounterChoices = 200;

    /// <summary>A new condition of this kind, filled with the first choices.</summary>
    public StateCondition Default(string kind)
    {
        string first = operators().FirstOrDefault()?.Id ?? "";
        return kind switch
        {
            "Combo" => new ComboCondition(first, true),
            "Ultimate" => new UltimateCondition(first, true),
            "UltimateCharge" => new UltimateChargeCondition(first, Comparison.AtLeast, 80),
            "UltimateActive" => new UltimateActiveCondition(first, true),
            "ComboCharge" => new ComboChargeCondition(first, Comparison.AtLeast, 80),
            "BattleSkill" => new BattleSkillCondition(first, true),
            "Counter" => counters().FirstOrDefault() is { } c ? new CounterCondition(c.OperatorId, c.Id, Comparison.AtLeast, c.Max ?? 1) : new SkillPointsCondition(Comparison.AtLeast, 2),
            "BossStatus" => new BossStatusCondition(library.Statuses.FirstOrDefault()?.Id ?? "", true),
            "Controlling" => new ControllingCondition(first),
            // Another phase's count by default: a phase's entry condition can't use its own (Rotation.Problems).
            "PhaseShown" => new PhaseShownCondition(defaultPhaseId?.Invoke() ?? phases().FirstOrDefault().Id ?? "", Comparison.AtLeast, 1),
            // A group is made to say "or": it starts as "any of" with one condition in it.
            "Group" => new AnyCondition([new UltimateCondition(first, true)]),
            _ => new SkillPointsCondition(Comparison.AtLeast, 2),
        };
    }

    /// <summary>One row per condition of <paramref name="list"/>, into <paramref name="into"/>; edits go back into the list.</summary>
    /// <param name="changed">After an edit; true when the list changed shape (added, removed, another kind) and the rows must be drawn again.</param>
    public void AddRows(Panel into, List<StateCondition> list, Action<bool> changed)
    {
        for (int i = 0; i < list.Count; i++) into.Children.Add(Row(list, i, changed));
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

    /// <summary>
    /// One condition as pickers: its kind, then what it needs, then a remove button. A group
    /// ("all of" / "any of") shows its conditions indented below it, with its own "add" button.
    /// </summary>
    private UIElement Row(List<StateCondition> list, int index, Action<bool> changed)
    {
        // A "not" is shown as its condition with the "not" box ticked.
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
        foreach (string k in AllKinds.Where(k => kinds.Contains(k) || k == KindOf(condition))) kind.Items.Add(Item(Loc.Get($"Editor.Condition.{k}"), k));
        kind.SelectedItem = kind.Items.Cast<ComboBoxItem>().First(i => (string)i.Tag == KindOf(condition));
        row.Children.Add(kind);

        UIElement result = row;
        ComboBox? groupMode = null;
        Func<StateCondition> read;
        // A grey line under the row, for conditions that need a word of explanation.
        string? hint = null;
        // Picking a value changes which pickers the row has (a counter's range, a status's count).
        bool rebuildOnPick = false;
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
                AddRows(inner, parts, PartsChanged);
                var add = new Button { Content = Loc.Get("Editor.AddToGroup"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 6) };
                add.Click += (_, _) =>
                {
                    parts.Add(Default("Ultimate"));
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
                            BorderBrush = Brushes.LightGray,
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
                var offered = counters().ToList();
                if (offered.Count == 0)
                {
                    row.Children.Add(new TextBlock { Text = Loc.Get("Editor.NoCounters"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
                    read = () => c;
                    break;
                }
                var which = Picker(offered, d => $"{OperatorName(d.OperatorId)} · {d.NameZh}",
                    offered.FirstOrDefault(d => d.OperatorId == c.OperatorId && d.Id == c.CounterId) ?? offered[0]);
                var compare = ComparePicker(c.Is);
                // A counter with a maximum: 0 to it. One without (Si's damage stacks): 0 to 200, and a
                // file's larger value is kept (Picker adds it).
                int? max = ((CounterDefinition)Picked(which)).Max;
                int top = max ?? UncappedCounterChoices;
                var value = Picker(Enumerable.Range(0, top + 1).ToList(), v => v.ToString(CultureInfo.InvariantCulture),
                    max is { } m ? Math.Clamp(c.Value, 0, m) : Math.Max(0, c.Value));
                row.Children.Add(which);
                row.Children.Add(compare);
                row.Children.Add(value);
                read = () =>
                {
                    var d = (CounterDefinition)Picked(which);
                    int picked = (int)Picked(value);
                    return new CounterCondition(d.OperatorId, d.Id, (Comparison)Picked(compare), d.Max is { } cap ? Math.Min(picked, cap) : picked);
                };
                rebuildOnPick = true; // the value range follows the counter
                break;
            }
            case BossStatusCondition c:
            {
                var present = Picker([true, false], p => Loc.Get(p ? "Editor.Has" : "Editor.HasNot"), c.Present);
                var statuses = library.Statuses.ToList();
                // A status the library doesn't list (not downloaded yet, or from a shared file) stays as it is.
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
                string shownId = ((StatusInfo)Picked(status)).Id;
                if (BossStatusReader.CountsRead.Contains(shownId))
                {
                    // Inflictions' and Vulnerable's stacks (1-4) or a reaction's level (I-IV); a file's value off the list is kept.
                    bool reaction = ReactionLevel.Measured.Contains(shownId);
                    string[] roman = ["I", "II", "III", "IV"];
                    var stacks = Picker(Enumerable.Range(1, 4).ToList(),
                        n => reaction && n is >= 1 and <= 4 ? Loc.Format("Editor.LevelAtLeast", roman[n - 1]) : Loc.Format("Editor.StacksAtLeast", n),
                        Math.Max(1, c.MinStacks));
                    row.Children.Add(stacks);
                    read = () =>
                    {
                        string id = ((StatusInfo)Picked(status)).Id;
                        return new BossStatusCondition(id, (bool)Picked(present), BossStatusReader.CountsRead.Contains(id) ? (int)Picked(stacks) : 1);
                    };
                }
                else
                {
                    // Its count isn't read: only "has it" is checked. A count the file had for this very status is
                    // kept as it is; another status's count isn't carried over when the player switches.
                    hint = Loc.Get("Editor.StacksNotRead");
                    read = () =>
                    {
                        string id = ((StatusInfo)Picked(status)).Id;
                        return new BossStatusCondition(id, (bool)Picked(present), id == c.StatusId ? c.MinStacks : 1);
                    };
                }
                rebuildOnPick = true; // the count picker follows the status
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
                // The rotation's phases by the strip's own names (name and Roman numeral), never ids; a deleted one stays listed as missing.
                var choices = phases().ToList();
                if (!choices.Any(p => p.Id == c.PhaseId)) choices.Insert(0, (c.PhaseId, Loc.Get("Editor.PhaseMissing")));
                var which = Picker(choices, p => p.Label, choices.First(p => p.Id == c.PhaseId));
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
                Children = { row, new TextBlock { Text = hint, Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) } },
            };
        }

        kind.SelectionChanged += (_, _) =>
        {
            if ((string)Picked(kind) == KindOf(condition)) return;
            list[index] = Wrap(Default((string)Picked(kind)));
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
                changed(rebuildOnPick);
            };
        }
        return result;
    }

    private static ComboBox ComparePicker(Comparison selected) =>
        Picker([Comparison.AtLeast, Comparison.AtMost, Comparison.Exactly, Comparison.Below, Comparison.Above], c => Loc.Get($"Editor.Compare.{c}"), selected);

    private ComboBox OperatorPicker(string selected)
    {
        var picker = new ComboBox { MinWidth = 120 };
        FillOperators(picker, operators(), avatar);
        // An operator the list doesn't offer (not in the squad, or not in the library) stays as it is,
        // never swapped for the first one.
        if (selected.Length > 0 && !picker.Items.Cast<ComboBoxItem>().Any(i => Equals(i.Tag, selected)))
            picker.Items.Add(Item(OperatorName(selected), selected));
        Select(picker, selected);
        if (picker.SelectedIndex < 0 && picker.Items.Count > 0) picker.SelectedIndex = 0;
        return picker;
    }

    private string OperatorName(string id) => library.Find(id)?.NameZh ?? id;

    // ---- Shared helpers (also the editors' own pickers) ----

    /// <summary>A picker of <paramref name="values"/>; a selected value not in the list is added and kept, never swapped for the first item.</summary>
    internal static ComboBox Picker<T>(IReadOnlyList<T> values, Func<T, string> text, T? selected)
    {
        var picker = new ComboBox { MinWidth = 64 };
        foreach (var value in values) picker.Items.Add(Item(text(value), value!));
        // (Skill points 1.8 from a file used to save as 0.5.)
        if (selected is not null && !values.Any(v => Equals(v, selected)))
            picker.Items.Add(Item(text(selected), selected));
        picker.SelectedItem = picker.Items.Cast<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, selected)) ?? picker.Items.Cast<ComboBoxItem>().FirstOrDefault();
        return picker;
    }

    internal static object Picked(ComboBox picker) => ((ComboBoxItem)picker.SelectedItem).Tag;

    /// <summary>Operators by Chinese name, each with its avatar.</summary>
    internal static void FillOperators(ComboBox picker, IEnumerable<OperatorInfo> operators, Func<OperatorInfo, BitmapSource?> avatar)
    {
        picker.Items.Clear();
        foreach (var op in operators.OrderBy(o => o.NameZh, StringComparer.Ordinal))
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            if (avatar(op) is { } image) content.Children.Add(new Image { Source = image, Width = 20, Height = 20, Margin = new Thickness(0, 0, 6, 0) });
            content.Children.Add(new TextBlock { Text = op.NameZh, VerticalAlignment = VerticalAlignment.Center });
            picker.Items.Add(new ComboBoxItem { Content = content, Tag = op.Id });
        }
    }

    internal static void Select(ComboBox picker, string? id) =>
        picker.SelectedItem = picker.Items.Cast<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, id));

    internal static ComboBoxItem Item(string text, object tag) => new() { Content = text, Tag = tag };
}
