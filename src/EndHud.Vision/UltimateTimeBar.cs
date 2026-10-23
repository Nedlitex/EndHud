namespace EndHud.Vision;

/// <summary>
/// The time left of an ultimate that lasts (Laevatain's, Zhuang Fangyi's): a thin bar right above
/// the skill point segments, bottom centre, as wide as they are. It's in the operator's colour
/// (Laevatain red, Zhuang Fangyi gold), full when the ultimate starts and draining to the left over
/// a dark track. It is NOT always the controlled operator's: an ultimate cast from the background
/// runs its bar under whoever is controlled, and with several running the game shows the controlled
/// operator's own if she has one (see <see cref="LastingUltimates"/>, which tells whose it is).
/// Nothing is drawn there otherwise (the scenery shows), so a bar is only taken as one when it has
/// that shape: one colour from the left end, then the dark track to the right end.
/// </summary>
public static class UltimateTimeBar
{
    /// <summary>
    /// The bar's rows at 4K (3840x2160), anchored to the bottom centre. Measured on recordings of
    /// 3838x2158, where it's at rows 1932–1937: the bottom-anchored mapping puts those 2 px higher
    /// than 4K, which a 6-row bar at 4K hardly notices but a 4-row bar at 1440p does.
    /// </summary>
    internal static readonly PixelRect Rect = new(1605, 1934, 630, 6);

    // A column is the fill when at least half its rows are bright and saturated (a scaled bar's edge
    // rows blur), the track when they're grey: unsaturated and not bright (it's darker in places and
    // lighter grey in others, like the skill points' spent trail).
    private const int FillMinBrightest = 150, FillMinSpread = 80, TrackMaxBrightest = 130, TrackMaxSpread = 40;
    // The fill's columns all share its colour: each channel within this of the run's middle column.
    private const int SameColour = 60;
    // Allowed off the shape: columns at the left end before the fill (its end cap), and columns of
    // the track that aren't dark (an effect, a seam).
    private const double EndCap = 0.02, StrayAllowed = 0.15;
    // The fill can be very short near the end; the track must then be clearly there.
    private const double MinFill = 0.005;
    private const int MaxGap = 2;
    // The bar is a thin line: the rows this far above and below it are something else. Bright
    // sky above the skill points is one colour across the width and read as a full bar (three
    // single false readings in the recordings) until this was checked.
    // Checked at these 4K rows (6 px above and below the bar, clear of its blurred edges when scaled).
    private const int AboveRow = 1928, BelowRow = 1946;
    private const int EdgeDifferent = 50;
    // Columns of the fill allowed off its colour: a short fill's end cap blurs into it when scaled.
    private const double OffColourAllowed = 0.2;
    private const double EdgeShare = 0.5;

    /// <summary>
    /// The share of the ultimate's time left, 0–1, or null when there's no such bar (no ultimate
    /// running, or something covers it). Read it in combat only, where the skill point bar is.
    /// </summary>
    public static double? Measure(Frame frame, UiScaler ui) => Read(frame, ui)?.Left;

    /// <summary>
    /// The bar as read: the share of time left and the fill's colour (whose ultimate it is, see
    /// <see cref="LastingUltimates.OwnerByColour"/>); null when there's no such bar.
    /// </summary>
    public static UltimateTimeBarReading? Read(Frame frame, UiScaler ui)
    {
        var r = ui.BottomCenter(Rect).ClampTo(frame.Width, frame.Height);
        if (r.Width < 20 || r.Height < 1) return null;

        var kinds = new Column[r.Width];
        var colours = new (int R, int G, int B)[r.Width];
        for (int x = 0; x < r.Width; x++)
        {
            int fill = 0, track = 0;
            (int R, int G, int B) sum = (0, 0, 0);
            for (int y = 0; y < r.Height; y++)
            {
                var (cr, cg, cb) = frame.GetPixel(r.X + x, r.Y + y);
                int max = Math.Max(cr, Math.Max(cg, cb)), min = Math.Min(cr, Math.Min(cg, cb));
                if (max >= FillMinBrightest && max - min >= FillMinSpread)
                {
                    fill++;
                    sum = (sum.R + cr, sum.G + cg, sum.B + cb);
                }
                else if (max <= TrackMaxBrightest && max - min <= TrackMaxSpread) track++;
            }
            int most = (r.Height + 1) / 2;
            kinds[x] = fill >= most ? Column.Fill : track >= most ? Column.Track : Column.Other;
            if (fill > 0) colours[x] = (sum.R / fill, sum.G / fill, sum.B / fill);
        }

        int start = 0;
        while (start < r.Width * EndCap && kinds[start] != Column.Fill) start++;
        // The fill, from the left; a scaled-down bar (2 rows at 1366x768) can lose a column or two to blur.
        int end = start;
        var bridged = new bool[r.Width];
        while (end < r.Width)
        {
            if (kinds[end] == Column.Fill) end++;
            else if (Enumerable.Range(end + 1, MaxGap).Any(x => x < r.Width && kinds[x] == Column.Fill)) bridged[end++] = true;
            else break;
        }
        int filled = end - start;
        if (filled < Math.Max(1, r.Width * MinFill)) return null;

        // One colour: scenery that happens to be bright and saturated isn't.
        var middle = colours[Enumerable.Range(start + filled / 2, end - start - filled / 2).First(x => !bridged[x])];
        int offColour = 0, inColour = 0;
        (int R, int G, int B) total = (0, 0, 0);
        for (int x = start; x < end; x++)
        {
            if (bridged[x]) continue;
            var c = colours[x];
            if (Math.Abs(c.R - middle.R) > SameColour || Math.Abs(c.G - middle.G) > SameColour || Math.Abs(c.B - middle.B) > SameColour)
                offColour++;
            else
            {
                inColour++;
                total = (total.R + c.R, total.G + c.G, total.B + c.B);
            }
        }
        if (offColour > Math.Max(1, filled * OffColourAllowed)) return null;

        // A thin line: above and below it, most fill columns show something of another colour.
        int above = ui.BottomCenter(new PixelRect(Rect.X, AboveRow, 1, 1)).Y, below = ui.BottomCenter(new PixelRect(Rect.X, BelowRow, 1, 1)).Y;
        int edged = 0, checkedColumns = 0;
        for (int x = start; x < end; x++)
        {
            if (bridged[x]) continue;
            checkedColumns++;
            var c = colours[x];
            bool Differs(int y)
            {
                if (y < 0 || y >= frame.Height) return true;
                var (ar, ag, ab) = frame.GetPixel(r.X + x, y);
                return Math.Max(Math.Abs(ar - c.R), Math.Max(Math.Abs(ag - c.G), Math.Abs(ab - c.B))) > EdgeDifferent;
            }
            if (Differs(above) && Differs(below)) edged++;
        }
        if (edged < checkedColumns * EdgeShare) return null;

        // The rest is the dark track, to the right end.
        int rest = r.Width - end;
        if (rest > 0 && kinds.Skip(end).Count(k => k != Column.Track) > rest * StrayAllowed) return null;
        var colour = new BarColour(total.R / inColour, total.G / inColour, total.B / inColour);
        return new UltimateTimeBarReading(Math.Clamp(filled / (double)(r.Width - start), 0, 1), colour);
    }

    private enum Column { Other, Fill, Track }
}

/// <param name="Left">The share of the ultimate's time left, 0–1.</param>
/// <param name="Colour">The fill's colour: the mean of its columns of the bar's one colour.</param>
public sealed record UltimateTimeBarReading(double Left, BarColour Colour);

/// <summary>A colour as read on screen, 0–255 a channel.</summary>
public readonly record struct BarColour(int R, int G, int B)
{
    /// <summary>The hue, 0–360 degrees (0 red, 60 yellow, 180 cyan).</summary>
    public double Hue
    {
        get
        {
            int max = Math.Max(R, Math.Max(G, B)), min = Math.Min(R, Math.Min(G, B));
            if (max == min) return 0;
            double d = max - min;
            double h = max == R ? (G - B) / d : max == G ? 2 + (B - R) / d : 4 + (R - G) / d;
            return (h * 60 + 360) % 360;
        }
    }

    public override string ToString() => $"#{R:X2}{G:X2}{B:X2} (hue {Hue:F0})";
}
