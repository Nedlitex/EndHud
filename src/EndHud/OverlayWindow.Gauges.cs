using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EndHud.Vision;
using EndHud.Vision.Operators;

namespace EndHud;

/// <summary>
/// The gauges: team buffs such as Link, and each recognised operator's ultimate charge and
/// own counters, as circles, in battle only (the same rule as the rotation strip: see StripVisibility). Placed like
/// the rotation strip: centred on a fraction of the game window, bottom middle by default, draggable
/// while the menu is open.
/// </summary>
public partial class OverlayWindow
{
    private readonly CounterMemory _counterMemory = new();
    private readonly List<string> _buffsSeen = []; // this battle, in the order first seen
    private readonly Dictionary<string, BitmapSource?> _statusIcons = [];
    private GaugeBoard? _gauges;
    private string? _gaugeKey;
    private Point? _gaugeDragOffset;
    private double _windowScale = 1; // following the game window (ComboHintLayout), before the player's size
    // True until InitGaugeSettings has put the saved size in the slider. WPF attaches the slider's
    // ValueChanged before applying Minimum="0.5", which moves it from 0 to 0.5: without this, that
    // was saved as the player's size at every start (every player's gauges were at 50%).
    private bool _loadingGaugeSettings = true;

    /// <summary>A new reading (<paramref name="inBattle"/> from the strip's rule): remember the counters, then redraw.</summary>
    private void TrackGauges(PartyState? state, bool inBattle)
    {
        if (!inBattle)
        {
            // A new battle starts from what it shows.
            _counterMemory.Clear();
            _buffsSeen.Clear();
        }
        if (state is not null && inBattle) _buffsSeen.AddRange(state.Buffs.Except(_buffsSeen).ToList());
        _gauges = state is not null && inBattle && _config.GaugesEnabled && _config.PartyOcrEnabled
            ? GaugesView.Build(state, _counterMemory.Update(state), _buffsSeen, SkillIcon, Avatar, StatusIcon)
            : null;
        RenderGauges();
    }

    private void RenderGauges()
    {
        var board = _gauges is { Gauges.Count: > 0 } shown && _config.GaugesEnabled ? shown
            // Something to drag while placing it, out of battle.
            : _menuOpen && _config.GaugesEnabled && _config.PartyOcrEnabled ? GaugesView.Sample()
            : null;
        string? key = board is null ? null : GaugesView.Key(board);
        if (key == _gaugeKey) return;
        _gaugeKey = key;
        GaugeContent.Content = board is null ? null : Gauges.Build(board);
        GaugeFrame.Visibility = board is null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>An icon finished loading: draw it in.</summary>
    private void RedrawGauges()
    {
        if (_lastState is { } state && _gauges is not null)
        {
            _gauges = GaugesView.Build(state, _counterMemory.Update(state), _buffsSeen, SkillIcon, Avatar, StatusIcon);
            RenderGauges();
        }
    }

    /// <summary>A status's icon from the library (the wiki's), converted once.</summary>
    private BitmapSource? StatusIcon(string id)
    {
        if (_statusIcons.TryGetValue(id, out var cached)) return cached;
        var frame = _party.Identifier?.Library.StatusIcon(id);
        return _statusIcons[id] = frame is null ? null : ComboBanner.ToBitmap(frame);
    }

    // ---- Menu ----

    private void InitGaugeSettings()
    {
        _loadingGaugeSettings = true;
        GaugesCheckBox.IsChecked = _config.GaugesEnabled;
        GaugesSizeSlider.Value = _config.GaugesScale;
        GaugesSizeText.Text = Loc.Format("Menu.Gauges.SizeValue", _config.GaugesScale * 100);
        _loadingGaugeSettings = false;
        ApplyGaugeSettings();
    }

    private void GaugesCheckBox_Click(object sender, RoutedEventArgs e)
    {
        _config.GaugesEnabled = GaugesCheckBox.IsChecked == true;
        ConfigStore.Save(_config);
        ApplyGaugeSettings();
        RenderGauges();
    }

    /// <summary>The menu's gauges card: it needs the squad panel read; its position button is dimmed while it's off.</summary>
    private void ApplyGaugeSettings()
    {
        GaugesNeedsPartyOcr.Visibility = _config.PartyOcrEnabled ? Visibility.Collapsed : Visibility.Visible;
        GaugesCheckBox.IsEnabled = _config.PartyOcrEnabled;
        GaugesResetButton.IsEnabled = _config.PartyOcrEnabled && _config.GaugesEnabled;
        GaugesSizeRow.IsEnabled = _config.PartyOcrEnabled && _config.GaugesEnabled;
    }

    /// <summary>The Size slider: resize the gauges as it's dragged (they stay centred where they are).</summary>
    private void GaugesSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loadingGaugeSettings || GaugesSizeText is null) return;
        _config.GaugesScale = Math.Clamp(Math.Round(e.NewValue, 2), HudConfig.MinGaugesScale, HudConfig.MaxGaugesScale);
        GaugesSizeText.Text = Loc.Format("Menu.Gauges.SizeValue", _config.GaugesScale * 100);
        ConfigStore.Save(_config);
        ScaleGauges(_windowScale);
    }

    private void GaugesResetButton_Click(object sender, RoutedEventArgs e)
    {
        _config.GaugesCenterX = HudConfig.DefaultGaugesCenterX;
        _config.GaugesBottomY = HudConfig.DefaultGaugesBottomY;
        ConfigStore.Save(_config);
        PlaceGaugesFromConfig();
    }

    // ---- Placing the gauges ----

    private void SetGaugesEditing(bool editing)
    {
        GaugeFrame.BorderBrush = editing ? (Brush)FindResource("Accent") : Brushes.Transparent;
        GaugeDragLabel.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        GaugeFrame.Cursor = editing ? Cursors.SizeAll : null;
        RenderGauges();
    }

    /// <param name="scale">Following the game window (see ComboHintLayout); the player's size is applied on top.</param>
    private void ScaleGauges(double scale)
    {
        _windowScale = scale;
        double size = scale * _config.GaugesScale;
        GaugeFrame.LayoutTransform = new ScaleTransform(size, size);
        PlaceGaugesFromConfig();
    }

    /// <summary>The gauges' width on screen (after scaling).</summary>
    private double GaugesWidth() => GaugeFrame.ActualWidth * ((GaugeFrame.LayoutTransform as ScaleTransform)?.ScaleX ?? 1);

    private double GaugesHeight() => GaugeFrame.ActualHeight * ((GaugeFrame.LayoutTransform as ScaleTransform)?.ScaleY ?? 1);

    private void PlaceGaugesFromConfig() =>
        PlaceGauges(_config.GaugesCenterX * HintCanvas.ActualWidth - GaugesWidth() / 2, _config.GaugesBottomY * HintCanvas.ActualHeight - GaugesHeight());

    /// <summary>The gauges changed size (an operator recognised, a buff seen, the Size slider): keep them centred, their bottom where it was.</summary>
    private void GaugeFrame_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_gaugeDragOffset is null) PlaceGaugesFromConfig();
    }

    private void PlaceGauges(double left, double top)
    {
        (left, top) = ComboHintLayout.Place(left, top, HintCanvas.ActualWidth, HintCanvas.ActualHeight);
        Canvas.SetLeft(GaugeFrame, left);
        Canvas.SetTop(GaugeFrame, top);
    }

    private void Gauges_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_menuOpen) return;
        _gaugeDragOffset = e.GetPosition(GaugeFrame);
        GaugeFrame.CaptureMouse();
        e.Handled = true;
    }

    private void Gauges_MouseMove(object sender, MouseEventArgs e)
    {
        if (_gaugeDragOffset is not { } offset) return;
        var at = e.GetPosition(HintCanvas);
        PlaceGauges(at.X - offset.X, at.Y - offset.Y);
    }

    private void Gauges_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_gaugeDragOffset is null) return;
        _gaugeDragOffset = null;
        GaugeFrame.ReleaseMouseCapture();
        if (HintCanvas.ActualWidth <= 0 || HintCanvas.ActualHeight <= 0) return;
        _config.GaugesCenterX = (Canvas.GetLeft(GaugeFrame) + GaugesWidth() / 2) / HintCanvas.ActualWidth;
        _config.GaugesBottomY = (Canvas.GetTop(GaugeFrame) + GaugesHeight()) / HintCanvas.ActualHeight;
        ConfigStore.Save(_config);
    }
}
