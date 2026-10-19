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
/// The inn helper's two overlays. The strip: during an inn session, which dishes to queue in each
/// kitchen, when to start Full Blaze, and what a gourmand asks for (<see cref="InnSession"/>,
/// <see cref="InnStripView"/>), shown only on the session's frames (never in a fight). The setup
/// panel: on the setup, chef picker and menu screens, tier lists of the chefs for each kitchen and
/// the dishes and drinks for the menu, from the built-in roster and dish list
/// (<see cref="InnSetupPlanning"/>, <see cref="InnSetupView"/>), worked out off the UI thread. Both are text only, shown as samples while the menu is open out of
/// the inn, placed by their top-left corners as fractions of the game window, draggable while the
/// menu is open, and share one size and opacity.
/// </summary>
public partial class OverlayWindow
{
    private InnSession? _innSession;
    private OperatorLibrary? _innLibrary; // the library the session was made with
    private bool _innOnScreen; // the last reading was a session frame
    private bool _innSetupOnScreen; // the last reading was a setup, chef picker or menu screen
    private string? _innKey, _innSetupKey;
    private InnSetupPlanning? _innSetupPlanning;
    private Point? _innDragOffset, _innSetupDragOffset;
    private double _innWindowScale = 1;
    private bool _loadingInnSettings = true;
    private readonly Dictionary<string, BitmapSource?> _innPictures = [];

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
            _innSetupPlanning = null;
        }
        ApplyInnSettings();
        RenderInn();
        Refresh(reassertTopmost: false);
    }

    private void InitInnSettings()
    {
        _loadingInnSettings = true;
        InnCheckBox.IsChecked = _config.InnHelperEnabled;
        InnSizeSlider.Value = _config.InnStripScale;
        InnSizeText.Text = Loc.Format("Menu.Gauges.SizeValue", _config.InnStripScale * 100);
        InnOpacitySlider.Value = _config.InnStripOpacity;
        InnOpacityText.Text = Loc.Format("Menu.Gauges.SizeValue", _config.InnStripOpacity * 100);
        InnContent.Opacity = _config.InnStripOpacity;
        InnSetupContent.Opacity = _config.InnStripOpacity;
        _loadingInnSettings = false;
        ApplyInnSettings();
    }

    private void ApplyInnSettings()
    {
        InnResetButton.IsEnabled = _config.InnHelperEnabled;
        InnSizeRow.IsEnabled = _config.InnHelperEnabled;
        InnOpacityRow.IsEnabled = _config.InnHelperEnabled;
    }

    /// <summary>A reading of the inn (null: a frame that isn't the inn), on the UI thread.</summary>
    private void ShowInn(InnReading? reading)
    {
        var now = InnClock.Now;
        if (reading is null)
        {
            _innOnScreen = _innSetupOnScreen = false;
            _innSession?.Tick(now);
            RenderInn();
            return;
        }
        if (Session() is { } session) session.Update(reading);
        _innOnScreen = reading.InSession;
        _innSetupOnScreen = reading.OnSetupScreens;
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
            _innSession = new InnSession(data, library.Operators);
            var planner = new InnPlanner(data);
            _innSetupPlanning = new InnSetupPlanning(input => InnSetupAdvice.For(planner, input));
        }
        return _innSession;
    }

    /// <summary>The slow loop: estimates move on with time, and a session not seen for a while ends.</summary>
    private void TickInn()
    {
        _innSession?.Tick(InnClock.Now);
        if (_innOnScreen || _innSetupOnScreen || _innKey is not null || _innSetupKey is not null) RenderInn();
    }

    private void RenderInn()
    {
        RenderInnSetup();
        InnStripModel? model = null;
        if (_config.InnHelperEnabled)
        {
            if (_innOnScreen && AdviceNow() is { } advice)
                model = InnStripView.Build(advice, Loc.Get, _party.InnProblem);
            else if (_menuOpen && !_innOnScreen)
                // Something to drag, size and see through while the menu is open, out of the inn.
                model = InnStripView.Sample(_party.Identifier?.Library.Inn, Loc.Get);
        }
        string? key = model?.Key;
        if (key == _innKey) return;
        _innKey = key;
        InnContent.Content = model is null ? null : InnStrip.Build(model, InnPicture);
        InnFrame.Visibility = model is null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// The setup panel: on the setup screens, the plan for what they showed (asked for off the UI
    /// thread when the inputs change; drawn again when it's ready), else what's missing. A sample
    /// while the menu is open out of the inn.
    /// </summary>
    private void RenderInnSetup()
    {
        InnStripModel? model = null;
        if (_config.InnHelperEnabled)
        {
            if (_innSetupOnScreen && _innSession is { } session && _innSetupPlanning is { } planning && _innLibrary?.Inn is { } inn)
            {
                var inputs = session.SetupInputs();
                planning.Request(inputs, () => Dispatcher.BeginInvoke(RenderInnSetup));
                model = InnSetupView.Build(inputs, planning.Showable(inputs), inn, Loc.Get, _party.InnProblem);
            }
            else if (_menuOpen && !_innOnScreen && !_innSetupOnScreen)
                model = InnSetupView.Sample(_party.Identifier?.Library.Inn, _party.Identifier?.Library.Operators ?? [], Loc.Get);
        }
        string? key = model?.Key;
        if (key == _innSetupKey) return;
        _innSetupKey = key;
        InnSetupContent.Content = model is null ? null : InnStrip.Build(model, InnPicture);
        InnSetupFrame.Visibility = model is null ? Visibility.Collapsed : Visibility.Visible;
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
        // An operator's avatar (the setup panel's chefs), as the combo banner shows it.
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
        InnSetupContent.Opacity = _config.InnStripOpacity;
        ConfigStore.Save(_config);
    }

    private void InnResetButton_Click(object sender, RoutedEventArgs e)
    {
        _config.InnStripX = HudConfig.DefaultInnStripX;
        _config.InnStripY = HudConfig.DefaultInnStripY;
        _config.InnSetupX = HudConfig.DefaultInnSetupX;
        _config.InnSetupBottomY = HudConfig.DefaultInnSetupBottomY;
        ConfigStore.Save(_config);
        PlaceInnFromConfig();
        PlaceInnSetupFromConfig();
    }

    // ---- Placing the strip ----

    private void SetInnEditing(bool editing)
    {
        InnFrame.BorderBrush = editing ? (Brush)FindResource("Accent") : Brushes.Transparent;
        InnDragLabel.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        InnFrame.Cursor = editing ? Cursors.SizeAll : null;
        InnSetupFrame.BorderBrush = InnFrame.BorderBrush;
        InnSetupDragLabel.Visibility = InnDragLabel.Visibility;
        InnSetupFrame.Cursor = InnFrame.Cursor;
        _innKey = "";
        _innSetupKey = "";
        RenderInn();
    }

    /// <param name="scale">Following the game window (see ComboHintLayout); the player's size is applied on top.</param>
    private void ScaleInnStrip(double scale)
    {
        _innWindowScale = scale;
        double size = scale * _config.InnStripScale;
        InnFrame.LayoutTransform = new ScaleTransform(size, size);
        InnSetupFrame.LayoutTransform = new ScaleTransform(size, size);
        PlaceInnFromConfig();
        PlaceInnSetupFromConfig();
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

    // ---- Placing the setup panel ----

    private void PlaceInnSetupFromConfig() =>
        PlaceInnSetup(_config.InnSetupX * HintCanvas.ActualWidth, _config.InnSetupBottomY * HintCanvas.ActualHeight - InnSetupHeight());

    /// <summary>The panel's height on screen (after scaling).</summary>
    private double InnSetupHeight() => InnSetupFrame.ActualHeight * ((InnSetupFrame.LayoutTransform as ScaleTransform)?.ScaleY ?? 1);

    /// <summary>The panel grew or shrank (a new plan): keep its bottom where it was.</summary>
    private void InnSetupFrame_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_innSetupDragOffset is null) PlaceInnSetupFromConfig();
    }

    private void PlaceInnSetup(double left, double top)
    {
        (left, top) = ComboHintLayout.Place(left, top, HintCanvas.ActualWidth, HintCanvas.ActualHeight);
        Canvas.SetLeft(InnSetupFrame, left);
        Canvas.SetTop(InnSetupFrame, top);
    }

    private void InnSetup_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_menuOpen) return;
        _innSetupDragOffset = e.GetPosition(InnSetupFrame);
        InnSetupFrame.CaptureMouse();
        e.Handled = true;
    }

    private void InnSetup_MouseMove(object sender, MouseEventArgs e)
    {
        if (_innSetupDragOffset is not { } offset) return;
        var at = e.GetPosition(HintCanvas);
        PlaceInnSetup(at.X - offset.X, at.Y - offset.Y);
    }

    private void InnSetup_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_innSetupDragOffset is null) return;
        _innSetupDragOffset = null;
        InnSetupFrame.ReleaseMouseCapture();
        if (HintCanvas.ActualWidth <= 0 || HintCanvas.ActualHeight <= 0) return;
        _config.InnSetupX = Canvas.GetLeft(InnSetupFrame) / HintCanvas.ActualWidth;
        _config.InnSetupBottomY = (Canvas.GetTop(InnSetupFrame) + InnSetupHeight()) / HintCanvas.ActualHeight;
        ConfigStore.Save(_config);
    }
}
