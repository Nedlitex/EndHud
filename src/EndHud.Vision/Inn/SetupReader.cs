using System.Text.RegularExpressions;
using EndHud.Vision.Operators;

namespace EndHud.Vision.Inn;

/// <summary>A kitchen on the 营业设定 screen.</summary>
/// <param name="Number">1–3 (厨房一…).</param>
/// <param name="ChefSlots">How many chef places it has; null when they can't be told.</param>
/// <param name="Chefs">The chefs placed, left to right: the operator's id, null when empty or unread.</param>
/// <param name="ChefNames">The names as read ("佩丽卡"), null when empty or unread.</param>
public sealed record SetupKitchen(int Number, int? ChefSlots, IReadOnlyList<string?> Chefs, IReadOnlyList<string?> ChefNames)
{
    /// <summary>
    /// How many of its places hold a chef, by the bar under each (dark red with a chef, grey
    /// without), whoever they are; null when the places couldn't be told. 0 means the kitchen is
    /// known to have no chef, and so cannot cook (<see cref="KitchenMath.Cooks"/>).
    /// </summary>
    public int? ChefsPlaced { get; init; }
}

/// <summary>What the setup screens show of the session to come.</summary>
/// <param name="ThemeName">The theme's name as read ("养身心").</param>
/// <param name="Theme">The theme in the inn data with that name; null when none has it.</param>
/// <param name="Objective">主题加分项, as read ("开启2次满堂彩"); null when unread.</param>
/// <param name="MenuRules">菜单要求, one line each, as read.</param>
/// <param name="HotTraits">热门特性: the ids of the traits, dish types and 饮品 whose names were read (<see cref="SetupReader.HotIds"/>).</param>
/// <param name="Kitchens">The kitchens and their chefs (营业设定); null on the theme list (主题设定), which shows none.</param>
public sealed record InnSetup(string ThemeName, InnTheme? Theme, string? Objective, IReadOnlyList<string> MenuRules,
    IReadOnlyList<string> HotTraits, IReadOnlyList<SetupKitchen>? Kitchens)
{
    /// <summary>
    /// The theme's text under its name, as read ("大海与陆地交汇…珍馐赏钱加成提升，出售珍馐菜品可获得更高收益。");
    /// null when unread. What it says of choice dishes (珍馐) is valued by the planner (<see cref="InnObjective"/>).
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Si's 品质把控 assist on 营业设定, as its line states it ("所有厨房的珍馐产出概率提升 5%"): the
    /// percent every kitchen's choice-dish chance rises; null when the line wasn't read (no assist, the
    /// theme list, or covered).
    /// </summary>
    public int? SiChoicePercent { get; init; }
}

/// <summary>
/// The setup screens: the theme list (//主题设定, the chosen theme's details on the right) and
/// 营业设定 (the theme's details on the left, the kitchens and their chefs on the right). The details
/// are read as OCR lines and taken apart by their headings (主题加分项, 菜单要求, 热门信息, 热门特性);
/// the chefs by their "大厨…" names under each kitchen. The text is read a few ways and the reading
/// that finds the most of the screen's fixed parts (headings, a known theme, known traits) is kept.
/// Measured on 4K frames of one theme (养身心).
/// </summary>
public sealed partial class SetupReader(InnText text, InnData data, IReadOnlyList<OperatorInfo> roster)
{
    // At 4K, on the centred 16:9 area: the theme's details on each screen, and 营业设定's kitchens.
    private static readonly PixelRect DetailsOnSetup = new(220, 280, 1260, 980), DetailsOnThemeList = new(1590, 280, 2040, 920);
    private static readonly PixelRect KitchenArea = new(1600, 560, 2020, 540);
    // At 4K on 营业设定: Si's assist line under its name ("品质把控 Ⅱ" / "所有厨房的珍馐产出概率提升 5%").
    private static readonly PixelRect AssistArea = new(2900, 330, 720, 170);
    // A kitchen's two chef places, from its title's left edge, and the rows of their bars:
    // a beige strip over a bar, dark red when a chef is there, grey when not.
    private static readonly double[] SlotOffsets = [180, 430];
    private const double StripY = 979, BarY = 988;
    private const int KitchenWidth = 680;

    private sealed record Details(string ThemeName, InnTheme? Theme, string? Description, string? Objective, IReadOnlyList<string> MenuRules,
        IReadOnlyList<string> HotTraits, int Headings, PixelRect? TraitsRow)
    {
        /// <summary>How much of the screen's fixed parts this reading found.</summary>
        public int Score => Headings + HotTraits.Count + (Theme is null ? 0 : 1) + (Objective is null ? 0 : 1);
    }

    /// <summary>The setup, or null when it isn't a setup screen or no theme name can be read.</summary>
    public async Task<InnSetup?> ReadAsync(Frame frame, InnScreenKind screen)
    {
        if (screen is not (InnScreenKind.Setup or InnScreenKind.ThemeList)) return null;
        var layout = new InnLayout(frame);
        // Two layouts: on 营业设定 the details sit left and the kitchens right; on the theme list the details right.
        var kitchens = await ReadKitchensAsync(frame, layout);
        var area = layout.Centered(kitchens is null ? DetailsOnThemeList : DetailsOnSetup);
        Details? best = null;
        await foreach (var lines in text.ReadPlacedLinesAttemptsAsync(frame, area, Ink, layout.Scale))
        {
            if (Parse(lines) is not { } details) continue;
            if (best is null || details.Score > best.Score) best = details;
            // Every heading, the theme, its objective and two traits: nothing more to find.
            if (best.Score >= 7) break;
        }
        if (best is null) return null;

        // The trait chips' grey letters are light enough to be lost in a small window: read the chips
        // on their own, with a lighter bar for ink, at a few sizes, and keep the reading naming most.
        var traits = best.HotTraits;
        if (best.TraitsRow is { } row)
        {
            var chips = new PixelRect(row.Right, row.Y - row.Height / 2, area.Right - row.Right, row.Height * 2);
            await foreach (string attempt in text.ReadAttemptsAsync(frame, chips, ChipInk, layout.Scale))
                if (HotIds(attempt, data) is var ids && ids.Count > traits.Count) traits = ids;
        }
        return new InnSetup(best.ThemeName, best.Theme, best.Objective, best.MenuRules, traits, kitchens)
        {
            Description = best.Description,
            SiChoicePercent = kitchens is null ? null : await ReadAssistAsync(frame, layout),
        };
    }

    /// <summary>Si's 品质把控 assist: the percent its line states; null when no reading finds the line.</summary>
    private async Task<int?> ReadAssistAsync(Frame frame, InnLayout layout)
    {
        await foreach (string attempt in text.ReadAttemptsAsync(frame, layout.Centered(AssistArea), Ink, layout.Scale))
            if (AssistPercent(attempt) is { } percent) return percent;
        return null;
    }

    /// <summary>"所有厨房的珍馐产出概率提升 5%" → 5; null for any other line.</summary>
    internal static int? AssistPercent(string line)
    {
        var match = AssistText().Match(line.Replace(" ", ""));
        return match.Success && int.Parse(match.Groups[1].Value) is var percent and > 0 and <= 100 ? percent : null;
    }

    /// <summary>A trait chip's letters: neutral grey on its lighter grey.</summary>
    private static bool ChipInk(byte r, byte g, byte b) => Math.Max(r, Math.Max(g, b)) <= 165 && Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)) <= 24;

    private Details? Parse(IReadOnlyList<(string Text, PixelRect Place)> read)
    {
        var lines = read.OrderBy(l => l.Place.Y).ToList();
        if (lines.Count == 0) return null;
        // The name: among the first lines, the one in the largest letters.
        var name = lines.Take(3).MaxBy(l => l.Place.Height);
        string themeName = Letters(name.Text);
        if (themeName.Length == 0) return null;
        var theme = data.Themes.FirstOrDefault(t => t.Zh == themeName);

        int Heading(params string[] words) => lines.FindIndex(l => words.Any(l.Text.Contains));
        int objective = Heading("加分项"), rules = Heading("菜单要求"), hot = lines.FindIndex(l => l.Text.Contains("热门信息") || HotLabel().IsMatch(l.Text));
        // The trend bar (流行趋势, with a chip and a refresh countdown at its right: test_yuanyuan8's gourmand theme) closes the
        // rules above it; without it the countdown and the bar's title were read as the end of a rule ("至少有1道@13小时39分钟").
        int trend = Heading("流行趋势");
        // A section's lines: after its heading, above the next heading's row (a chip beside the next
        // heading, "赏钱", stands a little higher than it).
        List<string> Section(int from, int to)
        {
            if (from < 0) return [];
            int limit = to > from ? lines[to].Place.Y - lines[to].Place.Height / 2 : int.MaxValue;
            return [.. lines.Skip(from + 1).TakeWhile((l, i) => from + 1 + i != to && l.Place.Y < limit).Select(l => l.Text).Where(t => t.Length > 0)];
        }
        int Next(int after) => new[] { objective, rules, hot, trend }.Where(i => i > after).DefaultIfEmpty(-1).Min();

        var (traits, traitsRow) = HotRow(lines, data);

        var objectiveLines = Section(objective, Next(objective));
        int headings = new[] { objective, rules, hot }.Count(i => i >= 0);
        // The theme's text: the lines under its name, above the first heading.
        int nameAt = lines.IndexOf(name), firstHeading = new[] { objective, rules, hot, trend }.Where(i => i > nameAt).DefaultIfEmpty(-1).Min();
        string description = firstHeading > nameAt ? string.Concat(lines.Skip(nameAt + 1).Take(firstHeading - nameAt - 1).Select(l => l.Text)) : "";
        return new Details(themeName, theme, description.Length > 0 ? description : null, objectiveLines.Count > 0 ? string.Concat(objectiveLines) : null,
            Section(rules, Next(rules)), traits, headings, traitsRow);
    }

    /// <summary>The traits named in a row of chips ("素食 滋补"), in the order they stand.</summary>
    /// <summary>
    /// The hot chips: the ids named to the right of the 热门 row's label, on its row, and where the
    /// label stands; nothing and null when there's no such row. The label is "热门特性" (养身心) or
    /// "热门类型" (舌上清流, the owner's screenshot): both seen in the game.
    /// </summary>
    internal static (List<string> Ids, PixelRect? Row) HotRow(IReadOnlyList<(string Text, PixelRect Place)> lines, InnData data)
    {
        var row = lines.FirstOrDefault(l => HotLabel().IsMatch(l.Text));
        if (row.Text is null) return ([], null);
        var chips = lines.Where(l => l.Place.X > row.Place.Right && Math.Abs(l.Place.Y - row.Place.Y) <= row.Place.Height).Select(l => l.Text);
        return (HotIds(string.Concat(chips), data), row.Place);
    }

    /// <summary>
    /// The 热门 ids named in a row of chips, in the order they stand: traits (<see cref="InnTrait.Id"/>),
    /// dish types (by name, "Soup") and 饮品 (<see cref="InnData.Drink"/>), as a theme's preferences
    /// name them (<see cref="InnPlanner.HotMatches(IReadOnlyCollection{string}, Dish)"/>).
    /// </summary>
    internal static List<string> HotIds(string row, InnData data)
    {
        var letters = Letters(row).ToCharArray();
        var named = data.Traits.Where(t => t.Zh is { Length: > 0 }).Select(t => (Zh: t.Zh!, t.Id))
            .Concat(data.DishTypes.Select(t => (t.Zh, Id: t.Id.ToString())))
            .Append((Zh: DrinkName, Id: InnData.Drink));
        var found = new List<(int At, string Id)>();
        // Longest names first, each taking its letters: 甜品 (a type) is not also 甜 (a trait).
        foreach (var (zh, id) in named.OrderByDescending(n => n.Zh.Length))
        {
            int at = new string(letters).IndexOf(zh, StringComparison.Ordinal);
            if (at < 0) continue;
            found.Add((at, id));
            Array.Fill(letters, ' ', at, zh.Length);
        }
        return [.. found.OrderBy(f => f.At).Select(f => f.Id)];
    }

    /// <summary>The 热门 chip for any drink.</summary>
    private const string DrinkName = "饮品";

    /// <summary>The kitchens of 营业设定, or null when there are none (the theme list).</summary>
    private async Task<IReadOnlyList<SetupKitchen>?> ReadKitchensAsync(Frame frame, InnLayout layout)
    {
        IReadOnlyList<(string Text, PixelRect Place)>? lines = null;
        int found = -1;
        // The reading with the most kitchen titles and chef names.
        await foreach (var attempt in text.ReadPlacedLinesAttemptsAsync(frame, layout.Centered(KitchenArea), Ink, layout.Scale))
        {
            int count = attempt.Count(l => KitchenNumber(l.Text) > 0) + attempt.Count(l => ChefName(l.Text) is { } n && Closest(n) is not null);
            if (count > found) (lines, found) = (attempt, count);
        }
        var titles = (lines ?? []).Select(l => (Number: KitchenNumber(l.Text), l.Place)).Where(t => t.Number > 0)
            .GroupBy(t => t.Number).Select(g => g.First()).OrderBy(t => t.Number).ToList();
        if (titles.Count == 0) return null;
        double originX = layout.Centered(new PixelRect(0, 0, 1, 1)).X;
        var kitchens = new List<SetupKitchen>();
        foreach (var (number, title) in titles)
        {
            double left = (title.X - originX) / layout.Scale;
            var places = SlotOffsets.Select(offset => left + offset).ToList();
            var slotPlaces = places.Where(x => IsSlot(frame, layout, x)).ToList();
            int slots = slotPlaces.Count;
            var names = new string?[places.Count];
            foreach (var line in lines!.Where(l => ChefName(l.Text) is not null && l.Place.X >= title.X && l.Place.X < title.X + KitchenWidth * layout.Scale))
            {
                // The place whose middle the name is centred under.
                double middle = (line.Place.X + line.Place.Width / 2.0 - originX) / layout.Scale;
                int slot = places.Select((p, i) => (i, Distance: Math.Abs(p - middle))).MinBy(p => p.Distance).i;
                names[slot] = ChefName(line.Text);
            }
            var ids = names.Select(n => n is null ? null : Closest(n)).ToArray();
            kitchens.Add(new SetupKitchen(number, slots > 0 ? slots : null, ids,
                [.. names.Select((n, i) => ids[i] is { } id ? roster.First(o => o.Id == id).NameZh : n)])
            {
                ChefsPlaced = slots > 0 ? slotPlaces.Count(x => HasChef(frame, layout, x)) : null,
            });
        }
        return kitchens;
    }

    private string? Closest(string name) => InnNames.Closest(name, roster.Select(o => (o.Id, o.NameZh)));

    /// <summary>"大厨佩丽卡" → "佩丽卡"; null for a line that isn't a chef's name.</summary>
    private static string? ChefName(string line)
    {
        string read = line.Replace(" ", "");
        int at = read.IndexOf("大厨", StringComparison.Ordinal);
        return at >= 0 && read.Length > at + 2 ? read[(at + 2)..] : null;
    }

    private static int KitchenNumber(string title) =>
        title.Contains("厨房一") ? 1 : title.Contains("厨房二") ? 2 : title.Contains("厨房三") ? 3 : 0;

    /// <summary>A chef place: the beige strip and, under it, the dark bar (red with a chef, grey without).</summary>
    private static bool IsSlot(Frame frame, InnLayout layout, double x)
    {
        var strip = layout.Centered(new PixelRect((int)Math.Round(x), (int)StripY, 1, 1));
        var bar = layout.Centered(new PixelRect((int)Math.Round(x), (int)BarY, 1, 1));
        if (strip.X < 0 || strip.X >= frame.Width || bar.Y >= frame.Height) return false;
        var (sr, sg, sb) = frame.GetPixel(strip.X, strip.Y);
        var (br, bg, bb) = frame.GetPixel(bar.X, bar.Y);
        bool beige = sr is >= 170 and <= 215 && sg is >= 160 and <= 205 && sb is >= 145 and <= 195 && sr - sb is >= 8 and <= 40;
        bool dark = Math.Max(br, Math.Max(bg, bb)) <= 120;
        return beige && dark;
    }

    /// <summary>
    /// A chef place's bar is dark red with a chef in it, dark grey without. Measured at 4K on
    /// setup-chefs-placed: red ≈ (85, 1, 0), grey ≈ (91, 91, 91).
    /// </summary>
    private static bool HasChef(Frame frame, InnLayout layout, double x)
    {
        var bar = layout.Centered(new PixelRect((int)Math.Round(x), (int)BarY, 1, 1));
        if (bar.X < 0 || bar.X >= frame.Width || bar.Y < 0 || bar.Y >= frame.Height) return false;
        var (r, g, b) = frame.GetPixel(bar.X, bar.Y);
        return r - g >= 40 && r - b >= 40;
    }

    /// <summary>The screens' text: dark grey, and the orange of trait names in brackets.</summary>
    private static bool Ink(byte r, byte g, byte b) =>
        Math.Max(r, Math.Max(g, b)) <= 120 || (r >= 200 && g >= 70 && g <= 170 && b <= 90);

    private static string Letters(string s) => new([.. s.Where(char.IsLetter)]);

    /// <summary>Si's quality assist: …珍馐产出概率提升 N% (珍馐 itself is often misread; OCR gives the full-width ％).</summary>
    [GeneratedRegex(@"概率提升(\d+)[%％]")]
    private static partial Regex AssistText();

    /// <summary>The hot row's label: 热门 then 类型 or 特性.</summary>
    [GeneratedRegex("热门.*(类型|特性)")]
    private static partial Regex HotLabel();
}
