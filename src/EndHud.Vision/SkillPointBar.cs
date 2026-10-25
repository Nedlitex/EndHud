namespace EndHud.Vision;

/// <summary>
/// The skill-point bar seen: <see cref="Value"/> points of <see cref="Max"/> (its segment count); the value null when the
/// segments are there but their fill can't be read (an effect over them).
/// </summary>
public readonly record struct SkillPointReading(double? Value, int Max);

/// <summary>
/// The team's skill points (技力): a row of segments above the controlled operator's HP bar, bottom centre, shown in
/// combat only. Usually three; some fights draw more (seven in the owner's boss fight, 2026-10-25), and the count is
/// read from the bar itself, not assumed. The row keeps its width (x 1598-2241 at 4K, on the three- and the
/// seven-segment screenshots) and the segments share it with the same gap (about 15 px), so more segments are narrower.
/// A full segment is yellow; the one filling up is white from the left; the rest are an empty, see-through track
/// (game effects show through it). A grey trail after the white is points just spent, fading, and doesn't count.
/// </summary>
public static class SkillPointBar
{
    /// <summary>The fewest segments told apart: one gap is needed to measure where the segments are.</summary>
    public const int MinSegments = 2;

    /// <summary>
    /// The most segments looked for (owner, 2026-10-25: "if we need a max, use 20"). The geometry would hold more at 4K (a
    /// segment is 658/n - 15 px wide there, its readable inside 5 px less), but a segment must keep
    /// <see cref="MinColumns"/> on screen: twenty do from 1920x1080 up; at 1280x720 up to seventeen do.
    /// </summary>
    public const int MaxSegments = 20;

    // The row at 4K, from the first segment's left edge to the last one's right edge, and the gap between segments.
    private const double RowLeft = 1598, RowRight = 2241, Gap = 15;
    // The rows read for the fill (a segment's inner rows), and the rows its edges are looked for over.
    private const int FillTop = 1958, FillHeight = 7;
    private const int EdgeTop = 1955, EdgeHeight = 12;

    // A segment counts as full from this share of yellow columns (the end caps are drawn darker).
    private const double FullAt = 0.9;
    // Each segment starts with a few dark columns (its rounded end cap): about 2.5% of its width.
    private const double EndCap = 0.06;
    /// <summary>
    /// The narrowest segment read, in screen columns: narrower, a part-filled one's white is a column or two blurred
    /// into the track and reads as nothing (20 segments at 1280x720 are 4 columns). Unknown is better than wrong.
    /// </summary>
    public const int MinColumns = 6;
    // Lit columns allowed past where the fill should end: more means an effect is on the bar.
    private const double StrayAllowed = 0.1;

    // A count is taken when the edges at its gaps stand out from its segments' insides by this much (mean brightness
    // step per column, 0-255): 13 or more on every bar in the fixtures at 4K, 1080p and 720p; at most 7.4 on frames
    // without the bar (a cutscene, a dialog, exploring).
    private const double MinEdgeScore = 10;
    // How far into a segment (reference px) its own edges are: the rim, and a full one's bright inner bar starting.
    private const double ClearOfEdges = 8;
    // Narrowest segment pitch looked for, in screen pixels.
    private const double MinPitch = 8;

    /// <summary>
    /// Skill points and the bar's segment count: 2.4 of 3 = two full, the third 40%. Null when no bar is seen (not on
    /// screen, or faded); the value alone null when its segments are seen but their fill doesn't look like the bar's (an
    /// effect over it). Read it only in combat: out of combat the bar isn't drawn.
    /// </summary>
    public static SkillPointReading? Measure(Frame frame, UiScaler ui) =>
        SegmentCount(frame, ui) is { } segments ? new SkillPointReading(Points(frame, ui, segments), segments) : null;

    private static double? Points(Frame frame, UiScaler ui, int segments)
    {
        double value = 0;
        for (int s = 0; s < segments; s++)
        {
            var columns = Columns(frame, ui.BottomCenter(Segment(segments, s)));
            if (columns is null) return null;
            int n = columns.Length;
            int yellow = columns.Count(c => c == Lit.Yellow);
            if (yellow >= n * FullAt)
            {
                value++;
                continue;
            }

            // The filling segment: white (or yellow, as it turns full) from the left, after the
            // segment's dark end cap, and nothing after the fill.
            int start = 0;
            // At least 2 columns: a narrow segment (many of them, or a small screen) still has its cap.
            while (start < Math.Max(2, n * EndCap) && columns[start] == Lit.None) start++;
            int run = start;
            while (run < n && columns[run] != Lit.None) run++;
            if (columns.Skip(run).Count(c => c != Lit.None) > n * StrayAllowed) return null;
            value += (run - start) / (double)(n - start);

            // Segments after the filling one are empty.
            for (int later = s + 1; later < segments; later++)
            {
                var rest = Columns(frame, ui.BottomCenter(Segment(segments, later)));
                if (rest is null || rest.Count(c => c != Lit.None) > rest.Length * StrayAllowed) return null;
            }
            return value;
        }
        return value;
    }

    private static double Pitch(int segments) => (RowRight - RowLeft + Gap) / segments;

    /// <summary>Segment <paramref name="s"/>'s inner rows at 4K (its box, less the rim), anchored to the bottom centre.</summary>
    internal static PixelRect Segment(int segments, int s)
    {
        double pitch = Pitch(segments), left = RowLeft + s * pitch;
        return new((int)Math.Round(left + 2), FillTop, (int)Math.Round(pitch - Gap - 5), FillHeight);
    }

    /// <summary>
    /// How many segments the bar has, from where its edges are: each segment is a box with a rim, so at a gap the
    /// brightness steps twice (out of one box, into the next) whatever is behind, full or empty. For each count, the
    /// steps at its gaps against the steps inside its segments; null when no count's gaps stand out (no bar there).
    /// </summary>
    internal static int? SegmentCount(Frame frame, UiScaler ui)
    {
        var counts = CountScores(frame, ui);
        if (counts.Count == 0) return null;
        var best = counts.MaxBy(c => c.Score);
        if (best.Score < MinEdgeScore) return null;
        // A divisor of the real count scores as well as the real one (three segments' gaps are among six's), and by
        // the scene behind it can score better: a multiple is the count when its own extra gaps show too. A wrong
        // multiple's extra gaps fall inside segments, where nothing steps twice 15 px apart.
        int taken = best.Segments;
        foreach (var more in counts.Where(c => c.Segments > best.Segments && c.Segments % best.Segments == 0))
        {
            int every = more.Segments / best.Segments;
            // The median of the extra gaps: a few strong ones (the real count's, among a wrong multiple's) don't carry it.
            var extra = more.Gaps.Where((_, k) => (k + 1) % every != 0).Order().ToList();
            if (extra[(extra.Count - 1) / 2] - more.Inside >= MinEdgeScore) taken = more.Segments;
        }
        return taken;
    }

    /// <summary>
    /// Each count's evidence (<see cref="SegmentCount"/>): the edges at each of its gaps (the weaker of a gap's two, so
    /// one line in the scene or a fill's end isn't a gap), and the steps inside its segments (the median, away from
    /// their rims); its score is the first's mean less the second.
    /// </summary>
    internal static List<(int Segments, double Score, double[] Gaps, double Inside)> CountScores(Frame frame, UiScaler ui)
    {
        var counts = new List<(int Segments, double Score, double[] Gaps, double Inside)>();
        var band = ui.BottomCenter(new PixelRect((int)RowLeft - 10, EdgeTop, (int)(RowRight - RowLeft) + 20, EdgeHeight)).ClampTo(frame.Width, frame.Height);
        if (band.IsEmpty || band.Width < 20) return counts;

        // The mean brightness step between neighbouring columns: step[i] is between columns i and i + 1.
        var step = new double[band.Width - 1];
        for (int y = band.Y; y < band.Bottom; y++)
        {
            int previous = Brightness(frame, band.X, y);
            for (int x = band.X + 1; x < band.Right; x++)
            {
                int here = Brightness(frame, x, y);
                step[x - band.X - 1] += Math.Abs(here - previous);
                previous = here;
            }
        }
        for (int i = 0; i < step.Length; i++) step[i] /= band.Height * 3.0;

        // A reference x at 4K to an index in step, as UiScaler places the band; the strongest step within reach (screen px).
        double scale = ui.Scale, origin = ui.BottomCenter(new PixelRect(0, EdgeTop, 1, 1)).X;
        double Strongest(double x, int reach)
        {
            int i = (int)Math.Round(origin + x * scale) - band.X;
            double best = 0;
            for (int j = Math.Max(0, i - reach); j <= Math.Min(step.Length - 1, i + reach); j++) best = Math.Max(best, step[j]);
            return best;
        }

        for (int n = MinSegments; n <= MaxSegments; n++)
        {
            double pitch = Pitch(n), width = pitch - Gap;
            if (pitch * scale < MinPitch) break;
            var gaps = new double[n - 1];
            for (int k = 1; k < n; k++)
                gaps[k - 1] = Math.Min(Strongest(RowLeft + k * pitch - Gap, 2), Strongest(RowLeft + k * pitch, 2));
            // Inside: at a quarter, half and three quarters, or the middle alone in a segment too narrow to have its
            // quarters clear of the rim and of where a full segment's bright inner bar starts (5 px in).
            double[] at = width / 4 >= ClearOfEdges ? [width / 4, width / 2, width * 3 / 4] : [width / 2];
            var inside = new List<double>();
            for (int k = 0; k < n; k++)
                foreach (double x in at)
                    inside.Add(Strongest(RowLeft + k * pitch + x, 0));
            inside.Sort();
            double median = inside[inside.Count / 2];
            counts.Add((n, gaps.Average() - median, gaps, median));
        }
        return counts;
    }

    /// <summary>Red + green + blue (the mean is taken once, over the steps).</summary>
    private static int Brightness(Frame frame, int x, int y)
    {
        var (r, g, b) = frame.GetPixel(x, y);
        return r + g + b;
    }

    private enum Lit { None, White, Yellow }

    /// <summary>Each column of a segment: mostly yellow, mostly white, or neither.</summary>
    private static Lit[]? Columns(Frame frame, PixelRect segment)
    {
        var r = segment.ClampTo(frame.Width, frame.Height);
        if (r.IsEmpty || r.Width < MinColumns) return null;
        var columns = new Lit[r.Width];
        for (int x = r.X; x < r.Right; x++)
        {
            int yellow = 0, white = 0;
            for (int y = r.Y; y < r.Bottom; y++)
            {
                var (cr, cg, cb) = frame.GetPixel(x, y);
                int max = Math.Max(cr, Math.Max(cg, cb)), min = Math.Min(cr, Math.Min(cg, cb));
                if (cr > 170 && cg > 150 && cb < 120 && max - min > 90) yellow++;
                else if (min > 200) white++;
            }
            columns[x - r.X] = yellow * 2 > r.Height ? Lit.Yellow : white * 2 > r.Height ? Lit.White : Lit.None;
        }
        return columns;
    }
}
