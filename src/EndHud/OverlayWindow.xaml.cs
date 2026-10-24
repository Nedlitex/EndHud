using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using System.IO;
using EndHud.Vision;
using EndHud.Vision.Rotations;

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
    private const int HotkeyDebug = 4;
    // The same, on Ctrl+F9-F11 (easier to reach mid-game). Not plain F9/F10: the game uses those
    // itself, and a registered hotkey never reaches the game. No quit on an F-key: too easy by accident.
    private const int HotkeyMenuAlt = 5;
    private const int HotkeyToggleAlt = 6;
    private const int HotkeyDebugAlt = 7;
    private const uint VK_F9 = 0x78, VK_F10 = 0x79, VK_F11 = 0x7A;
    private const uint VK_D = 0x44;
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
    private readonly GameExitWatch _gameExit = new();
    private string _gameTitle = "";
    private bool _hudOn;
    private bool _menuOpen;
    // Off at every start, whatever was shown last time: it's for checking readings, not for play.
    private bool _debugPanel;
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
        DebugPanelCheckBox.IsChecked = false;
        CloseWithGameCheckBox.IsChecked = config.CloseWithGame;
        CheckForUpdatesCheckBox.IsChecked = config.CheckForUpdates;
        AutoInstallUpdatesCheckBox.IsChecked = config.AutoInstallUpdates;
        AutoStartCheckBox.IsChecked = ReadAutoStart();
        AutoInstallUpdatesCheckBox.IsEnabled = config.CheckForUpdates;
        DebugRoot.Visibility = Visibility.Collapsed;
        InitComboHintSettings();
        _rotationLibrary = new RotationLibrary(Path.Combine(ConfigStore.Folder, "rotations"));
        _publishedRotations = new PublishedRotationService(_rotationLibrary);
        ReloadRotations();
        InitRotationSettings();
        InitSkillInput();
        InitGaugeSettings();
        InitAlertSettings();
        VersionText.Text = ReleaseChecker.DisplayVersion;

        _party = new PartyMonitor(TimeSpan.FromMilliseconds(Math.Max(250, config.OcrIntervalMs))) { UpdatesEnabled = () => _config.CheckForUpdates };
        _party.Updated += (state, error) => Dispatcher.BeginInvoke(() => ShowParty(state, error));
        _party.LibraryStatus += (key, args) => Dispatcher.BeginInvoke(() => ShowLibraryStatus(key, args));
        _party.NewOperators += added => NewOperatorsFound?.Invoke(added);
        _party.InnUpdated += reading => Dispatcher.BeginInvoke(() => ShowInn(reading));
        _party.ReadSquad = config.PartyOcrEnabled;
        _party.ReadInn = config.InnHelperEnabled;
        InitInnSettings();

        _winEventProc = OnWinEvent;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background,
            (_, _) => Tick(), Dispatcher);
    }

    public bool HudOn => _hudOn;
    public bool GameFound => _game != IntPtr.Zero;
    public bool AutoShow => _config.AutoShowOnGameFocus;
    public bool DebugPanel => _debugPanel;

    public void ToggleDebugPanel() => SetDebugPanel(!_debugPanel);

    /// <summary>Show or hide the debug panel until EndHud quits (never saved).</summary>
    private void SetDebugPanel(bool enabled)
    {
        _debugPanel = enabled;
        DebugPanelCheckBox.IsChecked = enabled;
        DebugRoot.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
    }

    public void ToggleHud()
    {
        _hudOn = !_hudOn;
        Refresh(reassertTopmost: true);
    }

    public void ToggleMenu() => SetMenuOpen(!_menuOpen);

    /// <summary>Check the wiki for new operators (at most every 15 minutes; see PartyMonitor).</summary>
    internal Task<OperatorCheck> CheckOperatorsAsync() => _party.CheckOperatorsAsync();

    /// <summary>The weekly background refresh found new operators (raised on a worker thread).</summary>
    internal event Action<IReadOnlyList<Vision.Operators.OperatorInfo>>? NewOperatorsFound;

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
        _party.ReadSquad = enabled;
        PartySection.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        PartyText.Text = Loc.Get("Party.Waiting");
        ApplyComboHintSettings();
        ApplyRotationSettings();
        ApplyAlertSettings();
        _rotationPosition = null;
        RenderRotation();
        ApplyGaugeSettings();
        _gauges = null;
        RenderGauges();
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

        // An always-on-top window covering the whole screen counts to Windows as a fullscreen app,
        // which keeps the taskbar behind it even after switching to another app (it's judged at the
        // switch, before the overlay hides). This property tells Explorer not to count the overlay.
        Native.SetProp(_hwnd, "NonRudeHWND", new IntPtr(1));

        if (_options.HideFromCapture)
            Native.SetWindowDisplayAffinity(_hwnd, Native.WDA_EXCLUDEFROMCAPTURE);
        // When the game's own window capture sends nothing, the game is read off the screen, which
        // must not include the HUD drawn over it: from then on the overlay stays out of captures
        // (screenshots and recordings too).
        _party.OverlayWindow = _hwnd;
        _party.ExcludeOverlayFromCapture = () => Dispatcher.Invoke(() =>
        {
            bool excluded = Native.SetWindowDisplayAffinity(_hwnd, Native.WDA_EXCLUDEFROMCAPTURE);
            if (!excluded) DiagnosticLog.Warn($"the overlay can't be kept out of screen captures (error {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}): the game isn't read from the screen");
            return excluded;
        });

        HwndSource.FromHwnd(_hwnd).AddHook(WndProc);
        const uint mods = Native.MOD_CONTROL | Native.MOD_SHIFT | Native.MOD_NOREPEAT;
        const uint ctrl = Native.MOD_CONTROL | Native.MOD_NOREPEAT;
        RegisterHotkey(HotkeyToggle, mods, VK_H, "Ctrl+Shift+H");
        RegisterHotkey(HotkeyQuit, mods, VK_Q, "Ctrl+Shift+Q");
        RegisterHotkey(HotkeyMenu, mods, VK_M, "Ctrl+Shift+M");
        RegisterHotkey(HotkeyDebug, mods, VK_D, "Ctrl+Shift+D");
        RegisterHotkey(HotkeyMenuAlt, ctrl, VK_F9, "Ctrl+F9");
        RegisterHotkey(HotkeyToggleAlt, ctrl, VK_F10, "Ctrl+F10");
        RegisterHotkey(HotkeyDebugAlt, ctrl, VK_F11, "Ctrl+F11");

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
        _skillInput.Dispose();
        foreach (var hook in _hooks) Native.UnhookWinEvent(hook);
        Native.UnregisterHotKey(_hwnd, HotkeyToggle);
        Native.UnregisterHotKey(_hwnd, HotkeyQuit);
        Native.UnregisterHotKey(_hwnd, HotkeyMenu);
        Native.UnregisterHotKey(_hwnd, HotkeyDebug);
        Native.UnregisterHotKey(_hwnd, HotkeyMenuAlt);
        Native.UnregisterHotKey(_hwnd, HotkeyToggleAlt);
        Native.UnregisterHotKey(_hwnd, HotkeyDebugAlt);
        base.OnClosed(e);
    }

    /// <summary>A global hotkey; when another program already has it, the log says so (the other key still works).</summary>
    private void RegisterHotkey(int id, uint modifiers, uint key, string name)
    {
        if (!Native.RegisterHotKey(_hwnd, id, modifiers, key))
            DiagnosticLog.Warn($"hotkey {name} is taken by another program (error {System.Runtime.InteropServices.Marshal.GetLastWin32Error()})");
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
        SetComboHintEditing(open);
        SetRotationEditing(open);
        SetAlertsEditing(open);
        SetGaugesEditing(open);
        SetInnEditing(open);
        SetPanelDragging(open);

        if (open)
        {
            RefreshOperatorPickers();
            // The game hides and pins the mouse while it has focus (in battle especially): the
            // menu must really take focus, and free the cursor, or nothing in it can be clicked or dragged.
            Native.ForceForeground(_hwnd);
            Native.ClipCursor(IntPtr.Zero);
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

    private void DebugPanelCheckBox_Click(object sender, RoutedEventArgs e) =>
        SetDebugPanel(DebugPanelCheckBox.IsChecked == true);

    /// <summary>Install newer versions by itself (when the game isn't running); read by the tray.</summary>
    public bool AutoInstallUpdates => _config.CheckForUpdates && _config.AutoInstallUpdates;

    private void AutoInstallUpdatesCheckBox_Click(object sender, RoutedEventArgs e)
    {
        _config.AutoInstallUpdates = AutoInstallUpdatesCheckBox.IsChecked == true;
        ConfigStore.Save(_config);
    }

    /// <summary>Whether EndHud starts with Windows (the registry says; see AutoStart). Off when it can't be read.</summary>
    private static bool ReadAutoStart()
    {
        try { return AutoStart.ForThisExe().Enabled; }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            DiagnosticLog.Warn($"start with Windows: couldn't read the entry ({ex.Message})");
            return false;
        }
    }

    private void AutoStartCheckBox_Click(object sender, RoutedEventArgs e)
    {
        bool wanted = AutoStartCheckBox.IsChecked == true;
        try
        {
            AutoStart.ForThisExe().Set(wanted);
            AutoStartError.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            DiagnosticLog.Error("start with Windows: couldn't change the entry", ex);
            AutoStartError.Text = Loc.Get("Menu.AutoStart.Failed");
            AutoStartError.Visibility = Visibility.Visible;
        }
        AutoStartCheckBox.IsChecked = ReadAutoStart(); // what Windows will actually do
    }

    private void CloseWithGameCheckBox_Click(object sender, RoutedEventArgs e)
    {
        _config.CloseWithGame = CloseWithGameCheckBox.IsChecked == true;
        ConfigStore.Save(_config);
    }

    private void CheckForUpdatesCheckBox_Click(object sender, RoutedEventArgs e)
    {
        _config.CheckForUpdates = CheckForUpdatesCheckBox.IsChecked == true;
        AutoInstallUpdatesCheckBox.IsEnabled = _config.CheckForUpdates;
        ConfigStore.Save(_config);
    }

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
        if (_gameExit.Update(_game != IntPtr.Zero, DateTime.UtcNow) && _config.CloseWithGame)
        {
            DiagnosticLog.Info($"the game exited (its window gone {GameExitWatch.GoneFor.TotalSeconds:F0} s): closing, as set");
            Application.Current.Shutdown();
            return;
        }

        ClockText.Text = DateTime.Now.ToString("HH:mm:ss");
        UpdateComboHints(); // timed hints end on time even when no new reading arrives
        TickInn();
        ExpireAlerts(); // a pop-up leaves after 3 s even when no new reading arrives
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
            UpdateSkillInput(gameInFront: false);
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
            UpdateSkillInput(gameInFront: fg == _game);
            show = _hudOn && !Native.IsIconic(_game) && (gameFocused || !_config.AutoShowOnGameFocus);
            StatusText.Text = Loc.Format("Hud.Status.Tracking", _gameTitle);
        }

        SizeText.Text = Loc.Format("Hud.Size", w, h, x, y);
        HudRoot.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        // The inn helper reads the game too, with the squad panel's reading off.
        _party.SetTarget(_game, show && (_config.PartyOcrEnabled || _config.InnHelperEnabled));

        // Nothing to show (the game isn't focused): take the window away altogether. Left up, an
        // always-on-top window the size of a fullscreen game makes Windows treat it as a fullscreen
        // app and keep the taskbar behind it, so the game's bottom edge shows instead of the taskbar.
        // Asked of Windows, not remembered: WPF shows the window itself once at startup.
        bool hide = !show && !_menuOpen;
        if (_hwnd != IntPtr.Zero && hide == Native.IsWindowVisible(_hwnd))
        {
            Native.ShowWindow(_hwnd, hide ? Native.SW_HIDE : Native.SW_SHOWNOACTIVATE);
            reassertTopmost |= !hide;
        }
        if (hide) return;

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
        state = WithRunningUltimates(state);
        _lastState = state;
        UpdateComboHints();
        TrackRotation(state);

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
        foreach (var slot in state.Slots.Where(s => !s.Empty))
        {
            int i = slot.Number - 1;
            string? operatorId = slot.Operator?.Operator.Id;
            // In combat the level text is replaced by the combo bar: keep showing the last level
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

            // The operator's own counters (read for the controlled one), e.g. 猎矢 4　启示 8.
            var counters = state.Counters.Where(r => r.OperatorId == operatorId).ToList();
            if (counters.Count > 0)
                sb.AppendLine(Loc.Format("Party.Counters", string.Join("　", counters.Select(r =>
                    $"{EndHud.Vision.Operators.OperatorCounters.Definitions.FirstOrDefault(d => d.OperatorId == r.OperatorId && d.Id == r.CounterId)?.NameZh ?? r.CounterId} {r.Value?.ToString() ?? unknown}"))));
        }
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
                OperatorPickersEmpty.Text = Loc.Get(identifier is not null ? "Menu.Operators.NoReading" : "Menu.Operators.NoLibrary");
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
            case HotkeyToggle or HotkeyToggleAlt:
                ToggleHud();
                handled = true;
                break;
            case HotkeyMenu or HotkeyMenuAlt:
                ToggleMenu();
                handled = true;
                break;
            case HotkeyDebug or HotkeyDebugAlt:
                ToggleDebugPanel();
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
