using EndHud.Vision.Inn;
using Xunit;
using Xunit.Abstractions;

namespace EndHud.Vision.Tests;

/// <summary>
/// Theme-matching dishes always first (owner, 2026-10-20: "the point is matching dishes will reward a
/// lot more at the end, so always prefer the matching ones"). A dish matches when one of its traits or
/// its dish type is among the theme's hot items (主题热门) or the theme text's guest preference
/// (客人更倾向…). The menu: every rule first, then as many matching dishes as it can hold (a dish
/// matching two hot items before one matching one), then value; a non-matching dish only where the
/// rules need one (≥3 types, ≥6 dishes). Drinks alike.
/// </summary>
public class InnThemeMatchTests(ITestOutputHelper output)
{
    private static InnData Inn => InnTestData.Inn;

    private static readonly InnPlanner Planner = new(Inn);

    private const string WholesomeRule = "至少有2道带有[素食]或[滋补]特性的菜品";
    private const string OceanLandRule = "至少有1道带有[肉食]特性的菜品和1道带有[水产]特性的菜品";
    private const string SoupRule = "至少有1道[汤羹]类菜品";

    /// <summary>Every dish in the data, all ∞: a player who has them all.</summary>
    private static IReadOnlyList<MenuDish> EveryDish => [.. Inn.Dishes.Select(d => new MenuDish(d, null))];

    private static InnPlanInput Theme(string theme, bool everyDish)
    {
        var (hot, rule, preferred) = theme switch
        {
            "养身心" => ((IReadOnlyList<string>)["Veggy", "Rich"], WholesomeRule, (IReadOnlyList<string>)["Veggy", "Rich"]),
            "海陆宴" => (["Meaty", "Finny"], OceanLandRule, []),
            _ => (["Soup", InnData.Drink], SoupRule, ["Soup"]),
        };
        var input = InnThemePreferenceTests.Session(hot, rule, preferred);
        return everyDish ? input with { Dishes = EveryDish } : input;
    }

    private static bool Matches(InnPlanInput input, Dish dish) => InnPlanner.ThemeMatches(InnPlanner.ThemeItems(input), dish) > 0;

    private void Show(string name, InnPlanInput input, InnPlan plan)
    {
        var items = InnPlanner.ThemeItems(input);
        output.WriteLine($"{name}: {string.Join(" ", plan.Dishes.Select(d => $"{d.Dish.Zh}{new string('*', InnPlanner.ThemeMatches(items, d.Dish))}"))} | {string.Join(" ", plan.Drinks.Select(d => d.Drink.Zh))}");
        output.WriteLine($"  chefs {string.Join(" / ", plan.Kitchens.Select(k => $"{k.Number}: " + string.Join("+", k.Chefs.Select(c => $"{c.NameZh}({string.Join(",", Inn.ChefBonusesOf(c).Select(b => b.DishType?.ToString() ?? b.Flavor ?? b.Kind.ToString()))})"))))}");
        output.WriteLine($"  simulated {plan.Simulated}");
    }

    /// <summary>
    /// The owner's dishes (the cards of test_yuanyuan1's menu screen): every matching dish they have is on
    /// the menu, and only as many others as the rules need (6 dishes at least, 3 types).
    /// 养身心: 白瓜肉丸汤 (滋补), 莓果甜汤 (滋补), 果味沙拉 (素食). 海陆宴: 茄果鳞肉汤 (水产), 坛子肉 (肉食),
    /// 金丝小贝 (肉食). 舌上清流 (汤羹): 白瓜肉丸汤, 莓果甜汤, 茄果鳞肉汤, 酸辣汤.
    /// </summary>
    [Theory]
    [InlineData("养身心", new[] { "白瓜肉丸汤", "莓果甜汤", "果味沙拉" }, 3)]
    [InlineData("海陆宴", new[] { "茄果鳞肉汤", "坛子肉", "金丝小贝" }, 3)]
    [InlineData("舌上清流", new[] { "白瓜肉丸汤", "莓果甜汤", "茄果鳞肉汤", "酸辣汤" }, 2)]
    public void Every_matching_dish_the_owner_has_is_on_the_menu(string theme, string[] matching, int others)
    {
        var input = Theme(theme, everyDish: false);
        var plan = Planner.Plan(input);
        Show(theme, input, plan);

        Assert.True(plan.RulesMet);
        Assert.Equal(matching.Order(), plan.Dishes.Where(d => Matches(input, d.Dish)).Select(d => d.Dish.Zh).Order());
        Assert.Equal(others, plan.Dishes.Count(d => !Matches(input, d.Dish)));
    }

    /// <summary>
    /// Every dish in the data: 养身心 has seven matching dishes of three types (福寿全, 果味沙拉, 莓果甜汤,
    /// 白瓜肉丸汤, 奶油蘑菇汤, 银耳莲子羹, 千果珍珠圆), so the menu is those seven and nothing else; 海陆宴
    /// six of four types (界龙肉卷, 坛子肉, 糖醋宝鳞, 茄果鳞肉汤, 白鳞高汤, 金丝小贝), likewise; 舌上清流 eight
    /// soups, one type: six soups and the two dishes of other types the "≥3 types" rule needs.
    /// </summary>
    [Theory]
    [InlineData("养身心", 7, 0)]
    [InlineData("海陆宴", 6, 0)]
    [InlineData("舌上清流", 6, 2)]
    public void With_every_dish_the_menu_is_matching_dishes_and_only_what_the_rules_need(string theme, int matching, int others)
    {
        var input = Theme(theme, everyDish: true);
        var plan = Planner.Plan(input);
        Show(theme + " (every dish)", input, plan);

        Assert.True(plan.RulesMet);
        Assert.Equal(matching, plan.Dishes.Count(d => Matches(input, d.Dish)));
        Assert.Equal(others, plan.Dishes.Count(d => !Matches(input, d.Dish)));
        if (theme == "舌上清流") Assert.Equal(3, plan.Dishes.Select(d => d.Dish.Type).Distinct().Count());
    }

    /// <summary>
    /// A dish matching two hot items beats one matching one, whatever their prices: with 汤羹 and 滋补 hot (a
    /// constructed case) and room for two dishes, the menu is the two 滋补 soups (莓果甜汤 70, 白瓜肉丸汤 90),
    /// not the dearer soups or 滋补 dishes that match once (簇锦豆腐羹 120, 福寿全 110, 银耳莲子羹 110).
    /// </summary>
    [Fact]
    public void A_dish_matching_two_hot_items_beats_one_matching_one()
    {
        var input = InnThemePreferenceTests.Session(["Soup", "Rich"], SoupRule, []) with
        {
            Dishes = EveryDish,
            Rules = new MenuRules(1, 2, 1, 8, 1, []),
        };

        var plan = Planner.Plan(input);
        Show("汤羹+滋补, two dishes", input, plan);

        Assert.Equal(["白瓜肉丸汤", "莓果甜汤"], plan.Dishes.Select(d => d.Dish.Zh).Order());
    }

    /// <summary>
    /// Matching drinks first likewise: with 茶香 hot (constructed), both tea drinks the owner has go on
    /// (梨花香茶 35 too, cheaper than the 60 drinks), and only as many others as the 3-drink minimum needs.
    /// </summary>
    [Fact]
    public void Matching_drinks_come_first()
    {
        var input = InnThemePreferenceTests.Session(["Tea"], SoupRule, []);

        var plan = Planner.Plan(input);
        Show("茶香", input, plan);

        Assert.Contains(plan.Drinks, d => d.Drink.Zh == "梨花香茶");
        Assert.Contains(plan.Drinks, d => d.Drink.Zh == "潮前龙井");
        Assert.Equal(3, plan.Drinks.Count);
    }

    /// <summary>The quick plan (the setup advice's first answer) and the model's plan put matching dishes first too.</summary>
    [Fact]
    public void The_quick_plan_and_the_models_plan_put_matching_dishes_first()
    {
        var input = Theme("养身心", everyDish: true);

        foreach (var plan in new[] { Planner.PlanQuickly(input), Planner.PlanByModel(input) })
        {
            Assert.True(plan.RulesMet);
            Assert.Equal(7, plan.Dishes.Count(d => Matches(input, d.Dish)));
            Assert.Equal(7, plan.Dishes.Count);
        }
    }

    /// <summary>
    /// 养身心: with the menu fixed by matching first, the chefs cook those dishes: every kitchen that cooks has
    /// a chef speeding a type or flavour of the menu, and the soups (白瓜肉丸汤, 莓果甜汤) are cooked
    /// faster than at base speed.
    /// </summary>
    [Fact]
    public void The_chefs_cook_the_matching_menu()
    {
        var input = Theme("养身心", everyDish: false);
        var plan = Planner.Plan(input);
        Show("养身心 chefs", input, plan);
        var kitchens = plan.Kitchens.Select(k => new Kitchen(k.Number, k.Chefs)).Where(KitchenMath.Cooks).ToList();

        foreach (string zh in new[] { "白瓜肉丸汤", "莓果甜汤", "果味沙拉" })
        {
            var dish = InnTestData.Dish(zh);
            double fastest = kitchens.Min(k => KitchenMath.CookSeconds(Inn, k, dish, 4));
            double plain = KitchenMath.CookSeconds(Inn, InnTestData.PlainKitchen(1), dish, 4);
            output.WriteLine($"{zh}: {fastest:F1} s (base {plain:F1} s)");
            Assert.True(fastest < plain - 0.5, $"{zh}: {fastest} vs base {plain}");
        }
    }

    /// <summary>
    /// Substitutes (可换) for a kitchen's dishes: matching dishes first. A plan whose menu leaves the 滋补 and
    /// 素食 dishes 福寿全, 奶油蘑菇汤, 银耳莲子羹 and 千果珍珠圆 off: every kitchen's substitutes list
    /// matching dishes before any other.
    /// </summary>
    [Fact]
    public void A_kitchens_dish_substitutes_are_matching_dishes_first()
    {
        var input = Theme("养身心", everyDish: true);
        IReadOnlyList<Kitchen> kitchens =
        [
            InnTestData.Kitchen(1, "Chen_Qianyu", "Zhuang_Fangyi"),
            InnTestData.Kitchen(2, "Tangtang", "Fluorite"),
            InnTestData.Kitchen(3, "Yvonne", "Avywenna"),
        ];
        IReadOnlyList<MenuDish> dishes = [.. new[] { "白瓜肉丸汤", "莓果甜汤", "果味沙拉", "八宝饭", "老麻抄手", "界龙肉卷" }.Select(zh => new MenuDish(InnTestData.Dish(zh), null))];
        var plan = new InnPlan([.. kitchens.Select(k => new PlannedKitchen(k.Number, k.Chefs))], dishes, [], null,
            Planner.Evaluate(input, kitchens, dishes, []), RulesMet: true, []);

        var suggestions = InnSuggester.Of(Planner, input, plan);

        var subs = suggestions.Kitchens.SelectMany(k => k.DishSubstitutes).ToList();
        foreach (var k in suggestions.Kitchens)
            output.WriteLine($"{k.Number}: {string.Join(" ", k.DishSubstitutes.Select(d => d.Dish.Zh + (Matches(input, d.Dish) ? "*" : "")))}");
        Assert.Contains(subs, d => Matches(input, d.Dish));
        Assert.All(suggestions.Kitchens, k =>
        {
            var flags = k.DishSubstitutes.Select(d => Matches(input, d.Dish)).ToList();
            Assert.Equal(flags.OrderByDescending(f => f), flags);
        });
    }
}
