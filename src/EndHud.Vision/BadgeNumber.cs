namespace EndHud.Vision;

/// <summary>
/// A white number drawn on a game badge (Si's damage stacks), any number of digits. Windows OCR
/// reads nothing from one or two lone digits, so, as <see cref="StackDigits"/> does, each digit's
/// white pixels, scaled to their own bounding box, are compared with the game's digits
/// (<see cref="Shapes"/>). Null unless every digit is clearly one of them.
/// </summary>
internal static class BadgeNumber
{
    private const int ShapeWidth = 12, ShapeHeight = 16;
    // A pixel of a digit: every channel at least this and near grey (the badge's cross is green, so
    // it drops out). Digits are anti-aliased, and on a small screen most of a stroke is its edge.
    private const int WhiteFrom = 170;
    private const int SmallWhiteFrom = 140;
    private const int SmallArea = 30;
    private const int MaxTint = 50;
    // The number is drawn centred and fills most of the area's height (Si: 26 px of 42 at 4K).
    private const double MinHeightShare = 0.5;
    private const double CentreSlack = 0.1;
    // A digit is narrower than it is tall: anything wider is two run together (small screens).
    private const double MaxDigitAspect = 0.95;
    // Overlap (intersection over union) with the best digit, and how far ahead of the next it must be.
    internal const double MatchAt = 0.6;
    internal const double MinLead = 0.1;

    /// <summary>The number centred in <paramref name="area"/>, or null when it isn't clearly one.</summary>
    public static int? Read(Frame frame, PixelRect area)
    {
        if (Glyphs(frame, area) is not { Count: > 0 and <= 9 } glyphs) return null;
        int value = 0;
        foreach (var glyph in glyphs)
        {
            var ranked = Shapes.Select(s => (s.Digit, Score: Overlap(glyph, s.Mask))).OrderByDescending(s => s.Score).ToList();
            if (ranked.Count == 0) return null;
            double next = ranked.Count > 1 ? ranked[1].Score : 0;
            if (ranked[0].Score < MatchAt || ranked[0].Score - next < MinLead) return null;
            value = value * 10 + ranked[0].Digit;
        }
        return value;
    }

    /// <summary>
    /// Each digit's white pixels, left to right, scaled to the shapes' size. White touching the
    /// area's edge is the background behind the badge, not the number, and is left out. Null when
    /// what's left isn't a row of digit-sized parts centred in the area.
    /// </summary>
    internal static List<bool[]>? Glyphs(Frame frame, PixelRect area)
    {
        var r = area.ClampTo(frame.Width, frame.Height);
        if (r.Width < 8 || r.Height < 8) return null;
        int w = r.Width, h = r.Height;
        int whiteFrom = h < SmallArea ? SmallWhiteFrom : WhiteFrom;
        var white = new bool[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            var (cr, cg, cb) = frame.GetPixel(r.X + x, r.Y + y);
            int max = Math.Max(cr, Math.Max(cg, cb)), min = Math.Min(cr, Math.Min(cg, cb));
            white[y * w + x] = min >= whiteFrom && max - min <= MaxTint;
        }
        DropEdgeTouching(white, w, h);

        // Parts: runs of columns with white in them.
        var parts = new List<(int Left, int Right, int Top, int Bottom)>();
        for (int x = 0; x < w; x++)
        {
            int top = int.MaxValue, bottom = -1;
            for (int y = 0; y < h; y++)
                if (white[y * w + x]) { top = Math.Min(top, y); bottom = Math.Max(bottom, y); }
            if (bottom < 0) continue;
            if (parts.Count > 0 && parts[^1].Right == x - 1)
            {
                var p = parts[^1];
                parts[^1] = (p.Left, x, Math.Min(p.Top, top), Math.Max(p.Bottom, bottom));
            }
            else parts.Add((x, x, top, bottom));
        }
        if (parts.Count == 0) return null;
        int tallest = parts.Max(p => p.Bottom - p.Top + 1);
        parts.RemoveAll(p => (p.Bottom - p.Top + 1) * 2 < tallest); // specks
        if (tallest < h * MinHeightShare) return null;
        if (parts.Any(p => p.Right - p.Left + 1 > tallest * MaxDigitAspect)) return null;
        double centre = (parts[0].Left + parts[^1].Right + 1) / 2.0;
        if (Math.Abs(centre - w / 2.0) > w * CentreSlack) return null; // not the number: part of it is cut off, or the badge moved

        var glyphs = new List<bool[]>(parts.Count);
        foreach (var (left, right, top, bottom) in parts)
        {
            int pw = right - left + 1, ph = bottom - top + 1;
            var glyph = new bool[ShapeWidth * ShapeHeight];
            for (int y = 0; y < ShapeHeight; y++)
            for (int x = 0; x < ShapeWidth; x++)
            {
                // The share of white in the matching patch of the bounding box: anti-aliased strokes stay whole.
                int x0 = left + x * pw / ShapeWidth, x1 = Math.Max(x0 + 1, left + (x + 1) * pw / ShapeWidth);
                int y0 = top + y * ph / ShapeHeight, y1 = Math.Max(y0 + 1, top + (y + 1) * ph / ShapeHeight);
                int n = 0, on = 0;
                for (int yy = y0; yy < y1; yy++)
                for (int xx = x0; xx < x1; xx++)
                {
                    n++;
                    if (white[yy * w + xx]) on++;
                }
                glyph[y * ShapeWidth + x] = on * 2 >= n;
            }
            glyphs.Add(glyph);
        }
        return glyphs;
    }

    /// <summary>Clears every white region (8-connected) that touches the edge of the area.</summary>
    private static void DropEdgeTouching(bool[] white, int w, int h)
    {
        var stack = new Stack<int>();
        for (int x = 0; x < w; x++) { Seed(x); Seed((h - 1) * w + x); }
        for (int y = 0; y < h; y++) { Seed(y * w); Seed(y * w + w - 1); }
        while (stack.Count > 0)
        {
            int i = stack.Pop(), x = i % w, y = i / w;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if ((uint)nx < (uint)w && (uint)ny < (uint)h) Seed(ny * w + nx);
            }
        }

        void Seed(int i)
        {
            if (!white[i]) return;
            white[i] = false;
            stack.Push(i);
        }
    }

    private static double Overlap(bool[] a, bool[] b)
    {
        int both = 0, either = 0;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] && b[i]) both++;
            if (a[i] || b[i]) either++;
        }
        return either == 0 ? 0 : both / (double)either;
    }

    private static bool[] Mask(string[] rows) => rows.SelectMany(r => r.Select(c => c == '#')).ToArray();

    /// <summary>
    /// The game's digits on Si's badge, each the white of one digit scaled to its bounding box
    /// (12 × 16): the majority over every digit of the labelled frames in Fixtures/si (frame and
    /// position in the number listed above each). Rebuilt with a throwaway run of <see cref="Glyphs"/>.
    /// </summary>
    internal static readonly (int Digit, bool[] Mask)[] Shapes =
    [
        // 0: 4 samples: 21s#1, 30_5s#1, 119s#1, 120s#1
        (0, Mask([
            ".....##.....",
            "...#######..",
            "..###...###.",
            ".###.....###",
            ".##.......##",
            "###.......##",
            "###.......##",
            "###.......##",
            "###.......##",
            "###.......##",
            "###.......##",
            ".##.......##",
            ".##......###",
            ".##.....###.",
            "..########..",
            "....#####...",
        ])),
        // 1: 21 samples: 16_5s#0, 21s#0, 22s#0, 22s#1, 22_25s#0, 22_25s#1, 23_5s#0, 25s#0, 27s#0, 29s#0, 34s#1, 53_75s#0, 53_75s#1, 57s#0, 58_5s#0, 72s#0, 73_5s#0, 84_5s#1, 105_5s#0, 106_75s#0, 107_5s#0
        (1, Mask([
            "........####",
            ".....#######",
            "...#########",
            "..##########",
            "######..####",
            "..#.....####",
            "........####",
            "........####",
            "........####",
            "........####",
            "........####",
            "........####",
            "........####",
            "........####",
            "........####",
            "........####",
        ])),
        // 2: 11 samples: 17_5s#0, 30_5s#0, 34s#0, 61_75s#0, 63s#0, 78_5s#0, 78_5s#1, 79_25s#0, 83_5s#0, 116_75s#0, 118_5s#0
        (2, Mask([
            "...######...",
            "..#########.",
            ".###....####",
            "###......###",
            ".........###",
            ".........###",
            ".........###",
            "........###.",
            ".......###..",
            "......###...",
            ".....###....",
            "...####.....",
            "..###.......",
            ".###........",
            "############",
            "############",
        ])),
        // 3: 19 samples: 18_5s#0, 57s#1, 79_25s#1, 84_5s#0, 86s#0, 86s#1, 87_25s#0, 89s#0, 89_75s#0, 90_75s#0, 92_5s#0, 94s#0, 105_5s#1, 119s#0, 120s#0, 121s#0, 122s#0, 122_25s#0, 122_5s#1
        (3, Mask([
            "...######...",
            "..#########.",
            ".###....####",
            "###......###",
            "..........##",
            "..........##",
            ".........##.",
            ".....#####..",
            ".....######.",
            ".........##.",
            "..........##",
            "..........##",
            "###.......##",
            "####.....###",
            ".##########.",
            "..########..",
        ])),
        // 4: 8 samples: 19s#0, 41_25s#0, 58_5s#1, 87_25s#1, 99_5s#0, 106_75s#1, 122_5s#0, 123_5s#0
        (4, Mask([
            "......####..",
            "......####..",
            ".....#####..",
            "....###.##..",
            "....##..##..",
            "...##...##..",
            "...##...##..",
            "..##....##..",
            ".###....##..",
            ".##.....##..",
            "############",
            "############",
            "........##..",
            "........##..",
            "........##..",
            "........##..",
        ])),
        // 5: 9 samples: 19_5s#0, 42s#0, 61_75s#1, 72s#1, 89s#1, 100_75s#0, 107_5s#1, 121s#1, 123_5s#1
        (5, Mask([
            "..#########.",
            ".##########.",
            ".###........",
            ".###........",
            ".##.........",
            ".##.........",
            ".#########..",
            "###########.",
            ".##......###",
            ".........###",
            "..........##",
            ".........###",
            "###......###",
            ".###....####",
            ".##########.",
            "...######...",
        ])),
        // 6: 5 samples: 23_5s#1, 63s#1, 67_5s#0, 89_75s#1, 101_25s#0
        (6, Mask([
            "......###...",
            ".....###....",
            "....###.....",
            "...###......",
            "...###......",
            "..###.......",
            ".#########..",
            ".##########.",
            "####.....###",
            "###.......##",
            "###.......##",
            "###.......##",
            "###.......##",
            ".###.....###",
            "..#########.",
            "...#######..",
        ])),
        // 7: 6 samples: 25s#1, 45s#0, 68_5s#0, 90_75s#1, 101_5s#0, 116_75s#1
        (7, Mask([
            "############",
            "############",
            ".........###",
            ".........##.",
            "........###.",
            "........###.",
            ".......###..",
            "......###...",
            "......###...",
            "......##....",
            ".....##.....",
            ".....##.....",
            "....##......",
            "...###......",
            "...##.......",
            "..###.......",
        ])),
        // 8: 5 samples: 27s#1, 69_5s#0, 83_5s#1, 92_5s#1, 122s#1
        (8, Mask([
            "....#####...",
            "..########..",
            ".###.....##.",
            ".##......##.",
            ".##......##.",
            ".##......##.",
            "..##....###.",
            "...######...",
            "..########..",
            ".###.....##.",
            "###.......##",
            "##........##",
            "###.......##",
            "###......###",
            ".##########.",
            "..#######...",
        ])),
        // 9: 8 samples: 20_25s#0, 29s#1, 50_75s#0, 70_5s#0, 73_5s#1, 94s#1, 118_5s#1, 122_25s#1
        (9, Mask([
            "....####....",
            "..########..",
            ".###....###.",
            "###......###",
            "##........##",
            "##........##",
            "##........##",
            "###......###",
            ".###....####",
            "..#########.",
            "....###.###.",
            ".......###..",
            "......###...",
            "......##....",
            ".....##.....",
            "....##......",
        ])),
    ];
}
