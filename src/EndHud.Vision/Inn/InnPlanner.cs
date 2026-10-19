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

    /// <summary>
    /// Whether a kitchen with no chef cooks. Not known: an assumption, the cautious side. The owner's
    /// 养身心 run left kitchen 3 empty, which the plan reads as "using fewer kitchens".
    /// </summary>
    public static readonly bool EmptyKitchenCooks = false;

    /// <summary>Line-ups the beam keeps at each chef place.</summary>
    public const int BeamWidth = 12;

    /// <summary>The best line-ups of a beam search each get their own menu.</summary>
    public const int LineUpsTried = 4;

    /// <summary>Rounds of chefs-then-menu at most.</summary>
    private const int MaxRounds = 4;

    /// <summary>A cook with no bonus: a kitchen at base speed (a place the beam hasn't filled yet).</summary>
    private static readonly OperatorInfo PlainCook = new("", "", "", "");

    public InnData Inn { get; } = inn;

    /// <summary>The traits to value: those read off the screen, else the theme's preferences.</summary>
    public static IReadOnlyList<string> HotTraitsOf(IReadOnlyList<string> read, InnTheme? theme) =>
        read.Count > 0 ? read : theme?.Prefers ?? [];

    /// <summary>The 热门 traits <paramref name="dish"/> matches: its traits and its type.</summary>
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
                var (m, d) = search.BestMenu(kitchens, drinks);
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

        var (finalKitchens, finalMenu, finalDrinks, finalScore) = search.FillEmptyPlaces(best!.Value);
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

    private static bool Cooks(Kitchen kitchen) => EmptyKitchenCooks || kitchen.Chefs.Count > 0;

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
        private readonly List<(OperatorInfo Representative, List<OperatorInfo> Operators)> _profiles;

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
                .Select(g => (g.First(), g.ToList()))];
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
        public (int[] Menu, int[] Drinks) BestMenu(IReadOnlyList<Kitchen> kitchens, int[] drinks)
        {
            var menu = BestDishes(kitchens, drinks);
            var newDrinks = BestDrinks(kitchens, menu);
            if (!newDrinks.SequenceEqual(drinks)) menu = BestDishes(kitchens, newDrinks);
            return (menu, newDrinks);
        }

        private int[] BestDishes(IReadOnlyList<Kitchen> kitchens, int[] drinks)
        {
            var cache = new Dictionary<UInt128, InnPlanScore>();
            InnPlanScore ScoreOf(List<int> menu)
            {
                UInt128 key = 0;
                foreach (int i in menu) key |= (UInt128)1 << i;
                if (Dishes.Count > 128 || !cache.TryGetValue(key, out var score))
                {
                    score = Score(kitchens, menu, drinks);
                    if (Dishes.Count <= 128) cache[key] = score;
                }
                return score;
            }

            List<int>? best = null;
            InnPlanScore? bestScore = null;
            int smallest = Math.Max(1, Rules.MinDishes), largest = Math.Min(Rules.MaxDishes, Dishes.Count);
            for (int size = smallest; size <= largest; size++)
            {
                var menu = Build(size, ScoreOf);
                Improve(menu, ScoreOf);
                var score = ScoreOf(menu);
                bool valid = Valid(menu);
                bool bestValid = best is not null && Valid(best);
                if (best is null || (valid && !bestValid) || (valid == bestValid && score.BetterThan(bestScore!)))
                    (best, bestScore) = (menu, score);
            }
            return [.. (best ?? []).Order()];
        }

        /// <summary>First the dishes that bring the menu closest to its rules (the best paying of those), then the best paying, up to <paramref name="size"/>.</summary>
        private List<int> Build(int size, Func<List<int>, InnPlanScore> score)
        {
            var menu = new List<int>();
            while (menu.Count < size)
            {
                int shortfall = Shortfall(menu);
                int pick = -1;
                (int Shortfall, InnPlanScore Score)? best = null;
                for (int i = 0; i < Dishes.Count; i++)
                {
                    if (menu.Contains(i)) continue;
                    menu.Add(i);
                    var candidate = (Shortfall: Shortfall(menu), Score: score(menu));
                    menu.RemoveAt(menu.Count - 1);
                    bool better = best is null
                        || (shortfall > 0 && candidate.Shortfall < best.Value.Shortfall)
                        || ((shortfall == 0 || candidate.Shortfall == best.Value.Shortfall) && candidate.Score.BetterThan(best.Value.Score));
                    if (better) (pick, best) = (i, candidate);
                }
                if (pick < 0) break;
                menu.Add(pick);
            }
            return menu;
        }

        /// <summary>Add, remove or swap one dish, the best move each time, while the menu stays valid and pays more.</summary>
        private void Improve(List<int> menu, Func<List<int>, InnPlanScore> score)
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
                        var score = Score(Materialize(candidate, kitchen), menu, drinks);
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
            var (menu, drinks) = BestMenu(kitchens, plan.Drinks);
            var chosen = Score(kitchens, menu, drinks);
            return chosen.BetterThan(kept) ? (kitchens, menu, drinks, chosen) : (kitchens, plan.Menu, plan.Drinks, kept);
        }
    }
}
