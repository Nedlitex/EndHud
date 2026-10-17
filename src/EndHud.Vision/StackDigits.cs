namespace EndHud.Vision;

/// <summary>
/// The count on a stack badge: a single white digit on a small dark square (Link's stacks, and the
/// operator's own statuses under the HP bar, drawn the same way). Windows OCR reads nothing from a
/// lone digit, so the digit's white pixels, scaled to their own bounding box, are compared with the
/// game's digits (<see cref="Shapes"/>).
/// </summary>
internal static class StackDigits
{
    private const int ShapeWidth = 12, ShapeHeight = 16;
    // A pixel of the digit: every channel at least this, near grey (the digit is anti-aliased white).
    // On a small badge (1080p: 13 px) most of a stroke is the anti-aliased edge, so the bar is lower.
    private const int WhiteFrom = 150;
    private const int SmallWhiteFrom = 110;
    private const int SmallBadge = 20;
    private const int MaxTint = 60;
    // Overlap (intersection over union) with the best digit, and how far ahead of the next it must be.
    internal const double MatchAt = 0.6;
    internal const double MinLead = 0.1;

    /// <summary>The digit on <paramref name="badge"/>, or null when it isn't clearly one of them.</summary>
    public static int? Read(Frame frame, PixelRect badge)
    {
        if (Glyph(frame, badge) is not { } glyph) return null;
        var scores = Shapes.Select(s => (s.Digit, Score: Overlap(glyph, s.Mask))).OrderByDescending(s => s.Score).ToList();
        var best = scores[0];
        double next = scores.Count > 1 ? scores[1].Score : 0;
        return best.Score >= MatchAt && best.Score - next >= MinLead ? best.Digit : null;
    }

    /// <summary>The digit's white pixels inside the badge (its rim left out), scaled to the shapes' size; null when there's too little.</summary>
    internal static bool[]? Glyph(Frame frame, PixelRect badge)
    {
        int rim = Math.Max(1, badge.Width / 12);
        var inner = new PixelRect(badge.X + rim, badge.Y + rim, badge.Width - 2 * rim, badge.Height - 2 * rim).ClampTo(frame.Width, frame.Height);
        if (inner.Width < 4 || inner.Height < 4) return null;
        int whiteFrom = badge.Width < SmallBadge ? SmallWhiteFrom : WhiteFrom;
        var white = new bool[inner.Width * inner.Height];
        int left = int.MaxValue, top = int.MaxValue, right = -1, bottom = -1;
        for (int y = 0; y < inner.Height; y++)
        for (int x = 0; x < inner.Width; x++)
        {
            var (r, g, b) = frame.GetPixel(inner.X + x, inner.Y + y);
            int max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            if (min < whiteFrom || max - min > MaxTint) continue;
            white[y * inner.Width + x] = true;
            left = Math.Min(left, x); right = Math.Max(right, x);
            top = Math.Min(top, y); bottom = Math.Max(bottom, y);
        }
        int w = right - left + 1, h = bottom - top + 1;
        if (right < 0 || h < inner.Height / 3 || w < 2) return null;

        var glyph = new bool[ShapeWidth * ShapeHeight];
        for (int y = 0; y < ShapeHeight; y++)
        for (int x = 0; x < ShapeWidth; x++)
        {
            // The share of white in the matching patch of the bounding box: anti-aliased strokes stay whole.
            int x0 = left + x * w / ShapeWidth, x1 = Math.Max(x0 + 1, left + (x + 1) * w / ShapeWidth);
            int y0 = top + y * h / ShapeHeight, y1 = Math.Max(y0 + 1, top + (y + 1) * h / ShapeHeight);
            int n = 0, on = 0;
            for (int yy = y0; yy < y1; yy++)
            for (int xx = x0; xx < x1; xx++)
            {
                n++;
                if (white[yy * inner.Width + xx]) on++;
            }
            glyph[y * ShapeWidth + x] = on * 2 >= n;
        }
        return glyph;
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
    /// The game's digits, each the white of a badge scaled to its bounding box (12 × 16): the majority
    /// over the badges of the operators' statuses on the owner's controller recordings (2 from 89
    /// badges, 4 from 45, 5 from 56; samples in Fixtures/stack-digits). No 3 has been seen yet: a 3 isn't
    /// clearly any of these, so it's reported as not read rather than guessed.
    /// </summary>
    internal static readonly (int Digit, bool[] Mask)[] Shapes =
    [
        (2, Mask([
            "..#########.",
            ".###########",
            "######.#####",
            "####.....###",
            "..........##",
            "..........##",
            ".........###",
            "........####",
            "......#####.",
            ".....#####..",
            ".....###....",
            "..#####.....",
            ".#####......",
            "#####.......",
            "############",
            "############",
        ])),
        (4, Mask([
            "......####..",
            ".....#####..",
            "....######..",
            "....######..",
            "...###..##..",
            "..###...##..",
            "..###...##..",
            ".###....##..",
            ".##.....##..",
            "###....###..",
            "############",
            "############",
            ".......###..",
            "........##..",
            "........##..",
            "........##..",
        ])),
        (5, Mask([
            "..#########.",
            "..#########.",
            ".##########.",
            ".###........",
            ".###........",
            ".###.###....",
            ".#########..",
            "###########.",
            ".###....####",
            ".........###",
            ".........###",
            "###......###",
            "####....####",
            "####...#####",
            ".##########.",
            "..########..",
        ])),
    ];
}
