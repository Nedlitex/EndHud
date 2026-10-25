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
    // The fill's middle column (darkest channel): 151-175 grey, up to 255 white, on every bar of the
    // fixtures; a white bar under a display's 55 % black backdrop 111-114.
    private const int MinFillLevel = 125;
    // An empty track against the rows above or below it: 14-116 darker on the fixtures' real bars.
    private const double TrackContrast = 8;
    private const double MinBarCoverage = 0.9;
    private const double MinFillFromLeft = 0.9;

    // The game lays its HUD out afresh at each screen size rather than scaling the 4K layout, so
    // the bar can sit a row or two off the scaled position: at a native 1920x1080 it's on rows
    // 987–989, where scaled 4K puts it on 988–990. Sampling the scaled rows there takes half of
    // each column from the scenery under the bar, which reads as art or as the wrong fill.
    private const int MaxShiftUp = 2;
    private const int MaxShiftDown = 1;
    // A bar's column is one colour top to bottom (JPEG noise: a few levels); scenery isn't. Each
    // row moved costs this much, so the scaled position wins unless it straddles an edge.
    private const double ShiftCost = 3;

    /// <returns>The reading, or null when no bar is drawn there.</returns>
    public static ComboReading? Measure(Frame frame, PixelRect bar)
    {
        var r = Locate(frame, bar).ClampTo(frame.Width, frame.Height);
        if (r.IsEmpty) return null;

        var isFill = new bool[r.Width];
        int fill = 0;
        double fillBrightness = 0;
        var fillLevels = new List<int>(r.Width);
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
                fillLevels.Add(min);
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
        // The fill is grey (151 or more, the middle column) or white: dimmer is the bar seen through
        // something dark, an on-screen display's backdrop (a full white bar at 45 % is 114).
        if (fill > 0 && fillLevels.Order().ElementAt(fill / 2) < MinFillLevel) return null;
        // An empty track is a thin dark line: darker than the rows just above or just below it. A
        // flat dark area (a display's backdrop over scenery) is dark there too.
        if (fill == 0 && !StandsOut(frame, r, bar.Height / (double)PartyLayout.ComboBar.Height)) return null;

        double progress = Math.Min(1, fill / (double)r.Width);
        // Full and white means ready; a full bar tinted pink at low HP is ready too.
        bool white = fill > 0 && fillBrightness / fill >= 215;
        return new ComboReading(progress, Ready: progress >= 0.99 || (progress >= 0.97 && white));
    }

    /// <summary>
    /// <paramref name="bar"/> moved up or down to where the bar's rows are: the position whose
    /// columns change least from row to row (judged over at least two rows).
    /// </summary>
    private static PixelRect Locate(Frame frame, PixelRect bar)
    {
        int rows = Math.Max(2, bar.Height);
        int bestShift = 0;
        double bestCost = double.MaxValue;
        for (int dy = -MaxShiftUp; dy <= MaxShiftDown; dy++)
        {
            var r = new PixelRect(bar.X, bar.Y + dy, bar.Width, rows).ClampTo(frame.Width, frame.Height);
            if (r.Height < rows || r.Width == 0) continue;
            double cost = VerticalChange(frame, r) + Math.Abs(dy) * ShiftCost;
            if (cost < bestCost) (bestShift, bestCost) = (dy, cost);
        }
        return bar.Offset(0, bestShift);
    }

    /// <summary>How much each column's colour changes between its top and bottom row, on average.</summary>
    private static double VerticalChange(Frame frame, PixelRect r)
    {
        double total = 0;
        for (int x = r.X; x < r.Right; x++)
        {
            int minR = 255, minG = 255, minB = 255, maxR = 0, maxG = 0, maxB = 0;
            for (int y = r.Y; y < r.Bottom; y++)
            {
                var (cr, cg, cb) = frame.GetPixel(x, y);
                (minR, maxR) = (Math.Min(minR, cr), Math.Max(maxR, cr));
                (minG, maxG) = (Math.Min(minG, cg), Math.Max(maxG, cg));
                (minB, maxB) = (Math.Min(minB, cb), Math.Max(maxB, cb));
            }
            total += Math.Max(maxR - minR, Math.Max(maxG - minG, maxB - minB));
        }
        return total / r.Width;
    }

    /// <summary>
    /// The bar's rows darker, by <see cref="TrackContrast"/>, than the rows 4-8 above it (4K; the portrait's
    /// bottom or the level text's place) or 2-4 below it (the gap over the HP bar).
    /// </summary>
    private static bool StandsOut(Frame frame, PixelRect r, double scale)
    {
        double track = Brightness(frame, r.X, r.Right, r.Y, r.Bottom);
        int above0 = r.Y - (int)Math.Round(8 * scale), above1 = Math.Max(above0 + 1, r.Y - (int)Math.Round(4 * scale) + 1);
        int below0 = r.Bottom + (int)Math.Round(2 * scale), below1 = Math.Max(below0 + 1, r.Bottom + (int)Math.Round(4 * scale) + 1);
        double above = Brightness(frame, r.X, r.Right, above0, above1), below = Brightness(frame, r.X, r.Right, below0, below1);
        return above - track >= TrackContrast || below - track >= TrackContrast;
    }

    /// <summary>Mean brightest channel over the rows from <paramref name="y0"/> to <paramref name="y1"/> (clamped to the frame).</summary>
    private static double Brightness(Frame frame, int x0, int x1, int y0, int y1)
    {
        y0 = Math.Max(0, y0);
        y1 = Math.Min(frame.Height, y1);
        double sum = 0;
        int n = 0;
        for (int y = y0; y < y1; y++)
        for (int x = x0; x < x1; x++)
        {
            var (cr, cg, cb) = frame.GetPixel(x, y);
            sum += Math.Max(cr, Math.Max(cg, cb));
            n++;
        }
        return n == 0 ? 0 : sum / n;
    }

    private static double StdDev(List<int> values)
    {
        double mean = values.Average();
        return Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / values.Count);
    }
}
