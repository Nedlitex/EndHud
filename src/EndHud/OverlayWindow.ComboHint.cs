using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EndHud.Vision;
using EndHud.Vision.Operators;

namespace EndHud;

/// <summary>
/// The "连携就绪" hint: a slanted banner per operator whose 连携 came off cooldown, like the
/// game's own combo banner, left of the screen's centre by default. While the menu is open the
/// hints can be dragged; the spot is saved as a fraction of the game window.
/// </summary>
public partial class OverlayWindow
{
    private static readonly double[] ComboHintSecondsChoices = [2, 3, 5];

    private readonly ComboHints _hints = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Dictionary<string, BitmapSource?> _comboIcons = [];
    private PartyState? _lastState;
    private string? _shownHints;
    private Point? _dragOffset;
    private bool _loadingComboHintSettings;

    private void InitComboHintSettings()
    {
        _loadingComboHintSettings = true;
        var choices = ComboHintSecondsChoices.Append(_config.ComboHintSeconds).Distinct().Order();
        foreach (double seconds in choices)
            ComboHintSecondsPicker.Items.Add(new ComboBoxItem { Content = Loc.Format("Menu.ComboHint.Seconds", seconds), Tag = seconds });
        ComboHintSecondsPicker.SelectedItem = ComboHintSecondsPicker.Items.Cast<ComboBoxItem>()
            .First(i => (double)i.Tag == _config.ComboHintSeconds);
        ComboHintCheckBox.IsChecked = _config.ComboHintEnabled;
        ComboHintWhenReady.IsChecked = _config.ComboHintMode == ComboHintMode.WhenReady;
        ComboHintWhileReady.IsChecked = _config.ComboHintMode == ComboHintMode.WhileReady;
        _loadingComboHintSettings = false;
        ApplyComboHintSettings();
    }

    private void ComboHintSetting_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingComboHintSettings) return;
        _config.ComboHintEnabled = ComboHintCheckBox.IsChecked == true;
        _config.ComboHintMode = ComboHintWhileReady.IsChecked == true ? ComboHintMode.WhileReady : ComboHintMode.WhenReady;
        if (ComboHintSecondsPicker.SelectedItem is ComboBoxItem { Tag: double seconds }) _config.ComboHintSeconds = seconds;
        ConfigStore.Save(_config);
        ApplyComboHintSettings();
    }

    private void ComboHintResetButton_Click(object sender, RoutedEventArgs e)
    {
        _config.ComboHintX = HudConfig.DefaultComboHintX;
        _config.ComboHintY = HudConfig.DefaultComboHintY;
        ConfigStore.Save(_config);
        PlaceComboHintsFromConfig();
    }

    private void ApplyComboHintSettings()
    {
        _hints.Mode = _config.ComboHintMode;
        _hints.ShowFor = TimeSpan.FromSeconds(_config.ComboHintSeconds);
        // The hint is worked out from the squad panel, so it needs that reading on.
        ComboHintSettings.IsEnabled = _config.PartyOcrEnabled;
        ComboHintModes.IsEnabled = _config.ComboHintEnabled;
        ComboHintSecondsPicker.IsEnabled = _config.ComboHintMode == ComboHintMode.WhenReady;
        _shownHints = null;
        UpdateComboHints();
    }

    /// <summary>Re-evaluates the hints for the latest reading. Safe to call repeatedly with the same one.</summary>
    private void UpdateComboHints()
    {
        var state = _lastState;
        var slots = state is not null && _config.ComboHintEnabled && _config.PartyOcrEnabled
            ? _hints.Update(state, _clock.Elapsed).Select(n => state.Slots[n - 1]).ToList()
            : [];

        string key = string.Join("|", slots.Select(s => $"{s.Number}:{s.Operator?.Operator.Id}")) + (_menuOpen ? "|menu" : "");
        if (key == _shownHints) return;
        _shownHints = key;

        ComboHintStack.Children.Clear();
        foreach (var slot in slots)
        {
            var op = slot.Operator?.Operator;
            ComboHintStack.Children.Add(Banner(ToBitmap(slot.Portrait), ComboIcon(op),
                op?.NameZh ?? Loc.Format("Party.SlotNumber", slot.Number), Loc.Get("ComboHint.Ready")));
        }
        // Something to drag while placing it.
        if (_menuOpen && slots.Count == 0)
            ComboHintStack.Children.Add(Banner(null, null, Loc.Get("ComboHint.Sample"), Loc.Get("ComboHint.Ready")));
    }

    private FrameworkElement Banner(BitmapSource? portrait, BitmapSource? icon, string name, string caption)
    {
        var accent = (Brush)FindResource("Accent");
        // The banner is slanted like the game's; its content is slanted back upright.
        var content = new StackPanel { Orientation = Orientation.Horizontal, RenderTransform = new SkewTransform(12, 0) };
        if (portrait is not null)
            content.Children.Add(new Image { Source = portrait, Height = 64, Margin = new Thickness(14, 4, 8, 4) });
        if (icon is not null)
            content.Children.Add(new Image { Source = icon, Width = 44, Height = 44, Margin = new Thickness(portrait is null ? 14 : 0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(portrait is null && icon is null ? 18 : 0, 0, 28, 0) };
        text.Children.Add(new TextBlock { Text = name, FontSize = 20, FontWeight = FontWeights.Bold });
        text.Children.Add(new TextBlock { Text = caption, FontSize = 14, Foreground = accent });
        content.Children.Add(text);

        return new Border
        {
            MinWidth = 280,
            Height = 72,
            Margin = new Thickness(0, 0, 0, 6),
            Background = new LinearGradientBrush(Color.FromArgb(0xE6, 0x10, 0x14, 0x18), Color.FromArgb(0x30, 0x10, 0x14, 0x18), 0),
            BorderBrush = accent,
            BorderThickness = new Thickness(5, 0, 0, 0),
            RenderTransform = new SkewTransform(-12, 0),
            Child = content,
        };
    }

    private static BitmapSource ToBitmap(Vision.Frame frame)
    {
        var bitmap = BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null, frame.Bgra, frame.Width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>The operator's 连携 icon from the library; loaded once, in the background.</summary>
    private BitmapSource? ComboIcon(OperatorInfo? op)
    {
        var icon = op?.Skills.FirstOrDefault(s => s.Kind == SkillKind.Combo);
        if (icon is null || _party.Identifier?.Library is not { } library) return null;
        if (_comboIcons.TryGetValue(icon.File, out var cached)) return cached;

        _comboIcons[icon.File] = null;
        _ = LoadAsync();
        return null;

        async Task LoadAsync()
        {
            try
            {
                var frame = await library.LoadSkillIconAsync(icon);
                _comboIcons[icon.File] = ToBitmap(frame);
                _shownHints = null; // redraw with the icon
                UpdateComboHints();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Debug.WriteLine($"EndHud: couldn't load {icon.File}: {ex.Message}");
            }
        }
    }

    // ---- Placing the hints ----

    private void SetComboHintEditing(bool editing)
    {
        ComboHintFrame.BorderBrush = editing ? (Brush)FindResource("Accent") : Brushes.Transparent;
        ComboHintDragLabel.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        ComboHintFrame.Cursor = editing ? Cursors.SizeAll : null;
        UpdateComboHints();
    }

    private void HintCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Sized for a 1080-unit-high window and scaled with the game, like the game's own UI.
        double scale = Math.Clamp(HintCanvas.ActualHeight / 1080, 0.6, 2.5);
        ComboHintFrame.LayoutTransform = new ScaleTransform(scale, scale);
        PlaceComboHintsFromConfig();
    }

    private void PlaceComboHintsFromConfig() =>
        PlaceComboHints(_config.ComboHintX * HintCanvas.ActualWidth, _config.ComboHintY * HintCanvas.ActualHeight);

    private void PlaceComboHints(double left, double top)
    {
        // Keep at least part of it on screen, whatever the window size.
        left = Math.Clamp(left, 0, Math.Max(0, HintCanvas.ActualWidth - 80));
        top = Math.Clamp(top, 0, Math.Max(0, HintCanvas.ActualHeight - 80));
        Canvas.SetLeft(ComboHintFrame, left);
        Canvas.SetTop(ComboHintFrame, top);
    }

    private void ComboHint_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_menuOpen) return;
        _dragOffset = e.GetPosition(ComboHintFrame);
        ComboHintFrame.CaptureMouse();
        e.Handled = true;
    }

    private void ComboHint_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragOffset is not { } offset) return;
        var at = e.GetPosition(HintCanvas);
        PlaceComboHints(at.X - offset.X, at.Y - offset.Y);
    }

    private void ComboHint_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragOffset is null) return;
        _dragOffset = null;
        ComboHintFrame.ReleaseMouseCapture();
        if (HintCanvas.ActualWidth <= 0 || HintCanvas.ActualHeight <= 0) return;
        _config.ComboHintX = Canvas.GetLeft(ComboHintFrame) / HintCanvas.ActualWidth;
        _config.ComboHintY = Canvas.GetTop(ComboHintFrame) / HintCanvas.ActualHeight;
        ConfigStore.Save(_config);
    }
}
