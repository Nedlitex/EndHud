using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace EndHud;

internal enum GaugeKind
{
    /// <summary>The ultimate: its charge fills a gold ring; full and glowing when ready.</summary>
    Ultimate,

    /// <summary>An operator's own counter: one ring segment per stack, the count in the corner.</summary>
    Counter,

    /// <summary>A team buff (Link): its icon in the middle; a full glowing ring while it's active.</summary>
    Buff,
}

/// <summary>One round gauge: the operator's avatar (or the buff's icon) in the middle, the ring around it.</summary>
/// <param name="Avatar">The operator's avatar (wiki), or for a buff its icon; null while it loads.</param>
/// <param name="Badge">Ultimates: the ultimate's icon, small in the corner (null while it loads).</param>
/// <param name="Label">Under the circle: "ultimate", or the counter's or buff's name.</param>
/// <param name="Fill">How full, 0–1; null when it can't be read now (drawn empty).</param>
/// <param name="Ready">Ready to use (ultimate), at its maximum (counter) or active (buff): drawn glowing.</param>
internal sealed record Gauge(GaugeKind Kind, BitmapSource? Avatar, BitmapSource? Badge, string Label, double? Fill, bool Ready)
{
    /// <summary>Counters: the count, or "?" when not seen yet.</summary>
    public string? Number { get; init; }

    /// <summary>Counters: their maximum, one ring segment each.</summary>
    public int Segments { get; init; }

    /// <summary>Counters: seen in this reading. False: the last value seen, drawn faded (it may have changed since).</summary>
    public bool Live { get; init; } = true;

    /// <summary>The operator has fallen: drawn faded.</summary>
    public bool Fallen { get; init; }

    /// <summary>
    /// Ultimates: a lasting ultimate is running (Laevatain's, Zhuang Fangyi's) and <see cref="Fill"/>
    /// is its time left. Drawn in another colour, glowing, the ring draining anticlockwise from full:
    /// charging run backwards.
    /// </summary>
    public bool Running { get; init; }

    /// <summary>Starts the next operator's gauges: a little more space before it.</summary>
    public bool FirstOfOperator { get; init; }
}

internal sealed record GaugeBoard(IReadOnlyList<Gauge> Gauges);

/// <summary>
/// The gauges: a row of large circles, at the bottom of the screen by default, for what
/// the game shows only small in its corners or only for the controlled operator. Each circle has the
/// operator's avatar in the middle, so whose it is reads at a glance. No dark box behind them; text
/// has a dark outline so it reads over any scene. Drawn at the size for a 1080p game window and
/// scaled with the window (see ComboHintLayout).
/// </summary>
internal static class Gauges
{
    internal const double Size = 96;
    private const double Thickness = 7;
    /// <summary>The most of the ring drawn while not ready: a gap of about 29° stays open.</summary>
    internal const double NotReadyMaxFill = 0.92;
    private const double Inset = 4; // between the ring and the avatar
    private const double BadgeSize = 36;
    private const double SegmentGap = 8; // degrees between a counter's segments

    // Frozen: shared by every gauge drawn, from any thread.
    internal static readonly Color UltimateColor = Color.FromRgb(0xFF, 0xC8, 0x3C);
    internal static readonly Color CounterColor = Color.FromRgb(0x5E, 0xE6, 0xFF);
    internal static readonly Color BuffColor = Color.FromRgb(0x7C, 0xF2, 0x9A);
    internal static readonly Color RunningColor = Color.FromRgb(0xD9, 0x7C, 0xFF);
    private static readonly Brush Disc = Frozen(Color.FromArgb(0xC8, 0x16, 0x18, 0x1C));
    private static readonly Brush Track = Frozen(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF));
    private static readonly Brush Text = Frozen(Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF));
    private static readonly Brush Label = Frozen(Color.FromArgb(0xD8, 0xD8, 0xD8, 0xD8));

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Effect Outline() => new DropShadowEffect { Color = Colors.Black, BlurRadius = 4, ShadowDepth = 0, Opacity = 0.85 };

    private static Effect Glow(Color color) => new DropShadowEffect { Color = color, BlurRadius = 22, ShadowDepth = 0, Opacity = 1 };

    public static UIElement Build(GaugeBoard board)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var gauge in board.Gauges)
        {
            var circle = Circle(gauge);
            if (gauge.FirstOfOperator && row.Children.Count > 0) ((FrameworkElement)circle).Margin = new Thickness(30, 0, 6, 0);
            row.Children.Add(circle);
        }
        return row;
    }

    private static UIElement Circle(Gauge gauge)
    {
        var colour = gauge.Kind switch
        {
            GaugeKind.Buff => BuffColor,
            GaugeKind.Ultimate => gauge.Running ? RunningColor : UltimateColor,
            _ => gauge.Ready ? UltimateColor : CounterColor,
        };
        var brush = new SolidColorBrush(colour);
        var circle = new Grid { Width = Size, Height = Size };
        circle.Children.Add(new Ellipse { Fill = Disc });

        // The avatar, in the middle. Dimmed while the ultimate charges, so a ready one stands out;
        // faded for a counter not seen this reading.
        double face = Size - 2 * (Thickness + Inset);
        if (gauge.Avatar is not null && gauge.Kind == GaugeKind.Buff)
        {
            // The buff's own icon (a transparent glyph): bright while active, faint once it has lapsed.
            circle.Children.Add(new Image
            {
                Source = gauge.Avatar,
                Width = face * 0.78,
                Height = face * 0.78,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = gauge.Ready ? 1 : 0.3,
            });
        }
        else if (gauge.Avatar is not null)
        {
            circle.Children.Add(new Ellipse
            {
                Width = face,
                Height = face,
                Fill = new ImageBrush(gauge.Avatar) { Stretch = Stretch.UniformToFill },
                Opacity = gauge.Kind == GaugeKind.Ultimate ? (gauge.Ready || gauge.Running ? 1 : 0.5) : (gauge.Live ? 0.9 : 0.45),
            });
        }

        circle.Children.Add(new Ellipse { Stroke = Track, StrokeThickness = Thickness });
        if (gauge.Kind == GaugeKind.Counter)
        {
            int lit = gauge.Fill is { } f ? (int)Math.Round(f * gauge.Segments) : 0;
            var ring = new Canvas { Width = Size, Height = Size, Opacity = gauge.Live ? 1 : 0.5 };
            double step = 360.0 / Math.Max(1, gauge.Segments);
            for (int i = 0; i < lit && i < gauge.Segments; i++) ring.Children.Add(Arc(i * step + SegmentGap / 2, step - SegmentGap, brush));
            if (gauge.Ready) ring.Effect = Glow(colour);
            circle.Children.Add(ring);
            circle.Children.Add(Badge(new TextBlock
            {
                Text = gauge.Number ?? "?",
                Foreground = gauge.Ready ? brush : Text,
                FontSize = 25,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = gauge.Live ? 1 : 0.55,
            }, gauge.Ready ? brush : Text));
        }
        else if (gauge.Kind == GaugeKind.Buff)
        {
            // Active: one glowing segment per stack, the count in the corner. Lapsed: the faint icon only.
            if (gauge.Ready && gauge.Segments > 0 && gauge.Fill is { } f)
            {
                int lit = (int)Math.Round(f * gauge.Segments);
                var ring = new Canvas { Width = Size, Height = Size, Effect = Glow(colour) };
                double step = 360.0 / gauge.Segments;
                for (int i = 0; i < lit && i < gauge.Segments; i++) ring.Children.Add(Arc(i * step + SegmentGap / 2, step - SegmentGap, brush));
                circle.Children.Add(ring);
            }
            else if (gauge.Ready)
            {
                circle.Children.Add(new Ellipse { Stroke = brush, StrokeThickness = Thickness, Effect = Glow(colour) });
            }
            if (gauge.Ready && gauge.Number is not null)
            {
                circle.Children.Add(Badge(new TextBlock
                {
                    Text = gauge.Number,
                    Foreground = brush,
                    FontSize = 25,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                }, brush));
            }
        }
        else
        {
            if (gauge.Running)
            {
                // Running: the time left, draining anticlockwise from 12 o'clock (charging, backwards), glowing.
                double left = Math.Clamp(gauge.Fill ?? 0, 0, 1);
                if (left > 0)
                {
                    var arc = left >= 0.999 ? (Shape)new Ellipse { Stroke = brush, StrokeThickness = Thickness } : Arc(360 - left * 360, left * 360, brush);
                    arc.Effect = Glow(colour);
                    circle.Children.Add(arc);
                }
            }
            else if (gauge.Fill is { } f && f > 0)
            {
                // Not ready (or not at its maximum): never a closed ring, however close, or 99%
                // reads as ready at a glance; a gap is always left at 12 o'clock.
                double shown = gauge.Ready ? f : Math.Min(f, NotReadyMaxFill);
                var arc = shown >= 0.999 ? (Shape)new Ellipse { Stroke = brush, StrokeThickness = Thickness } : Arc(0, shown * 360, brush);
                // Ready: the full ring glows. Charging: the arc is dimmer, so ready stands out at a glance.
                if (gauge.Ready) arc.Effect = Glow(colour);
                else arc.Opacity = 0.75;
                circle.Children.Add(arc);
            }
            if (gauge.Badge is not null)
            {
                circle.Children.Add(Badge(new Image { Source = gauge.Badge, Width = BadgeSize * 0.72, Height = BadgeSize * 0.72 },
                    gauge.Ready || gauge.Running ? brush : Track));
            }
        }

        var caption = new TextBlock
        {
            Text = gauge.Label,
            Foreground = gauge.Ready || gauge.Running ? brush : Label,
            FontSize = 18,
            FontWeight = gauge.Ready || gauge.Running ? FontWeights.Bold : FontWeights.Normal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 3, 0, 0),
            Effect = Outline(),
        };
        return new StackPanel
        {
            Margin = new Thickness(6, 0, 6, 0),
            MinWidth = Size,
            Opacity = gauge.Fallen ? 0.35 : 1,
            Children = { circle, caption },
        };
    }

    /// <summary>A small dark disc at the circle's bottom right, holding the count or the ultimate's icon.</summary>
    private static UIElement Badge(UIElement content, Brush rim) => new Grid
    {
        Width = BadgeSize,
        Height = BadgeSize,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Bottom,
        Margin = new Thickness(0, 0, -6, -4),
        Children =
        {
            new Ellipse { Fill = Disc, Stroke = rim, StrokeThickness = 2 },
            content,
        },
    };

    /// <summary>A ring arc from <paramref name="start"/> degrees (clockwise from 12 o'clock), <paramref name="sweep"/> degrees long.</summary>
    private static Shape Arc(double start, double sweep, Brush brush)
    {
        double r = (Size - Thickness) / 2, c = Size / 2;
        Point At(double degrees)
        {
            double rad = degrees * Math.PI / 180;
            return new Point(c + r * Math.Sin(rad), c - r * Math.Cos(rad));
        }
        var figure = new PathFigure { StartPoint = At(start), IsClosed = false };
        figure.Segments.Add(new ArcSegment(At(start + sweep), new Size(r, r), 0, sweep > 180, SweepDirection.Clockwise, true));
        return new Path
        {
            Data = new PathGeometry([figure]),
            Stroke = brush,
            StrokeThickness = Thickness,
            Width = Size,
            Height = Size,
        };
    }
}
