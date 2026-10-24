namespace EndHud.Vision.Inn;

/// <summary>
/// The table cards along the top of the session (one per table, 1–6 left to right): what state each
/// table is in and, for an order, what was ordered and how far along each item is.
/// <list type="bullet">
/// <item>The card's colour says whose it is: white (or greyed by its countdown) a normal guest,
/// yellow a gourmand (老饕), red waiting to pay. No card under the number: an empty table.</item>
/// <item>The state's word at the bottom (点餐中, 用餐中, 待结账, 老饕) is read by OCR.</item>
/// <item>An order shows one picture per item on a grey disc. Each disc is found by fitting a circle
/// to its edge (a new order's card is drawn larger for a moment), and its art compared with every
/// dish and drink picture drawn the way the card draws it, colour kept and the disc's own grey
/// around it, over the whole disc but the corner a badge covers. The best must be close and clearly
/// ahead of the next, or the item is left unnamed.</item>
/// <item>Whether each item is served: its pip under the card's number is green, or a blue spinner
/// (a ring of cyan dots) sits on the disc's lower right, the game serving it on its own. Both count
/// as served (owner, 2026-10-19). The green smiley that can show there means nothing useful (owner)
/// and is not read.</item>
/// </list>
/// Measured on 4K frames of the owner's recordings; see InnTableCardReaderTests for the accuracy.
/// </summary>
public sealed class TableCardReader(InnText text, InnPictures pictures)
{
    public const int Tables = 6;

    // At 4K, anchored top-left: card n's left edge, its discs' centres and radius, its art.
    internal const double FirstCardLeft = 563.1, CardStep = 283.25, CardWidth = 275;
    internal const double DiscY = 178, FirstDiscX = 73.5, SecondDiscX = 200.5, DiscRadius = 62.3;
    // The art is 103 px square at 4K, its centre 1 px under the disc's.
    internal const double ArtShare = 103 / DiscRadius, ArtDrop = 1 / DiscRadius;
    // The disc's grey, and the card's white around it.
    internal const int DiscGrey = 0xE7E5E6;
    private const int CardWhiteFrom = 246;
    // Compared within this share of the radius, leaving out the lower right beyond this share (the badge).
    private const double CompareShare = 0.93, BadgeCorner = 0.19;
    // A picture is named when its colour is this close (mean difference per channel, 0–255) and the
    // next picture this much further. Measured at 4K: right pictures 3.4–12.2, the next ones ≥ 12.5
    // and ≥ 7.7 behind (see the tests).
    internal const double MaxDifference = 20, MinLead = 5;
    // The state's word, at the card's bottom, under its icon.
    private static readonly PixelRect StateText = new(40, 214, 200, 50);
    // A gourmand's order: dark category icons on darker yellow discs where an order's pictures go.
    private const int GourmandDisc = 0xDFC676;
    // The pips (one per item) under the number, at a two-item order's places.
    private static readonly double[] PipX = [120, 155];
    private const double PipY = 105;

    /// <summary>Each table's card left to right; null where a card can't be read (fading, covered, or not clearly one thing).</summary>
    public async Task<IReadOnlyList<TableCard?>> ReadAsync(Frame frame)
    {
        var layout = new InnLayout(frame);
        var cards = new TableCard?[Tables];
        for (int n = 1; n <= Tables; n++) cards[n - 1] = await ReadCardAsync(frame, layout, n);
        return cards;
    }

    private async Task<TableCard?> ReadCardAsync(Frame frame, InnLayout layout, int table)
    {
        double left = FirstCardLeft + CardStep * (table - 1);
        var colour = CardColour(frame, layout, left);
        // No card: an empty table, unless an overlay drawn into the frame covers the card's edge (unknown, then).
        if (colour is CardKind.None)
            return OverlayShare(frame, layout, left) >= MinOverlayShare ? null : new TableCard(table, TableState.Empty, [], []);

        string word = await ReadWordAsync(frame, layout, left, colour);
        bool Says(string[] words) => words.Any(word.Contains);
        switch (colour)
        {
            case CardKind.Red:
                return Says(Paying) ? new TableCard(table, TableState.Paying, [], []) : null;
            case CardKind.Yellow:
                if (Says(Gourmand)) return new TableCard(table, TableState.GourmandArrived, [], []);
                if (Says(Ordering)) return new TableCard(table, TableState.GourmandOrdering, [], []);
                if (Says(Eating)) return new TableCard(table, TableState.GourmandEating, [], []);
                // No word, and the discs of a gourmand's order (what kind of dish, not which).
                return word.Length == 0 && (IsGourmandDisc(frame, layout, left + FirstDiscX) || IsGourmandDisc(frame, layout, left + SecondDiscX))
                    ? new TableCard(table, TableState.GourmandOrder, [], GourmandPips(frame, layout, left))
                    : null;
            default:
                if (Says(Ordering)) return new TableCard(table, TableState.Ordering, [], []);
                if (Says(Eating)) return new TableCard(table, TableState.Eating, [], []);
                if (word.Length > 0) return null;
                var discs = new List<Disc>();
                foreach (double x in new[] { FirstDiscX, SecondDiscX })
                    if (FindDisc(frame, layout, left + x) is { } disc) discs.Add(disc);
                if (discs.Count == 0) return null;
                // An item is served when its pip is green or the game is serving it on its own (the spinner).
                IReadOnlyList<bool> pips = discs.Count == 2 ? Pips(frame, layout, left) : [];
                var items = discs.Select((disc, i) => new OrderItem(Identify(frame, disc), (pips.Count == discs.Count && pips[i]) || HasSpinner(frame, disc))).ToList();
                return new TableCard(table, TableState.Order, items, pips);
        }
    }

    private enum CardKind { None, Neutral, Yellow, Red }

    // At 4K, from a card's left edge: the strip where its colour is read (the two patches and between them).
    private static readonly PixelRect CardEdge = new(0, 110, 60, 140);

    // The least overlay share that makes a place without a card colour unknown rather than empty. Measured on every inn
    // fixture at 4K and 1080p: 0.00 % on all 270 places read empty; 1.14 % at the least on an occupied place that the
    // synthetic RivaTuner display at the top centre made read empty (InnUnderOverlayTests).
    private const double MinOverlayShare = 0.003;

    /// <summary>
    /// How much of the card's edge is in an overlay's ink: the lesser of its shares of pure black (a drawn text's shadow) and
    /// of vivid, nearly pure colour (an on-screen display's lettering, such as Afterburner's). Lettering comes with both;
    /// the dining room shows neither together.
    /// </summary>
    internal static double OverlayShare(Frame frame, InnLayout layout, double left)
    {
        var r = layout.TopLeft(CardEdge with { X = (int)left }).ClampTo(frame.Width, frame.Height);
        if (r.IsEmpty) return 0;
        int black = 0, vivid = 0, all = 0;
        for (int y = r.Y; y < r.Bottom; y += 2)
        for (int x = r.X; x < r.Right; x += 2)
        {
            var (cr, cg, cb) = frame.GetPixel(x, y);
            int max = Math.Max(cr, Math.Max(cg, cb)), min = Math.Min(cr, Math.Min(cg, cb));
            all++;
            if (max <= 16) black++;
            else if (max >= 230 && min <= 40) vivid++;
        }
        return Math.Min(black, vivid) / (double)all;
    }

    /// <summary>
    /// The card's colour from two patches at its left edge, above and below the first disc (the
    /// countdown's grey creeps in from the right). Neither a card colour nor alike: no card.
    /// </summary>
    private static CardKind CardColour(Frame frame, InnLayout layout, double left)
    {
        var top = Kind(Average(frame, layout.TopLeft(new PixelRect((int)left + 8, 120, 14, 15))));
        var bottom = Kind(Average(frame, layout.TopLeft(new PixelRect((int)left + 8, 225, 14, 15))));
        return top == bottom ? top : CardKind.None;

        static CardKind Kind((double R, double G, double B, double Spread) c)
        {
            // A card is one flat colour: a patch of scenery seldom is.
            if (c.Spread > 18) return CardKind.None;
            double max = Math.Max(c.R, Math.Max(c.G, c.B)), min = Math.Min(c.R, Math.Min(c.G, c.B));
            if (c.R >= 120 && c.R - c.G >= 80 && c.R - c.B >= 60) return CardKind.Red;
            if (c.R >= 150 && c.R - c.B >= 50 && c.G - c.B >= 30 && c.R - c.G <= 50) return CardKind.Yellow;
            if (max - min <= 14 && min >= 110) return CardKind.Neutral;
            return CardKind.None;
        }
    }

    /// <summary>The patch's mean colour and how much it varies (mean distance from the mean, largest channel).</summary>
    private static (double R, double G, double B, double Spread) Average(Frame frame, PixelRect patch)
    {
        var r = patch.ClampTo(frame.Width, frame.Height);
        if (r.IsEmpty) return (0, 0, 0, 255);
        double sr = 0, sg = 0, sb = 0;
        int n = r.Width * r.Height;
        for (int y = r.Y; y < r.Bottom; y++)
        for (int x = r.X; x < r.Right; x++)
        {
            var (cr, cg, cb) = frame.GetPixel(x, y);
            sr += cr; sg += cg; sb += cb;
        }
        sr /= n; sg /= n; sb /= n;
        double dr = 0, dg = 0, db = 0;
        for (int y = r.Y; y < r.Bottom; y++)
        for (int x = r.X; x < r.Right; x++)
        {
            var (cr, cg, cb) = frame.GetPixel(x, y);
            dr += Math.Abs(cr - sr); dg += Math.Abs(cg - sg); db += Math.Abs(cb - sb);
        }
        return (sr, sg, sb, Math.Max(dr, Math.Max(dg, db)) / n);
    }

    // The words a card can show. A read that has none of them is tried again at another size.
    // OCR reads 点 as 占 now and then (the same letter without its dots).
    private static readonly string[] Ordering = ["点餐", "占餐"], Eating = ["用餐"], Paying = ["结账", "待结"], Gourmand = ["老饕", "老", "饕"];
    private static readonly string[] Words = [.. Ordering, .. Eating, .. Paying, .. Gourmand];

    /// <summary>
    /// The state's word at the card's bottom: dark letters, white on a red card. "" when there's no
    /// ink there (an order's card); otherwise the first read holding a known word, or the last read.
    /// </summary>
    private async Task<string> ReadWordAsync(Frame frame, InnLayout layout, double left, CardKind colour)
    {
        var region = layout.TopLeft(StateText with { X = (int)left + StateText.X });
        InnText.Ink ink = colour == CardKind.Red ? InnText.White : InnText.Dark;
        string last = "";
        await foreach (string read in text.ReadAttemptsAsync(frame, region, ink, layout.Scale))
        {
            last = read.Replace(" ", "");
            if (Words.Any(last.Contains)) return last;
        }
        return last;
    }

    /// <summary>A disc where an order's picture sits: its centre and radius in window pixels.</summary>
    internal readonly record struct Disc(double X, double Y, double Radius, double Scale);

    /// <summary>
    /// The disc near its usual place: rays from there out to the card's white, a circle fitted to
    /// where they meet it (twice, leaving out rays that met white inside the art). Null when there's
    /// no such circle, of about the disc's size, in the disc's grey.
    /// </summary>
    internal static Disc? FindDisc(Frame frame, InnLayout layout, double discX)
    {
        double s = layout.Scale;
        var (ex, ey) = layout.TopLeft(discX, DiscY);
        var points = new List<(double X, double Y)>();
        const int Rays = 72;
        for (int k = 0; k < Rays; k++)
        {
            double t = 2 * Math.PI * k / Rays, cos = Math.Cos(t), sin = Math.Sin(t);
            for (double r = 48 * s; r <= 86 * s; r += 0.5)
            {
                int x = (int)Math.Round(ex + r * cos), y = (int)Math.Round(ey + r * sin);
                if (x < 0 || y < 0 || x >= frame.Width || y >= frame.Height) break;
                var (cr, cg, cb) = frame.GetPixel(x, y);
                if (Math.Min(cr, Math.Min(cg, cb)) < CardWhiteFrom) continue;
                points.Add((x, y));
                break;
            }
        }
        if (points.Count < Rays * 2 / 3) return null;
        // Rays that met white past a badge (or inside a white bottle) end far from the rest: keep
        // those near the middle distance before fitting.
        double middle = points.Select(p => Math.Sqrt((p.X - ex) * (p.X - ex) + (p.Y - ey) * (p.Y - ey))).Order().ElementAt(points.Count / 2);
        points = [.. points.Where(p => Math.Abs(Math.Sqrt((p.X - ex) * (p.X - ex) + (p.Y - ey) * (p.Y - ey)) - middle) <= 8 * s + 1)];
        if (points.Count < Rays / 2) return null;
        var fit = FitCircle(points);
        if (fit is null) return null;
        var (cx, cy, radius) = fit.Value;
        var close = points.Where(p => Math.Abs(Math.Sqrt((p.X - cx) * (p.X - cx) + (p.Y - cy) * (p.Y - cy)) - radius) <= Math.Max(2, 2 * s)).ToList();
        if (close.Count < Rays / 2 || FitCircle(close) is not { } refit) return null;
        (cx, cy, radius) = refit;
        double usual = DiscRadius * s;
        if (radius < usual * 0.9 || radius > usual * 1.2 || Math.Abs(cx - ex) > 12 * s || Math.Abs(cy - ey) > 12 * s) return null;

        // Inside the edge, the disc's grey.
        var ring = new List<(int R, int G, int B)>();
        for (int k = 0; k < Rays; k++)
        {
            double t = 2 * Math.PI * k / Rays;
            int x = (int)Math.Round(cx + radius * 0.95 * Math.Cos(t)), y = (int)Math.Round(cy + radius * 0.95 * Math.Sin(t));
            if (x >= 0 && y >= 0 && x < frame.Width && y < frame.Height) ring.Add(frame.GetPixel(x, y));
        }
        int Median(Func<(int R, int G, int B), int> channel) => ring.Select(channel).Order().ElementAt(ring.Count / 2);
        int greyR = (DiscGrey >> 16) & 0xFF, greyG = (DiscGrey >> 8) & 0xFF, greyB = DiscGrey & 0xFF;
        if (Math.Abs(Median(c => c.R) - greyR) > 16 || Math.Abs(Median(c => c.G) - greyG) > 16 || Math.Abs(Median(c => c.B) - greyB) > 16)
            return null;
        return new Disc(cx, cy, radius, radius / DiscRadius);
    }

    /// <summary>A gourmand order's disc: a ring of its darker yellow at the disc's place.</summary>
    private static bool IsGourmandDisc(Frame frame, InnLayout layout, double discX)
    {
        var (cx, cy) = layout.TopLeft(discX, DiscY);
        int r0 = (GourmandDisc >> 16) & 0xFF, g0 = (GourmandDisc >> 8) & 0xFF, b0 = GourmandDisc & 0xFF;
        int like = 0, all = 0;
        foreach (double share in new[] { 0.82, 0.9 })
        for (int k = 0; k < 24; k++)
        {
            double t = 2 * Math.PI * k / 24, r = DiscRadius * share * layout.Scale;
            int x = (int)Math.Round(cx + r * Math.Cos(t)), y = (int)Math.Round(cy + r * Math.Sin(t));
            if (x < 0 || y < 0 || x >= frame.Width || y >= frame.Height) continue;
            all++;
            var (pr, pg, pb) = frame.GetPixel(x, y);
            if (Math.Abs(pr - r0) <= 16 && Math.Abs(pg - g0) <= 16 && Math.Abs(pb - b0) <= 16) like++;
        }
        // The icon reaches the ring here and there.
        return all > 0 && like >= all * 0.7;
    }

    /// <summary>The least-squares circle through the points (centre and radius), or null when they don't make one.</summary>
    private static (double X, double Y, double Radius)? FitCircle(List<(double X, double Y)> points)
    {
        // Solve [2x 2y 1]·[a b c] = x² + y² in the least-squares sense; radius² = c + a² + b².
        double sxx = 0, sxy = 0, sx = 0, syy = 0, sy = 0, n = points.Count, bx = 0, by = 0, b1 = 0;
        foreach (var (x, y) in points)
        {
            double z = x * x + y * y;
            sxx += 4 * x * x; sxy += 4 * x * y; sx += 2 * x; syy += 4 * y * y; sy += 2 * y;
            bx += 2 * x * z; by += 2 * y * z; b1 += z;
        }
        // Normal equations, 3×3, by Cramer's rule.
        double[,] m = { { sxx, sxy, sx }, { sxy, syy, sy }, { sx, sy, n } };
        double det = Det(m);
        if (Math.Abs(det) < 1e-9) return null;
        double a = Det(Replace(m, 0, bx, by, b1)) / det, b = Det(Replace(m, 1, bx, by, b1)) / det, c = Det(Replace(m, 2, bx, by, b1)) / det;
        double r2 = c + a * a + b * b;
        return r2 > 0 ? (a, b, Math.Sqrt(r2)) : null;

        static double Det(double[,] q) =>
            q[0, 0] * (q[1, 1] * q[2, 2] - q[1, 2] * q[2, 1]) - q[0, 1] * (q[1, 0] * q[2, 2] - q[1, 2] * q[2, 0]) + q[0, 2] * (q[1, 0] * q[2, 1] - q[1, 1] * q[2, 0]);

        static double[,] Replace(double[,] q, int column, double v0, double v1, double v2)
        {
            var copy = (double[,])q.Clone();
            copy[0, column] = v0; copy[1, column] = v1; copy[2, column] = v2;
            return copy;
        }
    }

    /// <summary>The dish or drink on the disc, or null when no picture is clearly it.</summary>
    internal string? Identify(Frame frame, Disc disc)
    {
        var ranked = Rank(frame, disc);
        if (ranked.Count == 0) return null;
        var (id, best) = ranked[0];
        double next = ranked.Count > 1 ? ranked[1].Difference : double.MaxValue;
        return best <= MaxDifference && next - best >= MinLead ? id : null;
    }

    /// <summary>Every picture's colour difference from the disc (mean per channel, 0–255), closest first.</summary>
    internal List<(string Id, double Difference)> Rank(Frame frame, Disc disc)
    {
        int size = Math.Max(8, (int)Math.Round(ArtShare * disc.Radius));
        var rendered = pictures.Rendered(size, DiscGrey);
        double compare = disc.Radius * CompareShare, corner = disc.Radius * BadgeCorner;
        // The pixels compared: inside the disc, not in the badge's corner. Every other one at 4K (plenty).
        int step = disc.Scale >= 0.9 ? 2 : 1;
        var pixels = new List<(int X, int Y, int R, int G, int B)>();
        for (int dy = -(int)compare; dy <= (int)compare; dy += step)
        for (int dx = -(int)compare; dx <= (int)compare; dx += step)
        {
            if (dx * dx + dy * dy > compare * compare || (dx > corner && dy > corner)) continue;
            int x = (int)Math.Round(disc.X) + dx, y = (int)Math.Round(disc.Y) + dy;
            if (x < 0 || y < 0 || x >= frame.Width || y >= frame.Height) continue;
            var (r, g, b) = frame.GetPixel(x, y);
            pixels.Add((dx, dy, r, g, b));
        }
        if (pixels.Count == 0) return [];

        // The art's top-left relative to the disc's centre, then a pixel either way for the rest of the
        // fit's error (closest few only).
        double artX = -size / 2.0, artY = -size / 2.0 + ArtDrop * disc.Radius;
        int greyR = (DiscGrey >> 16) & 0xFF, greyG = (DiscGrey >> 8) & 0xFF, greyB = DiscGrey & 0xFF;
        double Difference(InnPictures.Rendering p, int shiftX, int shiftY)
        {
            long sum = 0;
            int ox = (int)Math.Round(artX) + shiftX, oy = (int)Math.Round(artY) + shiftY;
            foreach (var (dx, dy, r, g, b) in pixels)
            {
                int px = dx - ox, py = dy - oy;
                int er, eg, eb;
                if (px >= 0 && py >= 0 && px < p.Size && py < p.Size)
                {
                    int i = (py * p.Size + px) * 3;
                    er = p.Rgb[i]; eg = p.Rgb[i + 1]; eb = p.Rgb[i + 2];
                }
                else
                {
                    er = greyR; eg = greyG; eb = greyB;
                }
                sum += Math.Abs(r - er) + Math.Abs(g - eg) + Math.Abs(b - eb);
            }
            return sum / (3.0 * pixels.Count);
        }

        var first = rendered.Select(p => (Picture: p, Difference: Difference(p, 0, 0))).OrderBy(p => p.Difference).ToList();
        var refined = first.Take(4).Select(p => (p.Picture.Id, Difference: (
            from sx in new[] { -1, 0, 1 }
            from sy in new[] { -1, 0, 1 }
            select Difference(p.Picture, sx, sy)).Min()));
        return [.. refined.Concat(first.Skip(4).Select(p => (p.Picture.Id, p.Difference))).OrderBy(p => p.Difference)];
    }

    /// <summary>
    /// The blue spinner on the disc's lower right (a ring of cyan dots): the game is serving the item
    /// on its own, so it counts as served (owner). The green smiley that can sit in the same corner
    /// means nothing useful (owner) and is not looked for; it has no cyan.
    /// </summary>
    private static bool HasSpinner(Frame frame, Disc disc)
    {
        int from = (int)Math.Round(5 * disc.Scale), to = (int)Math.Round(75 * disc.Scale);
        int cyan = 0, all = 0;
        for (int dy = from; dy < to; dy++)
        for (int dx = from; dx < to; dx++)
        {
            int x = (int)Math.Round(disc.X) + dx, y = (int)Math.Round(disc.Y) + dy;
            if (x >= frame.Width || y >= frame.Height) continue;
            all++;
            var (r, g, b) = frame.GetPixel(x, y);
            if (g - r >= 40 && b - r >= 40) cyan++;
        }
        // Measured at 4K: the spinner's dots cover ~7.5 % of the corner, art without it 0.
        return all > 0 && cyan >= all * 0.025;
    }

    /// <summary>The two pips under the number, green or not.</summary>
    /// <summary>
    /// A gourmand's order: whether each request is resolved, its pip no longer the card's cream but mint green (served), yellow-green
    /// (served imperfectly) or red (failed): test_yuanyuan8, (113, 250, 162), (207, 250, 72), (217, 24, 6) at 4K. Nothing more is
    /// to be done for a resolved request whichever its colour. Most of the pip's middle must say so (a small window blurs its edge).
    /// </summary>
    private static IReadOnlyList<bool> GourmandPips(Frame frame, InnLayout layout, double left) =>
        [.. PipX.Select(x =>
        {
            var (px, py) = layout.TopLeft(left + x, PipY);
            int gx = (int)Math.Round(px), gy = (int)Math.Round(py), coloured = 0;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int sx = gx + dx, sy = gy + dy;
                if (sx < 0 || sy < 0 || sx >= frame.Width || sy >= frame.Height) continue;
                var (r, g, b) = frame.GetPixel(sx, sy);
                int max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
                if (max >= 150 && max - min >= 100) coloured++;
            }
            return coloured >= 5;
        })];

    private static IReadOnlyList<bool> Pips(Frame frame, InnLayout layout, double left) =>
        [.. PipX.Select(x =>
        {
            var (px, py) = layout.TopLeft(left + x, PipY);
            int gx = (int)Math.Round(px), gy = (int)Math.Round(py);
            if (gx < 0 || gy < 0 || gx >= frame.Width || gy >= frame.Height) return false;
            var (r, g, b) = frame.GetPixel(gx, gy);
            return g >= 200 && g - b >= 100 && r >= 140;
        })];
}
