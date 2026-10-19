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
