using EndHud.Vision.Inn;
using Xunit;
using Xunit.Abstractions;

namespace EndHud.Vision.Tests;

/// <summary>
/// A gourmand's speech bubble (<see cref="BubbleReader"/>): the one recorded bubble
/// (session-gourmand-order, two requests on four lines) read and matched to the gourmand table, at
/// 4K, 1440p and 1080p; and no bubble on the other session frames.
/// </summary>
public class InnBubbleReaderTests(ITestOutputHelper output)
{
    private static readonly InnData Inn =
        InnData.Parse(File.ReadAllBytes(Path.Combine(OperatorCache.RepositoryRoot(), InnData.FolderName, InnData.FileName)))!;

    public static TheoryData<string> Screens() => [SimulatedScreen.Uhd.Name, SimulatedScreen.Qhd.Name, SimulatedScreen.FullHd.Name];

    public static TheoryData<string> WithoutBubble() =>
    [
        "session-start", "session-first-order", "session-fire-meter", "session-gourmand-arrives", "session-gourmand-one-request",
        "session-two-dishes", "session-fire-ready", "session-closed", "session-four-orders", "session-paying-and-waiting",
    ];

    [Theory]
    [MemberData(nameof(Screens))]
    public async Task The_gourmands_two_requests_are_read(string screenName)
    {
        var screen = SimulatedScreen.Named(screenName);
        var frame = await InnScreenTests.Load("session-gourmand-order");
        var bubble = await new BubbleReader(InnScreenTests.Text).ReadAsync(screen == SimulatedScreen.Uhd ? frame : screen.Render(frame));

        Assert.NotNull(bubble);
        output.WriteLine($"{screen.Width}x{screen.Height}: \"{bubble.Text}\" → {string.Join(" | ", bubble.Lines)}");
        Assert.Equal(["随便上点，别是滋补药膳那种就行！", "来点喝的清清口，别是奶味的就行。"], bubble.Requests(Inn).Select(m => m.Request.Text));
    }

    /// <summary>
    /// Owner, 2026-10-19 ("the gourmet asked for 2 dishes, it only hinted one"): the bubble at table 4
    /// reads 「来点汤，要酸口，促消 / 化。/ 上好的肉！做点菜！」, a category icon before each request.
    /// Live it was read as one line, "点汤，要酸口，促消匕好的肉！做点菜！", and only the soup was found.
    /// </summary>
    [Theory]
    [MemberData(nameof(Screens))]
    public async Task Both_requests_of_a_bubble_on_three_lines_are_read(string screenName)
    {
        var screen = SimulatedScreen.Named(screenName);
        var frame = await InnScreenTests.Load("session-gourmand-two-lines");
        var bubble = await new BubbleReader(InnScreenTests.Text).ReadAsync(screen == SimulatedScreen.Uhd ? frame : screen.Render(frame));

        Assert.NotNull(bubble);
        output.WriteLine($"{screen.Width}x{screen.Height}: \"{bubble.Text}\" → {string.Join(" | ", bubble.Lines)}");
        Assert.Equal(["来点汤，要酸口，促消化。", "上好的肉！做点菜！"], bubble.Requests(Inn).Select(m => m.Request.Text));
    }

    /// <summary>
    /// Owner, live 2026-10-21 ("it also missed the drink requirement"): table 4's gourmand asks 「清爽无油的汤羹，有吗？」 and
    /// 「来杯喝的，谨记，我滴酒不沾。」 (gourmand 3 of the normal pool: 汤羹&amp;非&lt;菜籽油&gt;, 非[酒香]: a soup and a drink), the
    /// bubble large, middle-sized and small as the camera moves. Each is read as both of them, and only them.
    /// </summary>
    public static TheoryData<string, string> SoupAndDrinkBubbles()
    {
        var data = new TheoryData<string, string>();
        foreach (string fixture in new[] { "session-gourmand-soup-and-drink", "session-gourmand-soup-and-drink-near", "session-panel-closed-mark-table2" })
        foreach (var screen in new[] { SimulatedScreen.Uhd, SimulatedScreen.Qhd, SimulatedScreen.FullHd })
            data.Add(fixture, screen.Name);
        return data;
    }

    [Theory]
    [MemberData(nameof(SoupAndDrinkBubbles))]
    public async Task A_soup_and_a_drink_are_both_read(string fixture, string screenName)
    {
        var screen = SimulatedScreen.Named(screenName);
        var frame = await InnScreenTests.Load(fixture);
        var bubble = await new BubbleReader(InnScreenTests.Text).ReadAsync(screen == SimulatedScreen.Uhd ? frame : screen.Render(frame));

        Assert.NotNull(bubble);
        output.WriteLine($"{fixture} {screen.Width}x{screen.Height}: \"{bubble.Text}\" → {string.Join(" | ", bubble.Lines)}");
        var requests = bubble.Requests(Inn);
        Assert.Equal(["清爽无油的汤羹，有吗？", "来杯喝的，谨记，我滴酒不沾。"], requests.Select(m => m.Request.Text));
        Assert.All(requests, m => Assert.Equal(("normal", 3), (m.Gourmand.Pool, m.Gourmand.Number)));
        Assert.Equal([false, true], requests.Select(m => GourmandCondition.Parse(m.Request.When, Inn).ForDrink));
    }

    [Theory]
    [MemberData(nameof(WithoutBubble))]
    public async Task No_bubble_where_there_is_none(string fixture)
    {
        Assert.Null(await new BubbleReader(InnScreenTests.Text).ReadAsync(await InnScreenTests.Load(fixture)));
    }
}
