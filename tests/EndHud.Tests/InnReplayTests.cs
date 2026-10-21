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
