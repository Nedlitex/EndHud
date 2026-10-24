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
/// typed. Used wherever conditions are written: the rotation editor (a phase's entry condition, its
/// priority rules, a step's expected state, a condition step) and the alert editor (a rule's
/// conditions). Every host offers the same full set of kinds (<see cref="ConditionCatalogue"/>);
/// a kind that can't be told in this setting stays listed, greyed out, saying why.
/// </summary>
/// <param name="operators">Who the operator pickers offer: a rotation's squad, or every operator for alerts.</param>
/// <param name="counters">The operator resources offered: a rotation squad's, or all of them for alerts.</param>
/// <param name="phases">The phases a "phase shown" or "phase played" condition can name, by the strip's own names
/// (for alerts, every rotation's phases, by rotation and phase).</param>
/// <param name="host">Where the conditions are used: the kinds offered are the catalogue's for it (a stored condition of another kind is still shown, as itself).</param>
/// <param name="avatar">An operator's avatar for the pickers, if there is one.</param>
/// <param name="keysRead">The player reads their battle-skill keys. While not, "phase played" is listed but
/// greyed out, saying why, and one already there says it is ignored.</param>
/// <param name="defaultPhaseId">The phase a new "phase shown" or "phase played" condition names.</param>
internal sealed class ConditionEditor(
    OperatorLibrary library,
    Func<IEnumerable<OperatorInfo>> operators,
    Func<IEnumerable<CounterDefinition>> counters,
    Func<IReadOnlyList<(string Id, string Label)>> phases,
    ConditionHostKind host,
    Func<OperatorInfo, BitmapSource?> avatar,
    Func<bool> keysRead,
    Func<string?>? defaultPhaseId = null)
{
    /// <summary>Where these conditions are used.</summary>
    public ConditionHostKind Host => host;

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
            // Only an operator whose skill time is read (Si) can be asked about.
            "BattleSkillActive" => new BattleSkillActiveCondition(Readable(kind).FirstOrDefault()?.Id ?? SkillRing.Operators[0], true),
            // Only an operator whose lasting ultimate is read (Laevatain, Zhuang Fangyi, Yvonne).
            "UltimateTimeLeft" => new UltimateTimeLeftCondition(Readable(kind).FirstOrDefault()?.Id ?? first, Comparison.AtMost, 30),
            "PhaseCurrent" => new PhaseCurrentCondition(defaultPhaseId?.Invoke() ?? phases().FirstOrDefault().Id ?? ""),
            "ComboCharge" => new ComboChargeCondition(first, Comparison.AtLeast, 80),
            "BattleSkill" => new BattleSkillCondition(first, true),
            "Counter" => counters().FirstOrDefault() is { } c ? new CounterCondition(c.OperatorId, c.Id, Comparison.AtLeast, c.Max ?? 1) : new SkillPointsCondition(Comparison.AtLeast, 2),
            "BossStatus" => new BossStatusCondition(library.Statuses.FirstOrDefault()?.Id ?? "", true),
            "Controlling" => new ControllingCondition(first),
            // Another phase's count by default: a phase's entry condition can't use its own (Rotation.Problems).
            "PhaseShown" => new PhaseShownCondition(defaultPhaseId?.Invoke() ?? phases().FirstOrDefault().Id ?? "", Comparison.AtLeast, 1),
            "PhasePlayed" => new PhasePlayedCondition(defaultPhaseId?.Invoke() ?? phases().FirstOrDefault().Id ?? "", Comparison.AtLeast, 1),
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

    /// <summary>The kind picker's name for a condition (a "not" is its condition with the box ticked).</summary>
    private static string KindOf(StateCondition c) => ConditionCatalogue.Of(c is NotCondition not ? not.Of : c).EditorKind ?? "SkillPoints";

    /// <summary>Who, a comparison, and a percentage (0% to 100% by 1%; a value from a file is kept as it is). At most 0%: just used.</summary>
    private (ComboBox Who, ComboBox Compare, ComboBox Value) ChargePickers(Panel row, string operatorId, Comparison comparison, int percent, IEnumerable<OperatorInfo>? offered = null)
    {
        var who = OperatorPicker(operatorId, offered);
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
        var offeredKinds = ConditionCatalogue.OfferedIn(host);
        foreach (string k in ConditionCatalogue.EditorKinds.Where(k => offeredKinds.Contains(k) || k == KindOf(condition))) kind.Items.Add(KindItem(k, KindOf(condition)));
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
            case BattleSkillActiveCondition c:
            {
                var who = OperatorPicker(c.OperatorId, Readable("BattleSkillActive"));
                var state = Picker([true, false], a => Loc.Get(a ? "Editor.Active" : "Editor.NotActive"), c.Active);
                row.Children.Add(who);
                row.Children.Add(state);
                read = () => new BattleSkillActiveCondition((string)Picked(who), (bool)Picked(state));
                hint = Loc.Get("Editor.SkillTimeHint");
                break;
            }
            case UltimateTimeLeftCondition c:
            {
                var (who, compare, value) = ChargePickers(row, c.OperatorId, c.Is, c.Percent, Readable("UltimateTimeLeft"));
                read = () => new UltimateTimeLeftCondition((string)Picked(who), (Comparison)Picked(compare), (int)Picked(value));
                hint = Loc.Get("Editor.UltimateTimeLeftHint");
                break;
            }
            case PhaseCurrentCondition c:
            {
                var choices = phases().ToList();
                if (!choices.Any(p => p.Id == c.PhaseId)) choices.Insert(0, (c.PhaseId, Loc.Get("Editor.PhaseMissing")));
                var which = Picker(choices, p => p.Label, choices.First(p => p.Id == c.PhaseId));
                // 0: whether played through or not; 1: played through this showing; 2: not yet.
                var played = Picker([0, 1, 2], p => Loc.Get(p switch { 1 => "Editor.PhaseCurrent.Played", 2 => "Editor.PhaseCurrent.NotPlayed", _ => "Editor.PhaseCurrent.Any" }),
                    c.PlayedThrough switch { true => 1, false => 2, null => 0 });
                row.Children.Add(new TextBlock { Text = Loc.Get("Editor.PhaseCurrent.Is"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
                row.Children.Add(which);
                row.Children.Add(played);
                read = () => new PhaseCurrentCondition((((string Id, string Label))Picked(which)).Id, (int)Picked(played) switch { 1 => true, 2 => false, _ => null });
                // Kept and editable while the keys aren't read, saying the played-through part is ignored until they are.
                hint = Loc.Get(keysRead() ? "Editor.PhaseCurrentHint" : "Editor.PhaseCurrentNeedsKeys");
                kind.ToolTip = hint;
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
                var (which, compare, times) = PhaseCountPickers(row, c.PhaseId, c.Is, c.Times);
                read = () => new PhaseShownCondition((((string Id, string Label))Picked(which)).Id, (Comparison)Picked(compare), (int)Picked(times));
                hint = Loc.Get("Editor.PhaseShownHint");
                kind.ToolTip = hint;
                break;
            }
            case PhasePlayedCondition c:
            {
                var (which, compare, times) = PhaseCountPickers(row, c.PhaseId, c.Is, c.Times);
                read = () => new PhasePlayedCondition((((string Id, string Label))Picked(which)).Id, (Comparison)Picked(compare), (int)Picked(times));
                // Kept and editable while the keys aren't read, saying it is ignored until they are.
                hint = Loc.Get(keysRead() ? "Editor.PhasePlayedHint" : "Editor.PhasePlayedNeedsKeys");
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

    /// <summary>
    /// A kind in the kind picker. One that can't be told in this setting stays listed, greyed out,
    /// saying why, by what its kind needs (<see cref="ConditionKind.Needs"/>): the battle-skill keys
    /// read ("phase played"), a phase to name, an operator whose time is read (Si's battle skill). The
    /// row's own kind is always pickable (it is what the row shows).
    /// </summary>
    private ComboBoxItem KindItem(string kind, string rowKind)
    {
        var item = Item(Loc.Get($"Editor.Condition.{kind}"), kind);
        if (kind == rowKind) return item;
        var needs = ConditionCatalogue.Kinds.First(k => k.EditorKind == kind);
        string? why =
            needs.Needs.HasFlag(ConditionNeeds.KeysRead) && !keysRead() ? "Editor.PhasePlayedNeedsKeys"
            : needs.Needs.HasFlag(ConditionNeeds.Phase) && phases().Count == 0 ? "Editor.PhasePlayedNoPhases"
            // Only Si's skill time is read, only some lasting ultimates: without them in the list, nobody to ask about.
            : needs.OnlyFor is { } only && !operators().Any(o => only.Contains(o.Id))
                ? needs.Needs.HasFlag(ConditionNeeds.SkillTime) ? "Editor.SkillTimeNoOperator" : "Editor.UltimateTimeNoOperator"
            : null;
        if (why is null) return item;
        if (why == "Editor.PhasePlayedNeedsKeys") item.Content = Loc.Get($"Editor.Condition.{kind}.NeedsKeys");
        item.IsEnabled = false;
        item.ToolTip = Loc.Get(why);
        ToolTipService.SetShowOnDisabled(item, true);
        return item;
    }

    /// <summary>
    /// Which phase, a comparison and a count (0 to 9; a file's larger count is kept). The phases by the
    /// strip's own names (name and Roman numeral), never ids; a deleted one stays listed as missing.
    /// </summary>
    private (ComboBox Which, ComboBox Compare, ComboBox Times) PhaseCountPickers(Panel row, string phaseId, Comparison comparison, int count)
    {
        var choices = phases().ToList();
        if (!choices.Any(p => p.Id == phaseId)) choices.Insert(0, (phaseId, Loc.Get("Editor.PhaseMissing")));
        var which = Picker(choices, p => p.Label, choices.First(p => p.Id == phaseId));
        var compare = ComparePicker(comparison);
        var times = Picker(Enumerable.Range(0, 10).ToList(), n => Loc.Format("Editor.Times", n), count);
        row.Children.Add(which);
        row.Children.Add(compare);
        row.Children.Add(times);
        return (which, compare, times);
    }

    private static ComboBox ComparePicker(Comparison selected) =>
        Picker([Comparison.AtLeast, Comparison.AtMost, Comparison.Exactly, Comparison.Below, Comparison.Above], c => Loc.Get($"Editor.Compare.{c}"), selected);

    /// <summary>The offered operators this kind can be told for (<see cref="ConditionKind.OnlyFor"/>: Si's skill time, the lasting ultimates); everyone offered for other kinds.</summary>
    private IEnumerable<OperatorInfo> Readable(string kind) =>
        ConditionCatalogue.Kinds.First(k => k.EditorKind == kind).OnlyFor is { } only ? operators().Where(o => only.Contains(o.Id)) : operators();

    private ComboBox OperatorPicker(string selected, IEnumerable<OperatorInfo>? offered = null)
    {
        var picker = new ComboBox { MinWidth = 120 };
        FillOperators(picker, offered ?? operators(), avatar);
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
