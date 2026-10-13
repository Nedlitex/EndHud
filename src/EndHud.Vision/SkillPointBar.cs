namespace EndHud.Vision;

/// <summary>
/// The team's skill points (技力): three segments above the controlled operator's HP bar, bottom
/// centre, shown in combat only. A full segment is yellow; the one filling up is white from the
/// left; the rest are an empty, see-through track (game effects show through it). A grey trail
/// after the white is points just spent, fading, and doesn't count.
/// </summary>
public static class SkillPointBar
{
    public const int Segments = 3;

    /// <summary>The segments' inner rows at 4K, anchored to the bottom centre.</summary>
    internal static readonly PixelRect[] SegmentRects =
    [
        new(1600, 1958, 200, 7),
        new(1820, 1958, 200, 7),
        new(2037, 1958, 203, 7),
    ];

    // A segment counts as full from this share of yellow columns (the end caps are drawn darker).
    private const double FullAt = 0.9;
    // Each segment starts with a few dark columns (its rounded end cap): about 2.5% of its width.
    private const double EndCap = 0.06;
    // Lit columns allowed past where the fill should end: more means an effect is on the bar.
    private const double StrayAllowed = 0.1;

    /// <summary>
    /// Skill points, 0–3, a part-filled segment as a fraction (2.4 = two full, the third 40%); null
    /// when the bar doesn't look like the bar (covered by an effect, or not on screen). Read it only
    /// in combat: out of combat the bar isn't drawn and the empty track looks like dark ground.
    /// </summary>
    public static double? Measure(Frame frame, UiScaler ui)
    {
        double value = 0;
        for (int s = 0; s < Segments; s++)
        {
            var columns = Columns(frame, ui.BottomCenter(SegmentRects[s]));
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
            while (start < n * EndCap && columns[start] == Lit.None) start++;
            int run = start;
            while (run < n && columns[run] != Lit.None) run++;
            if (columns.Skip(run).Count(c => c != Lit.None) > n * StrayAllowed) return null;
            value += (run - start) / (double)(n - start);

            // Segments after the filling one are empty.
            for (int later = s + 1; later < Segments; later++)
            {
                var rest = Columns(frame, ui.BottomCenter(SegmentRects[later]));
                if (rest is null || rest.Count(c => c != Lit.None) > rest.Length * StrayAllowed) return null;
            }
            return value;
        }
        return value;
    }

    private enum Lit { None, White, Yellow }

    /// <summary>Each column of a segment: mostly yellow, mostly white, or neither.</summary>
    private static Lit[]? Columns(Frame frame, PixelRect segment)
    {
        var r = segment.ClampTo(frame.Width, frame.Height);
        if (r.IsEmpty || r.Width < 8) return null;
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
