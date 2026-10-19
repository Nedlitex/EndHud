using EndHud.Vision.Inn;
using EndHud.Vision.Operators;
using Xunit;
using Xunit.Abstractions;

namespace EndHud.Vision.Tests;

/// <summary>
/// The pre-session planner (<see cref="InnPlanner"/>): chefs into kitchens and the menu, under the
/// menu rules, for 1–3 kitchens of 1–2 places; checked against brute force on small cases and against
/// the owner's own line-up in test_yuanyuan2.
/// </summary>
public class InnPlannerTests(ITestOutputHelper output)
{
    private const int Rating = 4;
    private const int SiQuality = 5;
    private const int Tables = 6;

    private static InnData Inn => InnTestData.Inn;

    private static readonly InnPlanner Planner = new(InnTestData.Inn);

    /// <summary>Rating 4 as the menu screen shows it: ≥3 types, 6–8 dishes, 3–8 drinks.</summary>
    private static MenuRules Rating4(params DishRule[] dishRules) => new(6, 8, 3, 8, 3, dishRules);

    private static DishRule VeggyOrRich => new(2, ["Veggy", "Rich"]);

    private static IReadOnlyList<OperatorInfo> Roster => InnTestData.Library.Operators;

    private static IReadOnlyList<MenuDish> AllDishes => [.. Inn.Dishes.Select(d => new MenuDish(d, null))];

    private static IReadOnlyList<MenuDrink> AllDrinks => [.. Inn.Drinks.Select(d => new MenuDrink(d, null))];

    private static InnPlanInput Input(IReadOnlyList<PlanKitchen> kitchens, IReadOnlyList<OperatorInfo>? chefs = null,
        IReadOnlyList<MenuDish>? dishes = null, IReadOnlyList<MenuDrink>? drinks = null, MenuRules? rules = null,
        IReadOnlyList<string>? hot = null, int tables = Tables) =>
        new(hot ?? [], Rating, SiQuality, tables, kitchens, chefs ?? Roster, dishes ?? AllDishes, drinks ?? AllDrinks, rules ?? Rating4());

    private static IReadOnlyList<PlanKitchen> Kitchens(int count, int places) => [.. Enumerable.Range(1, count).Select(n => new PlanKitchen(n, places))];

    private static OperatorInfo Plain(string id) => new(id, id, id, id + ".png");

    private static IReadOnlyList<Kitchen> AsKitchens(InnPlan plan) => [.. plan.Kitchens.Select(k => new Kitchen(k.Number, k.Chefs))];

    /// <summary>Every rule on screen holds, and only what was offered is used.</summary>
    private static void AssertValid(InnPlanInput input, InnPlan plan)
    {
        Assert.True(plan.RulesMet);
        Assert.True(Planner.MeetsRules(input.Rules, plan.Dishes, plan.Drinks));
        Assert.All(plan.Dishes, d => Assert.Contains(input.Dishes, m => m.Dish.Id == d.Dish.Id && m.Stock is not 0));
        Assert.All(plan.Drinks, d => Assert.Contains(input.Drinks, m => m.Drink.Id == d.Drink.Id && m.Stock is not 0));
        var chefs = plan.Kitchens.SelectMany(k => k.Chefs).ToList();
        Assert.Equal(chefs.Count, chefs.Select(c => c.Id).Distinct().Count());
        Assert.All(chefs, c => Assert.Contains(input.Chefs, o => o.Id == c.Id));
        Assert.All(chefs, c => Assert.DoesNotContain(c.Id, Inn.ChefBonuses.NotChefs));
        Assert.Equal(input.Kitchens.Select(k => k.Number), plan.Kitchens.Select(k => k.Number));
        Assert.All(plan.Kitchens, k => Assert.True(k.Chefs.Count <= input.Kitchens.Single(p => p.Number == k.Number).Places));
    }

    public static TheoryData<int, int> KitchenShapes() => new() { { 1, 1 }, { 1, 2 }, { 2, 1 }, { 2, 2 }, { 3, 1 }, { 3, 2 } };

    [Theory]
    [MemberData(nameof(KitchenShapes))]
    public void Every_kitchen_count_and_place_count_gets_a_valid_plan(int kitchens, int places)
    {
        var input = Input(Kitchens(kitchens, places), rules: Rating4(VeggyOrRich), hot: ["Veggy", "Rich"]);

        var plan = Planner.Plan(input);

        AssertValid(input, plan);
        Assert.All(plan.Kitchens, k => Assert.Equal(places, k.Chefs.Count)); // plenty of chefs: every place filled
        Assert.True(plan.Score.Income > 0);
        output.WriteLine($"{kitchens}x{places}: {Describe(plan)}");
    }

    [Fact]
    public void More_kitchens_never_earn_less()
    {
        double previous = 0;
        for (int kitchens = 1; kitchens <= KitchenMath.MaxKitchens; kitchens++)
        {
            double income = Planner.Plan(Input(Kitchens(kitchens, 2))).Score.Income;
            Assert.True(income >= previous - InnPlanner.Tolerance, $"{kitchens} kitchens earn {income:F0}, fewer earned {previous:F0}");
            previous = income;
        }
    }

    [Fact]
    public void Fewer_chefs_than_places_staff_fewer_kitchens()
    {
        var chefs = new[] { InnTestData.Chef("Perlica"), InnTestData.Chef("Avywenna") };
        var input = Input(Kitchens(3, 2), chefs);

        var plan = Planner.Plan(input);

        AssertValid(input, plan);
        Assert.Equal(2, plan.Kitchens.Sum(k => k.Chefs.Count));
        // Two kitchens of one chef cook more than one of two (an empty kitchen doesn't cook).
        Assert.Equal(2, plan.Kitchens.Count(k => k.Chefs.Count == 1));
        var unstaffed = Assert.Single(plan.Reasons, r => r.Kind == PlanReasonKind.KitchenUnstaffed);
        Assert.Equal(plan.Kitchens.Single(k => k.Chefs.Count == 0).Number, unstaffed.Kitchen);
    }

    [Fact]
    public void Si_and_the_Endministrator_never_cook()
    {
        var chefs = new[] { InnTestData.Chef("Si"), InnTestData.Chef("Endministrator"), InnTestData.Chef("Perlica") };
        var input = Input(Kitchens(2, 2), chefs);

        var plan = Planner.Plan(input);

        AssertValid(input, plan);
        Assert.Equal(["Perlica"], plan.Kitchens.SelectMany(k => k.Chefs).Select(c => c.Id));
    }

    [Fact]
    public void Never_a_dish_or_drink_not_offered_or_out_of_stock()
    {
        // The dearest dishes and drinks out of stock; only the Rating 1–3 dishes offered otherwise.
        var dishes = Inn.Dishes.Where(d => d.Unlock.Rating is <= 3).Select(d => new MenuDish(d, null))
            .Append(new MenuDish(InnTestData.Dish("界龙肉卷"), 0)).Append(new MenuDish(InnTestData.Dish("福寿全"), 0)).ToList();
        var drinks = Inn.Drinks.Select(d => new MenuDrink(d, d.Price >= 60 ? 0 : null)).ToList();
        var input = Input(Kitchens(3, 2), dishes: dishes, drinks: drinks);

        var plan = Planner.Plan(input);

        AssertValid(input, plan);
        Assert.DoesNotContain(plan.Dishes, d => d.Dish.Zh is "界龙肉卷" or "福寿全");
        Assert.DoesNotContain(plan.Drinks, d => d.Drink.Price >= 60);
    }

    /// <summary>
    /// 海陆宴 (Ocean &amp; Land) prefers 肉食 and 水产 (C8): such dishes pay 10 % more each and go on the
    /// menu whenever that pays. Checked both ways for every kitchen shape, with every dish offered and
    /// with the dishes the owner has: a menu kept off them must not earn more than the plan, and when
    /// it earns less, the plan has them.
    /// </summary>
    [Theory]
    [MemberData(nameof(KitchenShapes))]
    public void Ocean_and_Land_puts_meaty_and_finny_dishes_on_the_menu_when_they_pay(int kitchens, int places)
    {
        var theme = Inn.Themes.Single(t => t.Zh == "海陆宴");
        foreach (var offered in new[] { AllDishes, OwnersDishes })
        {
            var input = Input(Kitchens(kitchens, places), dishes: offered, hot: InnPlanner.HotTraitsOf([], theme));

            var plan = Planner.Plan(input);
            output.WriteLine(Describe(plan));

            AssertValid(input, plan);
            foreach (string trait in new[] { "Meaty", "Finny" })
            {
                var without = Planner.Plan(input with { Dishes = [.. offered.Where(d => !d.Dish.Traits.Contains(trait))] });
                output.WriteLine($"  without {trait}: {Describe(without)}");
                Assert.False(without.Score.BetterThan(plan.Score), $"kept off {trait}, the menu earns {without.Score.Income:F0} > {plan.Score.Income:F0}");
                if (plan.Score.BetterThan(without.Score)) Assert.Contains(plan.Dishes, d => d.Dish.Traits.Contains(trait));
            }
            // With every dish offered, 界龙肉卷 (肉食, the dearest dish) always pays.
            if (offered == AllDishes) Assert.Contains(plan.Dishes, d => d.Dish.Traits.Contains("Meaty"));
            // Each dish on the menu that matches a 热门 trait says so.
            foreach (var dish in plan.Dishes.Where(d => d.Dish.Traits.Intersect(input.HotTraits).Any()))
                Assert.Contains(plan.Reasons, r => r.Kind == PlanReasonKind.HotTraits && r.Id == dish.Dish.Id);
        }
    }

    /// <summary>
    /// The 热门 bonus decides between two dishes alike in price, time and kitchen: 金丝小贝 (肉食) and
    /// 八宝饭 (美观), both 80 and 35 s, in a kitchen with no bonus, one dish on the menu.
    /// </summary>
    [Theory]
    [InlineData("Meaty", "金丝小贝")]
    [InlineData("Lovely", "八宝饭")]
    public void A_hot_trait_decides_between_dishes_alike(string hot, string expected)
    {
        var offered = new[] { new MenuDish(InnTestData.Dish("金丝小贝"), null), new MenuDish(InnTestData.Dish("八宝饭"), null) };
        var input = Input(Kitchens(1, 1), [Plain("Plain")], offered, rules: new MenuRules(1, 1, 0, 8, 1, []), hot: [hot]);

        var plan = Planner.Plan(input);

        Assert.Equal(expected, Assert.Single(plan.Dishes).Dish.Zh);
    }

    /// <summary>养身心's rule (≥2 dishes with 素食 or 滋补) holds whatever is offered, also when such dishes don't pay.</summary>
    [Fact]
    public void Truly_Wholesomes_rule_always_holds()
    {
        var random = new Random(20261018);
        var veggyOrRich = Inn.Dishes.Where(d => d.Traits.Contains("Veggy") || d.Traits.Contains("Rich")).ToList();
        for (int run = 0; run < 12; run++)
        {
            // A random offer: a few dishes with the traits (the slow cheap kind first), the rest at random.
            var offered = veggyOrRich.OrderBy(d => d.Price).Take(2 + run % 3)
                .Concat(Inn.Dishes.Except(veggyOrRich).OrderBy(_ => random.Next()).Take(6 + run % 10))
                .Select(d => new MenuDish(d, null)).ToList();
            var chefs = Roster.OrderBy(_ => random.Next()).Take(1 + run % 6).ToList();
            var input = Input(Kitchens(1 + run % 3, 1 + run % 2), chefs, offered, rules: Rating4(VeggyOrRich), hot: ["Veggy", "Rich"]);

            var plan = Planner.Plan(input);

            AssertValid(input, plan);
            Assert.True(plan.Dishes.Count(d => d.Dish.Traits.Intersect(["Veggy", "Rich"]).Any()) >= 2, Describe(plan));
        }
    }

    [Fact]
    public void A_rule_no_menu_can_meet_is_said()
    {
        var offered = Inn.Dishes.Where(d => !d.Traits.Contains("Veggy") && !d.Traits.Contains("Rich")).Select(d => new MenuDish(d, null)).ToList();
        var input = Input(Kitchens(2, 2), dishes: offered, rules: Rating4(VeggyOrRich));

        var plan = Planner.Plan(input);

        Assert.False(plan.RulesMet);
        Assert.Contains(plan.Reasons, r => r is { Kind: PlanReasonKind.RuleUnmet, Rule: 0 });
    }

    [Fact]
    public void The_menu_rule_on_the_setup_screen_is_read()
    {
        var rule = DishRule.Parse("至少有2道带有[素食]或[滋补]特性的菜品", Inn);

        Assert.NotNull(rule);
        Assert.Equal(2, rule.Count);
        Assert.Equal(["Veggy", "Rich"], rule.AnyOf);
        Assert.Null(DishRule.Parse("开启2次满堂彩", Inn));
    }

    /// <summary>
    /// The model on numbers worked out by hand: 肉包子 (12, 15 s) alone, no drinks. One plain kitchen
    /// cooks 300 / 15 = 20 dishes, 1.2 a order: 16.7 orders, 240 income; two kitchens double it, three
    /// triple it; with one table the guests limit it (300 / 34 s a guest = 8.8 orders).
    /// </summary>
    [Theory]
    [InlineData(1, Tables, 240.0, true)]
    [InlineData(2, Tables, 480.0, true)]
    [InlineData(3, Tables, 720.0, true)]
    [InlineData(1, 1, 300.0 / InnPlanner.MinGuestCycleSeconds * 1.2 * 12, false)]
    public void The_model_on_numbers_worked_out_by_hand(int kitchens, int tables, double income, bool kitchenBound)
    {
        var input = Input(Kitchens(kitchens, 1), tables: tables);
        var plain = Enumerable.Range(1, kitchens).Select(n => new Kitchen(n, [Plain("Plain" + n)])).ToList();

        var score = Planner.Evaluate(input, plain, [new MenuDish(InnTestData.Dish("肉包子"), null)], []);

        Assert.Equal(income, score.Income, 6);
        Assert.Equal(kitchenBound, score.KitchenBound);
    }

    /// <summary>
    /// A dish short of stock earns only what can be cooked: 肉包子 with 5 left, where one kitchen
    /// would serve 20 (16.7 orders × 1.2), earns 5 × 12; the plan says it runs short.
    /// </summary>
    [Fact]
    public void A_dish_short_of_stock_earns_only_its_stock()
    {
        var input = Input(Kitchens(1, 1), [Plain("Plain")], [new MenuDish(InnTestData.Dish("肉包子"), 5)], [],
            new MenuRules(1, 1, 0, 0, 1, []));

        var score = Planner.Evaluate(input, [new Kitchen(1, [Plain("Plain")])], input.Dishes, []);
        var plan = Planner.Plan(input);

        Assert.Equal(60, score.Income, 6);
        var short_ = Assert.Single(plan.Reasons, r => r.Kind == PlanReasonKind.ShortOfStock);
        Assert.Equal("meat-baozi", short_.Id);
        Assert.Equal(15, short_.Value, 6);
    }

    /// <summary>Two dishes, one kitchen fast at each: the loads balance (each dish where it's fast).</summary>
    [Fact]
    public void The_kitchens_share_the_dishes_each_where_it_cooks_fastest()
    {
        // 菜肴 +10 % per Lifestyle chef, 汤羹 +10 % per Nature chef; 番茄炒蛋 is a side, 酸辣汤 a soup.
        var sides = new Kitchen(1, [Plain("A") with { Hobbies = ["Lifestyle"] }]);
        var soups = new Kitchen(2, [Plain("B") with { Hobbies = ["Nature"] }]);
        var menu = new[] { new MenuDish(InnTestData.Dish("番茄炒蛋"), null), new MenuDish(InnTestData.Dish("酸辣汤"), null) };
        var input = Input(Kitchens(2, 1));

        var score = Planner.Evaluate(input, [sides, soups], menu, []);

        // Each dish half the orders' dishes: side 14 s in kitchen 1, soup 37 s in kitchen 2. Balanced,
        // kitchen 1 also takes some soup: 0.6 dishes an order at 14 s and x of the 0.6 soups at 40 s
        // there, the rest at 37 s: 8.4 + 24x = 22.2(1 - x) → x = 13.8 / 46.2, 15.57 s an order.
        double perOrder = 0.6 * 14 + 0.6 * (13.8 / 46.2) * 40;
        Assert.Equal(300 / perOrder, score.Orders, 3);
    }

    /// <summary>
    /// The planner's search against every line-up and every menu, on cases small enough to try all:
    /// 2–3 kitchens of 1–2 places, 4–5 chefs, 6 dishes, menus of 3–4.
    /// </summary>
    [Theory]
    [InlineData(2, 1, 0)]
    [InlineData(2, 2, 1)]
    [InlineData(3, 1, 2)]
    [InlineData(1, 2, 3)]
    [InlineData(3, 2, 4)]
    [InlineData(2, 2, 5)]
    public void The_search_finds_the_best_plan_on_small_cases(int kitchens, int places, int seed)
    {
        var random = new Random(seed);
        var chefs = Roster.Where(o => !Inn.ChefBonuses.NotChefs.Contains(o.Id)).OrderBy(_ => random.Next()).Take(4 + seed % 2).ToList();
        var dishes = Inn.Dishes.OrderBy(_ => random.Next()).Take(6).Select(d => new MenuDish(d, null)).ToList();
        var drinks = Inn.Drinks.OrderBy(_ => random.Next()).Take(3).Select(d => new MenuDrink(d, null)).ToList();
        var rules = new MenuRules(3, 4, 1, 2, 2, seed % 2 == 0 ? [new DishRule(1, ["Sweet", "Salty"])] : []);
        var hot = seed % 3 == 0 ? new[] { "Sweet" } : [];
        var input = Input(Kitchens(kitchens, places), chefs, dishes, drinks, rules, hot);

        var plan = Planner.Plan(input);
        var best = BruteForce(input);

        AssertValid(input, plan);
        output.WriteLine($"planner {plan.Score.Income:F2} ({plan.Score.ChoiceDishes:F3} choice), brute force {best.Income:F2} ({best.ChoiceDishes:F3})");
        Assert.Equal(best.Income, plan.Score.Income, 6);
        Assert.False(best.BetterThan(plan.Score));
    }

    /// <summary>Every way to place the chefs (any place may stay empty) × every valid menu × every valid drink list.</summary>
    private static InnPlanScore BruteForce(InnPlanInput input)
    {
        var best = InnPlanScore.Zero;
        var menus = Subsets(input.Dishes, input.Rules.MinDishes, input.Rules.MaxDishes).ToList();
        var drinkLists = Subsets(input.Drinks, input.Rules.MinDrinks, input.Rules.MaxDrinks).ToList();
        foreach (var kitchens in LineUps(input))
            foreach (var menu in menus)
                foreach (var drinks in drinkLists)
                {
                    if (!Planner.MeetsRules(input.Rules, menu, drinks)) continue;
                    var score = Planner.Evaluate(input, kitchens, menu, drinks);
                    if (score.BetterThan(best)) best = score;
                }
        return best;
    }

    private static IEnumerable<IReadOnlyList<T>> Subsets<T>(IReadOnlyList<T> items, int min, int max)
    {
        for (int mask = 0; mask < 1 << items.Count; mask++)
        {
            int count = System.Numerics.BitOperations.PopCount((uint)mask);
            if (count >= min && count <= max) yield return [.. items.Where((_, i) => (mask & 1 << i) != 0)];
        }
    }

    private static IEnumerable<IReadOnlyList<Kitchen>> LineUps(InnPlanInput input)
    {
        var places = input.Kitchens.SelectMany(k => Enumerable.Repeat(k.Number, k.Places)).ToList();
        var chosen = new OperatorInfo?[places.Count];
        var used = new HashSet<string>();
        IEnumerable<IReadOnlyList<Kitchen>> Fill(int place)
        {
            if (place == places.Count)
            {
                yield return [.. input.Kitchens.Select(k => new Kitchen(k.Number,
                    [.. places.Select((n, i) => (n, i)).Where(p => p.n == k.Number && chosen[p.i] is not null).Select(p => chosen[p.i]!)]))];
                yield break;
            }
            chosen[place] = null;
            foreach (var lineUp in Fill(place + 1)) yield return lineUp;
            foreach (var chef in input.Chefs.Where(c => !used.Contains(c.Id)))
            {
                chosen[place] = chef;
                used.Add(chef.Id);
                foreach (var lineUp in Fill(place + 1)) yield return lineUp;
                used.Remove(chef.Id);
            }
            chosen[place] = null;
        }
        return Fill(0);
    }

    // test_yuanyuan2 (养身心, Rating 4, 6 tables): kitchen 1 佩丽卡 + 艾维文娜, kitchen 2 陈千语,
    // kitchen 3 empty (setup-chefs-placed). The menu's eight dishes as kitchen 1's 派单 panel lists
    // them (KitchenMathTests); the drinks chosen aren't on any frame, so the owner's line-up is
    // scored with the planner's drinks (drinks need no kitchen: the comparison is chefs and dishes).
    private static readonly string[] OwnersMenu =
        ["melon-meatball-soup", "berry-sweet-sip", "berry-fruity-toss", "spoon-swimmer", "clay-jar-meat", "fin-mato-soup", "meat-baozi", "yuanyuan-veggy-rice"];

    /// <summary>The dishes the owner has, as far as the recordings show: every dish a rating unlocks and every card seen.</summary>
    private static IReadOnlyList<MenuDish> OwnersDishes =>
    [
        .. Inn.Dishes.Where(d => d.Unlock.Rating is <= Rating || OwnersMenu.Contains(d.Id) || d.Id is "mala-xiang-guo" or "roll-of-fin-supreme")
            .Select(d => new MenuDish(d, null)),
    ];

    /// <summary>The eight drinks on test_yuanyuan2's drinks tab, all 库存∞.</summary>
    private static IReadOnlyList<MenuDrink> OwnersDrinks =>
    [
        .. new[] { "瓶装霞莓饮", "潮前龙井", "陈酿葡萄露", "瓶装优质兽奶", "游人酒", "梨花香茶", "瓶装脆蔗汁", "玉米汁" }
            .Select(zh => new MenuDrink(InnTestData.Drink(zh), null)),
    ];

    public static TheoryData<bool> RosterKinds() => [true, false];

    /// <param name="wholeRoster">Every operator (the picker can't be read yet), or only the three the owner placed.</param>
    [Theory]
    [MemberData(nameof(RosterKinds))]
    public void The_owners_line_up_scores_no_more_than_the_plan(bool wholeRoster)
    {
        var theme = Inn.Themes.Single(t => t.Zh == "养身心");
        var chefs = wholeRoster ? Roster : [InnTestData.Chef("Perlica"), InnTestData.Chef("Avywenna"), InnTestData.Chef("Chen_Qianyu")];
        var input = Input(Kitchens(3, 2), chefs, OwnersDishes, OwnersDrinks, Rating4(VeggyOrRich), InnPlanner.HotTraitsOf(["Veggy", "Rich"], theme));
        var owners = new[] { InnTestData.Kitchen(1, "Perlica", "Avywenna"), InnTestData.Kitchen(2, "Chen_Qianyu"), InnTestData.EmptyKitchen(3) };
        var ownersMenu = OwnersMenu.Select(id => new MenuDish(Inn.Dishes.Single(d => d.Id == id), null)).ToList();

        var plan = Planner.Plan(input);
        Assert.True(Planner.MeetsRules(input.Rules, ownersMenu, plan.Drinks));
        var ownersScore = Planner.Evaluate(input, owners, ownersMenu, plan.Drinks);

        AssertValid(input, plan);
        output.WriteLine($"owner: {ownersScore.Income:F0} income, {ownersScore.Orders:F1} orders, {ownersScore.ChoiceDishes:F2} choice dishes");
        // For the report: the same, were an empty kitchen to cook at base speed (unknown; the planner assumes not).
        var ifEmptyCooked = Planner.Evaluate(input, [owners[0], owners[1], new Kitchen(3, [Plain("Nobody")])], ownersMenu, plan.Drinks);
        output.WriteLine($"owner, if kitchen 3 cooked without a chef: {ifEmptyCooked.Income:F0} income, {ifEmptyCooked.Orders:F1} orders");
        output.WriteLine($"plan:  {Describe(plan)}");
        Assert.False(ownersScore.BetterThan(plan.Score));
        // The owner's chefs in the owner's kitchens with the planner's menu score no more than the plan either.
        Assert.False(Planner.Evaluate(input, owners, plan.Dishes, plan.Drinks).BetterThan(plan.Score));
    }

    /// <summary>The opening (C16): every kitchen that cooks gets dishes in every free slot, as many as the counter holds.</summary>
    [Theory]
    [MemberData(nameof(KitchenShapes))]
    public void The_plan_opens_with_every_kitchen_cooking(int kitchens, int places)
    {
        var plan = Planner.Plan(Input(Kitchens(kitchens, places)));

        Assert.NotNull(plan.Opening);
        int cooking = plan.Kitchens.Count(k => k.Chefs.Count > 0);
        Assert.Equal(cooking, plan.Opening.Kitchens.Count);
        Assert.Equal(Math.Min(cooking * KitchenQueue.Slots, InnCounter.Capacity), plan.Opening.Kitchens.Sum(k => k.Queue.Count));
        Assert.All(plan.Opening.Kitchens.SelectMany(k => k.Queue), q => Assert.Contains(plan.Dishes, d => d.Dish.Id == q.Dish.Id));
    }

    private static string Describe(InnPlan plan) =>
        $"{plan.Score.Income:F0} income, {plan.Score.Orders:F1} orders, {plan.Score.ChoiceDishes:F2} choice dishes; " +
        string.Join("; ", plan.Kitchens.Select(k => $"k{k.Number} {string.Join("+", k.Chefs.Select(c => c.Id))}")) +
        $"; dishes {string.Join(", ", plan.Dishes.Select(d => d.Dish.Id))}; drinks {string.Join(", ", plan.Drinks.Select(d => d.Drink.Id))}";
}
