using EndHud.Vision.Inn;
using EndHud.Vision.Operators;
using Xunit;
using Xunit.Abstractions;

namespace EndHud.Vision.Tests;

/// <summary>
/// 海陆宴 as the owner played it live (2026-10-19): the setup screen states the theme text
/// "珍馐赏钱加成提升，出售珍馐菜品可获得更高收益", the objective "出售6道[珍馐]菜品", the rule
/// "至少有1道带有[肉食]特性的菜品和1道带有[水产]特性的菜品" and the hot traits 肉食 and 水产. The
/// suggested menu had no 水产 dish at all: the rule's second half was never read.
/// </summary>
public class InnOceanLandTests(ITestOutputHelper output)
{
    private static InnData Inn => InnTestData.Inn;

    private const string Rule = "至少有1道带有[肉食]特性的菜品和1道带有[水产]特性的菜品";

    [Fact]
    public void Both_halves_of_a_rule_joined_by_and_are_read()
    {
        var read = DishRule.ParseAll([Rule], Inn);

        Assert.Equal(["1:Meaty", "1:Finny"], read.Rules.Select(r => $"{r.Count}:{string.Join("/", r.AnyOf)}"));
        Assert.Empty(read.NotUnderstood);
    }

    /// <summary>The menu screen's 菜单要求 panel wraps the same rule after "[水" (test_yuanyuan1 at 2:10).</summary>
    [Fact]
    public void The_menu_screens_wrapped_rule_gives_both_halves()
    {
        var read = MenuRequirementsReader.Parse(
            ["菜单要求", "4级客栈要求", "至少需要3种不同的类型的菜品", "至少需要6道菜品和3种饮品", "经营主题要求",
             "至少有1道带有[肉食]特性的菜品和1道带有[水", "产]特性的菜品", "主题热门", "肉食水产"], Inn);

        Assert.Equal(["1:Meaty", "1:Finny"], read.DishRules.Select(r => $"{r.Count}:{string.Join("/", r.AnyOf)}"));
        Assert.Equal((4, 3, 6, 3), (read.Rating, read.MinTypes, read.MinDishes, read.MinDrinks));
    }

    [Fact]
    public void The_objective_and_the_theme_text_about_choice_dishes_are_understood()
    {
        // The 珍馐 icon between 道 and 珍馐 reads as a stray letter, or nothing.
        Assert.Equal(6, InnObjective.ChoiceDishTarget("出售6道珍馐菜品"));
        Assert.Equal(6, InnObjective.ChoiceDishTarget("出售6道a珍馐菜品"));
        Assert.Null(InnObjective.ChoiceDishTarget("开启2次满堂彩"));
        Assert.Null(InnObjective.ChoiceDishTarget("出售10道[汤羹]类菜品"));
        Assert.True(InnObjective.RaisesChoiceTips("大海与陆地交汇，天地之间无数种巧妙味道就此诞生。珍馐赏钱加成提升，出售珍馐菜品可获得更高收益。"));
        Assert.False(InnObjective.RaisesChoiceTips("大海与陆地交汇，天地之间无数种巧妙味道就此诞生。"));
        Assert.Equal(5, SetupReader.AssistPercent("所有厨房的珍馐产出概率提升 5%"));
        Assert.Equal(5, SetupReader.AssistPercent("品质把控自0所有厨房的珍馐产出概率提升5％")); // as OCR gives it: the full-width ％
        Assert.Null(SetupReader.AssistPercent("品质把控 II"));
    }

    // ---- The owner's live session: 海陆宴 3 stars, Rating 4, six tables, Si's 品质把控 II ----

    private static OperatorInfo Op(string id) => InnTestData.Chef(id);

    /// <summary>The owner's line-up, every place taken: 秋栗 + 昼雪, 洁尔佩塔 + 伊冯, 弭弗 + 艾维文娜.</summary>
    private static IReadOnlyList<PlanKitchen> OwnersKitchens =>
    [
        new PlanKitchen(1, 2) { Placed = [Op("Akekuri"), Op("Snowshine")] },
        new PlanKitchen(2, 2) { Placed = [Op("Gilberta"), Op("Yvonne")] },
        new PlanKitchen(3, 2) { Placed = [Op("Mi_Fu"), Op("Avywenna")] },
    ];

    /// <summary>The cards the owner's menu screen showed while scrolling (test_yuanyuan1 2:10–2:40), all ∞.</summary>
    private static readonly string[] OwnersCards =
    [
        "茄果鳞肉汤", "坛子肉", "金丝小贝", "“勺里游”", "白瓜肉丸汤", "麻辣香锅", "至臻鳞肉卷", "果味沙拉", "八宝饭", "莓果甜汤", "酸辣汤", "雪绵豆沙",
        "烤肉串", "豌杂拌面", "酸莓布丁", "酸菜鳞肉", "炸薯条", "鳞肉水饺", "豆沙汤圆", "手抓饭", "咸酪脆饼", "鳞香肉丝", "青菜养生粥", "鲜奶炖蛋",
        "茄汁白瓜", "菌菇蒸饺",
    ];

    /// <summary>The owner's menu (6/8 on the bottom bar).</summary>
    private static readonly string[] OwnersMenu = ["茄果鳞肉汤", "坛子肉", "金丝小贝", "“勺里游”", "白瓜肉丸汤", "麻辣香锅"];

    /// <summary>The drinks tab's eight drinks, all 库存∞.</summary>
    private static readonly string[] OwnersDrinks = ["瓶装霞莓饮", "潮前龙井", "陈酿葡萄露", "瓶装优质兽奶", "游人酒", "梨花香茶", "瓶装脆蔗汁", "玉米汁"];

    private static InnPlanInput OwnersInput() => new(["Meaty", "Finny"], 4, 5, InnState.MaxTables, OwnersKitchens, InnTestData.Library.Operators,
        [.. OwnersCards.Select(zh => new MenuDish(InnTestData.Dish(zh), null))], [.. OwnersDrinks.Select(zh => new MenuDrink(InnTestData.Drink(zh), null))],
        new MenuRules(6, 8, 3, 8, 3, DishRule.ParseAll([Rule], Inn).Rules))
    {
        ChoiceDishTarget = 6,
        ChoiceTipsRaised = true,
    };

    private static readonly Lazy<InnPlan> OwnersPlan = new(() => new InnPlanner(Inn).Plan(OwnersInput()));

    /// <summary>
    /// The owner's own menu, played in the simulator with the same guests, earns no more than the
    /// plan; the plan meets both halves of the rule and uses only cards the owner has.
    /// </summary>
    [Fact]
    public void The_plan_earns_at_least_the_owners_own_menu_in_the_simulator()
    {
        var planner = new InnPlanner(Inn);
        var input = OwnersInput();
        var plan = OwnersPlan.Value;
        var kitchens = plan.Kitchens.Select(k => new Kitchen(k.Number, k.Chefs)).ToList();
        var owners = planner.Simulate(input, kitchens, [.. OwnersMenu.Select(zh => new MenuDish(InnTestData.Dish(zh), null))], plan.Drinks);
        output.WriteLine($"plan:  {string.Join(" ", plan.Dishes.Select(d => d.Dish.Zh))} | {string.Join(" ", plan.Drinks.Select(d => d.Drink.Zh))}");
        output.WriteLine($"       simulated {plan.Simulated}; model {plan.Score}");
        output.WriteLine($"owner: {string.Join(" ", OwnersMenu)} (the plan's drinks): simulated {owners}");

        Assert.True(plan.RulesMet);
        Assert.Contains(plan.Dishes, d => d.Dish.Traits.Contains("Meaty"));
        Assert.Contains(plan.Dishes, d => d.Dish.Traits.Contains("Finny"));
        Assert.All(plan.Dishes, d => Assert.Contains(d.Dish.Zh, OwnersCards));
        Assert.Equal(["1:Akekuri+Snowshine", "2:Gilberta+Yvonne", "3:Mi_Fu+Avywenna"], plan.Kitchens.Select(k => $"{k.Number}:{string.Join("+", k.Chefs.Select(c => c.Id))}"));
        var simulated = Assert.IsType<InnSimulatedScore>(plan.Simulated);
        Assert.False(owners.BetterThan(simulated), $"owner's menu {owners} > plan {simulated}");
    }

    /// <summary>
    /// The live complaint: the suggested menu was six quick 15 s dishes. Played in the simulator, a
    /// menu of the quickest dishes (meeting the rules) earns less than the plan, and the plan's
    /// dishes are mostly the dearer ones: the guests' clock, not the kitchens, sets how many orders come.
    /// </summary>
    [Fact]
    public void Quick_cheap_dishes_lose_to_dearer_ones_when_guests_come_on_a_clock()
    {
        var planner = new InnPlanner(Inn);
        var input = OwnersInput();
        var plan = OwnersPlan.Value;
        var kitchens = plan.Kitchens.Select(k => new Kitchen(k.Number, k.Chefs)).ToList();
        // The live suggestion's quick dishes the owner has, with the cheapest Meaty and Finny dishes the rule needs.
        string[] quick = ["青菜养生粥", "茄汁白瓜", "鳞香肉丝", "咸酪脆饼", "金丝小贝", "茄果鳞肉汤"];
        var quickScore = planner.Simulate(input, kitchens, [.. quick.Select(zh => new MenuDish(InnTestData.Dish(zh), null))], plan.Drinks);

        Assert.True(plan.Simulated!.BetterThan(quickScore), $"quick menu {quickScore} >= plan {plan.Simulated}");
        Assert.True(plan.Dishes.Count(d => d.Dish.Price >= 80) > plan.Dishes.Count / 2,
            $"plan: {string.Join(" ", plan.Dishes.Select(d => $"{d.Dish.Zh}({d.Dish.Price})"))}");
    }
}
