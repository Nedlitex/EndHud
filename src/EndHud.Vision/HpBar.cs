namespace EndHud.Vision;

/// <summary>Measures how full an HP bar is: the block of coloured columns from its left end; the empty track is dark grey.</summary>
public static class HpBar
{
    /// <returns>Fraction filled in [0, 1], or null when the region is outside the frame.</returns>
    public static double? Measure(Frame frame, PixelRect bar)
    {
        var r = bar.ClampTo(frame.Width, frame.Height);
        if (r.IsEmpty) return null;

        // Sample the middle third of the bar's height; edges blend into the outline.
        int y0 = r.Y + r.Height / 3;
        int y1 = Math.Max(y0 + 1, r.Y + r.Height * 2 / 3);
        // The fill is one block from the bar's left end. Coloured columns elsewhere are scenery:
        // the red and cyan fringes around rocks on a white sky read 4–8% in a controller recording
        // where no panel was drawn, and with white "combo bars" passed for the squad panel.
        int maxGap = Math.Max(MinGapColumns, (int)Math.Round(r.Width * MaxGapShare));
        int end = 0, gap = 0, blockColumns = 0, resumed = 0, tinted = 0, tintedFromLeft = 0, runStart = -1;
        double blockBrightness = 0;
        bool ended = false, light = false, lightThenDark = false, lightNotFill = false;
        var columnLevels = new List<int>(r.Width);
        for (int x = r.X; x < r.Right; x++)
        {
            int hits = 0, brightness = 0, colour = 0;
            for (int y = y0; y < y1; y++)
            {
                var (cr, cg, cb) = frame.GetPixel(x, y);
                if (IsFill(cr, cg, cb)) hits++;
                int max = Math.Max(cr, Math.Max(cg, cb));
                brightness += max;
                colour += max - Math.Min(cr, Math.Min(cg, cb));
            }
            bool fill = hits * 2 > y1 - y0;
            brightness /= y1 - y0;
            columnLevels.Add(brightness);
            if (colour / (y1 - y0) >= TintedFrom && brightness < DimBelow)
            {
                tinted++;
                // A run starting at the bar's left end (its soft end left out).
                if (runStart < 0) runStart = x - r.X;
                if (runStart <= maxGap) tintedFromLeft = Math.Max(tintedFromLeft, x - r.X + 1 - runStart);
            }
            else runStart = -1;
            if (!ended && fill)
            {
                end = x - r.X + 1;
                gap = 0;
                blockColumns++;
                blockBrightness += brightness;
                light = true;
                continue;
            }
            if (!ended && ++gap > maxGap) ended = true;
            bool lit = fill || brightness >= LightFrom;
            if (brightness < DarkBelow) lightThenDark |= light;
            else if (lit && ended && (lightThenDark || (blockColumns == 0 && fill && lightNotFill))
                     && brightness >= ResumedShare * (blockColumns == 0 ? 0 : blockBrightness / blockColumns)) resumed++;
            light |= lit;
            lightNotFill |= lit && !fill;
        }
        // Light, then black, then light again (or the fill going on, as bright) right of where the
        // fill stops, or the fill starting only after something light at the bar's left end: text
        // drawn across the bar (an on-screen display's, outlined in black), so where the fill ends
        // can't be told. The bar itself is fill from its left end, then the dark track to its end.
        if (resumed > maxGap) return null;
        // No fill from the left, yet the bar coloured from its left end or over most of it: not the
        // empty bar's grey track, but the fill dimmed out of reach (a display's dark backdrop over it).
        if (end == 0 && (tinted * 2 > r.Width || tintedFromLeft > maxGap)) return null;
        // No fill, yet the bar lighter than its dark empty track (23-37 on the fixtures, fallen or not):
        // a light fill dimmed below the fill colours (a pale orange bar under a display's backdrop, 114).
        if (end == 0 && Median(columnLevels) is >= EmptyTrackBelow and < DimmestFill) return null;
        // ...or darker than it, yet not black: the empty track itself under a display's backdrop (9-16),
        // where whether the operator has fallen can't be told either (its death mark is dimmed too).
        if (end == 0 && Median(columnLevels) is >= BlackBelow and < EmptyTrackFrom) return null;
        // A fill dimmer than the game ever dims it (its squad dimmed: teal 146, dull pink 137): seen
        // through something dark, a display's backdrop (cyan at 45 % is 111). The portrait above it is too.
        if (blockColumns > 0 && blockBrightness / blockColumns < DimmestFill) return null;
        return end / (double)r.Width;
    }

    /// <summary>The fill's brightness (brightest channel, mean over the block) under which the bar is seen through something.</summary>
    private const int DimmestFill = 125;

    /// <summary>An empty bar's columns (brightest channel, the middle one) are darker than this.</summary>
    private const int EmptyTrackBelow = 70;

    /// <summary>...and at least this bright (23 on the fixtures); under <see cref="BlackBelow"/> it is black (a frame's masked or unlit part).</summary>
    private const int EmptyTrackFrom = 20;

    private const int BlackBelow = 5;

    private static int Median(List<int> values)
    {
        values.Sort();
        return values[values.Count / 2];
    }

    /// <summary>A column darker than this (brightest channel, mean): black, as an overlay's text outline or shadow.</summary>
    private const int DarkBelow = 50;

    /// <summary>A column at least this bright, or fill-coloured, is light.</summary>
    private const int LightFrom = 120;

    /// <summary>Light after the black counts when at least this share as bright as the fill block (dimmed with it under a display's backdrop).</summary>
    private const double ResumedShare = 0.75;

    /// <summary>A column this coloured (brightest minus darkest channel, mean) isn't grey track.</summary>
    private const int TintedFrom = 40;

    /// <summary>...and this dim: the fill under a dark backdrop (cyan 246 at 45 % is 111), not lit scenery.</summary>
    private const int DimBelow = 120;

    // Columns the block may skip (its left end blending into the outline, a spark over it) and
    // still count as one block.
    private const int MinGapColumns = 2;
    private const double MaxGapShare = 0.03;

    // The fill is a saturated colour: cyan when healthy (~35,220,246), pink at low HP
    // (~235,130,155), and darker when the game dims the squad: teal (~35,116,146) or dull pink
    // (~137,75,87, saturation ~60). The track, the recent-damage trail and a fallen operator's
    // empty bar are grey (saturation < 20).
    // Only those two colour families count: a heal sweeps a green glow (hue 84–99°) over the
    // empty part of the bar, while the fill under it stays cyan-ish (148–180°).
    private static bool IsFill(byte r, byte g, byte b)
    {
        int min = Math.Min(r, Math.Min(g, b));
        int max = Math.Max(r, Math.Max(g, b));
        if (max < 100 || max - min < 45) return false;
        double hue = Hue(r, g, b, max, min);
        return hue is >= 140 and <= 240 // cyan / teal
            || hue >= 320 || hue <= 20; // pink / red
    }

    private static double Hue(int r, int g, int b, int max, int min)
    {
        double d = max - min;
        double h = max == r ? (g - b) / d % 6 : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        h *= 60;
        return h < 0 ? h + 360 : h;
    }
}
