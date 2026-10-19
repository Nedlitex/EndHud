namespace EndHud.Vision.Inn;

/// <summary>
/// Numbers in the inn's HUD font: the session timer ("04:46"), the fire meter ("92/100") and the
/// streak ("18"). Windows OCR misreads them now and then without saying so (04:25 read as "04•.26"),
/// so each glyph is cut out on its own and compared with the font's shapes (<see cref="Shapes"/>,
/// cut from the recordings), as <see cref="StackDigits"/> does for stack badges. A glyph that isn't
/// clearly one of them makes the whole number unreadable: never a guess.
/// </summary>
internal static class InnDigits
{
    internal const int ShapeWidth = 10, ShapeHeight = 14;
    // Likeness (correlation of the ink, less the aspect difference) to the best shape, and how far
    // ahead of the next character's it must be.
    internal const double MatchAt = 0.7;
    internal const double MinLead = 0.1;
    // A glyph is at least this share of the tallest one's height (the colon's dots aren't).
    private const double MinHeightShare = 0.7;

    /// <summary>One glyph's place in the region and its shape.</summary>
    /// <param name="Mask">How much of each cell of the glyph's box is ink, 0–1, <see cref="ShapeWidth"/> × <see cref="ShapeHeight"/>.</param>
    internal sealed record Glyph(int Left, int Right, int Top, int Bottom, float[] Mask)
    {
        public int Width => Right - Left + 1;
        public int Height => Bottom - Top + 1;
    }

    /// <summary>
    /// The characters (digits and '/') left to right, or null when a glyph isn't clearly one of them.
    /// <paramref name="dropUnknownFirst"/>: an icon may stand left of the number (the fire meter's
    /// flame): a first glyph that isn't a character is left out rather than spoiling the number.
    /// </summary>
    /// <param name="minHeightShare">A glyph is at least this share of the tallest one's height: a
    /// number beside a label (the streak's) leaves out the label's shorter characters.</param>
    public static string? Read(Frame frame, PixelRect region, InnText.Ink ink, double hudScale, bool dropUnknownFirst = false,
        double minHeightShare = MinHeightShare)
    {
        var glyphs = Glyphs(frame, region, ink, hudScale, minHeightShare);
        if (glyphs.Count == 0) return null;
        var chars = new List<char>();
        for (int i = 0; i < glyphs.Count; i++)
        {
            char? c = Classify(glyphs[i]);
            if (c is null)
            {
                if (i == 0 && dropUnknownFirst) continue;
                return null;
            }
            chars.Add(c.Value);
        }
        return chars.Count == 0 ? null : new string([.. chars]);
    }

    /// <summary>The character a glyph is, or null when it isn't clearly one.</summary>
    internal static char? Classify(Glyph glyph)
    {
        double aspect = glyph.Width / (double)glyph.Height;
        // A colon is two dots, one over the other: narrow, ink at the top and the bottom, none between.
        // Its dots are a few pixels across, so their shape says little; this does.
        if (aspect < 0.35 && IsColon(glyph.Mask)) return ':';
        var scores = Rank(glyph);
        var best = scores[0];
        // Shapes of one character (several cuts) don't compete with each other.
        double next = scores.FirstOrDefault(s => s.Char != best.Char).Score;
        return best.Score >= MatchAt && best.Score - next >= MinLead ? best.Char : null;
    }

    /// <summary>Each shape's likeness to the glyph, best first.</summary>
    internal static List<(char Char, double Score)> Rank(Glyph glyph)
    {
        double aspect = glyph.Width / (double)glyph.Height;
        return [.. Shapes
            .Select(s => (s.Char, Score: Correlation(glyph.Mask, s.Mask) - AspectPenalty(aspect, s.Aspect)))
            .OrderByDescending(s => s.Score)];
    }

    private static bool IsColon(float[] mask)
    {
        bool Row(int y) => Enumerable.Range(0, ShapeWidth).Any(x => mask[y * ShapeWidth + x] >= 0.5f);
        return Row(1) && Row(ShapeHeight - 2) && !Enumerable.Range(4, ShapeHeight - 8).Any(Row);
    }

    // A "1" and a "/" stretched to the shape's box look alike; their width says which.
    private static double AspectPenalty(double aspect, double shapeAspect) => Math.Min(0.5, Math.Abs(aspect - shapeAspect));

    /// <summary>
    /// The full-height glyphs in the region, left to right (the colon's dots and specks left out).
    /// In a window smaller than 4K the region is first enlarged smoothly to its 4K size: telling ink
    /// from background pixel by pixel loses the thin strokes of glyphs a dozen pixels tall.
    /// </summary>
    internal static List<Glyph> Glyphs(Frame frame, PixelRect region, InnText.Ink ink, double hudScale, double minHeightShare = MinHeightShare)
    {
        var r = region.ClampTo(frame.Width, frame.Height);
        if (r.Width < 4 || r.Height < 4) return [];
        if (hudScale < 0.9)
        {
            double enlarge = Math.Min(4, 1 / hudScale);
            frame = frame.Crop(r).ResizeSmooth((int)Math.Round(r.Width * enlarge), (int)Math.Round(r.Height * enlarge));
            r = new PixelRect(0, 0, frame.Width, frame.Height);
        }
        var on = new bool[r.Width * r.Height];
        for (int y = 0; y < r.Height; y++)
        for (int x = 0; x < r.Width; x++)
        {
            var (cr, cg, cb) = frame.GetPixel(r.X + x, r.Y + y);
            on[y * r.Width + x] = ink(cr, cg, cb);
        }

        // Connected blobs (8 neighbours), as boxes.
        var boxes = new List<(int L, int R, int T, int B, int N)>();
        var seen = new bool[on.Length];
        var stack = new Stack<int>();
        for (int start = 0; start < on.Length; start++)
        {
            if (!on[start] || seen[start]) continue;
            int l = int.MaxValue, rr = -1, t = int.MaxValue, b = -1, n = 0;
            seen[start] = true;
            stack.Push(start);
            while (stack.Count > 0)
            {
                int p = stack.Pop();
                int x = p % r.Width, y = p / r.Width;
                n++;
                l = Math.Min(l, x); rr = Math.Max(rr, x); t = Math.Min(t, y); b = Math.Max(b, y);
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= r.Width || ny >= r.Height) continue;
                    int q = ny * r.Width + nx;
                    if (!on[q] || seen[q]) continue;
                    seen[q] = true;
                    stack.Push(q);
                }
            }
            if (n >= 3) boxes.Add((l, rr, t, b, n));
        }
        // Blobs touching the region's edge are background reaching in (or the label beside the number), not text.
        boxes.RemoveAll(b => b.L == 0 || b.T == 0 || b.R == r.Width - 1 || b.B == r.Height - 1);
        if (boxes.Count == 0) return [];

        // A glyph broken in two by blur (a thin stroke at 1080p): merge blobs that overlap in x.
        boxes.Sort((a, b) => a.L.CompareTo(b.L));
        var merged = new List<(int L, int R, int T, int B)>();
        foreach (var b in boxes)
        {
            if (merged.Count > 0 && merged[^1] is var m && Math.Min(m.R, b.R) - b.L >= Math.Min(m.R - m.L, b.R - b.L) / 2)
                merged[^1] = (m.L, Math.Max(m.R, b.R), Math.Min(m.T, b.T), Math.Max(m.B, b.B));
            else
                merged.Add((b.L, b.R, b.T, b.B));
        }
        var tallest = merged.MaxBy(b => b.B - b.T + 1);
        int height = tallest.B - tallest.T + 1;
        var glyphs = new List<Glyph>();
        // Characters stand on one line: as tall as the tallest nearly, and ending where it ends.
        foreach (var b in merged.Where(b => b.B - b.T + 1 >= height * minHeightShare && Math.Abs(b.B - tallest.B) <= height * 0.15))
        {
            foreach (var (left, right) in Split(on, r.Width, b.L, b.R, b.T, b.B))
            {
                // The part's own rows.
                int top = b.B, bottom = b.T;
                for (int y = b.T; y <= b.B; y++)
                for (int x = left; x <= right; x++)
                {
                    if (!on[y * r.Width + x]) continue;
                    top = Math.Min(top, y);
                    bottom = Math.Max(bottom, y);
                }
                if (bottom >= top) glyphs.Add(new Glyph(left, right, top, bottom, Normalise(on, r.Width, left, right, top, bottom)));
            }
        }
        return glyphs;
    }

    // A digit is at most 0.82 of its height wide ("4"); a blob wider than this may carry a streak
    // off its end, and one wider than WidestDigit is digits run together.
    private const double WiderThanADigit = 0.95, WidestDigit = 1.1, DigitWidth = 0.78;

    /// <summary>
    /// A blob of digits that blur has run together (two digits a dozen pixels tall touch at 1080p),
    /// cut into its digits at the emptiest columns near where a digit's width would end.
    /// </summary>
    private static IEnumerable<(int Left, int Right)> Split(bool[] on, int stride, int left, int right, int top, int bottom)
    {
        int height = bottom - top + 1;
        int Ink(int x)
        {
            int n = 0;
            for (int y = top; y <= bottom; y++) if (on[y * stride + x]) n++;
            return n;
        }
        if (right - left + 1 > height * WiderThanADigit)
        {
            // Too wide for a digit: first leave out a thin streak trailing off either end (a brush
            // stroke behind the streak's number touching it), columns with hardly any ink.
            int thin = Math.Max(1, height / 10);
            while (right > left && Ink(right) <= thin) right--;
            while (left < right && Ink(left) <= thin) left++;
        }
        int width = right - left + 1;
        if (width <= height * WidestDigit)
        {
            yield return (left, right);
            yield break;
        }
        int parts = Math.Max(2, (int)Math.Round(width / (height * DigitWidth)));
        int start = left;
        for (int k = 1; k < parts; k++)
        {
            int expected = left + width * k / parts, reach = width / parts / 3;
            var candidates = Enumerable.Range(expected - reach, 2 * reach + 1).Where(x => x > start + 1 && x < right - 1).ToList();
            if (candidates.Count == 0) break;
            int cut = candidates.MinBy(Ink);
            yield return (start, cut - 1);
            start = cut + 1;
        }
        yield return (start, right);
    }

    /// <summary>The glyph's ink scaled to the shapes' size: the share of each cell's patch that is ink.</summary>
    private static float[] Normalise(bool[] on, int stride, int left, int right, int top, int bottom)
    {
        int w = right - left + 1, h = bottom - top + 1;
        var mask = new float[ShapeWidth * ShapeHeight];
        for (int y = 0; y < ShapeHeight; y++)
        for (int x = 0; x < ShapeWidth; x++)
        {
            int x0 = left + x * w / ShapeWidth, x1 = Math.Max(x0 + 1, left + (x + 1) * w / ShapeWidth);
            int y0 = top + y * h / ShapeHeight, y1 = Math.Max(y0 + 1, top + (y + 1) * h / ShapeHeight);
            int inked = 0, all = 0;
            for (int yy = y0; yy < y1; yy++)
            for (int xx = x0; xx < x1; xx++)
            {
                all++;
                if (on[yy * stride + xx]) inked++;
            }
            mask[y * ShapeWidth + x] = inked / (float)all;
        }
        return mask;
    }

    private static double Correlation(float[] a, float[] b)
    {
        double ma = a.Average(), mb = b.Average(), ab = 0, aa = 0, bb = 0;
        for (int i = 0; i < a.Length; i++)
        {
            double da = a[i] - ma, db = b[i] - mb;
            ab += da * db;
            aa += da * da;
            bb += db * db;
        }
        return aa == 0 || bb == 0 ? 0 : ab / Math.Sqrt(aa * bb);
    }

    /// <summary>The mask as rows of cells 0–9 (tenths of ink), as <see cref="Shapes"/> are written.</summary>
    internal static string Describe(float[] mask) =>
        string.Join("|", Enumerable.Range(0, ShapeHeight).Select(y => new string([.. Enumerable.Range(0, ShapeWidth).Select(x => (char)('0' + (int)Math.Round(mask[y * ShapeWidth + x] * 9)))])));

    internal sealed record Shape(char Char, double Aspect, float[] Mask);

    private static Shape S(char c, double aspect, string rows) =>
        new(c, aspect, [.. rows.Split('|').SelectMany(row => row.Select(ch => (ch - '0') / 9f))]);

    /// <summary>
    /// The font's digits and '/': each the average of its glyphs on the inn frames at 4K (the timers,
    /// the fire meters and the streaks of Fixtures/inn; 7 and 9 were seen once each, the timer at
    /// 04:57 and the meter at 92/100). Aspect is the glyph's width over its height. The colon is told
    /// by its shape (<see cref="IsColon"/>).
    /// </summary>
    internal static readonly Shape[] Shapes =
    [
        S('/', 0.53, "0000000597|0000000993|0000005990|0000019940|0000059920|0000299400|0000697000|0001992000|0005980000|0029900000|0079400000|0399200000|0597000000|4982000000"),
        S('0', 0.79, "0015886300|0189999950|0796113892|3970000395|5940000197|8930000078|9910000079|9910000079|8920000079|7940000188|4960000296|0893001793|0399668970|0037897510"),
        S('1', 0.38, "0000005789|0000488999|0048889999|4888858999|6885106899|0111106899|0000006899|0000006899|0000006899|0000006899|0000006899|0000006899|0000006899|0000004887"),
        S('2', 0.71, "0057998610|0799999981|6972113895|4720000398|0000000299|0000000497|0000002893|0000038970|0000599500|0016982000|0079810000|3896100000|9997777776|9999999998"),
        S('3', 0.72, "0037999620|0499999992|4983112798|3740000199|0000000198|0000001694|0000889940|0000888981|0000000596|0000000198|4610000199|7961000597|2998767993|0158999720"),
        S('4', 0.82, "0000178820|0000599930|0002888930|0007946930|0039606930|0189106930|0594006930|2980006930|7963337952|9999999998|5666669975|0000006930|0000006930|0000005920"),
        S('5', 0.73, "0399999871|0698888882|0684333320|0880000000|1970010000|3986999610|4998778981|3871001795|0000000397|0000000299|3610000398|7862002695|1898778981|0158998510"),
        S('6', 0.77, "0000189100|0000695000|0004960000|0029910000|0189311000|0698788720|2998758992|5992000796|7960000298|8950000199|7960000298|5993001696|0698768992|0047998620"),
        S('7', 0.70, "4746444200|7999998078|0000000048|0000000074|0000000473|0000000640|0000003700|0000008400|0000065000|0000292000|0000740000|0004930000|0008700000|0026000000"),
        S('8', 0.74, "0038998400|0498436960|0970120493|2942331194|1963331393|2495223860|2149999600|0499869950|2971000494|6910000087|9900000079|6940000187|1894333894|0168998620"),
        S('9', 0.75, "0049999720|0997447992|7970000296|9900000078|9900000049|9900000049|7920000296|2994444994|0049994992|0000007940|0000029700|0000299000|0000792000|0007920000"),
    ];
}
