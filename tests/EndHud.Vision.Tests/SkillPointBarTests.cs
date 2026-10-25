using EndHud.Vision;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// The skill-point bar (<see cref="SkillPointBar"/>) with any number of segments: three in most fights, more in some
/// (owner, 2026-10-25: seven in a boss fight, "can be more than 3"). The row keeps its width and the segments get
/// narrower, with the same gap between them (measured on the three- and seven-segment screenshots).
/// </summary>
public class SkillPointBarTests
{
    private static async Task<PartyState> ReadAsync(string fixture) =>
        await new PartyReader(new TextRecognizer()).ReadAsync(await ImageFile.LoadAsync(CombatFrameTests.Fixture(fixture)));

    private const string SevenFull = "skill-points/seven-segments-full.jpg";

    /// <summary>The owner's screenshot: seven segments, all full. It read as no bar at all, so not in combat.</summary>
    [Fact]
    public async Task Seven_full_segments_read_as_seven()
    {
        var state = await ReadAsync(SevenFull);

        Assert.NotNull(state.SkillPoints);
        Assert.Equal(7.0, state.SkillPoints!.Value, 2);
        Assert.True(state.InCombat);
    }

    public static TheoryData<int, double> Bars()
    {
        var data = new TheoryData<int, double>();
        foreach (int n in new[] { 2, 3, 4, 5, 6, 7, 8, 9, 10, 12, 15, 20 })
        {
            data.Add(n, n);           // all full
            data.Add(n, n - 1 + 0.4); // the last one filling
            data.Add(n, 0.5);         // the first one half full, the rest empty
            data.Add(n, 0);           // empty
        }
        return data;
    }

    /// <summary>A drawn bar of <paramref name="segments"/> segments holding <paramref name="points"/>, at 4K and 1080p.</summary>
    [Theory]
    [MemberData(nameof(Bars))]
    public void Any_count_of_segments_reads_its_points(int segments, double points)
    {
        var frame = SyntheticSkillPoints.Draw(segments, points);
        foreach (var screen in new[] { frame, frame.Resize(1920, 1080) })
        {
            var read = SkillPointBar.Measure(screen, new UiScaler(screen.Width, screen.Height));

            Assert.NotNull(read);
            Assert.InRange(read!.Value, points - 0.05, points + 0.05);
        }
    }
}
