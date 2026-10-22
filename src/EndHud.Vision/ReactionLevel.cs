namespace EndHud.Vision;

/// <summary>
/// The level badge on a reaction's icon in the boss's status row (Corrosion, Electrification): a dark
/// square over the icon's bottom-right corner with the level as a Roman numeral I–IV, drawn in the
/// reaction's colour (yellow-green, yellow) with serifs at top and bottom. The serifs can join the
/// strokes, so the numeral is read in two bands between them: an "I" is one vertical run of ink in
/// both bands, at the same place; a "V" is two runs in the upper band (its arms) that close into one
/// in the lower band, between them. The runs must spell I, II, III or IV; anything else is null.
/// Only reactions whose badge has been seen are read (<see cref="Measured"/>).
/// </summary>
public static class ReactionLevel
{
    /// <summary>Reactions whose level badge has been measured on the owner's recordings.</summary>
    public static IReadOnlySet<string> Measured { get; } = new HashSet<string>(StringComparer.Ordinal) { "Corrosion", "Electrification" };

    // The badge relative to the status's inner square (BossStatusReader.FirstSquare, 40 px at 4K), as
    // fractions of its width. Measured on Fixtures/reactions: the dark badge spans x 21-52 and y 18-53
    // px from the square's corner (it overlaps the frame), the numeral x 27-50 and y 28-46. The box
    // starts at 27 px, right of the icon art (Electrification's lower wave reaches x 26).
    internal const double BadgeFrom = 0.68, BadgeTo = 1.3;

    // A numeral pixel: saturated yellow to yellow-green. Measured: Corrosion's numeral hue 72-83°,
    // Electrification's 41-60°.
    internal const double MinInkHue = 35, MaxInkHue = 95, MinInkSaturation = 0.45, MinInkValue = 0.5;

    // The badge's background: of the box's pixels that aren't ink, the share darker than DarkBelow
    // (max channel). Measured below in MinDarkShare's comment.
    internal const int DarkBelow = 100;
    internal const double MinDarkShare = 0.5;

    // The numeral is at least this tall (fraction of the square's width; 18-19 px of 40 at 4K).
    internal const double MinNumeralHeight = 0.3;

    // The two bands between the serifs, as fractions of the numeral's height from its top.
    private const double UpperFrom = 0.3, UpperTo = 0.45, LowerFrom = 0.6, LowerTo = 0.75;

    // How far a run may move between the bands and still be an upright "I", as a fraction of the
    // square's width (1.2 px at 4K). Measured: the I strokes moved at most 1.0 px at 4K (0.75 of this),
    // 1.0 px on a simulated 1440p screen (over it: unknown) and 0.5 px at 1080p. A V's arm caught in
    // both bands (a dim IV over a black scene, fullbattletest 107 s) moved 2.0 px.
    internal const double SameColumn = 0.03;

    /// <summary>The level (1–4) on the badge of the icon in <paramref name="square"/>, or null.</summary>
    public static int? ReadLevel(Frame frame, PixelRect square) => Measure(frame, square).Level;

    /// <summary>What the reading is based on, for measuring thresholds.</summary>
    internal static (double DarkShare, int Height, double[] Upper, double[] Lower, int? Level) Measure(Frame frame, PixelRect square)
    {
        int x0 = square.X + (int)Math.Round(square.Width * BadgeFrom), x1 = square.X + (int)Math.Round(square.Width * BadgeTo);
        int y0 = square.Y + (int)Math.Round(square.Height * BadgeFrom), y1 = square.Y + (int)Math.Round(square.Height * BadgeTo);
        if (x0 < 0 || y0 < 0 || x1 > frame.Width || y1 > frame.Height || x1 - x0 < 6) return (0, 0, [], [], null);

        int w = x1 - x0, h = y1 - y0;
        var ink = new bool[w, h];
        int dark = 0, other = 0;
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            var (r, g, b) = frame.GetPixel(x0 + x, y0 + y);
            ink[x, y] = IsInk(r, g, b);
            if (ink[x, y]) continue;
            other++;
            if (Math.Max(r, Math.Max(g, b)) < DarkBelow) dark++;
        }
        double darkShare = other == 0 ? 0 : dark / (double)other;

        int top = -1, bottom = -1;
        for (int y = 0; y < h; y++)
        {
            int count = 0;
            for (int x = 0; x < w; x++) if (ink[x, y]) count++;
            if (count < 2) continue;
            if (top < 0) top = y;
            bottom = y;
        }
        int height = top < 0 ? 0 : bottom - top + 1;
        if (height == 0) return (darkShare, 0, [], [], null);
        var upper = Runs(ink, w, top + (int)Math.Round(height * UpperFrom), top + (int)Math.Round(height * UpperTo));
        var lower = Runs(ink, w, top + (int)Math.Round(height * LowerFrom), top + (int)Math.Round(height * LowerTo));
        bool badge = darkShare >= MinDarkShare && height >= square.Width * MinNumeralHeight;
        return (darkShare, height, upper, lower, badge ? Spell(upper, lower, square.Width * SameColumn) : null);
    }

    /// <summary>
    /// The level the runs spell: n runs in both bands, each in the same place (I, II, III); or one run
    /// in the same place, then two upper runs closing into one lower run between them (IV).
    /// </summary>
    /// <param name="same">How far (px) a run may move between the bands and still be an upright "I".</param>
    internal static int? Spell(double[] upper, double[] lower, double same)
    {
        if (upper.Length == lower.Length && upper.Length is >= 1 and <= 3)
            return upper.Zip(lower).All(p => Math.Abs(p.First - p.Second) <= same) ? upper.Length : null;
        if (upper.Length == 3 && lower.Length == 2
            && Math.Abs(upper[0] - lower[0]) <= same
            && lower[1] > upper[1] + 0.5 && lower[1] < upper[2] - 0.5)
            return 4;
        return null;
    }

    /// <summary>The centres (x) of the runs of columns with ink in rows <paramref name="from"/>..<paramref name="to"/>.</summary>
    private static double[] Runs(bool[,] ink, int w, int from, int to)
    {
        var centres = new List<double>();
        int start = -1;
        for (int x = 0; x <= w; x++)
        {
            bool any = false;
            if (x < w)
                for (int y = from; y <= to && y < ink.GetLength(1); y++)
                    if (ink[x, y]) { any = true; break; }
            if (any && start < 0) start = x;
            else if (!any && start >= 0)
            {
                centres.Add((start + x - 1) / 2.0);
                start = -1;
            }
        }
        return [.. centres];
    }

    private static bool IsInk(byte r, byte g, byte b)
    {
        double rf = r / 255.0, gf = g / 255.0, bf = b / 255.0;
        double max = Math.Max(rf, Math.Max(gf, bf)), min = Math.Min(rf, Math.Min(gf, bf)), d = max - min;
        if (max < MinInkValue || d / max < MinInkSaturation) return false;
        double hue = max == rf ? 60 * ((gf - bf) / d % 6) : max == gf ? 60 * ((bf - rf) / d + 2) : 60 * ((rf - gf) / d + 4);
        if (hue < 0) hue += 360;
        return hue >= MinInkHue && hue <= MaxInkHue;
    }
}
