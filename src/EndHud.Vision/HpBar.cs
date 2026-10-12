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

    // The fill is a saturated, bright colour (cyan when healthy). The track and recent-damage
    // trail are grey, i.e. unsaturated, so they don't count.
    private static bool IsFill(byte r, byte g, byte b)
    {
        int min = Math.Min(r, Math.Min(g, b));
        int max = Math.Max(r, Math.Max(g, b));
        return max >= 150 && max - min >= 90;
    }
}
