namespace EndHud.Vision;

/// <summary>Measures how full an HP bar is by counting coloured columns; the empty track is dark grey.</summary>
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
        int filled = 0;
        for (int x = r.X; x < r.Right; x++)
        {
            int hits = 0;
            for (int y = y0; y < y1; y++)
            {
                var (cr, cg, cb) = frame.GetPixel(x, y);
                if (IsFill(cr, cg, cb)) hits++;
            }
            if (hits * 2 > y1 - y0) filled++;
        }
        return filled / (double)r.Width;
    }

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
