using EndHud.Vision.Operators;

namespace EndHud.Vision.Inn;

/// <summary>One of the inn's kitchens and the chefs put in it (none, one or two).</summary>
/// <param name="Number">1 to <see cref="KitchenMath.MaxKitchens"/>, as the game numbers them.</param>
public sealed record Kitchen(int Number, IReadOnlyList<OperatorInfo> Chefs);

/// <summary>
/// How long a dish takes in a kitchen and how often it comes out as a choice dish (珍馐), from the
/// kitchen's chefs (<see cref="InnData.ChefBonusesOf"/>). Checked against the game's 派单 panel:
/// 赛希 and 伊冯 (dessert +30 %, 鲜 +10 %) cook 金丝小贝 35 → 27 s and 白瓜肉丸汤 40 → 37 s.
/// </summary>
public static class KitchenMath
{
    /// <summary>The inn has at most three kitchens (owner).</summary>
    public const int MaxKitchens = 3;

    /// <summary>Each kitchen has at most two chef slots (owner; the setup screen).</summary>
    public const int MaxChefs = 2;

    /// <summary>
    /// A kitchen's choice-dish chance before any chef, by Inn Rating 1 to 4 (the plan's evidence:
    /// 2/4/6/8 %; at Rating 4 the panel's 18 % is 8 + a chef's 5 + Si's 5).
    /// </summary>
    private static readonly int[] ChoiceBaseByRating = [2, 4, 6, 8];

    /// <summary>The highest Inn Rating whose base choice chance is known.</summary>
    public static int MaxRating => ChoiceBaseByRating.Length;

    /// <summary>
    /// The kitchen's speed bonus for <paramref name="dish"/>, in percent: its type's bonuses and,
    /// from <see cref="InnChefRules.FlavorFromRating"/> on, the bonuses of every flavor it has,
    /// all added up (the game's kitchen screen adds bonuses of the same kind).
    /// </summary>
    public static int SpeedPercent(InnData inn, Kitchen kitchen, Dish dish, int rating)
    {
        int percent = 0;
        foreach (var bonus in Bonuses(inn, kitchen))
        {
            if (bonus is { Kind: ChefBonusKind.DishSpeed } && bonus.DishType == dish.Type) percent += bonus.Percent;
            else if (FlavorCounts(inn, bonus, dish, rating)) percent += bonus.Percent;
        }
        return percent;
    }

    /// <summary>Seconds <paramref name="dish"/> takes in <paramref name="kitchen"/>: base ÷ (1 + bonus), rounded up, as the game shows.</summary>
    public static int CookSeconds(InnData inn, Kitchen kitchen, Dish dish, int rating)
    {
        int divisor = 100 + SpeedPercent(inn, kitchen, dish, rating);
        // In whole numbers, so 35 s at +40 % is exactly 25 s, not 25.000000001 rounded up to 26.
        return (dish.CookSeconds * 100 + divisor - 1) / divisor;
    }

    /// <summary>
    /// The chance, in percent, that <paramref name="dish"/> comes out of <paramref name="kitchen"/>
    /// as a choice dish: the rating's base, the chefs' choice bonuses, Si's 品质把控 assist
    /// (<paramref name="siQualityPercent"/>, 5 at level II, 0 without it) and the flavor bonuses
    /// for a dish of that flavor (from <see cref="InnChefRules.FlavorFromRating"/> on).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A rating whose base chance isn't known.</exception>
    public static int ChoicePercent(InnData inn, Kitchen kitchen, Dish dish, int rating, int siQualityPercent)
    {
        if (rating < 1 || rating > MaxRating)
            throw new ArgumentOutOfRangeException(nameof(rating), rating, $"The base choice chance is known for Inn Rating 1 to {MaxRating}.");
        int percent = ChoiceBaseByRating[rating - 1] + siQualityPercent;
        foreach (var bonus in Bonuses(inn, kitchen))
        {
            if (bonus.Kind == ChefBonusKind.ChoiceChance) percent += bonus.Percent;
            else if (FlavorCounts(inn, bonus, dish, rating)) percent += bonus.Percent;
        }
        return percent;
    }

    /// <summary><see cref="ChoicePercent"/> as a probability, 0 to 1.</summary>
    public static double ChoiceChance(InnData inn, Kitchen kitchen, Dish dish, int rating, int siQualityPercent) =>
        ChoicePercent(inn, kitchen, dish, rating, siQualityPercent) / 100.0;

    private static IEnumerable<ChefBonus> Bonuses(InnData inn, Kitchen kitchen)
    {
        if (kitchen.Chefs.Count > MaxChefs)
            throw new ArgumentException($"Kitchen {kitchen.Number} has {kitchen.Chefs.Count} chefs; a kitchen has at most {MaxChefs}.", nameof(kitchen));
        return kitchen.Chefs.SelectMany(inn.ChefBonusesOf);
    }

    private static bool FlavorCounts(InnData inn, ChefBonus bonus, Dish dish, int rating) =>
        bonus is { Kind: ChefBonusKind.Flavor, Flavor: { } flavor } && rating >= inn.ChefBonuses.FlavorFromRating && dish.Traits.Contains(flavor);
}

/// <summary>
/// The session's kitchens with what decides their speed: the chefs in each, the Inn Rating and Si's
/// quality assist. Cook times and choice chances are worked out once per dish and kept.
/// </summary>
public sealed class InnKitchens
{
    private readonly Dictionary<(int, string), int> _seconds = [];
    private readonly Dictionary<(int, string), int> _choice = [];

    /// <exception cref="ArgumentException">No kitchens, more than <see cref="KitchenMath.MaxKitchens"/>, two with one number, or a kitchen with too many chefs.</exception>
    public InnKitchens(InnData inn, IReadOnlyList<Kitchen> kitchens, int rating, int siQualityPercent)
    {
        if (kitchens.Count is < 1 or > KitchenMath.MaxKitchens)
            throw new ArgumentException($"{kitchens.Count} kitchens; the inn has 1 to {KitchenMath.MaxKitchens}.", nameof(kitchens));
        if (kitchens.Select(k => k.Number).Distinct().Count() != kitchens.Count)
            throw new ArgumentException("Two kitchens have one number.", nameof(kitchens));
        if (kitchens.FirstOrDefault(k => k.Chefs.Count > KitchenMath.MaxChefs) is { } crowded)
            throw new ArgumentException($"Kitchen {crowded.Number} has {crowded.Chefs.Count} chefs; a kitchen has at most {KitchenMath.MaxChefs}.", nameof(kitchens));
        Inn = inn;
        Kitchens = kitchens;
        Rating = rating;
        SiQualityPercent = siQualityPercent;
    }

    public InnData Inn { get; }
    public IReadOnlyList<Kitchen> Kitchens { get; }
    public int Rating { get; }
    public int SiQualityPercent { get; }

    /// <summary>The kitchen numbered <paramref name="number"/>.</summary>
    public Kitchen this[int number] => Kitchens.First(k => k.Number == number);

    public int CookSeconds(int kitchen, Dish dish)
    {
        if (!_seconds.TryGetValue((kitchen, dish.Id), out int seconds))
            _seconds[(kitchen, dish.Id)] = seconds = KitchenMath.CookSeconds(Inn, this[kitchen], dish, Rating);
        return seconds;
    }

    public int ChoicePercent(int kitchen, Dish dish)
    {
        if (!_choice.TryGetValue((kitchen, dish.Id), out int percent))
            _choice[(kitchen, dish.Id)] = percent = KitchenMath.ChoicePercent(Inn, this[kitchen], dish, Rating, SiQualityPercent);
        return percent;
    }
}
