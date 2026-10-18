using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// A lasting ultimate's time bar (<see cref="UltimateTimeBar"/>) on frames from the owner's
/// recordings (Fixtures/ultimate-time), at 4K and on 16:9-class screens. Wider screens aren't
/// simulated here: <see cref="SimulatedScreen"/> pins the corners and leaves the bottom centre black.
/// </summary>
public class UltimateTimeBarTests
{
    public static TheoryData<string, double?> Frames() => new()
    {
        { "laevatain-running", 0.97 },
        { "laevatain-nearly-out", 0.02 },
        { "zhuang-fangyi-running", 0.95 },
        { "red-ground-no-ultimate", null },
        { "sky-no-ultimate", null },
        { "sky-no-ultimate-2", null },
        { "sky-no-ultimate-controller", null },
    };

    public static TheoryData<string> Screens() =>
        [.. SimulatedScreen.All.Where(s => Math.Abs(s.Width / (double)s.Height - 16 / 9.0) < 0.2).Select(s => s.Name)];

    private static async Task<Frame> Load(string name) =>
        await ImageFile.LoadAsync(CombatFrameTests.Fixture(Path.Combine("ultimate-time", name + ".jpg")));

    [Theory]
    [MemberData(nameof(Frames))]
    public async Task The_time_left_is_read_and_scenery_is_no_bar(string fixture, double? left)
    {
        var frame = await Load(fixture);

        AssertLeft(left, UltimateTimeBar.Measure(frame, new UiScaler(frame.Width, frame.Height)));
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public async Task On_smaller_and_larger_screens_too(string screenName)
    {
        var screen = SimulatedScreen.Named(screenName);
        foreach (var (fixture, left) in Frames().Select(row => ((string)row[0], (double?)row[1])))
        {
            var frame = screen.Render(await Load(fixture));
            AssertLeft(left, UltimateTimeBar.Measure(frame, new UiScaler(frame.Width, frame.Height)), fixture);
        }
    }

    /// <summary>The skill points and the time bar are read together, in combat.</summary>
    [Fact]
    public async Task The_squad_reading_carries_it()
    {
        var reader = new PartyReader(new TextRecognizer());

        Assert.InRange((await reader.ReadAsync(await Load("laevatain-running"))).UltimateTimeLeft ?? -1, 0.94, 1);
        Assert.Null((await reader.ReadAsync(await Load("sky-no-ultimate"))).UltimateTimeLeft);
    }

    private static void AssertLeft(double? expected, double? read, string? what = null)
    {
        if (expected is not { } e)
        {
            Assert.True(read is null, $"{what}: read {read} where there's no bar");
            return;
        }
        Assert.True(read is { } r && Math.Abs(r - e) <= 0.03, $"{what}: read {read?.ToString() ?? "nothing"}, expected {e}");
    }
}
