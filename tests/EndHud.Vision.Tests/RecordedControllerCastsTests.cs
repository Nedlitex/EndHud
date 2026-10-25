using EndHud.Vision.Input;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// The 30 fps controller recording (full battle 3 from 25 s) read the way the app reads the game
/// (<see cref="RecordingCasts"/>, endhud-cli trace-video --casts): readings about four a second, the battle-skill corner
/// sampled at 15 Hz between them, every cast judged, against the casts labelled by eye
/// (Recordings/battle-controller-30fps.casts.txt). Every cast there is made with LB held.
/// </summary>
[Collection(Recordings.ThirtyFps)]
public sealed class RecordedControllerCastsTests
{
    private static readonly Lazy<Task<List<RecordedReading>>> Readings = new(async () =>
    {
        var video = await VideoFile.OpenAsync(Recordings.PathOf(Recordings.BattleController));
        var readings = new List<RecordedReading>();
        // Two windows: the first three casts (slots 3, 1, 1), and slot 2's empowered glow with its cast at 19.37.
        foreach (var (from, to) in new[] { (1.0, 10.0), (17.0, 20.5) })
            await foreach (var reading in new RecordingCasts(video, new PartyReader(new TextRecognizer())).ReadAsync(from, to, 2 / 30.0, 4))
                readings.Add(reading);
        return readings;
    });

    private static List<SkillPress> Labels() =>
        FileSkillPresses.Parse(File.ReadAllText(Recordings.PathOf(Recordings.BattleControllerLabels))).TakeUntil(TimeSpan.MaxValue).ToList();

    [Fact]
    public async Task Every_labelled_cast_is_found_on_its_slot_and_nothing_else_is_cast()
    {
        var readings = await Readings.Value;
        Assert.Contains(readings, r => r.State.Layout == HudLayout.ControllerSkillsHeld);
        var casts = readings.SelectMany(r => r.Judging.Casts).ToList();
        var labels = Labels();
        Assert.Equal(4, labels.Count);

        string Said() => "judged: " + string.Join(", ", casts.Select(c => $"{c.Slot?.ToString() ?? "?"}:{c.Source}@{c.At.TotalSeconds:F2}"));
        foreach (var label in labels)
            Assert.True(casts.Any(c => c.Slot == label.Slot && c.Source == Rotations.CastSource.Flash && (c.At - label.At).Duration() <= TimeSpan.FromSeconds(0.3)),
                $"missed {label.Slot}@{label.At.TotalSeconds:F2}; {Said()}");
        Assert.True(casts.All(c => labels.Any(l => l.Slot == c.Slot && (c.At - l.At).Duration() <= TimeSpan.FromSeconds(0.3))), Said());
        Assert.All(casts, c => Assert.True(c.Certain, Said()));
    }

    /// <summary>Slot 2's empowered glow (战技已强化) on the readings: not before it lights (about 18.0 s), on until its cast at 19.37.</summary>
    [Fact]
    public async Task Slot_2s_empowered_glow_reaches_the_readings_while_it_lasts()
    {
        var readings = await Readings.Value;
        string Said() => string.Join(" ", readings.Select(r => $"{r.Seconds:F2}:{r.State.EmpoweredOf(1)?.ToString() ?? "-"}"));
        var before = readings.Where(r => r.Seconds is >= 17.0 and < 17.8).Select(r => r.State.EmpoweredOf(1)).ToList();
        var during = readings.Where(r => r.Seconds is >= 18.6 and < 19.3).Select(r => r.State.EmpoweredOf(1)).ToList();
        Assert.True(before.Contains(false) && !before.Contains(true), Said());
        Assert.True(during.Contains(true) && !during.Contains(false), Said());
        // Nobody else is empowered then.
        Assert.True(readings.Where(r => r.Seconds >= 17.0).All(r => r.State.EmpoweredOf(0) != true && r.State.EmpoweredOf(2) != true && r.State.EmpoweredOf(3) != true), Said());
    }
}
