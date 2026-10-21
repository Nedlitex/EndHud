using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EndHud;

/// <summary>
/// The gourmands' circles (<see cref="InnWantsView"/>): during a session, while a gourmand's order is open, right of
/// the table cards (<see cref="InnWantsLayout"/>), also while the dispatch panel is open (they sit above it). The inn's
/// size and opacity apply; never hit-tested.
/// </summary>
public partial class OverlayWindow
{
    private Border? _innWantsFrame;
    private string? _innWantsKey;

    private void RenderInnWants(IReadOnlyList<InnWant> wants)
    {
        double width = HintCanvas.ActualWidth, height = HintCanvas.ActualHeight, scale = InnScale();
        string? key = wants.Count == 0 ? null : $"{width:F0}x{height:F0}@{scale:F3}\n{InnWantsView.Key(wants)}";
        if (key == _innWantsKey) return;
        _innWantsKey = key;
        if (_innWantsFrame is null)
        {
            _innWantsFrame = new Border { IsHitTestVisible = false };
            HintCanvas.Children.Add(_innWantsFrame);
        }
        if (wants.Count == 0)
        {
            _innWantsFrame.Visibility = Visibility.Collapsed;
            _innWantsFrame.Child = null;
            return;
        }
        var at = InnWantsLayout.TopLeft(width, height);
        _innWantsFrame.Child = Gauges.Build(InnWantsView.Board(wants, InnPicture, Loc.Get));
        _innWantsFrame.LayoutTransform = new ScaleTransform(scale, scale);
        _innWantsFrame.Opacity = _config.InnStripOpacity;
        Canvas.SetLeft(_innWantsFrame, at.X);
        Canvas.SetTop(_innWantsFrame, at.Y);
        _innWantsFrame.Visibility = Visibility.Visible;
    }
}
