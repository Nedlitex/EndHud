using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EndHud.Vision.Inn;
using EndHud.Vision.Operators;

namespace EndHud;

/// <summary>
/// The inn helper's overlays. The strip: during an inn session, which dishes to queue in each
/// kitchen, when to start Full Blaze, and what a gourmand asks for (<see cref="InnSession"/>,
/// <see cref="InnStripView"/>), shown only on the session's frames (never in a fight); placed by its
/// top-left corner as a fraction of the game window, shown as a sample and draggable while the menu
/// is open out of the inn. The setup advice: small panels where the game shows the choice they're
/// about (<see cref="InnSetupView"/>, placed by <see cref="InnAdviceLayout"/>): under each kitchen's
/// card on the setup screen, in a chef picker, on the menu screen; nothing on the theme list. Their plans are worked
/// out off the UI thread (<see cref="InnSetupPlanning"/>), one for the chefs and one for the menu.
/// They follow the game's layout, so they aren't dragged. All share one size and opacity; text only.
/// </summary>
public partial class OverlayWindow
{
    private InnSession? _innSession;
    private OperatorLibrary? _innLibrary; // the library the session was made with
    private bool _innOnScreen; // the last reading was a session frame
    private InnScreenKind? _innSetupScreen; // the last reading's screen when it was the setup screen, a chef picker or the menu screen
    private string? _innKey, _innSetupKey;
    private InnSetupPlanning? _innChefPlanning, _innMenuPlanning;
    private readonly Dictionary<InnAdviceSpot, Border> _innAdviceFrames = [];
    private Point? _innDragOffset;
    private double _innWindowScale = 1;
    private bool _loadingInnSettings = true;
    private readonly Dictionary<string, BitmapSource?> _innPictures = [];
    private bool _innProblemSaid;

    /// <summary>
    /// The inn was seen while it can only be partly read (<see cref="PartyMonitor.InnProblem"/>: the Simplified Chinese
    /// OCR language missing), once per run, with the sentence saying what to install. The tray shows it; no panel
    /// over the game says it (owner, live 2026-10-20: lines over the game about what can't be seen are "disturbing").
    /// </summary>
    public event Action<string>? InnProblemFound;

    /// <summary>The inn helper's setting, for the tray.</summary>
    public bool InnHelperEnabled => _config.InnHelperEnabled;

    /// <summary>Turn the inn helper on or off (the menu's card, the tray), saved.</summary>
    public void SetInnHelper(bool enabled)
    {
        _config.InnHelperEnabled = enabled;
        ConfigStore.Save(_config);
        InnCheckBox.IsChecked = enabled;
        _party.ReadInn = enabled;
        if (!enabled)
        {
            // Nothing is kept while it's off.
            _innSession = null;
            _innChefPlanning = _innMenuPlanning = null;
        }
        ApplyInnSettings();
        RenderInn();
        Refresh(reassertTopmost: false);
    }

    private void InitInnSettings()
    {
        _loadingInnSettings = true;
        InnCheckBox.IsChecked = _config.InnHelperEnabled;
        InnCallsCheckBox.IsChecked = _config.InnCallsEnabled;
        InnSizeSlider.Value = _config.InnStripScale;
        InnSizeText.Text = Loc.Format("Menu.Gauges.SizeValue", _config.InnStripScale * 100);
        InnOpacitySlider.Value = _config.InnStripOpacity;
        InnOpacityText.Text = Loc.Format("Menu.Gauges.SizeValue", _config.InnStripOpacity * 100);
        InnContent.Opacity = _config.InnStripOpacity;
        _loadingInnSettings = false;
        ApplyInnSettings();
    }

    private void ApplyInnSettings()
    {
        InnResetButton.IsEnabled = _config.InnHelperEnabled;
        InnCallsCheckBox.IsEnabled = _config.InnHelperEnabled;
        InnSizeRow.IsEnabled = _config.InnHelperEnabled;
        InnOpacityRow.IsEnabled = _config.InnHelperEnabled;
    }

    /// <summary>A reading of the inn (null: a frame that isn't the inn), on the UI thread.</summary>
    private void ShowInn(InnReading? reading)
    {
        var now = InnClock.Now;
        if (reading is null)
        {
            _innOnScreen = false;
            _innSetupScreen = null;
            _innSession?.Tick(now);
            RenderInn();
            return;
        }
        if (_party.InnProblem is { } problem && !_innProblemSaid)
        {
            _innProblemSaid = true;
            DiagnosticLog.Warn($"inn: on an inn screen, but it can only be partly read ({problem}); the tray says what to install");
            InnProblemFound?.Invoke(Loc.Get(problem));
        }
        if (Session() is { } session) session.Update(reading);
        _innOnScreen = reading.InSession;
        _innSetupScreen = reading.OnSetupScreens ? reading.Screen : null;
        RenderInn();
    }

    /// <summary>The session's memory, made once the operator library (with the inn's data) is loaded; anew when the library is reloaded.</summary>
    private InnSession? Session()
    {
        if (!_config.InnHelperEnabled || _party.Identifier?.Library is not { Inn: { } data } library) return null;
        if (_innSession is null || !ReferenceEquals(library, _innLibrary))
        {
            _innLibrary = library;
            _innPictures.Clear();
            _innSession = new InnSession(data, library.Operators, _party.Reading.At);
            var planner = new InnPlanner(data);
            // The model's advice first (sub-second), the simulator's after (seconds) where it differs.
            // A plan on screen stays until it no longer fits or another is clearly better (InnPlanJudge).
            var judge = new InnPlanJudge(planner);
            _innChefPlanning = new InnSetupPlanning(judge, input => InnSetupAdvice.Fast(planner, input), (input, cancel) => InnSetupAdvice.For(planner, input, cancel));
            _innMenuPlanning = new InnSetupPlanning(judge, input => InnSetupAdvice.Fast(planner, input), (input, cancel) => InnSetupAdvice.For(planner, input, cancel));
        }
        return _innSession;
    }

    /// <summary>The slow loop: estimates move on with time, and a session not seen for a while ends.</summary>
    private void TickInn()
    {
        _innSession?.Tick(InnClock.Now);
        if (_innOnScreen || _innSetupScreen is not null || _innKey is not null || _innSetupKey is not null) RenderInn();
    }

    private void RenderInn()
    {
        RenderInnSetup();
        InnStripModel? model = null;
        IReadOnlyList<InnCall> calls = [];
        IReadOnlyList<InnPanelMark> marks = [];
        if (_config.InnHelperEnabled)
        {
            if (_innOnScreen && AdviceNow() is { } advice)
            {
                // The panel: the gourmands only; empty, it's put away.
                model = InnStripView.Build(advice, Loc.Get) is { Lines.Count: > 0 } built ? built : null;
                // The banners, except over the open dispatch panel, where its marks take over.
                calls = advice.BannerCalls;
                if (_config.InnCallsEnabled) marks = InnPanelMarks.Of(advice.Panel, advice.Calls, Loc.Get);
            }
            else if (_menuOpen && !_innOnScreen)
                // Something to drag, size and see through while the menu is open, out of the inn.
                model = InnStripView.Sample(_party.Identifier?.Library.Inn, Loc.Get);
        }
        RenderInnCalls(calls);
        RenderInnMarks(marks);
        string? key = model?.Key;
        if (key == _innKey) return;
        _innKey = key;
        InnContent.Content = model is null ? null : InnStrip.Build(model, InnPicture, InnStrip.LineWidth, InnStripLook.Gourmands);
        InnFrame.Visibility = model is null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// The setup advice: on the setup screen, a chef picker and the menu screen, the panels for that screen, from the
    /// plans for what's remembered of the setup (asked for off the UI thread when their inputs
    /// change; drawn again when ready); nothing on any other screen.
    /// </summary>
    private void RenderInnSetup()
    {
        IReadOnlyList<InnAdvicePanel> panels = [];
        if (_config.InnHelperEnabled && _innSetupScreen is { } screen && _innSession is { } session
            && _innChefPlanning is { } chefPlanning && _innMenuPlanning is { } menuPlanning && _innLibrary?.Inn is { } inn)
        {
            InnSetupAdvice? chefs = null, menu = null;
            if (screen == InnScreenKind.Menu)
            {
                var inputs = session.MenuInputs();
                menuPlanning.Request(inputs, InnClock.Now, () => Dispatcher.BeginInvoke(RenderInnSetup));
                menu = menuPlanning.Showable(inputs);
            }
            else
            {
                var inputs = session.SetupInputs();
                chefPlanning.Request(inputs, InnClock.Now, () => Dispatcher.BeginInvoke(RenderInnSetup));
                chefs = chefPlanning.Showable(inputs);
            }
            panels = InnSetupView.ForScreen(screen, session, chefs, menu, inn, Loc.Get);
        }
        // Drawn again when a panel's words change, or the window's size or the player's size does (the lines' width follows them).
        string key = $"{HintCanvas.ActualWidth:F0}x{HintCanvas.ActualHeight:F0}@{InnScale():F3}\n"
                     + string.Join("\n", panels.Select(p => $"{p.Spot}:{p.Model.Key}"));
        if (key == _innSetupKey) return;
        _innSetupKey = panels.Count == 0 ? null : key;
        foreach (var frame in _innAdviceFrames.Values) frame.Visibility = Visibility.Collapsed;
        foreach (var panel in panels) ShowAdvice(panel);
    }

    /// <summary>The size the inn's overlays are drawn at: the game window's, times the player's.</summary>
    private double InnScale() => _innWindowScale * _config.InnStripScale;

    /// <summary>Draws one advice panel in its spot, its lines no wider than the spot.</summary>
    private void ShowAdvice(InnAdvicePanel panel)
    {
        if (!_innAdviceFrames.TryGetValue(panel.Spot, out var frame))
        {
            // Never in the way of the game's own clicks.
            frame = new Border { IsHitTestVisible = false };
            HintCanvas.Children.Add(frame);
            _innAdviceFrames[panel.Spot] = frame;
        }
        double width = HintCanvas.ActualWidth, height = HintCanvas.ActualHeight, scale = InnScale();
        var spot = InnAdviceLayout.Place(panel.Spot, width, height);
        frame.LayoutTransform = new ScaleTransform(scale, scale);
        frame.Opacity = _config.InnStripOpacity;
        frame.Child = InnStrip.Build(panel.Model, InnPicture, InnAdviceLayout.LineWidth(panel.Spot, width, height, scale), InnAdviceLayout.Look(panel.Spot));
        Canvas.SetLeft(frame, spot.Left);
        Canvas.SetTop(frame, spot.Top);
        frame.Visibility = Visibility.Visible;
    }

    /// <summary>The session's advice now; null (and logged) if working it out failed, so the overlay carries on.</summary>
    private InnAdvice? AdviceNow()
    {
        try
        {
            return _innSession?.Advice(InnClock.Now);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException)
        {
            DiagnosticLog.Throttled("inn-advice-" + ex.GetType().Name, TimeSpan.FromSeconds(30), $"inn: working out the advice failed: {ex}", "ERROR");
            return null;
        }
    }

    /// <summary>A dish's or drink's picture from the operator package, loaded once; null when the package has none.</summary>
    private BitmapSource? InnPicture(string id)
    {
        // An operator's avatar (the setup advice's chefs), as the combo banner shows it.
        if (id.StartsWith(InnSetupView.OperatorPicture, StringComparison.Ordinal))
        {
            string opId = id[InnSetupView.OperatorPicture.Length..];
            return _party.Identifier?.Library.Operators.FirstOrDefault(o => o.Id == opId) is { } op ? Avatar(op) : null;
        }
        if (_innPictures.TryGetValue(id, out var cached)) return cached;
        BitmapSource? picture = null;
        string path = InnData.PicturePath(PartyMonitor.ReferenceFolder, id);
        try
        {
            if (File.Exists(path))
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(path);
                image.EndInit();
                image.Freeze();
                picture = image;
            }
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException)
        {
            DiagnosticLog.Warn($"inn: couldn't load the picture of {id}: {ex.Message}");
        }
        return _innPictures[id] = picture;
    }

    // ---- Menu ----

    private void InnCheckBox_Click(object sender, RoutedEventArgs e) => SetInnHelper(InnCheckBox.IsChecked == true);

    private void InnSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loadingInnSettings || InnSizeText is null) return;
        _config.InnStripScale = Math.Clamp(Math.Round(e.NewValue, 2), HudConfig.MinInnStripScale, HudConfig.MaxInnStripScale);
        InnSizeText.Text = Loc.Format("Menu.Gauges.SizeValue", _config.InnStripScale * 100);
        ConfigStore.Save(_config);
        ScaleInnStrip(_innWindowScale);
    }

    private void InnOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loadingInnSettings || InnOpacityText is null) return;
        _config.InnStripOpacity = Math.Clamp(Math.Round(e.NewValue, 2), HudConfig.MinInnStripOpacity, HudConfig.MaxInnStripOpacity);
        InnOpacityText.Text = Loc.Format("Menu.Gauges.SizeValue", _config.InnStripOpacity * 100);
        InnContent.Opacity = _config.InnStripOpacity;
        InnCallStack.Opacity = _config.InnStripOpacity;
        foreach (var frame in _innAdviceFrames.Values) frame.Opacity = _config.InnStripOpacity;
        foreach (var mark in _innMarks) mark.Opacity = _config.InnStripOpacity;
        ConfigStore.Save(_config);
    }

    private void InnResetButton_Click(object sender, RoutedEventArgs e)
    {
        _config.InnStripX = HudConfig.DefaultInnStripX;
        _config.InnStripY = HudConfig.DefaultInnStripY;
        _config.InnCallX = HudConfig.DefaultComboHintX;
        _config.InnCallY = HudConfig.DefaultComboHintY;
        ConfigStore.Save(_config);
        PlaceInnFromConfig();
        PlaceInnCallsFromConfig();
    }

    // ---- Placing the strip ----

    private void SetInnEditing(bool editing)
    {
        InnFrame.BorderBrush = editing ? (Brush)FindResource("Accent") : Brushes.Transparent;
        InnDragLabel.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        InnFrame.Cursor = editing ? Cursors.SizeAll : null;
        SetInnCallEditing(editing);
        _innKey = "";
        RenderInn();
    }

    /// <param name="scale">Following the game window (see ComboHintLayout); the player's size is applied on top.</param>
    private void ScaleInnStrip(double scale)
    {
        _innWindowScale = scale;
        double size = scale * _config.InnStripScale;
        InnFrame.LayoutTransform = new ScaleTransform(size, size);
        PlaceInnFromConfig();
        ScaleInnCalls(size);
        // The advice panels follow the game's layout and the size: drawn again.
        RenderInnSetup();
    }

    private void PlaceInnFromConfig() => PlaceInn(_config.InnStripX * HintCanvas.ActualWidth, _config.InnStripY * HintCanvas.ActualHeight);

    private void PlaceInn(double left, double top)
    {
        (left, top) = ComboHintLayout.Place(left, top, HintCanvas.ActualWidth, HintCanvas.ActualHeight);
        Canvas.SetLeft(InnFrame, left);
        Canvas.SetTop(InnFrame, top);
    }

    private void Inn_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_menuOpen) return;
        _innDragOffset = e.GetPosition(InnFrame);
        InnFrame.CaptureMouse();
        e.Handled = true;
    }

    private void Inn_MouseMove(object sender, MouseEventArgs e)
    {
        if (_innDragOffset is not { } offset) return;
        var at = e.GetPosition(HintCanvas);
        PlaceInn(at.X - offset.X, at.Y - offset.Y);
    }

    private void Inn_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_innDragOffset is null) return;
        _innDragOffset = null;
        InnFrame.ReleaseMouseCapture();
        if (HintCanvas.ActualWidth <= 0 || HintCanvas.ActualHeight <= 0) return;
        _config.InnStripX = Canvas.GetLeft(InnFrame) / HintCanvas.ActualWidth;
        _config.InnStripY = Canvas.GetTop(InnFrame) / HintCanvas.ActualHeight;
        ConfigStore.Save(_config);
    }
}
