using System.Windows;
using System.Windows.Media;
using EndHud.Vision.Operators;

namespace EndHud;

/// <summary>
/// A drawn picture of each operator counter EndHud reads (owner, 2026-10-25: a counter in a condition should show "the
/// actual counter icon, not the character icon"). The community wiki has no picture of any counter, so each is drawn
/// here as a copy of what the game draws beside the HP bar, full (every segment lit), as clean vector art (owner,
/// 2026-10-25: "make the counter look more like what the game shows ... use a more vector style"): the lit shapes in
/// their measured colour over the game's own dark outline or backing, sharp at any size, on a transparent square, for
/// the strip's badges, a condition step's circle and the alert pop-up (<see cref="ConditionIcon"/>). A counter without a
/// glyph (one from a newer file) keeps the operator's avatar.
/// <para>
/// Shapes are in the game's 4K pixels (bottom-centre anchored), measured on the fixtures named on each, colours the
/// medians measured there; <c>CounterGlyphsTests</c> lays each glyph over its fixture and compares it point by point.
/// Nothing is added that isn't on screen; the numbers the game prints on or beside some counters (Laevatain's Roman
/// numeral, Zhuang Fangyi's count, Si's stacks) are left out: the badge has its own.
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
    /// Laevatain's 熔火 full (Fixtures/battle-hud/battle-47s, 4 stacks; the same shape at every count, unlit petals grey and
    /// lit ones white below 4): an X of four petals, pink (R 208 G 75 B 104), inside one dark outline (R 38 G 28 B 30) that
    /// also parts the petals with a dark cross 5 px wide. Centre x 1535.5 y 1981.5; the outline's outer edge 30.5 px out,
    /// notched in to 19.5 px between the petals. Each petal has a square outer corner, a straight top and side, and chamfered sides
    /// running in to the cross; the other three are the top-left one mirrored.
    /// </summary>
    private static Drawing MeltingFlame()
    {
        var centre = new Point(1535.5, 1981.5);
        // The top-left petal's pink, from the centre: the outer corner, along the top, the chamfer in to the cross, round the
        // inner corner, back out along the cross and the lower chamfer to the side. Symmetric about the diagonal.
        Point[] topLeft = [new(-27, -27), new(-13.5, -27), new(-2.5, -16), new(-2.5, -2.5), new(-16, -2.5), new(-27, -13.5)];
        var petals = new GeometryGroup();
        foreach (var (sx, sy) in new[] { (1, 1), (-1, 1), (1, -1), (-1, -1) })
            petals.Children.Add(Polygon(topLeft.Select(p => new Point(centre.X + p.X * sx, centre.Y + p.Y * sy))));
        // The petals 5 px apart: their outlines run together into the dark cross between them.
        return Rimmed(petals, Color.FromRgb(208, 75, 104), Color.FromRgb(38, 28, 30), rim: 3.5);
    }

    /// <summary>
    /// Typhoeus's 猎矢 full (Fixtures/counters/native-20s, 4 arrows): four bars slanted the same way (the top edge x
    /// 1504–1542, the bottom x 1497–1534, rows −3.5 to +6.5 about each middle row), each with a small triangle at its left
    /// end, lit cyan (R 63 G 255 B 250), with the game's dark outline 4 px round them, tinted by their glow where it shows
    /// between the bars (R 34 G 110 B 100: 48, 144, 130 between, 41, 73, 70 at the edge).
    /// </summary>
    private static Drawing HuntingArrows()
    {
        var bars = new GeometryGroup();
        foreach (int y in BarRows)
        {
            bars.Children.Add(Polygon([new(1504, y - 3.5), new(1542, y - 3.5), new(1534, y + 6.5), new(1497, y + 6.5)]));
            bars.Children.Add(Polygon([new(1495.5, y - 1.5), new(1495.5, y + 6.5), new(1487, y + 6.5)]));
        }
        return Rimmed(bars, Color.FromRgb(63, 255, 250), Color.FromRgb(34, 110, 100), rim: 4);
    }

    /// <summary>
    /// Typhoeus's 启示 full (Fixtures/counters/native-33s, 8 signs): eight round dots 12 px across and 11 high in two
    /// columns (centres x 1551.5 and 1566.5, y half a pixel above the arrow bars' rows), lit white (R 237 G 237 B 239), on
    /// the game's dark backing (R 36 G 36 B 38), one block round all eight, 3.5 px past them.
    /// </summary>
    private static Drawing Signs()
    {
        var dots = new GeometryGroup();
        foreach (int y in BarRows)
        foreach (double x in new[] { 1551.5, 1566.5 })
            dots.Children.Add(new EllipseGeometry(new Point(x, y - 0.5), 6, 5.5));
        // The backing is one block behind all eight (dark between the columns and the rows alike).
        var block = new RectangleGeometry(new Rect(1545.5, BarRows[0] - 6, 27, BarRows[^1] - BarRows[0] + 11));
        return Rimmed(dots, Color.FromRgb(237, 237, 239), Color.FromRgb(36, 36, 38), rim: 3.5, under: block);
    }

    /// <summary>
    /// Zhuang Fangyi's 青霆剑 full (Fixtures/counters/zhuang-96s, 9 swords): nine small diamonds in a diamond of rows 1, 2,
    /// 3, 2, 1, at the cells OperatorCounters reads (13 px corner to corner), lit mint (R 174 G 251 B 230), on the game's
    /// dark diamond plate (R 30 G 42 B 42) that runs straight along the outer cells' edges, 3.5 px past their corners.
    /// </summary>
    private static Drawing Sunderblades()
    {
        (double X, double Y)[] cells = [(1523, 1948), (1510, 1961), (1536, 1961), (1496, 1974), (1523, 1974), (1548, 1973), (1510, 1987), (1535, 1987), (1523, 2000)];
        const double Half = 6.5;
        var diamonds = new GeometryGroup();
        foreach (var (x, y) in cells)
            diamonds.Children.Add(Polygon([new(x, y - Half), new(x + Half, y), new(x, y + Half), new(x - Half, y)]));
        // The plate: the diamond through the outer cells' corners (top, right, bottom, left), widened by the rim.
        var plate = Polygon([new(1523, 1948 - Half), new(1548 + Half, 1973.5), new(1523, 2000 + Half), new(1496 - Half, 1974)]);
        return Rimmed(diamonds, Color.FromRgb(174, 251, 230), Color.FromRgb(30, 42, 42), rim: 3.5, under: plate);
    }

    /// <summary>
    /// Si's damage stacks, as drawn with no number (Fixtures/si/stacks-14s; the backing measured on stacks-50_75s, where it
    /// shows against a light scene): a green cross (R 151 G 254 B 164) of four lobes round a hollow middle, centre x 1535 y
    /// 1981.5, the lobes out to 26.5 px and 17 px wide at their ends, widening into a ring 18.5 px round (through the lobes' inner corners) with an 11.5 px
    /// hollow, on the game's dark stepped backing (R 28 G 34 B 28): a plus 38 px wide reaching 30.5 px up and down and 31 px
    /// to the sides, with a step 22.5 px out between its arms. The hollow shows the backing. With stacks the game prints the
    /// number over the middle; the number is the condition's, beside the glyph.
    /// </summary>
    private static Drawing SiBadge()
    {
        var centre = new Point(1535, 1981.5);
        Point[] up = [new(-8.5, -26.5), new(8.5, -26.5), new(9.5, -19), new(14, -12), new(-14, -12), new(-9.5, -19)];
        Geometry outline = new EllipseGeometry(centre, 18.5, 18.5);
        foreach (var (cos, sin) in new[] { (1, 0), (0, 1), (-1, 0), (0, -1) }) // the up lobe turned a quarter at a time
            outline = new CombinedGeometry(GeometryCombineMode.Union, outline,
                Polygon(up.Select(p => new Point(centre.X + p.X * cos - p.Y * sin, centre.Y + p.X * sin + p.Y * cos))));
        var cross = new CombinedGeometry(GeometryCombineMode.Exclude, outline, new EllipseGeometry(centre, 11.5, 11.5));

        var backing = new GeometryGroup { FillRule = FillRule.Nonzero };
        backing.Children.Add(new RectangleGeometry(new Rect(centre.X - 19, centre.Y - 30.5, 38, 61)));
        backing.Children.Add(new RectangleGeometry(new Rect(centre.X - 31, centre.Y - 18.5, 62, 37)));
        backing.Children.Add(new RectangleGeometry(new Rect(centre.X - 22.5, centre.Y - 22.5, 45, 45)));
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(28, 34, 28)), null, backing));
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(151, 254, 164)), null, cross));
        return group;
    }

    /// <summary>
    /// The lit shapes over the game's dark outline: <paramref name="rim"/> px of <paramref name="dark"/> all round them
    /// (round corners, so shapes closer than twice the rim run together, as the game's do), over an optional dark shape
    /// <paramref name="under"/> given the same rim (the plate or the middle the shapes sit on).
    /// </summary>
    private static Drawing Rimmed(Geometry lit, Color fill, Color dark, double rim, Geometry? under = null)
    {
        var darkBrush = new SolidColorBrush(dark);
        var pen = new Pen(darkBrush, 2 * rim) { LineJoin = PenLineJoin.Round };
        var group = new DrawingGroup();
        if (under is not null) group.Children.Add(new GeometryDrawing(darkBrush, pen, under));
        group.Children.Add(new GeometryDrawing(darkBrush, pen, lit));
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(fill), null, lit));
        return group;
    }

    private static Geometry Polygon(IEnumerable<Point> points)
    {
        var all = points.ToList();
        var figure = new PathFigure { StartPoint = all[0], IsClosed = true, IsFilled = true };
        figure.Segments.Add(new PolyLineSegment(all.Skip(1), isStroked: true));
        return new PathGeometry([figure]);
    }

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
