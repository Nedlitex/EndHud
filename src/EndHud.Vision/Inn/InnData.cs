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

/// <param name="Id">An ingredient id (see <see cref="InnData.Ingredients"/>), e.g. "Meat".</param>
/// <param name="Quality">The higher grade ("Meat [A]", 优质肉块), from the ranch.</param>
public sealed record DishIngredient(string Id, int Count, bool Quality = false);

/// <param name="Id">English, e.g. "Fin Fillet".</param>
/// <param name="Zh">As gourmands' requests name it (鳞肉); null while not yet known.</param>
public sealed record InnIngredient(string Id, string? Zh);

/// <summary>A drink at the bar: no kitchen, no cooking time.</summary>
/// <param name="Traits">Drink trait ids (Tea, Kohlic, Milky, Fruity), at most one so far.</param>
/// <param name="GameId">The game's item id (the wiki's file name), when it has one.</param>
/// <param name="Unlimited">Never runs out (potable water).</param>
public sealed record InnDrink(string Id, string En, string Zh, int Price, IReadOnlyList<string> Traits, DishUnlock Unlock,
    string? GameId = null, bool Unlimited = false);

/// <summary>
/// A gourmand (老饕): a recognisable guest who asks for a kind of dish or drink rather than a dish.
/// Each has a fixed list of possible requests; which gourmands come depends on the theme and what
/// the kitchens can make (<paramref name="Likes"/>).
/// </summary>
/// <param name="Pool">"normal", or "feast" for the 食有老饕 mode.</param>
/// <param name="Look">How they look (少年警员), to tell who's come.</param>
/// <param name="Likes">What they're drawn by, as a condition (see <see cref="GourmandCondition"/>).</param>
public sealed record Gourmand(string Pool, int Number, string Look, string Likes, IReadOnlyList<GourmandRequest> Requests);

/// <summary>A request that a line read off a speech bubble may be (<see cref="InnData.MatchSaid"/>).</summary>
/// <param name="Score">The share of the line's two-character pieces the request has, each weighed by its rarity, 0 to 1.</param>
public sealed record GourmandRequestMatch(Gourmand Gourmand, GourmandRequest Request, double Score)
{
    /// <summary>
    /// At least this much of the line in the request. A cut-off line ("什么，但不要稻") is all in its
    /// request; an OCR slip ("亻日" for "但") costs a piece or two; the inn's other talk (Si's
    /// "不用谢，菜已经帮你上齐了") shares hardly any.
    /// </summary>
    public const double MinScore = 0.6;

    /// <summary>Fewer pieces than this (a line of two or three characters) say too little.</summary>
    public const int MinPieces = 3;

    /// <summary>The text's two-character pieces, punctuation and spaces left out.</summary>
    internal static HashSet<string> Pieces(string text)
    {
        var letters = text.Where(char.IsLetterOrDigit).ToArray();
        return [.. Enumerable.Range(0, Math.Max(0, letters.Length - 1)).Select(i => new string(letters, i, 2))];
    }
}

/// <param name="Text">What they say, as the speech bubble shows it (typed by players: wording may be a little off).</param>
/// <param name="When">What meets it, as a condition (see <see cref="GourmandCondition"/>).</param>
public sealed record GourmandRequest(string Text, string When);

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
/// has no dish pages yet, so this is kept by hand in the repository (<c>inn/inn-data.json</c> and the
/// dish and drink pictures in <c>inn/pictures.zip</c>, with their sources in <c>inn/README.md</c>) and travels in the operator package
/// (<see cref="OperatorPackage"/>): built into EndHud and updated from the repository with it. The
/// operators' hobbies and elements come from the wiki with the roster (<see cref="OperatorInfo"/>).
/// </summary>
public sealed record InnData(int Format, IReadOnlyList<InnDishType> DishTypes, IReadOnlyList<InnTrait> Traits,
    IReadOnlyList<InnIngredient> Ingredients, InnChefRules ChefBonuses, IReadOnlyList<Dish> Dishes, IReadOnlyList<InnDrink> Drinks,
    IReadOnlyList<InnTheme> Themes, IReadOnlyList<Gourmand> Gourmands)
{
    /// <summary>The data format this build reads. A newer one is left unread (an older EndHud carries on without it).</summary>
    public const int CurrentFormat = 1;

    public const string FolderName = "inn";
    public const string FileName = "inn-data.json";

    /// <summary>A theme may prefer drinks in general.</summary>
    public const string Drink = "Drink";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    /// <summary>The dishes' and drinks' pictures in a package: <c>pictures/&lt;dish or drink id&gt;.png</c>, 128×128.</summary>
    public const string PictureFolderName = "pictures";

    /// <summary>In the repository the pictures are kept as one zip beside the file (<c>&lt;dish or drink id&gt;.png</c> each).</summary>
    public const string PicturesZipName = "pictures.zip";

    /// <summary>The file's place inside a reference folder (or package).</summary>
    public static string PathIn(string folder) => Path.Combine(folder, FolderName, FileName);

    /// <summary>Where a dish's or drink's picture is inside a reference folder (or package). One can have none yet.</summary>
    public static string PicturePath(string folder, string id) => Path.Combine(folder, FolderName, PictureFolderName, id + ".png");

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
            var data = JsonSerializer.Deserialize<InnData>(json, Json) ?? throw new InvalidDataException("Not inn data.");
            // A list the file doesn't have (an earlier copy, before drinks or gourmands) is empty.
            return data with
            {
                DishTypes = data.DishTypes ?? [], Traits = data.Traits ?? [], Ingredients = data.Ingredients ?? [],
                Dishes = data.Dishes ?? [], Drinks = data.Drinks ?? [], Themes = data.Themes ?? [], Gourmands = data.Gourmands ?? [],
            };
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
            foreach (var ingredient in dish.Ingredients.Where(i => !Ingredients.Any(k => k.Id == i.Id)))
                problems.Add($"{dish.Id}: unknown ingredient {ingredient.Id}.");
        }
        foreach (var group in Drinks.GroupBy(d => d.Id).Where(g => g.Count() > 1)) problems.Add($"Two drinks are {group.Key}.");
        // One picture per id: a drink can't share one with a dish.
        foreach (var drink in Drinks.Where(d => Dishes.Any(x => x.Id == d.Id))) problems.Add($"{drink.Id} is a dish and a drink.");
        // One picture per id: a drink can't share one with a dish.
        foreach (var drink in Drinks.Where(d => Dishes.Any(x => x.Id == d.Id))) problems.Add($"{drink.Id} is a dish and a drink.");
        foreach (var drink in Drinks)
        {
            if (string.IsNullOrWhiteSpace(drink.Zh)) problems.Add($"{drink.Id}: no Chinese name.");
            foreach (string trait in drink.Traits.Where(t => Traits.FirstOrDefault(k => k.Id == t) is not { Drink: true }))
                problems.Add($"{drink.Id}: {trait} isn't a drink trait.");
        }
        foreach (var gourmand in Gourmands)
        {
            foreach (string condition in gourmand.Requests.Select(r => r.When).Prepend(gourmand.Likes))
            {
                try { GourmandCondition.Parse(condition, this); }
                catch (FormatException ex) { problems.Add($"Gourmand {gourmand.Pool} {gourmand.Number}: {ex.Message}"); }
            }
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
        [.. Traits.Where(t => t.Zh is null).Select(t => $"trait {t.Id}"), .. Themes.Where(t => t.Zh is null).Select(t => $"theme {t.En}"),
         .. Ingredients.Where(i => i.Zh is null).Select(i => $"ingredient {i.Id}")];

    /// <summary>
    /// The gourmand requests closest to <paramref name="said"/> (a line read off a speech bubble),
    /// best first, those at least <see cref="GourmandRequestMatch.MinScore"/> only. Neither the
    /// table's sentences (typed by players) nor OCR are exact, and a bubble wraps onto lines that are
    /// read one at a time, so it's matched by its words, not its letters: the line's two-character
    /// pieces (随便, 便上, 上点…) that the request has, each counted by how rare it is among all
    /// requests. A piece many requests share (随便, 不要) says little; one only this request has
    /// (要稻 of 稻谷) says most; a piece no request has (an OCR slip) counts against.
    /// </summary>
    public IReadOnlyList<GourmandRequestMatch> MatchSaid(string said)
    {
        var pieces = GourmandRequestMatch.Pieces(said);
        if (pieces.Count < GourmandRequestMatch.MinPieces) return [];
        var requests = Gourmands.SelectMany(g => g.Requests.Select(r => (Gourmand: g, Request: r, Pieces: GourmandRequestMatch.Pieces(r.Text)))).ToList();
        double Weight(string piece) => Math.Log(1 + requests.Count / (double)Math.Max(1, requests.Count(r => r.Pieces.Contains(piece))));
        double total = pieces.Sum(Weight);
        return [.. requests
            .Select(r => new GourmandRequestMatch(r.Gourmand, r.Request, pieces.Where(r.Pieces.Contains).Sum(Weight) / total))
            .Where(m => m.Score >= GourmandRequestMatch.MinScore)
            .OrderByDescending(m => m.Score)];
    }

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
