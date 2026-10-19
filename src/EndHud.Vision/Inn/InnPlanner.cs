using System.Text.RegularExpressions;
using EndHud.Vision.Operators;

namespace EndHud.Vision.Inn;

/// <summary>A kitchen the session has and how many chef places it has (1 or 2).</summary>
public sealed record PlanKitchen(int Number, int Places);

/// <summary>"At least <paramref name="Count"/> dishes with any of these traits" (至少有2道带有[素食]或[滋补]特性的菜品).</summary>
/// <param name="AnyOf">Trait ids (<see cref="InnTrait.Id"/>).</param>
public sealed partial record TraitRule(int Count, IReadOnlyList<string> AnyOf)
{
    /// <summary>
    /// The rule a 菜单要求 line states, or null when the line isn't one: "至少有N道带有[A]或[B]特性的菜品",
    /// as test_yuanyuan2's 养身心 shows it. Traits are matched by their Chinese names in brackets.
    /// </summary>
    public static TraitRule? Parse(string line, InnData data)
    {
        var match = TraitRuleText().Match(line.Replace(" ", ""));
        if (!match.Success) return null;
        var traits = Brackets().Matches(match.Groups[2].Value)
            .Select(m => data.Traits.FirstOrDefault(t => t.Zh == m.Groups[1].Value)?.Id)
            .ToList();
        if (traits.Count == 0 || traits.Any(t => t is null)) return null;
        return new TraitRule(int.Parse(match.Groups[1].Value), [.. traits!]);
    }

    [GeneratedRegex(@"至少有?(\d+)道带有(.+?)特性的菜品")]
    private static partial Regex TraitRuleText();

    [GeneratedRegex(@"\[([^\]]+)\]")]
    private static partial Regex Brackets();
}

/// <summary>
/// What the menu must be (菜单要求 and the menu screen's counters "N/8"). The numbers are passed by
/// the caller from what the screens show; only the trait rule's wording is known so far
/// (<see cref="TraitRule.Parse"/>).
/// </summary>
/// <param name="MinTypes">Dish types (主食, 菜肴, 汤羹, 甜品) on the menu at least ("≥3 types" at Rating 4).</param>
public sealed record MenuRules(int MinDishes, int MaxDishes, int MinDrinks, int MaxDrinks, int MinTypes, IReadOnlyList<TraitRule> TraitRules);

/// <summary>Everything the planner works from.</summary>
/// <param name="HotTraits">热门特性 as read (trait ids), or the theme's preferences when none were read (<see cref="InnPlanner.HotTraitsOf"/>).</param>
/// <param name="Rating">Inn Rating, 1 to <see cref="KitchenMath.MaxRating"/>.</param>
/// <param name="SiQualityPercent">Si's 品质把控 assist (5 at level II, 0 without it).</param>
/// <param name="Tables">How many tables the inn has (6 at Rating 4 in the recordings).</param>
/// <param name="Kitchens">The kitchens and their chef places.</param>
/// <param name="Chefs">The operators the player has (anyone who can't cook is left out by the planner).</param>
/// <param name="Dishes">The unlocked dishes and their stock.</param>
/// <param name="Drinks">The unlocked drinks and their stock.</param>
public sealed record InnPlanInput(IReadOnlyList<string> HotTraits, int Rating, int SiQualityPercent, int Tables,
    IReadOnlyList<PlanKitchen> Kitchens, IReadOnlyList<OperatorInfo> Chefs, IReadOnlyList<MenuDish> Dishes, IReadOnlyList<MenuDrink> Drinks,
    MenuRules Rules);

/// <summary>How a plan scores under the planner's model.</summary>
/// <param name="Income">Expected income per 5-minute session (prices with the 热门 bonus; no tips).</param>
/// <param name="Orders">Orders served in the session.</param>
/// <param name="KitchenBound">True when the kitchens, not the guests, limit the orders.</param>
/// <param name="ChoiceDishes">Expected choice dishes (珍馐) in the session: only a tie-breaker.</param>
public sealed record InnPlanScore(double Income, double Orders, bool KitchenBound, double ChoiceDishes)
{
    public static readonly InnPlanScore Zero = new(0, 0, false, 0);

    /// <summary>Better: more income, then (within a cent) more choice dishes.</summary>
    public bool BetterThan(InnPlanScore other) =>
        Income > other.Income + InnPlanner.Tolerance || (Math.Abs(Income - other.Income) <= InnPlanner.Tolerance && ChoiceDishes > other.ChoiceDishes + 1e-9);
}

public enum PlanReasonKind
{
    /// <summary>A dish's value per kitchen-second where it cooks fastest (<see cref="PlanReason.Value"/>).</summary>
    DishValue,

    /// <summary>A dish matches this many 热门 traits (<see cref="PlanReason.Value"/>).</summary>
    HotTraits,

    /// <summary>A dish counts towards a trait rule (<see cref="PlanReason.Rule"/>).</summary>
    MeetsRule,

    /// <summary>A rule no menu of the available dishes can meet (<see cref="PlanReason.Rule"/>; -1 the types or counts).</summary>
    RuleUnmet,

    /// <summary>A kitchen left without chefs: none left to put there (it doesn't cook, an assumption).</summary>
    KitchenUnstaffed,

    /// <summary>The guests limit the orders, not the kitchens.</summary>
    GuestBound,

    /// <summary>A dish or drink whose stock runs out before the session ends (<see cref="PlanReason.Value"/> = orders short).</summary>
    ShortOfStock,
}

/// <summary>Why the plan is what it is, as data (the panel words it).</summary>
/// <param name="Id">The dish or drink, when it's about one.</param>
/// <param name="Kitchen">The kitchen, when it's about one.</param>
/// <param name="Rule">Index into <see cref="MenuRules.TraitRules"/>, when it's about a rule.</param>
public sealed record PlanReason(PlanReasonKind Kind, string? Id = null, int? Kitchen = null, int? Rule = null, double Value = 0);

/// <summary>A kitchen of the plan: its number and the chefs to put in it.</summary>
public sealed record PlannedKitchen(int Number, IReadOnlyList<OperatorInfo> Chefs);

/// <summary>The planner's suggestion for the session.</summary>
/// <param name="Opening">What to cook ahead in every kitchen the moment the session starts.</param>
/// <param name="RulesMet">False when no menu of the available dishes meets every rule (the reasons say which).</param>
public sealed record InnPlan(IReadOnlyList<PlannedKitchen> Kitchens, IReadOnlyList<MenuDish> Dishes, IReadOnlyList<MenuDrink> Drinks,
    DispatchAdvice? Opening, InnPlanScore Score, bool RulesMet, IReadOnlyList<PlanReason> Reasons);

/// <summary>
/// The pre-session planner (plan S4): which chefs to put in which kitchen and which dishes and
/// drinks to put on the menu, to earn the most in a session under every menu rule.
/// </summary>
public sealed class InnPlanner(InnData inn)
{
    /// <summary>Two incomes this close are equal (then the choice-dish chance decides).</summary>
    public const double Tolerance = 0.01;

    /// <summary>Stub value, replaced by the implementation.</summary>
    public const double MinGuestCycleSeconds = 34;

    public InnData Inn { get; } = inn;

    /// <summary>The traits to value: those read off the screen, else the theme's preferences.</summary>
    public static IReadOnlyList<string> HotTraitsOf(IReadOnlyList<string> read, InnTheme? theme) =>
        read.Count > 0 ? read : theme?.Prefers ?? [];

    public InnPlan Plan(InnPlanInput input) => new([], [], [], null, InnPlanScore.Zero, false, []);

    public InnPlanScore Evaluate(InnPlanInput input, IReadOnlyList<Kitchen> kitchens, IReadOnlyList<MenuDish> dishes, IReadOnlyList<MenuDrink> drinks) =>
        InnPlanScore.Zero;

    public bool MeetsRules(MenuRules rules, IReadOnlyList<MenuDish> dishes, IReadOnlyList<MenuDrink> drinks) => false;
}
