using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace EndHud;

/// <summary>
/// The gourmands' banners (<see cref="InnGourmandView"/>): during a session, while a gourmand's order is open, one
/// banner per gourmand in its own stack on the right (<see cref="InnGourmandLayout"/>), sliding in when new and out
/// when served, put away while the dispatch panel is open. A sample shows while the menu is open out of a session, to
/// drag the stack. The inn's size and opacity apply.
/// </summary>
public partial class OverlayWindow
{
    private readonly List<(string Key, string Content, Border Element, Grid Holder)> _innGourmandBanners = [];
    private Point? _innGourmandDragOffset;
    private static readonly TimeSpan InnGourmandResize = TimeSpan.FromMilliseconds(150);

    /// <summary>
    /// Puts up the banners for <paramref name="wants"/> (none: all slide out). A gourmand's banner already up stays where
    /// it is; when what it says changes (a request read later, another item clearly sooner) it's drawn again in place.
    /// </summary>
    private void RenderInnGourmands(IReadOnlyList<InnWant> wants)
    {
        var banners = _config.InnHelperEnabled ? InnGourmandView.Banners(wants, Loc.Get).ToList() : [];
        if (_menuOpen && banners.Count == 0 && _config.InnHelperEnabled)
            banners.Add(InnGourmandView.Sample(_party.Identifier?.Library.Inn, Loc.Get));

        foreach (var banner in _innGourmandBanners.Where(b => banners.All(m => m.Key != b.Key)).ToList())
        {
            _innGourmandBanners.Remove(banner);
            ComboBanner.SlideOut(banner.Holder, banner.Element, () => InnGourmandStack.Children.Remove(banner.Holder));
        }
        foreach (var model in banners)
        {
            int up = _innGourmandBanners.FindIndex(b => b.Key == model.Key);
            if (up >= 0)
            {
                var (key, content, old, holder) = _innGourmandBanners[up];
                if (content == model.Content) continue;
                var redrawn = InnGourmandView.Banner(model, InnPicture);
                holder.Children.Remove(old);
                holder.Children.Add(redrawn);
                holder.BeginAnimation(FrameworkElement.HeightProperty,
                    new System.Windows.Media.Animation.DoubleAnimation(redrawn.Height + ComboBanner.Gap, InnGourmandResize));
                _innGourmandBanners[up] = (key, model.Content, redrawn, holder);
                continue;
            }
            var element = InnGourmandView.Banner(model, InnPicture);
            var newHolder = ComboBanner.Holder(element, model.Table);
            int index = InnGourmandStack.Children.OfType<FrameworkElement>().Count(c => c.Tag is int table && table < model.Table);
            InnGourmandStack.Children.Insert(index, newHolder);
            _innGourmandBanners.Add((model.Key, model.Content, element, newHolder));
            ComboBanner.SlideIn(newHolder, element);
        }
    }

    private void SetInnGourmandEditing(bool editing)
    {
        InnGourmandFrame.BorderBrush = editing ? (Brush)FindResource("Accent") : Brushes.Transparent;
        InnGourmandDragLabel.Visibility = editing && _config.InnHelperEnabled ? Visibility.Visible : Visibility.Collapsed;
        InnGourmandFrame.Cursor = editing ? Cursors.SizeAll : null;
    }

    /// <param name="size">The game window's scale (ComboHintLayout) times the player's size.</param>
    private void ScaleInnGourmands(double size)
    {
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
    }
}
