using EndHud.Vision.Inn;
using Xunit;
using Xunit.Abstractions;

namespace EndHud.Vision.Tests;

/// <summary>
/// The dispatch panel (<see cref="DispatchPanelReader"/>) on the frames that show it (Fixtures/inn),
/// at 4K, 1440p and 1080p: the kitchen shown, its chefs, the choice chance, each dish's cook time
/// and the queue. A value may be left unread; none may be read wrong; and at least 95 % of them are
/// read at each size.
/// </summary>
public class InnDispatchPanelReaderTests(ITestOutputHelper output)
{
    private static readonly Lazy<Task<DispatchPanelReader>> Reader = new(async () =>
    {
        string folder = await OperatorCache.FolderAsync();
        var data = InnData.Load(folder) ?? throw new InvalidOperationException("The operator package has no inn data.");
        var library = await OperatorCache.LoadAsync(Path.Combine(Path.GetTempPath(), "endhud-inn-tests-user"));
        return new DispatchPanelReader(InnScreenTests.Text, await InnPictures.LoadAsync(folder, data), data, library.Operators);
    });

    private static readonly (string Id, int Seconds)[] KitchenThree =
        [("fin-mato-soup", 40), ("goldfloss-chiffon", 27), ("sweetbeans-on-snow", 20), ("melon-meatball-soup", 37), ("spoon-swimmer", 32), ("mala-xiang-guo", 35)];

    /// <summary>What each frame's panel shows. A queue place: a dish id with its seconds left, "" empty, "?" a dish there but faded (not to be named).</summary>
    internal static readonly (string Fixture, int Kitchen, int Kitchens, string[] Chefs, int Chance, (string Id, int Seconds)[] Dishes, (string Dish, int? Seconds)[] Queue)[] Panels =
    [
        ("session-fire-0", 3, 3, ["Xaihi", "Yvonne"], 18, KitchenThree, [("goldfloss-chiffon", 25), ("sweetbeans-on-snow", null), ("sweetbeans-on-snow", null)]),
        ("session-dispatch-panel", 3, 3, ["Xaihi", "Yvonne"], 18, KitchenThree, [("goldfloss-chiffon", 1), ("", null), ("", null)]),
        ("session-paying-and-waiting", 3, 3, ["Xaihi", "Yvonne"], 18, KitchenThree, [("?", null), ("", null), ("", null)]),
        ("session-four-orders", 1, 3, ["Perlica", "Estella"], 23,
            [("fin-mato-soup", 40), ("goldfloss-chiffon", 35), ("sweetbeans-on-snow", 24), ("melon-meatball-soup", 39), ("spoon-swimmer", 27), ("mala-xiang-guo", 30)],
            [("mala-xiang-guo", 8), ("spoon-swimmer", null), ("", null)]),
        ("dispatch-panel-queue", 1, 2, ["Perlica", "Avywenna"], 18,
            [("melon-meatball-soup", 40), ("berry-sweet-sip", 37), ("berry-fruity-toss", 30), ("spoon-swimmer", 27), ("clay-jar-meat", 27), ("fin-mato-soup", 40), ("meat-baozi", 15), ("yuanyuan-veggy-rice", 15)],
            [("melon-meatball-soup", 40), ("", null), ("", null)]),
    ];

    public static TheoryData<string> AllScreens() => [SimulatedScreen.Uhd.Name, SimulatedScreen.Qhd.Name, SimulatedScreen.FullHd.Name];

    [Theory]
    [MemberData(nameof(AllScreens))]
    public async Task The_panel_is_read_95_percent_and_nothing_wrong(string screenName)
    {
        var screen = SimulatedScreen.Named(screenName);
        var reader = await Reader.Value;
        int values = 0, right = 0;
        var wrong = new List<string>();
        foreach (var (fixture, kitchen, kitchens, chefs, chance, dishes, queue) in Panels)
        {
            var frame = await InnScreenTests.Load(fixture);
            var panel = await reader.ReadAsync(screen == SimulatedScreen.Uhd ? frame : screen.Render(frame));
            Assert.True(panel is not null, $"{fixture}: no panel read");

            void Check<T>(string what, T expected, T? read)
            {
                values++;
                if (read is null) output.WriteLine($"{fixture} {what}: unread");
                else if (Equals(read, expected)) right++;
                else wrong.Add($"{fixture} {what}: read {read}, is {expected}");
            }
            Check("kitchen", kitchen, (int?)panel.Kitchen);
            Check("kitchens", kitchens, (int?)panel.Kitchens);
            for (int i = 0; i < 2; i++) Check($"chef {i + 1}", chefs[i], panel.Chefs[i]);
            Check("choice chance", chance, panel.ChoiceChancePercent);
            Assert.True(panel.Dishes.Count == dishes.Length, $"{fixture}: {panel.Dishes.Count} dishes read, {dishes.Length} there");
            for (int i = 0; i < dishes.Length; i++)
            {
                Check($"dish {i + 1}", dishes[i].Id, panel.Dishes[i].DishId);
                Check($"dish {i + 1} cook time", dishes[i].Seconds, panel.Dishes[i].CookSeconds);
            }
            for (int i = 0; i < queue.Length; i++)
            {
                var (dish, seconds) = queue[i];
                var slot = panel.Queue[i];
                Check($"queue {i + 1} filled", dish != "", (bool?)slot.Filled);
                if (dish == "?") Assert.True(slot.DishId is null, $"{fixture} queue {i + 1}: named {slot.DishId} where the dish is faded");
                else if (dish != "") Check($"queue {i + 1} dish", dish, slot.DishId);
                if (seconds is not null) Check($"queue {i + 1} seconds", seconds, slot.SecondsLeft);
            }
        }
        output.WriteLine($"{screen.Width}x{screen.Height}: {right}/{values} values read right ({100.0 * right / values:0.0} %)");
        Assert.Empty(wrong);
        Assert.True(right >= values * 0.95, $"{right} of {values} read right");
    }

    [Theory]
    [InlineData("session-fire-92")]
    [InlineData("setup-chefs-placed")]
    [InlineData("menu-dishes")]
    public async Task No_panel_where_there_is_none(string fixture)
    {
        Assert.Null(await (await Reader.Value).ReadAsync(await InnScreenTests.Load(fixture)));
    }
}
