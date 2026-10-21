using System.Security.Cryptography;
using EndHud.Vision.Operators;

namespace EndHud.Vision.Inn;

/// <summary>A bonus icon under a chef picker card: what that chef speeds up in a kitchen.</summary>
public enum ChefIcon
{
    /// <summary>主食 (a gold square with a bowl).</summary>
    Entree,

    /// <summary>菜肴 (a green square).</summary>
    Side,

    /// <summary>汤羹 (a blue square).</summary>
    Soup,

    /// <summary>甜品 (a red square).</summary>
    Dessert,

    /// <summary>珍馐 (a yellow circle with a cloche).</summary>
    Choice,
}

/// <summary>One card of the chef picker's grid.</summary>
/// <param name="Place">The card in the frame.</param>
/// <param name="Icons">Its two bonus icons, left to right; null where one couldn't be told.</param>
/// <param name="Flavor">The flavor chip's trait id ("Salty"); null when unread.</param>
/// <param name="Candidates">The operators whose bonuses are exactly these icons and flavor.</param>
/// <param name="OperatorId">Who the card is: the one candidate its portrait matches; null when the profile and the portrait don't agree (never guessed).</param>
/// <param name="Portrait">The operator the portrait is closest to in the whole roster and how close (0 = the same character); null when it wasn't compared.</param>
public sealed record ChefCard(PixelRect Place, IReadOnlyList<ChefIcon?> Icons, string? Flavor, IReadOnlyList<string> Candidates, string? OperatorId,
    (string Id, float Difference)? Portrait = null);

/// <summary>
/// The chef picker (a kitchen's 安排干员 screen): who each card of the grid is, though the cards
/// show no names. A card shows the operator's portrait, two bonus icons (told by their colour) and
/// a flavor chip (one character, by OCR). Icons and flavor are the operator's bonus profile
/// (<see cref="InnData.ChefBonusesOf"/>), which only a few operators share; the portrait (the same
/// drawing as the wiki avatar, compared by the character model, <see cref="PortraitMatcher"/>)
/// picks among them. A card is named only when the portrait's closest operator in the whole roster
/// is one of the profile's candidates and clearly closer than the others: a profile and a portrait
/// that disagree leave the card unnamed. Measured on one 4K frame (chef-picker.jpg, twelve cards),
/// and its 1440p and 1080p copies. Used only to mark the recommended chefs' cards in view (owner,
/// 2026-10-20: "in the choose dish and choose cook menu, show the same suggestion"): nothing is
/// remembered and no scrolling is asked for.
/// </summary>
public sealed class ChefPickerReader(InnData data, PortraitMatcher matcher)
{
    // At 4K, on the centred 16:9 area. The grid's four columns: the cards' left edges and width.
    internal static readonly int[] ColumnLefts = [109, 379, 650, 920];
    private const int CardWidth = 240;
    // The list's visible rows: a card is read only when it's wholly inside.
    private const int ListTop = 300, ListBottom = 1705;
    // From the bottom of a card's dark label bar ("暂无安排"): the card's top, the bar's top,
    // the icons' row (centre) and the flavor chip.
    private const int BarToCardTop = 273, BarHeight = 38, BarToIcons = 45, IconRadius = 24, BarToChipTop = 78, BarToChipBottom = 125;
    private static readonly int[] IconCentres = [86, 150];
    private const int ChipInset = 12;

    /// <summary>
    /// Difference (0 = the same character) above which a portrait is no one the roster has (a new
    /// operator the package doesn't know yet). Measured on the twelve cards at 4K, 1440p and 1080p:
    /// each card's own operator 0.017–0.056; the nearest other operator 0.076 (噗切娜 to 大潘's card,
    /// both drawn as round white creatures) to 0.13.
    /// </summary>
    public const float MaxDifference = 0.07f;

    /// <summary>
    /// Among a profile's candidates, the named one must be closer than every other by this much
    /// (大潘's card: 大潘 0.029, 噗切娜 0.076 at 4K).
    /// </summary>
    public const float MinLead = 0.03f;

    private readonly IReadOnlyList<OperatorInfo> _roster = matcher.Library.Operators;
    private readonly Dictionary<string, float[]> _differences = [];
    private readonly Queue<string> _cached = new();
    private const int CacheSize = 64;

    /// <summary>The grid's cards wholly in view, top to bottom, left to right; empty when none is found.</summary>
    public IReadOnlyList<ChefCard> Read(Frame frame)
    {
        var layout = new InnLayout(frame);
        var cards = new List<ChefCard>();
        int portraitWidth = Math.Max(1, (int)Math.Round(CardWidth * layout.Scale));
        // The avatars are described for this size in the background, once; until then cards are left unnamed.
        bool avatarsReady = matcher.HasWikiAvatars(portraitWidth);
        if (!avatarsReady) _ = matcher.PrepareWikiAvatars(portraitWidth);
        foreach (int left in ColumnLefts)
        {
            foreach (double barBottom in Bars(frame, layout, left))
            {
                var icons = IconCentres.Select(x => ClassifyIcon(frame, layout, left + x, barBottom + BarToIcons)).ToList();
                // Not a card: the dark run wasn't a label bar (dark hair, a shadow).
                if (icons.All(i => i is null)) continue;
                var place = layout.Centered(new PixelRect(left, (int)Math.Round(barBottom - BarToCardTop), CardWidth, BarToCardTop + BarToChipBottom));
                string? flavor = ReadFlavor(frame, layout, left, barBottom);
                var candidates = Candidates(icons, flavor);
                string? id = null;
                (string, float)? closest = null;
                if (candidates.Count > 0 && avatarsReady)
                {
                    var portrait = layout.Centered(new PixelRect(left, (int)Math.Round(barBottom - BarToCardTop), CardWidth, BarToCardTop - BarHeight));
                    var differences = Differences(frame.Crop(portrait.ClampTo(frame.Width, frame.Height)));
                    id = Name(differences, candidates);
                    if (differences is { Length: > 0 } && differences.Length == _roster.Count)
                    {
                        int best = Array.IndexOf(differences, differences.Min());
                        closest = (_roster[best].Id, differences[best]);
                    }
                }
                cards.Add(new ChefCard(place, icons, flavor, candidates, id, closest));
            }
        }
        return [.. cards.OrderBy(c => c.Place.Y / Math.Max(1, (int)(100 * layout.Scale))).ThenBy(c => c.Place.X)];
    }

    /// <summary>
    /// The bottoms of the label bars in a column (4K coordinates of the centred area): rows where
    /// most of the card's width is near-black, a run about a bar's height, the whole card in the list.
    /// </summary>
    internal static IEnumerable<double> Bars(Frame frame, InnLayout layout, int left)
    {
        double s = layout.Scale;
        var columns = Enumerable.Range(0, CardWidth / 4).Select(i => layout.Centered(new PixelRect(left + 4 + i * 4 - 2, 0, 1, 1)).X).Distinct().ToList();
        int top = layout.Centered(new PixelRect(0, ListTop, 1, 1)).Y, bottom = layout.Centered(new PixelRect(0, ListBottom, 1, 1)).Y;
        double origin = layout.Centered(new PixelRect(0, 0, 1, 1)).Y;
        int gap = Math.Max(1, (int)Math.Round(4 * s));
        int? start = null, lastDark = null;
        var runs = new List<(int Start, int End)>();
        for (int y = Math.Max(0, top); y < Math.Min(frame.Height, bottom); y++)
        {
            int dark = 0;
            foreach (int x in columns)
                if (x >= 0 && x < frame.Width && frame.GetPixel(x, y) is var (r, g, b) && Math.Max(r, Math.Max(g, b)) <= 70) dark++;
            if (dark >= columns.Count / 2)
            {
                start ??= y;
                lastDark = y;
            }
            else if (start is not null && y - lastDark!.Value > gap)
            {
                runs.Add((start.Value, lastDark.Value + 1));
                start = null;
            }
        }
        if (start is not null) runs.Add((start.Value, lastDark!.Value + 1));
        foreach (var (from, to) in runs)
        {
            double length = (to - from) / s;
            if (length < 28 || length > 50) continue;
            double barBottom = (to - origin) / s;
            if (barBottom - BarToCardTop < ListTop - 10 || barBottom + BarToChipBottom > ListBottom) continue;
            yield return barBottom;
        }
    }

    /// <summary>
    /// An icon by the median hue and brightness of its saturated pixels within its circle: gold
    /// (主食, h 0.13 v 0.59), green (菜肴, h 0.34), blue (汤羹, h 0.55), red (甜品, h 0.98) and bright
    /// yellow (珍馐, h 0.16 v 0.98), as measured; null when too few pixels or none of these.
    /// </summary>
    internal static ChefIcon? ClassifyIcon(Frame frame, InnLayout layout, double x, double y)
    {
        if (IconColour(frame, layout, x, y) is not var (hue, value, share)) return null;
        // The squares' fill covers over half the circle, the 珍馐 circle 34–44 % (its dark cloche the
        // rest), measured at 4K, 1440p and 1080p; less is no icon.
        if (share < 0.25) return null;
        if (Math.Abs(hue - -0.02) <= 0.05) return ChefIcon.Dessert;
        if (Math.Abs(hue - 0.34) <= 0.06) return ChefIcon.Side;
        if (Math.Abs(hue - 0.55) <= 0.06) return ChefIcon.Soup;
        if (hue is >= 0.09 and <= 0.2) return value >= 0.8 ? ChefIcon.Choice : value <= 0.72 ? ChefIcon.Entree : null;
        return null;
    }

    /// <summary>
    /// The median hue (red near 0 as just below it) and brightness of the saturated pixels in the
    /// icon's circle, and their share of the circle; null when there are none.
    /// </summary>
    internal static (double Hue, double Value, double Share)? IconColour(Frame frame, InnLayout layout, double x, double y)
    {
        var centre = layout.Centered(new PixelRect((int)Math.Round(x), (int)Math.Round(y), 1, 1));
        double radius = IconRadius * layout.Scale;
        var hues = new List<double>();
        var values = new List<double>();
        int total = 0;
        for (int py = (int)(centre.Y - radius); py <= (int)(centre.Y + radius); py++)
        for (int px = (int)(centre.X - radius); px <= (int)(centre.X + radius); px++)
        {
            if (px < 0 || py < 0 || px >= frame.Width || py >= frame.Height) continue;
            double dx = px - centre.X, dy = py - centre.Y;
            if (dx * dx + dy * dy > radius * radius) continue;
            total++;
            var (r, g, b) = frame.GetPixel(px, py);
            var (h, sat, v) = Hsv(r, g, b);
            if (sat <= 0.45 || v <= 0.3) continue;
            hues.Add(h > 0.9 ? h - 1 : h);
            values.Add(v);
        }
        if (total == 0 || hues.Count == 0) return null;
        return (Median(hues), Median(values), hues.Count / (double)total);
    }

    private static double Median(List<double> values)
    {
        values.Sort();
        return values[values.Count / 2];
    }

    private static (double H, double S, double V) Hsv(byte r, byte g, byte b)
    {
        double rf = r / 255.0, gf = g / 255.0, bf = b / 255.0;
        double max = Math.Max(rf, Math.Max(gf, bf)), min = Math.Min(rf, Math.Min(gf, bf)), d = max - min;
        double h = d == 0 ? 0 : max == rf ? (gf - bf) / d % 6 : max == gf ? (bf - rf) / d + 2 : (rf - gf) / d + 4;
        h /= 6;
        if (h < 0) h += 1;
        return (h, max == 0 ? 0 : d / max, max);
    }

    /// <summary>The flavor chip of the card whose bar ends at <paramref name="barBottom"/> (window pixels).</summary>
    internal static PixelRect FlavorChip(InnLayout layout, int left, double barBottom) =>
        layout.Centered(new PixelRect(left + ChipInset, (int)Math.Round(barBottom + BarToChipTop), CardWidth - 2 * ChipInset, BarToChipBottom - BarToChipTop));

    /// <summary>
    /// The flavor chip's one character (咸, 辣, 鲜, 酸, 甜) as a trait id, by its shape
    /// (<see cref="FlavorShapes"/>): Windows OCR read hardly any of these lone grey characters
    /// (3 of 12 at 4K, measured). Null when the glyph isn't clearly one of them.
    /// </summary>
    private string? ReadFlavor(Frame frame, InnLayout layout, int left, double barBottom)
    {
        if (FlavorMask(frame, FlavorChip(layout, left, barBottom), layout.Scale) is not { } mask) return null;
        var ranked = FlavorShapes.Select(s => (s.Zh, Score: Correlation(mask, s.Mask))).OrderByDescending(s => s.Score).ToList();
        var (best, score) = ranked[0];
        if (score < FlavorMatchAt || score - ranked[1].Score < FlavorLead) return null;
        return data.Traits.FirstOrDefault(t => t.Flavor && t.Zh == best)?.Id;
    }

    // Likeness of a chip's glyph to the best flavor shape, and its lead over the next.
    private const double FlavorMatchAt = 0.75, FlavorLead = 0.1;
    internal const int MaskSize = 16;

    /// <summary>The chip's grey letter; not the chip's near-white, nor the faint logo behind it.</summary>
    private static bool ChipInk(byte r, byte g, byte b)
    {
        int max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        return max <= 160 && max - min <= 30;
    }

    /// <summary>
    /// The chip's character as <see cref="MaskSize"/>² cells, each the share of its patch that is
    /// ink, over the box of the ink not touching the region's edge (the chip's frame). Enlarged to
    /// 4K size first in a smaller window, as <see cref="InnDigits"/> does. Null when there's no ink.
    /// </summary>
    internal static float[]? FlavorMask(Frame frame, PixelRect region, double hudScale)
    {
        var r = region.ClampTo(frame.Width, frame.Height);
        if (r.Width < 4 || r.Height < 4) return null;
        if (hudScale < 0.9)
        {
            double enlarge = Math.Min(4, 1 / hudScale);
            frame = frame.Crop(r).ResizeSmooth((int)Math.Round(r.Width * enlarge), (int)Math.Round(r.Height * enlarge));
            r = new PixelRect(0, 0, frame.Width, frame.Height);
        }
        int w = r.Width, h = r.Height;
        var on = new bool[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            var (cr, cg, cb) = frame.GetPixel(r.X + x, r.Y + y);
            on[y * w + x] = ChipInk(cr, cg, cb);
        }
        // Blobs (8 neighbours); those touching the edge are the chip's frame or what's around it.
        var seen = new bool[on.Length];
        var keep = new bool[on.Length];
        var stack = new Stack<int>();
        var blob = new List<int>();
        int left = w, right = -1, top = h, bottom = -1;
        for (int start = 0; start < on.Length; start++)
        {
            if (!on[start] || seen[start]) continue;
            blob.Clear();
            bool edge = false;
            seen[start] = true;
            stack.Push(start);
            while (stack.Count > 0)
            {
                int p = stack.Pop();
                blob.Add(p);
                int x = p % w, y = p / w;
                if (x == 0 || y == 0 || x == w - 1 || y == h - 1) edge = true;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    int q = ny * w + nx;
                    if (!on[q] || seen[q]) continue;
                    seen[q] = true;
                    stack.Push(q);
                }
            }
            if (edge || blob.Count < 3) continue;
            foreach (int p in blob)
            {
                keep[p] = true;
                left = Math.Min(left, p % w); right = Math.Max(right, p % w);
                top = Math.Min(top, p / w); bottom = Math.Max(bottom, p / w);
            }
        }
        if (right < left) return null;
        int bw = right - left + 1, bh = bottom - top + 1;
        // A character is about as wide as tall; a sliver is a stray stroke, not a glyph.
        if (bw < bh * 0.5 || bh < bw * 0.5) return null;
        var mask = new float[MaskSize * MaskSize];
        for (int y = 0; y < MaskSize; y++)
        for (int x = 0; x < MaskSize; x++)
        {
            int x0 = left + x * bw / MaskSize, x1 = Math.Max(x0 + 1, left + (x + 1) * bw / MaskSize);
            int y0 = top + y * bh / MaskSize, y1 = Math.Max(y0 + 1, top + (y + 1) * bh / MaskSize);
            int inked = 0, all = 0;
            for (int yy = y0; yy < y1; yy++)
            for (int xx = x0; xx < x1; xx++)
            {
                all++;
                if (keep[yy * w + xx]) inked++;
            }
            mask[y * MaskSize + x] = inked / (float)all;
        }
        return mask;
    }

    private static double Correlation(float[] a, float[] b)
    {
        double ma = a.Average(), mb = b.Average(), ab = 0, aa = 0, bb = 0;
        for (int i = 0; i < a.Length; i++)
        {
            double da = a[i] - ma, db = b[i] - mb;
            ab += da * db;
            aa += da * da;
            bb += db * db;
        }
        return aa == 0 || bb == 0 ? 0 : ab / Math.Sqrt(aa * bb);
    }

    /// <summary>A mask as rows of cells 0–9 (tenths of ink), as <see cref="FlavorShapes"/> are written.</summary>
    internal static string Describe(float[] mask) =>
        string.Join("|", Enumerable.Range(0, MaskSize).Select(y => new string([.. Enumerable.Range(0, MaskSize).Select(x => (char)('0' + (int)Math.Round(mask[y * MaskSize + x] * 9)))])));

    internal sealed record FlavorShape(string Zh, float[] Mask);

    private static FlavorShape F(string zh, string rows) => new(zh, [.. rows.Split('|').SelectMany(row => row.Select(ch => (ch - '0') / 9f))]);

    /// <summary>
    /// The five flavor characters as the chips draw them: each the average of its chips on
    /// chef-picker.jpg at 4K (咸 ×3, 辣 ×2, 鲜 ×4, 酸 ×2, 甜 ×1).
    /// </summary>
    internal static readonly FlavorShape[] FlavorShapes =
    [
        F("咸", "0000000004802300|0000000004803800|0233333335933962|0499999999999996|0494333336943332|0491111111930220|0494999992930680|0491222220940940|0491222210874820|0491999980687700|0491952680499300|0490940580197015|0580920580496028|0970999984989027|4940000049608785|7910000076002982"),
        F("辣", "0049000000490000|0039200000490000|5789863688898884|3784882344896443|0460460000490000|0260640466896661|1381840674794492|8999994640390092|0039200672694392|0039200699999993|3689762014998120|2789771019897400|0049000188491920|0076002781390395|0491019800490028|2940002000490000"),
        F("甜", "0000042099000690|2499999099000690|4999740099000690|0009400999999999|0009400499444894|7999999299000690|4669866199000690|0009400099000690|0009400099847990|0889973099666890|0984484099000690|0930064099000690|0930064099000690|0930064099444890|0999994099999990|0464332099333790"),
        F("鲜", "0027200007400370|0059322006700670|0188897001912910|0870074099999996|7952492067799774|6898989200078000|0742909200078000|0754929248899884|0799999234499442|0753919200089000|0753929211199111|0799999499999999|0233333144489444|0001344200078000|5999998300078000|3532200000078000"),
        F("酸", "0000000000280000|9999999500670200|4468774304820840|0034440039622692|1478774299999996|2978878333421312|2934237307700840|2934437588410286|2944368462922224|2981189307999990|2920008359622860|2954449796812910|2989789420689500|2900007300499400|2954449338934873|2666666485200256"),
    ];

    /// <summary>The operators (cooks only) whose bonuses are exactly <paramref name="icons"/> and <paramref name="flavor"/>.</summary>
    internal IReadOnlyList<string> Candidates(IReadOnlyList<ChefIcon?> icons, string? flavor)
    {
        if (flavor is null || icons.Any(i => i is null)) return [];
        var wanted = icons.Select(i => i!.Value).Order().ToList();
        return [.. _roster.Where(op =>
        {
            var bonuses = data.ChefBonusesOf(op);
            if (bonuses.Count == 0) return false;
            var mine = bonuses.Where(b => b.Kind != ChefBonusKind.Flavor).Select(IconOf).Order().ToList();
            return mine.SequenceEqual(wanted) && bonuses.Count(b => b.Kind == ChefBonusKind.Flavor && b.Flavor == flavor) == 1
                   && bonuses.Count(b => b.Kind == ChefBonusKind.Flavor) == 1;
        }).Select(op => op.Id)];
    }

    private static ChefIcon IconOf(ChefBonus bonus) => bonus.Kind == ChefBonusKind.ChoiceChance
        ? ChefIcon.Choice
        : bonus.DishType switch
        {
            DishType.Entree => ChefIcon.Entree,
            DishType.Side => ChefIcon.Side,
            DishType.Soup => ChefIcon.Soup,
            _ => ChefIcon.Dessert,
        };

    /// <summary>
    /// The candidate the portrait names: the closest operator of the whole roster, close enough,
    /// one of the candidates and clearly closer than the other candidates. Null otherwise.
    /// </summary>
    internal string? Name(float[]? differences, IReadOnlyList<string> candidates)
    {
        if (differences is null || differences.Length != _roster.Count) return null;
        int best = 0;
        for (int j = 1; j < differences.Length; j++)
            if (differences[j] < differences[best]) best = j;
        if (differences[best] > MaxDifference || !candidates.Contains(_roster[best].Id)) return null;
        for (int j = 0; j < differences.Length; j++)
            if (j != best && candidates.Contains(_roster[j].Id) && differences[j] - differences[best] < MinLead) return null;
        return _roster[best].Id;
    }

    /// <summary>The portrait's differences from every operator, kept for portraits seen before (the list sits still between reads).</summary>
    private float[]? Differences(Frame portrait)
    {
        string key = Convert.ToHexString(SHA1.HashData(portrait.Bgra)) + portrait.Width;
        lock (_differences)
            if (_differences.TryGetValue(key, out var known)) return known;
        var row = matcher.Differences(portrait);
        if (row is null) return null;
        lock (_differences)
        {
            if (_differences.TryAdd(key, row)) _cached.Enqueue(key);
            while (_cached.Count > CacheSize) _differences.Remove(_cached.Dequeue());
        }
        return row;
    }
}
