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
        Assert.True(read == new FireMeter(fire, streak, ready), $"{what}: read {read?.ToString() ?? "nothing"}, expected fire {fire}, streak {streak}, ready {ready}");
    }
}
