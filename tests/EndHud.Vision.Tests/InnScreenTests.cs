using EndHud.Vision.Inn;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// Which inn screen is showing and the session's timer (<see cref="InnScreen"/>), on the inn frames
/// of the owner's recordings (Fixtures/inn, what each shows in its README), at 4K and scaled to
/// 1440p and 1080p.
/// </summary>
public class InnScreenTests
{
    internal static readonly InnText Text = InnText.TryCreate()
        ?? throw new InvalidOperationException("The inn tests need the Simplified Chinese OCR language (Windows Settings → Language).");

    internal static async Task<Frame> Load(string name) => await ImageFile.LoadAsync(CombatFrameTests.Fixture(Path.Combine("inn", name + ".jpg")));

    public static TheoryData<string, InnScreenKind, string?> Frames() => new()
    {
        { "setup-theme", InnScreenKind.Setup, null },
        { "setup-chefs-placed", InnScreenKind.Setup, null },
        { "chef-picker", InnScreenKind.ChefPicker, null },
        { "menu-dishes", InnScreenKind.Menu, null },
        { "menu-drinks", InnScreenKind.Menu, null },
        { "session-start", InnScreenKind.Session, "04:57" },
        { "session-first-order", InnScreenKind.Session, "04:45" },
        { "session-fire-meter", InnScreenKind.Session, "04:25" },
        { "session-gourmand-arrives", InnScreenKind.Session, "03:54" },
        { "session-gourmand-order", InnScreenKind.Session, "03:51" },
        { "session-gourmand-one-request", InnScreenKind.Session, "02:30" },
        { "session-two-dishes", InnScreenKind.Session, "02:18" },
        { "session-fire-92", InnScreenKind.Session, "02:18" },
        { "session-fire-ready", InnScreenKind.Session, "01:51" },
        { "session-closed", InnScreenKind.Session, "00:00" },
        { "dispatch-panel-queue", InnScreenKind.DispatchPanel, "04:52" },
        { "session-fire-0", InnScreenKind.DispatchPanel, "04:46" },
        { "session-four-orders", InnScreenKind.DispatchPanel, "04:06" },
        { "session-dispatch-panel", InnScreenKind.DispatchPanel, "03:16" },
        { "session-paying-and-waiting", InnScreenKind.DispatchPanel, "02:33" },
    };

    /// <summary>Frames that aren't the inn: combat and the squad screen.</summary>
    public static TheoryData<string> NotTheInn() => ["combat-all-ready.jpg", Path.Combine("panel-or-not", "squad-select-screen.jpg")];

    public static TheoryData<string> Screens() => [SimulatedScreen.Uhd.Name, SimulatedScreen.Qhd.Name, SimulatedScreen.FullHd.Name];

    [Theory]
    [MemberData(nameof(Frames))]
    public async Task The_screen_and_the_time_left_are_read(string fixture, InnScreenKind kind, string? time)
    {
        var frame = await Load(fixture);

        await AssertRead(frame, kind, time, fixture);
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public async Task On_1440p_and_1080p_too(string screenName)
    {
        var screen = SimulatedScreen.Named(screenName);
        // The session's first frame has the HUD fading in: its faint timer reads at 4K only.
        foreach (var row in Frames().Where(row => (string)row[0] != "session-start"))
            await AssertRead(screen.Render(await Load((string)row[0])), (InnScreenKind)row[1], (string?)row[2], $"{row[0]} at {screen.Width}x{screen.Height}");
    }

    [Theory]
    [MemberData(nameof(NotTheInn))]
    public async Task Other_screens_are_not_the_inn(string fixture)
    {
        var frame = await ImageFile.LoadAsync(CombatFrameTests.Fixture(fixture));

        Assert.Null(await InnScreen.DetectAsync(Text, frame));
        Assert.Null(InnScreen.ReadClock(frame));
    }

    private static async Task AssertRead(Frame frame, InnScreenKind kind, string? time, string what)
    {
        Assert.True(await InnScreen.DetectAsync(Text, frame) == kind, $"{what}: read {await InnScreen.DetectAsync(Text, frame)}, expected {kind}");
        var clock = InnScreen.ReadClock(frame);
        if (time is null)
        {
            Assert.True(clock is null, $"{what}: read a timer {clock} where there's none");
            return;
        }
        var expected = TimeSpan.ParseExact(time, @"mm\:ss", null);
        Assert.True(clock?.Left == expected, $"{what}: read {clock?.Left.ToString() ?? "no timer"}, expected {time}");
        Assert.Equal(expected == TimeSpan.Zero, clock!.Closed);
    }
}
