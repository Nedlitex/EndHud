using System.Windows;
using System.Windows.Media;

namespace EndHud;

/// <summary>
/// The drawn picture for "time in phase" (<see cref="Vision.Rotations.TimeInPhaseCondition"/>; <see cref="ConditionIcon"/>):
/// the game has no picture for a phase's time, so a plain clock, a white ring with its hands at 12 and 3, on a transparent
/// square, for the strip's badges, a condition step's circle and the editor. Frozen: shared across threads.
/// </summary>
internal static class ClockGlyph
{
    private const double Side = 24, Centre = Side / 2, Radius = 9.5;
    private static readonly Color White = Color.FromRgb(240, 242, 245);

    public static ImageSource Image { get; } = Draw();

    private static DrawingImage Draw()
    {
        var pen = new Pen(new SolidColorBrush(White), 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        var hands = new GeometryGroup();
        hands.Children.Add(new LineGeometry(new Point(Centre, Centre), new Point(Centre, Centre - 6.5)));
        hands.Children.Add(new LineGeometry(new Point(Centre, Centre), new Point(Centre + 4.5, Centre)));
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, Side, Side))));
        group.Children.Add(new GeometryDrawing(null, pen, new EllipseGeometry(new Point(Centre, Centre), Radius, Radius)));
        group.Children.Add(new GeometryDrawing(null, pen, hands));
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }
}
