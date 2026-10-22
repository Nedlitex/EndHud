using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace EndHud;

/// <summary>
/// The kitchens' circles (<see cref="InnKitchenCircleView"/>): during a session, each kitchen's call as a big circle in
/// its own column above the held-dishes tray, kept as it is while the call stands (<see cref="InnCircleRow"/>), put away
/// in place while the dispatch panel is open (its marks take over). With the banners' setting (InnCallsEnabled), the inn's
/// size and opacity; placed by <see cref="InnKitchenCircleLayout"/>, draggable while the menu is open (three sample
/// circles show then, out of a session).
/// </summary>
public partial class OverlayWindow
{
    private InnCircleRow? _innCircleRow;
    private Point? _innCircleDragOffset;
    private double _innCircleScale = 1;

    /// <summary>The circles drawn into <c>InnCircleSlots</c> (made on first use: the XAML's elements exist by then).</summary>
    private InnCircleRow InnCircles => _innCircleRow ??= new InnCircleRow(InnCircleRow.Shape(InnCircleSlots), model => InnKitchenCircleView.Circle(model, InnPicture));

    private void RenderInnCircles(InnViews views)
    {
        bool on = _config.InnHelperEnabled && _config.InnCallsEnabled;
        var slots = on && views.Session ? InnKitchenCircleView.Of(views.Calls, Loc.Get) : [];
        // Something to drag while placing them.
        if (_menuOpen && on && slots.All(s => s is null)) slots = InnKitchenCircleView.Sample(_party.Identifier?.Library.Inn, Loc.Get);
        InnCircleFrame.Visibility = views.Circles == InnCirclePlace.Hidden && views.Session && !_menuOpen ? Visibility.Hidden : Visibility.Visible;
        InnCircles.Update(slots);
    }

    private void SetInnCircleEditing(bool editing)
    {
        InnCircleFrame.BorderBrush = editing ? (Brush)FindResource("Accent") : Brushes.Transparent;
        InnCircleDragLabel.Visibility = editing && _config.InnHelperEnabled && _config.InnCallsEnabled ? Visibility.Visible : Visibility.Collapsed;
        InnCircleFrame.Cursor = editing ? Cursors.SizeAll : null;
    }

    /// <param name="size">The game window's scale (ComboHintLayout) times the player's size.</param>
    private void ScaleInnCircles(double size)
    {
        _innCircleScale = size;
        InnCircleFrame.LayoutTransform = new ScaleTransform(size, size);
        InnCircleSlots.Opacity = _config.InnStripOpacity;
        PlaceInnCirclesFromConfig();
    }

    private void PlaceInnCirclesFromConfig()
    {
        var place = InnKitchenCircleLayout.Place(HintCanvas.ActualWidth, HintCanvas.ActualHeight, _innCircleScale, _config.InnCircleX, _config.InnCircleY);
        Canvas.SetLeft(InnCircleFrame, place.Left);
        Canvas.SetTop(InnCircleFrame, place.Top);
    }

    private void InnCircle_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_menuOpen) return;
        var at = e.GetPosition(HintCanvas);
        _innCircleDragOffset = new Point(at.X - Canvas.GetLeft(InnCircleFrame), at.Y - Canvas.GetTop(InnCircleFrame));
        InnCircleFrame.CaptureMouse();
        e.Handled = true;
    }

    private void InnCircle_MouseMove(object sender, MouseEventArgs e)
    {
        if (_innCircleDragOffset is not { } offset) return;
        var at = e.GetPosition(HintCanvas);
        var size = InnKitchenCircleLayout.Size(_innCircleScale);
        Canvas.SetLeft(InnCircleFrame, Math.Clamp(at.X - offset.X, 0, Math.Max(0, HintCanvas.ActualWidth - size.Width)));
        Canvas.SetTop(InnCircleFrame, Math.Clamp(at.Y - offset.Y, 0, Math.Max(0, HintCanvas.ActualHeight - size.Height)));
    }

    private void InnCircle_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_innCircleDragOffset is null) return;
        _innCircleDragOffset = null;
        InnCircleFrame.ReleaseMouseCapture();
        if (HintCanvas.ActualWidth <= 0 || HintCanvas.ActualHeight <= 0) return;
        var placed = new Rect(new Point(Canvas.GetLeft(InnCircleFrame), Canvas.GetTop(InnCircleFrame)), InnKitchenCircleLayout.Size(_innCircleScale));
        (_config.InnCircleX, _config.InnCircleY) = InnKitchenCircleLayout.Fractions(placed, HintCanvas.ActualWidth, HintCanvas.ActualHeight);
        ConfigStore.Save(_config);
    }
}
