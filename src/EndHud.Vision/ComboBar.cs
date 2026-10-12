namespace EndHud.Vision;

/// <param name="Progress">Cooldown progress in [0, 1]: how much of the bar has refilled.</param>
/// <param name="Ready">The bar is full: the combo skill can be used.</param>
public sealed record ComboReading(double Progress, bool Ready);

/// <summary>
/// Reads the combo cooldown bar above each squad HP bar: a dark track that refills with grey
/// from the left while cooling down, and is full (white) when ready. It is only drawn in
/// combat; outside combat the same rows show the bottom of the level text and portrait art,
/// which must not be mistaken for a bar. At low HP a pink frame surrounds the slot and tints
/// the whole bar.
/// </summary>
public static class ComboBar
{
    // Measured over combat, low-HP and exploration frames (same rows):
    //   track brightness: ≤ 28 normally, 44–60 when tinted pink at low HP — but art behind an
    //   absent bar is in the same range, so darkness alone can't tell them apart;
    //   track evenness (std-dev across the track): 3.7–6.4 on real bars, ≥ 15 for art behind
    //   an absent bar that otherwise looks like one.
    private const int TrackMaxBrightness = 80;
    private const double MaxTrackUnevenness = 9;
    private const int MinTrackColumnsForEvenness = 8;
    private const int FillEdgeColumns = 4;
    private const int EndCapColumns = 3;
    private const double MinBarCoverage = 0.9;
    private const double MinFillFromLeft = 0.9;

    /// <returns>The reading, or null when no bar is drawn there.</returns>
    public static ComboReading? Measure(Frame frame, PixelRect bar)
    {
        var r = bar.ClampTo(frame.Width, frame.Height);
        if (r.IsEmpty) return null;

        var isFill = new bool[r.Width];
        int fill = 0;
        double fillBrightness = 0;
        var trackBrightness = new List<(int Column, int Brightness)>(r.Width);
        for (int i = 0; i < r.Width; i++)
        {
            int x = r.X + i;
            int sr = 0, sg = 0, sb = 0;
            for (int y = r.Y; y < r.Bottom; y++)
            {
                var (cr, cg, cb) = frame.GetPixel(x, y);
                sr += cr; sg += cg; sb += cb;
            }
            int red = sr / r.Height, green = sg / r.Height, blue = sb / r.Height;
            int max = Math.Max(red, Math.Max(green, blue)), min = Math.Min(red, Math.Min(green, blue));
            if (min >= 110 && max - min <= 50)
            {
                isFill[i] = true;
                fill++;
                fillBrightness += min;
            }
            else if (max < TrackMaxBrightness)
            {
                trackBrightness.Add((i, max));
            }
        }

        // A real bar: almost every column is fill or dark track…
        if (fill + trackBrightness.Count < r.Width * MinBarCoverage) return null;
        // …the fill grows from the left edge as one piece…
        if (fill > 0 && isFill.Take(fill).Count(f => f) < fill * MinFillFromLeft) return null;
        // …and the track is one flat colour, not art. Leave out the soft edge where the fill
        // meets the track and the bar's end cap: on a nearly full bar they'd be most of the track.
        var flat = trackBrightness
            .Where(t => t.Column >= fill + FillEdgeColumns && t.Column < r.Width - EndCapColumns)
            .Select(t => t.Brightness)
            .ToList();
        if (flat.Count >= MinTrackColumnsForEvenness && StdDev(flat) > MaxTrackUnevenness) return null;

        double progress = Math.Min(1, fill / (double)r.Width);
        // Full and white means ready; a full bar tinted pink at low HP is ready too.
        bool white = fill > 0 && fillBrightness / fill >= 215;
        return new ComboReading(progress, Ready: progress >= 0.99 || (progress >= 0.97 && white));
    }

    private static double StdDev(List<int> values)
    {
        double mean = values.Average();
        return Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / values.Count);
    }
}
