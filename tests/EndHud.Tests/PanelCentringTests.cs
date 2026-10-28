using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Xunit;

namespace EndHud.Tests;

/// <summary>
/// With the menu open, the rotation strip and the gauges are centred where they were saved (the owner, 2026-10-27:
/// "the rotation and buff are no longer at the middle of the screen"): the strip's middle at RotationCenterX, the
/// gauges' middle at GaugesCenterX and their bottom at GaugesBottomY. Shows the overlay, aimed at a window that
/// doesn't exist, so nothing of the desktop is read.
/// </summary>
[Trait("Category", "Desktop")] // needs an interactive desktop: runs locally, not on CI runners
[Collection(WpfCollection.Name)]
public class PanelCentringTests
{
    [Fact]
    public void With_the_menu_open_the_strip_and_the_gauges_are_centred_where_saved()
    {
        var at = OnSta(() =>
        {
            var config = new HudConfig { PartyOcrEnabled = true, RotationEnabled = true, GaugesEnabled = true, RotationCenterX = 0.5, GaugesCenterX = 0.5, GaugesBottomY = 0.885, AutoShowOnGameFocus = false };
            var window = new OverlayWindow(HudOptions.Parse(["--target", "EndHud test: no such window"]), config);
            try
            {
                window.Show();
                Pump();
                if (!window.HudOn) window.ToggleHud(); // the HUD up when the menu opens, as over the game
                Pump();
                window.ToggleMenu();
                Pump();
                double width = window.HintCanvas.ActualWidth, height = window.HintCanvas.ActualHeight;
                static (double Middle, double Bottom) Where(FrameworkElement frame) =>
                    (Canvas.GetLeft(frame) + frame.RenderSize.Width * Scale(frame) / 2, Canvas.GetTop(frame) + frame.RenderSize.Height * Scale(frame));
                return (Strip: Where(window.RotationFrame), Gauges: Where(window.GaugeFrame), StripWidth: window.RotationFrame.ActualWidth, Width: width, Height: height);
            }
            finally
            {
                window.SkillInput.Dispose();
                window.Close();
            }
        });

        Assert.True(at.StripWidth > 0, "the strip's sample should be shown with the menu open");
        Assert.InRange(at.Strip.Middle, at.Width * 0.5 - 2, at.Width * 0.5 + 2);
        Assert.InRange(at.Gauges.Middle, at.Width * 0.5 - 2, at.Width * 0.5 + 2);
        Assert.InRange(at.Gauges.Bottom, at.Height * 0.885 - 2, at.Height * 0.885 + 2);
    }

    /// <summary>
    /// The owner's case (2026-10-27): the menu already open when the game starts. The overlay follows a stand-in game
    /// window (another process: EndHud never follows its own) that first opens small, then goes to its full size, as
    /// the game does; the samples must end up centred on the full-size window.
    /// </summary>
    [Fact]
    public void Open_before_the_game_grows_the_strip_and_the_gauges_end_up_centred()
    {
        string title = "EndHud stand-in game " + Guid.NewGuid().ToString("N");
        StandInGame? game = null;
        var at = OnSta(() =>
        {
            var config = new HudConfig { PartyOcrEnabled = true, RotationEnabled = true, GaugesEnabled = true, RotationCenterX = 0.5, GaugesCenterX = 0.5, GaugesBottomY = 0.885, AutoShowOnGameFocus = false };
            var window = new OverlayWindow(HudOptions.Parse(["--target", title]), config);
            try
            {
                window.Show();
                window.ToggleMenu();
                Pump(); // on the screen: no game yet
                game = StandInGame.Start(title, 640, 360);
                Pump(20);
                game.Resize(1600, 900);
                Pump(20); // a few ticks: the overlay follows the window
                double width = window.HintCanvas.ActualWidth, height = window.HintCanvas.ActualHeight;
                static (double Middle, double Bottom) Where(FrameworkElement frame) =>
                    (Canvas.GetLeft(frame) + frame.RenderSize.Width * Scale(frame) / 2, Canvas.GetTop(frame) + frame.RenderSize.Height * Scale(frame));
                return (Strip: Where(window.RotationFrame), Gauges: Where(window.GaugeFrame), Width: width, Height: height, Found: window.GameFound);
            }
            finally
            {
                window.SkillInput.Dispose();
                window.Close();
                game?.Dispose();
            }
        });

        Assert.True(at.Found, "the overlay should follow the stand-in game");
        Assert.InRange(at.Strip.Middle, at.Width * 0.5 - 2, at.Width * 0.5 + 2);
        Assert.InRange(at.Gauges.Middle, at.Width * 0.5 - 2, at.Width * 0.5 + 2);
        Assert.InRange(at.Gauges.Bottom, at.Height * 0.885 - 2, at.Height * 0.885 + 2);
    }

    private static double Scale(FrameworkElement frame) => (frame.LayoutTransform as System.Windows.Media.ScaleTransform)?.ScaleX ?? 1;

    private static void Pump(int times = 5)
    {
        for (int i = 0; i < times; i++)
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, () => frame.Continue = false);
            Dispatcher.PushFrame(frame);
            Thread.Sleep(50);
        }
    }

    private static T OnSta<T>(Func<T> work)
    {
        T result = default!;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { result = work(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw failure;
        return result;
    }

    /// <summary>A borderless window in another process, standing in for the game: opened at a size, resized later.</summary>
    private sealed class StandInGame : IDisposable
    {
        private readonly Process _process;
        private readonly IntPtr _hwnd;

        private StandInGame(Process process, IntPtr hwnd) => (_process, _hwnd) = (process, hwnd);

        public static StandInGame Start(string title, int width, int height)
        {
            string script = "Add-Type -AssemblyName System.Windows.Forms; " +
                $"$f = New-Object System.Windows.Forms.Form; $f.Text = '{title}'; $f.FormBorderStyle = 'None'; " +
                $"$f.StartPosition = 'Manual'; $f.Left = 0; $f.Top = 0; $f.Width = {width}; $f.Height = {height}; " +
                "[System.Windows.Forms.Application]::Run($f)";
            var process = Process.Start(new ProcessStartInfo("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", script]) { CreateNoWindow = true })!;
            var waited = Stopwatch.StartNew();
            IntPtr hwnd;
            while ((hwnd = FindWindow(null, title)) == IntPtr.Zero)
            {
                if (waited.Elapsed > TimeSpan.FromSeconds(30)) { process.Kill(); throw new TimeoutException("the stand-in game window never opened"); }
                Thread.Sleep(100);
            }
            return new StandInGame(process, hwnd);
        }

        public void Resize(int width, int height) => SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, width, height, 0x4 | 0x10); // NOZORDER, NOACTIVATE

        public void Dispose()
        {
            try { _process.Kill(); } catch (InvalidOperationException) { }
            _process.Dispose();
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string? className, string title);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    }
}
