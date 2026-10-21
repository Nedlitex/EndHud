using System.Text.RegularExpressions;

namespace EndHud.Vision.Inn;

/// <summary>Which list the menu screen shows (its tabs).</summary>
public enum MenuTab
{
    /// <summary>全部菜品</summary>
    AllDishes,

    /// <summary>主食</summary>
    Entrees,

    /// <summary>菜肴</summary>
    Sides,

    /// <summary>甜品</summary>
    Desserts,

    /// <summary>汤羹</summary>
    Soups,

    /// <summary>饮品</summary>
    Drinks,
}

/// <summary>A dish or drink card on the menu screen.</summary>
/// <param name="Id">The dish's or drink's id; null when the name read matches none.</param>
/// <param name="Name">The name (the data's, when matched; else as read).</param>
/// <param name="OnMenu">Chosen for the menu (the card's red frame).</param>
/// <param name="Unlimited">The stock is ∞ (可烹饪∞, 库存∞).</param>
/// <param name="Stock">The stock as a number, when one is shown and read.</param>
public sealed record MenuCard(string? Id, string Name, bool OnMenu, bool Unlimited, int? Stock);

/// <summary>What the menu screen (菜单设定) shows.</summary>
/// <param name="Tab">The list shown; null when no tab reads as chosen.</param>
/// <param name="Dishes">The dishes chosen and allowed ("2/8"); null when unread.</param>
/// <param name="Drinks">The drinks chosen and allowed ("0/8"); null when unread.</param>
/// <param name="Cards">The cards in view, by rows, left then right.</param>
public sealed record InnMenu(MenuTab? Tab, (int Chosen, int Allowed)? Dishes, (int Chosen, int Allowed)? Drinks, IReadOnlyList<MenuCard> Cards);

/// <summary>
/// The menu screen (菜单设定): the list's tab, the counters of chosen dishes and drinks at the
/// bottom, and the cards in view: each found by its name (OCR, matched to the inn's dishes and
/// drinks), with its red frame when it's on the menu and its stock (∞ told by its shape). The list
/// scrolls, so the cards are placed by where their names are read, not by fixed rows. Measured on
/// two 4K frames (the dishes and the drinks tab).
/// </summary>
public sealed partial class MenuReader(InnText text, InnData data)
{
    // At 4K, on the centred 16:9 area.
    private static readonly PixelRect List = new(1640, 390, 1900, 1240);
    private static readonly (int Left, int Right)[] Tabs = [(1637, 1920), (1975, 2175), (2262, 2462), (2550, 2750), (2837, 3037), (3287, 3600)];
    private const int TabRow = 262;
    // The bottom bar: the dishes' counter, the dishes' places, the drinks' counter (which moves left
    // when the dishes' places fold into a stack).
    // The counters' text rows, between the grey bands above and below their boxes.
    private static readonly PixelRect BottomBar = new(480, 1966, 2400, 54);
    private const int DishCounterEnd = 700;
    // From a card's name: its left edge, its stock label's row and the stock's place right of the label.
    private const int NameToLeft = 330, NameToFrameY = 170, NameToStockY = 170, StockWidth = 160;

    /// <summary>The menu screen, or null when no card or counter can be read.</summary>
    public async Task<InnMenu?> ReadAsync(Frame frame)
    {
        var layout = new InnLayout(frame);
        double s = layout.Scale;
        var origin = layout.Centered(new PixelRect(0, 0, 1, 1));

        MenuTab? tab = null;
        for (int i = 0; i < Tabs.Length; i++)
        {
            var row = layout.Centered(new PixelRect(Tabs[i].Left, TabRow, Tabs[i].Right - Tabs[i].Left, 1)).ClampTo(frame.Width, frame.Height);
            int black = 0;
            for (int x = row.X; x < row.Right; x++)
                if (frame.GetPixel(x, row.Y) is var (r, g, b) && Math.Max(r, Math.Max(g, b)) <= 20) black++;
            if (row.Width > 0 && black >= row.Width * 0.4) tab = (MenuTab)i;
        }

        var names = data.Dishes.Select(d => (d.Id, d.Zh)).Concat(data.Drinks.Select(d => (d.Id, d.Zh))).ToList();
        IReadOnlyList<(string Text, PixelRect Place)> best = [];
        int found = -1;
        // The reading naming the most cards.
        await foreach (var attempt in text.ReadPlacedLinesAttemptsAsync(frame, layout.Centered(List), InnText.Dark, s))
        {
            int count = attempt.Count(l => InnNames.Closest(l.Text, names) is not null);
            if (count > found) (best, found) = (attempt, count);
            if (found >= 8) break;
        }

        var cards = new List<(PixelRect Place, MenuCard Card)>();
        foreach (var (line, place) in best)
        {
            if (InnNames.Closest(line, names) is not { } id) continue;
            // Card names stand in large letters; the traits' and types' short words are smaller.
            if (place.Height < 34 * s) continue;
            double nameX = (place.X - origin.X) / s, nameY = (place.Y - origin.Y) / s;
            bool onMenu = IsRedFrame(frame, layout, nameX - NameToLeft, nameY + NameToFrameY);
            var (unlimited, stock) = ReadStock(frame, layout, best, place);
            string name = names.First(n => n.Id == id).Zh;
            cards.Add((place, new MenuCard(id, name, onMenu, unlimited, stock)));
        }
        var (dishes, drinks) = Counters(frame, layout);
        if (cards.Count == 0 && dishes is null && drinks is null) return null;
        // By rows (names on one row within half a name's height), left then right.
        var ordered = cards.OrderBy(c => c.Place.Y / Math.Max(1, (int)(60 * s))).ThenBy(c => c.Place.X).Select(c => c.Card).ToList();
        return new InnMenu(tab, dishes, drinks, ordered);
    }

    /// <summary>The card's frame left of it is red when the card is on the menu.</summary>
    private static bool IsRedFrame(Frame frame, InnLayout layout, double left, double y)
    {
        int red = 0;
        for (int dx = -10; dx <= 0; dx += 2)
        {
            var p = layout.Centered(new PixelRect((int)Math.Round(left + dx), (int)Math.Round(y), 1, 1));
            if (p.X < 0 || p.Y < 0 || p.X >= frame.Width || p.Y >= frame.Height) continue;
            var (r, g, b) = frame.GetPixel(p.X, p.Y);
            if (r >= 100 && g <= 50 && b <= 50) red++;
        }
        return red >= 2;
    }

    /// <summary>The stock right of the card's 可烹饪 / 库存 label: ∞ (a glyph much wider than tall), a number, or unread.</summary>
    private static (bool Unlimited, int? Stock) ReadStock(Frame frame, InnLayout layout, IReadOnlyList<(string Text, PixelRect Place)> lines, PixelRect name)
    {
        double s = layout.Scale;
        // The card's label: the line reading 可烹饪 or 库存 under the card's left part.
        var label = lines.Where(l => (l.Text.Contains("烹饪") || l.Text.Contains("库存"))
                                     && l.Place.X < name.X && l.Place.X > name.X - NameToLeft * s
                                     && Math.Abs(l.Place.Y - (name.Y + NameToStockY * s)) <= 40 * s)
            .Select(l => (PixelRect?)l.Place).FirstOrDefault();
        if (label is not { } at) return (false, null);
        // The label's own characters end where its letters end; the value follows (OCR may fold "∞" into it as "oo").
        int labelChars = lines.First(l => l.Place == at).Text.Count(c => c is '可' or '烹' or '饪' or '库' or '存');
        int valueX = at.X + (int)Math.Round(labelChars * at.Height * 1.05);
        var region = new PixelRect(valueX, at.Y - at.Height / 3, (int)(StockWidth * s), at.Height * 5 / 3);
        // ∞ is short (half a character's height) and twice as wide as tall: the last blob on the line.
        var blobs = InnDigits.Glyphs(frame, region, InnText.Dark, s, minHeightShare: 0, split: false);
        if (blobs.Count > 0 && blobs[^1] is var last && last.Width >= last.Height * 1.5 && last.Height <= at.Height * 0.7) return (true, null);
        string? digits = InnDigits.Read(frame, region, InnText.Dark, s);
        return digits is { Length: >= 1 and <= 4 } && digits.All(char.IsAsciiDigit) ? (false, int.Parse(digits)) : (false, null);
    }

    /// <summary>
    /// The counters on the bottom bar: the dishes' at its left end, the drinks' further along (it
    /// moves left when the dishes' places fold into a stack). Each is a digit, a slash and a digit
    /// standing close together on the bar, read by their glyphs; the dishes' prices on the bar have
    /// no slash. Null for a counter not found.
    /// </summary>
    private static ((int Chosen, int Allowed)? Dishes, (int Chosen, int Allowed)? Drinks) Counters(Frame frame, InnLayout layout)
    {
        var bar = layout.Centered(BottomBar);
        var glyphs = InnDigits.Glyphs(frame, bar, CounterInk, layout.Scale, oneLine: false);
        var chars = glyphs.Select(InnDigits.Classify).ToList();
        (int, int)? dishes = null, drinks = null;
        double dishesEnd = layout.Centered(new PixelRect(DishCounterEnd, 0, 1, 1)).X - bar.X;
        for (int i = 0; i + 2 < glyphs.Count; i++)
        {
            if (chars[i] is not { } a || !char.IsAsciiDigit(a) || chars[i + 1] != '/' || chars[i + 2] is not { } b || !char.IsAsciiDigit(b)) continue;
            var (first, slash, last) = (glyphs[i], glyphs[i + 1], glyphs[i + 2]);
            int height = slash.Height;
            bool close = slash.Left - first.Right <= height * 0.6 && last.Left - slash.Right <= height * 0.6;
            bool alike = new[] { first.Height, last.Height }.All(h => Math.Abs(h - height) <= height * 0.3);
            if (!close || !alike || a - '0' > b - '0') continue;
            var counter = (a - '0', b - '0');
            if (first.Left < dishesEnd) dishes ??= counter;
            else drinks ??= counter;
        }
        return (dishes, drinks);
    }

    /// <summary>The counters' light letters on their black or red boxes: white, light grey, or pink when dimmed on red.</summary>
    private static bool CounterInk(byte r, byte g, byte b) => Math.Min(g, b) >= 130 && r >= 170 && r - Math.Min(g, b) <= 70;

    [GeneratedRegex(@"^(\d)/(\d)$")]
    private static partial Regex CounterText();
}
