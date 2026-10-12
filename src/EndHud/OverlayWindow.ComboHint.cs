using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using EndHud.Vision;
using EndHud.Vision.Operators;

namespace EndHud;

/// <summary>
/// The combo-ready hint: a slanted banner per operator whose combo came off cooldown, like the
/// game's own combo banner, left of the screen's centre by default. While the menu is open the
/// hints can be dragged; the spot is saved as a fraction of the game window.
/// </summary>
public partial class OverlayWindow
{
    private static readonly double[] ComboHintSecondsChoices = [2, 3, 5];

    private static readonly TimeSpan SlideInTime = TimeSpan.FromMilliseconds(320);
    private static readonly TimeSpan SlideOutTime = TimeSpan.FromMilliseconds(200);
    private const double SlideDistance = 160;
    private const double BannerHeight = 72, BannerGap = 6;
    private static readonly TimeSpan GapTime = TimeSpan.FromMilliseconds(220);

    private readonly ComboHints _hints = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Dictionary<string, BitmapSource?> _comboIcons = [];
    private readonly Dictionary<string, BitmapSource?> _avatars = [];
    private readonly List<HintBanner> _banners = [];
    private PartyState? _lastState;
    private Point? _dragOffset;
    private bool _loadingComboHintSettings;

    /// <summary>A banner on screen. <paramref name="Key"/> is who it's for, so a banner is only replaced when that changes.</summary>
    /// <paramref name="Holder"/> is the banner's space in the stack: it opens and closes, so the
    /// banners below glide to their new place instead of jumping.
    private sealed record HintBanner(int Slot, string Key, Border Element, Grid Holder, Func<UIElement> BuildContent);

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
        UpdateComboHints();
    }

    /// <summary>
    /// Re-evaluates the hints for the latest reading. Safe to call repeatedly with the same one.
    /// A banner that stays up is left alone; new ones slide in, finished ones slide out.
    /// </summary>
    private void UpdateComboHints()
    {
        var state = _lastState;
        var wanted = (state is not null && _config.ComboHintEnabled && _config.PartyOcrEnabled
                ? _hints.Update(state, _clock.Elapsed).Select(n => state.Slots[n - 1])
                : [])
            .Select(s => (s.Number, Key: s.Operator?.Operator.Id ?? $"slot{s.Number}", Slot: (PartySlot?)s))
            .ToList();
        // Something to drag while placing it.
        if (_menuOpen && wanted.Count == 0) wanted.Add((0, "sample", null));

        foreach (var banner in _banners.Where(b => !wanted.Any(w => w.Number == b.Slot && w.Key == b.Key)).ToList())
        {
            _banners.Remove(banner);
            SlideOut(banner);
        }
        foreach (var (number, key, slot) in wanted)
        {
            if (_banners.Any(b => b.Slot == number && b.Key == key)) continue;
            Func<UIElement> build = slot is null
                ? () => BannerContent(null, null, Loc.Get("ComboHint.Sample"))
                : () => OperatorBannerContent(slot);
            var element = Banner(build());
            var holder = new Grid { Tag = number, Height = 0, Children = { element } };
            // In slot order; banners still sliding out count too.
            int index = ComboHintStack.Children.OfType<FrameworkElement>().Count(c => c.Tag is int n && n < number);
            ComboHintStack.Children.Insert(index, holder);
            var banner = new HintBanner(number, key, element, holder, build);
            _banners.Add(banner);
            SlideIn(banner);
        }
    }

    private UIElement OperatorBannerContent(PartySlot slot)
    {
        var op = slot.Operator?.Operator;
        // The roster's avatar and combo icon; an unrecognised operator gets just the slot number.
        return BannerContent(op is null ? null : Avatar(op), ComboIcon(op), op?.NameZh ?? Loc.Format("Party.SlotNumber", slot.Number));
    }

    private UIElement BannerContent(BitmapSource? avatar, BitmapSource? icon, string name)
    {
        // The banner is slanted like the game's; its content is slanted back upright.
        var content = new StackPanel { Orientation = Orientation.Horizontal, RenderTransform = new SkewTransform(12, 0) };
        if (avatar is not null)
            content.Children.Add(new Image { Source = avatar, Width = 60, Height = 60, Margin = new Thickness(14, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        if (icon is not null)
            content.Children.Add(new Image { Source = icon, Width = 44, Height = 44, Margin = new Thickness(avatar is null ? 14 : 0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(avatar is null && icon is null ? 18 : 0, 0, 28, 0) };
        text.Children.Add(new TextBlock { Text = name, FontSize = 20, FontWeight = FontWeights.Bold });
        text.Children.Add(new TextBlock { Text = Loc.Get("ComboHint.Ready"), FontSize = 14, Foreground = (Brush)FindResource("Accent") });
        content.Children.Add(text);
        return content;
    }

    private Border Banner(UIElement content) => new()
    {
        MinWidth = 280,
        Height = BannerHeight,
        VerticalAlignment = VerticalAlignment.Center,
        Background = new LinearGradientBrush(Color.FromArgb(0xE6, 0x10, 0x14, 0x18), Color.FromArgb(0x30, 0x10, 0x14, 0x18), 0),
        BorderBrush = (Brush)FindResource("Accent"),
        BorderThickness = new Thickness(5, 0, 0, 0),
        RenderTransform = new TransformGroup { Children = { new SkewTransform(-12, 0), new TranslateTransform() } },
        Child = content,
    };

    /// <summary>Slides in from the left and fades in, with the gold edge flaring and settling, so it catches the eye.</summary>
    private static void SlideIn(HintBanner hint)
    {
        var banner = hint.Element;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        hint.Holder.BeginAnimation(HeightProperty, new DoubleAnimation(0, BannerHeight + BannerGap, GapTime) { EasingFunction = ease });
        Translation(banner).BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-SlideDistance, 0, SlideInTime) { EasingFunction = ease });
        banner.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, SlideInTime));
        banner.BeginAnimation(Border.BorderThicknessProperty, new ThicknessAnimation(new Thickness(18, 0, 0, 0), new Thickness(5, 0, 0, 0),
            TimeSpan.FromMilliseconds(600)) { BeginTime = SlideInTime / 2, EasingFunction = ease });
    }

    /// <summary>Slides back out to the left and fades away; then its space closes and it leaves the stack.</summary>
    private void SlideOut(HintBanner hint)
    {
        var banner = hint.Element;
        Translation(banner).BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(-SlideDistance / 2, SlideOutTime) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
        banner.BeginAnimation(OpacityProperty, new DoubleAnimation(0, SlideOutTime));
        var close = new DoubleAnimation(0, GapTime) { BeginTime = SlideOutTime * 0.75, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
        close.Completed += (_, _) => ComboHintStack.Children.Remove(hint.Holder);
        hint.Holder.BeginAnimation(HeightProperty, close);
    }

    private static TranslateTransform Translation(Border banner) =>
        ((TransformGroup)banner.RenderTransform).Children.OfType<TranslateTransform>().Single();

    private static BitmapSource ToBitmap(Vision.Frame frame)
    {
        var bitmap = BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null, frame.Bgra, frame.Width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>The operator's avatar from the library (the wiki icon), converted once.</summary>
    private BitmapSource? Avatar(OperatorInfo op)
    {
        if (_avatars.TryGetValue(op.Id, out var cached)) return cached;
        var frame = _party.Identifier?.Library.FindAvatar(op.Id);
        return _avatars[op.Id] = frame is null ? null : ToBitmap(frame);
    }

    /// <summary>The operator's combo icon from the library; loaded once, in the background.</summary>
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
                // Fill the icon into banners already up for this operator, without animating them again.
                foreach (var banner in _banners.Where(b => b.Key == op!.Id)) banner.Element.Child = banner.BuildContent();
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

    private void HintCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => ScaleComboHints();

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        ScaleComboHints(); // the game window moved to a monitor with different display scaling
    }

    /// <summary>Scale the hints with the game window, like the game's own HUD (see <see cref="ComboHintLayout"/>).</summary>
    private void ScaleComboHints()
    {
        double scale = ComboHintLayout.Scale(HintCanvas.ActualHeight, VisualTreeHelper.GetDpi(this).DpiScaleY);
        ComboHintFrame.LayoutTransform = new ScaleTransform(scale, scale);
        PlaceComboHintsFromConfig();
    }

    private void PlaceComboHintsFromConfig() =>
        PlaceComboHints(_config.ComboHintX * HintCanvas.ActualWidth, _config.ComboHintY * HintCanvas.ActualHeight);

    private void PlaceComboHints(double left, double top)
    {
        (left, top) = ComboHintLayout.Place(left, top, HintCanvas.ActualWidth, HintCanvas.ActualHeight);
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
