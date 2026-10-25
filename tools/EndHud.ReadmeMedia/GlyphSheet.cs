// A review sheet of the drawn counter glyphs (CounterGlyphs) beside the game's own counters:
//
//   dotnet run --project tools/EndHud.ReadmeMedia -c Release -- glyphs <tests/data/Fixtures> <out.png>
//
// One row per counter: the game's counter cut from the fixture its glyph was measured on, then the glyph, at the strip's
// badge size (20 px, on the badge's dark fill) and at 4x the game's 4K pixels (the glyph drawn in the same pixels, so the
// two line up). Under them, Laevatain's 熔火 at 0-4 stacks from the battle-hud fixtures, at 4x: the glyph is the full one.

using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EndHud;

static class GlyphSheet
{
    private sealed record Row(string Name, string CounterId, string Fixture);

    private static readonly Row[] Rows =
    [
        new("Laevatain melting-flame (4)", "melting-flame", "battle-hud/battle-47s.jpg"),
        new("Typhoeus hunting-arrow (4)", "hunting-arrow", "counters/native-20s.jpg"),
        new("Typhoeus sign (8)", "sign", "counters/native-33s.jpg"),
        new("Zhuang Fangyi sunderblade (9)", "sunderblade", "counters/zhuang-96s.jpg"),
        new("Si damage-stacks (0)", "damage-stacks", "si/stacks-14s.jpg"),
        new("Si damage-stacks (backing)", "damage-stacks", "si/stacks-50_75s.jpg"),
    ];

    private static readonly (string Label, string Fixture)[] FlameStates =
    [
        ("0", "battle-hud/battle-21s.jpg"), ("I", "battle-hud/battle-60s.jpg"), ("II", "battle-hud/battle-28s.jpg"),
        ("III", "battle-hud/battle-36s.jpg"), ("IV", "battle-hud/battle-47s.jpg"),
    ];

    private const int Badge = 20, Zoom = 4, Pad = 12, LabelWidth = 230, BadgeColumns = 180;
    private static readonly Brush BadgeFill = new SolidColorBrush(Color.FromArgb(0xD0, 0x18, 0x1A, 0x1E));
    private static readonly Brush Back = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x64));

    public static int Run(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: EndHud.ReadmeMedia glyphs <tests/data/Fixtures> <out.png>");
            return 2;
        }
        string fixtures = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[1]);
        int code = 0;
        var thread = new Thread(() => code = Draw(fixtures, output));
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return code;
    }

    private static int Draw(string fixtures, string output)
    {
        var frames = new Dictionary<string, BitmapSource>();
        BitmapSource Frame(string name) => frames.TryGetValue(name, out var f) ? f
            : frames[name] = BitmapFrame.Create(new Uri(Path.Combine(fixtures, name)), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);

        // Every glyph's square in the game's pixels; the 4x cells are as big as the largest.
        var squares = Rows.Select(r => ((DrawingImage)CounterGlyphs.For(r.CounterId)!).Drawing.Bounds).ToList();
        int cell = (int)Math.Ceiling(squares.Max(s => s.Width)) * Zoom;
        int rowHeight = cell + Pad;
        int flameSide = (int)Math.Ceiling(squares[0].Width) * Zoom;
        int width = Math.Max(LabelWidth + BadgeColumns + 2 * (cell + Pad) + Pad, Pad + FlameStates.Length * (flameSide + Pad));
        int height = 40 + Rows.Length * rowHeight + 40 + cell + 30;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            double x0 = LabelWidth, y = 10;
            Text(dc, "game | glyph, badge size", x0, y);
            Text(dc, "game, 4x", x0 + BadgeColumns, y);
            Text(dc, "glyph, 4x", x0 + BadgeColumns + cell + Pad, y);
            y = 40;
            for (int i = 0; i < Rows.Length; i++, y += rowHeight)
            {
                var row = Rows[i];
                var glyph = (DrawingImage)CounterGlyphs.For(row.CounterId)!;
                var square = squares[i];
                var crop = Crop(Frame(row.Fixture), square);
                Text(dc, row.Name, Pad, y + cell / 2.0 - 8);
                Text(dc, row.Fixture, Pad, y + cell / 2.0 + 8, 10);
                double x = x0;
                // Badge size: the game's counter scaled down smoothly, the glyph as the strip draws it, on the badge's fill.
                dc.DrawRectangle(BadgeFill, null, new Rect(x - 2, y + cell / 2.0 - Badge / 2.0 - 2, 2 * Badge + Pad + 4, Badge + 4));
                dc.DrawImage(crop, new Rect(x, y + cell / 2.0 - Badge / 2.0, Badge, Badge));
                dc.DrawImage(glyph, new Rect(x + Badge + Pad, y + cell / 2.0 - Badge / 2.0, Badge, Badge));
                x += BadgeColumns;
                // 4x: the game's pixels blown up as they are, and the glyph drawn over the same square.
                double side = square.Width * Zoom;
                dc.DrawImage(Nearest(crop, Zoom), new Rect(x, y, side, side));
                x += cell + Pad;
                dc.DrawRectangle(Back, null, new Rect(x, y, side, side));
                dc.DrawImage(glyph, new Rect(x, y, side, side));
            }
            y += 10;
            Text(dc, "Laevatain melting-flame in the game at 0, I, II, III, IV (4x); the glyph shows IV, full, without the numeral", Pad, y);
            y += 25;
            var flame = squares[0];
            double fx = Pad;
            foreach (var (label, fixture) in FlameStates)
            {
                double side = flame.Width * Zoom;
                dc.DrawImage(Nearest(Crop(Frame(fixture), flame), Zoom), new Rect(fx, y, side, side));
                Text(dc, label, fx + 2, y + 2);
                fx += side + Pad;
            }
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        using (var file = File.Create(output)) encoder.Save(file);
        Console.WriteLine(output);
        return 0;
    }

    /// <summary>The frame's pixels in <paramref name="square"/> (the game's 4K pixels), whole pixels.</summary>
    private static BitmapSource Crop(BitmapSource frame, Rect square)
    {
        var area = new Int32Rect((int)Math.Floor(square.X), (int)Math.Floor(square.Y), (int)Math.Ceiling(square.Width), (int)Math.Ceiling(square.Height));
        var crop = new CroppedBitmap(frame, area);
        crop.Freeze();
        return crop;
    }

    /// <summary>The bitmap blown up <paramref name="zoom"/> times, each pixel a block (no smoothing).</summary>
    private static BitmapSource Nearest(BitmapSource bitmap, int zoom)
    {
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);
        using (var dc = visual.RenderOpen()) dc.DrawImage(bitmap, new Rect(0, 0, bitmap.PixelWidth * zoom, bitmap.PixelHeight * zoom));
        var target = new RenderTargetBitmap(bitmap.PixelWidth * zoom, bitmap.PixelHeight * zoom, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        return target;
    }

    private static void Text(DrawingContext dc, string text, double x, double y, double size = 13) =>
        dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, Brushes.Black, 1.0),
            new Point(x, y));
}
