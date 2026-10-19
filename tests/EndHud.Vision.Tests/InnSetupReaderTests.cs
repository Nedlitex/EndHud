using EndHud.Vision.Inn;
using Xunit;
using Xunit.Abstractions;

namespace EndHud.Vision.Tests;

/// <summary>
/// The setup screens (<see cref="SetupReader"/>): the theme list (setup-theme) and 营业设定
/// (setup-chefs-placed), at 4K, 1440p and 1080p. At 4K everything is read word for word; in the
/// smaller windows the theme, its hot traits and the kitchens and chefs are, and the objective and
/// menu rules are read as OCR gives them (logged, not checked: OCR slips on them at 1440p).
/// </summary>
public class InnSetupReaderTests(ITestOutputHelper output)
{
    private static readonly Lazy<Task<SetupReader>> Reader = new(async () =>
    {
        string folder = await OperatorCache.FolderAsync();
        var data = InnData.Load(folder) ?? throw new InvalidOperationException("The operator package has no inn data.");
        var library = await OperatorCache.LoadAsync(Path.Combine(Path.GetTempPath(), "endhud-inn-tests-user"));
        return new SetupReader(InnScreenTests.Text, data, library.Operators);
    });

    public static TheoryData<string> Screens() => [SimulatedScreen.Uhd.Name, SimulatedScreen.Qhd.Name, SimulatedScreen.FullHd.Name];

    private static async Task<InnSetup> Read(string fixture, SimulatedScreen screen)
    {
        var frame = await InnScreenTests.Load(fixture);
        var setup = await (await Reader.Value).ReadAsync(screen == SimulatedScreen.Uhd ? frame : screen.Render(frame), InnScreenKind.Setup);
        Assert.True(setup is not null, $"{fixture} at {screen.Width}x{screen.Height}: nothing read");
        return setup;
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public async Task The_setup_screen_its_theme_and_its_chefs(string screenName)
    {
        var screen = SimulatedScreen.Named(screenName);
        var setup = await Read("setup-chefs-placed", screen);
        output.WriteLine($"{screen.Width}x{screen.Height}: objective \"{setup.Objective}\", rules \"{string.Join(" | ", setup.MenuRules)}\"");

        Assert.Equal("养身心", setup.ThemeName);
        Assert.Equal("truly-wholesome", setup.Theme?.Id);
        Assert.Equal(["Veggy", "Rich"], setup.HotTraits);
        Assert.NotNull(setup.Kitchens);
        Assert.Equal(
            ["1: 2 places, Perlica Avywenna", "2: 2 places, Chen_Qianyu -", "3: 2 places, - -"],
            setup.Kitchens.Select(k => $"{k.Number}: {k.ChefSlots} places, {string.Join(" ", k.Chefs.Select(c => c ?? "-"))}"));
        if (screen == SimulatedScreen.Uhd)
        {
            Assert.Equal("开启2次满堂彩", setup.Objective);
            Assert.Equal(["至少有2道带有[素食]或[滋补]特性的菜品"], setup.MenuRules);
        }
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public async Task The_theme_list_shows_the_chosen_theme_and_no_kitchens(string screenName)
    {
        var screen = SimulatedScreen.Named(screenName);
        var setup = await Read("setup-theme", screen);
        output.WriteLine($"{screen.Width}x{screen.Height}: objective \"{setup.Objective}\", rules \"{string.Join(" | ", setup.MenuRules)}\"");

        Assert.Equal("养身心", setup.ThemeName);
        Assert.Equal("truly-wholesome", setup.Theme?.Id);
        Assert.Equal(["Veggy", "Rich"], setup.HotTraits);
        Assert.Null(setup.Kitchens);
        if (screen == SimulatedScreen.Uhd)
        {
            Assert.Equal("开启2次满堂彩", setup.Objective);
            Assert.Equal(["至少有2道带有[素食]或[滋补]特性的菜品"], setup.MenuRules);
        }
    }

    [Fact]
    public async Task Only_the_setup_screens_are_read()
    {
        Assert.Null(await (await Reader.Value).ReadAsync(await InnScreenTests.Load("menu-dishes"), InnScreenKind.Menu));
    }
}
