namespace EndHud.Vision;

/// <param name="Progress">Cooldown progress in [0, 1]: how much of the bar has refilled.</param>
/// <param name="Ready">The bar is solid white: the 连携 (combo skill) can be used.</param>
public sealed record ComboReading(double Progress, bool Ready);

/// <summary>
/// Reads the 连携 cooldown bar above each squad HP bar: a black track that refills with grey
/// from the left while cooling down, and turns solid white when ready.
/// </summary>
public static class ComboBar
{
    /// <returns>The reading, or null when no bar is drawn there (outside combat the level text shows instead).</returns>
    public static ComboReading? Measure(Frame frame, PixelRect bar)
    {
        var r = bar.ClampTo(frame.Width, frame.Height);
        if (r.IsEmpty) return null;

        int y0 = r.Y + r.Height / 4;
        int y1 = Math.Max(y0 + 1, r.Bottom - r.Height / 4);
        int fill = 0, track = 0;
        double fillBrightness = 0;
        for (int x = r.X; x < r.Right; x++)
        {
            int sr = 0, sg = 0, sb = 0, n = 0;
            for (int y = y0; y < y1; y++)
            {
                var (cr, cg, cb) = frame.GetPixel(x, y);
                sr += cr; sg += cg; sb += cb;
                n++;
            }
            int red = sr / n, green = sg / n, blue = sb / n;
            int max = Math.Max(red, Math.Max(green, blue)), min = Math.Min(red, Math.Min(green, blue));

            if (min >= 110 && max - min <= 50)
            {
                fill++;
                fillBrightness += min;
            }
            else if (max < 70)
            {
                track++;
            }
        }

        // Anything else (portrait art, effects) means this isn't the bar.
        if (fill + track < r.Width * 0.85) return null;

        double progress = fill / (double)r.Width;
        bool white = fill > 0 && fillBrightness / fill >= 215;
        return new ComboReading(Math.Min(1, progress), Ready: progress >= 0.97 && white);
    }
}
