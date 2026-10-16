using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using EndHud.Vision;
using EndHud.Vision.Capture;
using Xunit;

namespace EndHud.Tests;

/// <summary>
/// The game in an ordinary window (title bar and borders), as on a laptop where it isn't run
/// full screen: the capture includes the window frame, and EndHud must read only the game's
/// client area. Shows a window for a few seconds.
/// </summary>
[Trait("Category", "Desktop")] // needs an interactive desktop: runs locally, not on CI runners
public class WindowedGameCaptureTests
{
    private static readonly string Fixture = System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "combat-combos-nearly-refilled.jpg");

    [Fact]
    public async Task Windowed_game_is_read_from_its_client_area_not_the_window_frame()
    {
        // EndHud is per-monitor DPI aware (app.manifest); work in physical pixels like it does.
        SetThreadDpiAwarenessContext(PerMonitorAwareV2);
        using var window = BorderedGameWindow.Show(Fixture, new Size(1600, 900));
        using var capture = new WindowCapture(window.Handle);
        await capture.GetFrameAsync(); // the first frame can predate the first full paint
        var frame = await capture.GetFrameAsync();

        // DPI awareness is per thread here (EndHud sets it for the whole process), and the awaits
        // above may have moved this test to a thread-pool thread that measures in scaled units.
        SetThreadDpiAwarenessContext(PerMonitorAwareV2);
        var client = Native.ClientAreaInCapture(window.Handle, frame.Width, frame.Height);
        Assert.True(frame.Height > client.Height, "the capture should include the title bar");
        Assert.Equal(new Size(1600, 900), new Size(client.Width, client.Height));

        var game = frame.Crop(client);
        var expected = (await ImageFile.LoadAsync(Fixture)).Resize(client.Width, client.Height);
        // The client area is the picture: off by the title bar, most pixels would differ.
        Assert.True(MeanDifference(game, expected) < 8, $"client area differs from the picture by {MeanDifference(game, expected):F1} on average");

        var reader = new PartyReader(new TextRecognizer());
        var (read, want) = (await reader.ReadAsync(game), await reader.ReadAsync(expected));
        Assert.True(read.PanelVisible);
        for (int i = 0; i < 4; i++)
        {
            Assert.InRange(read.Slots[i].Hp!.Value, want.Slots[i].Hp!.Value - 0.03, want.Slots[i].Hp!.Value + 0.03);
            Assert.Equal(want.Slots[i].Combo?.Ready, read.Slots[i].Combo?.Ready);
            Assert.Equal(want.Slots[i].Ultimate?.Ready, read.Slots[i].Ultimate?.Ready);
        }
    }

    /// <summary>
    /// When the game's window capture sends nothing (Windows 10 22H2 players), the game is read off
    /// the screen: its client area, with EndHud's overlay kept out of the copy (WDA_EXCLUDEFROMCAPTURE),
    /// even where the overlay covers the squad panel.
    /// </summary>
    [Fact]
    public async Task Read_off_the_screen_the_game_is_its_client_area_without_the_overlay()
    {
        SetThreadDpiAwarenessContext(PerMonitorAwareV2);
        using var window = BorderedGameWindow.Show(Fixture, new Size(1600, 900), topMost: true);
        var area = Native.ClientAreaOnScreen(window.Handle);
        // A stand-in overlay: solid red over the squad panel, kept out of captures like EndHud's.
        using var overlay = BorderedGameWindow.Cover(new Rectangle(area.X, area.Y + area.Height / 2, area.Width / 2, area.Height / 2));
        Assert.True(IsWindowVisible(overlay.Handle), "the stand-in overlay should be on screen");
        var expected = (await ImageFile.LoadAsync(Fixture)).Resize(area.Width, area.Height);

        // The screen shows the window once Windows has composed it, which can take a moment after it
        // opens (a copy taken too early showed the desktop behind it): copy until it's there.
        var game = ScreenCapture.Copy(area);
        for (var waited = System.Diagnostics.Stopwatch.StartNew(); MeanDifference(game, expected) >= 8 && waited.Elapsed < TimeSpan.FromSeconds(5);)
        {
            Thread.Sleep(100);
            game = ScreenCapture.Copy(area);
        }

        Assert.Equal((1600, 900), (game.Width, game.Height));
        Assert.True(MeanDifference(game, expected) < 8, $"screen copy differs from the picture by {MeanDifference(game, expected):F1} on average");
        var reader = new PartyReader(new TextRecognizer());
        var read = await reader.ReadAsync(game);
        Assert.True(read.PanelVisible);
        Assert.Equal(8, read.PanelMarks);
    }

    private static double MeanDifference(Vision.Frame a, Vision.Frame b)
    {
        long sum = 0;
        for (int i = 0; i < a.Bgra.Length; i += 4)
            sum += Math.Abs(a.Bgra[i] - b.Bgra[i]) + Math.Abs(a.Bgra[i + 1] - b.Bgra[i + 1]) + Math.Abs(a.Bgra[i + 2] - b.Bgra[i + 2]);
        return sum / (a.Bgra.Length / 4 * 3.0);
    }

    private static readonly IntPtr PerMonitorAwareV2 = new(-4);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

    /// <summary>An ordinary window (title bar, borders) on its own UI thread, showing one image in its client area.</summary>
    private sealed class BorderedGameWindow : IDisposable
    {
        private readonly Thread _thread;
        private readonly ManualResetEventSlim _shown = new();
        private Form? _form;

        private BorderedGameWindow(Func<Form> make)
        {
            _thread = new Thread(() =>
            {
                SetThreadDpiAwarenessContext(PerMonitorAwareV2);
                _form = make();
                var redraw = new System.Windows.Forms.Timer { Interval = 100 };
                redraw.Tick += (_, _) => _form.Invalidate();
                redraw.Start();
                _form.Shown += (_, _) => _shown.Set();
                Application.Run(_form);
            });
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.IsBackground = true;
            _thread.Start();
            if (!_shown.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Stand-in window didn't open.");
            Thread.Sleep(500); // let it paint
            Handle = (IntPtr)_form!.Invoke(() => _form.Handle);
        }

        public IntPtr Handle { get; }

        public static BorderedGameWindow Show(string imagePath, Size clientSize, bool topMost = false) => new(() =>
        {
            var image = Image.FromFile(imagePath);
            var form = new ImageForm(image)
            {
                Text = "EndHud windowed capture test",
                FormBorderStyle = FormBorderStyle.Sizable,
                StartPosition = FormStartPosition.Manual,
                Location = new Point(40, 40),
                ClientSize = clientSize,
                ShowInTaskbar = false,
                TopMost = topMost,
            };
            form.FormClosed += (_, _) => image.Dispose();
            return form;
        });

        /// <summary>A borderless solid red window over <paramref name="bounds"/>, on top, kept out of screen captures.</summary>
        public static BorderedGameWindow Cover(Rectangle bounds) => new(() =>
        {
            var form = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual,
                Bounds = bounds,
                BackColor = Color.Red,
                ShowInTaskbar = false,
                TopMost = true,
            };
            form.HandleCreated += (_, _) => Native.SetWindowDisplayAffinity(form.Handle, Native.WDA_EXCLUDEFROMCAPTURE);
            return form;
        });

        public void Dispose()
        {
            _form?.BeginInvoke(() => _form.Close());
            _thread.Join(TimeSpan.FromSeconds(5));
        }
    }

    private sealed class ImageForm : Form
    {
        private readonly Image _image;

        public ImageForm(Image image)
        {
            _image = image;
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.Opaque, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            e.Graphics.DrawImage(_image, ClientRectangle);
        }
    }
}
