using EndHud.Vision.Inn;
using Xunit;
using Xunit.Abstractions;

namespace EndHud.Vision.Tests;

/// <summary>
/// The menu screen's 菜单要求 panel (<see cref="MenuRequirementsReader"/>) on both menu frames at
/// 4K, 1440p and 1080p: Inn Rating 4, at least 3 types, 6 dishes and 3 drinks, and the theme's
/// 至少有2道带有[素食]或[滋补]特性的菜品 (README).
/// </summary>
public class InnMenuRequirementsReaderTests(ITestOutputHelper output)
{
    private static readonly Lazy<Task<(MenuRequirementsReader Reader, InnData Data)>> Setup = new(async () =>
    {
        var data = InnData.Load(await OperatorCache.FolderAsync()) ?? throw new InvalidOperationException("The operator package has no inn data.");
        return (new MenuRequirementsReader(InnScreenTests.Text, data), data);
    });

    public static TheoryData<string, string> Frames()
    {
        var frames = new TheoryData<string, string>();
        foreach (string fixture in new[] { "menu-dishes", "menu-drinks" })
        foreach (var screen in new[] { SimulatedScreen.Uhd, SimulatedScreen.Qhd, SimulatedScreen.FullHd })
            frames.Add(fixture, screen.Name);
        return frames;
    }

    [Theory]
    [MemberData(nameof(Frames))]
    public async Task The_requirements_are_read(string fixture, string screenName)
    {
        var screen = SimulatedScreen.Named(screenName);
        var source = await InnScreenTests.Load(fixture);
        var frame = screen == SimulatedScreen.Uhd ? source : screen.Render(source);
        var read = await (await Setup.Value).Reader.ReadAsync(frame);
        Assert.NotNull(read);
        output.WriteLine($"{fixture} {screen.Width}x{screen.Height}: {string.Join(" | ", read.Lines)}");
        Assert.Equal(4, read.Rating);
        Assert.Equal(3, read.MinTypes);
        Assert.Equal(6, read.MinDishes);
        Assert.Equal(3, read.MinDrinks);
        var rule = Assert.Single(read.DishRules);
        Assert.Equal(2, rule.Count);
        Assert.Equal(["Veggy", "Rich"], rule.AnyOf);
    }

    /// <summary>
    /// Owner, live 2026-10-21 (test_yuanyuan8 at 2:00, menu-gourmand-theme.jpg): 专属菜单要求 "至少需要8道菜品和6种饮品 / 且[主食]类、
    /// [菜肴]类、[甜品]类、 / [汤羹]类菜品，每种至少有1道", the 流行趋势 heading and its chips (汤羹, 素食) below. The trend lines
    /// are not part of the requirements, and the rule is one dish of each of the four types.
    /// </summary>
    [Theory]
    [MemberData(nameof(Screens))]
    public async Task The_gourmand_themes_requirements_are_read_without_the_trend_below(string screenName)
    {
        var screen = SimulatedScreen.Named(screenName);
        var source = await InnScreenTests.Load("menu-gourmand-theme");
        var frame = screen == SimulatedScreen.Uhd ? source : screen.Render(source);
        var read = await (await Setup.Value).Reader.ReadAsync(frame);
        Assert.NotNull(read);
        output.WriteLine($"{screen.Width}x{screen.Height}: {string.Join(" | ", read.Lines)}");
        Assert.DoesNotContain(read.Lines, l => l.Contains("流行") || l.Contains("素食"));
        Assert.Equal(8, read.MinDishes);
        Assert.Equal(6, read.MinDrinks);
        Assert.Equal(["1:Entree", "1:Side", "1:Dessert", "1:Soup"], read.DishRules.Select(r => $"{r.Count}:{string.Join("/", r.AnyOf)}"));
    }

    public static TheoryData<string> Screens() => [SimulatedScreen.Uhd.Name, SimulatedScreen.Qhd.Name, SimulatedScreen.FullHd.Name];

    /// <summary>
    /// "且[A]类、[B]类…菜品，每种至少有N道": N dishes of each type named. The type's icon before each bracket may be read as a
    /// stray letter, a bracket lost, the line cut anywhere.
    /// </summary>
    [Theory]
    [InlineData("至少需要8道菜品和6种饮品|且[主食]类、[菜肴]类、[甜品]类、|[汤羹]类菜品，每种至少有1道", "1:Entree 1:Side 1:Dessert 1:Soup")]
    [InlineData("至少需要8道菜品和6种饮品|且@[主食]类、回[菜肴]类、][甜品]类、[汤羹]类|菜品，每|种至少有1道", "1:Entree 1:Side 1:Dessert 1:Soup")]
    [InlineData("且主食类、汤羹类菜品，每种至少有2道", "2:Entree 2:Soup")]
    public async Task Each_type_at_least_is_a_rule_per_type(string lines, string expected)
    {
        var data = (await Setup.Value).Data;
        var read = DishRule.ParseAll(lines.Split('|'), data);
        Assert.Empty(read.NotUnderstood);
        Assert.Equal(expected, string.Join(" ", read.Rules.Select(r => $"{r.Count}:{string.Join("/", r.AnyOf)}")));
    }

    [Fact]
    public async Task Lines_not_understood_read_as_nothing()
    {
        var data = (await Setup.Value).Data;
        var read = MenuRequirementsReader.Parse(["菜单要求", "某种没见过的要求"], data);
        Assert.Equal(0, read.Found);
    }

    [Fact]
    public async Task The_setup_screen_has_no_menu_requirements_panel()
    {
        var read = await (await Setup.Value).Reader.ReadAsync(await InnScreenTests.Load("session-first-order"));
        Assert.True(read is null || read.Rating is null, $"read {read?.Rating}");
    }
}
