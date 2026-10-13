using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EndHud.Vision;
using EndHud.Vision.Operators;
using EndHud.Vision.Rotations;
using Rotation = EndHud.Vision.Rotations.Rotation;

namespace EndHud;

/// <summary>
/// The rotation strip (排轴): the rotation for the recognised squad, with the step the player is
/// on (<see cref="RotationTracker"/>). Shown in combat only. Placed like the combo hint: a fraction
/// of the game window, draggable while the menu is open.
/// </summary>
public partial class OverlayWindow
{
    private readonly RotationStore _rotationStore = new(Path.Combine(ConfigStore.Folder, "rotations"));
    private IReadOnlyList<Rotation> _rotations = [];
    private readonly Dictionary<string, RotationTracker> _trackers = [];
    private readonly Dictionary<string, BitmapSource?> _skillIcons = [];
    private (Rotation Rotation, RotationPosition Position)? _rotationPosition;
    private string? _stripKey;
    private Point? _rotationDragOffset;

    internal RotationStore RotationStore => _rotationStore;

    /// <summary>(Re)load the rotations from disk, e.g. after the editor saved one.</summary>
    internal void ReloadRotations()
    {
        _rotations = _rotationStore.Load().Rotations;
        _trackers.Clear();
        _rotationPosition = null;
        RenderRotation();
    }

    /// <summary>The key a squad's chosen rotation is remembered under: its operators, sorted.</summary>
    internal static string SquadKey(IEnumerable<string> operatorIds) => string.Join("|", operatorIds.Order(StringComparer.Ordinal));

    /// <summary>The rotation for this squad: the one chosen for it, else the first made for it.</summary>
    internal Rotation? RotationFor(IReadOnlyList<string?> squad)
    {
        if (squad.Any(id => id is null)) return null;
        var candidates = _rotations.Where(r => r.IsFor(squad)).ToList();
        if (candidates.Count == 0) return null;
        return _config.ActiveRotations.TryGetValue(SquadKey(squad!), out var id) && candidates.FirstOrDefault(r => r.Id == id) is { } chosen
            ? chosen
            : candidates[0];
    }

    /// <summary>A new reading: move the tracker on, then redraw.</summary>
    private void TrackRotation(PartyState? state)
    {
        _rotationPosition = null;
        Rotation? rotation = null;
        bool inCombat = state is not null && state.Slots.Any(s => s.Combo is not null);
        if (state is not null && inCombat && _config.RotationEnabled && _config.PartyOcrEnabled)
        {
            rotation = RotationFor(state.Slots.Select(s => s.Operator?.Operator.Id).ToList());
            if (rotation is not null)
            {
                if (!_trackers.TryGetValue(rotation.Id, out var tracker)) _trackers[rotation.Id] = tracker = new RotationTracker(rotation);
                if (tracker.Update(state) is { } position) _rotationPosition = (rotation, position);
            }
        }
        // The boss's statuses take a while to read: only when this rotation asks about them.
        _party.ReadBossStatuses = rotation?.Phases.Any(p => p.Steps.Any(s => s.Expect.Any(c => c is BossStatusCondition))) == true;
        RenderRotation();
    }

    private void RenderRotation()
    {
        StripView? view = _rotationPosition is { } at ? View(at.Rotation, at.Position) : null;
        if (view is null && _menuOpen && _config.RotationEnabled)
            view = new StripView(Loc.Get("Rotation.Sample"), [new StripStep(null, null, Loc.Get("Rotation.Action.BattleSkill"), null, StripStepState.Ready, false)], []);

        string? key = view is null ? null
            : $"{view.Phase}|{string.Join(",", view.Steps.Select(s => $"{s.Action}{s.State}{s.Icon is not null}{s.Avatar is not null}{s.Note}"))}|{string.Join(",", view.Reasons)}";
        if (key == _stripKey) return;
        _stripKey = key;
        RotationContent.Content = view is null ? null : RotationStrip.Build(view);
        RotationFrame.Visibility = view is null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>What to draw: the current phase's steps, then the next phase's first two.</summary>
    private StripView View(Rotation rotation, RotationPosition position)
    {
        var played = rotation.Played();
        int current = position.Step.Index;
        var shown = played.Where(p => p.PhaseLabel == position.Step.PhaseLabel).ToList();
        int after = shown.Count > 0 ? shown[^1].Index + 1 : current + 1;
        for (int i = 0; i < 2 && played.Count > 0; i++) shown.Add(played[(after + i) % played.Count]);

        var steps = new List<StripStep>();
        foreach (var p in shown)
        {
            var op = _party.Identifier?.Library.Find(p.Step.OperatorId);
            var state = p.Index == current ? (position.Ready ? StripStepState.Ready : StripStepState.Waiting)
                : p.Index < current && p.PhaseLabel == position.Step.PhaseLabel ? StripStepState.Done
                : StripStepState.Upcoming;
            steps.Add(new StripStep(op is null ? null : SkillIcon(op, p.Step.Action), op is null ? null : Avatar(op),
                p.Step.Label ?? Loc.Get($"Rotation.Action.{p.Step.Action}"), p.Step.Note, state, p.Step.WithPrevious));
        }
        var reasons = position.Ready ? [] : position.Checks.Where(c => c.Holds == false && c.Kind != CheckKind.PreviousStepUsed)
            .Select(c => Reason(c, position.Step.Step)).Where(r => r is not null).Select(r => r!).ToList();
        return new StripView(position.Step.PhaseLabel, steps, reasons);
    }

    /// <summary>Why the current step isn't ready, in words.</summary>
    private string? Reason(ConditionCheck check, RotationStep step)
    {
        var library = _party.Identifier?.Library;
        string Name(string id) => library?.Find(id)?.NameZh ?? id;
        var state = _lastState;
        if (check.Kind == CheckKind.OwnAbility)
        {
            return step.Action switch
            {
                StepAction.BattleSkill => state?.SkillPoints is < 1 ? Loc.Format("Rotation.Reason.SkillPoints", $"{state.SkillPoints:0.#}", 1)
                    : Loc.Format("Rotation.Reason.Cooling", Name(step.OperatorId), Loc.Get("Rotation.Action.BattleSkill")),
                StepAction.Combo => Loc.Format("Rotation.Reason.Cooling", Name(step.OperatorId), Loc.Get("Rotation.Action.Combo")),
                _ => Loc.Format("Rotation.Reason.UltimateCharging", Name(step.OperatorId)),
            };
        }
        return check.Condition switch
        {
            SkillPointsCondition c => Loc.Format("Rotation.Reason.SkillPoints", $"{state?.SkillPoints:0.#}", $"{c.Value:0.#}"),
            ComboCondition c => Loc.Format(c.Ready ? "Rotation.Reason.NeedComboReady" : "Rotation.Reason.NeedComboCooling", Name(c.OperatorId)),
            UltimateCondition c => Loc.Format(c.Ready ? "Rotation.Reason.NeedUltimateReady" : "Rotation.Reason.NeedUltimateUsed", Name(c.OperatorId)),
            CounterCondition c => Loc.Format("Rotation.Reason.Counter", Name(c.OperatorId),
                OperatorCounters.Definitions.FirstOrDefault(d => d.Id == c.CounterId)?.NameZh ?? c.CounterId,
                c.Is switch { Comparison.AtLeast => "≥", Comparison.AtMost => "≤", _ => "=" } + c.Value,
                state?.Counters.FirstOrDefault(r => r.CounterId == c.CounterId)?.Value?.ToString() ?? "?"),
            BossStatusCondition c => Loc.Format(c.Present ? "Rotation.Reason.BossLacks" : "Rotation.Reason.BossHas",
                StatusNames.Zh(c.StatusId) ?? c.StatusId),
            ControllingCondition c => Loc.Format("Rotation.Reason.Controlling", Name(c.OperatorId)),
            _ => null,
        };
    }

    /// <summary>The operator's icon for this action, from the library; loaded once, in the background.</summary>
    private BitmapSource? SkillIcon(OperatorInfo op, StepAction action)
    {
        var kind = action switch
        {
            StepAction.Combo => SkillKind.Combo,
            StepAction.Ultimate => SkillKind.Ultimate,
            _ => SkillKind.BattleSkill,
        };
        var icon = op.Skills.FirstOrDefault(s => s.Kind == kind);
        if (icon is null || _party.Identifier?.Library is not { } library) return null;
        if (_skillIcons.TryGetValue(icon.File, out var cached)) return cached;

        _skillIcons[icon.File] = null;
        _ = LoadAsync();
        return null;

        async Task LoadAsync()
        {
            try
            {
                _skillIcons[icon.File] = ComboBanner.ToBitmap(await library.LoadSkillIconAsync(icon));
                RenderRotation();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Debug.WriteLine($"EndHud: couldn't load {icon.File}: {ex.Message}");
            }
        }
    }

    // ---- Placing the strip ----

    private void SetRotationEditing(bool editing)
    {
        RotationFrame.BorderBrush = editing ? (Brush)FindResource("Accent") : Brushes.Transparent;
        RotationDragLabel.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        RotationFrame.Cursor = editing ? Cursors.SizeAll : null;
        _stripKey = null;
        RenderRotation();
    }

    private void ScaleRotationStrip(double scale)
    {
        RotationFrame.LayoutTransform = new ScaleTransform(scale, scale);
        PlaceRotationFromConfig();
    }

    private void PlaceRotationFromConfig() =>
        PlaceRotation(_config.RotationX * HintCanvas.ActualWidth, _config.RotationY * HintCanvas.ActualHeight);

    private void PlaceRotation(double left, double top)
    {
        (left, top) = ComboHintLayout.Place(left, top, HintCanvas.ActualWidth, HintCanvas.ActualHeight);
        System.Windows.Controls.Canvas.SetLeft(RotationFrame, left);
        System.Windows.Controls.Canvas.SetTop(RotationFrame, top);
    }

    private void RotationResetButton_Click(object sender, RoutedEventArgs e)
    {
        _config.RotationX = HudConfig.DefaultRotationX;
        _config.RotationY = HudConfig.DefaultRotationY;
        ConfigStore.Save(_config);
        PlaceRotationFromConfig();
    }

    private void Rotation_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_menuOpen) return;
        _rotationDragOffset = e.GetPosition(RotationFrame);
        RotationFrame.CaptureMouse();
        e.Handled = true;
    }

    private void Rotation_MouseMove(object sender, MouseEventArgs e)
    {
        if (_rotationDragOffset is not { } offset) return;
        var at = e.GetPosition(HintCanvas);
        PlaceRotation(at.X - offset.X, at.Y - offset.Y);
    }

    private void Rotation_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_rotationDragOffset is null) return;
        _rotationDragOffset = null;
        RotationFrame.ReleaseMouseCapture();
        if (HintCanvas.ActualWidth <= 0 || HintCanvas.ActualHeight <= 0) return;
        _config.RotationX = System.Windows.Controls.Canvas.GetLeft(RotationFrame) / HintCanvas.ActualWidth;
        _config.RotationY = System.Windows.Controls.Canvas.GetTop(RotationFrame) / HintCanvas.ActualHeight;
        ConfigStore.Save(_config);
    }
}
