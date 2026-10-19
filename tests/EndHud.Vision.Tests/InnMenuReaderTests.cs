using EndHud.Vision.Inn;
using Xunit;
using Xunit.Abstractions;

namespace EndHud.Vision.Tests;

/// <summary>
/// The menu screen (<see cref="MenuReader"/>) on its two frames (the dishes and the drinks tab), at
/// 4K, 1440p and 1080p: the tab, every card in view with whether it's on the menu, read exactly; the
/// stock (∞ on every card here) and the counters never read wrong, and how many are read logged.
/// </summary>
public class InnMenuReaderTests(ITestOutputHelper output)
{
    private static readonly Lazy<Task<MenuReader>> Reader = new(async () =>
        new MenuReader(InnScreenTests.Text, InnData.Load(await OperatorCache.FolderAsync())
            ?? throw new InvalidOperationException("The operator package has no inn data.")));

    public static TheoryData<string> Screens() => [SimulatedScreen.Uhd.Name, SimulatedScreen.Qhd.Name, SimulatedScreen.FullHd.Name];

    private static readonly (string Fixture, MenuTab Tab, (int, int) Dishes, (int, int) Drinks, string[] Cards)[] Menus =
    [
        ("menu-dishes", MenuTab.AllDishes, (2, 8), (0, 8),
            ["melon-meatball-soup*", "berry-fruity-toss", "berry-sweet-sip*", "spoon-swimmer", "fin-mato-soup", "clay-jar-meat", "mala-xiang-guo", "roll-of-fin-supreme"]),
        ("menu-drinks", MenuTab.Drinks, (8, 8), (0, 8),
            ["bottled-duskberry-juice", "pre-tide-lungjing", "vintage-grapedew", "bottled-milk-a", "seekers-kohl", "pear-blossom-tea", "bottled-canejuice", "corn-juice"]),
    ];

    [Theory]
    [MemberData(nameof(Screens))]
    public async Task The_tab_the_cards_and_the_counters(string screenName)
    {
        var screen = SimulatedScreen.Named(screenName);
        int stocks = 0, unlimited = 0, counters = 0, countersRead = 0;
        foreach (var (fixture, tab, dishes, drinks, cards) in Menus)
        {
            var frame = await InnScreenTests.Load(fixture);
            var menu = await (await Reader.Value).ReadAsync(screen == SimulatedScreen.Uhd ? frame : screen.Render(frame));

            Assert.NotNull(menu);
            Assert.Equal(tab, menu.Tab);
            Assert.Equal(cards, menu.Cards.Select(c => c.Id + (c.OnMenu ? "*" : "")));
            // Every card here has unlimited stock: none may be read as a number.
            Assert.All(menu.Cards, c => Assert.Null(c.Stock));
            stocks += menu.Cards.Count;
            unlimited += menu.Cards.Count(c => c.Unlimited);
            foreach (var (expected, read, what) in new[] { (dishes, menu.Dishes, "dishes"), (drinks, menu.Drinks, "drinks") })
            {
                counters++;
                if (read is null) output.WriteLine($"{fixture} {what} counter: unread");
                else
                {
                    Assert.Equal(expected, read.Value);
                    countersRead++;
                }
            }
        }
        output.WriteLine($"{screen.Width}x{screen.Height}: stock ∞ read on {unlimited}/{stocks} cards, counters {countersRead}/{counters}");
    }

    [Fact]
    public async Task The_counters_and_stock_at_4K()
    {
        var menu = await (await Reader.Value).ReadAsync(await InnScreenTests.Load("menu-dishes"));

        Assert.Equal((2, 8), menu?.Dishes);
        Assert.Equal((0, 8), menu?.Drinks);
        Assert.True(menu!.Cards.Count(c => c.Unlimited) >= 6, $"∞ read on {menu.Cards.Count(c => c.Unlimited)} of 8 cards");
    }
}
