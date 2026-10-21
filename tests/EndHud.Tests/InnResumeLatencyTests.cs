using System.Diagnostics;
using System.Drawing;
using System.IO;
using EndHud.Vision.Inn;
using Xunit;
using Xunit.Abstractions;
using static EndHud.Tests.InnTestKit;

namespace EndHud.Tests;

/// <summary>
/// Back from an alt-tab (owner, live 2026-10-20: "the capture is too slow"): the real reading loop
/// (<see cref="PartyMonitor"/>, a real window capture, the real inn readers) over a stand-in game
/// window showing the menu screen, its reading turned off and on as the overlay does when another
/// window takes the focus. The window capture is kept through the pause, and the time from "reading
/// on" to the menu screen's first fresh reading goes to the test output (not asserted: a slow test
/// machine is not a defect). Shows a window for a few seconds.
/// </summary>
[Trait("Category", "Desktop")] // needs an interactive desktop: runs locally, not on CI runners
[Collection(WpfCollection.Name)]
public class InnResumeLatencyTests(ITestOutputHelper output)
{
    private static readonly string Fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "inn", "menu-dishes.jpg");

    private const int Rounds = 5;

    [Fact]
    public async Task Back_from_an_alt_tab_the_capture_is_kept_and_the_inn_read_at_once()
    {
        var text = InnText.TryCreate()
            ?? throw new InvalidOperationException("The inn tests need the Simplified Chinese OCR language (Windows Settings > Language).");
        var screens = new InnScreens(text, await InnPictures.LoadAsync(Folder, Inn), Inn, Library);
        using var window = WindowedGameCaptureTests.BorderedGameWindow.Show(Fixture, new Size(1920, 1080));
        using var monitor = new PartyMonitor(TimeSpan.FromMilliseconds(250)) { InnFrames = new InnFrameReader(screens), ReadSquad = false };

        var menuRead = new SemaphoreSlim(0);
        monitor.InnUpdated += r =>
        {
            if (r is { Screen: InnScreenKind.Menu, Fresh: true }) menuRead.Release();
        };
        monitor.SetTarget(window.Handle, true);
        monitor.StartReading();
        Assert.True(await menuRead.WaitAsync(TimeSpan.FromSeconds(20)), "the menu screen was never read off the stand-in window");

        var times = new List<long>();
        for (int round = 0; round < Rounds; round++)
        {
            monitor.SetTarget(window.Handle, false); // another window in front
            await Task.Delay(1500);
            while (menuRead.CurrentCount > 0) await menuRead.WaitAsync();
            var watch = Stopwatch.StartNew();
            monitor.SetTarget(window.Handle, true);
            Assert.True(await menuRead.WaitAsync(TimeSpan.FromSeconds(10)), $"round {round}: no menu reading after the reading came back on");
            times.Add(watch.ElapsedMilliseconds);
        }

        times.Sort();
        output.WriteLine($"reading on -> first fresh menu reading, {Rounds} rounds: median {times[Rounds / 2]} ms, min {times[0]} ms, max {times[^1]} ms ({string.Join(", ", times)})");
        output.WriteLine($"window captures built: {monitor.CapturesStarted}");
        Assert.Equal(1, monitor.CapturesStarted);
    }
}
