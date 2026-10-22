namespace EndHud.Vision;

/// <summary>
/// The elemental infliction on the boss (灼热 / 寒冷 / 电磁 / 自然附着). It isn't in the status row: the
/// game draws it as the element's icon in a dark disc at the right end of the boss's HP bar, with
/// white arc segments around it for the stacks (the top one first). Measured on the full boss fight
/// (Fixtures/battle-hud): the top segment's whiteness was 229–243 whenever the ring was up, and the
/// disc's coloured pixels were 59–75% of it with a hue of 5–8° (heat) or 75° (nature); flashes and
/// blue effects in the same place had no white segment above them.
/// </summary>
public static class InflictionRing
{
    /// <summary>A 160 px square around the ring at 4K, top-centre anchored; the ring's centre is at (80, 82) in it.</summary>
    internal static readonly PixelRect Area = new(2400, 60, 160, 160);
    private const double CentreX = 80, CentreY = 82;
    private const double DiscRadius = 17;
    private const double ArcInner = 30, ArcOuter = 34;

    internal const int MinArcWhiteness = 190;
    internal const double MinColouredShare = 0.15;

    // The stack arcs (4K px from the ring's centre): the arc's white peaks at radius 28-35, its dark
    // outline at 36-39. Each arc spans about ±27° around its centre (gaps 35°-55° between them). An
    // unlit segment has no track: the scene shows through, as white as 248 on a bright scene, but
    // without the outline. On the infliction fixtures (Si fight's cryo at 1-4 stacks, the boss fight's
    // heat and nature) a lit arc stood 90-130 above its outline at every sampled angle, so every lit
    // segment's share was 1.00; unlit segments 0.00, once 0.27 (0.36 on smaller screens: a streak
    // across the empty left segment), which reads as unknown.
    private const double PeakInner = 28, PeakOuter = 35, OutlineInner = 36, OutlineOuter = 39;
    private const int SegmentHalfWidth = 20;
    internal const int MinOutlineContrast = 40;
    internal const double LitShare = 0.7, UnlitShare = 0.3;

    /// <summary>The elements, by the hue (degrees) of their icon; ids as the wiki names the statuses.</summary>
    private static readonly (string Id, double From, double To)[] Elements =
    [
        ("Heat Infliction", 345, 28),
        ("Electric Infliction", 28, 55),
        ("Nature Infliction", 55, 100),
        ("Cryo Infliction", 165, 215),
    ];

    /// <summary>An infliction and how many stacks it has.</summary>
    /// <param name="Stacks">1–4, or null when the arcs can't be told apart (an effect over them).</param>
    public sealed record Infliction(string Element, int? Stacks);

    /// <summary>The infliction shown, or null when there's none (or it can't be told).</summary>
    public static string? Read(Frame frame) => Measure(frame).Element;

    /// <summary>The infliction with its stack count: white arcs clockwise from the top, one per stack.</summary>
    public static Infliction? ReadWithStacks(Frame frame) =>
        Measure(frame).Element is { } element ? new Infliction(element, Stacks(frame)) : null;

    /// <summary>
    /// Four arc segments, centred at 0° (top), 90°, 180° and 270°. Each is lit (a white arc with its
    /// dark outline), unlit (no arc: the game draws no track, the scene shows through) or neither
    /// (null: covered). The lit ones must be the first n clockwise from the top; anything else is null.
    /// </summary>
    internal static int? Stacks(Frame frame)
    {
        var shares = SegmentShares(frame);
        var lit = new bool?[4];
        for (int s = 0; s < 4; s++) lit[s] = shares[s] >= LitShare ? true : shares[s] <= UnlitShare ? false : null;
        if (lit.Any(l => l is null)) return null;
        int n = lit.TakeWhile(l => l == true).Count();
        return n >= 1 && lit.Skip(n).All(l => l == false) ? n : null;
    }

    /// <summary>
    /// For each segment (top, right, bottom, left): the share of its angles where a white arc stands
    /// out of its surroundings, for measuring thresholds.
    /// </summary>
    internal static double[] SegmentShares(Frame frame)
    {
        var ui = new UiScaler(frame.Width, frame.Height);
        var area = ui.TopCenter(Area);
        double k = area.Width / (double)Area.Width;
        double cx = area.X + CentreX * k, cy = area.Y + CentreY * k;
        var shares = new double[4];
        for (int s = 0; s < 4; s++)
        {
            int lit = 0, n = 0;
            for (int a = -SegmentHalfWidth; a <= SegmentHalfWidth; a += 4)
            {
                double rad = (s * 90 + a) * Math.PI / 180;
                int peak = 0, outline = 255;
                for (double r = PeakInner; r <= PeakOuter; r += 0.5)
                {
                    var (pr, pg, pb) = Pixel(frame, cx + r * k * Math.Sin(rad), cy - r * k * Math.Cos(rad));
                    int min = Math.Min(pr, Math.Min(pg, pb)), max = Math.Max(pr, Math.Max(pg, pb));
                    peak = Math.Max(peak, max - min < 40 ? min : 0);
                }
                for (double r = OutlineInner; r <= OutlineOuter; r += 0.5)
                {
                    var (pr, pg, pb) = Pixel(frame, cx + r * k * Math.Sin(rad), cy - r * k * Math.Cos(rad));
                    outline = Math.Min(outline, Math.Min(pr, Math.Min(pg, pb)));
                }
                n++;
                if (peak >= MinArcWhiteness && peak - outline >= MinOutlineContrast) lit++;
            }
            shares[s] = lit / (double)n;
        }
        return shares;
    }

    /// <summary>What the reading is based on, for measuring thresholds.</summary>
    internal static (int ArcWhiteness, double ColouredShare, double? Hue, string? Element) Measure(Frame frame)
    {
        var ui = new UiScaler(frame.Width, frame.Height);
        var area = ui.TopCenter(Area);
        double k = area.Width / (double)Area.Width;
        double cx = area.X + CentreX * k, cy = area.Y + CentreY * k;

        // The top segment: white and grey, never coloured.
        var arc = new List<int>();
        for (double r = ArcInner; r <= ArcOuter; r++)
        for (int a = -20; a <= 20; a += 4)
        {
            double rad = a * Math.PI / 180;
            var (pr, pg, pb) = Pixel(frame, cx + r * k * Math.Sin(rad), cy - r * k * Math.Cos(rad));
            int min = Math.Min(pr, Math.Min(pg, pb)), max = Math.Max(pr, Math.Max(pg, pb));
            arc.Add(max - min < 40 ? min : 0);
        }
        arc.Sort();
        int whiteness = arc[arc.Count / 2];

        // The disc: the element's colour.
        var hues = new List<double>();
        int n = 0;
        double radius = DiscRadius * k;
        for (int y = (int)Math.Floor(cy - radius); y <= (int)Math.Ceiling(cy + radius); y++)
        for (int x = (int)Math.Floor(cx - radius); x <= (int)Math.Ceiling(cx + radius); x++)
        {
            if ((x - cx) * (x - cx) + (y - cy) * (y - cy) > radius * radius) continue;
            n++;
            var (r, g, b) = Pixel(frame, x, y);
            var (h, s, v) = Hsv(r, g, b);
            if (s > 0.5 && v > 0.45) hues.Add(h);
        }
        double share = n == 0 ? 0 : hues.Count / (double)n;
        if (hues.Count == 0) return (whiteness, share, null, null);
        hues.Sort();
        double hue = hues[hues.Count / 2];
        if (whiteness < MinArcWhiteness || share < MinColouredShare) return (whiteness, share, hue, null);
        string? element = Elements.FirstOrDefault(e => e.From < e.To ? hue >= e.From && hue < e.To : hue >= e.From || hue < e.To).Id;
        return (whiteness, share, hue, element);
    }

    private static (byte R, byte G, byte B) Pixel(Frame frame, double x, double y)
    {
        int px = Math.Clamp((int)Math.Round(x), 0, frame.Width - 1), py = Math.Clamp((int)Math.Round(y), 0, frame.Height - 1);
        return frame.GetPixel(px, py);
    }

    private static (double H, double S, double V) Hsv(byte r, byte g, byte b)
    {
        double rf = r / 255.0, gf = g / 255.0, bf = b / 255.0;
        double max = Math.Max(rf, Math.Max(gf, bf)), min = Math.Min(rf, Math.Min(gf, bf)), d = max - min;
        double h = d == 0 ? 0
            : max == rf ? 60 * ((gf - bf) / d % 6)
            : max == gf ? 60 * ((bf - rf) / d + 2)
            : 60 * ((rf - gf) / d + 4);
        if (h < 0) h += 360;
        return (h, max == 0 ? 0 : d / max, max);
    }
}
