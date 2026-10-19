using EndHud.Vision.Inn;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// A theme's menu rule about a dish TYPE and its 热门 types (owner, live, 2026-10-19: theme 舌上清流,
/// rule "至少有1道[汤羹]类菜品", 热门 汤羹 and 饮品; the panel's menu had no 汤羹 dish). There is no
/// fixture frame of this theme: these tests start at the reader's text, the lines and chips as OCR
/// gives them, and the planner's input built from them.
/// </summary>
public class InnDishTypeRuleTests
{
    private static InnData Inn => InnTestData.Inn;

    private static readonly InnPlanner Planner = new(InnTestData.Inn);

    [Fact]
    public void A_dish_type_rule_is_read()
    {
        var rule = DishRule.Parse("至少有1道[汤羹]类菜品", Inn);

        Assert.NotNull(rule);
        Assert.Equal(1, rule.Count);
        Assert.Equal(["Soup"], rule.AnyOf);
    }

    [Fact]
    public void A_dish_type_rule_counts_dishes_of_that_type()
    {
        var rule = DishRule.Parse("至少有1道[汤羹]类菜品", Inn)!;

        Assert.True(rule.Matches(Inn.Dishes.First(d => d.Type == DishType.Soup)));
        Assert.False(rule.Matches(Inn.Dishes.First(d => d.Type == DishType.Entree)));
    }

    [Fact]
    public void The_trait_rule_still_reads_as_before()
    {
        var rule = DishRule.Parse("至少有2道带有[素食]或[滋补]特性的菜品", Inn)!;

        Assert.Equal((2, "Veggy/Rich"), (rule.Count, string.Join("/", rule.AnyOf)));
    }

    [Fact]
    public void A_rule_wrapped_over_two_lines_is_read_whole()
    {
        var read = DishRule.ParseAll(["至少有2道带有[素食]或[滋", "补]特性的菜品"], Inn);

        var rule = Assert.Single(read.Rules);
        Assert.Equal(["Veggy", "Rich"], rule.AnyOf);
        Assert.Empty(read.NotUnderstood);
    }

    [Fact]
    public void A_rule_not_understood_is_kept_as_read()
    {
        var read = DishRule.ParseAll(["至少有1道[汤羹]类菜品", "至少有3道很奇怪的菜品"], Inn);

        Assert.Equal(["Soup"], Assert.Single(read.Rules).AnyOf);
        Assert.Equal(["至少有3道很奇怪的菜品"], read.NotUnderstood);
    }

    [Fact]
    public void The_hot_chips_name_dish_types_and_drinks_too()
    {
        Assert.Equal(["Soup", InnData.Drink], SetupReader.HotIds("汤羹 饮品", Inn));
        Assert.Equal(["Veggy", "Rich"], SetupReader.HotIds("素食 滋补", Inn));
    }

    /// <summary>The live case: three kitchens of two places, the whole roster and dish list at Rating 4.</summary>
    [Fact]
    public void The_plan_for_a_soup_rule_puts_a_soup_on_the_menu()
    {
        var rule = DishRule.Parse("至少有1道[汤羹]类菜品", Inn)!;
        var input = new InnPlanInput(SetupReader.HotIds("汤羹 饮品", Inn), 4, 0, 6,
            [new PlanKitchen(1, 2), new PlanKitchen(2, 2), new PlanKitchen(3, 2)], InnTestData.Library.Operators,
            [.. Inn.Dishes.Select(d => new MenuDish(d, null))], [.. Inn.Drinks.Select(d => new MenuDrink(d, null))],
            new MenuRules(6, 8, 3, 8, 3, [rule]));

        var plan = Planner.Plan(input);

        Assert.True(plan.RulesMet);
        Assert.Contains(plan.Dishes, d => d.Dish.Type == DishType.Soup);
    }
}
