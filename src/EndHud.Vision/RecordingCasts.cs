using EndHud.Vision.Input;
using EndHud.Vision.Rotations;

namespace EndHud.Vision;

/// <summary>A reading of a recording, with the battle skills judged cast at it.</summary>
/// <param name="Judging">What <see cref="ScreenCastJudge"/> decided at this reading, and how the presses taken were judged.</param>
public sealed record RecordedReading(double Seconds, PartyState State, CastJudging Judging);

/// <summary>
/// A recording read the way the app reads the game (endhud-cli trace-video, and the recorded-clip tests): a reading
/// every so often, and between readings the battle-skill corner of the frames sampled for the cast flash
/// (<see cref="FlashSampler"/>), as the window capture hands it on live; each reading is judged for battle skills cast
/// (<see cref="ScreenCastJudge"/>), with a file of presses as the assist if given. The recording must be 30 fps or
/// so for the flash: it lasts 2-10 frames at 30 fps.
/// </summary>
public sealed class RecordingCasts(VideoFile video, PartyReader reader)
{
    /// <param name="from">First reading, in seconds.</param>
    /// <param name="to">Readings up to here (the recording's end if null).</param>
    /// <param name="samplesPerReading">Corner samples per reading, evenly spaced, the reading's own frame the last.</param>
    /// <param name="sampleEvery">Seconds between corner samples (1/30: every frame of a 30 fps recording; 1/15: the live minimum).</param>
    /// <param name="prepare">What to do to each reading before it is judged (e.g. naming the squad as given).</param>
    /// <param name="sampleCorners">Sample the battle-skill corner for the flash; false: readings only (no flashes told).</param>
    public async IAsyncEnumerable<RecordedReading> ReadAsync(double from, double? to, double sampleEvery, int samplesPerReading,
        ISkillPressSource? presses = null, Func<PartyState, PartyState>? prepare = null, bool sampleCorners = true)
    {
        var sampler = new FlashSampler();
        var judge = new ScreenCastJudge();
        PartyState? before = null;
        double end = Math.Min(to ?? double.MaxValue, video.Duration.TotalSeconds - sampleEvery);
        int sample = 0;
        // Both layouts' corners: which one is read goes by the layout the last reading with the squad panel saw, as live.
        var corner = BattleSkillFlash.AnyRegion(video.Width, video.Height);
        double scale = new UiScaler(video.Width, video.Height).Scale;
        var layout = HudLayout.KeyboardMouse;
        for (double t = from; t <= end + 1e-9; t = from + ++sample * sampleEvery)
        {
            var at = TimeSpan.FromSeconds(t);
            bool reading = sample % samplesPerReading == 0;
            if (!reading)
            {
                // Between readings only the corner, decoded on its own (a whole 4K frame per sample runs out of memory).
                if (sampleCorners) sampler.Sample(await video.GetRegionAsync(at, corner), scale, at, layout);
                continue;
            }
            var frame = await video.GetFrameAsync(at);
            var read = await reader.ReadAsync(frame);
            if (read.PanelVisible) layout = read.Layout;
            if (sampleCorners) sampler.Sample(frame.Crop(corner), scale, at, layout);

            var state = read with { ReadAt = at, Flashes = sampler.Take(), Empowered = sampleCorners ? sampler.Empowered(read.SquadSize) : [] };
            if (prepare is not null) state = prepare(state);
            var judging = judge.Update(before, state, presses?.TakeUntil(at) ?? []);
            yield return new RecordedReading(t, state, judging);
            before = state;
        }
    }
}
