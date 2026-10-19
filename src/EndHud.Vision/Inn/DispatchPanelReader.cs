using EndHud.Vision.Operators;

namespace EndHud.Vision.Inn;

/// <summary>A dish on the dispatch panel's menu: what the shown kitchen cooks and how long it takes there.</summary>
/// <param name="DishId">The dish (<see cref="Dish.Id"/>), or null when the name read matches none.</param>
/// <param name="Name">The name as read.</param>
/// <param name="CookSeconds">The cook time in this kitchen, its chefs' bonuses counted; null when unread.</param>
public sealed record KitchenDish(string? DishId, string Name, int? CookSeconds);

/// <summary>A place in the kitchen's queue.</summary>
/// <param name="Filled">A dish is there (its "−" shows).</param>
/// <param name="DishId">The dish, or null when its picture isn't clearly one (or the place is empty).</param>
/// <param name="SecondsLeft">For the dish cooking: its time left; null for the waiting ones and when unread.</param>
public sealed record QueueSlot(bool Filled, string? DishId, int? SecondsLeft);

/// <summary>What the dispatch panel (派单) shows of one kitchen.</summary>
/// <param name="Kitchen">The kitchen shown, 1–3 (its tab is the dark one).</param>
/// <param name="Kitchens">How many kitchens have a tab.</param>
/// <param name="Chefs">The kitchen's two chef places: the operator's id, or null when empty or unread.</param>
/// <param name="ChefNames">The names as read ("赛希"), null when empty or unread.</param>
/// <param name="ChoiceChancePercent">珍馐产出概率; null when unread.</param>
/// <param name="Dishes">The menu's dishes, left to right, top to bottom.</param>
/// <param name="Queue">The queue: [0] the dish cooking (right), then the waiting ones in the order they'll cook.</param>
public sealed record DispatchPanel(int Kitchen, int Kitchens, IReadOnlyList<string?> Chefs, IReadOnlyList<string?> ChefNames,
    int? ChoiceChancePercent, IReadOnlyList<KitchenDish> Dishes, IReadOnlyList<QueueSlot> Queue);

/// <summary>
/// The dispatch panel (派单) over the session: which kitchen it shows, the kitchen's chefs (大厨…,
/// OCR, matched to the roster by Chinese name), its choice chance, each dish's cook time there, and
/// the queue (the dish cooking with its time left, the dishes waiting). Laid out on the centred
/// 16:9 area; measured on 4K frames. Nothing is guessed: what isn't clearly read is null.
/// </summary>
public sealed class DispatchPanelReader(InnText text, InnPictures pictures, InnData data, IReadOnlyList<OperatorInfo> roster)
{
    // At 4K. The kitchen tabs: dark when shown, light grey otherwise, absent beyond the kitchens there are.
    private const double FirstTabX = 520, TabStep = 665, TabSampleY = 465;
    // The chefs' names under their portraits.
    private static readonly PixelRect[] ChefNameRegions = [new(700, 1015, 290, 60), new(990, 1015, 290, 60)];
    // Where the bonus list ends with the choice chance's row ("珍馐产出概率 18%"): its place moves with the list's length.
    private static readonly PixelRect BonusArea = new(640, 1250, 700, 320);
    private const int ChanceDigitsLeft = 1150, ChanceDigitsRight = 1330;
    // The menu: two columns, up to four rows of dish cards.
    private const double CardX = 1467, CardColumnStep = 920, CardY = 630, CardRowStep = 277.5;
    private static readonly PixelRect CookTime = new(58, 12, 100, 46), DishName = new(320, 22, 380, 62);
    // The queue: the dish cooking (right, in a white circle), and two waiting (grey discs), each with a "−".
    private static readonly (double X, double Y, int ArtSize, int Compare)[] Slots = [(1209, 1608, 195, 55), (962, 1672, 190, 70), (732, 1672, 190, 70)];
    // The cooking place's "−" sits further right than a waiting one's; a dish that has just finished
    // (faded, on a grey disc) has its "−" where a waiting one's would be.
    private static readonly (double X, double Y)[][] RemoveButtons = [[(1330, 1587), (1275, 1587)], [(1033, 1587)], [(805, 1587)]];
    private static readonly PixelRect CookingTime = new(1150, 1666, 120, 52);
    // A picture is named when its mean colour difference is at most this and the next one this much
    // further (measured: right pictures 10.6–29.6, the next ones ≥ 14.8 behind).
    private const double MaxDifference = 36, MinLead = 8;

    /// <summary>The panel, or null when it isn't showing (no tab is dark).</summary>
    public async Task<DispatchPanel?> ReadAsync(Frame frame)
    {
        var layout = new InnLayout(frame);
        int kitchen = 0, kitchens = 0;
        for (int k = 1; k <= 3; k++)
        {
            var (x, y) = Point(layout, FirstTabX + TabStep * (k - 1), TabSampleY);
            var (r, g, b) = frame.GetPixel(x, y);
            int max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            if (max - min > 12) break;
            if (max <= 60) kitchen = k;
            else if (max is < 170 or > 210) break;
            kitchens = k;
        }
        if (kitchen == 0) return null;

        var chefs = new string?[2];
        var names = new string?[2];
        for (int i = 0; i < 2; i++)
        {
            // Read at a few sizes until a name in the roster comes out; the last name read is kept either way.
            await foreach (string attempt in text.ReadAttemptsAsync(frame, layout.Centered(ChefNameRegions[i]), ChefInk, layout.Scale))
            {
                string read = attempt.Replace(" ", "");
                int at = read.IndexOf("大厨", StringComparison.Ordinal);
                string? name = at >= 0 && read.Length > at + 2 ? read[(at + 2)..] : null;
                if (name is null) continue;
                names[i] = name;
                if ((chefs[i] = InnNames.Closest(name, roster.Select(o => (o.Id, o.NameZh)))) is not null)
                {
                    names[i] = roster.First(o => o.Id == chefs[i]).NameZh;
                    break;
                }
            }
        }

        return new DispatchPanel(kitchen, kitchens, chefs, names, await ReadChanceAsync(frame, layout),
            await ReadDishesAsync(frame, layout), ReadQueue(frame, layout));
    }

    private static (int X, int Y) Point(InnLayout layout, double x, double y)
    {
        var r = layout.Centered(new PixelRect((int)Math.Round(x), (int)Math.Round(y), 1, 1));
        return (r.X, r.Y);
    }

    /// <summary>
    /// The small white numbers on dark badges (cook times, the time left, the choice chance): a lower
    /// bar than <see cref="InnText.White"/>, as their strokes blur to grey in a small window.
    /// </summary>
    private static bool LightOnDark(byte r, byte g, byte b) => Math.Min(r, Math.Min(g, b)) >= 120 && Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)) <= 60;

    /// <summary>The chefs' names: light grey on the panel's dark grey.</summary>
    private static bool ChefInk(byte r, byte g, byte b) => Math.Min(r, Math.Min(g, b)) >= 150 && Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)) <= 40;

    // A number's unit ("s") is lower case, shorter than the digits: leave it out by height.
    private const double DigitsNotUnit = 0.82;

    /// <summary>"27" as seconds; null for anything that isn't one or two digits (a cook time is under a minute).</summary>
    private static int? Seconds(string? read) => read is { Length: >= 1 and <= 2 } && read.All(char.IsAsciiDigit) ? int.Parse(read) : null;

    private async Task<int?> ReadChanceAsync(Frame frame, InnLayout layout)
    {
        foreach (var (line, place) in await text.ReadPlacedLinesAsync(frame, layout.Centered(BonusArea), InnText.White, layout.Scale))
        {
            if (!line.Contains("概率") && !line.Contains("产出")) continue;
            var left = layout.Centered(new PixelRect(ChanceDigitsLeft, 0, ChanceDigitsRight - ChanceDigitsLeft, 1));
            int pad = place.Height / 2;
            var digits = new PixelRect(left.X, place.Y - pad, left.Width, place.Height + 2 * pad);
            string? read = InnDigits.Read(frame, digits, LightOnDark, layout.Scale, dropUnknownLast: true);
            return read is { Length: >= 1 and <= 3 } && read.All(char.IsAsciiDigit) && int.Parse(read) is var n and <= 100 ? n : null;
        }
        return null;
    }

    private async Task<IReadOnlyList<KitchenDish>> ReadDishesAsync(Frame frame, InnLayout layout)
    {
        var dishes = new List<KitchenDish>();
        for (int row = 0; row < 4; row++)
        for (int column = 0; column < 2; column++)
        {
            double x = CardX + CardColumnStep * column, y = CardY + CardRowStep * row;
            var nameRegion = layout.Centered(DishName with { X = (int)x + DishName.X, Y = (int)y + DishName.Y });
            string? name = null, id = null;
            await foreach (string attempt in text.ReadAttemptsAsync(frame, nameRegion, InnText.Dark, layout.Scale))
            {
                string read = attempt.Replace(" ", "");
                if (read.Length == 0) continue;
                name = read;
                if ((id = InnNames.Closest(read, data.Dishes.Select(d => (d.Id, d.Zh)))) is not null)
                {
                    name = data.Dishes.First(d => d.Id == id).Zh;
                    break;
                }
            }
            if (name is null) continue;
            var timeRegion = layout.Centered(CookTime with { X = (int)x + CookTime.X, Y = (int)y + CookTime.Y });
            dishes.Add(new KitchenDish(id, name, Seconds(InnDigits.Read(frame, timeRegion, LightOnDark, layout.Scale, minHeightShare: DigitsNotUnit))));
        }
        return dishes;
    }

    private IReadOnlyList<QueueSlot> ReadQueue(Frame frame, InnLayout layout)
    {
        var queue = new List<QueueSlot>();
        for (int i = 0; i < Slots.Length; i++)
        {
            if (!RemoveButtons[i].Any(at => HasRemoveButton(frame, layout, at)))
            {
                queue.Add(new QueueSlot(false, null, null));
                continue;
            }
            var (x, y, size, compare) = Slots[i];
            string? id = IdentifySlot(frame, layout, x, y, size, compare);
            int? seconds = null;
            if (i == 0)
            {
                seconds = Seconds(InnDigits.Read(frame, layout.Centered(CookingTime), LightOnDark, layout.Scale, minHeightShare: DigitsNotUnit));
            }
            queue.Add(new QueueSlot(true, id, seconds));
        }
        return queue;
    }

    /// <summary>A "−" button: a light bar on a small dark disc.</summary>
    private static bool HasRemoveButton(Frame frame, InnLayout layout, (double X, double Y) at)
    {
        var area = layout.Centered(new PixelRect((int)at.X - 25, (int)at.Y - 20, 50, 40)).ClampTo(frame.Width, frame.Height);
        int light = 0, dark = 0, left = int.MaxValue, right = -1, top = int.MaxValue, bottom = -1;
        for (int y = area.Y; y < area.Bottom; y++)
        for (int x = area.X; x < area.Right; x++)
        {
            var (r, g, b) = frame.GetPixel(x, y);
            int max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            if (min >= 170 && max - min <= 30)
            {
                light++;
                left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y);
            }
            else if (max <= 90) dark++;
        }
        if (light == 0) return false;
        int width = right - left + 1, height = bottom - top + 1;
        // The bar is about 26 × 9 at 4K, filled; the disc around it dark.
        return width >= 2.2 * height && width >= 16 * layout.Scale && light >= width * height * 0.6 && dark >= area.Width * area.Height * 0.4;
    }

    /// <summary>The dish in a queue place, compared over the place's disc like a table card's picture, on the disc's own colour.</summary>
    private string? IdentifySlot(Frame frame, InnLayout layout, double x, double y, int artSize, int compareRadius)
    {
        double s = layout.Scale;
        var (cx, cy) = Point(layout, x, y);
        int size = Math.Max(8, (int)Math.Round(artSize * s)), radius = Math.Max(4, (int)Math.Round(compareRadius * s));
        // The disc's colour around the art: its median on a ring outside the compared area.
        var ring = new List<(int R, int G, int B)>();
        double ringRadius = (compareRadius + 12) * s;
        for (int k = 0; k < 60; k++)
        {
            double t = 2 * Math.PI * k / 60;
            int px = (int)Math.Round(cx + ringRadius * Math.Cos(t)), py = (int)Math.Round(cy + ringRadius * Math.Sin(t));
            if (px >= 0 && py >= 0 && px < frame.Width && py < frame.Height) ring.Add(frame.GetPixel(px, py));
        }
        if (ring.Count == 0) return null;
        int Median(Func<(int R, int G, int B), int> channel) => ring.Select(channel).Order().ElementAt(ring.Count / 2);
        int background = (Quantise(Median(c => c.R)) << 16) | (Quantise(Median(c => c.G)) << 8) | Quantise(Median(c => c.B));
        var rendered = pictures.Rendered(size, background);

        int step = s >= 0.9 ? 2 : 1;
        var pixels = new List<(int Dx, int Dy, int R, int G, int B)>();
        for (int dy = -radius; dy <= radius; dy += step)
        for (int dx = -radius; dx <= radius; dx += step)
        {
            if (dx * dx + dy * dy > radius * radius) continue;
            int px = cx + dx, py = cy + dy;
            if (px < 0 || py < 0 || px >= frame.Width || py >= frame.Height) continue;
            var (r, g, b) = frame.GetPixel(px, py);
            pixels.Add((dx, dy, r, g, b));
        }
        if (pixels.Count == 0) return null;
        int bgR = (background >> 16) & 0xFF, bgG = (background >> 8) & 0xFF, bgB = background & 0xFF;
        double Difference(InnPictures.Rendering p, int shiftX, int shiftY)
        {
            long sum = 0;
            int ox = -size / 2 + shiftX, oy = -size / 2 + shiftY;
            foreach (var (dx, dy, r, g, b) in pixels)
            {
                int px = dx - ox, py = dy - oy;
                if (px >= 0 && py >= 0 && px < p.Size && py < p.Size)
                {
                    int i = (py * p.Size + px) * 3;
                    sum += Math.Abs(r - p.Rgb[i]) + Math.Abs(g - p.Rgb[i + 1]) + Math.Abs(b - p.Rgb[i + 2]);
                }
                else sum += Math.Abs(r - bgR) + Math.Abs(g - bgG) + Math.Abs(b - bgB);
            }
            return sum / (3.0 * pixels.Count);
        }
        int jitter = Math.Max(1, (int)Math.Round(2 * s));
        var ranked = rendered
            .Select(p => (p.Id, Difference: (from sx in new[] { -jitter, 0, jitter } from sy in new[] { -jitter, 0, jitter } select Difference(p, sx, sy)).Min()))
            .OrderBy(p => p.Difference).ToList();
        if (ranked.Count == 0) return null;
        double next = ranked.Count > 1 ? ranked[1].Difference : double.MaxValue;
        return ranked[0].Difference <= MaxDifference && next - ranked[0].Difference >= MinLead ? ranked[0].Id : null;

        // A few colours for the cache: the disc's colour varies a little with what's behind the panel.
        static int Quantise(int v) => Math.Min(255, (v + 4) / 8 * 8);
    }

}
