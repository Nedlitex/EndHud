using System.Windows;
using System.Windows.Media;

namespace EndHud;

/// <summary>
/// Drawn pictures for the boss's stagger conditions (<see cref="ConditionIcon"/>): the wiki has no picture of the stagger
/// bar, so each is a simple copy of a piece of it as the game draws it under the boss's HP bar, in its 4K pixels and the
/// colours measured on the owner's recordings (<see cref="Vision.StaggerBar"/>): vector shapes on a transparent square, for
/// the strip's badges, a condition step's circle and the alert pop-up.
/// <list type="bullet">
/// <item><see cref="Bar"/> (how far it has filled): the track with the pink fill ending in its white cap.</item>
/// <item><see cref="Node"/> (node boxes reached): a node box, its white brackets above and below, the fill through it.</item>
/// <item><see cref="Countdown"/> (staggered, and its time left): the track with the yellow countdown.</item>
/// </list>
/// </summary>
internal static class StaggerGlyphs
{
    public const string BarId = "bar", NodeId = "node", CountdownId = "countdown";

    // Rows at 4K: the track (the scene darkened behind it) 154-169, the fill 158-167, the cap 156-168.
    private const double TrackTop = 154, TrackBottom = 170, FillTop = 158, FillBottom = 168;
    private static readonly Color Track = Color.FromRgb(72, 76, 80);
    private static readonly Color PinkLeft = Color.FromRgb(235, 110, 128), PinkRight = Color.FromRgb(240, 165, 172);
    private static readonly Color White = Color.FromRgb(240, 242, 245);
    private static readonly Color Yellow = Color.FromRgb(230, 200, 85);

    public static ImageSource Bar { get; } = Glyph(BarDrawing());
    public static ImageSource Node { get; } = Glyph(NodeDrawing());
    public static ImageSource Countdown { get; } = Glyph(CountdownDrawing());

    /// <summary>The picture for a stagger subject's id (<see cref="BarId"/>, <see cref="NodeId"/>, <see cref="CountdownId"/>).</summary>
    public static ImageSource? For(string? id) => id switch
    {
        BarId => Bar,
        NodeId => Node,
        CountdownId => Countdown,
        _ => null,
    };

    /// <summary>The bar's left part (x 1445-1505): the pink fill to x 1490, its cap 1490-1493, the track after it.</summary>
    private static Drawing BarDrawing()
    {
        var group = new DrawingGroup();
        group.Children.Add(Fill(new Rect(1441, TrackTop, 64, TrackBottom - TrackTop), Track));
        group.Children.Add(new GeometryDrawing(new LinearGradientBrush(PinkLeft, PinkRight, 0), null, new RectangleGeometry(new Rect(1445, FillTop, 45, FillBottom - FillTop))));
        group.Children.Add(Fill(new Rect(1490, 156, 3.5, 13), White));
        return group;
    }

    /// <summary>The node box at the bar's middle (x 1912-1927): bars across y 152-155 and 170-172, sides 4 px beside the fill.</summary>
    private static Drawing NodeDrawing()
    {
        var group = new DrawingGroup();
        group.Children.Add(Fill(new Rect(1904, TrackTop, 32, TrackBottom - TrackTop), Track));
        group.Children.Add(Fill(new Rect(1904, FillTop, 32, FillBottom - FillTop), PinkRight));
        group.Children.Add(Fill(new Rect(1912, 152, 16, 4), White));
        group.Children.Add(Fill(new Rect(1912, 156, 4, 2), White));
        group.Children.Add(Fill(new Rect(1924, 156, 4, 2), White));
        group.Children.Add(Fill(new Rect(1912, 168, 4, 2), White));
        group.Children.Add(Fill(new Rect(1924, 168, 4, 2), White));
        group.Children.Add(Fill(new Rect(1912, 170, 16, 3), White));
        return group;
    }

    /// <summary>The bar's left part with the yellow countdown to x 1490 (it has no cap).</summary>
    private static Drawing CountdownDrawing()
    {
        var group = new DrawingGroup();
        group.Children.Add(Fill(new Rect(1441, TrackTop, 64, TrackBottom - TrackTop), Track));
        group.Children.Add(Fill(new Rect(1445, FillTop, 45, FillBottom - FillTop), Yellow));
        return group;
    }

    private static GeometryDrawing Fill(Rect rect, Color color) => new(new SolidColorBrush(color), null, new RectangleGeometry(rect));

    /// <summary><paramref name="drawing"/> centred on a transparent square a little larger than it, frozen (shared across threads).</summary>
    private static DrawingImage Glyph(Drawing drawing)
    {
        var bounds = drawing.Bounds;
        double side = Math.Max(bounds.Width, bounds.Height) * 1.12;
        var square = new Rect(bounds.X + bounds.Width / 2 - side / 2, bounds.Y + bounds.Height / 2 - side / 2, side, side);
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(square)));
        group.Children.Add(drawing);
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }
}
