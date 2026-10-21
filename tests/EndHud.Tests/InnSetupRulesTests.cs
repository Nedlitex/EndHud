using EndHud.Vision.Inn;
using Xunit;
using static EndHud.Tests.InnTestKit;

namespace EndHud.Tests;

/// <summary>
/// The setup advice under a dish-type rule (owner, live, 2026-10-19): the soup theme, its menu requirement
/// "至少有1道[汤羹]类菜品", 热门 汤羹 and 饮品. The panel's menu had no 汤羹 dish: the rule wasn't
/// understood and was silently dropped. No fixture frame of this theme exists: the setup screen is
/// given as the reader returns it (its lines and its 热门 ids), see InnDishTypeRuleTests for the
/// reading of the text itself.
/// </summary>
public class InnSetupRulesTests
{
    private static InnReading Setup(IReadOnlyList<string> rules, IReadOnlyList<string> hot) =>
        new(InnScreenKind.Setup, S(0), Setup: new InnSetup("舌上清流", Inn.Themes.FirstOrDefault(t => t.Zh == "舌上清流"), "出售10道[汤羹]类菜品",
            rules, hot, [new SetupKitchen(1, 2, [null, null], [null, null]), new SetupKitchen(2, 2, [null, null], [null, null]),
                new SetupKitchen(3, 2, [null, null], [null, null])]));

    private static InnSetupInputs Inputs(IReadOnlyList<string> rules, IReadOnlyList<string> hot)
    {
        var session = NewSession();
        session.Update(Setup(rules, hot));
        return session.SetupInputs();
    }

    [Fact]
    public void The_soup_rule_and_the_hot_types_reach_the_planner()
    {
        var inputs = Inputs(["至少有1道[汤羹]类菜品"], ["Soup", InnData.Drink]);

        var input = inputs.Input!;
        var rule = Assert.Single(input.Rules.DishRules);
        Assert.Equal((1, "Soup"), (rule.Count, string.Join("/", rule.AnyOf)));
        Assert.Equal(["Soup", InnData.Drink], input.HotTraits);
        Assert.Empty(inputs.RulesNotUnderstood);
    }

    /// <summary>A rule not understood is kept as read, for the log (the overlay logs it once per change; no panel says it).</summary>
    [Fact]
    public void A_rule_not_understood_is_kept_as_read()
    {
        var inputs = Inputs(["至少有3道很奇怪的菜品"], ["Soup"]);

        Assert.Equal(["至少有3道很奇怪的菜品"], inputs.RulesNotUnderstood);
    }

    /// <summary>
    /// A rule whose last character was lost where its line wraps (owner, live 2026-10-21: "至少有2道「[甜品]类菜。", the
    /// dessert half of the tea-break theme's rule, said as not understood): read all the same, with the half before it.
    /// </summary>
    [Fact]
    public void A_rule_cut_where_its_line_wraps_is_understood()
    {
        var inputs = Inputs(["至少有2道「[汤羹]类菜品和2道「[甜品]类菜。"], ["Soup", "Dessert"]);

        Assert.Empty(inputs.RulesNotUnderstood);
        Assert.Equal(["2:Soup", "2:Dessert"], inputs.Input!.Rules.DishRules.Select(r => $"{r.Count}:{string.Join("/", r.AnyOf)}"));
    }
}
