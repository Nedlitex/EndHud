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
        output.WriteLine($"{screen.Width}x{screen.Height}: \"{bubble.Text}\" → {string.Join(" | ", bubble.Sentences)}");
        Assert.Equal(["随便上点，别是滋补药膳那种就行！", "来点喝的清清口，别是奶味的就行。"], bubble.Requests(Inn).Select(m => m.Request.Text));
    }

    [Theory]
    [MemberData(nameof(WithoutBubble))]
    public async Task No_bubble_where_there_is_none(string fixture)
    {
        Assert.Null(await new BubbleReader(InnScreenTests.Text).ReadAsync(await InnScreenTests.Load(fixture)));
    }
}
