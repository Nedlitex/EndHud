using EndHud.Vision;
using EndHud.Vision.Input;
using EndHud.Vision.Operators;
using EndHud.Vision.Rotations;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// The 30 fps recording of the fight that started following battle skills from the screen (Tangtang's battle skill
/// not followed, 2026-10-24), read the way the app reads the game (<see cref="RecordingCasts"/>, endhud-cli trace-video
/// --casts): readings about four a second, the battle-skill corner sampled at 15 Hz between them (the live minimum),
/// every cast judged, against the casts labelled by eye (Recordings/battle-casts-30fps.casts.txt).
/// </summary>
[Collection(Recordings.ThirtyFps)]
[Trait("Category", "Slow")] // replays a 30 fps recording: left out of the fast lane (scripts/test.ps1 -Fast)
public sealed class RecordedCastsTests
{
    private static readonly string[] Squad = ["Si", "Last_Rite", "Tangtang", "Arcane"];

    private static readonly Lazy<Task<List<RecordedReading>>> Readings = new(ReadAsync);

    private static async Task<List<RecordedReading>> ReadAsync()
    {
        var video = await VideoFile.OpenAsync(Recordings.PathOf(Recordings.BattleCasts));
        var readings = new List<RecordedReading>();
        // From before the scene light (~14.4 s) to after the cast (~16.4 s) and Tangtang's combo; 15 Hz samples, a
        // reading every fourth (0.27 s). (The whole fight, sampled, takes minutes, and the other recording tests memory.)
        await foreach (var reading in new RecordingCasts(video, new PartyReader(new TextRecognizer())).ReadAsync(13.0, 21.0, 2 / 30.0, 4, prepare: Named))
            readings.Add(reading);
        return readings;
    }

    /// <summary>The squad as labelled (the naming isn't what is tested here).</summary>
    private static PartyState Named(PartyState state) => state with
    {
        Slots = state.Slots.Select((s, i) => s.Empty ? s : s with { Operator = new OperatorMatch(new OperatorInfo(Squad[i], Squad[i], Squad[i], Squad[i] + ".png"), 1, true) }).ToList(),
    };

    private static List<SkillPress> Labels() =>
        FileSkillPresses.Parse(File.ReadAllText(Recordings.PathOf(Recordings.BattleCastsLabels))).TakeUntil(TimeSpan.MaxValue).ToList();

    [Fact]
    public async Task Every_labelled_cast_is_found_on_its_slot_and_nothing_else_is_cast()
    {
        var casts = (await Readings.Value).SelectMany(r => r.Judging.Casts).ToList();
        var labels = Labels();
        Assert.NotEmpty(labels);

        string Said() => "judged: " + string.Join(", ", casts.Select(c => $"{c.Slot?.ToString() ?? "?"}:{c.Source}@{c.At.TotalSeconds:F2}"));
        foreach (var label in labels)
            Assert.True(casts.Any(c => c.Slot == label.Slot && (c.At - label.At).Duration() <= TimeSpan.FromSeconds(0.3)), $"missed {label.Slot}@{label.At.TotalSeconds:F2}; {Said()}");
        // No cast that isn't labelled: no wrong slot, none unattributed, none out of scene light (~14.4 s).
        Assert.True(casts.All(c => labels.Any(l => l.Slot == c.Slot && (c.At - l.At).Duration() <= TimeSpan.FromSeconds(0.3))), Said());
        Assert.All(casts, c => Assert.True(c.Certain, Said()));
    }

    /// <summary>The bug itself: on the burst's first step (Tangtang's battle skill), the strip moves on once she casts it.</summary>
    [Fact]
    public async Task The_burst_moves_past_tangtangs_battle_skill_when_she_casts_it()
    {
        var rotation = RotationFormat.FromJson(File.ReadAllText(Path.Combine(OperatorCache.RepositoryRoot(), "rotations", "next-release", "si-last-rite-arcane-tangtang.json")));
        var selector = new PhaseSelector(rotation);
        var steps = new StepFollower(rotation);
        var visibility = new StripVisibility();
        PartyState? before = null;
        var seen = new List<(double At, string Phase, int Step)>();
        foreach (var reading in await Readings.Value)
        {
            if (visibility.Update(reading.State))
            {
                var shown = selector.Update(reading.State, steps);
                var position = steps.Update(shown.Step.PhaseIndex, before, reading.State, reading.Judging.Casts, selector);
                seen.Add((reading.Seconds, shown.Step.PhaseLabel, position.Marks.ToList().IndexOf(StepMark.Current) + 1));
            }
            before = reading.State;
        }

        string Said() => string.Join(" ", seen.Select(s => $"{s.At:F2}:{s.Phase}/{s.Step}"));
        Assert.True(seen.Where(s => s.At < 16.3).All(s => s is { Phase: "爆发轴 IV", Step: 1 }), Said());
        Assert.True(seen.Where(s => s.At is > 17.0 and < 18.5).All(s => s is { Phase: "爆发轴 IV", Step: 2 }), Said());
    }
}
