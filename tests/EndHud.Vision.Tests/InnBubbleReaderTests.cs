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

    /// <summary>
    /// test_yuanyuan8 (2026-10-21, the 食有老饕 mode: every guest a gourmand of the data's feast pool), frames read by eye:
    /// each bubble on the frame as (gourmand number, its requests' indexes, whether each request's icon has turned into a face).
    /// <list type="bullet">
    /// <item>feast-bubble-short (192.5 s, table 1): the tenth gourmand's dish in five letters and drink in three, the drink served.</item>
    /// <item>feast-bubble-wrapped (204 s, table 2): the seventh's, each request wrapping its last letter onto a line of its own.</item>
    /// <item>feast-two-bubbles (359.5 s): table 4's eleventh gourmand near, table 6's fourteenth far and smaller.</item>
    /// <item>feast-bubble-faces (365 s, table 6): the fourteenth's two requests both failed (red faces).</item>
    /// <item>feast-two-soups (406 s, table 5): the fourth's two soups, a dish icon before each.</item>
    /// <item>feast-two-bubbles-one-resolved (401 s): table 2's tenth gourmand (drink served), table 4's eleventh far (its dish
    /// failed). At 4K and 1080p the far bubble's first line reads with two letters slipped and its dish isn't found: a bubble
    /// marked partial may lose requests, never read one wrong or with the wrong state.</item>
    /// </list>
    /// </summary>
    public static TheoryData<string, string, (int Gourmand, int[] Requests, bool[] Resolved, bool Partial)[]> FeastBubbles()
    {
        var data = new TheoryData<string, string, (int, int[], bool[], bool)[]>();
        foreach (var screen in new[] { SimulatedScreen.Uhd, SimulatedScreen.Qhd, SimulatedScreen.FullHd })
        {
            data.Add("feast-bubble-short", screen.Name, [(10, [1, 3], [false, true], false)]);
            data.Add("feast-bubble-wrapped", screen.Name, [(7, [0, 3], [false, false], false)]);
            data.Add("feast-two-bubbles", screen.Name, [(11, [2, 3], [false, false], false), (14, [1, 3], [false, false], false)]);
            data.Add("feast-bubble-faces", screen.Name, [(14, [1, 3], [true, true], false)]);
            data.Add("feast-two-soups", screen.Name, [(4, [4, 0], [false, false], false)]);
            data.Add("feast-two-bubbles-one-resolved", screen.Name, [(10, [1, 3], [false, true], false), (11, [2, 3], [true, false], true)]);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(FeastBubbles))]
    public async Task Every_feast_bubble_on_a_frame_is_read_with_each_requests_state(string fixture, string screenName,
        (int Gourmand, int[] Requests, bool[] Resolved, bool Partial)[] expected)
    {
        var screen = SimulatedScreen.Named(screenName);
        var frame = await InnScreenTests.Load(fixture);
        var bubbles = await new BubbleReader(InnScreenTests.Text).ReadAllAsync(screen == SimulatedScreen.Uhd ? frame : screen.Render(frame));

        var read = bubbles.Select(b => b.RequestsAndState(Inn)).Where(r => r.Count > 0)
            .Select(r => (Gourmand: r[0].Match.Gourmand.Number,
                Requests: r.Select(x => x.Match.Gourmand.Requests.ToList().IndexOf(x.Match.Request)).ToArray(),
                Resolved: r.Select(x => x.Resolved).ToArray()))
            .ToList();
        foreach (var b in bubbles) output.WriteLine($"{fixture} {screenName}: {b.Place} {string.Join(" / ", b.Lines)} starts {string.Join(",", b.Starts ?? [])} resolved {string.Join(",", b.Resolved ?? [])}");
        Assert.All(bubbles.SelectMany(b => b.Requests(Inn)), m => Assert.Equal("feast", m.Gourmand.Pool));
        Assert.Equal(expected.Length, read.Count);
        for (int i = 0; i < expected.Length; i++)
        {
            var (gourmand, requests, resolved, partial) = expected[i];
            Assert.Equal(gourmand, read[i].Gourmand);
            var want = requests.Zip(resolved, (r, s) => $"{r}:{s}").ToList();
            var got = read[i].Requests.Zip(read[i].Resolved, (r, s) => $"{r}:{s}").ToList();
            if (partial) Assert.All(got, g => Assert.Contains(g, want));
            else Assert.Equal(want, got);
        }
    }

    /// <summary>
    /// feast-bubble-behind-stats (test_yuanyuan8 186 s, table 1): the session's stats box (income, guests, dishes sold) is drawn
    /// over the bubble's second line. What is read is right: the tenth gourmand's dish request, and nothing else.
    /// </summary>
    [Fact]
    public async Task A_bubble_partly_behind_the_stats_box_gives_only_what_can_be_read()
    {
        var bubbles = await new BubbleReader(InnScreenTests.Text).ReadAllAsync(await InnScreenTests.Load("feast-bubble-behind-stats"));

        var requests = Assert.Single(bubbles).Requests(Inn);
        Assert.Contains(requests, m => m.Gourmand.Number == 10 && m.Request == m.Gourmand.Requests[1]);
        Assert.All(requests, m => Assert.True(m.Gourmand is { Pool: "feast", Number: 10 } && (m.Request == m.Gourmand.Requests[1] || m.Request == m.Gourmand.Requests[3])));
    }

    [Theory]
    [MemberData(nameof(WithoutBubble))]
    public async Task No_bubble_where_there_is_none(string fixture)
    {
        Assert.Null(await new BubbleReader(InnScreenTests.Text).ReadAsync(await InnScreenTests.Load(fixture)));
    }
}
