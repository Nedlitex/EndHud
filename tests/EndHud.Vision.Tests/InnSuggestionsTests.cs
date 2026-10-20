using EndHud.Vision.Inn;
using EndHud.Vision.Operators;
using Xunit;
using Xunit.Abstractions;

namespace EndHud.Vision.Tests;

/// <summary>
/// The setup advice's suggestions (<see cref="InnSuggester"/>) from a plan over the whole built-in
/// roster and dish list: which kitchen cooks each menu dish (the balanced split of the planner's
/// model, <see cref="InnPlanner.Shares"/>), and per kitchen the substitutes for its chefs and
/// dishes, then the drinks'.
/// </summary>
public class InnSuggestionsTests(ITestOutputHelper output)
{
    private static InnData Inn => InnTestData.Inn;

    private static readonly InnPlanner Planner = new(Inn);

    private static InnPlanInput Input(IReadOnlyList<string> hot, IReadOnlyList<DishRule> rules, int kitchens = 3, int places = 2) =>
        new(hot, 4, 0, InnState.MaxTables, [.. Enumerable.Range(1, kitchens).Select(n => new PlanKitchen(n, places))], InnTestData.Library.Operators,
            [.. Inn.Dishes.Select(d => new MenuDish(d, null))], [.. Inn.Drinks.Select(d => new MenuDrink(d, null))],
            new MenuRules(6, 8, 3, 8, 3, rules)) { Preferred = [] };

    private static readonly Lazy<(InnPlanInput Input, InnPlan Plan, InnSuggestions Suggestions)> Truly = new(() =>
    {
        var input = Input(["Veggy", "Rich"], [new DishRule(2, ["Veggy", "Rich"])]);
        var plan = Planner.Plan(input);
        return (input, plan, InnSuggester.Of(Planner, input, plan));
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

    // ---- The split of each dish among the kitchens ----

    /// <summary>Two kitchens, each twice as fast at one of two dishes: each cooks its own.</summary>
    [Fact]
    public void Each_kitchen_cooks_the_dish_it_is_fastest_at_when_that_balances()
    {
        var shares = InnPlanner.Shares(new double[,] { { 10, 20 }, { 20, 10 } });

        Assert.Equal(new double[,] { { 1, 0 }, { 0, 1 } }, shares);
    }

    /// <summary>One dish, two equal kitchens: half each.</summary>
    [Fact]
    public void A_dish_both_kitchens_cook_alike_is_halved()
    {
        var shares = InnPlanner.Shares(new double[,] { { 15 }, { 15 } });

        Assert.Equal(0.5, shares[0, 0], 9);
        Assert.Equal(0.5, shares[1, 0], 9);
    }

    /// <summary>
    /// On random cook times for 2–3 kitchens and 1–8 dishes: every dish fully shared out, and the
    /// busiest kitchen's load is the balance the planner scores (<see cref="InnPlanner.Balance"/>).
    /// </summary>
    [Theory]
    [InlineData(2, 1)]
    [InlineData(2, 5)]
    [InlineData(2, 8)]
    [InlineData(3, 2)]
    [InlineData(3, 6)]
    [InlineData(3, 8)]
    public void The_split_reaches_the_balance(int kitchens, int dishes)
    {
        var random = new Random(kitchens * 100 + dishes);
        for (int trial = 0; trial < 50; trial++)
        {
            var seconds = new double[kitchens, dishes];
            for (int k = 0; k < kitchens; k++)
            for (int d = 0; d < dishes; d++)
                // The game's cook times (15–40 s) at speeds 0–150 %, some equal on purpose.
                seconds[k, d] = Math.Round(new[] { 15, 25, 35, 40 }[random.Next(4)] / (1 + random.Next(4) * 0.5), 1);

            var shares = InnPlanner.Shares(seconds);

            double busiest = 0;
            for (int k = 0; k < kitchens; k++)
            {
                double load = 0;
                for (int d = 0; d < dishes; d++)
                {
                    Assert.InRange(shares[k, d], -1e-9, 1 + 1e-9);
                    load += shares[k, d] * seconds[k, d] / dishes;
                }
                busiest = Math.Max(busiest, load);
            }
            for (int d = 0; d < dishes; d++)
                Assert.Equal(1, Enumerable.Range(0, kitchens).Sum(k => shares[k, d]), 6);
            Assert.Equal(InnPlanner.Balance(seconds), busiest, 6);
        }
    }

    [Fact]
    public void Every_menu_dish_is_under_exactly_one_kitchen()
    {
        var (_, plan, suggestions) = Truly.Value;

        Assert.Equal(plan.Kitchens.Select(k => k.Number), suggestions.Kitchens.Select(k => k.Number));
        Assert.Equal(plan.Dishes.Select(d => d.Dish.Id).Order(), suggestions.Kitchens.SelectMany(k => k.Dishes).Select(d => d.Dish.Id).Order());
        Assert.Empty(suggestions.Unplaced);
        foreach (var kitchen in suggestions.Kitchens)
        {
            output.WriteLine($"kitchen {kitchen.Number} ({string.Join("/", kitchen.Types)}): chefs {string.Join(" ", kitchen.Chefs.Select(c => c.Id))}"
                + $" | {string.Join(" ", kitchen.ChefSubstitutes.Select(c => c.Id))}; dishes {string.Join(" ", kitchen.Dishes.Select(d => d.Dish.Id))}"
                + $" | {string.Join(" ", kitchen.DishSubstitutes.Select(d => d.Dish.Id))}");
            Assert.Equal(kitchen.Dishes.Select(d => d.Dish.Type).Distinct().Order(), kitchen.Types.Order());
        }
    }

    // ---- Substitutes ----

    [Fact]
    public void Chef_substitutes_are_the_best_left_for_each_kitchen_never_planned_never_twice()
    {
        var (input, plan, suggestions) = Truly.Value;
        var planned = plan.Kitchens.SelectMany(k => k.Chefs).Select(c => c.Id).ToHashSet();
        var listed = suggestions.Kitchens.SelectMany(k => k.ChefSubstitutes).Select(c => c.Id).ToList();

        Assert.Equal(listed.Count, listed.Distinct().Count());
        Assert.DoesNotContain(listed, planned.Contains);
        Assert.DoesNotContain(listed, id => Inn.ChefBonuses.NotChefs.Contains(id));
        foreach (var kitchen in suggestions.Kitchens)
        {
            Assert.Equal(plan.Kitchens.Single(k => k.Number == kitchen.Number).Chefs, kitchen.Chefs);
            Assert.InRange(kitchen.ChefSubstitutes.Count, 1, InnSuggester.MaxSubstitutes);
            var worth = kitchen.ChefSubstitutes.Select(c => Adds(input, plan, kitchen.Number, c)).ToList();
            Assert.All(worth, w => Assert.True(w > InnPlanner.Tolerance));
            Assert.Equal(worth.OrderByDescending(w => w), worth);
            // No chef left out (planned nowhere, listed nowhere) adds more here than the last one listed.
            double last = worth[^1];
            var left = input.Chefs.Where(c => !planned.Contains(c.Id) && !listed.Contains(c.Id) && !Inn.ChefBonuses.NotChefs.Contains(c.Id));
            Assert.All(left.Take(40), c => Assert.True(Adds(input, plan, kitchen.Number, c) <= last + 1e-9, c.Id));
        }
    }

    [Fact]
    public void Dish_substitutes_are_off_the_menu_never_twice_and_of_the_kitchens_types_first()
    {
        var (_, plan, suggestions) = Truly.Value;
        var listed = suggestions.Kitchens.SelectMany(k => k.DishSubstitutes).Select(d => d.Dish.Id).ToList();

        Assert.Equal(listed.Count, listed.Distinct().Count());
        Assert.DoesNotContain(listed, id => plan.Dishes.Any(d => d.Dish.Id == id));
        foreach (var kitchen in suggestions.Kitchens.Where(k => k.Dishes.Count > 0))
        {
            Assert.InRange(kitchen.DishSubstitutes.Count, 1, InnSuggester.MaxSubstitutes);
            // Its own types first: a dish of another type only after every one of its types.
            var own = kitchen.DishSubstitutes.Select(d => kitchen.Types.Contains(d.Dish.Type)).ToList();
            Assert.Equal(own.OrderByDescending(o => o), own);
        }
    }

    [Fact]
    public void Drink_substitutes_are_the_best_off_the_list()
    {
        var (input, plan, suggestions) = Truly.Value;
        var hot = input.HotTraits.ToHashSet();

        Assert.Equal(plan.Drinks, suggestions.Drinks);
        Assert.InRange(suggestions.DrinkSubstitutes.Count, 1, InnSuggester.MaxSubstitutes);
        Assert.DoesNotContain(suggestions.DrinkSubstitutes, d => plan.Drinks.Any(p => p.Drink.Id == d.Drink.Id));
        double worst = suggestions.DrinkSubstitutes.Min(d => InnPlanner.Value(hot, d.Drink));
        Assert.All(Inn.Drinks.Where(d => !plan.Drinks.Any(p => p.Drink.Id == d.Id) && !suggestions.DrinkSubstitutes.Any(s => s.Drink.Id == d.Id)),
            d => Assert.True(InnPlanner.Value(hot, d) <= worst));
    }

    [Fact]
    public void A_kitchen_without_a_chef_is_unused_and_suggests_nothing()
    {
        var cook = InnTestData.Library.Operators.First(o => Inn.ChefBonusesOf(o).Count > 0);
        var input = new InnPlanInput([], 4, 0, InnState.MaxTables, [new PlanKitchen(1, 1), new PlanKitchen(2, 1)], [cook],
            [.. Inn.Dishes.Select(d => new MenuDish(d, null))], [.. Inn.Drinks.Select(d => new MenuDrink(d, null))], new MenuRules(6, 8, 3, 8, 3, [])) { Preferred = [] };
        var plan = Planner.Plan(input);

        var suggestions = InnSuggester.Of(Planner, input, plan);

        var unused = suggestions.Kitchens.Single(k => k.Number == 2);
        Assert.True(unused.Unused);
        Assert.Empty(unused.Dishes);
        Assert.Empty(unused.DishSubstitutes);
        Assert.Empty(unused.ChefSubstitutes);
        Assert.Equal(plan.Dishes.Select(d => d.Dish.Id), suggestions.Kitchens.Single(k => k.Number == 1).Dishes.Select(d => d.Dish.Id));
    }

    [Fact]
    public void Where_no_other_chef_adds_anything_there_are_no_chef_substitutes()
    {
        var plain = new OperatorInfo("Plain", "Plain", "Plain", "");
        var input = new InnPlanInput([], 4, 0, InnState.MaxTables, [new PlanKitchen(1, 1)], [plain, new OperatorInfo("Other", "Other", "Other", "")],
            [new MenuDish(InnTestData.Dish("肉包子"), null)], [], new MenuRules(1, 1, 0, 8, 1, [])) { Preferred = [] };
        var plan = Planner.Plan(input);

        var suggestions = InnSuggester.Of(Planner, input, plan);

        Assert.Empty(suggestions.Kitchens.Single().ChefSubstitutes);
    }

    // ---- Chefs already placed ----

    private static readonly Lazy<(InnPlanInput Input, InnPlan Plan, InnSuggestions Suggestions)> Placed = new(() =>
    {
        OperatorInfo Op(string id) => InnTestData.Library.Operators.Single(o => o.Id == id);
        var input = Input(["Veggy", "Rich"], [new DishRule(2, ["Veggy", "Rich"])]) with
        {
            Kitchens = [new PlanKitchen(1, 2) { Placed = [Op("Chen_Qianyu")] }, new PlanKitchen(2, 2) { Placed = [Op("Perlica"), Op("Avywenna")] }, new PlanKitchen(3, 2)],
        };
        var plan = Planner.Plan(input);
        return (input, plan, InnSuggester.Of(Planner, input, plan));
    });

    /// <summary>
    /// Kitchen 1 half placed (Chen Qianyu), kitchen 2 full (Perlica, Avywenna), kitchen 3 empty: kitchen 1
    /// gets one chef to put in, kitchen 2 none (it's full, nothing to swap), kitchen 3 two; no placed
    /// chef is suggested anywhere, as a pick or a substitute.
    /// </summary>
    [Fact]
    public void Placed_chefs_are_kept_and_only_empty_places_get_suggestions()
    {
        var (_, _, suggestions) = Placed.Value;
        var byNumber = suggestions.Kitchens.ToDictionary(k => k.Number);
        string[] placed = ["Chen_Qianyu", "Perlica", "Avywenna"];

        Assert.Single(byNumber[1].Chefs);
        Assert.Equal(1, byNumber[1].Placed);
        Assert.False(byNumber[1].Full);
        Assert.NotEmpty(byNumber[1].ChefSubstitutes);

        Assert.True(byNumber[2].Full);
        Assert.Empty(byNumber[2].Chefs);
        Assert.Empty(byNumber[2].ChefSubstitutes);
        Assert.False(byNumber[2].Unused);

        Assert.Equal(2, byNumber[3].Chefs.Count);
        Assert.All(suggestions.Kitchens.SelectMany(k => k.Chefs.Concat(k.ChefSubstitutes)), c => Assert.DoesNotContain(c.Id, placed));
    }
}
