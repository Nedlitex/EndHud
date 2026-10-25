using System.Windows;
using System.Windows.Media;
using EndHud.Vision.Operators;

namespace EndHud;

/// <summary>
/// A drawn picture of each operator counter EndHud reads (owner, 2026-10-25: a counter in a condition should show "the
/// actual counter icon, not the character icon"). The community wiki has no picture of any counter, so each is drawn
/// here as a simple copy of what the game draws beside the HP bar, full: vector shapes (sharp at any size) on a
/// transparent square, for the strip's badges, a condition step's circle and the alert pop-up
/// (<see cref="ConditionIcon"/>). A counter without a glyph (one from a newer file) keeps the operator's avatar.
/// <para>
/// Shapes are in the game's 4K pixels (bottom-centre anchored), from the places <see cref="OperatorCounters"/> reads and
/// the drawings measured round them on the fixtures; colours are the medians measured there (the fixture each came from
/// is named). Nothing is added that isn't on screen; the numbers the game prints beside some counters are left out.
/// </para>
/// </summary>
internal static class CounterGlyphs
{
    // The arrow bars' middle rows, as OperatorCounters reads them (its ArrowBarY). Before the glyphs: they're drawn with it.
    private static readonly int[] BarRows = [1959, 1973, 1987, 2001];

    private static readonly Dictionary<string, DrawingImage> Glyphs = new(StringComparer.Ordinal)
    {
        [OperatorCounters.MeltingFlame.Id] = Glyph(MeltingFlame()),
        [OperatorCounters.HuntingArrow.Id] = Glyph(HuntingArrows()),
        [OperatorCounters.Sign.Id] = Glyph(Signs()),
        [OperatorCounters.Sunderblade.Id] = Glyph(Sunderblades()),
        [OperatorCounters.SiStacks.Id] = Glyph(SiBadge()),
    };

    /// <summary>The counter's glyph (one shared, frozen instance per counter), or null for a counter id without one.</summary>
    public static ImageSource? For(string counterId) => Glyphs.GetValueOrDefault(counterId);

    /// <summary>
    /// Laevatain's 熔火 full: four pink petals round the flower's centre (Fixtures/battle-hud/battle-47s, 4 stacks: the
    /// petals x 1510–1563, y 1957–2010, pink R 214 G 76 B 107). Each petal has a straight outer side and top, and narrows
    /// to a point towards the next petal; the other three are the top-left one mirrored.
    /// </summary>
    private static Drawing MeltingFlame()
    {
        Point[] topLeft = [new(1510, 1957), new(1523, 1957), new(1530, 1968), new(1528, 1971), new(1520, 1977), new(1516, 1976), new(1510, 1970)];
        const double MirrorX = 1510 + 1563, MirrorY = 1957 + 2010; // the flower's centre, doubled
        var petals = new GeometryGroup();
        foreach (var (fx, fy) in new[] { (false, false), (true, false), (false, true), (true, true) })
            petals.Children.Add(Polygon(topLeft.Select(p => new Point(fx ? MirrorX - p.X : p.X, fy ? MirrorY - p.Y : p.Y))));
        return Fill(petals, Color.FromRgb(214, 76, 107));
    }

    /// <summary>
    /// Typhoeus's 猎矢 full: four bars slanted the same way (the top edge x 1504–1542, the bottom x 1497–1534, rows −3.5 to
    /// +6.5 about each middle row), each with a small triangle at its left end, all lit cyan
    /// (Fixtures/counters/native-9_5s, 4 arrows: R 63 G 255 B 250).
    /// </summary>
    private static Drawing HuntingArrows()
    {
        var bars = new GeometryGroup();
        foreach (int y in BarRows)
        {
            bars.Children.Add(Polygon([new(1504, y - 3.5), new(1542, y - 3.5), new(1534, y + 6.5), new(1497, y + 6.5)]));
            bars.Children.Add(Polygon([new(1495.5, y - 1.5), new(1495.5, y + 6.5), new(1487, y + 6.5)]));
        }
        return Fill(bars, Color.FromRgb(63, 255, 250));
    }

    /// <summary>
    /// Typhoeus's 启示 full: eight round dots in two columns (centres x 1552.5 and 1567.5, beside the arrow bars' rows, about
    /// 12 px across), all lit white (Fixtures/counters/native-33s, 8 signs: R 237 G 237 B 239).
    /// </summary>
    private static Drawing Signs()
    {
        var dots = new GeometryGroup();
        foreach (int y in BarRows)
        foreach (double x in new[] { 1552.5, 1567.5 })
            dots.Children.Add(new EllipseGeometry(new Point(x, y + 1.5), 6, 5.75));
        return Fill(dots, Color.FromRgb(237, 237, 239));
    }

    /// <summary>
    /// Zhuang Fangyi's 青霆剑 full: nine small diamonds in a diamond of rows 1, 2, 3, 2, 1, at the cells OperatorCounters
    /// reads (about 13 px corner to corner), all lit cyan (Fixtures/counters/zhuang-96s, 9 swords: R 174 G 251 B 230).
    /// </summary>
    private static Drawing Sunderblades()
    {
        (double X, double Y)[] cells = [(1523, 1948), (1510, 1961), (1536, 1961), (1496, 1974), (1523, 1974), (1548, 1973), (1510, 1987), (1535, 1987), (1523, 2000)];
        const double Half = 6.5;
        var diamonds = new GeometryGroup();
        foreach (var (x, y) in cells)
            diamonds.Children.Add(Polygon([new(x, y - Half), new(x + Half, y), new(x, y + Half), new(x - Half, y)]));
        return Fill(diamonds, Color.FromRgb(174, 251, 230));
    }

    /// <summary>
    /// Si's damage stacks: her green badge, a cross of four lobes round a hollow middle, as drawn with no number on it
    /// (Fixtures/si/stacks-14s: lobes out to 25.5 px from the centre, 15 px wide where they meet, the hollow about 22 px
    /// across; green R 150 G 254 B 163). With stacks the game prints the number over the middle; the number is the
    /// condition's, beside the glyph.
    /// </summary>
    private static Drawing SiBadge()
    {
        var centre = new Point(1535.5, 1982.5);
        Point[] up = [new(-7.5, -25.5), new(7.5, -25.5), new(8.5, -19), new(15, -11), new(-15, -11), new(-8.5, -19)];
        Geometry outline = new EllipseGeometry(centre, 15, 15);
        foreach (var (cos, sin) in new[] { (1, 0), (0, 1), (-1, 0), (0, -1) }) // the up lobe turned a quarter at a time
            outline = new CombinedGeometry(GeometryCombineMode.Union, outline,
                Polygon(up.Select(p => new Point(centre.X + p.X * cos - p.Y * sin, centre.Y + p.X * sin + p.Y * cos))));
        var badge = new CombinedGeometry(GeometryCombineMode.Exclude, outline, new EllipseGeometry(centre, 11, 11));
        return Fill(badge, Color.FromRgb(150, 254, 163));
    }

    private static Geometry Polygon(IEnumerable<Point> points)
    {
        var all = points.ToList();
        var figure = new PathFigure { StartPoint = all[0], IsClosed = true, IsFilled = true };
        figure.Segments.Add(new PolyLineSegment(all.Skip(1), isStroked: true));
        return new PathGeometry([figure]);
    }

    private static GeometryDrawing Fill(Geometry geometry, Color color) => new(new SolidColorBrush(color), null, geometry);

    /// <summary>
    /// <paramref name="drawing"/> centred on a transparent square a little larger than it, so every glyph fills its place the
    /// same way whatever its shape, and isn't cut by the alert pop-up's round frame.
    /// </summary>
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
