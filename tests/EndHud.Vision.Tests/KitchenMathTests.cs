using EndHud.Vision.Inn;
using EndHud.Vision.Operators;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>The shipped inn data and roster, for the inn's model and advisor tests.</summary>
internal static class InnTestData
{
    private static readonly Lazy<OperatorLibrary> LibraryOnce = new(() =>
        OperatorCache.LoadAsync(Path.Combine(Path.GetTempPath(), "endhud-tests", "inn-" + Guid.NewGuid().ToString("N"))).GetAwaiter().GetResult());

    public static OperatorLibrary Library => LibraryOnce.Value;

    public static InnData Inn => Library.Inn!;

    public static Dish Dish(string zh) => Inn.Dishes.Single(d => d.Zh == zh);

    public static InnDrink Drink(string zh) => Inn.Drinks.Single(d => d.Zh == zh);

    public static OperatorInfo Chef(string id) => Library.Find(id)!;

    public static Kitchen Kitchen(int number, params string[] chefs) => new(number, [.. chefs.Select(Chef)]);

    /// <summary>A chef with no hobby or element the rules know: a kitchen at base speed.</summary>
    public static Kitchen PlainKitchen(int number) => new(number, []);
}

/// <summary>
/// Cook times and choice-dish (珍馐) chances in a kitchen (<see cref="KitchenMath"/>), against the
/// game's 派单 panel in the owner's recordings, both at Inn Rating 4 with Si's 品质把控 II (+5 %).
/// </summary>
public class KitchenMathTests
{
    private const int Rating = 4;
    private const int SiQuality = 5;

    private static InnData Inn => InnTestData.Inn;

    /// <summary>test_yuanyuan1 at 4:15: kitchen 3 with 赛希 (Xaihi) and 伊冯 (Yvonne).</summary>
    [Theory]
    [InlineData("金丝小贝", 27)]
    [InlineData("雪绵豆沙", 20)]
    [InlineData("白瓜肉丸汤", 37)]
    [InlineData("“勺里游”", 32)]
    public void Xaihi_and_Yvonne_cook_as_the_panel_shows(string dish, int seconds)
    {
        var kitchen = InnTestData.Kitchen(3, "Xaihi", "Yvonne");

        Assert.Equal(seconds, KitchenMath.CookSeconds(Inn, kitchen, InnTestData.Dish(dish), Rating));
    }

    /// <summary>test_yuanyuan2: kitchen 1 with 大厨佩丽卡 (Perlica) and 大厨艾维文娜 (Avywenna).</summary>
    [Theory]
    [InlineData("白瓜肉丸汤", 40)]
    [InlineData("莓果甜汤", 37)]
    [InlineData("果味沙拉", 30)]
    [InlineData("“勺里游”", 27)]
    [InlineData("坛子肉", 27)]
    [InlineData("茄果鳞肉汤", 40)]
    [InlineData("肉包子", 15)]
    [InlineData("缘缘菜饭", 15)]
    public void Perlica_and_Avywenna_cook_as_the_panel_shows(string dish, int seconds)
    {
        var kitchen = InnTestData.Kitchen(1, "Perlica", "Avywenna");

        Assert.Equal(seconds, KitchenMath.CookSeconds(Inn, kitchen, InnTestData.Dish(dish), Rating));
    }

    /// <summary>The second panel's bonus lines: 菜肴 +20 %, 甜品 +10 %, 甜 +10 %.</summary>
    [Fact]
    public void Perlica_and_Avywennas_bonuses_add_up_as_the_panel_shows()
    {
        var kitchen = InnTestData.Kitchen(1, "Perlica", "Avywenna");

        Assert.Equal(20, KitchenMath.SpeedPercent(Inn, kitchen, InnTestData.Dish("果味沙拉"), Rating)); // a side, not sweet
        Assert.Equal(10, KitchenMath.SpeedPercent(Inn, kitchen, InnTestData.Dish("鲜奶炖蛋"), Rating)); // a dessert, no flavor
        Assert.Equal(30, KitchenMath.SpeedPercent(Inn, kitchen, InnTestData.Dish("坛子肉"), Rating)); // a sweet side
        Assert.Equal(10, KitchenMath.SpeedPercent(Inn, kitchen, InnTestData.Dish("莓果甜汤"), Rating)); // a sweet soup
    }

    /// <summary>
    /// Both panels show 珍馐 18 %: Rating 4's 8 % + one chef's 5 % (赛希's 益智, 佩丽卡's 创意) + Si's 5 %.
    /// A dish of the kitchen's flavor comes out choice more often still.
    /// </summary>
    [Fact]
    public void The_choice_chance_is_the_ratings_base_the_chefs_and_si()
    {
        var kitchen3 = InnTestData.Kitchen(3, "Xaihi", "Yvonne");
        var kitchen1 = InnTestData.Kitchen(1, "Perlica", "Avywenna");

        Assert.Equal(18, KitchenMath.ChoicePercent(Inn, kitchen3, InnTestData.Dish("金丝小贝"), Rating, SiQuality));
        Assert.Equal(18, KitchenMath.ChoicePercent(Inn, kitchen1, InnTestData.Dish("果味沙拉"), Rating, SiQuality));
        Assert.Equal(0.18, KitchenMath.ChoiceChance(Inn, kitchen1, InnTestData.Dish("肉包子"), Rating, SiQuality), 6);
        // 鲜 +10 % from two Cryo chefs; 甜 +10 % from two Electric ones.
        Assert.Equal(28, KitchenMath.ChoicePercent(Inn, kitchen3, InnTestData.Dish("白瓜肉丸汤"), Rating, SiQuality));
        Assert.Equal(28, KitchenMath.ChoicePercent(Inn, kitchen1, InnTestData.Dish("坛子肉"), Rating, SiQuality));
        // Without Si's assist, and at a lower rating.
        Assert.Equal(13, KitchenMath.ChoicePercent(Inn, kitchen3, InnTestData.Dish("金丝小贝"), Rating, 0));
        Assert.Equal(7, KitchenMath.ChoicePercent(Inn, kitchen3, InnTestData.Dish("金丝小贝"), 1, 0));
    }

    /// <summary>The element's flavor bonus helps only from Inn Rating 3 on.</summary>
    [Fact]
    public void The_flavor_bonus_starts_at_rating_3()
    {
        var kitchen = InnTestData.Kitchen(3, "Xaihi", "Yvonne");
        var soup = InnTestData.Dish("白瓜肉丸汤");

        Assert.Equal(40, KitchenMath.CookSeconds(Inn, kitchen, soup, 2));
        Assert.Equal(37, KitchenMath.CookSeconds(Inn, kitchen, soup, 3));
        Assert.Equal(4 + 5, KitchenMath.ChoicePercent(Inn, kitchen, soup, 2, 0));
    }

    /// <summary>Fewer chefs (C5): one chef, or none, and the kitchen still cooks.</summary>
    [Fact]
    public void A_kitchen_with_one_chef_or_none_cooks()
    {
        var dessert = InnTestData.Dish("金丝小贝");

        Assert.Equal(30, KitchenMath.CookSeconds(Inn, InnTestData.Kitchen(1, "Yvonne"), dessert, Rating)); // 35 ÷ 1.2
        Assert.Equal(35, KitchenMath.CookSeconds(Inn, InnTestData.PlainKitchen(2), dessert, Rating));
        Assert.Equal(8, KitchenMath.ChoicePercent(Inn, InnTestData.PlainKitchen(2), dessert, Rating, 0));
    }

    [Fact]
    public void Three_chefs_a_fourth_kitchen_or_an_unknown_rating_are_refused()
    {
        var crowded = InnTestData.Kitchen(1, "Xaihi", "Yvonne", "Perlica");
        var dessert = InnTestData.Dish("金丝小贝");

        Assert.Throws<ArgumentException>(() => KitchenMath.CookSeconds(Inn, crowded, dessert, Rating));
        Assert.Throws<ArgumentException>(() => new InnKitchens(Inn, [.. Enumerable.Range(1, 4).Select(InnTestData.PlainKitchen)], Rating, 0));
        Assert.Throws<ArgumentException>(() => new InnKitchens(Inn, [], Rating, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => KitchenMath.ChoicePercent(Inn, InnTestData.PlainKitchen(1), dessert, 5, 0));
    }

    [Fact]
    public void Inn_kitchens_give_each_kitchens_times_by_its_number()
    {
        var kitchens = new InnKitchens(Inn, [InnTestData.Kitchen(1, "Perlica", "Avywenna"), InnTestData.Kitchen(3, "Xaihi", "Yvonne")], Rating, SiQuality);

        Assert.Equal(27, kitchens.CookSeconds(1, InnTestData.Dish("坛子肉")));
        Assert.Equal(27, kitchens.CookSeconds(3, InnTestData.Dish("金丝小贝")));
        Assert.Equal(28, kitchens.ChoicePercent(3, InnTestData.Dish("白瓜肉丸汤")));
    }
}
