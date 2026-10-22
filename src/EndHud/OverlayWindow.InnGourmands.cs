using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace EndHud;

/// <summary>
/// The gourmands' tiles (<see cref="InnGourmandView.Tile"/>): during a session, while a gourmand's order is open, one tile
/// per gourmand at its table's fixed spot (<see cref="InnGourmandLayout"/>: tables 1–3 down the left edge, 4–6 down the
/// right; owner, test_yuanyuan8: "make the position FIXED"), the same with the dispatch panel open or closed, fading in when
/// new and out when served. A tile never moves; what it says changing redraws it in place. Nothing to drag: the spots
/// follow the game's screen. The inn's size (smaller only) and opacity apply.
/// </summary>
public partial class OverlayWindow
{
    private readonly Dictionary<string, (string Content, FrameworkElement Element)> _innGourmandTiles = [];
    private static readonly TimeSpan InnGourmandFade = TimeSpan.FromMilliseconds(200);

    /// <summary>The gourmands' tiles up now (the overlay's tick keeps drawing while any is).</summary>
    private int InnGourmandCount => _innGourmandTiles.Count;

    /// <summary>Puts up the tiles for <paramref name="views"/>' gourmands (none: all fade out), each at its table's spot.</summary>
    private void RenderInnGourmands(InnViews views)
    {
        var banners = _config.InnHelperEnabled ? InnGourmandView.Banners(views.Wants, Loc.Get).ToList() : [];
        double width = HintCanvas.ActualWidth, height = HintCanvas.ActualHeight;
        var arrangement = InnGourmandLayout.Arrange(banners, width, height, InnScale());

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
            var at = arrangement.Places[i];
            // Its place is in the content: only a new window size or player's size moves it, and then it's drawn there anew.
            string content = $"{model.Content}|{arrangement.Scale:F3}|{at.X:F1},{at.Y:F1}";
            if (_innGourmandTiles.TryGetValue(model.Key, out var up))
            {
                if (up.Content == content) continue;
                InnGourmandTiles.Children.Remove(up.Element);
                var redrawn = NewInnGourmandTile(model, arrangement.Scale, at);
                redrawn.Opacity = 1;
                _innGourmandTiles[model.Key] = (content, redrawn);
                continue;
            }
            var tile = NewInnGourmandTile(model, arrangement.Scale, at);
            tile.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, InnGourmandFade));
            _innGourmandTiles[model.Key] = (content, tile);
        }
    }

    private FrameworkElement NewInnGourmandTile(InnGourmandBanner model, double scale, Point at)
    {
        var tile = InnGourmandView.Tile(model, InnPicture);
        tile.LayoutTransform = new ScaleTransform(scale, scale);
        Canvas.SetLeft(tile, at.X);
        Canvas.SetTop(tile, at.Y);
        InnGourmandTiles.Children.Add(tile);
        return tile;
    }

    private void ScaleInnGourmands() => InnGourmandTiles.Opacity = _config.InnStripOpacity;
}
