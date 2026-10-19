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
/// The inn helper's strip: during an inn session, which dishes to queue in each kitchen, when to
/// start Full Blaze, and what a gourmand asks for (<see cref="InnSession"/>, <see cref="InnStripView"/>).
/// Shown only on the session's frames (never in a fight), and as a sample while the menu is open out
/// of the inn. Placed by its top-left corner as a fraction of the game window, draggable while the
/// menu is open, with its own size and opacity.
/// </summary>
public partial class OverlayWindow
{
    private InnSession? _innSession;
    private OperatorLibrary? _innLibrary; // the library the session was made with
    private bool _innOnScreen; // the last reading was a session frame
    private string? _innKey;
    private Point? _innDragOffset;
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
        if (!enabled) _innSession = null; // nothing is kept while it's off
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
            _innOnScreen = false;
            _innSession?.Tick(now);
            RenderInn();
            return;
        }
        if (Session() is { } session) session.Update(reading);
        _innOnScreen = reading.InSession;
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
        }
        return _innSession;
    }

    /// <summary>The slow loop: estimates move on with time, and a session not seen for a while ends.</summary>
    private void TickInn()
    {
        _innSession?.Tick(InnClock.Now);
        if (_innOnScreen || _innKey is not null) RenderInn();
    }

    private void RenderInn()
    {
        InnStripModel? model = null;
        if (_config.InnHelperEnabled)
        {
            if (_innOnScreen && _innSession?.Advice(InnClock.Now) is { } advice)
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

    /// <summary>A dish's or drink's picture from the operator package, loaded once; null when the package has none.</summary>
    private BitmapSource? InnPicture(string id)
    {
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
        ConfigStore.Save(_config);
    }

    private void InnResetButton_Click(object sender, RoutedEventArgs e)
    {
        _config.InnStripX = HudConfig.DefaultInnStripX;
        _config.InnStripY = HudConfig.DefaultInnStripY;
        ConfigStore.Save(_config);
        PlaceInnFromConfig();
    }

    // ---- Placing the strip ----

    private void SetInnEditing(bool editing)
    {
        InnFrame.BorderBrush = editing ? (Brush)FindResource("Accent") : Brushes.Transparent;
        InnDragLabel.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        InnFrame.Cursor = editing ? Cursors.SizeAll : null;
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
