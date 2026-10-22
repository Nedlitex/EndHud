using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using EndHud.Vision.Inn;

namespace EndHud;

/// <summary>
/// The gourmands' tiles (<see cref="InnGourmandView.Tile"/>): during a session, while a gourmand's order is open, one tile
/// per gourmand with pictures as large as the kitchens' circles, placed by <see cref="InnGourmandLayout.Arrange"/> in table
/// order (on the right, clear of the Full Blaze meter, the tray and the circles; beside the dispatch panel while it's open,
/// never hidden for it), fading in when new and out when served, gliding when the layout moves them. A sample tile shows
/// in the drag frame while the menu is open out of a session, to place the columns. The inn's size and opacity apply.
/// </summary>
public partial class OverlayWindow
{
    private readonly Dictionary<string, (string Content, FrameworkElement Element, Point At)> _innGourmandTiles = [];
    private Point? _innGourmandDragOffset;
    private double _innGourmandScale = 1;
    private static readonly TimeSpan InnGourmandMove = TimeSpan.FromMilliseconds(150), InnGourmandFade = TimeSpan.FromMilliseconds(200);

    /// <summary>The gourmands' tiles up now (the overlay's tick keeps drawing while any is).</summary>
    private int InnGourmandCount => _innGourmandTiles.Count;

    /// <summary>
    /// Puts up the tiles for <paramref name="views"/>' gourmands (none: all fade out). A tile already up stays as it is; when
    /// what it says or its shape changes it's drawn again in place, and when the layout moves it (a gourmand coming or
    /// going, the panel opening or closing) it glides to its new place.
    /// </summary>
    private void RenderInnGourmands(InnViews views)
    {
        var banners = _config.InnHelperEnabled ? InnGourmandView.Banners(views.Wants, Loc.Get).ToList() : [];
        RenderInnGourmandSample(_menuOpen && _config.InnHelperEnabled && banners.Count == 0);

        double width = HintCanvas.ActualWidth, height = HintCanvas.ActualHeight;
        bool panelOpen = views.Session && views.Away;
        var circles = views.Circles == InnCirclePlace.BelowPanel && views.Session
            ? InnKitchenCircleLayout.BelowPanel(width, height, _innCircleScale)
            : InnKitchenCircleLayout.Place(width, height, _innCircleScale, _config.InnCircleX, _config.InnCircleY);
        var arrangement = InnGourmandLayout.Arrange(banners, width, height, InnScale(), _config.InnGourmandRight, _config.InnGourmandTop, panelOpen, circles);

        foreach (var (key, gone) in _innGourmandTiles.Where(t => banners.All(b => b.Key != t.Key)).ToList())
        {
            _innGourmandTiles.Remove(key);
            var fade = new DoubleAnimation(0, InnGourmandFade);
            fade.Completed += (_, _) => InnGourmandTiles.Children.Remove(gone.Element);
            gone.Element.BeginAnimation(UIElement.OpacityProperty, fade);
        }
        for (int i = 0; i < banners.Count; i++)
        {
            var model = banners[i];
            double s = arrangement.Scale;
            // A tile of one request is drawn at its slot's right.
            var at = new Point(arrangement.Places[i].X + (InnGourmandView.Width(arrangement.Shape) - InnGourmandView.Width(arrangement.Shape, model.Rows.Count)) * s,
                arrangement.Places[i].Y);
            string content = $"{model.Content}|{arrangement.Shape}|{s:F3}";
            if (_innGourmandTiles.TryGetValue(model.Key, out var up))
            {
                var element = up.Element;
                if (up.Content != content)
                {
                    InnGourmandTiles.Children.Remove(element);
                    element = NewInnGourmandTile(model, arrangement.Shape, s, up.At);
                    element.Opacity = 1;
                }
                if (up.At != at)
                {
                    element.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(at.X, InnGourmandMove));
                    element.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(at.Y, InnGourmandMove));
                }
                _innGourmandTiles[model.Key] = (content, element, at);
                continue;
            }
            var tile = NewInnGourmandTile(model, arrangement.Shape, s, at);
            tile.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, InnGourmandFade));
            _innGourmandTiles[model.Key] = (content, tile, at);
        }
    }

    private FrameworkElement NewInnGourmandTile(InnGourmandBanner model, InnGourmandShape shape, double scale, Point at)
    {
        var tile = InnGourmandView.Tile(model, InnPicture, shape);
        tile.LayoutTransform = new ScaleTransform(scale, scale);
        Canvas.SetLeft(tile, at.X);
        Canvas.SetTop(tile, at.Y);
        InnGourmandTiles.Children.Add(tile);
        return tile;
    }

    /// <summary>The sample tile in the drag frame while the menu is open out of a session (or none).</summary>
    private void RenderInnGourmandSample(bool show)
    {
        if (!show)
        {
            InnGourmandStack.Children.Clear();
            InnGourmandStack.Width = InnGourmandStack.Height = 0;
            return;
        }
        if (InnGourmandStack.Children.Count > 0) return;
        var sample = InnGourmandView.Tile(InnGourmandView.Sample(_party.Identifier?.Library.Inn, Loc.Get), InnPicture, InnGourmandShape.Full);
        InnGourmandStack.Children.Add(sample);
        InnGourmandStack.Width = InnGourmandView.Width(InnGourmandShape.Full);
        InnGourmandStack.Height = InnGourmandView.Height(InnGourmandShape.Full, InnGourmandView.MostRequests);
    }

    private void SetInnGourmandEditing(bool editing)
    {
        InnGourmandFrame.BorderBrush = editing ? (Brush)FindResource("Accent") : Brushes.Transparent;
        InnGourmandDragLabel.Visibility = editing && _config.InnHelperEnabled ? Visibility.Visible : Visibility.Collapsed;
        InnGourmandFrame.Cursor = editing ? Cursors.SizeAll : null;
    }

    /// <param name="size">The game window's scale (ComboHintLayout) times the player's size; the tiles may be drawn smaller (<see cref="InnGourmandLayout.Arrange"/>).</param>
    private void ScaleInnGourmands(double size)
    {
        _innGourmandScale = size;
        InnGourmandFrame.LayoutTransform = new ScaleTransform(size, size);
        InnGourmandStack.Opacity = _config.InnStripOpacity;
        InnGourmandTiles.Opacity = _config.InnStripOpacity;
        PlaceInnGourmandsFromConfig();
    }

    private void PlaceInnGourmandsFromConfig()
    {
        var (fromRight, top) = InnGourmandLayout.Place(HintCanvas.ActualWidth, HintCanvas.ActualHeight, _config.InnGourmandRight, _config.InnGourmandTop);
        Canvas.SetRight(InnGourmandFrame, fromRight);
        Canvas.SetTop(InnGourmandFrame, top);
    }

    private void InnGourmand_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_menuOpen) return;
        // From the frame's top-right corner: the stack is placed by its right edge.
        var at = e.GetPosition(HintCanvas);
        _innGourmandDragOffset = new Point(HintCanvas.ActualWidth - Canvas.GetRight(InnGourmandFrame) - at.X, at.Y - Canvas.GetTop(InnGourmandFrame));
        InnGourmandFrame.CaptureMouse();
        e.Handled = true;
    }

    private void InnGourmand_MouseMove(object sender, MouseEventArgs e)
    {
        if (_innGourmandDragOffset is not { } offset || HintCanvas.ActualWidth <= 0 || HintCanvas.ActualHeight <= 0) return;
        var at = e.GetPosition(HintCanvas);
        var (fromRight, top) = InnGourmandLayout.Place(HintCanvas.ActualWidth, HintCanvas.ActualHeight,
            (at.X + offset.X) / HintCanvas.ActualWidth, (at.Y - offset.Y) / HintCanvas.ActualHeight);
        Canvas.SetRight(InnGourmandFrame, fromRight);
        Canvas.SetTop(InnGourmandFrame, top);
    }

    private void InnGourmand_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_innGourmandDragOffset is null) return;
        _innGourmandDragOffset = null;
        InnGourmandFrame.ReleaseMouseCapture();
        if (HintCanvas.ActualWidth <= 0 || HintCanvas.ActualHeight <= 0) return;
        _config.InnGourmandRight = 1 - Canvas.GetRight(InnGourmandFrame) / HintCanvas.ActualWidth;
        _config.InnGourmandTop = Canvas.GetTop(InnGourmandFrame) / HintCanvas.ActualHeight;
        ConfigStore.Save(_config);
        // Where the banners go around the Full Blaze meter follows the stack's place.
        RenderInn();
    }
}
