using System.Text.RegularExpressions;

namespace EndHud.Vision.Inn;

/// <summary>What the menu screen's 菜单要求 panel asks of the menu. A number not read is null.</summary>
/// <param name="Rating">The Inn Rating, from "4级客栈要求".</param>
/// <param name="MinTypes">"至少需要3种不同的类型的菜品": dish types at least.</param>
/// <param name="MinDishes">"至少需要6道菜品和3种饮品": dishes at least.</param>
/// <param name="MinDrinks">The same line's drinks at least.</param>
/// <param name="TraitRules">The theme's "至少有2道带有[素食]或[滋补]特性的菜品" rules.</param>
/// <param name="Lines">The panel's lines as read, for the diagnostic log.</param>
public sealed record MenuRequirements(int? Rating, int? MinTypes, int? MinDishes, int? MinDrinks, IReadOnlyList<TraitRule> TraitRules, IReadOnlyList<string> Lines)
{
    /// <summary>How many of its numbers were read.</summary>
    public int Found => (Rating is null ? 0 : 1) + (MinTypes is null ? 0 : 1) + (MinDishes is null ? 0 : 1) + (MinDrinks is null ? 0 : 1) + TraitRules.Count;
}

/// <summary>
/// The menu screen's 菜单要求 panel at the left: the Inn Rating's requirements (types, dishes and
/// drinks at least) and the theme's. Lines are grey once met, dark while not, the traits in orange.
/// Only the wordings seen on test_yuanyuan2's 养身心 (Inn Rating 4) are understood; another line is
/// kept as read and understood as nothing. Measured on the two 4K menu frames.
/// </summary>
public sealed partial class MenuRequirementsReader(InnText text, InnData data)
{
    // At 4K, on the centred 16:9 area.
    private static readonly PixelRect Panel = new(220, 330, 1280, 610);

    /// <summary>The requirements, or null when none of them can be read.</summary>
    public async Task<MenuRequirements?> ReadAsync(Frame frame)
    {
        var layout = new InnLayout(frame);
        MenuRequirements? best = null;
        await foreach (var lines in text.ReadPlacedLinesAttemptsAsync(frame, layout.Centered(Panel), Ink, layout.Scale))
        {
            var read = Parse([.. lines.OrderBy(l => l.Place.Y).Select(l => l.Text.Replace(" ", ""))], data);
            if (best is null || read.Found > best.Found) best = read;
            if (best.Rating is not null && best.MinTypes is not null && best.MinDishes is not null && best.MinDrinks is not null && best.TraitRules.Count > 0) break;
        }
        return best is { Found: > 0 } ? best : null;
    }

    /// <summary>The requirements in the panel's lines (top to bottom).</summary>
    internal static MenuRequirements Parse(IReadOnlyList<string> lines, InnData data)
    {
        int? rating = null, types = null, dishes = null, drinks = null;
        var rules = new List<TraitRule>();
        foreach (string read in lines)
        {
            string line = Normalise(read);
            if (RatingText().Match(line) is { Success: true } r) rating = int.Parse(r.Groups[1].Value);
            if (TypesText().Match(line) is { Success: true } t) types = int.Parse(t.Groups[1].Value);
            if (CountsText().Match(line) is { Success: true } c)
            {
                dishes = int.Parse(c.Groups[1].Value);
                if (c.Groups[2].Success) drinks = int.Parse(c.Groups[2].Value);
            }
            if (TraitRule.Parse(line, data) is { } rule && !rules.Any(k => k.Count == rule.Count && k.AnyOf.SequenceEqual(rule.AnyOf))) rules.Add(rule);
        }
        return new MenuRequirements(rating >= 1 && rating <= KitchenMath.MaxRating ? rating : null, types, dishes, drinks, rules, lines);
    }

    /// <summary>
    /// OCR's slips on this panel, put right: "品" read as two boxes ("口口"), and the start of a line
    /// lost under the yellow tick of a met rule ("少有2道…" for "至少有2道…").
    /// </summary>
    internal static string Normalise(string line)
    {
        line = line.Replace("口口", "品");
        int at = line.IndexOf("少有", StringComparison.Ordinal);
        return at >= 0 && !line.Contains("至少有") ? "至" + line[at..] : line;
    }

    /// <summary>The panel's text: grey or dark, and the light orange of the traits.</summary>
    private static bool Ink(byte r, byte g, byte b)
    {
        int max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        return (max <= 200 && max - min <= 30) || (r >= 200 && r - b >= 60 && g >= 120);
    }

    [GeneratedRegex(@"(\d)级客栈")]
    private static partial Regex RatingText();

    [GeneratedRegex(@"至少需要(\d+)种不同的?类型")]
    private static partial Regex TypesText();

    [GeneratedRegex(@"至少需要(\d+)道菜品(?:和(\d+)种饮品)?")]
    private static partial Regex CountsText();
}
