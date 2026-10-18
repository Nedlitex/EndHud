using System.Text.Json;
using System.Text.Json.Serialization;
using EndHud.Vision.Operators;

namespace EndHud.Vision.Inn;

/// <summary>A dish's kind on the inn's menu; a kitchen's chefs speed up some kinds.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DishType
{
    /// <summary>主食</summary>
    Entree,

    /// <summary>菜肴</summary>
    Side,

    /// <summary>汤羹</summary>
    Soup,

    /// <summary>甜品</summary>
    Dessert,
}

public sealed record InnDishType(DishType Id, string Zh);

/// <summary>A trait a dish (or drink) can have, e.g. "Savory" (鲜).</summary>
/// <param name="Zh">As the game names it; null while not yet seen in the game.</param>
/// <param name="Flavor">One of the five flavors an operator's element favours.</param>
/// <param name="Drink">A drink's trait (themes can prefer them; no dish has one).</param>
public sealed record InnTrait(string Id, string? Zh, bool Flavor, bool Drink);

/// <summary>What an operator's hobby does in the kitchen: speeds up one dish type, or raises the chance of a choice dish (珍馐).</summary>
/// <param name="Id">The hobby as the wiki writes it, e.g. "Sports".</param>
public sealed record InnHobby(string Id, string Zh, DishType? DishType = null, bool Choice = false);

/// <summary>The flavor an operator's element favours, e.g. Heat → Spicy.</summary>
/// <param name="Id">The element as the wiki writes it, e.g. "Heat".</param>
public sealed record InnElement(string Id, string Zh, string Flavor);

/// <summary>How chefs help their kitchen (the game's 干员加成 screen). Bonuses of the same kind add up.</summary>
/// <param name="FlavorFromRating">The element's flavor bonus applies from this Inn Rating on.</param>
/// <param name="NotChefs">Operators that can't be put in a kitchen.</param>
public sealed record InnChefRules(int DishSpeedPercent, int ChoiceChancePercent, int FlavorPercent, int FlavorFromRating,
    IReadOnlyList<InnHobby> Hobbies, IReadOnlyList<InnElement> Elements, IReadOnlyList<string> NotChefs);

/// <summary>How a dish is unlocked: one of an Inn Rating, a theme's star, or something else (said in English).</summary>
public sealed record DishUnlock(int? Rating = null, string? Theme = null, int? Stars = null, string? Other = null);

public sealed record DishIngredient(string En, int Count);

/// <param name="Id">Stable key, e.g. "meat-baozi".</param>
/// <param name="Traits">Trait ids (see <see cref="InnData.Traits"/>), at most two.</param>
public sealed record Dish(string Id, string En, string Zh, DishType Type, int Price, int CookSeconds,
    IReadOnlyList<string> Traits, DishUnlock Unlock, IReadOnlyList<DishIngredient> Ingredients);

/// <param name="Zh">As the game names it; null while not yet seen in the game.</param>
/// <param name="Prefers">Dish types, trait ids, or "Drink".</param>
/// <param name="StarScores">The session score for 1 to 5 stars.</param>
public sealed record InnTheme(string Id, string En, string? Zh, int Rating, IReadOnlyList<string> Prefers, IReadOnlyList<int> StarScores);

/// <summary>What one chef brings to their kitchen.</summary>
/// <param name="DishType">For <see cref="ChefBonusKind.DishSpeed"/>.</param>
/// <param name="Flavor">For <see cref="ChefBonusKind.Flavor"/>: dishes with this trait.</param>
public sealed record ChefBonus(ChefBonusKind Kind, int Percent, DishType? DishType = null, string? Flavor = null);

public enum ChefBonusKind
{
    /// <summary>Dishes of one type cook faster (a hobby).</summary>
    DishSpeed,

    /// <summary>Choice dishes (珍馐) come out more often (a hobby).</summary>
    ChoiceChance,

    /// <summary>Dishes with one flavor cook faster and come out as choice dishes more often (the element).</summary>
    Flavor,
}

/// <summary>
/// The inn of Yuanyuan Manor (缘缘庄, the "Yuanyuan's Blazing Stoves" mode): its dishes, the traits
/// and themes they're judged by, and how an operator's hobbies and element help a kitchen. The wiki
/// has no dish pages yet, so this is kept by hand in the repository (<c>inn/inn-data.json</c>, with
/// its sources in <c>inn/README.md</c>) and travels in the operator package
/// (<see cref="OperatorPackage"/>): built into EndHud and updated from the repository with it. The
/// operators' hobbies and elements come from the wiki with the roster (<see cref="OperatorInfo"/>).
/// </summary>
public sealed record InnData(int Format, IReadOnlyList<InnDishType> DishTypes, IReadOnlyList<InnTrait> Traits,
    InnChefRules ChefBonuses, IReadOnlyList<Dish> Dishes, IReadOnlyList<InnTheme> Themes)
{
    /// <summary>The data format this build reads. A newer one is left unread (an older EndHud carries on without it).</summary>
    public const int CurrentFormat = 1;

    public const string FolderName = "inn";
    public const string FileName = "inn-data.json";

    /// <summary>A theme may prefer drinks in general.</summary>
    public const string Drink = "Drink";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    /// <summary>The file's place inside a reference folder (or package).</summary>
    public static string PathIn(string folder) => Path.Combine(folder, FolderName, FileName);

    /// <summary>The inn data in <paramref name="referenceFolder"/>, or null when there is none or it's in a newer format.</summary>
    /// <exception cref="InvalidDataException">The file is there but isn't inn data.</exception>
    public static InnData? Load(string referenceFolder)
    {
        string path = PathIn(referenceFolder);
        return File.Exists(path) ? Parse(File.ReadAllBytes(path)) : null;
    }

    /// <summary>Null for a newer format.</summary>
    /// <exception cref="InvalidDataException">Not inn data.</exception>
    public static InnData? Parse(byte[] json)
    {
        try
        {
            var format = JsonSerializer.Deserialize<FormatOnly>(json, Json);
            if (format is null || format.Format < 1) throw new InvalidDataException("Not inn data (no format).");
            if (format.Format > CurrentFormat) return null;
            return JsonSerializer.Deserialize<InnData>(json, Json) ?? throw new InvalidDataException("Not inn data.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Not inn data: {ex.Message}", ex);
        }
    }

    private sealed record FormatOnly(int Format);

    public byte[] ToJson() => JsonSerializer.SerializeToUtf8Bytes(this, Json);

    /// <summary>
    /// What's wrong with the data, for the maintainer (empty when it can be shipped): a dish with an
    /// unknown trait or theme, two dishes with one id, a theme preferring something unknown, a rule
    /// naming an unknown trait.
    /// </summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        var traits = Traits.Select(t => t.Id).ToHashSet();
        var themes = Themes.Select(t => t.Id).ToHashSet();
        var types = Enum.GetNames<DishType>().ToHashSet();
        if (Dishes.Count == 0) problems.Add("No dishes.");
        foreach (var group in Dishes.GroupBy(d => d.Id).Where(g => g.Count() > 1)) problems.Add($"Two dishes are {group.Key}.");
        foreach (var dish in Dishes)
        {
            if (string.IsNullOrWhiteSpace(dish.Zh)) problems.Add($"{dish.Id}: no Chinese name.");
            if (dish.Price <= 0 || dish.CookSeconds <= 0) problems.Add($"{dish.Id}: price and cook time must be above 0.");
            foreach (string trait in dish.Traits.Where(t => !traits.Contains(t))) problems.Add($"{dish.Id}: unknown trait {trait}.");
            if (dish.Unlock.Theme is { } theme && !themes.Contains(theme)) problems.Add($"{dish.Id}: unlocked by unknown theme {theme}.");
            if (dish.Unlock is { Rating: null, Theme: null, Other: null }) problems.Add($"{dish.Id}: no unlock.");
        }
        foreach (var theme in Themes)
        {
            foreach (string p in theme.Prefers.Where(p => !traits.Contains(p) && !types.Contains(p) && p != Drink))
                problems.Add($"Theme {theme.Id}: prefers unknown {p}.");
            if (theme.StarScores.Count != 5) problems.Add($"Theme {theme.Id}: needs 5 star scores.");
        }
        foreach (var element in ChefBonuses.Elements.Where(e => Traits.FirstOrDefault(t => t.Id == e.Flavor) is not { Flavor: true }))
            problems.Add($"Element {element.Id}: {element.Flavor} isn't a flavor.");
        foreach (var hobby in ChefBonuses.Hobbies.Where(h => (h.DishType is null) == !h.Choice))
            problems.Add($"Hobby {hobby.Id}: needs either a dish type or choice.");
        return problems;
    }

    /// <summary>Names not yet seen in the game (shown in English until they are).</summary>
    public IReadOnlyList<string> MissingChinese() =>
        [.. Traits.Where(t => t.Zh is null).Select(t => $"trait {t.Id}"), .. Themes.Where(t => t.Zh is null).Select(t => $"theme {t.En}")];

    /// <summary>What <paramref name="op"/> brings to a kitchen; empty for an operator who can't cook. Unknown hobbies (a new one) bring nothing.</summary>
    public IReadOnlyList<ChefBonus> ChefBonusesOf(OperatorInfo op)
    {
        if (ChefBonuses.NotChefs.Contains(op.Id)) return [];
        var bonuses = new List<ChefBonus>();
        foreach (string hobby in op.Hobbies)
        {
            if (ChefBonuses.Hobbies.FirstOrDefault(h => h.Id == hobby) is not { } h) continue;
            bonuses.Add(h.Choice
                ? new ChefBonus(ChefBonusKind.ChoiceChance, ChefBonuses.ChoiceChancePercent)
                : new ChefBonus(ChefBonusKind.DishSpeed, ChefBonuses.DishSpeedPercent, DishType: h.DishType));
        }
        if (ChefBonuses.Elements.FirstOrDefault(e => e.Id == op.Element) is { } element)
            bonuses.Add(new ChefBonus(ChefBonusKind.Flavor, ChefBonuses.FlavorPercent, Flavor: element.Flavor));
        return bonuses;
    }
}
