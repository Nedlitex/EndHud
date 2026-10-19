using EndHud.Vision.Operators;

namespace EndHud.Vision.Inn;

/// <summary>A kitchen's tier list: what the plan has it cook and its chefs, best tier first.</summary>
/// <param name="Role">The dish type it's best at among the menu's (its chefs' speed bonus, on average); null when no type is faster there.</param>
/// <param name="Tiers">T1, T2, T3: each the chefs for this kitchen, best first; a tier may be empty.</param>
public sealed record KitchenTiers(int Number, DishType? Role, IReadOnlyList<IReadOnlyList<OperatorInfo>> Tiers);

/// <summary>The setup panel's tier lists: per kitchen, the menu's dishes and the drinks (each T1 first).</summary>
/// <param name="Dishes">T1 is the plan's menu (it meets every rule); T2 and T3 the dishes to put in place of one the player lacks.</param>
/// <param name="Drinks">T1 the plan's drinks; T2 and T3 the next best.</param>
public sealed record InnTiers(IReadOnlyList<KitchenTiers> Kitchens, IReadOnlyList<IReadOnlyList<MenuDish>> Dishes,
    IReadOnlyList<IReadOnlyList<MenuDrink>> Drinks);

/// <summary>
/// Tier lists from a plan (owner, 2026-10-19), so a player who doesn't have a chef or dish the plan
/// names puts in one of the same tier. The plan is worked out from the whole built-in roster and
/// dish list; the tiers say how much each alternative is worth under the planner's model:
/// <list type="bullet">
/// <item><b>A kitchen's chefs:</b> what a chef adds there is the income with them in one of its
/// places (the plan's other chefs and menu kept) less the income with a cook of no bonus in that
/// place, the better of its places. Ranked against the best chef there: T1 at least
/// <see cref="Tier1"/> of it, T2 <see cref="Tier2"/>, T3 <see cref="Tier3"/>. The plan's own chefs
/// are T1. A chef the plan puts in another kitchen isn't listed.</item>
/// <item><b>Dishes:</b> T1 is the plan's menu. The others by value per kitchen-second (price with
/// the theme's 热门 bonus, over its cook time in the kitchen fastest at it), against the best of
/// the menu: T2 at least <see cref="Tier2"/>, T3 <see cref="Tier3"/>.</item>
/// <item><b>Drinks:</b> T1 the plan's; the others by value against the best of them, as dishes.</item>
/// </list>
/// Each tier after T1's plan is capped at <see cref="MaxPerTier"/> so a kitchen's line stays short.
/// </summary>
public static class InnTierList
{
    /// <summary>T1: at least this share of the best.</summary>
    public const double Tier1 = 0.90;

    /// <summary>T2: at least this share of the best.</summary>
    public const double Tier2 = 0.75;

    /// <summary>T3: at least this share of the best.</summary>
    public const double Tier3 = 0.50;

    /// <summary>The most names in a tier beyond the plan's own.</summary>
    public const int MaxPerTier = 3;

    /// <summary>A cook with no bonus: what a place is worth without a chef's bonuses.</summary>
    private static readonly OperatorInfo PlainCook = new("", "", "", "");

    /// <summary>The tier lists for <paramref name="plan"/>, worked out for <paramref name="input"/> (the plan's input).</summary>
    public static InnTiers Of(InnPlanner planner, InnPlanInput input, InnPlan plan)
    {
        var inn = planner.Inn;
        var hot = input.HotTraits.ToHashSet();
        var kitchens = plan.Kitchens.OrderBy(k => k.Number).Select(k => new Kitchen(k.Number, k.Chefs)).ToArray();
        var cooks = input.Chefs.Where(o => !inn.ChefBonuses.NotChefs.Contains(o.Id)).DistinctBy(o => o.Id).ToList();

        var kitchenTiers = new List<KitchenTiers>();
        for (int k = 0; k < kitchens.Length; k++)
        {
            int places = input.Kitchens.FirstOrDefault(p => p.Number == kitchens[k].Number)?.Places ?? kitchens[k].Chefs.Count;
            var planned = kitchens[k].Chefs;
            var elsewhere = kitchens.Where((_, i) => i != k).SelectMany(x => x.Chefs).Select(c => c.Id).ToHashSet();
            var worth = new Dictionary<string, double>();
            foreach (var chef in cooks.Where(c => !elsewhere.Contains(c.Id)))
                worth[chef.Id] = Enumerable.Range(0, Math.Max(1, places)).Max(place => Adds(planner, input, plan, kitchens, k, place, chef));
            double best = worth.Count == 0 ? 0 : worth.Values.Max();
            IReadOnlyList<IReadOnlyList<OperatorInfo>> tiers = best <= InnPlanner.Tolerance
                // No chef adds anything here: the plan's, and no ranking.
                ? [planned, [], []]
                : Rank(cooks.Where(c => worth.ContainsKey(c.Id)).ToList(), c => worth[c.Id] / best, planned, c => c.Id);
            kitchenTiers.Add(new KitchenTiers(kitchens[k].Number, Role(inn, kitchens[k], plan.Dishes, input.Rating), tiers));
        }

        var cooking = kitchens.Where(KitchenMath.Cooks).ToList();
        double PerSecond(Dish d) => cooking.Count == 0 ? 0 : InnPlanner.Value(hot, d) / cooking.Min(k => KitchenMath.CookSeconds(inn, k, d, input.Rating));
        var dishes = input.Dishes.Where(d => d.Stock is not <= 0).DistinctBy(d => d.Dish.Id).ToList();
        double bestDish = plan.Dishes.Count == 0 ? 0 : plan.Dishes.Max(d => PerSecond(d.Dish));
        IReadOnlyList<IReadOnlyList<MenuDish>> dishTiers = bestDish <= 0
            ? [plan.Dishes, [], []]
            : Rank(dishes, d => PerSecond(d.Dish) / bestDish, plan.Dishes, d => d.Dish.Id, planIsTier1: true);

        var drinks = input.Drinks.Where(d => d.Stock is not <= 0).DistinctBy(d => d.Drink.Id).ToList();
        double bestDrink = plan.Drinks.Count == 0 ? 0 : plan.Drinks.Max(d => InnPlanner.Value(hot, d.Drink));
        IReadOnlyList<IReadOnlyList<MenuDrink>> drinkTiers = bestDrink <= 0
            ? [plan.Drinks, [], []]
            : Rank(drinks, d => InnPlanner.Value(hot, d.Drink) / bestDrink, plan.Drinks, d => d.Drink.Id, planIsTier1: true);

        return new InnTiers(kitchenTiers, dishTiers, drinkTiers);
    }

    /// <summary>
    /// What <paramref name="chef"/> adds in <paramref name="place"/> of kitchen <paramref name="k"/>:
    /// the plan's income with them there less with a cook of no bonus there.
    /// </summary>
    private static double Adds(InnPlanner planner, InnPlanInput input, InnPlan plan, Kitchen[] kitchens, int k, int place, OperatorInfo chef)
    {
        double With(OperatorInfo cook)
        {
            var chefs = kitchens[k].Chefs.ToList();
            while (chefs.Count <= place) chefs.Add(PlainCook);
            chefs[place] = cook;
            var trial = kitchens.ToArray();
            trial[k] = kitchens[k] with { Chefs = chefs };
            return planner.Evaluate(input, trial, plan.Dishes, plan.Drinks).Income;
        }
        return With(chef) - With(PlainCook);
    }

    /// <summary>
    /// Three tiers by share of the best: <paramref name="planned"/> always first in T1 (the plan's
    /// own), then the rest best first, each tier past the plan's capped at <see cref="MaxPerTier"/>.
    /// With <paramref name="planIsTier1"/>, T1 is the plan's alone and the others start at T2.
    /// </summary>
    private static IReadOnlyList<IReadOnlyList<T>> Rank<T>(IReadOnlyList<T> all, Func<T, double> share, IReadOnlyList<T> planned, Func<T, string> id,
        bool planIsTier1 = false)
    {
        var plannedIds = planned.Select(id).ToHashSet();
        var others = all.Where(x => !plannedIds.Contains(id(x)))
            .Select(x => (Item: x, Share: share(x)))
            .OrderByDescending(x => x.Share).ThenBy(x => id(x.Item), StringComparer.Ordinal)
            .ToList();
        List<T> Take(double from, double below) =>
            [.. others.Where(x => x.Share >= from && x.Share < below).Take(MaxPerTier).Select(x => x.Item)];
        if (planIsTier1)
            return [planned, Take(Tier2, double.MaxValue), Take(Tier3, Tier2)];
        return [[.. planned, .. Take(Tier1, double.MaxValue)], Take(Tier2, Tier1), Take(Tier3, Tier2)];
    }

    /// <summary>The menu's dish type this kitchen cooks fastest, on average over the menu's dishes of each type; null when none is faster there.</summary>
    private static DishType? Role(InnData inn, Kitchen kitchen, IReadOnlyList<MenuDish> menu, int rating)
    {
        if (!KitchenMath.Cooks(kitchen)) return null;
        var byType = menu.GroupBy(d => d.Dish.Type)
            .Select(g => (Type: g.Key, Speed: g.Average(d => KitchenMath.SpeedPercent(inn, kitchen, d.Dish, rating))))
            .OrderByDescending(x => x.Speed).ThenBy(x => x.Type)
            .ToList();
        return byType.Count > 0 && byType[0].Speed > 0 ? byType[0].Type : null;
    }
}
