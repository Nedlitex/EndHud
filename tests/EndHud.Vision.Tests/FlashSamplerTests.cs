using EndHud.Vision;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// The fast path as the app runs it: the corner of each frame of a 30 fps recording handed to the sampler, as the
/// window capture hands it (<see cref="Capture.WindowCapture.SetCornerTap"/>), and the flashes taken by readings.
/// </summary>
[Collection(Recordings.ThirtyFps)]
public sealed class FlashSamplerTests
{
    /// <summary>Feed the corner of every <paramref name="every"/>-th frame from <paramref name="from"/> to <paramref name="to"/> seconds; take every 0.25 s.</summary>
    private static async Task<List<FlashEvent>> SampleAsync(double from, double to, int every)
    {
        var video = await VideoFile.OpenAsync(Recordings.PathOf(Recordings.BattleCasts));
        var sampler = new FlashSampler();
        var taken = new List<FlashEvent>();
        double nextTake = from + 0.25;
        for (int frame = (int)Math.Round(from * 30); frame <= to * 30; frame += every)
        {
            var at = TimeSpan.FromSeconds(frame / 30.0);
            var corner = await video.GetRegionAsync(at, BattleSkillFlash.Region(video.Width, video.Height));
            sampler.Sample(corner, new UiScaler(video.Width, video.Height).Scale, at);
            if (at.TotalSeconds >= nextTake)
            {
                taken.AddRange(sampler.Take());
                nextTake += 0.25;
            }
        }
        taken.AddRange(sampler.Take());
        return taken;
    }

    [Fact]
    public async Task Tangtangs_cast_is_one_fill_on_slot_3()
    {
        var flashes = await SampleAsync(15.5, 17.5, every: 1);
        var fill = Assert.Single(flashes);
        Assert.Equal((2, FlashKind.Fill), (fill.Button, fill.Kind));
        Assert.InRange(fill.At.TotalSeconds, 16.3, 16.5);
    }

    [Fact]
    public async Task At_15_hz_too()
    {
        var flashes = await SampleAsync(15.5, 17.5, every: 2);
        var fill = Assert.Single(flashes);
        Assert.Equal((2, FlashKind.Fill), (fill.Button, fill.Kind));
    }

    [Fact]
    public async Task Scene_light_behind_three_buttons_is_no_flash()
    {
        Assert.Empty(await SampleAsync(13.5, 15.5, every: 1));
    }

    /// <summary>The corner decoded on its own is the same pixels as that corner of the whole frame.</summary>
    [Fact]
    public async Task A_region_of_a_frame_is_that_corner_of_the_whole_frame()
    {
        var video = await VideoFile.OpenAsync(Recordings.PathOf(Recordings.BattleCasts));
        var at = TimeSpan.FromSeconds(16.43);
        var region = BattleSkillFlash.Region(video.Width, video.Height);

        var corner = await video.GetRegionAsync(at, region);
        var whole = (await video.GetFrameAsync(at)).Crop(region);

        Assert.Equal((whole.Width, whole.Height), (corner.Width, corner.Height));
        Assert.Equal(whole.Bgra, corner.Bgra);
    }

    [Fact]
    public void Sampling_counts_are_described_without_any_picture()
    {
        var sampler = new FlashSampler();
        var rest = new Frame(820, 370, Enumerable.Repeat((byte)60, 820 * 370 * 4).ToArray());
        sampler.Sample(rest, 1, TimeSpan.Zero);
        Assert.Matches(@"^battle-skill flash: 1 samples, \d+\.\d\d ms each; Fill 0, EmpoweredFill 0, EmpoweredCast 0, EmpoweredStart 0$", sampler.Describe());
    }
}
