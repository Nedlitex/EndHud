using EndHud.Vision;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// The gameplay recordings are hundreds of MB, so they aren't in the repo. Tests that need one
/// run only when it's found: in ENDHUD_TEST_VIDEO_DIR if set, else %USERPROFILE%\Downloads.
/// </summary>
public static class Recordings
{
    /// <summary>33 s of combat: slot 1 uses its ultimate (~9 s), 连携 cooldowns, slot 1 takes a hit.</summary>
    public const string Combat = "endhudtest.mp4";

    /// <summary>38 s: slot 1's ultimate charging (1% → ~45%), slot 4's 连携 refilling, two operators hurt.</summary>
    public const string UltimateCharging = "Recording 2026-10-11 172152.mp4";

    public static string? Find(string name)
    {
        string? dir = Environment.GetEnvironmentVariable("ENDHUD_TEST_VIDEO_DIR");
        if (string.IsNullOrEmpty(dir))
            dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        string path = Path.Combine(dir, name);
        return File.Exists(path) ? path : null;
    }
}

public sealed class RecordingFactAttribute : FactAttribute
{
    public RecordingFactAttribute(string recording)
    {
        if (Recordings.Find(recording) is null)
            Skip = $"Recording \"{recording}\" not found; put it in Downloads or set ENDHUD_TEST_VIDEO_DIR.";
        Timeout = 300_000;
    }
}

/// <summary>
/// Whole recordings, sampled through time: values must change the way the game showed them
/// changing, not just be right on a few hand-picked frames.
/// </summary>
public class RecordedVideoTests
{
    private sealed record Sample(double Time, PartyState State);

    private static readonly Dictionary<string, Lazy<Task<List<Sample>>>> Cache = new()
    {
        [Recordings.Combat] = new(() => ReadEverySecondAsync(Recordings.Combat)),
        [Recordings.UltimateCharging] = new(() => ReadEverySecondAsync(Recordings.UltimateCharging)),
    };

    private static async Task<List<Sample>> ReadEverySecondAsync(string recording)
    {
        var video = await VideoFile.OpenAsync(Recordings.Find(recording)!);
        var reader = new PartyReader(new TextRecognizer());
        var samples = new List<Sample>();
        for (int t = 0; t < (int)video.Duration.TotalSeconds; t++)
            samples.Add(new Sample(t, await reader.ReadAsync(await video.GetFrameAsync(TimeSpan.FromSeconds(t)))));
        return samples;
    }

    private static Task<List<Sample>> SamplesOf(string recording) => Cache[recording].Value;

    private static IEnumerable<Sample> Between(List<Sample> samples, double from, double to) =>
        samples.Where(s => s.Time >= from && s.Time <= to && s.State.PanelVisible);

    // ---- Combat recording ----

    [RecordingFact(Recordings.Combat)]
    public async Task Squad_panel_is_found_except_during_the_ultimate_cutscene()
    {
        var samples = await SamplesOf(Recordings.Combat);
        // 9–10 s is the full-screen ultimate animation, with no HUD; 2 s is a scene transition.
        var missing = samples.Where(s => !s.State.PanelVisible).Select(s => s.Time).ToList();
        Assert.All(missing, t => Assert.Contains(t, new double[] { 2, 9, 10 }));
    }

    [RecordingFact(Recordings.Combat)]
    public async Task Hp_of_slot_1_drops_once_and_stays_down()
    {
        var samples = await SamplesOf(Recordings.Combat);
        Assert.All(Between(samples, 3, 26), s => Assert.InRange(s.State.Slots[0].Hp!.Value, 0.97, 1.0));
        Assert.All(Between(samples, 27, 33), s => Assert.InRange(s.State.Slots[0].Hp!.Value, 0.80, 0.86));
        // The others aren't hit.
        Assert.All(Between(samples, 3, 33), s => Assert.All(s.State.Slots.Skip(1), slot => Assert.InRange(slot.Hp!.Value, 0.97, 1.0)));
    }

    [RecordingFact(Recordings.Combat)]
    public async Task Combo_cooldowns_refill_steadily_then_become_ready()
    {
        var samples = await SamplesOf(Recordings.Combat);
        AssertRefills(samples, slot: 0, cooling: (13, 21), readyFrom: 22);
        AssertRefills(samples, slot: 2, cooling: (11, 24), readyFrom: 25);
    }

    [RecordingFact(Recordings.Combat)]
    public async Task Ultimates_all_ready_until_slot_1_uses_its_own()
    {
        var samples = await SamplesOf(Recordings.Combat);
        // Before slot 1's ultimate (~9 s) all four are ready.
        Assert.All(Between(samples, 3, 8), s => Assert.All(s.State.Slots, slot => Assert.True(slot.Ultimate!.Ready)));
        // After it, slot 1's is charging (still empty) and the others stay ready.
        var after = Between(samples, 12, 33).ToList();
        Assert.All(after, s => Assert.False(s.State.Slots[0].Ultimate?.Ready ?? false));
        Assert.All(after, s => Assert.InRange(s.State.Slots[0].Ultimate?.Progress ?? 0, 0, 0.03));
        Assert.All(after, s => Assert.All(s.State.Slots.Skip(1), slot => Assert.True(slot.Ultimate?.Ready ?? true)));
        Assert.True(after.Count(s => s.State.Slots[0].Ultimate is not null) >= after.Count * 0.85,
            "slot 1's charging ultimate should be readable most of the time");
    }

    // ---- Ultimate-charging recording ----

    [RecordingFact(Recordings.UltimateCharging)]
    public async Task Ultimate_charge_of_slot_1_only_grows_and_reaches_about_45_percent()
    {
        var samples = await SamplesOf(Recordings.UltimateCharging);
        var charge = Between(samples, 2, 37)
            .Select(s => (s.Time, Reading: s.State.Slots[0].Ultimate))
            .Where(r => r.Reading is not null)
            .ToList();
        Assert.True(charge.Count >= 25, $"slot 1's ultimate readable in only {charge.Count} of 36 samples");
        Assert.All(charge, r => Assert.False(r.Reading!.Ready));

        // Small and slowly growing at first; a big jump around 31 s; never shrinking (beyond
        // a few percent of measurement noise).
        Assert.All(charge.Where(r => r.Time <= 30), r => Assert.InRange(r.Reading!.Progress, 0, 0.10));
        Assert.All(charge.Where(r => r.Time >= 31), r => Assert.InRange(r.Reading!.Progress, 0.40, 0.50));
        double highest = 0;
        foreach (var (time, reading) in charge)
        {
            Assert.True(reading!.Progress >= highest - 0.04, $"charge fell from {highest:P0} to {reading.Progress:P0} at {time} s");
            highest = Math.Max(highest, reading.Progress);
        }
    }

    [RecordingFact(Recordings.UltimateCharging)]
    public async Task Other_ultimates_stay_ready_while_slot_1_charges()
    {
        var samples = await SamplesOf(Recordings.UltimateCharging);
        Assert.All(Between(samples, 2, 37), s => Assert.All(s.State.Slots.Skip(1), slot => Assert.True(slot.Ultimate?.Ready ?? true)));
    }

    [RecordingFact(Recordings.UltimateCharging)]
    public async Task Combo_of_slot_4_refills_after_use()
    {
        var samples = await SamplesOf(Recordings.UltimateCharging);
        AssertRefills(samples, slot: 3, cooling: (22, 37), readyFrom: 99);
        var cooling = Between(samples, 22, 37).Select(s => s.State.Slots[3].Combo!.Progress).ToList();
        Assert.InRange(cooling.First(), 0.05, 0.15);
        Assert.InRange(cooling.Last(), 0.88, 0.98);
    }

    [RecordingFact(Recordings.UltimateCharging)]
    public async Task Two_operators_are_hurt_at_about_20_seconds()
    {
        var samples = await SamplesOf(Recordings.UltimateCharging);
        Assert.All(Between(samples, 3, 18), s => Assert.All(s.State.Slots, slot => Assert.InRange(slot.Hp!.Value, 0.97, 1.0)));
        Assert.All(Between(samples, 22, 37), s =>
        {
            Assert.InRange(s.State.Slots[0].Hp!.Value, 0.85, 0.91);
            Assert.InRange(s.State.Slots[1].Hp!.Value, 0.87, 0.93);
            Assert.InRange(s.State.Slots[2].Hp!.Value, 0.97, 1.0);
            Assert.InRange(s.State.Slots[3].Hp!.Value, 0.97, 1.0);
        });
    }

    // ---- Frame-by-frame: no flicker in either recording ----

    [RecordingFact(Recordings.Combat)]
    public Task Ultimate_states_never_flicker_in_combat_recording() => AssertNoUltimateFlicker(
        Recordings.Combat, allReadyUntil: 8.5, slot1ChargingFrom: 12, end: 33);

    [RecordingFact(Recordings.UltimateCharging)]
    public Task Ultimate_states_never_flicker_in_charging_recording() => AssertNoUltimateFlicker(
        Recordings.UltimateCharging, allReadyUntil: null, slot1ChargingFrom: 2, end: 37.5);

    /// <summary>
    /// Every 0.25 s — far denser than the HUD's once a second: a reading that flips state even for
    /// one frame would show on the HUD. Includes the moments the game dims the HUD and skill
    /// effects sweep over the circles. A circle may be unreadable (null), never wrong.
    /// </summary>
    private static async Task AssertNoUltimateFlicker(string recording, double? allReadyUntil, double slot1ChargingFrom, double end)
    {
        var video = await VideoFile.OpenAsync(Recordings.Find(recording)!);
        if (allReadyUntil is { } until)
        {
            for (double t = 3; t < until; t += 0.25)
            {
                var frame = await video.GetFrameAsync(TimeSpan.FromSeconds(t));
                for (int slot = 0; slot < 4; slot++)
                    Assert.True(UltimateGauge.Measure(frame, slot)?.Ready ?? true, $"slot {slot + 1} read as charging at {t:F2} s");
            }
        }
        for (double t = slot1ChargingFrom; t < end; t += 0.25)
        {
            var frame = await video.GetFrameAsync(TimeSpan.FromSeconds(t));
            Assert.False(UltimateGauge.Measure(frame, 0)?.Ready ?? false, $"slot 1 read as ready at {t:F2} s");
            for (int slot = 1; slot < 4; slot++)
                Assert.True(UltimateGauge.Measure(frame, slot)?.Ready ?? true, $"slot {slot + 1} read as charging at {t:F2} s");
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
        Assert.All(Between(samples, readyFrom, 99), s => Assert.True(s.State.Slots[slot].Combo!.Ready));
    }
}
