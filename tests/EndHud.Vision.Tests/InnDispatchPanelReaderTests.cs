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

    /// <summary>The dishes on each card, left to right and top to bottom, as the frames show them.</summary>
    public static readonly (string Fixture, string[] Cards)[] CardOrder =
    [
        ("session-dispatch-panel", ["fin-mato-soup", "goldfloss-chiffon", "sweetbeans-on-snow", "melon-meatball-soup", "spoon-swimmer", "mala-xiang-guo"]),
        ("dispatch-panel-queue", ["melon-meatball-soup", "berry-sweet-sip", "berry-fruity-toss", "spoon-swimmer", "clay-jar-meat", "fin-mato-soup", "meat-baozi", "yuanyuan-veggy-rice"]),
        // The owner's 2026-10-20 session, EndHud's banners over the panel's chef column.
        ("dispatch-panel-banners", ["roll-of-fin-supreme", "melon-meatball-soup", "berry-sweet-sip", "berry-fruity-toss", "spoon-swimmer", "fin-mato-soup"]),
    ];

    /// <summary>
    /// The marker on the suggested dish (owner, live 2026-10-20: "is it possible to ALSO have a suggestion marker by the
    /// dish itself on the right side?") is drawn around the card its dish was read on. Each dish read is said to be on
    /// the card it is on, and that card's place (<see cref="DispatchPanelReader.CardOnScreen"/>) is the card's outline:
    /// just inside it the card's light face, just outside the panel's ground, clearly darker (light grey above the first
    /// row, dark elsewhere).
    /// </summary>
    [Theory]
    [MemberData(nameof(AllScreens))]
    public async Task Each_dish_is_read_on_its_card_and_the_cards_place_is_its_outline(string screenName)
    {
        var screen = SimulatedScreen.Named(screenName);
        var reader = await Reader.Value;
        foreach (var (fixture, cards) in CardOrder)
        {
            var frame = await InnScreenTests.Load(fixture);
            if (screen != SimulatedScreen.Uhd) frame = screen.Render(frame);
            var panel = await reader.ReadAsync(frame);
            Assert.True(panel is not null, $"{fixture}: no panel read");

            var named = panel.Dishes.Where(d => d.DishId is not null).ToList();
            Assert.True(named.Count >= cards.Length - 1, $"{fixture}: {named.Count} of {cards.Length} dishes named");
            Assert.All(named, d => Assert.Equal(cards[d.Card], d.DishId));

            for (int card = 0; card < cards.Length; card++)
            {
                var place = DispatchPanelReader.CardOnScreen(card, frame.Width, frame.Height);
                int step = Math.Max(2, place.Height / 60), gap = Math.Max(3, place.Height / 25);
                var inside = Sides(frame, place, step);
                var outside = Sides(frame, place, -gap);
                int edges = inside.Zip(outside).Count(p => Math.Abs(p.First.Level - p.Second.Level) >= 20);
                output.WriteLine($"{screenName} {fixture} card {card}: {place.X},{place.Y} {place.Width}x{place.Height}, "
                    + $"inside {string.Join("/", inside.Select(x => $"{x.Level:F0}"))}, outside {string.Join("/", outside.Select(x => $"{x.Level:F0}"))}");
                Assert.True(inside.Average(x => x.Light) >= 0.7, $"{fixture} card {card}: only {inside.Average(x => x.Light):P0} of its inner edge is the card's face");
                Assert.True(edges >= 3, $"{fixture} card {card}: the card's edge is on {edges} of its 4 sides (the place is off the card)");
            }
        }
    }

    /// <summary>
    /// Along each side of a rectangle (top, right, bottom, left), <paramref name="inset"/> pixels inside its edge (outside
    /// when negative): the share of light grey pixels (the cards' face, white or greyed) and the mean brightness.
    /// </summary>
    private static (double Light, double Level)[] Sides(Frame frame, PixelRect r, int inset)
    {
        int left = r.X + inset, top = r.Y + inset, right = r.Right - 1 - inset, bottom = r.Bottom - 1 - inset;
        (double, double) Along(IEnumerable<(int X, int Y)> points)
        {
            int light = 0, all = 0;
            long level = 0;
            foreach (var (x, y) in points)
            {
                if (x < 0 || y < 0 || x >= frame.Width || y >= frame.Height) continue;
                all++;
                var (red, green, blue) = frame.GetPixel(x, y);
                level += red + green + blue;
                if (Math.Min(red, Math.Min(green, blue)) >= 185 && Math.Max(red, Math.Max(green, blue)) - Math.Min(red, Math.Min(green, blue)) <= 30) light++;
            }
            return all == 0 ? (0, 0) : (light / (double)all, level / (3.0 * all));
        }
        // The middle 80 % of each side: the corners are rounded.
        int x0 = left + (right - left) / 10, x1 = right - (right - left) / 10, y0 = top + (bottom - top) / 10, y1 = bottom - (bottom - top) / 10;
        return
        [
            Along(Enumerable.Range(x0, x1 - x0 + 1).Select(x => (x, top))),
            Along(Enumerable.Range(y0, y1 - y0 + 1).Select(y => (right, y))),
            Along(Enumerable.Range(x0, x1 - x0 + 1).Select(x => (x, bottom))),
            Along(Enumerable.Range(y0, y1 - y0 + 1).Select(y => (left, y))),
        ];
    }

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
