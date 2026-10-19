using EndHud.Vision.Operators;

namespace EndHud.Vision.Inn;

/// <summary>What the setup panel suggests for one kitchen.</summary>
/// <param name="Chefs">The plan's chefs for it; none when it isn't used (it can't cook, and nothing else is suggested for it).</param>
/// <param name="ChefSubstitutes">The best chefs to put in instead of one the player lacks, best first (at most <see cref="InnSuggester.MaxSubstitutes"/>).</param>
/// <param name="Dishes">The menu's dishes this kitchen cooks the larger share of, in the plan's order.</param>
/// <param name="DishSubstitutes">Off-menu dishes that would do best here, best first, for a dish the player lacks.</param>
/// <param name="Types">The dish types of <paramref name="Dishes"/>, the most dishes first.</param>
public sealed record KitchenSuggestion(int Number, IReadOnlyList<OperatorInfo> Chefs, IReadOnlyList<OperatorInfo> ChefSubstitutes,
    IReadOnlyList<MenuDish> Dishes, IReadOnlyList<MenuDish> DishSubstitutes, IReadOnlyList<DishType> Types)
{
    /// <summary>Places already taken by a chef the player put in (kept by the plan).</summary>
    public int Placed { get; init; }

    /// <summary>Every place is taken already: nothing to suggest for its chefs.</summary>
    public bool Full { get; init; }

    /// <summary>No chef is planned or placed here: the kitchen can't cook.</summary>
    public bool Unused => Chefs.Count == 0;
}

/// <summary>The setup panel's suggestions: per kitchen, then the drinks.</summary>
/// <param name="Unplaced">Menu dishes no kitchen cooks (only when none can: no chef anywhere).</param>
/// <param name="Drinks">The plan's drinks.</param>
/// <param name="DrinkSubstitutes">The best off-menu drinks, best first, for a drink the player lacks.</param>
public sealed record InnSuggestions(IReadOnlyList<KitchenSuggestion> Kitchens, IReadOnlyList<MenuDish> Unplaced,
    IReadOnlyList<MenuDrink> Drinks, IReadOnlyList<MenuDrink> DrinkSubstitutes);

/// <summary>
/// The setup panel's suggestions from a plan (owner, 2026-10-19: "split this to each kitchen: in
/// kitchen 1, show suggested cook and suggested dish"). The plan is worked out from the whole
/// built-in roster and dish list, so a player who lacks a chef, dish or drink it names puts in a
/// substitute; each is ranked by what it's worth under the planner's model:
/// <list type="bullet">
/// <item><b>A kitchen's dishes:</b> each menu dish under the kitchen that cooks the larger share of
/// it, the kitchens' loads balanced (<see cref="InnPlanner.MainKitchens"/>): every menu dish under
/// exactly one kitchen.</item>
/// <item><b>Chef substitutes:</b> what a chef adds in a kitchen is the plan's income with them in
/// one of its places (the plan's other chefs and menu kept) less with a cook of no bonus there, the
/// better of its places. Handed out across the kitchens best first, at most
/// <see cref="MaxSubstitutes"/> each, a chef to one kitchen only; never a chef the plan uses. A
/// kitchen left with none takes the best of those listed elsewhere (nothing else is left).</item>
/// <item><b>Dish substitutes:</b> off-menu dishes by value per second cooked in that kitchen (price
/// with the 热门 bonus over its cook time there), handed out like the chefs; of the dish types that
/// kitchen cooks first (a dish put in for one of them keeps the menu's types), then any.</item>
/// <item><b>Drink substitutes:</b> the best off-menu drinks by value.</item>
/// </list>
/// Nothing with no worth (adding nothing, or no value) is suggested.
/// </summary>
public static class InnSuggester
{
    /// <summary>The most substitutes in a row: the panel stays short.</summary>
    public const int MaxSubstitutes = 3;

    /// <summary>A cook with no bonus: what a place is worth without a chef's bonuses.</summary>
    private static readonly OperatorInfo PlainCook = new("", "", "", "");

    /// <summary>The suggestions for <paramref name="plan"/>, worked out for <paramref name="input"/> (the plan's input).</summary>
    public static InnSuggestions Of(InnPlanner planner, InnPlanInput input, InnPlan plan)
    {
        var inn = planner.Inn;
        var hot = input.HotTraits.ToHashSet();
        var kitchens = plan.Kitchens.OrderBy(k => k.Number).Select(k => new Kitchen(k.Number, k.Chefs)).ToArray();

        // Each menu dish to the kitchen cooking most of it.
        var main = planner.MainKitchens(input, kitchens, plan.Dishes);
        var dishesOf = kitchens.ToDictionary(k => k.Number, k => plan.Dishes.Where((_, i) => main[i] == k.Number).ToList());
        var unplaced = plan.Dishes.Where((_, i) => main[i] is null).ToList();

        // What each chef not in the plan adds in each kitchen used.
        var planned = kitchens.SelectMany(k => k.Chefs).Select(c => c.Id).ToHashSet();
        var cooks = input.Chefs.Where(o => !inn.ChefBonuses.NotChefs.Contains(o.Id) && !planned.Contains(o.Id)).DistinctBy(o => o.Id).ToList();
        var chefWorth = new List<(int Kitchen, OperatorInfo Item, double Worth)>();
        for (int k = 0; k < kitchens.Length; k++)
        {
            if (!KitchenMath.Cooks(kitchens[k])) continue;
            int places = input.Kitchens.FirstOrDefault(p => p.Number == kitchens[k].Number)?.Places ?? kitchens[k].Chefs.Count;
            foreach (var chef in cooks)
            {
                double worth = Enumerable.Range(0, Math.Max(1, places)).Max(place => Adds(planner, input, plan, kitchens, k, place, chef));
                if (worth > InnPlanner.Tolerance) chefWorth.Add((kitchens[k].Number, chef, worth));
            }
        }
        var used = kitchens.Where(KitchenMath.Cooks).Select(k => k.Number).ToList();
        var chefSubs = HandOut(used, [chefWorth], c => c.Id);

        // Off-menu dishes by value per second in each kitchen, its own dish types first.
        var onMenu = plan.Dishes.Select(d => d.Dish.Id).ToHashSet();
        var offMenu = input.Dishes.Where(d => d.Stock is not <= 0 && !onMenu.Contains(d.Dish.Id)).DistinctBy(d => d.Dish.Id).ToList();
        var sameType = new List<(int Kitchen, MenuDish Item, double Worth)>();
        var anyType = new List<(int Kitchen, MenuDish Item, double Worth)>();
        foreach (var kitchen in kitchens.Where(k => KitchenMath.Cooks(k) && dishesOf[k.Number].Count > 0))
        {
            var types = dishesOf[kitchen.Number].Select(d => d.Dish.Type).ToHashSet();
            foreach (var dish in offMenu)
            {
                double perSecond = InnPlanner.Value(hot, dish.Dish) / KitchenMath.CookSeconds(inn, kitchen, dish.Dish, input.Rating);
                if (perSecond <= 0) continue;
                (types.Contains(dish.Dish.Type) ? sameType : anyType).Add((kitchen.Number, dish, perSecond));
            }
        }
        var dishSubs = HandOut([.. kitchens.Where(k => dishesOf[k.Number].Count > 0).Select(k => k.Number)], [sameType, anyType], d => d.Dish.Id);

        var onList = plan.Drinks.Select(d => d.Drink.Id).ToHashSet();
        var drinkSubs = input.Drinks.Where(d => d.Stock is not <= 0 && !onList.Contains(d.Drink.Id)).DistinctBy(d => d.Drink.Id)
            .Select(d => (Drink: d, Value: InnPlanner.Value(hot, d.Drink))).Where(d => d.Value > 0)
            .OrderByDescending(d => d.Value).ThenBy(d => d.Drink.Drink.Id, StringComparer.Ordinal)
            .Take(MaxSubstitutes).Select(d => d.Drink).ToList();

        var suggestions = kitchens.Select(k => KitchenMath.Cooks(k)
            ? new KitchenSuggestion(k.Number, k.Chefs, chefSubs.GetValueOrDefault(k.Number) ?? [], dishesOf[k.Number],
                dishSubs.GetValueOrDefault(k.Number) ?? [], Types(dishesOf[k.Number]))
            : new KitchenSuggestion(k.Number, [], [], [], [], [])).ToList();
        return new InnSuggestions(suggestions, unplaced, plan.Drinks, drinkSubs);
    }

    /// <summary>The dish types of <paramref name="dishes"/>, the most dishes first (then in the order they come).</summary>
    private static List<DishType> Types(IReadOnlyList<MenuDish> dishes) =>
        [.. dishes.Select((d, i) => (d.Dish.Type, i)).GroupBy(x => x.Type).OrderByDescending(g => g.Count()).ThenBy(g => g.Min(x => x.i)).Select(g => g.Key)];

    /// <summary>
    /// Substitutes handed out across <paramref name="kitchens"/>: the candidates of each pool in turn,
    /// best worth first, at most <see cref="MaxSubstitutes"/> a kitchen and each item to one kitchen
    /// only. A kitchen left with none then takes its best even if listed elsewhere (nothing else is left).
    /// </summary>
    private static Dictionary<int, List<T>> HandOut<T>(IReadOnlyList<int> kitchens, IReadOnlyList<List<(int Kitchen, T Item, double Worth)>> pools, Func<T, string> id)
    {
        var lists = kitchens.ToDictionary(k => k, _ => new List<T>());
        var taken = new HashSet<string>();
        foreach (var pool in pools)
        {
            foreach (var (kitchen, item, _) in Ranked(pool, id))
            {
                if (!lists.TryGetValue(kitchen, out var list) || list.Count >= MaxSubstitutes || taken.Contains(id(item))) continue;
                list.Add(item);
                taken.Add(id(item));
            }
        }
        foreach (int kitchen in kitchens.Where(k => lists[k].Count == 0))
            lists[kitchen].AddRange(pools.SelectMany(p => Ranked(p, id)).Where(c => c.Kitchen == kitchen).Select(c => c.Item)
                .DistinctBy(id).Take(MaxSubstitutes));
        return lists;
    }

    private static IEnumerable<(int Kitchen, T Item, double Worth)> Ranked<T>(IEnumerable<(int Kitchen, T Item, double Worth)> pool, Func<T, string> id) =>
        pool.OrderByDescending(c => c.Worth).ThenBy(c => c.Kitchen).ThenBy(c => id(c.Item), StringComparer.Ordinal);

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
}
