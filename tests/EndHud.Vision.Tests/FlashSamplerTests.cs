using EndHud.Vision;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// The fast path as the app runs it: the corner of each frame of a 30 fps recording handed to the sampler, as the
/// window capture hands it (<see cref="Capture.WindowCapture.SetCornerTap"/>), and the flashes taken by readings.
/// </summary>
[Collection(Recordings.ThirtyFps)]
[Trait("Category", "Slow")] // replays a 30 fps recording: left out of the fast lane (scripts/test.ps1 -Fast)
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
            sampler.Sample(corner, new UiScaler(video.Width, video.Height).Scale, at, HudLayout.KeyboardMouse);
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

    /// <summary>
    /// The controller recording's corner (both diamonds) from <paramref name="from"/> to <paramref name="to"/> seconds, every
    /// <paramref name="every"/>-th frame, as the app samples it in the controller layout; the flashes, and the empowered
    /// verdict by slot (squad of four) at each time asked for.
    /// </summary>
    private static async Task<(List<FlashEvent> Flashes, Dictionary<double, IReadOnlyList<bool?>> Empowered)> SampleControllerAsync(
        double from, double to, int every, params double[] askAt)
    {
        var video = await VideoFile.OpenAsync(Recordings.PathOf(Recordings.BattleController));
        var sampler = new FlashSampler();
        var region = BattleSkillFlash.Region(video.Width, video.Height, HudLayout.Controller);
        double scale = new UiScaler(video.Width, video.Height).Scale;
        var flashes = new List<FlashEvent>();
        var empowered = new Dictionary<double, IReadOnlyList<bool?>>();
        for (int frame = (int)Math.Round(from * 30); frame <= to * 30; frame += every)
        {
            var at = TimeSpan.FromSeconds(frame / 30.0);
            sampler.Sample(await video.GetRegionAsync(at, region), scale, at, HudLayout.Controller);
            foreach (double t in askAt.Where(t => Math.Abs(t - at.TotalSeconds) < every / 60.0)) empowered[t] = sampler.Empowered(4);
        }
        flashes.AddRange(sampler.Take());
        return (flashes, empowered);
    }

    [Fact]
    public async Task With_a_controller_each_cast_is_one_fill_on_its_own_slot_in_the_held_diamond()
    {
        // Labelled by eye: slot 3 at 1.97, slot 1 at 2.87 (Recordings/battle-controller-30fps.casts.txt).
        var (flashes, _) = await SampleControllerAsync(1.0, 4.0, every: 1);
        Assert.Equal([(2, FlashKind.Fill, SkillButtonPlaces.DiamondHeld), (0, FlashKind.Fill, SkillButtonPlaces.DiamondHeld)],
            flashes.Select(f => (f.Button, f.Kind, f.Places)));
        Assert.InRange(flashes[0].At.TotalSeconds, 1.9, 2.1);
        Assert.InRange(flashes[1].At.TotalSeconds, 2.8, 3.0);
    }

    [Fact]
    public async Task With_a_controller_at_15_hz_too()
    {
        var (flashes, _) = await SampleControllerAsync(1.0, 4.0, every: 2);
        Assert.Equal([2, 0], flashes.Where(f => f.Kind == FlashKind.Fill).Select(f => f.Button));
    }

    [Fact]
    public async Task Slot_2s_empowered_glow_is_told_while_it_lasts_and_not_before()
    {
        // Its rim glows from about 18.0 s until its cast at 19.37.
        var (_, empowered) = await SampleControllerAsync(16.0, 19.2, every: 2, 17.0, 18.8, 19.1);
        Assert.Equal([false, false, false, false], empowered[17.0]);
        Assert.Equal([false, true, false, false], empowered[18.8]);
        Assert.Equal(true, empowered[19.1][1]);
    }

    [Fact]
    public void Sampling_counts_are_described_without_any_picture()
    {
        var sampler = new FlashSampler();
        var rest = new Frame(820, 370, Enumerable.Repeat((byte)60, 820 * 370 * 4).ToArray());
        sampler.Sample(rest, 1, TimeSpan.Zero, HudLayout.KeyboardMouse);
        Assert.Matches(@"^battle-skill flash: 1 samples, \d+\.\d\d ms each; Fill 0, EmpoweredFill 0, EmpoweredCast 0, EmpoweredStart 0$", sampler.Describe());
    }
}
