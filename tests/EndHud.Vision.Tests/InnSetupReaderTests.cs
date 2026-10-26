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
            ["1: 2 places, 2 taken, Perlica Avywenna", "2: 2 places, 1 taken, Chen_Qianyu -", "3: 2 places, 0 taken, - -"],
            setup.Kitchens.Select(k => $"{k.Number}: {k.ChefSlots} places, {k.ChefsPlaced} taken, {string.Join(" ", k.Chefs.Select(c => c ?? "-"))}"));
        if (screen == SimulatedScreen.Uhd)
        {
            Assert.Equal("开启2次满堂彩", setup.Objective);
            Assert.Equal(["至少有2道带有[素食]或[滋补]特性的菜品"], setup.MenuRules);
        }
    }

    /// <summary>
    /// Si's 品质把控 II line, "所有厨房的珍馐产出概率提升 5%", and the theme's text under its name
    /// ("几道山珍素食…灶火升温更快…"), which the planner reads for what it says of choice dishes.
    /// </summary>
    [Theory]
    [MemberData(nameof(Screens))]
    public async Task Sis_assist_and_the_theme_text_are_read(string screenName)
    {
        var screen = SimulatedScreen.Named(screenName);
        var setup = await Read("setup-chefs-placed", screen);
        output.WriteLine($"{screen.Width}x{screen.Height}: Si {setup.SiChoicePercent}, text \"{setup.Description}\"");

        Assert.Equal(5, setup.SiChoicePercent);
        Assert.NotNull(setup.Description);
        Assert.Contains("灶火升温更快", setup.Description);
        Assert.False(InnObjective.RaisesChoiceTips(setup.Description));
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

    /// <summary>
    /// Owner, live 2026-10-21 (test_yuanyuan8, setup-gourmand-theme.jpg): the gourmand theme's 菜单要求 ("至少需要8道菜品和6种
    /// 饮品 / 且[主食]类、[菜肴]类、[甜品]类、[汤羹]类菜品，每 / 种至少有1道") sits above a 流行趋势 bar with a chip and a refresh
    /// countdown. The section used to run on to the next heading the reader knew (热门类型), taking in the bar: the menu advice
    /// then said "看不懂菜单要求：至少有1道@13小时39分钟0流行趋势" and the plan ignored the rule. The section stops at the bar,
    /// and every rule in it is understood: a dish of each of the four types.
    /// </summary>
    [Theory]
    [MemberData(nameof(Screens))]
    public async Task The_menu_rules_stop_at_the_trend_bar_and_each_type_rule_is_understood(string screenName)
    {
        var screen = SimulatedScreen.Named(screenName);
        var setup = await Read("setup-gourmand-theme", screen);
        output.WriteLine($"{screen.Width}x{screen.Height}: rules \"{string.Join(" | ", setup.MenuRules)}\"");

        Assert.DoesNotContain(setup.MenuRules, l => l.Contains("流行") || l.Contains("小时") || l.Contains("分钟") || l.Contains("赏钱"));
        var read = DishRule.ParseAll(setup.MenuRules, InnData.Load(await OperatorCache.FolderAsync())!);
        Assert.Empty(read.NotUnderstood);
        Assert.Equal(["1:Entree", "1:Side", "1:Dessert", "1:Soup"], read.Rules.Select(r => $"{r.Count}:{string.Join("/", r.AnyOf)}"));
    }

    /// <summary>
    /// Screens not 16:9 (player diagnostics 2026-10-26, a 3840×2400 laptop: 营业设定 gave no kitchens and "厨房一" for the
    /// theme, so no menu or kitchen advice). Where the game puts the setup panels in the extra height isn't known, so each
    /// layout it may use: the HUD's corners (<see cref="SimulatedScreen.Render"/>) and the whole 16:9 screen at the top,
    /// centred or at the bottom (<see cref="SimulatedScreen.Placed"/>). Each must give the theme, the kitchens and the chefs.
    /// </summary>
    public static TheoryData<string, string> OtherShapes()
    {
        var data = new TheoryData<string, string>();
        var laptop = new SimulatedScreen("3840x2400 (16:10, 16-inch 4K laptop)", 3840, 2400);
        foreach (var screen in new[] { laptop, SimulatedScreen.Wqxga, SimulatedScreen.Wuxga, SimulatedScreen.SurfaceLarge })
            foreach (string layout in new[] { "corners", "top", "centred", "bottom" })
                data.Add($"{screen.Width}x{screen.Height}", layout);
        return data;
    }

    [Theory]
    [MemberData(nameof(OtherShapes))]
    [Trait("Category", "Slow")] // sweeps screen shapes, reading each place: left out of the fast lane
    public Task The_setup_screen_is_read_on_a_screen_not_16_by_9(string size, string layout) => ReadOnOtherShape(size, layout);

    /// <summary>The player's screen, as the diagnostics' symptom was reproduced (theme "厨房三", no kitchens): in the fast lane.</summary>
    [Fact]
    public Task The_setup_screen_is_read_on_the_players_16_by_10_laptop() => ReadOnOtherShape("3840x2400", "corners");

    private async Task ReadOnOtherShape(string size, string layout)
    {
        var (w, h) = (int.Parse(size.Split('x')[0]), int.Parse(size.Split('x')[1]));
        var screen = new SimulatedScreen(size, w, h);
        var reader = await Reader.Value;
        Frame Shaped(Frame frame) => layout switch
        {
            "corners" => screen.Render(frame),
            "top" => screen.Placed(frame, 0),
            "centred" => screen.Placed(frame, 0.5),
            _ => screen.Placed(frame, 1),
        };

        var setup = await reader.ReadAsync(Shaped(await InnScreenTests.Load("setup-chefs-placed")), InnScreenKind.Setup);
        var list = await reader.ReadAsync(Shaped(await InnScreenTests.Load("setup-theme")), InnScreenKind.ThemeList);
        output.WriteLine($"{size} {layout}: theme {setup?.ThemeName}, traits {string.Join(" ", setup?.HotTraits ?? [])}, Si {setup?.SiChoicePercent}; list: theme {list?.ThemeName}");

        Assert.Equal("养身心", setup?.ThemeName);
        Assert.Equal("truly-wholesome", setup!.Theme?.Id);
        Assert.Equal(["Veggy", "Rich"], setup.HotTraits);
        Assert.Equal(5, setup.SiChoicePercent);
        Assert.NotNull(setup.Kitchens);
        Assert.Equal(
            ["1: 2 places, 2 taken, Perlica Avywenna", "2: 2 places, 1 taken, Chen_Qianyu -", "3: 2 places, 0 taken, - -"],
            setup.Kitchens.Select(k => $"{k.Number}: {k.ChefSlots} places, {k.ChefsPlaced} taken, {string.Join(" ", k.Chefs.Select(c => c ?? "-"))}"));
        Assert.Equal("养身心", list?.ThemeName);
        Assert.Equal(["Veggy", "Rich"], list!.HotTraits);
        Assert.Null(list.Kitchens);
    }

    [Fact]
    public async Task Only_the_setup_screens_are_read()
    {
        Assert.Null(await (await Reader.Value).ReadAsync(await InnScreenTests.Load("menu-dishes"), InnScreenKind.Menu));
    }
}
