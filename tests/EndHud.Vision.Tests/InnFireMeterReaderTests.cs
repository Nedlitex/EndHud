using EndHud.Vision.Inn;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// The Full Blaze meter (<see cref="FireMeterReader"/>) on the inn frames (Fixtures/inn), at 4K and
/// scaled to 1440p and 1080p: the fire below full, the ready state at full, the streak.
/// </summary>
public class InnFireMeterReaderTests
{
    public static TheoryData<string, int?, int?, bool> Frames() => new()
    {
        { "session-first-order", 0, 0, false },
        { "session-fire-meter", 0, 0, false },
        { "session-gourmand-arrives", 4, 2, false },
        { "session-gourmand-order", 8, 4, false },
        { "session-gourmand-one-request", 61, 15, false },
        { "session-fire-92", 92, 18, false },
        { "session-two-dishes", 92, 18, false },
        { "session-fire-ready", 100, 20, true },
        { "session-closed", 100, 36, true },
        // The dispatch panel hides the meter.
        { "session-fire-0", null, null, false },
        { "session-dispatch-panel", null, null, false },
        { "dispatch-panel-queue", null, null, false },
        // Not a session.
        { "setup-chefs-placed", null, null, false },
        { "menu-dishes", null, null, false },
    };

    public static TheoryData<string> Screens() => [SimulatedScreen.Uhd.Name, SimulatedScreen.Qhd.Name, SimulatedScreen.FullHd.Name];

    [Theory]
    [MemberData(nameof(Frames))]
    public async Task The_fire_and_the_streak_are_read(string fixture, int? fire, int? streak, bool ready)
    {
        AssertMeter(await FireMeterReader.ReadAsync(InnScreenTests.Text, await InnScreenTests.Load(fixture)), fire, streak, ready, fixture);
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public async Task On_1440p_and_1080p_too(string screenName)
    {
        var screen = SimulatedScreen.Named(screenName);
        foreach (var row in Frames())
        {
            var frame = screen.Render(await InnScreenTests.Load((string)row[0]));
            AssertMeter(await FireMeterReader.ReadAsync(InnScreenTests.Text, frame), (int?)row[1], (int?)row[2], (bool)row[3], $"{row[0]} at {screen.Width}x{screen.Height}");
        }
    }

    /// <summary>
    /// Full Blaze on (test_yuanyuan5, owner's live session 2026-10-21): the button is a red circle counting down what's left,
    /// with the game's own line left of it ("赏钱翻倍，速度提升!" as it starts, then "灶火正旺，满堂生彩!"). Before, the second
    /// line was read as the ready label, and "开启满堂彩（R）" stayed up all through Full Blaze. At every size.
    /// </summary>
    [Theory]
    [InlineData("session-full-blaze-started", 60, 19)]
    [InlineData("session-full-blaze-running", 13, 26)]
    public async Task Full_blaze_running_is_read_as_its_countdown_not_as_ready(string fixture, int secondsLeft, int streak)
    {
        foreach (var name in Screens().Select(row => (string)row[0]))
        {
            var screen = SimulatedScreen.Named(name);
            var read = await FireMeterReader.ReadAsync(InnScreenTests.Text, screen.Render(await InnScreenTests.Load(fixture)));
            Assert.True(read == new FireMeter(null, streak, false, BlazeLeft: TimeSpan.FromSeconds(secondsLeft)),
                $"{fixture} at {screen.Width}x{screen.Height}: read {read?.ToString() ?? "nothing"}, expected Full Blaze with {secondsLeft} s left, streak {streak}");
        }
    }

    /// <summary>The session's first frame: the HUD is fading in and the meter can't be read yet. Nothing is guessed.</summary>
    [Fact]
    public async Task A_fading_meter_is_not_read()
    {
        Assert.Null(await FireMeterReader.ReadAsync(InnScreenTests.Text, await InnScreenTests.Load("session-start")));
    }

    private static void AssertMeter(FireMeter? read, int? fire, int? streak, bool ready, string what)
    {
        if (fire is null)
        {
            Assert.True(read is null, $"{what}: read {read} where there's no meter");
            return;
        }
        Assert.True(read == new FireMeter(fire, streak, ready, BlazeLeft: null), $"{what}: read {read?.ToString() ?? "nothing"}, expected fire {fire}, streak {streak}, ready {ready}");
    }
}
