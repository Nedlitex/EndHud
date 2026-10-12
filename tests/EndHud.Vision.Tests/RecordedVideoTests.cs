using EndHud.Vision;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// Runs only when the recording is available (it's 279 MB, so it isn't in the repo): set
/// ENDHUD_TEST_VIDEO to its path, or keep it at %USERPROFILE%\Downloads\endhudtest.mp4.
/// </summary>
public sealed class VideoFactAttribute : FactAttribute
{
    public static string? VideoPath
    {
        get
        {
            string? path = Environment.GetEnvironmentVariable("ENDHUD_TEST_VIDEO");
            if (string.IsNullOrEmpty(path))
                path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "endhudtest.mp4");
            return File.Exists(path) ? path : null;
        }
    }

    public VideoFactAttribute()
    {
        if (VideoPath is null) Skip = "Recording not found; set ENDHUD_TEST_VIDEO to endhudtest.mp4 to run.";
        Timeout = 300_000;
    }
}

/// <summary>
/// The whole 33 s combat recording, one reading per second: values must change the way the
/// game showed them changing, not just be right on a few hand-picked frames.
/// </summary>
public class RecordedVideoTests
{
    private sealed record Sample(double Time, PartyState State);

    private static async Task<List<Sample>> ReadEverySecondAsync()
    {
        var video = await VideoFile.OpenAsync(VideoFactAttribute.VideoPath!);
        var reader = new PartyReader(new TextRecognizer());
        var samples = new List<Sample>();
        for (int t = 0; t < (int)video.Duration.TotalSeconds; t++)
            samples.Add(new Sample(t, await reader.ReadAsync(await video.GetFrameAsync(TimeSpan.FromSeconds(t)))));
        return samples;
    }

    private static readonly Lazy<Task<List<Sample>>> Samples = new(ReadEverySecondAsync);

    private static IEnumerable<Sample> Between(List<Sample> samples, double from, double to) =>
        samples.Where(s => s.Time >= from && s.Time <= to && s.State.PanelVisible);

    [VideoFact]
    public async Task Squad_panel_is_found_except_during_the_ultimate_cutscene()
    {
        var samples = await Samples.Value;
        // 9–10 s is the full-screen ultimate animation, with no HUD; 2 s is a scene transition.
        var missing = samples.Where(s => !s.State.PanelVisible).Select(s => s.Time).ToList();
        Assert.All(missing, t => Assert.Contains(t, new double[] { 2, 9, 10 }));
    }

    [VideoFact]
    public async Task Hp_of_slot_1_drops_once_and_stays_down()
    {
        var samples = await Samples.Value;
        Assert.All(Between(samples, 3, 26), s => Assert.InRange(s.State.Slots[0].Hp!.Value, 0.97, 1.0));
        Assert.All(Between(samples, 27, 33), s => Assert.InRange(s.State.Slots[0].Hp!.Value, 0.80, 0.86));
        // The others aren't hit.
        Assert.All(Between(samples, 3, 33), s => Assert.All(s.State.Slots.Skip(1), slot => Assert.InRange(slot.Hp!.Value, 0.97, 1.0)));
    }

    [VideoFact]
    public async Task Combo_cooldowns_refill_steadily_then_become_ready()
    {
        var samples = await Samples.Value;
        AssertRefills(samples, slot: 0, cooling: (13, 21), readyFrom: 22);
        AssertRefills(samples, slot: 2, cooling: (11, 24), readyFrom: 25);
    }

    [VideoFact]
    public async Task Ultimate_of_slot_4_becomes_ready_and_no_other_does()
    {
        var samples = await Samples.Value;
        Assert.All(Between(samples, 3, 18), s => Assert.False(s.State.Slots[3].Ultimate?.Ready ?? false));
        Assert.All(Between(samples, 20, 33), s => Assert.True(s.State.Slots[3].Ultimate!.Ready));
        Assert.All(Between(samples, 3, 33), s => Assert.All(s.State.Slots.Take(3), slot => Assert.False(slot.Ultimate?.Ready ?? false)));
    }

    [VideoFact]
    public async Task Ultimate_ready_holds_steady_through_the_pulsing_ring()
    {
        // The ready ring pulses, so check far more often than once a second: a reading that
        // flickers between ready and not would show on the HUD.
        var video = await VideoFile.OpenAsync(VideoFactAttribute.VideoPath!);
        for (double t = 19.5; t < 33; t += 0.2)
        {
            var reading = UltimateGauge.Measure(await video.GetFrameAsync(TimeSpan.FromSeconds(t)), slot: 3);
            Assert.True(reading?.Ready, $"slot 4 ultimate not read as ready at {t:F1} s");
        }
        for (double t = 3; t < 18; t += 0.5)
        {
            var reading = UltimateGauge.Measure(await video.GetFrameAsync(TimeSpan.FromSeconds(t)), slot: 3);
            Assert.False(reading?.Ready ?? false, $"slot 4 ultimate read as ready at {t:F1} s, before it was");
        }
    }

    private static void AssertRefills(List<Sample> samples, int slot, (double From, double To) cooling, double readyFrom)
    {
        var progress = Between(samples, cooling.From, cooling.To).Select(s => s.State.Slots[slot].Combo!).ToList();
        Assert.NotEmpty(progress);
        Assert.All(progress, c => Assert.False(c.Ready));
        for (int i = 1; i < progress.Count; i++)
            Assert.True(progress[i].Progress > progress[i - 1].Progress,
                $"slot {slot + 1} cooldown went from {progress[i - 1].Progress:P0} to {progress[i].Progress:P0}");
        Assert.All(Between(samples, readyFrom, 33), s => Assert.True(s.State.Slots[slot].Combo!.Ready));
    }
}
