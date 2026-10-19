using EndHud.Vision.Inn;
using EndHud.Vision.Operators;
using Xunit;
using Xunit.Abstractions;

namespace EndHud.Vision.Tests;

/// <summary>
/// The setup panel's tier lists (<see cref="InnTierList"/>) from a plan over the whole built-in
/// roster and dish list, on 养身心 (Rating 4, hot traits 素食 and 滋补, at least two of those dishes):
/// the plan's own chefs, dishes and drinks are T1, and every other name sits in the tier its worth
/// under the planner's model puts it.
/// </summary>
public class InnTiersTests(ITestOutputHelper output)
{
    private static InnData Inn => InnTestData.Inn;

    private static readonly InnPlanner Planner = new(Inn);

    private static readonly Lazy<(InnPlanInput Input, InnPlan Plan, InnTiers Tiers)> Truly = new(() =>
    {
        var input = new InnPlanInput(["Veggy", "Rich"], 4, 0, InnState.MaxTables,
            [new PlanKitchen(1, 2), new PlanKitchen(2, 2), new PlanKitchen(3, 2)], InnTestData.Library.Operators,
            [.. Inn.Dishes.Select(d => new MenuDish(d, null))], [.. Inn.Drinks.Select(d => new MenuDrink(d, null))],
            new MenuRules(6, 8, 3, 8, 3, [new DishRule(2, ["Veggy", "Rich"])]));
        var plan = Planner.Plan(input);
        return (input, plan, InnTierList.Of(Planner, input, plan));
    });

    private static double Adds(InnPlanInput input, InnPlan plan, int kitchen, OperatorInfo chef)
    {
        var kitchens = plan.Kitchens.Select(k => new Kitchen(k.Number, k.Chefs)).ToArray();
        int k = Array.FindIndex(kitchens, x => x.Number == kitchen);
        var plain = new OperatorInfo("", "", "", "");
        return Enumerable.Range(0, 2).Max(place =>
        {
            double With(OperatorInfo cook)
            {
                var chefs = kitchens[k].Chefs.ToList();
                while (chefs.Count <= place) chefs.Add(plain);
                chefs[place] = cook;
                var trial = kitchens.ToArray();
                trial[k] = kitchens[k] with { Chefs = chefs };
                return Planner.Evaluate(input, trial, plan.Dishes, plan.Drinks).Income;
            }
            return With(chef) - With(plain);
        });
    }

    [Fact]
    public void Each_kitchens_planned_chefs_are_its_T1_and_every_other_tier_holds_what_its_share_says()
    {
        var (input, plan, tiers) = Truly.Value;

        Assert.Equal(plan.Kitchens.Select(k => k.Number), tiers.Kitchens.Select(k => k.Number));
        foreach (var kitchen in tiers.Kitchens)
        {
            var planned = plan.Kitchens.Single(k => k.Number == kitchen.Number).Chefs;
            Assert.Equal(planned.Select(c => c.Id), kitchen.Tiers[0].Take(planned.Count).Select(c => c.Id));
            double best = kitchen.Tiers.SelectMany(t => t).Max(c => Adds(input, plan, kitchen.Number, c));
            var bounds = new[] { (InnTierList.Tier1, double.MaxValue), (InnTierList.Tier2, InnTierList.Tier1), (InnTierList.Tier3, InnTierList.Tier2) };
            for (int t = 0; t < 3; t++)
            {
                var others = kitchen.Tiers[t].Where(c => !planned.Any(p => p.Id == c.Id)).ToList();
                Assert.True(others.Count <= InnTierList.MaxPerTier);
                foreach (var chef in others)
                {
                    double share = Adds(input, plan, kitchen.Number, chef) / best;
                    Assert.InRange(share, bounds[t].Item1 - 1e-9, bounds[t].Item2);
                }
            }
            // No chef the plan puts in another kitchen, nor Si or the Endministrator, nor anyone twice.
            var listed = kitchen.Tiers.SelectMany(t => t).Select(c => c.Id).ToList();
            Assert.Equal(listed.Count, listed.Distinct().Count());
            Assert.DoesNotContain(listed, id => plan.Kitchens.Where(k => k.Number != kitchen.Number).SelectMany(k => k.Chefs).Any(c => c.Id == id));
            Assert.DoesNotContain(listed, id => Inn.ChefBonuses.NotChefs.Contains(id));
            output.WriteLine($"kitchen {kitchen.Number} ({kitchen.Role}): " + string.Join(" | ", kitchen.Tiers.Select(t => string.Join(" ", t.Select(c => c.Id)))));
        }
    }

    [Fact]
    public void The_menus_T1_is_the_plan_and_meets_every_rule_and_the_rest_rank_by_value_per_kitchen_second()
    {
        var (input, plan, tiers) = Truly.Value;

        Assert.Equal(plan.Dishes.Select(d => d.Dish.Id), tiers.Dishes[0].Select(d => d.Dish.Id));
        Assert.True(Planner.MeetsRules(input.Rules, tiers.Dishes[0], tiers.Drinks[0]));
        var others = tiers.Dishes.Skip(1).SelectMany(t => t).ToList();
        Assert.NotEmpty(others);
        Assert.DoesNotContain(others, d => plan.Dishes.Any(p => p.Dish.Id == d.Dish.Id));
        Assert.All(tiers.Dishes.Skip(1), t => Assert.True(t.Count <= InnTierList.MaxPerTier));
        Assert.Equal(plan.Drinks.Select(d => d.Drink.Id), tiers.Drinks[0].Select(d => d.Drink.Id));
        output.WriteLine("dishes: " + string.Join(" | ", tiers.Dishes.Select(t => string.Join(" ", t.Select(d => d.Dish.Id)))));
        output.WriteLine("drinks: " + string.Join(" | ", tiers.Drinks.Select(t => string.Join(" ", t.Select(d => d.Drink.Id)))));
    }

    [Fact]
    public void A_kitchens_role_is_the_menus_dish_type_its_chefs_cook_fastest()
    {
        var (input, plan, tiers) = Truly.Value;

        foreach (var kitchen in tiers.Kitchens)
        {
            var chefs = new Kitchen(kitchen.Number, plan.Kitchens.Single(k => k.Number == kitchen.Number).Chefs);
            var speed = plan.Dishes.GroupBy(d => d.Dish.Type).ToDictionary(g => g.Key, g => g.Average(d => KitchenMath.SpeedPercent(Inn, chefs, d.Dish, input.Rating)));
            if (kitchen.Role is { } role) Assert.Equal(speed.Values.Max(), speed[role]);
            else Assert.All(speed.Values, v => Assert.Equal(0, v));
        }
    }

    [Fact]
    public void A_kitchen_where_no_chef_adds_anything_lists_only_its_own()
    {
        var plain = new OperatorInfo("Plain", "Plain", "Plain", "");
        var input = new InnPlanInput([], 4, 0, InnState.MaxTables, [new PlanKitchen(1, 1)], [plain],
            [new MenuDish(InnTestData.Dish("肉包子"), null)], [], new MenuRules(1, 1, 0, 8, 1, []));
        var plan = Planner.Plan(input);

        var tiers = InnTierList.Of(Planner, input, plan);

        Assert.Equal(["Plain"], tiers.Kitchens.Single().Tiers[0].Select(c => c.Id));
        Assert.Empty(tiers.Kitchens.Single().Tiers[1]);
        Assert.Null(tiers.Kitchens.Single().Role);
    }
}
