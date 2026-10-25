namespace EndHud.Vision;

/// <summary>The boss's stagger: still building, or staggered (the large stagger, its yellow countdown running).</summary>
public enum StaggerState
{
    /// <summary>The bar filling (pink, whiter towards its right end).</summary>
    Building,

    /// <summary>The bar was filled: the large stagger, the bar a yellow countdown draining to the left.</summary>
    Staggered,
}

/// <summary>The stagger bar as read.</summary>
/// <param name="State">Building, or the large stagger.</param>
/// <param name="Progress">How far the bar has filled, 0-1 (1 while staggered).</param>
/// <param name="Nodes">The node boxes on the bar, left to right, as shares of it.</param>
/// <param name="CountdownLeft">While staggered: the yellow countdown's length, 0-1 of the bar; null while building.</param>
public sealed record StaggerReading(StaggerState State, double Progress, IReadOnlyList<double> Nodes, double? CountdownLeft)
{
    /// <summary>How many nodes the fill has passed (each one a small stagger); all of them while staggered.</summary>
    public int NodesReached => State == StaggerState.Staggered ? Nodes.Count : Nodes.Count(n => n <= Progress + StaggerBar.NodeReachedSlack);

    public override string ToString() =>
        $"{State} {Progress:F3}{(CountdownLeft is { } c ? $" countdown {c:F3}" : "")} nodes [{string.Join(", ", Nodes.Select(n => n.ToString("F3")))}] reached {NodesReached}";
}

/// <summary>
/// The boss's stagger bar: the thin bar right under the boss's HP bar, top centre. Measured on the owner's two
/// recordings of one boss (3838x2158; Fixtures/stagger), positions at 4K, anchored top-centre like the boss's bars:
/// <list type="bullet">
/// <item>The track is x 1441-2398, y 154-169: the scene behind darkened (140-170 over a white sky, 80-110 over a dark one).
/// The fill is inset: x 1445 to 2394, rows 158-167.</item>
/// <item>Building, the fill is pink at its left end (about 235,110,128) and whiter to the right on a gradient fixed to the
/// bar (243,223,228 near the right end), and it ends in a white cap 3-4 px wide that stands 2 rows out of the fill
/// above (156-157) and 1 below (168), over the track's dark rim. Stagger just added shows first as a lighter part
/// ahead of the fill, with the cap at its end; the fill catches up a moment later. The whole fill flashes lighter at
/// times (fb 60 s).</item>
/// <item>A node box: white brackets above and below the fill, 16 px wide: bars across at y 152-155 and 170-172, sides 4 px
/// wide beside the fill (y 156-157, 168-169) with the dark track between them. This boss has one, at the bar's middle
/// (x 1912-1927); the bars in other fixtures' fights (reactions/level-I-typhoeus-*, ultimate-time/sky-no-ultimate.jpg) have
/// none, so the count is read from the bar, never assumed. When the fill reaches it
/// the box is drawn about 1.5 times its size for 0.7-1 s (fb 43.9-44.8 s, f2 67.1-68.1 s), then as before: that swell
/// is the only cue of the small stagger on the bar, and the box isn't found while it lasts (<see cref="StaggerMemory"/>
/// remembers it). Nothing else marks a node reached: it is told from the fill having passed it.</item>
/// <item>Filled, the bar turns into a yellow countdown (about 230,200,85; no cap) that drains to the left: the large
/// stagger. It shows at 0.97-0.99 of the bar on its first frame (the most pink seen before it was 0.98), and drained
/// about 0.11 of the bar a second in both recordings while the game ran, so about 9 s from full; but it stood still
/// for up to 2.7 s at a time (fb 69.5-72.2 s from the start, 77.3-78.0 s, 80.0-81.3 s; f2 105.0-106.2 s), as game time
/// stops in some skills' moments, so its seconds left can't be told from its length. Then the fill is back at 0.</item>
/// </list>
/// The bar is taken as there only when the boss's HP bar is: its left end red (HP fill, about 229,62,111), x 1447-1457,
/// y 122-140. A reading must have the bar's own shape, else it is unknown: a pink fill must end in its cap with no
/// fill colour past it, and a yellow countdown must have none past it either, nor an edge across the HP bar right
/// where it ends (something dark over the bar from there, an on-screen display's backdrop).
/// </summary>
public static class StaggerBar
{
    internal const int FillLeft = 1445, FillRight = 2395; // 4K, the fill's columns (right exclusive)
    internal const int Span = FillRight - FillLeft;
    private const int FillTop = 159, FillBottom = 166;    // rows sampled for the fill (inclusive), clear of its edges
    private static readonly int[] CapRows = [156, 157, 168]; // the cap's rows out of the fill, over the track's rim
    private static readonly PixelRect HpLeftEnd = new(1447, 122, 11, 19);

    // Node boxes (4K px from the centre): the brackets' bars across (rows 152-155 above the fill, 170-172 below) and
    // their sides next to the fill (rows 156-157, 168-169) with the dark track between them; the track's rim outside.
    // Measured (white = the sides' and the bars' least channel; contrast = sides less hole in the row it is largest, and
    // sides less the rim beside the box): real boxes 4K white 213-242, hole 86-168, rim 52-175; scaled to 1080p and 1440p
    // the hole falls to 33 and the rim to 30 on a bright sky. Elsewhere along the bar nothing came within 7 of passing
    // (nearest: 13 px beside a real box, which the spacing rule drops).
    private static readonly int[] SideOffsets = [5, 6, 7];
    private static readonly int[] HoleOffsets = [-1, 0, 1];
    private static readonly int[] OutsideOffsets = [13, 14];
    private static readonly int[] SideRows = [156, 157, 168, 169];
    private static readonly int[] BarRows = [154, 155, 171, 172];
    private const int SideWhite = 190, BoxContrast = 25;
    private const int NodeSpacing = 20; // two boxes can't be closer than a box's width
    private const int CapClearance = 16; // no box taken this close to a building fill's end (4K px)

    /// <summary>A node counts as reached with the fill this close to it (the box's centre is a half pixel off a column).</summary>
    internal const double NodeReachedSlack = 0.005;

    private const int MaxGap = 3;                   // columns off the fill's colour bridged (4K px)
    private const double StrayAllowed = 0.02;       // share of the bar past the end that may look like the fill
    private const int CapWhite = 150, CapContrast = 35;
    private const double HpRedShare = 0.7;
    private const double EdgeDarker = 0.7;          // the HP bar right of a countdown's end at most this times as bright: covered

    private enum Column { Other, Pink, Yellow }

    /// <summary>The stagger bar, or null when it isn't there or can't be read (no boss bar, an effect or overlay over it).</summary>
    public static StaggerReading? Read(Frame frame) => Measure(frame).Reading;

    /// <summary>The reading, or why there is none (for tests and the diagnostic log).</summary>
    internal static (StaggerReading? Reading, string Why) Measure(Frame frame)
    {
        var ui = new UiScaler(frame.Width, frame.Height);
        if (ui.Scale < 0.2) return (null, "screen too small");
        var at = new Sampler(frame, ui);
        if (!HpBarThere(at)) return (null, "no boss HP bar");

        var columns = new Column[Span];
        for (int i = 0; i < Span; i++) columns[i] = Classify(at, FillLeft + i);

        var (pinkEnd, pinkStart) = Run(columns, Column.Pink);
        var (yellowEnd, _) = Run(columns, Column.Yellow);
        if (yellowEnd > 0 && pinkEnd > 0) return (null, $"pink to {pinkEnd} and yellow to {yellowEnd}");
        var nodes = Nodes(at);

        if (yellowEnd > 0)
        {
            if (Strays(columns, yellowEnd + 8)) return (null, $"yellow to {yellowEnd}, fill colours past it");
            if (CoveredFrom(at, FillLeft + yellowEnd)) return (null, $"yellow to {yellowEnd}, an edge across the HP bar there");
            return (new StaggerReading(StaggerState.Staggered, 1, nodes, Math.Clamp(yellowEnd / (double)Span, 0, 1)), "");
        }
        if (pinkEnd == 0)
        {
            // Nothing filled, not even the cap that stands at the left end of an empty bar.
            if (Strays(columns, 0)) return (null, "no fill from the left, fill colours further on");
            return (new StaggerReading(StaggerState.Building, 0, nodes, null), "");
        }
        if (pinkStart > 3) return (null, $"pink from {pinkStart}");
        if (!CapAt(at, FillLeft + pinkEnd - 1)) return (null, $"pink to {pinkEnd} without its cap");
        if (Strays(columns, pinkEnd + 8)) return (null, $"pink to {pinkEnd}, fill colours past it");
        // The cap alone (3-4 px) at the left end is an empty bar.
        double progress = pinkEnd <= 5 ? 0 : pinkEnd / (double)Span;
        // Beside the fill's end the cap and a lighter part of the fill just added can look like a box's side (seven times
        // over the two recordings, each 6-9 px left of the end): no box is taken there.
        var clear = nodes.Where(n => Math.Abs(n * Span - pinkEnd) >= CapClearance).ToList();
        return (new StaggerReading(StaggerState.Building, Math.Clamp(progress, 0, 1), clear, null), "");
    }

    /// <summary>Pixels by 4K position, anchored top-centre (the nearest pixel of the frame).</summary>
    private readonly struct Sampler(Frame frame, UiScaler ui)
    {
        public (int R, int G, int B) this[int x, int y]
        {
            get
            {
                int fx = (int)Math.Round(frame.Width / 2.0 - (UiScaler.ReferenceWidth / 2.0 - x - 0.5) * ui.Scale - 0.5);
                int fy = (int)Math.Round((y + 0.5) * ui.Scale - 0.5);
                fx = Math.Clamp(fx, 0, frame.Width - 1);
                fy = Math.Clamp(fy, 0, frame.Height - 1);
                var (r, g, b) = frame.GetPixel(fx, fy);
                return (r, g, b);
            }
        }
    }

    private static bool HpBarThere(Sampler at)
    {
        int red = 0, n = 0;
        for (int y = HpLeftEnd.Y; y < HpLeftEnd.Bottom; y++)
        for (int x = HpLeftEnd.X; x < HpLeftEnd.Right; x++)
        {
            var (r, g, b) = at[x, y];
            if (HpRed(r, g, b)) red++;
            n++;
        }
        return red >= n * HpRedShare;
    }

    private static Column Classify(Sampler at, int x)
    {
        int pink = 0, yellow = 0, rows = FillBottom - FillTop + 1;
        for (int y = FillTop; y <= FillBottom; y++)
        {
            var (r, g, b) = at[x, y];
            if (r >= 180 && g >= 150 && b <= 150 && r - b >= 70 && g >= 0.75 * r && g <= r + 10) yellow++;
            else if (r >= 200 && r >= g && r + 8 >= b && b >= 80 && b >= g - 20) pink++;
        }
        int most = rows * 5 / 8;
        return yellow >= most ? Column.Yellow : pink >= most ? Column.Pink : Column.Other;
    }

    /// <summary>The run of <paramref name="kind"/> from the bar's left end (gaps of up to <see cref="MaxGap"/> bridged):
    /// its end (columns from the left, 0 for none) and where it starts.</summary>
    private static (int End, int Start) Run(Column[] columns, Column kind)
    {
        int start = 0;
        while (start < 4 && columns[start] != kind) start++;
        if (start == 4) return (0, 0);
        int end = start, gap = 0;
        for (int i = start; i < columns.Length; i++)
        {
            if (columns[i] == kind) { end = i + 1; gap = 0; }
            else if (++gap > MaxGap) break;
        }
        return (end, start);
    }

    /// <summary>Past <paramref name="from"/>, more than a few columns in the fill's colours: not the bar's shape.</summary>
    private static bool Strays(Column[] columns, int from)
    {
        int stray = 0;
        for (int i = Math.Max(0, from); i < columns.Length; i++)
            if (columns[i] != Column.Other) stray++;
        return stray > Span * StrayAllowed;
    }

    /// <summary>The fill's white cap at its end (<paramref name="lastX"/>, 4K): white over the rim above the fill, the rim dark just right of it.</summary>
    private static bool CapAt(Sampler at, int lastX)
    {
        // Any one of its rows out of the fill will do: scaled down, a row of the rim blurs into it (at 1440p rows 156-157
        // share a pixel row with 155 and read 163 over a rim of 141, while row 168 stays 213).
        foreach (int y in CapRows)
        {
            int capWhite = -1;
            for (int x = lastX - 6; x <= lastX + 3; x++)
            {
                var (r, g, b) = at[x, y];
                capWhite = Math.Max(capWhite, Math.Min(r, Math.Min(g, b)));
            }
            if (capWhite < CapWhite) continue;
            double rim = 0;
            for (int x = lastX + 6; x <= lastX + 9; x++)
            {
                var (r, g, b) = at[x, y];
                rim += Math.Max(r, Math.Max(g, b)) / 4.0;
            }
            if (rim <= capWhite - CapContrast) return true;
        }
        return false;
    }

    /// <summary>
    /// An edge across the HP bar where the countdown ends: right of it much darker than left of it, in the same part of
    /// the HP bar (both its fill or both its track). Something dark covers the bar from there. Where the HP fill itself ends
    /// right there, it can't be told: covered too.
    /// </summary>
    private static bool CoveredFrom(Sampler at, int endX)
    {
        var (leftRed, leftLight) = HpWindow(at, endX - 12, endX - 4);
        var (rightRed, rightLight) = HpWindow(at, endX + 4, endX + 12);
        if (leftRed != rightRed) return true;
        return rightLight < leftLight * EdgeDarker;
    }

    /// <summary>The window's HP fill (red or not, by most of its pixels) and its brightness, the median pixel's: text over a
    /// backdrop is a few bright pixels and doesn't move it.</summary>
    private static (bool Red, double Light) HpWindow(Sampler at, int x0, int x1)
    {
        int red = 0;
        var light = new List<int>();
        for (int x = Math.Max(x0, 1441); x <= Math.Min(x1, 2398); x++)
        for (int y = 124; y <= 138; y += 2)
        {
            var (r, g, b) = at[x, y];
            if (HpRed(r, g, b)) red++;
            light.Add(Math.Max(r, Math.Max(g, b)));
        }
        if (light.Count == 0) return (false, 0);
        light.Sort();
        return (red * 2 > light.Count, light[light.Count / 2]);
    }

    /// <summary>The boss's HP fill: red with some blue (about 229,62,111). An on-screen display's magenta text (255,80,255) isn't.</summary>
    private static bool HpRed(int r, int g, int b) => r >= 160 && r - g >= 70 && r - b >= 60;

    /// <summary>The node boxes, as shares of the bar: white sides in the rim's rows, a dark hole between, darker outside.</summary>
    private static IReadOnlyList<double> Nodes(Sampler at)
    {
        var scores = new List<(int Centre, double Score)>();
        for (int c = FillLeft + 12; c < FillRight - 12; c++)
            if (BoxScore(at, c) is var score && score >= 0) scores.Add((c, score));
        // The best first; a weaker one within a box's width of a kept one is the same box seen off-centre.
        var kept = new List<int>();
        foreach (var (centre, _) in scores.OrderByDescending(s => s.Score))
            if (kept.All(k => Math.Abs(k - centre) >= NodeSpacing)) kept.Add(centre);
        // The box spans columns c-8 .. c+7: its centre is half a column left of c.
        return [.. kept.Order().Select(c => (c - 0.5 - FillLeft) / Span)];
    }

    /// <summary>How clearly a node box is centred at <paramref name="c"/> (4K): 0 or more is a box.</summary>
    private static double BoxScore(Sampler at, int c)
    {
        double sides = 0, bars = 0, bestHole = double.MinValue, rim = 0;
        int nb = 0;
        foreach (int y in SideRows)
        {
            double side = 0, hole = 0, outside = 0;
            foreach (int d in SideOffsets) side += Min(at[c - d, y]) + Min(at[c + d - 1, y]);
            foreach (int d in HoleOffsets) hole += Max(at[c + d - 1, y]);
            foreach (int d in OutsideOffsets) outside += Max(at[c - d, y]) + Max(at[c + d - 1, y]);
            side /= 2 * SideOffsets.Length;
            hole /= HoleOffsets.Length;
            outside /= 2 * OutsideOffsets.Length;
            sides += side;
            rim += side - outside;
            bestHole = Math.Max(bestHole, side - hole);
        }
        sides /= SideRows.Length;
        rim /= SideRows.Length;
        foreach (int y in BarRows)
            for (int x = c - 6; x <= c + 5; x++) { bars += Min(at[x, y]); nb++; }
        bars /= nb;
        return Math.Min(Math.Min(sides, bars) - SideWhite, Math.Min(bestHole, rim) - BoxContrast);

        static int Min((int R, int G, int B) p) => Math.Min(p.R, Math.Min(p.G, p.B));
        static int Max((int R, int G, int B) p) => Math.Max(p.R, Math.Max(p.G, p.B));
    }
}

/// <summary>
/// Carries the stagger bar's node boxes across readings: the game draws a box swollen for about a second as the fill
/// reaches it, and it isn't found as a box then, which would drop the count of nodes reached for that second (and make an
/// alert on it fire twice). A box seen stays on the bar until no bar has been read for <see cref="ForgetAfterReadings"/>
/// readings in a row (the fight over, or another boss's bar with its own nodes); one seen again where it was is the same.
/// </summary>
public sealed class StaggerMemory
{
    /// <summary>Readings in a row without a bar after which the boxes are forgotten (about 5 s at four readings a second).</summary>
    public const int ForgetAfterReadings = 20;

    /// <summary>Readings a box must be seen in before it counts: a box seen once may be something else for a frame.</summary>
    public const int SightingsNeeded = 2;

    private const double SameNode = 0.01;
    private readonly List<(double Position, int Sightings)> _nodes = [];
    private int _missing;

    /// <summary>The reading with the boxes seen in at least <see cref="SightingsNeeded"/> readings of this bar; null stays null.</summary>
    public StaggerReading? Track(StaggerReading? reading)
    {
        if (reading is null)
        {
            if (++_missing >= ForgetAfterReadings) _nodes.Clear();
            return null;
        }
        _missing = 0;
        foreach (double node in reading.Nodes)
        {
            int i = _nodes.FindIndex(n => Math.Abs(n.Position - node) <= SameNode);
            if (i < 0) _nodes.Add((node, 1));
            else _nodes[i] = (_nodes[i].Position, _nodes[i].Sightings + 1);
        }
        return reading with { Nodes = [.. _nodes.Where(n => n.Sightings >= SightingsNeeded).Select(n => n.Position).Order()] };
    }
}
