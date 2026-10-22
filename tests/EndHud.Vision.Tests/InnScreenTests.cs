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
        { "setup-theme", InnScreenKind.ThemeList, null },
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

    /// <summary>The chef picker's title names its kitchen (kitchen 1): the panel there is that kitchen's advice.</summary>
    [Theory]
    [MemberData(nameof(Screens))]
    public async Task The_chef_pickers_kitchen_is_read(string screenName)
    {
        var screen = SimulatedScreen.Named(screenName);
        var frame = await Load("chef-picker");

        Assert.Equal(1, await InnScreen.ReadPickerKitchenAsync(Text, screen == SimulatedScreen.Uhd ? frame : screen.Render(frame)));
    }

    [Theory]
    [InlineData("厨房一", 1)]
    [InlineData("//厨房二", 2)]
    [InlineData("厨房三", 3)]
    [InlineData("厨房", null)]
    [InlineData("营业设定", null)]
    public void A_pickers_title_names_its_kitchen(string title, int? kitchen) => Assert.Equal(kitchen, InnScreen.PickerKitchen(title));

    /// <summary>
    /// Where the timer was read says whether the dispatch panel can be open: under the "结束经营" button only with the panel
    /// closed (test_yuanyuan5 252.6 s, the panel just closed and the Full Blaze meter not faded in yet), at the top with it
    /// open. At every size.
    /// </summary>
    [Theory]
    [InlineData("session-panel-just-closed", true)]
    [InlineData("session-tray-two-dishes", true)]
    [InlineData("session-full-blaze-running", true)]
    [InlineData("dispatch-panel-queue", false)]
    [InlineData("session-dispatch-panel", false)]
    [InlineData("dispatch-panel-banners", false)]
    public async Task The_timer_under_its_button_is_told_from_the_timer_at_the_top(string fixture, bool belowButton)
    {
        foreach (var screen in new[] { SimulatedScreen.Uhd, SimulatedScreen.Qhd, SimulatedScreen.FullHd })
        {
            var clock = InnScreen.ReadClock(screen.Render(await Load(fixture)));
            Assert.True(clock is not null && clock.BelowButton == belowButton, $"{fixture} at {screen.Width}x{screen.Height}: read {clock?.ToString() ?? "no timer"}");
        }
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
