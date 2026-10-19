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
    private static readonly double[] ComboHintLeadChoices = [0, 0.5, 1, 2, 3];
    private readonly Dictionary<int, string> _bannerStatus = []; // slot → the status line each banner shows
    private readonly Dictionary<int, bool> _bannerSoon = []; // slot → counting down (not ready yet)


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
        foreach (double seconds in ComboHintLeadChoices.Append(_config.ComboHintLeadSeconds).Distinct().Order())
            ComboHintLeadPicker.Items.Add(new ComboBoxItem
            {
                Content = seconds == 0 ? Loc.Get("Menu.ComboHint.LeadOff") : Loc.Format("Menu.ComboHint.Seconds", seconds),
                Tag = seconds,
            });
        ComboHintLeadPicker.SelectedItem = ComboHintLeadPicker.Items.Cast<ComboBoxItem>().First(i => (double)i.Tag == _config.ComboHintLeadSeconds);
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
        if (ComboHintLeadPicker.SelectedItem is ComboBoxItem { Tag: double lead }) _config.ComboHintLeadSeconds = lead;
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
        _hints.Lead = TimeSpan.FromSeconds(_config.ComboHintLeadSeconds);
        // The hint is worked out from the squad panel, so it needs that reading on; its settings
        // are dimmed while it's off.
        ComboHintNeedsPartyOcr.Visibility = _config.PartyOcrEnabled ? Visibility.Collapsed : Visibility.Visible;
        ComboHintCheckBox.IsEnabled = _config.PartyOcrEnabled;
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
                ? _hints.Update(state, _clock.Elapsed).Select(n => state.Slots[n - 1]).Where(Nameable)
                : [])
            .Select(s => (s.Number, Key: s.Operator?.Operator.Id ?? $"slot{s.Number}", Slot: (PartySlot?)s))
            .ToList();
        // Something to drag while placing it.
        if (_menuOpen && wanted.Count == 0) wanted.Add((0, "sample", null));

        // Each banner's status line: ready, or the seconds left while it's shown early.
        var now = _clock.Elapsed;
        foreach (var (number, _, _) in wanted)
        {
            bool soon = _hints.SecondsLeft(number, now) is { } left && state?.Slots[number - 1].Combo is { Ready: false };
            _bannerSoon[number] = soon;
            double secondsLeft = soon ? _hints.SecondsLeft(number, now)!.Value : 0;
            _bannerStatus[number] = !soon ? Loc.Get("ComboHint.Ready")
                : secondsLeft < 0.1 ? Loc.Get("ComboHint.Almost")
                : Loc.Format("ComboHint.Soon", secondsLeft);
        }

        foreach (var banner in _banners.Where(b => !wanted.Any(w => w.Number == b.Slot && w.Key == b.Key)).ToList())
        {
            _banners.Remove(banner);
            ComboBanner.SlideOut(banner.Holder, banner.Element, () => ComboHintStack.Children.Remove(banner.Holder));
        }
        foreach (var (number, key, slot) in wanted)
        {
            if (_banners.FirstOrDefault(b => b.Slot == number && b.Key == key) is { } shown)
            {
                // Up already: the countdown moves on, or it became ready; changed in place, no slide.
                if (shown.Element.Tag as string != Status(number))
                {
                    bool wasSoon = shown.Element.Tag is string before && before != Loc.Get("ComboHint.Ready");
                    shown.Element.Child = shown.BuildContent();
                    shown.Element.Tag = Status(number);
                    if (wasSoon && !Soon(number)) ComboBanner.BecomeReady(shown.Element); // it counted down to ready
                }
                continue;
            }
            Func<UIElement> build = slot is null
                ? () => ComboBanner.Content(null, null, Loc.Get("ComboHint.Sample"), Loc.Get("ComboHint.Ready"))
                : () => OperatorBannerContent(slot);
            var element = ComboBanner.Create(build());
            element.Tag = Status(number);
            ComboBanner.SetSoon(element, Soon(number));
            var holder = ComboBanner.Holder(element, number);
            // In slot order; banners still sliding out count too.
            int index = ComboHintStack.Children.OfType<FrameworkElement>().Count(c => c.Tag is int n && n < number);
            ComboHintStack.Children.Insert(index, holder);
            var banner = new HintBanner(number, key, element, holder, build);
            _banners.Add(banner);
            ComboBanner.SlideIn(holder, element);
        }
    }

    /// <summary>
    /// Whether a hint can go up for this slot now. While the squad is still being recognised (a
    /// battle just started) an unnamed slot waits for its name, rather than showing "1 号位". The
    /// slot number is shown only when recognition isn't possible: no operator library, or the player
    /// marked the slot as not recognised.
    /// </summary>
    private bool Nameable(PartySlot slot) =>
        slot.Operator is not null || _party.Identifier is not { } identifier || identifier.MarkedUnrecognised(slot.Number);

    private UIElement OperatorBannerContent(PartySlot slot)
    {
        var op = slot.Operator?.Operator;
        // The roster's avatar and combo icon; an unrecognised operator gets just the slot number.
        return ComboBanner.Content(op is null ? null : Avatar(op), ComboIcon(op),
            op?.NameZh ?? Loc.Format("Party.SlotNumber", slot.Number), Status(slot.Number), Soon(slot.Number));
    }

    private bool Soon(int slot) => _bannerSoon.TryGetValue(slot, out bool soon) && soon;

    private string Status(int slot) => _bannerStatus.TryGetValue(slot, out var status) ? status : Loc.Get("ComboHint.Ready");

    /// <summary>The operator's avatar from the library (the wiki icon), converted once.</summary>
    private BitmapSource? Avatar(OperatorInfo op)
    {
        if (_avatars.TryGetValue(op.Id, out var cached)) return cached;
        var frame = _party.Identifier?.Library.FindAvatar(op.Id);
        return _avatars[op.Id] = frame is null ? null : ComboBanner.ToBitmap(frame);
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
                _comboIcons[icon.File] = ComboBanner.ToBitmap(frame);
                // Fill the icon into banners already up for this operator, without animating them again.
                foreach (var banner in _banners.Where(b => b.Key == op!.Id)) banner.Element.Child = banner.BuildContent();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                DiagnosticLog.Warn($"couldn't load icon {icon.File}: {ex.Message}");
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
        ScaleComboHints();
        PlacePanelsFromConfig();
    }

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
        ScaleRotationStrip(scale);
        ScaleGauges(scale);
        ScaleInnStrip(scale);
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
