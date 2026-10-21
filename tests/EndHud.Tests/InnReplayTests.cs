using System.IO;
using EndHud.Vision;
using EndHud.Vision.Inn;
using Xunit;
using Xunit.Abstractions;
using static EndHud.Tests.InnTestKit;

namespace EndHud.Tests;

/// <summary>
/// A test that runs only on a developer's machine with a recording of the inn named in ENDHUD_INN_RECORDING (and
/// ENDHUD_INN_TRACE_OUT, a folder for the text it writes); skipped everywhere else.
/// </summary>
public sealed class InnRecordingFactAttribute : FactAttribute
{
    public InnRecordingFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("ENDHUD_INN_RECORDING") is not { Length: > 0 } path || !File.Exists(path)
            || Environment.GetEnvironmentVariable("ENDHUD_INN_TRACE_OUT") is not { Length: > 0 })
            Skip = "set ENDHUD_INN_RECORDING to an inn recording and ENDHUD_INN_TRACE_OUT to a folder for the trace";
    }
}

/// <summary>
/// The session's overlays held steady (owner, live 2026-10-20: "it is still flashing, the display of both banner and
/// the circle should just stay, not flash all the time"), measured by replaying readings through the real session and
/// views (<see cref="InnReplay"/>).
/// </summary>
public class InnReplayTests(ITestOutputHelper output)
{
    /// <summary>The live reading loop's pace on a 4K game (the owner's log: a frame about every 0.3 s in the inn).</summary>
    private const double FrameEvery = 0.3;

    /// <summary>
    /// A recording through the real readers and the reading loop's holds (PartyMonitor.TryInnAsync), one frame every
    /// <see cref="FrameEvery"/> s up to ENDHUD_INN_RECORDING_TO seconds (default: all but the last 10 s): the readings
    /// saved as text (readings.jsonl.gz) and the replay's changes (views.txt) in ENDHUD_INN_TRACE_OUT. No frame is kept.
    /// </summary>
    [InnRecordingFact]
    public async Task Record_the_readings_of_a_recording_and_trace_the_overlays()
    {
        string path = Environment.GetEnvironmentVariable("ENDHUD_INN_RECORDING")!;
        string outFolder = Environment.GetEnvironmentVariable("ENDHUD_INN_TRACE_OUT")!;
        Directory.CreateDirectory(outFolder);
        var text = InnText.TryCreate() ?? throw new InvalidOperationException("needs the Simplified Chinese OCR language");
        var screens = new InnScreens(text, await InnPictures.LoadAsync(Folder, Inn), Inn, Library);
        var monitor = new PartyMonitor(TimeSpan.FromMilliseconds(250)) { InnFrames = new InnFrameReader(screens) };
        var events = new List<InnReplayEvent>();
        TimeSpan now = TimeSpan.Zero;
        monitor.InnUpdated += r => events.Add(new InnReplayEvent(now, r));

        var video = await VideoFile.OpenAsync(path);
        double to = Environment.GetEnvironmentVariable("ENDHUD_INN_RECORDING_TO") is { Length: > 0 } t ? double.Parse(t, System.Globalization.CultureInfo.InvariantCulture)
            : video.Duration.TotalSeconds - 10;
        for (int i = 0; i * FrameEvery < to; i++)
        {
            now = TimeSpan.FromSeconds(Math.Round(i * FrameEvery, 3));
            var frame = await video.GetFrameAsync(now);
            await monitor.TryInnAsync(frame, now, squadPanelSeen: false);
        }
        InnReplay.Save(Path.Combine(outFolder, "readings.jsonl.gz"), events);
        var replay = InnReplay.Run(InnReplay.Load(Path.Combine(outFolder, "readings.jsonl.gz")));
        File.WriteAllText(Path.Combine(outFolder, "views.txt"), replay.Report());
        foreach (var (display, rate) in replay.PerMinute()) output.WriteLine($"{display}: {rate:F1}/min");
    }

    /// <summary>
    /// The owner's 2026-10-20 session (test_yuanyuan3.mp4, 0–430 s, a frame every 0.3 s through the real readers and the
    /// reading loop's holds; Fixtures/inn-replay, text only) replayed through the session and the views. Before the
    /// displays were held steady: 69 banners went and came back within a second and 37 were up for under half a
    /// second (all banners: 60.8 changes a minute); 26 marks went and came back within a second.
    /// </summary>
    [Fact]
    public void Over_the_owners_recorded_session_nothing_flashes()
    {
        var replay = InnReplay.Run(InnReplay.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "inn-replay", "test-yuanyuan3-readings.jsonl.gz")));
        foreach (var (display, rate) in replay.PerMinute()) output.WriteLine($"{display}: {rate:F1} changes/min");

        var banners = Flashes(replay, "banner");
        var marks = Flashes(replay, "mark");
        var circles = Flashes(replay, "circle");
        output.WriteLine($"banners: {banners.HiddenUnderASecond} hidden under a second, {banners.UpUnderHalfASecond} up under half a second");
        output.WriteLine($"marks: {marks.HiddenUnderASecond} hidden under a second, {marks.UpUnderHalfASecond} up under half a second");

        Assert.True(replay.SessionSpan > TimeSpan.FromMinutes(5), "the whole session was replayed");
        Assert.Equal(0, banners.HiddenUnderASecond);
        Assert.True(banners.UpUnderHalfASecond <= 5, $"{banners.UpUnderHalfASecond} banners up under half a second");
        Assert.True(replay.PerMinute()["banner"] <= 30, $"{replay.PerMinute()["banner"]:F1} banner changes a minute");
        Assert.True(marks.HiddenUnderASecond <= 8, $"{marks.HiddenUnderASecond} marks hidden under a second");
        Assert.Equal((0, 0), (circles.HiddenUnderASecond, circles.UpUnderHalfASecond));
    }

    /// <summary>How often a display's item went and came back within a second, and came and went within half a second.</summary>
    private static (int HiddenUnderASecond, int UpUnderHalfASecond) Flashes(InnReplay replay, string display)
    {
        int hidden = 0, up = 0;
        var last = new Dictionary<string, InnViewChange>();
        foreach (var change in replay.Changes.Where(c => c.Display == display))
        {
            if (last.TryGetValue(change.What, out var before))
            {
                double seconds = (change.At - before.At).TotalSeconds;
                if (before.Change == "-" && change.Change == "+" && seconds < 1) hidden++;
                if (before.Change == "+" && change.Change == "-" && seconds < 0.5) up++;
            }
            last[change.What] = change;
        }
        return (hidden, up);
    }

    /// <summary>A trace of saved readings again (ENDHUD_INN_TRACE_OUT/readings.jsonl.gz), without the recording: views.txt.</summary>
    [InnRecordingFact]
    public void Trace_the_overlays_over_saved_readings()
    {
        string outFolder = Environment.GetEnvironmentVariable("ENDHUD_INN_TRACE_OUT")!;
        string saved = Path.Combine(outFolder, "readings.jsonl.gz");
        if (!File.Exists(saved)) return;
        var replay = InnReplay.Run(InnReplay.Load(saved));
        File.WriteAllText(Path.Combine(outFolder, "views.txt"), replay.Report());
        foreach (var (display, rate) in replay.PerMinute()) output.WriteLine($"{display}: {rate:F1}/min");
    }
}
