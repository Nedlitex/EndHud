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
        Assert.Equal(7, state.SkillPointMax);
        Assert.True(state.InCombat);
    }

    /// <summary>
    /// How the diagnostics (the player's diagnostics zip, the debug panel, endhud-cli analyze) write the reading: the
    /// points of the bar's size, so a bar of another size shows, and a bar seen with its fill unread isn't "no bar".
    /// </summary>
    [Theory]
    [InlineData(7.0, 7, "7.00/7")]
    [InlineData(1.4, 3, "1.40/3")]
    [InlineData(null, 3, "?/3")]
    [InlineData(null, null, "-")]
    [InlineData(2.0, null, "2.00")]
    public void Diagnostics_write_the_points_of_the_bars_size(double? points, int? segments, string text) =>
        Assert.Equal(text, SkillPointBar.Describe(new PartyState([]) { SkillPoints = points, SkillPointMax = segments }));

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

    /// <summary>
    /// A drawn bar of <paramref name="segments"/> segments holding <paramref name="points"/>, at 4K, 1080p and 720p. The
    /// count exactly; the points to ±0.05, or to a column and a half of the segment where it is narrower than 30 px on
    /// screen (twenty segments are 6 px wide at 1080p, so their fill is read to about a quarter). A segment narrower than
    /// <see cref="SkillPointBar.MinColumns"/> (twenty at 720p, 4 px) isn't read at all: unknown, never a wrong value.
    /// </summary>
    [Theory]
    [MemberData(nameof(Bars))]
    public void Any_count_of_segments_reads_its_points(int segments, double points)
    {
        var frame = SyntheticSkillPoints.Draw(segments, points);
        foreach (var screen in new[] { frame, frame.Resize(1920, 1080), frame.Resize(1280, 720) })
        {
            var ui = new UiScaler(screen.Width, screen.Height);
            var read = SkillPointBar.Measure(screen, ui);
            double columns = ui.BottomCenter(SkillPointBar.Segment(segments, 0)).Width;
            if (columns < SkillPointBar.MinColumns)
            {
                Assert.Null(read?.Value);
                continue;
            }

            Assert.NotNull(read);
            Assert.Equal(segments, read!.Value.Max);
            double within = Math.Max(0.05, 1.5 / columns);
            Assert.NotNull(read.Value.Value);
            Assert.InRange(read.Value.Value!.Value, points - within, points + within);
        }
    }
}
