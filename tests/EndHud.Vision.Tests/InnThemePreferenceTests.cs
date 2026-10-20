using EndHud.Vision.Inn;
using EndHud.Vision.Operators;
using Xunit;
using Xunit.Abstractions;

namespace EndHud.Vision.Tests;

/// <summary>
/// A theme's guest preference (owner, live 2026-10-19, theme 养身心: "this theme promotes 滋补, but
/// your recommend dish does not even contain the one with 滋补"). The theme's text says what guests
/// lean towards ("客人更倾向带有[素食]、[滋补]特性的菜品"); guests order those dishes more often
/// (<see cref="InnPlanner.PreferenceWeight"/>, measured with trace-inn), so the planner's model and
/// the simulator draw orders by it, and the menu and the chefs follow what will be ordered.
/// </summary>
public class InnThemePreferenceTests(ITestOutputHelper output)
{
    private static InnData Inn => InnTestData.Inn;

    private static readonly InnPlanner Planner = new(Inn);

    // ---- The theme's text ----

    /// <summary>养身心's text as the setup screen's reading gives it at 4K (InnSetupReaderTests).</summary>
    private const string Wholesome = "几道山珍素食轻灵之气轻抚身心。灶火升温更快，可更快开启“满堂彩\"。客人更倾向带有[素食]、[滋补]特性的菜品。";

    /// <summary>海陆宴's text (InnOceanLandTests): it speaks of choice dishes, not of what guests prefer.</summary>
    private const string OceanLand = "大海与陆地交汇，天地之间无数种巧妙味道就此诞生。珍馐赏钱加成提升，出售珍馐菜品可获得更高收益。";

    [Fact]
    public void The_trait_wording_names_the_traits_in_order()
    {
        Assert.Equal(["Veggy", "Rich"], InnObjective.GuestPreference(Wholesome, Inn));
    }

    [Fact]
    public void The_type_wording_names_the_dish_type()
    {
        // 舌上清流 (the lead's reading of the owner's screen), with and without the type's icon in brackets.
        Assert.Equal(["Soup"], InnObjective.GuestPreference("客人更倾向汤羹类菜品。", Inn));
        Assert.Equal(["Soup"], InnObjective.GuestPreference("客人更倾向[汤羹]类菜品。", Inn));
    }

    [Fact]
    public void A_sentence_cut_short_by_the_reading_is_still_understood()
    {
        // 养身心 at 1440p: the reading ends at "特性的".
        Assert.Equal(["Veggy", "Rich"], InnObjective.GuestPreference("几道山畛素食．轻荬之气轻抚身心灶火升温更快，可更快开启“满堂彩\"。客人更倾向带有[素食]、[滋补]特性的", Inn));
    }

    [Fact]
    public void A_text_naming_no_preference_or_an_unknown_one_gives_none()
    {
        Assert.Null(InnObjective.GuestPreference(OceanLand, Inn));
        Assert.Null(InnObjective.GuestPreference(null, Inn));
        // One bracket naming no trait the data knows: not read as a preference for the other alone.
        Assert.Null(InnObjective.GuestPreference("客人更倾向带有[素食]、[某某]特性的菜品。", Inn));
    }

    /// <summary>
    /// Where the planner's preference comes from: the text when it was read (海陆宴's names none, so
    /// guests pick uniformly); before the text is read, the 热门 chips, else the theme's data.
    /// </summary>
    [Fact]
    public void The_preference_is_the_texts_and_the_hot_chips_only_when_no_text_was_read()
    {
        var wholesome = Inn.Themes.Single(t => t.Zh == "养身心");
        var ocean = Inn.Themes.Single(t => t.Zh == "海陆宴");

        Assert.Equal(["Veggy", "Rich"], InnPlanner.PreferenceOf(Wholesome, [], wholesome, Inn));
        Assert.Empty(InnPlanner.PreferenceOf(OceanLand, ["Meaty", "Finny"], ocean, Inn));
        Assert.Equal(["Soup", "Drink"], InnPlanner.PreferenceOf(null, ["Soup", "Drink"], null, Inn));
        Assert.Equal(["Veggy", "Rich"], InnPlanner.PreferenceOf(null, [], wholesome, Inn));
    }

    // ---- Orders follow the preference ----

    /// <summary>Two dishes alike but for their price and type (15 s each): 缘缘菜饭 (主食, 28) and 番茄炒蛋 (菜肴, 12).</summary>
    private static IReadOnlyList<MenuDish> RiceAndEggs => [new(InnTestData.Dish("缘缘菜饭"), null), new(InnTestData.Dish("番茄炒蛋"), null)];

    /// <summary>Three kitchens at base speed: the guests' clock, not the kitchens, sets the orders.</summary>
    private static IReadOnlyList<Kitchen> ThreePlainKitchens => [InnTestData.PlainKitchen(1), InnTestData.PlainKitchen(2), InnTestData.PlainKitchen(3)];

    private static InnPlanInput Simple(IReadOnlyList<string> preferred) =>
        new([], 4, 0, InnState.MaxTables, [new PlanKitchen(1, 1), new PlanKitchen(2, 1), new PlanKitchen(3, 1)], [],
            RiceAndEggs, [], new MenuRules(1, 8, 0, 8, 1, []))
        {
            Preferred = preferred,
        };

    /// <summary>
    /// The model: a preferred dish gets <see cref="InnPlanner.PreferenceWeight"/> shares of the dishes
    /// ordered to another's one, so the income is the orders × dishes per order × the weighted mean price.
    /// </summary>
    [Fact]
    public void The_model_gives_a_preferred_dish_its_weight_of_the_orders()
    {
        var input = Simple(["Entree"]);
        var score = Planner.Evaluate(input, ThreePlainKitchens, RiceAndEggs, []);

        double w = InnPlanner.PreferenceWeight;
        double expected = score.Orders * InnPlanner.DishesPerOrder * (w * 28 + 12) / (w + 1);
        output.WriteLine($"orders {score.Orders:F2}, income {score.Income:F2}, expected {expected:F2} (uniform: {score.Orders * InnPlanner.DishesPerOrder * 20:F2})");
        Assert.False(score.KitchenBound);
        Assert.Equal(expected, score.Income, 6);
    }

    /// <summary>
    /// The simulator draws each guest's dishes by the same weights: the same guests earn more when they
    /// prefer the dearer dish and less when they prefer the cheaper one than when they pick uniformly.
    /// </summary>
    [Fact]
    public void The_simulators_guests_order_the_preferred_dish_more_often()
    {
        InnSimulatedScore Run(IReadOnlyList<string> preferred) => Planner.Simulate(Simple(preferred), ThreePlainKitchens, RiceAndEggs, []);
        var rice = Run(["Entree"]);
        double uniform = Run([]).Income, eggs = Run(["Side"]).Income;
        output.WriteLine($"guests prefer the rice {rice.Income:F1}, uniform {uniform:F1}, prefer the eggs {eggs:F1}");

        Assert.True(rice.Income > uniform + 1, $"{rice.Income} vs {uniform}");
        Assert.True(uniform > eggs + 1, $"{uniform} vs {eggs}");
        // The rice's share of the dishes served, from the income per dish: about w / (w + 1).
        double riceShare = (rice.Income / (rice.Orders * InnPlanner.DishesPerOrder) - 12) / (28 - 12);
        output.WriteLine($"rice's share of the dishes ~{riceShare:P0}, model {InnPlanner.PreferenceWeight / (InnPlanner.PreferenceWeight + 1):P0}");
        Assert.InRange(riceShare, 0.70, 0.95);
    }

    /// <summary>The kitchens' balance with an order mix: each dish shared out whole, the busiest kitchen at the balance.</summary>
    [Fact]
    public void The_split_reaches_the_balance_for_a_mix()
    {
        var random = new Random(20261019);
        for (int trial = 0; trial < 100; trial++)
        {
            int kitchens = 2 + random.Next(2), dishes = 1 + random.Next(8);
            var seconds = new double[kitchens, dishes];
            for (int k = 0; k < kitchens; k++)
            for (int d = 0; d < dishes; d++)
                seconds[k, d] = Math.Round(new[] { 15, 25, 35, 40 }[random.Next(4)] / (1 + random.Next(4) * 0.5), 1);
            var mix = InnPlanner.OrderShares([.. Enumerable.Range(0, dishes).Select(_ => random.Next(2) == 0 ? 1.0 : InnPlanner.PreferenceWeight)]);

            var shares = InnPlanner.Shares(seconds, mix);

            double busiest = Enumerable.Range(0, kitchens).Max(k => Enumerable.Range(0, dishes).Sum(d => shares[k, d] * seconds[k, d] * mix[d]));
            for (int d = 0; d < dishes; d++) Assert.Equal(1, Enumerable.Range(0, kitchens).Sum(k => shares[k, d]), 6);
            Assert.Equal(InnPlanner.Balance(seconds, mix), busiest, 6);
        }
    }

    /// <summary>A preferred dish weighs more in the balance: a mix leaning on the slow dish costs more kitchen time per dish.</summary>
    [Fact]
    public void A_mix_leaning_on_a_slow_dish_costs_more_kitchen_time()
    {
        var seconds = new double[,] { { 40, 15 } };
        Assert.Equal(27.5, InnPlanner.Balance(seconds), 9);
        double w = InnPlanner.PreferenceWeight;
        Assert.Equal((w * 40 + 15) / (w + 1), InnPlanner.Balance(seconds, InnPlanner.OrderShares([w, 1])), 9);
    }

    // ---- The plan follows what will be ordered ----

    private static readonly Lazy<(InnPlanInput Before, InnPlan PlanBefore, InnPlanInput After, InnPlan PlanAfter)> WholesomePlans = new(() =>
    {
        var before = Wholesome_([]);
        var after = Wholesome_(["Veggy", "Rich"]);
        return (before, Planner.Plan(before), after, Planner.Plan(after));
    });

    /// <summary>
    /// 养身心 with the owner's dishes: the chefs the plan picks cook the preferred dishes on its menu
    /// faster than the chefs of the plan made as if guests picked uniformly (owner: "you are not
    /// promoting cooks that are good at cook dishes with 滋补 trait").
    /// </summary>
    [Fact]
    public void The_chefs_follow_the_preferred_dishes()
    {
        var (before, planBefore, after, planAfter) = WholesomePlans.Value;
        var preferred = after.Preferred.ToHashSet();
        double Seconds(InnPlan plan, Dish dish) => plan.Kitchens.Select(k => new Kitchen(k.Number, k.Chefs)).Where(KitchenMath.Cooks)
            .Min(k => KitchenMath.CookSeconds(Inn, k, dish, 4));
        var dishes = planAfter.Dishes.Select(d => d.Dish).Where(d => InnPlanner.Prefers(preferred, d)).ToList();
        double withAfter = dishes.Sum(d => Seconds(planAfter, d)), withBefore = dishes.Sum(d => Seconds(planBefore, d));
        Show("before", before, planBefore);
        Show("after", after, planAfter);
        output.WriteLine($"preferred dishes {string.Join(" ", dishes.Select(d => d.Zh))}: {withAfter:F1} s with the plan's chefs, {withBefore:F1} s with the uniform plan's");

        Assert.True(planAfter.RulesMet);
        Assert.True(dishes.Count >= 2);
        Assert.True(withAfter < withBefore - 0.5, $"{withAfter} vs {withBefore}");
    }

    /// <summary>
    /// Where guests do prefer (the simulator drawing by the preference), the plan made for it earns more
    /// than the plan made as if they picked uniformly: 养身心 and 舌上清流 with the owner's dishes.
    /// </summary>
    [Theory]
    [InlineData("养身心")]
    [InlineData("舌上清流")]
    public void The_plan_for_the_preference_earns_more_where_guests_prefer(string theme)
    {
        const string SoupRule = "至少有1道[汤羹]类菜品";
        var (before, after) = theme == "养身心"
            ? (Wholesome_([]), Wholesome_(["Veggy", "Rich"]))
            : (Session(["Soup", InnData.Drink], SoupRule, []), Session(["Soup", InnData.Drink], SoupRule, ["Soup"]));
        var planBefore = theme == "养身心" ? WholesomePlans.Value.PlanBefore : Planner.Plan(before);
        var planAfter = theme == "养身心" ? WholesomePlans.Value.PlanAfter : Planner.Plan(after);

        var uniformPlanPlayed = Planner.Simulate(after, [.. planBefore.Kitchens.Select(k => new Kitchen(k.Number, k.Chefs))], planBefore.Dishes, planBefore.Drinks);
        output.WriteLine($"{theme}: plan for the preference {planAfter.Simulated}; the uniform plan where guests prefer {uniformPlanPlayed}");

        Assert.True(planAfter.Simulated!.BetterThan(uniformPlanPlayed), $"{planAfter.Simulated} vs {uniformPlanPlayed}");
    }

    // ---- The setup advice under a preference ----

    /// <summary>
    /// The soup theme's plan for the owner's case (Chen Qianyu placed in kitchen 1; every dish): five
    /// quick dishes and one soup that guests order five times as often, so kitchen 3 holds the larger
    /// share of all five and kitchen 1 of none.
    /// </summary>
    private static (InnPlanInput Input, IReadOnlyList<Kitchen> Kitchens, IReadOnlyList<MenuDish> Dishes) SoupThemeCase(bool secondChefInKitchen1 = true)
    {
        IReadOnlyList<Kitchen> kitchens =
        [
            secondChefInKitchen1 ? InnTestData.Kitchen(1, "Chen_Qianyu", "Zhuang_Fangyi") : InnTestData.Kitchen(1, "Chen_Qianyu"),
            InnTestData.Kitchen(2, "Tangtang", "Fluorite"),
            InnTestData.Kitchen(3, "Yvonne", "Avywenna"),
        ];
        IReadOnlyList<MenuDish> dishes = [.. new[] { "青菜养生粥", "界龙肉卷", "鳞香肉丝", "簇锦豆腐羹", "咸酪脆饼", "红蕊奶酪蛋糕" }.Select(zh => new MenuDish(InnTestData.Dish(zh), null))];
        var input = new InnPlanInput(["Soup", InnData.Drink], 4, 0, InnState.MaxTables,
            [new PlanKitchen(1, 2) { Placed = [InnTestData.Chef("Chen_Qianyu")] }, new PlanKitchen(2, 2), new PlanKitchen(3, 2)], InnTestData.Library.Operators,
            [.. Inn.Dishes.Select(d => new MenuDish(d, null))], [.. Inn.Drinks.Select(d => new MenuDrink(d, null))],
            new MenuRules(6, 8, 3, 8, 3, [new DishRule(1, ["Soup"])]))
        {
            Preferred = ["Soup", InnData.Drink],
        };
        return (input, kitchens, dishes);
    }

    /// <summary>Every kitchen that cooks has a dish to be told (the setup advice's "做…" under it), each dish under one kitchen.</summary>
    [Fact]
    public void Every_cooking_kitchen_is_the_main_kitchen_of_a_dish()
    {
        var (input, kitchens, dishes) = SoupThemeCase();

        var main = Planner.MainKitchens(input, kitchens, dishes);
        output.WriteLine(string.Join(" ", dishes.Select((d, i) => $"{d.Dish.Zh}:{main[i]}")));

        Assert.All(main, m => Assert.NotNull(m));
        Assert.Equal([1, 2, 3], main.Distinct().Order().Select(m => m!.Value));
        // The soup stays with the soup chefs.
        Assert.Equal(2, main[dishes.ToList().FindIndex(d => d.Dish.Type == DishType.Soup)]);
    }

    /// <summary>
    /// Where the guests' clock, not the kitchens, sets the orders, a faster chef earns nothing more under
    /// the model: chef substitutes are still listed, the one saving the most kitchen time first.
    /// </summary>
    [Fact]
    public void Chef_substitutes_are_ranked_by_kitchen_time_when_the_guests_set_the_orders()
    {
        var (input, kitchens, dishes) = SoupThemeCase(secondChefInKitchen1: false);
        var score = Planner.Evaluate(input, kitchens, dishes, []);
        Assert.False(score.KitchenBound);
        var plan = new InnPlan([.. kitchens.Select(k => new PlannedKitchen(k.Number, k.Chefs))], dishes, [], null, score, RulesMet: true, []);

        var suggestions = InnSuggester.Of(Planner, input, plan);

        var subs = suggestions.Kitchens.Single(k => k.Number == 1).ChefSubstitutes;
        double Seconds(OperatorInfo chef) =>
            Planner.KitchenSecondsPerDish(input, [InnTestData.Kitchen(1, "Chen_Qianyu") with { Chefs = [InnTestData.Chef("Chen_Qianyu"), chef] }, kitchens[1], kitchens[2]], dishes);
        output.WriteLine(string.Join(" ", subs.Select(c => $"{c.NameZh} {Seconds(c):F3}")));
        Assert.NotEmpty(subs);
        Assert.True(subs.Count <= InnSuggester.MaxSubstitutes);
        Assert.Equal(subs.Select(Seconds).Order(), subs.Select(Seconds));
        Assert.True(Seconds(subs[0]) < Planner.KitchenSecondsPerDish(input, kitchens, dishes));
    }

    // ---- The owner's sessions: what the plan becomes ----

    /// <summary>The owner's dish cards (test_yuanyuan1's menu screen, 2026-10-18; the same account as the live 养身心), all ∞.</summary>
    internal static readonly string[] OwnersCards =
    [
        "茄果鳞肉汤", "坛子肉", "金丝小贝", "“勺里游”", "白瓜肉丸汤", "麻辣香锅", "至臻鳞肉卷", "果味沙拉", "八宝饭", "莓果甜汤", "酸辣汤", "雪绵豆沙",
        "烤肉串", "豌杂拌面", "酸莓布丁", "酸菜鳞肉", "炸薯条", "鳞肉水饺", "豆沙汤圆", "手抓饭", "咸酪脆饼", "鳞香肉丝", "青菜养生粥", "鲜奶炖蛋",
        "茄汁白瓜", "菌菇蒸饺",
    ];

    /// <summary>The owner's eight drinks (the drinks tab), all ∞.</summary>
    internal static readonly string[] OwnersDrinks = ["瓶装霞莓饮", "潮前龙井", "陈酿葡萄露", "瓶装优质兽奶", "游人酒", "梨花香茶", "瓶装脆蔗汁", "玉米汁"];

    /// <summary>A Rating 4 session with the owner's dishes and drinks, three empty kitchens of two places and the whole roster.</summary>
    internal static InnPlanInput Session(IReadOnlyList<string> hot, string rule, IReadOnlyList<string> preferred) =>
        new(hot, 4, 5, InnState.MaxTables, [new PlanKitchen(1, 2), new PlanKitchen(2, 2), new PlanKitchen(3, 2)], InnTestData.Library.Operators,
            [.. OwnersCards.Select(zh => new MenuDish(InnTestData.Dish(zh), null))], [.. OwnersDrinks.Select(zh => new MenuDrink(InnTestData.Drink(zh), null))],
            new MenuRules(6, 8, 3, 8, 3, DishRule.ParseAll([rule], Inn).Rules))
        {
            Preferred = preferred,
        };

    internal static InnPlanInput Wholesome_(IReadOnlyList<string> preferred) => Session(["Veggy", "Rich"], "至少有2道带有[素食]或[滋补]特性的菜品", preferred);

    private void Show(string name, InnPlanInput input, InnPlan plan)
    {
        var preferred = input.Preferred.ToHashSet();
        output.WriteLine($"{name}: {string.Join(" ", plan.Dishes.Select(d => d.Dish.Zh + (InnPlanner.Prefers(preferred, d.Dish) ? "*" : "")))} | {string.Join(" ", plan.Drinks.Select(d => d.Drink.Zh))}");
        output.WriteLine($"  chefs {string.Join(" / ", plan.Kitchens.Select(k => $"{k.Number}: " + string.Join("+", k.Chefs.Select(c => $"{c.NameZh}({string.Join(",", Inn.ChefBonusesOf(c).Select(b => b.DishType?.ToString() ?? b.Flavor ?? b.Kind.ToString()))})"))))}");
        output.WriteLine($"  simulated {plan.Simulated}; model {plan.Score}");
    }

    /// <summary>
    /// The owner's three themes, planned as if guests picked uniformly and with the theme's preference
    /// (the report's before and after). Both plans meet the rules; 海陆宴, whose text names no
    /// preference, is planned exactly as before.
    /// </summary>
    [Theory]
    [InlineData("养身心")]
    [InlineData("海陆宴")]
    [InlineData("舌上清流")]
    public void Plans_before_and_after_the_preference(string theme)
    {
        var (hot, rule, text) = theme switch
        {
            "养身心" => ((IReadOnlyList<string>)["Veggy", "Rich"], "至少有2道带有[素食]或[滋补]特性的菜品", Wholesome),
            "海陆宴" => (["Meaty", "Finny"], "至少有1道带有[肉食]特性的菜品和1道带有[水产]特性的菜品", OceanLand),
            _ => (["Soup", InnData.Drink], "至少有1道[汤羹]类菜品", "客人更倾向汤羹类菜品。"),
        };
        var before = Session(hot, rule, []);
        var after = Session(hot, rule, InnPlanner.PreferenceOf(text, hot, null, Inn));
        var planBefore = Planner.Plan(before);
        var planAfter = Planner.Plan(after);
        Show($"{theme} before (uniform)", before, planBefore);
        Show($"{theme} after (preferred {string.Join(",", after.Preferred)})", after, planAfter);
        // The plan made without the preference, played where guests do prefer: what it really earns.
        var kitchensBefore = planBefore.Kitchens.Select(k => new Kitchen(k.Number, k.Chefs)).ToList();
        output.WriteLine($"  the uniform plan, played with the preference: {Planner.Simulate(after, kitchensBefore, planBefore.Dishes, planBefore.Drinks)}");

        Assert.True(planBefore.RulesMet && planAfter.RulesMet);
        if (after.Preferred.Count == 0)
        {
            Assert.Equal(planBefore.Dishes.Select(d => d.Dish.Id), planAfter.Dishes.Select(d => d.Dish.Id));
            Assert.Equal(planBefore.Simulated, planAfter.Simulated);
        }
    }
}
