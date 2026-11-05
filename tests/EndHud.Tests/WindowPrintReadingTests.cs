using System.Drawing;
using System.IO;
using EndHud.Vision.Inn;
using Xunit;
using static EndHud.Tests.InnTestKit;

namespace EndHud.Tests;

/// <summary>
/// Windows 10 (issue 8): window capture draws a yellow border round the game there, so the real
/// reading loop copies the game window directly instead (WindowPrint). A stand-in game window
/// showing the inn's menu is read that way, and no window capture is ever started. Shows a window
/// for a few seconds.
/// </summary>
[Trait("Category", "Desktop")] // needs an interactive desktop: runs locally, not on CI runners
[Collection(WpfCollection.Name)]
public class WindowPrintReadingTests
{
    private static readonly string Fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "inn", "menu-dishes.jpg");

    [Fact]
    public async Task Where_the_border_cant_be_hidden_the_game_is_read_from_a_direct_copy_with_no_window_capture()
    {
        var text = InnText.TryCreate()
            ?? throw new InvalidOperationException("The inn tests need the Simplified Chinese OCR language (Windows Settings > Language).");
        var screens = new InnScreens(text, await InnPictures.LoadAsync(Folder, Inn), Inn, Library);
        using var window = WindowedGameCaptureTests.BorderedGameWindow.Show(Fixture, new Size(1920, 1080));
        using var monitor = new PartyMonitor(TimeSpan.FromMilliseconds(250))
        {
            InnFrames = new InnFrameReader(screens),
            ReadSquad = false,
            CopyWindowDirectly = true,
        };

        var menuRead = new SemaphoreSlim(0);
        monitor.InnUpdated += r =>
        {
            if (r is { Screen: InnScreenKind.Menu, Fresh: true }) menuRead.Release();
        };
        monitor.SetTarget(window.Handle, true);
        monitor.StartReading();

        Assert.True(await menuRead.WaitAsync(TimeSpan.FromSeconds(20)), $"the menu screen was never read from the direct copy: {monitor.Describe()}");
        Assert.True(monitor.CapturesStarted == 0, $"window captures started: {monitor.CapturesStarted}; {monitor.Describe()}");
        Assert.Matches(@"from window print, .*; window print: picture seen, mean copy \d+ ms$", monitor.Describe());
    }
}
