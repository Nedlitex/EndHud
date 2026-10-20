using EndHud.Vision.Inn;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// 海陆宴 as the owner played it live (2026-10-19): the setup screen states the theme text
/// "珍馐赏钱加成提升，出售珍馐菜品可获得更高收益", the objective "出售6道[珍馐]菜品", the rule
/// "至少有1道带有[肉食]特性的菜品和1道带有[水产]特性的菜品" and the hot traits 肉食 and 水产. The
/// suggested menu had no 水产 dish at all: the rule's second half was never read.
/// </summary>
public class InnOceanLandTests
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
}
