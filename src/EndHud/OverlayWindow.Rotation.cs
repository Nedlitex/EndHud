using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EndHud.Vision;
using EndHud.Vision.Input;
using EndHud.Vision.Operators;
using EndHud.Vision.Rotations;
using Rotation = EndHud.Vision.Rotations.Rotation;

namespace EndHud;

/// <summary>
/// The rotation strip (排轴): the rotation for the recognised squad, with the step the player is
/// on (<see cref="RotationTracker"/>). Shown in combat, and as a sample while the menu is open. Placed like the combo hint: a fraction
/// of the game window, draggable while the menu is open.
/// </summary>
public partial class OverlayWindow
{
    private readonly RotationLibrary _rotationLibrary;
    private readonly PublishedRotationService _publishedRotations;
    private IReadOnlyList<Rotation> _rotations = [];
    private readonly RotationFollower _rotationFollower = new();
    private readonly Dictionary<string, BitmapSource?> _skillIcons = [];

    private (Rotation Rotation, RotationPosition Position)? _rotationPosition;

    /// <summary>Where the player is inside the phase on the strip; null when their battle-skill keys aren't read.</summary>
    private StepPosition? _stepPosition;
    private readonly DrawnKey _stripKey = new();
    private Point? _rotationDragOffset;

    /// <summary>Keeps the recommended rotations up to date from GitHub (started by App).</summary>
    internal PublishedRotationService PublishedRotations => _publishedRotations;

    /// <summary>(Re)load the rotations from disk, e.g. after the editor saved one.</summary>
    internal void ReloadRotations()
    {
        _rotations = _rotationLibrary.Load().Rotations.Select(r => r.Rotation).ToList();
        _rotationFollower.Forget();
        _rotationPosition = null;
        _stepPosition = null;
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
        // The battle-skill presses, only when the player turned reading them on: then the strip marks the current step.
        var follow = _rotationFollower.Update(state, _config.RotationEnabled && _config.PartyOcrEnabled, RotationFor, ActiveSkillPresses);
        _rotationPosition = follow.Position;
        _stepPosition = follow.Steps;
        // The boss's statuses are read when the rotation or an alert rule asks about them.
        bool alertsNeedStatuses = TrackAlerts(state, follow.InBattle, follow.History);
        _party.ReadBossStatuses = follow.ReadBossStatuses || alertsNeedStatuses;
        RenderRotation();
        TrackGauges(state, follow.InBattle);
    }

    private void RenderRotation()
    {
        StripView? view = _rotationPosition is { } at && _config.RotationEnabled ? View(at.Rotation, at.Position)
            // Something to drag, size and see through while the menu is open, out of battle (a player
            // couldn't adjust the strip before a fight).
            : _menuOpen && _config.RotationEnabled && _config.PartyOcrEnabled ? SampleView()
            : null;

        static string Keys(IEnumerable<StripStep> steps) =>
            string.Join(",", steps.Select(s => $"{s.Action}{s.State}{s.Icon is not null}{s.Avatar is not null}{s.Note}"));
        string? key = view is null ? null
            : $"{view.Phase}|{Keys(view.Steps)}|{view.Next?.Phase}|{Keys(view.Next?.Steps ?? [])}|{string.Join(",", view.Reasons)}";
        if (!_stripKey.Changed(key)) return;
        RotationContent.Content = view is null ? null : RotationStrip.Build(view);
        RotationFrame.Visibility = view is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private StripView SampleView()
    {
        var squad = _lastState?.Slots.Select(s => s.Operator?.Operator.Id).ToList();
        return RotationStripView.Sample(squad is null ? null : RotationFor(squad), _party.Identifier?.Library, SkillIcon, Avatar);
    }

    private StripView View(Rotation rotation, RotationPosition position) =>
        RotationStripView.Build(rotation, position, _lastState, _party.Identifier?.Library, SkillIcon, Avatar, _stepPosition);

    /// <summary>The operator's icon for this action, from the library; loaded once, in the background.</summary>
    private BitmapSource? SkillIcon(OperatorInfo op, StepAction action)
    {
        if (action == StepAction.BasicAttack) return null; // no skill icon: never borrow the battle skill's
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
                RedrawGauges();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                DiagnosticLog.Warn($"couldn't load icon {icon.File}: {ex.Message}");
            }
        }
    }

    // ---- Menu and editor ----

    private RotationEditor? _editor;

    // True until InitRotationSettings has put the saved size in the slider: WPF attaches the slider's
    // ValueChanged before applying Minimum="0.5", which would otherwise save 50% at every start (the
    // gauges' slider did, see OverlayWindow.Gauges).
    private bool _loadingRotationSettings = true;
    private double _rotationWindowScale = 1;

    private void InitRotationSettings()
    {
        _loadingRotationSettings = true;
        RotationCheckBox.IsChecked = _config.RotationEnabled;
        RotationSizeSlider.Value = _config.RotationScale;
        RotationSizeText.Text = Loc.Format("Menu.Gauges.SizeValue", _config.RotationScale * 100);
        RotationOpacitySlider.Value = _config.RotationOpacity;
        RotationOpacityText.Text = Loc.Format("Menu.Gauges.SizeValue", _config.RotationOpacity * 100);
        RotationContent.Opacity = _config.RotationOpacity;
        _loadingRotationSettings = false;
        ApplyRotationSettings();
        _publishedRotations.Changed += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            ReloadRotations();
            _editor?.PublishedChanged();
        });
    }

    private void RotationCheckBox_Click(object sender, RoutedEventArgs e)
    {
        _config.RotationEnabled = RotationCheckBox.IsChecked == true;
        ConfigStore.Save(_config);
        _rotationPosition = null;
        _stepPosition = null;
        ApplyRotationSettings();
        RenderRotation();
    }

    /// <summary>The menu's 排轴 card: it needs the squad panel read; its position button is dimmed while it's off.</summary>
    private void ApplyRotationSettings()
    {
        RotationNeedsPartyOcr.Visibility = _config.PartyOcrEnabled ? Visibility.Collapsed : Visibility.Visible;
        RotationCheckBox.IsEnabled = _config.PartyOcrEnabled;
        RotationResetButton.IsEnabled = _config.PartyOcrEnabled && _config.RotationEnabled;
        RotationSizeRow.IsEnabled = _config.PartyOcrEnabled && _config.RotationEnabled;
        RotationOpacityRow.IsEnabled = _config.PartyOcrEnabled && _config.RotationEnabled;
    }

    /// <summary>The Opacity slider: the strip (not its drag frame) fades as it's dragged, so the fight shows through.</summary>
    private void RotationOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loadingRotationSettings || RotationOpacityText is null) return;
        _config.RotationOpacity = Math.Clamp(Math.Round(e.NewValue, 2), HudConfig.MinRotationOpacity, HudConfig.MaxRotationOpacity);
        RotationOpacityText.Text = Loc.Format("Menu.Gauges.SizeValue", _config.RotationOpacity * 100);
        RotationContent.Opacity = _config.RotationOpacity;
        ConfigStore.Save(_config);
    }

    /// <summary>The Size slider: resize the strip as it's dragged (it stays centred where it is). A player asked: the strip covered too much.</summary>
    private void RotationSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loadingRotationSettings || RotationSizeText is null) return;
        _config.RotationScale = Math.Clamp(Math.Round(e.NewValue, 2), HudConfig.MinRotationScale, HudConfig.MaxRotationScale);
        RotationSizeText.Text = Loc.Format("Menu.Gauges.SizeValue", _config.RotationScale * 100);
        ConfigStore.Save(_config);
        ScaleRotationStrip(_rotationWindowScale);
    }

    private void EditRotationsButton_Click(object sender, RoutedEventArgs e) => OpenRotationEditor();

    /// <summary>The rotation editor (one at a time), on the rotation for the recognised squad if there is one.</summary>
    public void OpenRotationEditor()
    {
        if (_editor is not null)
        {
            _editor.Activate();
            return;
        }
        if (_party.Identifier?.Library is not { } library)
        {
            MessageBox.Show(Loc.Get("Editor.NoLibrary"), Loc.Get("Editor.Title"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var squad = _lastState?.Slots.Select(s => s.Operator?.Operator.Id).ToList() ?? [null, null, null, null];
        _editor = new RotationEditor(_rotationLibrary, library, squad, RotationFor(squad)?.Id, RestorePublishedAsync, saved =>
        {
            // A saved rotation becomes the one used for its squad.
            if (saved is not null)
            {
                _config.ActiveRotations[SquadKey(saved.Squad)] = saved.Id;
                ConfigStore.Save(_config);
            }
            ReloadRotations();
        });
        _editor.Closed += (_, _) => _editor = null;
        if (_menuOpen) SetMenuOpen(false); // hand the screen to the editor
        _editor.Show();
    }

    /// <summary>The editor's "restore recommended rotations": bring back deleted ones and sync now.</summary>
    private async Task<RotationSyncResult?> RestorePublishedAsync()
    {
        _rotationLibrary.ShowHidden();
        var result = await _publishedRotations.SyncAsync();
        ReloadRotations();
        return result;
    }

    // ---- Placing the strip ----

    private void SetRotationEditing(bool editing)
    {
        RotationFrame.BorderBrush = editing ? (Brush)FindResource("Accent") : Brushes.Transparent;
        RotationDragLabel.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        RotationFrame.Cursor = editing ? Cursors.SizeAll : null;
        _stripKey.Invalidate();
        RenderRotation();
    }

    /// <param name="scale">Following the game window (see ComboHintLayout); the player's size is applied on top.</param>
    private void ScaleRotationStrip(double scale)
    {
        _rotationWindowScale = scale;
        double size = scale * _config.RotationScale;
        RotationFrame.LayoutTransform = new ScaleTransform(size, size);
        PlaceRotationFromConfig();
    }

    /// <summary>The strip's width on screen (after scaling).</summary>
    private double RotationWidth() => RotationFrame.ActualWidth * ((RotationFrame.LayoutTransform as ScaleTransform)?.ScaleX ?? 1);

    private void PlaceRotationFromConfig() =>
        PlaceRotation(_config.RotationCenterX * HintCanvas.ActualWidth - RotationWidth() / 2, _config.RotationY * HintCanvas.ActualHeight);

    /// <summary>The strip changed width (a new phase, a step done): keep it centred where it was.</summary>
    private void RotationFrame_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_rotationDragOffset is null) PlaceRotationFromConfig();
    }

    private void PlaceRotation(double left, double top)
    {
        (left, top) = ComboHintLayout.Place(left, top, HintCanvas.ActualWidth, HintCanvas.ActualHeight);
        System.Windows.Controls.Canvas.SetLeft(RotationFrame, left);
        System.Windows.Controls.Canvas.SetTop(RotationFrame, top);
    }

    private void RotationResetButton_Click(object sender, RoutedEventArgs e)
    {
        _config.RotationCenterX = HudConfig.DefaultRotationCenterX;
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
        _config.RotationCenterX = (System.Windows.Controls.Canvas.GetLeft(RotationFrame) + RotationWidth() / 2) / HintCanvas.ActualWidth;
        _config.RotationY = System.Windows.Controls.Canvas.GetTop(RotationFrame) / HintCanvas.ActualHeight;
        ConfigStore.Save(_config);
    }
}

/// <summary>What the overlay's rotation following decided for a reading.</summary>
/// <param name="InBattle">In battle, riding over short gaps (<see cref="StripVisibility"/>).</param>
/// <param name="Position">The rotation and where the strip is in it; null when nothing is shown.</param>
/// <param name="Steps">Where the player is inside the phase (<see cref="StepFollower"/>); null when the
/// player hasn't turned on reading their battle-skill keys, or nothing is shown.</param>
/// <param name="ReadBossStatuses">The squad's rotation asks about the boss's statuses, so they must be read.</param>
/// <param name="History">The phases shown and played through, for alerts' "phase played": the selector of the
/// rotation followed now; <see cref="PhaseHistory.KeysNotRead"/> while the battle-skill keys aren't read (then
/// "phase played" is ignored); <see cref="PhaseHistory.None"/> when no rotation is followed (unknown).</param>
internal sealed record RotationFollowing(bool InBattle, (Rotation Rotation, RotationPosition Position)? Position, StepPosition? Steps, bool ReadBossStatuses, IPhaseHistory History);

/// <summary>
/// The overlay's rotation following, without the drawing (<see cref="OverlayWindow"/> draws what it
/// says): in battle, the phase selector of the rotation for the recognised squad, one per rotation;
/// with a press source, the step follower inside the phase it shows, fed the presses the screen
/// confirms (<see cref="SkillPressJudge"/>).
/// </summary>
internal sealed class RotationFollower
{
    private readonly StripVisibility _visibility = new();
    private readonly Dictionary<string, PhaseSelector> _selectors = [];
    private readonly Dictionary<string, StepFollower> _steps = [];
    private PartyState? _previous;
    private bool _wasInBattle;

    /// <summary>The phase selector of each rotation followed since the rotations were last loaded.</summary>
    internal IReadOnlyDictionary<string, PhaseSelector> Selectors => _selectors;

    /// <summary>How each press was judged since start, by slot (0-3) and <see cref="PressVerdict"/>: counts only, for diagnostics.</summary>
    internal int[,] PressTally { get; } = new int[4, Enum.GetValues<PressVerdict>().Length];

    /// <summary>The rotations changed (the editor saved one): start every rotation afresh.</summary>
    public void Forget()
    {
        _selectors.Clear();
        _steps.Clear();
    }

    /// <param name="enabled">The strip is on and the squad panel is read.</param>
    /// <param name="rotationFor">The rotation for a squad (operator ids by slot, null where unrecognised), if any.</param>
    /// <param name="presses">The player's battle-skill presses; null when the player hasn't turned reading them on
    /// (then no step is marked: the strip is as it always was).</param>
    public RotationFollowing Update(PartyState? state, bool enabled, Func<IReadOnlyList<string?>, Rotation?> rotationFor, ISkillPressSource? presses)
    {
        // Every reading takes its presses, in battle or not, so none pile up for later; each is
        // judged against the reading before it.
        var taken = presses is not null && state?.ReadAt is { } readAt ? presses.TakeUntil(readAt) : [];
        var judged = SkillPressJudge.Judge(_previous, taken);
        foreach (var j in judged)
            if (j.Press.Slot is >= 1 and <= 4) PressTally[j.Press.Slot - 1, (int)j.Verdict]++;

        Rotation? rotation = null;
        (Rotation, RotationPosition)? position = null;
        StepPosition? steps = null;
        IPhaseHistory history = presses is null ? PhaseHistory.KeysNotRead : PhaseHistory.None;
        // In battle only (through short gaps: see StripVisibility), for a rotation made for the recognised squad.
        bool inBattle = state is not null && _visibility.Update(state);
        // The fight ended (StripVisibility already rides over short gaps): every phase-shown count back
        // to 0, every step follower back to the first step.
        if (_wasInBattle && !inBattle)
        {
            foreach (var each in _selectors.Values) each.EndFight();
            foreach (var each in _steps.Values) each.EndFight();
        }
        _wasInBattle = inBattle;
        if (state is not null && inBattle && enabled)
        {
            rotation = rotationFor(state.Slots.Select(s => s.Operator?.Operator.Id).ToList());
            if (rotation is not null)
            {
                if (!_selectors.TryGetValue(rotation.Id, out var selector)) _selectors[rotation.Id] = selector = new PhaseSelector(rotation);
                // The step follower counts the phases played through, for "phase played"; without the keys there is none.
                StepFollower? follower = null;
                if (presses is not null && !_steps.TryGetValue(rotation.Id, out follower)) _steps[rotation.Id] = follower = new StepFollower(rotation);
                var shown = selector.Update(state, follower);
                position = (rotation, shown);
                if (follower is not null)
                {
                    history = selector;
                    var cast = judged.Where(j => j.Verdict == PressVerdict.Counted).Select(j => j.Press.Slot).ToList();
                    steps = follower.Update(shown.Step.PhaseIndex, _previous, state, cast, selector);
                }
            }
        }
        _previous = state;
        // The boss's statuses take a while to read: only when this rotation asks about them, anywhere: a
        // phase's entry condition (what the strip follows) or a step, at any depth of and/or/not.
        return new RotationFollowing(inBattle, position, steps, ConditionWalk.NeedsBossStatuses(rotation), history);
    }
}
