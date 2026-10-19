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
public sealed record MenuRules(int MinDishes, int MaxDishes, int MinDrinks, int MaxDrinks, int MinTypes, IReadOnlyList<TraitRule> TraitRules)
{
    /// <summary>The most dishes, and drinks, a menu has been seen to allow (the menu screen's counters "N/8" at Rating 4).</summary>
    public const int MostSeen = 8;

    /// <summary>
    /// An Inn Rating's own rules, for when the menu screen's 菜单要求 hasn't been read, with the
    /// theme's <paramref name="traitRules"/>. Known only for Rating 4, as test_yuanyuan2's menu screen
    /// states them: at least 3 dish types, 6 dishes and 3 drinks, and 8 of each at most. Null for a
    /// rating not seen yet (never guessed).
    /// </summary>
    public static MenuRules? OfRating(int rating, IReadOnlyList<TraitRule> traitRules) =>
        rating == 4 ? new MenuRules(6, MostSeen, 3, MostSeen, 3, traitRules) : null;
}

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

    /// <summary>A kitchen left without chefs: none left to put there (it can't cook, <see cref="KitchenMath.Cooks"/>).</summary>
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
/// <para>
/// The model (from the measurements of test_yuanyuan1, plan S1): the kitchens, not the guests,
/// are the bottleneck. Guests pick uniformly from the menu (owner); an order is 1 dish + 1 drink
/// 75 %, 2 dishes 20 %, 1 dish 5 %, so 1.2 dishes and 0.75 drinks. Each dish is cooked in the
/// kitchens side by side, each share where it costs least, so that the kitchens finish together
/// (a small linear program, <see cref="Balance"/>); the orders served are what the kitchens cook
/// in the session, or what the tables can bring, whichever is less. A dish is worth its price ×
/// (1 + 10 % per matched 热门 trait); tips aren't modelled (owner), and a dish's choice-dish (珍馐)
/// chance only breaks ties (owner: "only track it is a 珍馐").
/// </para>
/// <para>
/// The search: chefs into kitchens by a beam search over bonus profiles (operators with the same
/// bonuses are one profile), the menu by greedy add and then add/remove/swap until nothing
/// improves; the two alternate until the plan stops improving.
/// </para>
/// </summary>
public sealed class InnPlanner(InnData inn)
{
    /// <summary>Two incomes this close are equal (then the choice-dish chance decides).</summary>
    public const double Tolerance = 0.01;

    /// <summary>The session is 5:00 (recording).</summary>
    public const double SessionSeconds = InnSimulator.SessionSeconds;

    /// <summary>Dishes per order: 1 dish + 1 drink 75 %, 2 dishes 20 %, 1 dish 5 % (20 orders of test_yuanyuan1).</summary>
    public const double DishesPerOrder = 0.75 * 1 + 0.20 * 2 + 0.05 * 1;

    /// <summary>Drinks per order: the 75 % of orders with a drink; never 2 drinks, never a drink alone (test_yuanyuan1).</summary>
    public const double DrinksPerOrder = 0.75;

    /// <summary>A dish or drink is worth 10 % more per matched 热门 trait (the theme screen: 热门特性 +10 % each).</summary>
    public const double HotTraitBonus = 0.10;

    /// <summary>
    /// The fastest a table turns a guest over, in seconds: an assumption built from the fastest of
    /// each step measured on test_yuanyuan1 (ordering 6 s, order to eating 12 s, eating 10 s,
    /// paying 3 s, the next guest seated 3 s). It caps the orders only when the kitchens could cook
    /// more than the tables bring; in the recording the kitchens were the limit.
    /// </summary>
    public const double MinGuestCycleSeconds = 6 + 12 + 10 + 3 + 3;

    /// <summary>Line-ups the beam keeps at each chef place.</summary>
    public const int BeamWidth = 20;

    /// <summary>The best line-ups of a beam search each get their own menu.</summary>
    public const int LineUpsTried = 8;

    /// <summary>Steps of Dinkelbach's method when the beam judges a line-up.</summary>
    private const int DinkelbachSteps = 3;

    /// <summary>Rounds of chefs-then-menu at most.</summary>
    private const int MaxRounds = 4;

    /// <summary>A cook with no bonus: a kitchen at base speed (a place the beam hasn't filled yet).</summary>
    private static readonly OperatorInfo PlainCook = new("", "", "", "");

    public InnData Inn { get; } = inn;

    /// <summary>The traits to value: those read off the screen, else the theme's preferences.</summary>
    public static IReadOnlyList<string> HotTraitsOf(IReadOnlyList<string> read, InnTheme? theme) =>
        read.Count > 0 ? read : theme?.Prefers ?? [];

    /// <summary>
    /// The 热门 traits <paramref name="dish"/> matches: its traits and its type. The screen has only
    /// shown traits as 热门特性 so far; a type counts when a theme's preferences (used when none were
    /// read) name one, an assumption until a type-preferring theme is seen.
    /// </summary>
    public static int HotMatches(IReadOnlyCollection<string> hot, Dish dish) =>
        dish.Traits.Count(hot.Contains) + (hot.Contains(dish.Type.ToString()) ? 1 : 0);

    /// <summary>The 热门 traits <paramref name="drink"/> matches: its traits, and "Drink" for any drink.</summary>
    public static int HotMatches(IReadOnlyCollection<string> hot, InnDrink drink) =>
        drink.Traits.Count(hot.Contains) + (hot.Contains(InnData.Drink) ? 1 : 0);

    public static double Value(IReadOnlyCollection<string> hot, Dish dish) => dish.Price * (1 + HotTraitBonus * HotMatches(hot, dish));

    public static double Value(IReadOnlyCollection<string> hot, InnDrink drink) => drink.Price * (1 + HotTraitBonus * HotMatches(hot, drink));

    /// <summary>Whether the menu meets every rule: its size, its drinks, its types and each trait rule; no dish or drink twice.</summary>
    public bool MeetsRules(MenuRules rules, IReadOnlyList<MenuDish> dishes, IReadOnlyList<MenuDrink> drinks) =>
        dishes.Count >= rules.MinDishes && dishes.Count <= rules.MaxDishes
        && drinks.Count >= rules.MinDrinks && drinks.Count <= rules.MaxDrinks
        && dishes.Select(d => d.Dish.Id).Distinct().Count() == dishes.Count
        && drinks.Select(d => d.Drink.Id).Distinct().Count() == drinks.Count
        && dishes.Select(d => d.Dish.Type).Distinct().Count() >= rules.MinTypes
        && rules.TraitRules.All(r => dishes.Count(d => d.Dish.Traits.Intersect(r.AnyOf).Any()) >= r.Count);

    /// <summary>The score of a line-up and menu under the model.</summary>
    public InnPlanScore Evaluate(InnPlanInput input, IReadOnlyList<Kitchen> kitchens, IReadOnlyList<MenuDish> dishes, IReadOnlyList<MenuDrink> drinks)
    {
        var hot = input.HotTraits.ToHashSet();
        var times = Times(input, kitchens, dishes.Select(d => d.Dish).ToList());
        return Score(input, times, [.. dishes.Select(d => Value(hot, d.Dish))], [.. dishes.Select(d => d.Stock)],
            [.. drinks.Select(d => Value(hot, d.Drink))], [.. drinks.Select(d => d.Stock)]);
    }

    /// <summary>The plan for <paramref name="input"/>.</summary>
    /// <exception cref="ArgumentException">No kitchens or more than three, two with one number, places outside 1–2, or an unknown Inn Rating.</exception>
    public InnPlan Plan(InnPlanInput input)
    {
        Validate(input);
        var hot = input.HotTraits.ToHashSet();
        var search = new Search(this, input, hot);

        // Start from a menu for kitchens at base speed, then alternate chefs and menu.
        var (menu, drinks) = search.BestMenu(search.Materialize(search.PlainLineUp()), search.InitialDrinks());
        (Kitchen[] Kitchens, int[] Menu, int[] Drinks, InnPlanScore Score)? best = null;
        for (int round = 0; round < MaxRounds; round++)
        {
            bool improved = false;
            foreach (var lineUp in search.Beam(menu, drinks).Take(LineUpsTried))
            {
                var kitchens = search.Materialize(lineUp, int.MaxValue);
                var (m, d) = search.BestMenu(kitchens, drinks, menu);
                var score = search.Score(kitchens, m, d);
                if (best is null || score.BetterThan(best.Value.Score))
                {
                    best = (kitchens, m, d, score);
                    improved = true;
                }
            }
            (menu, drinks) = (best!.Value.Menu, best.Value.Drinks);
            if (!improved) break;
        }

        var (finalKitchens, finalMenu, finalDrinks, finalScore) = search.FillEmptyPlaces(search.Polish(best!.Value));
        var dishes = finalMenu.Select(i => search.Dishes[i]).ToList();
        var drinkList = finalDrinks.Select(i => search.Drinks[i]).ToList();
        var cooking = finalKitchens.Where(Cooks).ToList();
        var opening = cooking.Count == 0 || dishes.Count == 0
            ? null
            : new DispatchAdvisor(new InnKitchens(Inn, cooking, input.Rating, input.SiQualityPercent)).Opening(dishes);
        bool rulesMet = MeetsRules(input.Rules, dishes, drinkList);
        return new InnPlan([.. finalKitchens.Select(k => new PlannedKitchen(k.Number, k.Chefs))], dishes, drinkList, opening, finalScore, rulesMet,
            Reasons(input, hot, finalKitchens, dishes, drinkList, finalScore, rulesMet, search));
    }

    private static void Validate(InnPlanInput input)
    {
        if (input.Kitchens.Count is < 1 or > KitchenMath.MaxKitchens)
            throw new ArgumentException($"{input.Kitchens.Count} kitchens; the inn has 1 to {KitchenMath.MaxKitchens}.", nameof(input));
        if (input.Kitchens.Select(k => k.Number).Distinct().Count() != input.Kitchens.Count)
            throw new ArgumentException("Two kitchens have one number.", nameof(input));
        if (input.Kitchens.FirstOrDefault(k => k.Places is < 1 or > KitchenMath.MaxChefs) is { } bad)
            throw new ArgumentException($"Kitchen {bad.Number} has {bad.Places} chef places; a kitchen has 1 to {KitchenMath.MaxChefs}.", nameof(input));
        if (input.Rating < 1 || input.Rating > KitchenMath.MaxRating)
            throw new ArgumentException($"Inn Rating {input.Rating}; the model knows 1 to {KitchenMath.MaxRating}.", nameof(input));
    }

    private static bool Cooks(Kitchen kitchen) => KitchenMath.Cooks(kitchen);

    /// <summary>Cook seconds and choice chances (0–1) of each dish in each kitchen that cooks.</summary>
    private (double[,] Seconds, double[,] Choice) Times(InnPlanInput input, IReadOnlyList<Kitchen> kitchens, IReadOnlyList<Dish> dishes)
    {
        var cooking = kitchens.Where(Cooks).ToList();
        var seconds = new double[cooking.Count, dishes.Count];
        var choice = new double[cooking.Count, dishes.Count];
        for (int k = 0; k < cooking.Count; k++)
        {
            for (int d = 0; d < dishes.Count; d++)
            {
                seconds[k, d] = KitchenMath.CookSeconds(Inn, cooking[k], dishes[d], input.Rating);
                choice[k, d] = KitchenMath.ChoiceChance(Inn, cooking[k], dishes[d], input.Rating, input.SiQualityPercent);
            }
        }
        return (seconds, choice);
    }

    /// <summary>The model's score from the cook times, the values and the stock of each dish and drink.</summary>
    private static InnPlanScore Score(InnPlanInput input, (double[,] Seconds, double[,] Choice) times,
        double[] dishValues, int?[] dishStock, double[] drinkValues, int?[] drinkStock)
    {
        var (seconds, choice) = times;
        int kitchens = seconds.GetLength(0), dishes = seconds.GetLength(1);
        if (kitchens == 0 || dishes == 0 || input.Tables <= 0) return InnPlanScore.Zero;

        double perDish = Balance(seconds);
        double kitchenOrders = SessionSeconds / (DishesPerOrder * perDish);
        double guestOrders = input.Tables * SessionSeconds / MinGuestCycleSeconds;
        double orders = Math.Min(kitchenOrders, guestOrders);

        // Guests pick uniformly: each dish gets an equal share of the dishes ordered, each drink of the drinks.
        double eachDish = orders * DishesPerOrder / dishes;
        double income = 0, choiceDishes = 0;
        for (int d = 0; d < dishes; d++)
        {
            double served = Math.Min(eachDish, dishStock[d] ?? double.MaxValue);
            income += served * dishValues[d];
            // Tie-breaker only: the chance where the dish cooks fastest (the better chance on a tie).
            int fastest = 0;
            for (int k = 1; k < kitchens; k++)
                if (seconds[k, d] < seconds[fastest, d] || (seconds[k, d] == seconds[fastest, d] && choice[k, d] > choice[fastest, d])) fastest = k;
            choiceDishes += served * choice[fastest, d];
        }
        if (drinkValues.Length > 0)
        {
            double eachDrink = orders * DrinksPerOrder / drinkValues.Length;
            for (int d = 0; d < drinkValues.Length; d++) income += Math.Min(eachDrink, drinkStock[d] ?? double.MaxValue) * drinkValues[d];
        }
        return new InnPlanScore(income, orders, kitchenOrders <= guestOrders, choiceDishes);
    }

    /// <summary>
    /// The kitchen-seconds one dish of the menu's mix costs with the kitchens cooking side by side
    /// and their loads balanced: the least, over every way of sharing each dish among the kitchens,
    /// of the busiest kitchen's seconds per dish ordered (each dish an equal share of the orders).
    /// Solved through its dual, max over kitchen weights λ (λ ≥ 0, Σλ = 1) of Σ_d min_k λ_k t_kd / m,
    /// a concave piecewise-linear function whose maximum is at a vertex of the arrangement of the
    /// planes λ_i t_id = λ_j t_jd and λ_k = 0; with at most three kitchens there are few, and every
    /// one is tried.
    /// </summary>
    internal static double Balance(double[,] seconds)
    {
        int kitchens = seconds.GetLength(0), dishes = seconds.GetLength(1);
        double F(ReadOnlySpan<double> lambda)
        {
            double sum = 0;
            for (int d = 0; d < dishes; d++)
            {
                double least = double.MaxValue;
                for (int k = 0; k < kitchens; k++) least = Math.Min(least, lambda[k] * seconds[k, d]);
                sum += least;
            }
            return sum / dishes;
        }
        if (kitchens == 1) return F([1.0]);

        // The planes, as coefficient rows c (c·λ = 0), each once.
        var planes = new List<double[]>();
        var seen = new HashSet<(int, int, double)>();
        for (int k = 0; k < kitchens; k++)
        {
            var c = new double[kitchens];
            c[k] = 1;
            planes.Add(c);
        }
        for (int d = 0; d < dishes; d++)
        {
            for (int i = 0; i < kitchens; i++)
            {
                for (int j = i + 1; j < kitchens; j++)
                {
                    if (!seen.Add((i, j, Math.Round(seconds[i, d] / seconds[j, d], 12)))) continue;
                    var c = new double[kitchens];
                    c[i] = seconds[i, d];
                    c[j] = -seconds[j, d];
                    planes.Add(c);
                }
            }
        }

        double best = 0;
        Span<double> lambda = stackalloc double[kitchens];
        if (kitchens == 2)
        {
            foreach (var c in planes)
            {
                // c0 λ0 + c1 λ1 = 0, λ0 + λ1 = 1.
                double det = c[0] - c[1];
                if (Math.Abs(det) < 1e-12) continue;
                lambda[0] = -c[1] / det;
                lambda[1] = c[0] / det;
                if (Feasible(lambda)) best = Math.Max(best, F(lambda));
            }
            return best;
        }
        for (int p = 0; p < planes.Count; p++)
        {
            for (int q = p + 1; q < planes.Count; q++)
            {
                if (Solve3(planes[p], planes[q], lambda) && Feasible(lambda)) best = Math.Max(best, F(lambda));
            }
        }
        return best;
    }

    private static bool Feasible(Span<double> lambda)
    {
        for (int k = 0; k < lambda.Length; k++)
        {
            if (lambda[k] < -1e-12) return false;
            lambda[k] = Math.Max(0, lambda[k]);
        }
        return true;
    }

    /// <summary>a·λ = 0, b·λ = 0, λ0 + λ1 + λ2 = 1, by Cramer's rule; false when the planes don't meet in one point.</summary>
    private static bool Solve3(double[] a, double[] b, Span<double> lambda)
    {
        double Det(double a0, double a1, double a2, double b0, double b1, double b2, double c0, double c1, double c2) =>
            a0 * (b1 * c2 - b2 * c1) - a1 * (b0 * c2 - b2 * c0) + a2 * (b0 * c1 - b1 * c0);
        double det = Det(a[0], a[1], a[2], b[0], b[1], b[2], 1, 1, 1);
        if (Math.Abs(det) < 1e-9) return false;
        // Right-hand side (0, 0, 1).
        lambda[0] = Det(0, a[1], a[2], 0, b[1], b[2], 1, 1, 1) / det;
        lambda[1] = Det(a[0], 0, a[2], b[0], 0, b[2], 1, 1, 1) / det;
        lambda[2] = Det(a[0], a[1], 0, b[0], b[1], 0, 1, 1, 1) / det;
        return true;
    }

    private IReadOnlyList<PlanReason> Reasons(InnPlanInput input, HashSet<string> hot, IReadOnlyList<Kitchen> kitchens,
        IReadOnlyList<MenuDish> dishes, IReadOnlyList<MenuDrink> drinks, InnPlanScore score, bool rulesMet, Search search)
    {
        var reasons = new List<PlanReason>();
        foreach (var kitchen in kitchens.Where(k => !Cooks(k))) reasons.Add(new PlanReason(PlanReasonKind.KitchenUnstaffed, Kitchen: kitchen.Number));
        if (!score.KitchenBound && score.Orders > 0) reasons.Add(new PlanReason(PlanReasonKind.GuestBound, Value: score.Orders));
        if (!rulesMet) reasons.AddRange(search.UnmetRules().Select(r => new PlanReason(PlanReasonKind.RuleUnmet, Rule: r)));

        var cooking = kitchens.Where(Cooks).ToList();
        double eachDish = dishes.Count == 0 ? 0 : score.Orders * DishesPerOrder / dishes.Count;
        foreach (var menu in dishes)
        {
            var dish = menu.Dish;
            if (cooking.Count > 0)
            {
                var fastest = cooking.MinBy(k => KitchenMath.CookSeconds(Inn, k, dish, input.Rating))!;
                double perSecond = Value(hot, dish) / KitchenMath.CookSeconds(Inn, fastest, dish, input.Rating);
                reasons.Add(new PlanReason(PlanReasonKind.DishValue, dish.Id, fastest.Number, Value: perSecond));
            }
            if (HotMatches(hot, dish) is var matches and > 0) reasons.Add(new PlanReason(PlanReasonKind.HotTraits, dish.Id, Value: matches));
            for (int r = 0; r < input.Rules.TraitRules.Count; r++)
                if (dish.Traits.Intersect(input.Rules.TraitRules[r].AnyOf).Any()) reasons.Add(new PlanReason(PlanReasonKind.MeetsRule, dish.Id, Rule: r));
            if (menu.Stock is { } stock && stock < eachDish) reasons.Add(new PlanReason(PlanReasonKind.ShortOfStock, dish.Id, Value: eachDish - stock));
        }
        double eachDrink = drinks.Count == 0 ? 0 : score.Orders * DrinksPerOrder / drinks.Count;
        foreach (var drink in drinks)
        {
            if (HotMatches(hot, drink.Drink) is var matches and > 0) reasons.Add(new PlanReason(PlanReasonKind.HotTraits, drink.Drink.Id, Value: matches));
            if (drink.Stock is { } stock && stock < eachDrink) reasons.Add(new PlanReason(PlanReasonKind.ShortOfStock, drink.Drink.Id, Value: eachDrink - stock));
        }
        return reasons;
    }

    /// <summary>One planning run: the candidates, the chefs' profiles, and the cached scores.</summary>
    private sealed class Search
    {
        private readonly InnPlanner _planner;
        private readonly InnPlanInput _input;
        private readonly double[] _dishValues, _drinkValues;

        /// <summary>Chefs with the same bonuses, as one profile: its bonuses and its operators (in the input's order).</summary>
        private readonly List<(string Bonuses, List<OperatorInfo> Operators)> _profiles;

        public Search(InnPlanner planner, InnPlanInput input, HashSet<string> hot)
        {
            _planner = planner;
            _input = input;
            Dishes = [.. input.Dishes.Where(d => d.Stock is not <= 0).DistinctBy(d => d.Dish.Id)];
            Drinks = [.. input.Drinks.Where(d => d.Stock is not <= 0).DistinctBy(d => d.Drink.Id)];
            _dishValues = [.. Dishes.Select(d => Value(hot, d.Dish))];
            _drinkValues = [.. Drinks.Select(d => Value(hot, d.Drink))];
            var inn = planner.Inn;
            _profiles = [.. input.Chefs
                .Where(o => !inn.ChefBonuses.NotChefs.Contains(o.Id))
                .DistinctBy(o => o.Id)
                .GroupBy(o => string.Join("|", inn.ChefBonusesOf(o).Select(b => $"{b.Kind}:{b.Percent}:{b.DishType}:{b.Flavor}").Order(StringComparer.Ordinal)))
                .Select(g => (g.Key, g.ToList()))];
        }

        /// <summary>The dishes and drinks that can go on the menu (offered, in stock, each once).</summary>
        public IReadOnlyList<MenuDish> Dishes { get; }

        public IReadOnlyList<MenuDrink> Drinks { get; }

        private MenuRules Rules => _input.Rules;

        // ---- Scoring ----

        private readonly Dictionary<string, (double[,] Seconds, double[,] Choice)> _times = [];

        /// <summary>Cook times of every candidate dish in the kitchens, kept per line-up.</summary>
        private (double[,] Seconds, double[,] Choice) TimesOf(IReadOnlyList<Kitchen> kitchens)
        {
            string key = string.Join("/", kitchens.Where(Cooks).Select(k => string.Join("+", k.Chefs.Select(c => c.Id).Order(StringComparer.Ordinal))));
            if (!_times.TryGetValue(key, out var times))
                _times[key] = times = _planner.Times(_input, kitchens, [.. Dishes.Select(d => d.Dish)]);
            return times;
        }

        public InnPlanScore Score(IReadOnlyList<Kitchen> kitchens, IReadOnlyList<int> menu, IReadOnlyList<int> drinks)
        {
            var (allSeconds, allChoice) = TimesOf(kitchens);
            int k = allSeconds.GetLength(0);
            var seconds = new double[k, menu.Count];
            var choice = new double[k, menu.Count];
            for (int i = 0; i < k; i++)
            {
                for (int d = 0; d < menu.Count; d++)
                {
                    seconds[i, d] = allSeconds[i, menu[d]];
                    choice[i, d] = allChoice[i, menu[d]];
                }
            }
            return InnPlanner.Score(_input, (seconds, choice), [.. menu.Select(i => _dishValues[i])], [.. menu.Select(i => Dishes[i].Stock)],
                [.. drinks.Select(i => _drinkValues[i])], [.. drinks.Select(i => Drinks[i].Stock)]);
        }

        // ---- Drinks ----

        /// <summary>Drinks dearest first (unlimited stock first on a tie).</summary>
        private IEnumerable<int> DrinksByValue() =>
            Enumerable.Range(0, Drinks.Count).OrderByDescending(i => _drinkValues[i]).ThenBy(i => Drinks[i].Stock is null ? 0 : 1).ThenBy(i => i);

        /// <summary>
        /// The drinks for a dish menu: the dearest few, as many as pay best (guests pick uniformly, so
        /// a cheaper drink lowers the average; a drink short of stock spreads the orders); at least
        /// one when there is one, since orders bring drinks.
        /// </summary>
        public int[] BestDrinks(IReadOnlyList<Kitchen> kitchens, IReadOnlyList<int> menu)
        {
            var order = DrinksByValue().ToList();
            int least = Math.Max(Rules.MinDrinks, Math.Min(1, order.Count)), most = Math.Min(Rules.MaxDrinks, order.Count);
            int[] best = [.. order.Take(least)];
            InnPlanScore? bestScore = null;
            for (int count = least; count <= most; count++)
            {
                int[] drinks = [.. order.Take(count)];
                var score = Score(kitchens, menu, drinks);
                if (bestScore is null || score.BetterThan(bestScore)) (best, bestScore) = (drinks, score);
            }
            return best;
        }

        /// <summary>The drinks before any dish is chosen: the dearest, as few as allowed (at least one).</summary>
        public int[] InitialDrinks() => [.. DrinksByValue().Take(Math.Min(Drinks.Count, Math.Max(Rules.MinDrinks, 1)))];

        // ---- Menu ----

        private int Types(IEnumerable<int> menu) => menu.Select(i => Dishes[i].Dish.Type).Distinct().Count();

        private bool Has(int dish, TraitRule rule) => Dishes[dish].Dish.Traits.Intersect(rule.AnyOf).Any();

        /// <summary>How far the menu is from its types and trait rules.</summary>
        private int Shortfall(IReadOnlyCollection<int> menu) =>
            Math.Max(0, Rules.MinTypes - Types(menu)) + Rules.TraitRules.Sum(r => Math.Max(0, r.Count - menu.Count(i => Has(i, r))));

        private bool Valid(IReadOnlyCollection<int> menu) =>
            menu.Count >= Rules.MinDishes && menu.Count <= Rules.MaxDishes && Shortfall(menu) == 0;

        /// <summary>Rules no menu of the candidates can meet on its own: a trait rule's index, or -1 for the types or counts.</summary>
        public IEnumerable<int> UnmetRules()
        {
            bool countsOrTypes = Dishes.Count < Rules.MinDishes || Types(Enumerable.Range(0, Dishes.Count)) < Rules.MinTypes || Drinks.Count < Rules.MinDrinks;
            var traitRules = Enumerable.Range(0, Rules.TraitRules.Count)
                .Where(r => Enumerable.Range(0, Dishes.Count).Count(i => Has(i, Rules.TraitRules[r])) < Rules.TraitRules[r].Count).ToList();
            // Rules each possible on their own but not together within the menu's size.
            if (!countsOrTypes && traitRules.Count == 0) countsOrTypes = true;
            return countsOrTypes ? traitRules.Prepend(-1) : traitRules;
        }

        /// <summary>
        /// The best menu for these kitchens: built greedily (first what the rules need, then what pays
        /// most) up to each size the rules allow, each improved by adding, removing and swapping a
        /// dish while that pays; then the drinks for it, and the dishes again for those drinks.
        /// </summary>
        /// <param name="seed">A menu to improve as well (the plan's so far), so a change of one dish from it is never missed.</param>
        public (int[] Menu, int[] Drinks) BestMenu(IReadOnlyList<Kitchen> kitchens, int[] drinks, int[]? seed = null)
        {
            var menu = BestDishes(kitchens, drinks, seed);
            var newDrinks = BestDrinks(kitchens, menu);
            if (!newDrinks.SequenceEqual(drinks)) menu = BestDishes(kitchens, newDrinks, menu);
            return (menu, newDrinks);
        }

        /// <summary>Scores of menus already worked out, by line-up, drinks and dishes.</summary>
        private readonly Dictionary<(string Kitchens, string Drinks, UInt128 Menu), InnPlanScore> _scores = [];

        private static string KitchensKey(IReadOnlyList<Kitchen> kitchens) =>
            string.Join("/", kitchens.Select(k => string.Join("+", k.Chefs.Select(c => c.Id).Order(StringComparer.Ordinal))));

        /// <summary>The menu's score for the line-up and drinks, worked out once.</summary>
        private Func<IReadOnlyCollection<int>, InnPlanScore> Scorer(IReadOnlyList<Kitchen> kitchens, int[] drinks)
        {
            string kitchensKey = KitchensKey(kitchens), drinksKey = string.Join(",", drinks);
            return menu =>
            {
                if (Dishes.Count > 128) return Score(kitchens, [.. menu], drinks);
                UInt128 mask = 0;
                foreach (int i in menu) mask |= (UInt128)1 << i;
                if (!_scores.TryGetValue((kitchensKey, drinksKey, mask), out var score))
                    _scores[(kitchensKey, drinksKey, mask)] = score = Score(kitchens, [.. menu], drinks);
                return score;
            };
        }

        /// <summary>
        /// Each dish's worth (its value and its share of the drinks an order brings) and its time with
        /// every kitchen cooking it at once; null when no kitchen cooks.
        /// </summary>
        private (double[] Worth, double[] Together)? Weights(IReadOnlyList<Kitchen> kitchens, int[] drinks)
        {
            var (seconds, _) = TimesOf(kitchens);
            int cooking = seconds.GetLength(0);
            if (cooking == 0) return null;
            double drinkShare = drinks.Length == 0 ? 0 : DrinksPerOrder * drinks.Average(i => _drinkValues[i]) / DishesPerOrder;
            var together = Enumerable.Range(0, Dishes.Count).Select(d => 1 / Enumerable.Range(0, cooking).Sum(k => 1 / seconds[k, d])).ToArray();
            var worth = Enumerable.Range(0, Dishes.Count).Select(d => _dishValues[d] + drinkShare).ToArray();
            return (worth, together);
        }

        /// <summary>Greedy menus of every allowed size by worth − r × time.</summary>
        private IEnumerable<List<int>> BuildAtRatio(double[] worth, double[] together, double r)
        {
            var weight = Enumerable.Range(0, Dishes.Count).Select(d => worth[d] - r * together[d]).ToArray();
            for (int size = Math.Max(1, Rules.MinDishes); size <= Math.Min(Rules.MaxDishes, Dishes.Count); size++) yield return BuildBy(size, weight);
        }

        /// <summary>
        /// A quick judge of a line-up for the beam: the better of <paramref name="menu"/> and the menus
        /// one step of Dinkelbach's method gives from it for these kitchens (worth − r × time, r the
        /// menu's own worth per second here), so a line-up that suits other dishes isn't judged only
        /// by the dishes another line-up suits.
        /// </summary>
        public InnPlanScore QuickScore(IReadOnlyList<Kitchen> kitchens, int[] menu, int[] drinks)
        {
            var score = Scorer(kitchens, drinks);
            var best = score(menu);
            if (Weights(kitchens, drinks) is not var (worth, together) || menu.Length == 0) return best;
            // Each step's best menu gives the next step's r, until it settles.
            IReadOnlyList<int> current = menu;
            for (int step = 0; step < DinkelbachSteps; step++)
            {
                double r = current.Sum(d => worth[d]) / current.Sum(d => together[d]);
                IReadOnlyList<int>? next = null;
                foreach (var candidate in BuildAtRatio(worth, together, r))
                {
                    if (!Valid(candidate)) continue;
                    var s = score(candidate);
                    if (s.BetterThan(best)) (best, next) = (s, candidate);
                }
                if (next is null) break;
                current = next;
            }
            return best;
        }

        private int[] BestDishes(IReadOnlyList<Kitchen> kitchens, int[] drinks, int[]? seed)
        {
            var ScoreOf = Scorer(kitchens, drinks);

            // Starting menus: greedy by the score itself, and by a weight per dish for a range of
            // thresholds r: value (with its share of the drinks) − r × its time with every kitchen
            // cooking it. For one kitchen and no rules the best menu is the top of that order for
            // some r (Dinkelbach's method for a best ratio); the swaps below take care of the rest.
            int smallest = Math.Max(1, Rules.MinDishes), largest = Math.Min(Rules.MaxDishes, Dishes.Count);
            var starts = new List<List<int>>();
            for (int size = smallest; size <= largest; size++) starts.Add(Build(size, ScoreOf));
            if (Weights(kitchens, drinks) is var (worth, together))
            {
                foreach (double r in Enumerable.Range(0, Dishes.Count).Select(d => worth[d] / together[d]).Append(0).Distinct())
                    starts.AddRange(BuildAtRatio(worth, together, r));
            }

            // The best few distinct starts (valid ones first) are improved by swaps.
            var chosen = starts
                .DistinctBy(m => string.Join(",", m.Order()))
                .OrderByDescending(m => Valid(m))
                .ThenByDescending(m => m, Comparer<List<int>>.Create((a, b) => ScoreOf(a).BetterThan(ScoreOf(b)) ? 1 : ScoreOf(b).BetterThan(ScoreOf(a)) ? -1 : 0))
                .Take(StartsImproved)
                .ToList();
            if (seed is { Length: > 0 }) chosen.Add([.. seed]);
            List<int>? best = null;
            foreach (var menu in chosen)
            {
                Improve(menu, ScoreOf);
                bool valid = Valid(menu), bestValid = best is not null && Valid(best);
                if (best is null || (valid && !bestValid) || (valid == bestValid && ScoreOf(menu).BetterThan(ScoreOf(best))))
                    best = menu;
            }
            return [.. (best ?? []).Order()];
        }

        /// <summary>Starting menus improved by swaps, per menu search.</summary>
        private const int StartsImproved = 6;

        /// <summary>
        /// First the dishes that bring the menu closest to its rules, then the rest, each time the one
        /// <paramref name="better"/> prefers, up to <paramref name="size"/>.
        /// </summary>
        private List<int> BuildWith(int size, Func<List<int>, int, int, bool> better)
        {
            var menu = new List<int>();
            while (menu.Count < size)
            {
                int pick = -1, pickShortfall = int.MaxValue;
                for (int i = 0; i < Dishes.Count; i++)
                {
                    if (menu.Contains(i)) continue;
                    menu.Add(i);
                    int shortfall = Shortfall(menu);
                    menu.RemoveAt(menu.Count - 1);
                    if (pick < 0 || shortfall < pickShortfall || (shortfall == pickShortfall && better(menu, i, pick)))
                        (pick, pickShortfall) = (i, shortfall);
                }
                if (pick < 0) break;
                menu.Add(pick);
            }
            return menu;
        }

        /// <summary>Greedy by the score of the menu with each dish added.</summary>
        private List<int> Build(int size, Func<IReadOnlyCollection<int>, InnPlanScore> score) =>
            BuildWith(size, (menu, i, pick) => score([.. menu, i]).BetterThan(score([.. menu, pick])));

        /// <summary>Greedy by a weight per dish.</summary>
        private List<int> BuildBy(int size, double[] weight) => BuildWith(size, (_, i, pick) => weight[i] > weight[pick]);

        /// <summary>Add, remove or swap one dish, the best move each time, while the menu stays valid and pays more.</summary>
        private void Improve(List<int> menu, Func<IReadOnlyCollection<int>, InnPlanScore> score)
        {
            if (!Valid(menu)) return;
            while (true)
            {
                var current = score(menu);
                List<int>? bestMove = null;
                var bestScore = current;
                void Try(List<int> candidate)
                {
                    if (!Valid(candidate)) return;
                    var s = score(candidate);
                    if (s.BetterThan(bestScore)) (bestMove, bestScore) = (candidate, s);
                }
                var outside = Enumerable.Range(0, Dishes.Count).Where(i => !menu.Contains(i)).ToList();
                if (menu.Count < Rules.MaxDishes)
                    foreach (int add in outside) Try([.. menu, add]);
                if (menu.Count > Rules.MinDishes)
                    foreach (int remove in menu) Try([.. menu.Where(i => i != remove)]);
                foreach (int remove in menu)
                    foreach (int add in outside) Try([.. menu.Where(i => i != remove), add]);
                if (bestMove is null) return;
                menu.Clear();
                menu.AddRange(bestMove);
            }
        }

        // ---- Chefs ----

        /// <summary>A line-up: per kitchen (in the input's order), the profiles of its chefs; and the chefs of each profile left.</summary>
        public sealed record LineUp(int[][] Kitchens, int[] Left);

        /// <summary>No chef placed yet.</summary>
        private LineUp Empty() => new([.. _input.Kitchens.Select(_ => Array.Empty<int>())], [.. _profiles.Select(p => p.Operators.Count)]);

        /// <summary>As many kitchens as there are chefs, each with a cook of no bonus (the menu's starting point).</summary>
        public LineUp PlainLineUp() => Empty();

        /// <summary>
        /// The line-up's kitchens. A profile's chefs are given out in the input's order. Kitchens not
        /// yet reached (<paramref name="reached"/>) get a cook of no bonus while chefs are left for them,
        /// so a partial line-up is judged as it would at least end up.
        /// </summary>
        public Kitchen[] Materialize(LineUp lineUp, int reached = -1)
        {
            var next = new int[_profiles.Count];
            int spare = lineUp.Left.Sum();
            var kitchens = new Kitchen[_input.Kitchens.Count];
            for (int k = 0; k < kitchens.Length; k++)
            {
                var chefs = new List<OperatorInfo>();
                foreach (int profile in lineUp.Kitchens[k])
                {
                    var operators = _profiles[profile].Operators;
                    chefs.Add(operators[Math.Min(next[profile]++, operators.Count - 1)]);
                }
                if (chefs.Count == 0 && k > reached && spare > 0)
                {
                    chefs.Add(PlainCook);
                    spare--;
                }
                kitchens[k] = new Kitchen(_input.Kitchens[k].Number, chefs);
            }
            return kitchens;
        }

        /// <summary>
        /// The line-ups, best first: chef places filled one at a time (kitchen by kitchen), each with
        /// any profile that has chefs left or left empty, keeping the <see cref="BeamWidth"/> best
        /// at each place as judged with <paramref name="menu"/> and <paramref name="drinks"/>.
        /// </summary>
        public IReadOnlyList<LineUp> Beam(int[] menu, int[] drinks)
        {
            var places = _input.Kitchens.SelectMany((k, i) => Enumerable.Repeat(i, k.Places)).ToList();
            var beam = new List<LineUp> { Empty() };
            for (int place = 0; place < places.Count; place++)
            {
                int kitchen = places[place];
                bool lastOfKitchen = place + 1 == places.Count || places[place + 1] != kitchen;
                var next = new Dictionary<string, (LineUp LineUp, InnPlanScore Score, int Chefs)>();
                foreach (var lineUp in beam)
                {
                    foreach (int profile in Enumerable.Range(-1, _profiles.Count + 1))
                    {
                        if (profile >= 0 && lineUp.Left[profile] == 0) continue;
                        var kitchens = lineUp.Kitchens.Select(a => a).ToArray();
                        var left = lineUp.Left;
                        if (profile >= 0)
                        {
                            kitchens[kitchen] = [.. lineUp.Kitchens[kitchen].Append(profile).Order()];
                            left = [.. lineUp.Left];
                            left[profile]--;
                        }
                        var candidate = new LineUp(kitchens, left);
                        string key = Key(candidate, lastOfKitchen ? kitchen : kitchen - 1);
                        if (next.ContainsKey(key)) continue;
                        var score = QuickScore(Materialize(candidate, kitchen), menu, drinks);
                        next[key] = (candidate, score, kitchens.Sum(a => a.Length));
                    }
                }
                beam = [.. next.Values.OrderByDescending(x => x, Comparer<(LineUp LineUp, InnPlanScore Score, int Chefs)>.Create(Compare))
                    .Take(BeamWidth).Select(x => x.LineUp)];
            }
            return beam;

            // Better first: the score, then more chefs placed (a chef never slows a kitchen).
            static int Compare((LineUp LineUp, InnPlanScore Score, int Chefs) a, (LineUp LineUp, InnPlanScore Score, int Chefs) b) =>
                a.Score.BetterThan(b.Score) ? 1 : b.Score.BetterThan(a.Score) ? -1 : a.Chefs.CompareTo(b.Chefs);
        }

        /// <summary>Line-ups alike up to the order of finished kitchens with the same number of places are one.</summary>
        private string Key(LineUp lineUp, int finishedUpTo)
        {
            var parts = new List<string>();
            var finished = Enumerable.Range(0, finishedUpTo + 1)
                .GroupBy(k => _input.Kitchens[k].Places)
                .SelectMany(g => g.Select(k => $"{g.Key}:{string.Join(",", lineUp.Kitchens[k])}").Order(StringComparer.Ordinal));
            parts.AddRange(finished);
            parts.Add("#");
            parts.AddRange(Enumerable.Range(finishedUpTo + 1, lineUp.Kitchens.Length - finishedUpTo - 1).Select(k => string.Join(",", lineUp.Kitchens[k])));
            return string.Join(";", parts);
        }

        /// <summary>
        /// Places left empty while chefs are left over get the chef that pays most there (a chef never
        /// slows a kitchen; it may add choice-dish chance), and the menu is chosen again for them.
        /// </summary>
        public (Kitchen[] Kitchens, int[] Menu, int[] Drinks, InnPlanScore Score) FillEmptyPlaces((Kitchen[] Kitchens, int[] Menu, int[] Drinks, InnPlanScore Score) plan)
        {
            var kitchens = plan.Kitchens.ToArray();
            var used = kitchens.SelectMany(k => k.Chefs).Select(c => c.Id).ToHashSet();
            var spare = _profiles.SelectMany(p => p.Operators).Where(o => !used.Contains(o.Id)).ToList();
            bool changed = false;
            while (spare.Count > 0)
            {
                (int Kitchen, OperatorInfo Chef, InnPlanScore Score)? best = null;
                for (int k = 0; k < kitchens.Length; k++)
                {
                    if (kitchens[k].Chefs.Count >= _input.Kitchens[k].Places) continue;
                    foreach (var chef in spare)
                    {
                        var trial = kitchens.ToArray();
                        trial[k] = kitchens[k] with { Chefs = [.. kitchens[k].Chefs, chef] };
                        var score = Score(trial, plan.Menu, plan.Drinks);
                        if (best is null || score.BetterThan(best.Value.Score)) best = (k, chef, score);
                    }
                }
                if (best is not { } b) break;
                kitchens[b.Kitchen] = kitchens[b.Kitchen] with { Chefs = [.. kitchens[b.Kitchen].Chefs, b.Chef] };
                spare.Remove(b.Chef);
                changed = true;
            }
            if (!changed) return plan;
            // The old menu with the added chefs scores at least what it did; keep whichever menu is better now.
            var kept = Score(kitchens, plan.Menu, plan.Drinks);
            var (menu, drinks) = BestMenu(kitchens, plan.Drinks, plan.Menu);
            var chosen = Score(kitchens, menu, drinks);
            return chosen.BetterThan(kept) ? (kitchens, menu, drinks, chosen) : (kitchens, plan.Menu, plan.Drinks, kept);
        }

        /// <summary>
        /// The last improvement: line-ups one change away from the plan (a chef replaced by one not
        /// placed, taken out, or two chefs of different kitchens swapped), each judged quickly with the
        /// plan's menu (<see cref="QuickScore"/>); the best <see cref="LineUpsTried"/> get a full menu
        /// search, and the best that beats the plan is taken, until none does. This finds a better
        /// chef and dish changed together, which choosing chefs and menu in turn can't.
        /// </summary>
        public (Kitchen[] Kitchens, int[] Menu, int[] Drinks, InnPlanScore Score) Polish((Kitchen[] Kitchens, int[] Menu, int[] Drinks, InnPlanScore Score) plan)
        {
            for (int round = 0; round < MaxPolishRounds; round++)
            {
                var neighbours = Neighbours(plan.Kitchens)
                    .Select(k => (Kitchens: k, Quick: Better(QuickScore(k, plan.Menu, plan.Drinks), OneDishChanged(k, plan.Menu, plan.Drinks))))
                    .OrderByDescending(x => x.Quick, Comparer<InnPlanScore>.Create((a, b) => a.BetterThan(b) ? 1 : b.BetterThan(a) ? -1 : 0))
                    .Take(LineUpsTried)
                    .ToList();
                var best = plan;
                foreach (var (kitchens, _) in neighbours)
                {
                    var (menu, drinks) = BestMenu(kitchens, plan.Drinks, plan.Menu);
                    var score = Score(kitchens, menu, drinks);
                    if (score.BetterThan(best.Score)) best = (kitchens, menu, drinks, score);
                }
                if (ReferenceEquals(best.Kitchens, plan.Kitchens)) break;
                plan = best;
            }
            return plan;
        }

        private static InnPlanScore Better(InnPlanScore a, InnPlanScore b) => b.BetterThan(a) ? b : a;

        /// <summary>The best score of <paramref name="menu"/> or a valid menu one dish added, removed or swapped from it.</summary>
        private InnPlanScore OneDishChanged(IReadOnlyList<Kitchen> kitchens, int[] menu, int[] drinks)
        {
            var score = Scorer(kitchens, drinks);
            var best = score(menu);
            var outside = Enumerable.Range(0, Dishes.Count).Where(i => !menu.Contains(i)).ToList();
            void Try(List<int> candidate)
            {
                if (Valid(candidate)) best = Better(best, score(candidate));
            }
            if (menu.Length < Rules.MaxDishes)
                foreach (int add in outside) Try([.. menu, add]);
            if (menu.Length > Rules.MinDishes)
                foreach (int remove in menu) Try([.. menu.Where(i => i != remove)]);
            foreach (int remove in menu)
                foreach (int add in outside) Try([.. menu.Where(i => i != remove), add]);
            return best;
        }

        /// <summary>Rounds of <see cref="Polish"/> at most.</summary>
        private const int MaxPolishRounds = 6;

        private IEnumerable<Kitchen[]> Neighbours(Kitchen[] kitchens)
        {
            var used = kitchens.SelectMany(k => k.Chefs).Select(c => c.Id).ToHashSet();
            // One spare chef per profile: the others of that profile change nothing.
            var spare = _profiles.Select(p => p.Operators.FirstOrDefault(o => !used.Contains(o.Id))).OfType<OperatorInfo>().ToList();
            Kitchen[] With(int k, IEnumerable<OperatorInfo> chefs)
            {
                var copy = kitchens.ToArray();
                copy[k] = kitchens[k] with { Chefs = [.. chefs] };
                return copy;
            }
            for (int k = 0; k < kitchens.Length; k++)
            {
                var chefs = kitchens[k].Chefs;
                for (int i = 0; i < chefs.Count; i++)
                {
                    yield return With(k, chefs.Where((_, j) => j != i));
                    foreach (var chef in spare) yield return With(k, chefs.Select((c, j) => j == i ? chef : c));
                }
                if (chefs.Count < _input.Kitchens[k].Places)
                    foreach (var chef in spare) yield return With(k, [.. chefs, chef]);
            }
            for (int a = 0; a < kitchens.Length; a++)
            {
                for (int b = a + 1; b < kitchens.Length; b++)
                {
                    for (int i = 0; i < kitchens[a].Chefs.Count; i++)
                    {
                        for (int j = 0; j < kitchens[b].Chefs.Count; j++)
                        {
                            var copy = kitchens.ToArray();
                            copy[a] = kitchens[a] with { Chefs = [.. kitchens[a].Chefs.Select((c, x) => x == i ? kitchens[b].Chefs[j] : c)] };
                            copy[b] = kitchens[b] with { Chefs = [.. kitchens[b].Chefs.Select((c, x) => x == j ? kitchens[a].Chefs[i] : c)] };
                            yield return copy;
                        }
                    }
                }
            }
        }
    }
}
