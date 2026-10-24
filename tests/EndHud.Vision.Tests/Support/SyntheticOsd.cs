using EndHud.Vision;

namespace EndHud.Vision.Tests;

/// <summary>Where an on-screen display sits: the four corners and the top centre.</summary>
internal enum OsdPosition
{
    /// <summary>Top left: where RivaTuner (MSI Afterburner's OSD) puts it by default.</summary>
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
    TopCentre,
}

/// <summary>
/// A stand-in for the on-screen display MSI Afterburner draws through RivaTuner Statistics Server
/// (RTSS) inside the game's own frames, which EndHud then captures and reads: multi-colour lines of
/// 5×7 text ("GPU 65°C 98% 1950MHZ" … "FRAMETIME 6.9 MS", orange, cyan, magenta, green, yellow and
/// white, each with a black shadow) over a green frametime graph, optionally on a 55 % black
/// background. About RTSS's size: glyph pixel 2 at 1080p (about 270 × 140 px), 4 at 4K. The
/// <see cref="Minimal"/> one is a single "144 FPS" line.
/// <para>
/// Pasted onto a fixture at test time, so no new picture is stored. Shared by every reader's
/// regression gate: a reading with the display on must be the clean reading or unknown, never
/// another value (the 2026-10-24 RTSS investigation).
/// </para>
/// </summary>
internal static class SyntheticOsd
{
    // 5x7 glyphs, rows top to bottom, '#' = ink.
    private static readonly Dictionary<char, string[]> Glyphs = new()
    {
        ['0'] = [".###.", "#...#", "#..##", "#.#.#", "##..#", "#...#", ".###."],
        ['1'] = ["..#..", ".##..", "..#..", "..#..", "..#..", "..#..", ".###."],
        ['2'] = [".###.", "#...#", "....#", "...#.", "..#..", ".#...", "#####"],
        ['3'] = ["####.", "....#", "....#", ".###.", "....#", "....#", "####."],
        ['4'] = ["...#.", "..##.", ".#.#.", "#..#.", "#####", "...#.", "...#."],
        ['5'] = ["#####", "#....", "####.", "....#", "....#", "#...#", ".###."],
        ['6'] = [".###.", "#....", "#....", "####.", "#...#", "#...#", ".###."],
        ['7'] = ["#####", "....#", "...#.", "..#..", ".#...", ".#...", ".#..."],
        ['8'] = [".###.", "#...#", "#...#", ".###.", "#...#", "#...#", ".###."],
        ['9'] = [".###.", "#...#", "#...#", ".####", "....#", "....#", ".###."],
        ['A'] = [".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#"],
        ['B'] = ["####.", "#...#", "#...#", "####.", "#...#", "#...#", "####."],
        ['C'] = [".###.", "#...#", "#....", "#....", "#....", "#...#", ".###."],
        ['D'] = ["####.", "#...#", "#...#", "#...#", "#...#", "#...#", "####."],
        ['E'] = ["#####", "#....", "#....", "####.", "#....", "#....", "#####"],
        ['F'] = ["#####", "#....", "#....", "####.", "#....", "#....", "#...."],
        ['G'] = [".###.", "#...#", "#....", "#.###", "#...#", "#...#", ".###."],
        ['H'] = ["#...#", "#...#", "#...#", "#####", "#...#", "#...#", "#...#"],
        ['I'] = [".###.", "..#..", "..#..", "..#..", "..#..", "..#..", ".###."],
        ['K'] = ["#...#", "#..#.", "#.#..", "##...", "#.#..", "#..#.", "#...#"],
        ['L'] = ["#....", "#....", "#....", "#....", "#....", "#....", "#####"],
        ['M'] = ["#...#", "##.##", "#.#.#", "#.#.#", "#...#", "#...#", "#...#"],
        ['N'] = ["#...#", "##..#", "#.#.#", "#..##", "#...#", "#...#", "#...#"],
        ['O'] = [".###.", "#...#", "#...#", "#...#", "#...#", "#...#", ".###."],
        ['P'] = ["####.", "#...#", "#...#", "####.", "#....", "#....", "#...."],
        ['R'] = ["####.", "#...#", "#...#", "####.", "#.#..", "#..#.", "#...#"],
        ['S'] = [".####", "#....", "#....", ".###.", "....#", "....#", "####."],
        ['T'] = ["#####", "..#..", "..#..", "..#..", "..#..", "..#..", "..#.."],
        ['U'] = ["#...#", "#...#", "#...#", "#...#", "#...#", "#...#", ".###."],
        ['W'] = ["#...#", "#...#", "#...#", "#.#.#", "#.#.#", "##.##", "#...#"],
        ['Z'] = ["#####", "....#", "...#.", "..#..", ".#...", "#....", "#####"],
        ['%'] = ["##..#", "##..#", "...#.", "..#..", ".#...", "#..##", "#..##"],
        ['°'] = [".##..", "#..#.", ".##..", ".....", ".....", ".....", "....."],
        ['.'] = [".....", ".....", ".....", ".....", ".....", ".##..", ".##.."],
        ['/'] = ["....#", "....#", "...#.", "..#..", ".#...", "#....", "#...."],
        [' '] = [".....", ".....", ".....", ".....", ".....", ".....", "....."],
    };

    private static readonly (string Text, (byte R, byte G, byte B) Colour)[][] MinimalLines = [[("144 ", (255, 255, 0)), ("FPS", (0, 255, 0))]];

    // Afterburner's style: labels and values in different colours.
    private static readonly (string Text, (byte R, byte G, byte B) Colour)[][] FullLines =
    [
        [("GPU  ", (255, 140, 0)), ("65°C ", (255, 255, 255)), ("98% ", (255, 255, 255)), ("1950MHZ", (255, 220, 0))],
        [("MEM  ", (255, 140, 0)), ("7820MB", (255, 255, 255))],
        [("CPU  ", (0, 200, 255)), ("54°C ", (255, 255, 255)), ("37%", (255, 255, 255))],
        [("RAM  ", (255, 80, 255)), ("12400MB", (255, 255, 255))],
        [("D3D12 ", (0, 255, 0)), ("144 ", (255, 255, 0)), ("FPS", (0, 255, 0))],
        [("FRAMETIME ", (0, 255, 0)), ("6.9 MS", (255, 255, 255))],
    ];

    /// <summary>The widest line, in characters.</summary>
    private const int Chars = 22;

    /// <summary>Every position, for theories.</summary>
    public static readonly OsdPosition[] Positions = Enum.GetValues<OsdPosition>();

    /// <summary>The glyph pixel size RTSS's default font has on a frame <paramref name="height"/> pixels high (2 at 1080p, 4 at 4K).</summary>
    public static int PixelFor(int height) => Math.Max(1, (int)Math.Round(height / 540.0));

    /// <summary>The display's size at glyph pixel <paramref name="px"/>.</summary>
    public static (int W, int H) Size(int px, bool minimal = false)
    {
        int cellW = 6 * px, lineH = 9 * px;
        if (minimal) return (8 * cellW + 2 * px, lineH + 2 * px);
        int graphH = 6 * lineH / 4;
        return (Chars * cellW + 2 * px, FullLines.Length * lineH + graphH + 3 * px);
    }

    /// <summary>Where the display's top-left goes for <paramref name="position"/>: a couple of glyph pixels from the edges, as RTSS places it.</summary>
    public static (int X, int Y) Place(OsdPosition position, int frameWidth, int frameHeight, int px, bool minimal = false)
    {
        var (w, h) = Size(px, minimal);
        int m = 2 * px;
        return position switch
        {
            OsdPosition.TopLeft => (m, m),
            OsdPosition.TopRight => (frameWidth - w - m, m),
            OsdPosition.BottomLeft => (m, frameHeight - h - m),
            OsdPosition.BottomRight => (frameWidth - w - m, frameHeight - h - m),
            OsdPosition.TopCentre => ((frameWidth - w) / 2, m),
            _ => throw new ArgumentOutOfRangeException(nameof(position)),
        };
    }

    /// <summary>A copy of <paramref name="frame"/> with the display at <paramref name="position"/>, sized for the frame's height.</summary>
    public static Frame Draw(Frame frame, OsdPosition position, bool background, bool minimal = false)
    {
        int px = PixelFor(frame.Height);
        var (x, y) = Place(position, frame.Width, frame.Height, px, minimal);
        return Draw(frame, x, y, px, background, minimal);
    }

    /// <summary>A copy of <paramref name="frame"/> with the display's top-left at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public static Frame Draw(Frame frame, int x, int y, int px, bool background, bool minimal = false)
    {
        var copy = new Frame(frame.Width, frame.Height, (byte[])frame.Bgra.Clone());
        var (w, h) = Size(px, minimal);
        if (background) Blend(copy, x, y, w, h, (0, 0, 0), 0.55);
        int cellW = 6 * px, lineH = 9 * px;
        int cy = y + px;
        foreach (var line in minimal ? MinimalLines : FullLines)
        {
            int cx = x + px;
            foreach (var (text, colour) in line)
                foreach (char c in text)
                {
                    DrawGlyph(copy, cx, cy, px, c, colour);
                    cx += cellW;
                }
            cy += lineH;
        }
        if (minimal) return copy;
        // The frametime graph: a dimmer box and a jagged green line.
        int gx = x + px, gy = cy, gw = Chars * cellW - px, gh = 6 * lineH / 4;
        Blend(copy, gx, gy, gw, gh, (20, 20, 20), background ? 0.5 : 0.0);
        var rng = new Random(7);
        int prev = gy + gh / 2;
        for (int i = 0; i < gw; i++)
        {
            int v = gy + gh / 2 + (int)((rng.NextDouble() - 0.5) * gh * 0.5) + (i % 97 == 0 ? -gh / 3 : 0);
            v = Math.Clamp(v, gy + 1, gy + gh - 2);
            int a = Math.Min(prev, v), b = Math.Max(prev, v);
            for (int yy = a; yy <= b; yy++)
                for (int t = 0; t < Math.Max(1, px / 2); t++) Set(copy, gx + i, yy + t, (0, 255, 0));
            prev = v;
        }
        return copy;
    }

    /// <summary>
    /// <paramref name="picture"/> (16:9) centred in a <paramref name="width"/> × <paramref name="height"/> frame of black
    /// bars, as the game draws on a screen of another shape.
    /// </summary>
    public static (Frame Frame, PixelRect Picture) Letterboxed(Frame picture, int width, int height)
    {
        int x0 = (width - picture.Width) / 2, y0 = (height - picture.Height) / 2;
        var bgra = new byte[width * height * 4];
        for (int i = 3; i < bgra.Length; i += 4) bgra[i] = 255;
        for (int y = 0; y < picture.Height; y++)
            Buffer.BlockCopy(picture.Bgra, y * picture.Width * 4, bgra, ((y + y0) * width + x0) * 4, picture.Width * 4);
        return (new Frame(width, height, bgra), new PixelRect(x0, y0, picture.Width, picture.Height));
    }

    private static void DrawGlyph(Frame f, int x, int y, int px, char c, (byte, byte, byte) colour)
    {
        if (!Glyphs.TryGetValue(c, out var g)) g = Glyphs[' '];
        // The shadow first (RTSS draws text with a dark shadow), then the ink.
        int sh = Math.Max(1, px / 3);
        for (int pass = 0; pass < 2; pass++)
            for (int r = 0; r < 7; r++)
                for (int k = 0; k < 5; k++)
                {
                    if (g[r][k] != '#') continue;
                    for (int dy = 0; dy < px; dy++)
                        for (int dx = 0; dx < px; dx++)
                        {
                            if (pass == 0) Set(f, x + k * px + dx + sh, y + r * px + dy + sh, (0, 0, 0));
                            else Set(f, x + k * px + dx, y + r * px + dy, colour);
                        }
                }
    }

    private static void Set(Frame f, int x, int y, (byte R, byte G, byte B) c)
    {
        if (x < 0 || y < 0 || x >= f.Width || y >= f.Height) return;
        int i = (y * f.Width + x) * 4;
        f.Bgra[i] = c.B; f.Bgra[i + 1] = c.G; f.Bgra[i + 2] = c.R;
    }

    private static void Blend(Frame f, int x, int y, int w, int h, (byte R, byte G, byte B) c, double alpha)
    {
        if (alpha <= 0) return;
        for (int yy = Math.Max(0, y); yy < Math.Min(f.Height, y + h); yy++)
            for (int xx = Math.Max(0, x); xx < Math.Min(f.Width, x + w); xx++)
            {
                int i = (yy * f.Width + xx) * 4;
                f.Bgra[i] = (byte)(f.Bgra[i] * (1 - alpha) + c.B * alpha);
                f.Bgra[i + 1] = (byte)(f.Bgra[i + 1] * (1 - alpha) + c.G * alpha);
                f.Bgra[i + 2] = (byte)(f.Bgra[i + 2] * (1 - alpha) + c.R * alpha);
            }
    }
}
