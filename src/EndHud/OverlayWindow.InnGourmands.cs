using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace EndHud;

/// <summary>
/// The gourmands' banners (<see cref="InnGourmandView"/>): during a session, while a gourmand's order is open, one
/// banner per gourmand in its own stack on the right, by table, placed by <see cref="InnGourmandLayout.Arrange"/> (up to
/// six: compact when many, clear of the Full Blaze meter), sliding in when new and out when served, put away while the
/// dispatch panel is open. A sample shows while the menu is open out of a session, to drag the stack. The inn's size and
/// opacity apply.
/// </summary>
public partial class OverlayWindow
{
    private readonly List<(string Key, string Content, Border Element, Grid Holder, double Top)> _innGourmandBanners = [];
    private Point? _innGourmandDragOffset;
    private double _innGourmandScale = double.NaN;
    private static readonly TimeSpan InnGourmandResize = TimeSpan.FromMilliseconds(150);

    /// <summary>
    /// Puts up the banners for <paramref name="wants"/> (none: all slide out). A gourmand's banner already up stays as it
    /// is; when what it says changes (a request read later, another item clearly sooner, the stack turning compact) it's
    /// drawn again in place, and when a gourmand comes or goes above it, it glides to its new place.
    /// </summary>
    /// <param name="away">The dispatch panel is open: the stack is hidden in place and shown again as it is (<see cref="InnViews.Away"/>).</param>
    private void RenderInnGourmands(IReadOnlyList<InnWant> wants, bool away)
    {
        var banners = _config.InnHelperEnabled ? InnGourmandView.Banners(wants, Loc.Get).ToList() : [];
        if (_menuOpen && banners.Count == 0 && _config.InnHelperEnabled)
            banners.Add(InnGourmandView.Sample(_party.Identifier?.Library.Inn, Loc.Get));
        InnGourmandStack.Visibility = away && !_menuOpen ? Visibility.Hidden : Visibility.Visible;

        var arrangement = InnGourmandLayout.Arrange(banners, HintCanvas.ActualWidth, HintCanvas.ActualHeight, InnScale(), _config.InnGourmandRight, _config.InnGourmandTop);
        if (arrangement.Scale != _innGourmandScale)
        {
            _innGourmandScale = arrangement.Scale;
            InnGourmandFrame.LayoutTransform = new ScaleTransform(arrangement.Scale, arrangement.Scale);
        }
        InnGourmandStack.Height = arrangement.Height;

        foreach (var gone in _innGourmandBanners.Where(b => banners.All(m => m.Key != b.Key)).ToList())
        {
            _innGourmandBanners.Remove(gone);
            ComboBanner.SlideOut(gone.Holder, gone.Element, () => InnGourmandStack.Children.Remove(gone.Holder));
        }
        for (int i = 0; i < banners.Count; i++)
        {
            var model = banners[i];
            double top = arrangement.Tops[i];
            string content = model.Content + (arrangement.Compact ? "|compact" : "");
            int up = _innGourmandBanners.FindIndex(b => b.Key == model.Key);
            if (up >= 0)
            {
                var (key, was, element, holder, wasTop) = _innGourmandBanners[up];
                if (wasTop != top) holder.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(top, InnGourmandResize));
                if (was != content)
                {
                    var redrawn = InnGourmandView.Banner(model, InnPicture, arrangement.Compact);
                    holder.Children.Remove(element);
                    holder.Children.Add(redrawn);
                    holder.BeginAnimation(FrameworkElement.HeightProperty, new DoubleAnimation(redrawn.Height + ComboBanner.Gap, InnGourmandResize));
                    element = redrawn;
                }
                _innGourmandBanners[up] = (key, content, element, holder, top);
                continue;
            }
            var banner = InnGourmandView.Banner(model, InnPicture, arrangement.Compact);
            var newHolder = ComboBanner.Holder(banner, model.Table);
            Canvas.SetRight(newHolder, 0);
            Canvas.SetTop(newHolder, top);
            InnGourmandStack.Children.Add(newHolder);
            _innGourmandBanners.Add((model.Key, content, banner, newHolder, top));
            ComboBanner.SlideIn(newHolder, banner);
        }
        // As wide as the widest banner, so the frame around the stack (dragged while the menu is open) covers them.
        InnGourmandStack.Width = _innGourmandBanners.Count == 0 ? 0 : _innGourmandBanners.Max(b =>
        {
            b.Element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return b.Element.DesiredSize.Width;
        });
    }

    private void SetInnGourmandEditing(bool editing)
    {
        InnGourmandFrame.BorderBrush = editing ? (Brush)FindResource("Accent") : Brushes.Transparent;
        InnGourmandDragLabel.Visibility = editing && _config.InnHelperEnabled ? Visibility.Visible : Visibility.Collapsed;
        InnGourmandFrame.Cursor = editing ? Cursors.SizeAll : null;
    }

    /// <param name="size">The game window's scale (ComboHintLayout) times the player's size; the stack may be drawn smaller (<see cref="InnGourmandLayout.Arrange"/>).</param>
    private void ScaleInnGourmands(double size)
    {
        _innGourmandScale = size;
        InnGourmandFrame.LayoutTransform = new ScaleTransform(size, size);
        InnGourmandStack.Opacity = _config.InnStripOpacity;
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
