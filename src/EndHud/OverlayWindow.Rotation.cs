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
    private readonly RotationLibrary _rotationLibrary;
    private readonly PublishedRotationService _publishedRotations;
    private IReadOnlyList<Rotation> _rotations = [];
    private readonly Dictionary<string, PhaseSelector> _trackers = [];
    private readonly Dictionary<string, BitmapSource?> _skillIcons = [];

    private readonly StripVisibility _stripVisibility = new();
    private (Rotation Rotation, RotationPosition Position)? _rotationPosition;
    private string? _stripKey;
    private Point? _rotationDragOffset;

    /// <summary>Keeps the recommended rotations up to date from GitHub (started by App).</summary>
    internal PublishedRotationService PublishedRotations => _publishedRotations;

    /// <summary>(Re)load the rotations from disk, e.g. after the editor saved one.</summary>
    internal void ReloadRotations()
    {
        _rotations = _rotationLibrary.Load().Rotations.Select(r => r.Rotation).ToList();
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
        Rotation? rotation = null;
        _rotationPosition = null;
        // In battle only (through short gaps: see StripVisibility), for a rotation made for the recognised squad.
        bool inBattle = state is not null && _stripVisibility.Update(state);
        if (state is not null && inBattle && _config.RotationEnabled && _config.PartyOcrEnabled)
        {
            rotation = RotationFor(state.Slots.Select(s => s.Operator?.Operator.Id).ToList());
            if (rotation is not null)
            {
                if (!_trackers.TryGetValue(rotation.Id, out var tracker)) _trackers[rotation.Id] = tracker = new PhaseSelector(rotation);
                if (tracker.Update(state) is { } position) _rotationPosition = (rotation, position);
            }
        }
        // The boss's statuses take a while to read: only when this rotation asks about them.
        _party.ReadBossStatuses = rotation?.Phases.Any(p => p.Steps.Any(s => s.Expect.Any(c => c is BossStatusCondition))) == true;
        RenderRotation();
        TrackGauges(state, inBattle);
    }

    private void RenderRotation()
    {
        // No sample while the menu is open: the strip is placed by dragging it in a battle.
        StripView? view = _rotationPosition is { } at && _config.RotationEnabled ? View(at.Rotation, at.Position) : null;

        string? key = view is null ? null
            : $"{view.Phase}|{string.Join(",", view.Steps.Select(s => $"{s.Action}{s.State}{s.Icon is not null}{s.Avatar is not null}{s.Note}"))}|{string.Join(",", view.Reasons)}";
        if (key == _stripKey) return;
        _stripKey = key;
        RotationContent.Content = view is null ? null : RotationStrip.Build(view);
        RotationFrame.Visibility = view is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private StripView View(Rotation rotation, RotationPosition position) =>
        RotationStripView.Build(rotation, position, _lastState, _party.Identifier?.Library, SkillIcon, Avatar);

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
                RedrawGauges();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Debug.WriteLine($"EndHud: couldn't load {icon.File}: {ex.Message}");
            }
        }
    }

    // ---- Menu and editor ----

    private RotationEditor? _editor;

    private void InitRotationSettings()
    {
        RotationCheckBox.IsChecked = _config.RotationEnabled;
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
        ApplyRotationSettings();
        RenderRotation();
    }

    /// <summary>The menu's 排轴 card: it needs the squad panel read; its position button is dimmed while it's off.</summary>
    private void ApplyRotationSettings()
    {
        RotationNeedsPartyOcr.Visibility = _config.PartyOcrEnabled ? Visibility.Collapsed : Visibility.Visible;
        RotationCheckBox.IsEnabled = _config.PartyOcrEnabled;
        RotationResetButton.IsEnabled = _config.PartyOcrEnabled && _config.RotationEnabled;
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
        _stripKey = null;
        RenderRotation();
    }

    private void ScaleRotationStrip(double scale)
    {
        RotationFrame.LayoutTransform = new ScaleTransform(scale, scale);
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
