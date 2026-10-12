using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using EndHud.Vision;

namespace EndHud;

/// <summary>
/// A transparent, click-through, always-on-top window that sits exactly over the game's
/// client area and follows it. Runs entirely in its own process.
/// </summary>
public partial class OverlayWindow : Window
{
    private const int HotkeyToggle = 1;
    private const int HotkeyQuit = 2;
    private const int HotkeyMenu = 3;
    private const uint VK_H = 0x48;
    private const uint VK_M = 0x4D;
    private const uint VK_Q = 0x51;
    private const long PassThroughStyles = Native.WS_EX_TRANSPARENT | Native.WS_EX_NOACTIVATE;

    private readonly HudOptions _options;
    private readonly HudConfig _config;
    private readonly PartyMonitor _party;
    private readonly DispatcherTimer _timer;
    private readonly Native.WinEventDelegate _winEventProc; // held so the GC can't collect the callback
    private readonly List<IntPtr> _hooks = [];

    private IntPtr _hwnd;
    private IntPtr _game;
    private string _gameTitle = "";
    private bool _hudOn;
    private bool _menuOpen;
    private (int X, int Y, int W, int H) _lastRect;

    internal OverlayWindow(HudOptions options, HudConfig config)
    {
        _options = options;
        _config = config;
        // Auto mode: the HUD is "on" and appears whenever the game has focus.
        // Manual mode: it starts off and waits for Ctrl+Shift+H / the tray menu.
        _hudOn = config.AutoShowOnGameFocus;

        InitializeComponent();
        AutoShowCheckBox.IsChecked = config.AutoShowOnGameFocus;
        PartyOcrCheckBox.IsChecked = config.PartyOcrEnabled;
        PartySection.Visibility = config.PartyOcrEnabled ? Visibility.Visible : Visibility.Collapsed;
        PartyText.Text = Loc.Get("Party.Waiting");

        _party = new PartyMonitor(TimeSpan.FromMilliseconds(Math.Max(250, config.OcrIntervalMs)));
        _party.Updated += (state, error) => Dispatcher.BeginInvoke(() => ShowParty(state, error));
        _party.LibraryStatus += (key, args) => Dispatcher.BeginInvoke(() => ShowLibraryStatus(key, args));

        _winEventProc = OnWinEvent;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background,
            (_, _) => Tick(), Dispatcher);
    }

    public bool HudOn => _hudOn;
    public bool GameFound => _game != IntPtr.Zero;
    public bool AutoShow => _config.AutoShowOnGameFocus;

    public void ToggleHud()
    {
        _hudOn = !_hudOn;
        Refresh(reassertTopmost: true);
    }

    public void ToggleMenu() => SetMenuOpen(!_menuOpen);

    public void UpdateOperatorLibrary() => _party.UpdateLibrary();

    public void SetAutoShow(bool enabled)
    {
        _config.AutoShowOnGameFocus = enabled;
        ConfigStore.Save(_config);
        AutoShowCheckBox.IsChecked = enabled;
        _hudOn = enabled;
        Refresh(reassertTopmost: true);
    }

    private void SetPartyOcr(bool enabled)
    {
        _config.PartyOcrEnabled = enabled;
        ConfigStore.Save(_config);
        PartySection.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        PartyText.Text = Loc.Get("Party.Waiting");
        Refresh(reassertTopmost: false);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;

        // Click-through (TRANSPARENT), never takes focus (NOACTIVATE), hidden from Alt+Tab (TOOLWINDOW).
        long ex = Native.GetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE).ToInt64();
        ex |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | PassThroughStyles;
        Native.SetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE, new IntPtr(ex));

        if (_options.HideFromCapture)
            Native.SetWindowDisplayAffinity(_hwnd, Native.WDA_EXCLUDEFROMCAPTURE);

        HwndSource.FromHwnd(_hwnd).AddHook(WndProc);
        const uint mods = Native.MOD_CONTROL | Native.MOD_SHIFT | Native.MOD_NOREPEAT;
        Native.RegisterHotKey(_hwnd, HotkeyToggle, mods, VK_H);
        Native.RegisterHotKey(_hwnd, HotkeyQuit, mods, VK_Q);
        Native.RegisterHotKey(_hwnd, HotkeyMenu, mods, VK_M);

        // Out-of-context WinEvent hooks: Windows posts events to our thread; nothing is loaded into the game.
        _hooks.Add(Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _winEventProc, 0, 0, Native.WINEVENT_OUTOFCONTEXT));
        _hooks.Add(Native.SetWinEventHook(Native.EVENT_SYSTEM_MINIMIZESTART, Native.EVENT_SYSTEM_MINIMIZEEND,
            IntPtr.Zero, _winEventProc, 0, 0, Native.WINEVENT_OUTOFCONTEXT));
        _hooks.Add(Native.SetWinEventHook(Native.EVENT_OBJECT_LOCATIONCHANGE, Native.EVENT_OBJECT_LOCATIONCHANGE,
            IntPtr.Zero, _winEventProc, 0, 0, Native.WINEVENT_OUTOFCONTEXT));

        _party.Start();
        Tick();
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        _party.Dispose();
        foreach (var hook in _hooks) Native.UnhookWinEvent(hook);
        Native.UnregisterHotKey(_hwnd, HotkeyToggle);
        Native.UnregisterHotKey(_hwnd, HotkeyQuit);
        Native.UnregisterHotKey(_hwnd, HotkeyMenu);
        base.OnClosed(e);
    }

    /// <summary>
    /// Opening the menu makes the overlay interactive (clickable, focusable); closing it restores
    /// click-through and hands focus back to the game.
    /// </summary>
    private void SetMenuOpen(bool open)
    {
        _menuOpen = open;

        long ex = Native.GetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE).ToInt64();
        ex = open ? ex & ~PassThroughStyles : ex | PassThroughStyles;
        Native.SetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE, new IntPtr(ex));

        MenuPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;

        if (open)
        {
            RefreshOperatorPickers();
            Activate();
            CloseMenuButton.Focus();
        }
        else if (_game != IntPtr.Zero)
        {
            Native.SetForegroundWindow(_game);
        }

        Refresh(reassertTopmost: true);
    }

    private void CloseMenuButton_Click(object sender, RoutedEventArgs e) => SetMenuOpen(false);

    private void AutoShowCheckBox_Click(object sender, RoutedEventArgs e) =>
        SetAutoShow(AutoShowCheckBox.IsChecked == true);

    private void PartyOcrCheckBox_Click(object sender, RoutedEventArgs e) =>
        SetPartyOcr(PartyOcrCheckBox.IsChecked == true);

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (_menuOpen && e.Key == Key.Escape)
        {
            SetMenuOpen(false);
            e.Handled = true;
        }
        base.OnPreviewKeyDown(e);
    }

    /// <summary>Slow loop: (re)acquire the game window and update the clock.</summary>
    private void Tick()
    {
        if (_game != IntPtr.Zero && !Native.IsWindow(_game))
            _game = IntPtr.Zero;

        if (_game == IntPtr.Zero)
        {
            _game = GameWindowFinder.Find(_options.TargetTitle);
            _gameTitle = _game == IntPtr.Zero ? "" : Native.GetTitle(_game);
        }

        ClockText.Text = DateTime.Now.ToString("HH:mm:ss");
        Refresh(reassertTopmost: true);
    }

    private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild,
        uint thread, uint time)
    {
        if (eventType == Native.EVENT_OBJECT_LOCATIONCHANGE)
        {
            // Fires for every moving thing on the desktop (including the cursor); only the game window matters.
            if (hwnd != _game || idObject != Native.OBJID_WINDOW || idChild != 0) return;
            Refresh(reassertTopmost: false);
        }
        else
        {
            Refresh(reassertTopmost: eventType == Native.EVENT_SYSTEM_FOREGROUND);
        }
    }

    /// <summary>Snap the overlay to the game's client area and decide whether the HUD is shown.</summary>
    private void Refresh(bool reassertTopmost)
    {
        int x, y, w, h;
        bool show;

        if (_game == IntPtr.Zero)
        {
            // No game: cover the primary screen. Only shown in manual mode, as a preview.
            x = 0;
            y = 0;
            w = Native.GetSystemMetrics(Native.SM_CXSCREEN);
            h = Native.GetSystemMetrics(Native.SM_CYSCREEN);
            show = _hudOn && !_config.AutoShowOnGameFocus;
            StatusText.Text = _options.TargetTitle is null
                ? Loc.Get("Hud.Status.NotFound")
                : Loc.Format("Hud.Status.TargetNotFound", _options.TargetTitle);
        }
        else
        {
            Native.GetClientRect(_game, out var client);
            var origin = new Native.POINT();
            Native.ClientToScreen(_game, ref origin);
            x = origin.X;
            y = origin.Y;
            w = client.Right;
            h = client.Bottom;

            IntPtr fg = Native.GetForegroundWindow();
            bool gameFocused = fg == _game || fg == _hwnd;
            show = _hudOn && !Native.IsIconic(_game) && (gameFocused || !_config.AutoShowOnGameFocus);
            StatusText.Text = Loc.Format("Hud.Status.Tracking", _gameTitle);
        }

        SizeText.Text = Loc.Format("Hud.Size", w, h, x, y);
        HudRoot.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        _party.SetTarget(_game, show && _config.PartyOcrEnabled);

        if (w <= 0 || h <= 0) return;

        var rect = (x, y, w, h);
        if (rect != _lastRect)
        {
            _lastRect = rect;
            Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, x, y, w, h, Native.SWP_NOACTIVATE);
        }
        else if (reassertTopmost)
        {
            // Some games push themselves topmost when focused; put the overlay back above them.
            Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }
    }

    private void ShowParty(PartyState? state, string? errorKey)
    {
        if (errorKey is not null)
        {
            PartyText.Text = Loc.Get(errorKey);
            return;
        }
        if (state is null || !state.PanelVisible)
        {
            PartyText.Text = Loc.Get("Party.NotVisible");
            return;
        }

        string unknown = Loc.Get("Party.Unknown");
        var sb = new StringBuilder();
        foreach (var slot in state.Slots)
        {
            int i = slot.Number - 1;
            string? operatorId = slot.Operator?.Operator.Id;
            // In combat the level text is replaced by the 连携 bar: keep showing the last level
            // read for this slot, unless a different operator is in it now.
            if (slot.Level is { } read) _lastLevels[i] = (operatorId, read);
            else if (_lastLevels[i] is { } last && last.OperatorId != operatorId) _lastLevels[i] = null;

            string name = slot.Operator?.Operator.NameZh ?? Loc.Format("Party.SlotNumber", slot.Number);
            if (slot.Dead)
            {
                sb.AppendLine(Loc.Format("Party.Dead", name));
                continue;
            }
            string level = _lastLevels[i]?.Level.ToString() ?? unknown;
            string hp = slot.Hp is { } fraction ? $"{fraction:P0}" : unknown;
            sb.AppendLine(Loc.Format("Party.Slot", name, level, hp));

            if (slot.Combo is not null || slot.Ultimate is not null)
            {
                string combo = slot.Combo switch
                {
                    { Ready: true } => Loc.Get("Party.Combo.Ready"),
                    { } c => Loc.Format("Party.Combo.Cooling", c.Progress.ToString("P0")),
                    null => unknown,
                };
                string ultimate = slot.Ultimate switch
                {
                    { Ready: true } => Loc.Get("Party.Ultimate.Ready"),
                    { } u => Loc.Format("Party.Ultimate.Charging", u.Progress.ToString("P0")),
                    null => unknown,
                };
                sb.AppendLine(Loc.Format("Party.Skills", combo, ultimate));
            }
        }
        if (state.PingMs is { } ping) sb.Append(Loc.Format("Party.Ping", ping));
        PartyText.Text = sb.ToString().TrimEnd();
    }

    private void ShowLibraryStatus(string key, object[] args)
    {
        // Progress and failures stay visible; success is shown briefly, then cleared.
        LibraryText.Text = Loc.Format(key, args);
        LibraryText.Visibility = Visibility.Visible;
        if (key == "Operators.Updated")
        {
            var clear = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
            clear.Tick += (_, _) =>
            {
                clear.Stop();
                LibraryText.Visibility = Visibility.Collapsed;
            };
            clear.Start();
        }
        if (_menuOpen) RefreshOperatorPickers();
    }

    private readonly (string? OperatorId, int Level)?[] _lastLevels = new (string?, int)?[4];

    private bool _populatingPickers;

    /// <summary>One picker per squad slot: "unrecognised" plus every operator, most likely first.</summary>
    private void RefreshOperatorPickers()
    {
        _populatingPickers = true;
        try
        {
            OperatorPickers.Children.Clear();
            ForgetConfirmPending = false;

            var identifier = _party.Identifier;
            var current = identifier?.Current;
            if (identifier is null || current is null || current.All(c => c is null))
            {
                OperatorPickersEmpty.Text = Loc.Get(identifier is null ? "Menu.Operators.NoLibrary" : "Menu.Operators.NoReading");
                OperatorPickersEmpty.Visibility = Visibility.Visible;
                ForgetLearnedButton.IsEnabled = identifier is not null;
                return;
            }

            OperatorPickersEmpty.Visibility = Visibility.Collapsed;
            ForgetLearnedButton.IsEnabled = true;
            for (int i = 0; i < current.Count; i++)
            {
                if (current[i] is not { } slot) continue;
                int number = i + 1;

                var picker = new System.Windows.Controls.ComboBox { MaxDropDownHeight = 320 };
                picker.Items.Add(new System.Windows.Controls.ComboBoxItem
                {
                    Content = Loc.Format("Menu.Operators.Unrecognised", number),
                    Tag = null,
                });
                foreach (var op in slot.Suggestions)
                    picker.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = op.NameZh, Tag = op });
                picker.SelectedIndex = slot.Match is { } m
                    ? 1 + slot.Suggestions.ToList().FindIndex(o => o.Id == m.Operator.Id)
                    : 0;
                picker.SelectionChanged += async (_, _) =>
                {
                    if (_populatingPickers || picker.SelectedItem is not System.Windows.Controls.ComboBoxItem item) return;
                    var chosen = item.Tag as Vision.Operators.OperatorInfo;
                    await _party.CorrectAsync(number, chosen);
                    OperatorsStatus.Text = chosen is null
                        ? Loc.Format("Menu.Operators.Cleared", number)
                        : Loc.Format("Menu.Operators.Saved", number, chosen.NameZh);
                };

                var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
                var label = new System.Windows.Controls.TextBlock
                {
                    Text = Loc.Format("Party.SlotNumber", number),
                    Width = 120,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = ClockText.Foreground, // code-built rows don't pick up the XAML text style
                    FontFamily = ClockText.FontFamily,
                };
                DockPanel.SetDock(label, Dock.Left);
                row.Children.Add(label);
                row.Children.Add(picker);
                OperatorPickers.Children.Add(row);
            }
        }
        finally
        {
            _populatingPickers = false;
        }
    }

    private bool ForgetConfirmPending
    {
        get => _forgetConfirmPending;
        set
        {
            _forgetConfirmPending = value;
            ForgetLearnedButton.Content = Loc.Get(value ? "Menu.Operators.ForgetConfirm" : "Menu.Operators.Forget");
        }
    }
    private bool _forgetConfirmPending;

    private void ForgetLearnedButton_Click(object sender, RoutedEventArgs e)
    {
        // Two clicks: this deletes everything EndHud has learned.
        if (!ForgetConfirmPending)
        {
            ForgetConfirmPending = true;
            return;
        }
        _party.ForgetLearned();
        RefreshOperatorPickers();
        OperatorsStatus.Text = Loc.Get("Menu.Operators.Forgotten");
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != Native.WM_HOTKEY) return IntPtr.Zero;

        switch (wParam.ToInt32())
        {
            case HotkeyToggle:
                ToggleHud();
                handled = true;
                break;
            case HotkeyMenu:
                ToggleMenu();
                handled = true;
                break;
            case HotkeyQuit:
                Application.Current.Shutdown();
                handled = true;
                break;
        }
        return IntPtr.Zero;
    }
}
