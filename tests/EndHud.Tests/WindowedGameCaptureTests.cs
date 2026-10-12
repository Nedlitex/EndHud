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
[Trait("Category", "Desktop")] // needs an interactive desktop session (see .github/workflows/tests.yml)
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

    private static double MeanDifference(Vision.Frame a, Vision.Frame b)
    {
        long sum = 0;
        for (int i = 0; i < a.Bgra.Length; i += 4)
            sum += Math.Abs(a.Bgra[i] - b.Bgra[i]) + Math.Abs(a.Bgra[i + 1] - b.Bgra[i + 1]) + Math.Abs(a.Bgra[i + 2] - b.Bgra[i + 2]);
        return sum / (a.Bgra.Length / 4 * 3.0);
    }

    private static readonly IntPtr PerMonitorAwareV2 = new(-4);

    [DllImport("user32.dll")]
    private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

    /// <summary>An ordinary window (title bar, borders) on its own UI thread, showing one image in its client area.</summary>
    private sealed class BorderedGameWindow : IDisposable
    {
        private readonly Thread _thread;
        private readonly ManualResetEventSlim _shown = new();
        private Form? _form;

        private BorderedGameWindow(string imagePath, Size clientSize)
        {
            _thread = new Thread(() =>
            {
                SetThreadDpiAwarenessContext(PerMonitorAwareV2);
                var image = Image.FromFile(imagePath);
                _form = new ImageForm(image)
                {
                    Text = "EndHud windowed capture test",
                    FormBorderStyle = FormBorderStyle.Sizable,
                    StartPosition = FormStartPosition.Manual,
                    Location = new Point(40, 40),
                    ClientSize = clientSize,
                    ShowInTaskbar = false,
                };
                var redraw = new System.Windows.Forms.Timer { Interval = 100 };
                redraw.Tick += (_, _) => _form.Invalidate();
                redraw.Start();
                _form.Shown += (_, _) => _shown.Set();
                Application.Run(_form);
                image.Dispose();
            });
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.IsBackground = true;
            _thread.Start();
            if (!_shown.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Stand-in window didn't open.");
            Thread.Sleep(500); // let it paint
            Handle = (IntPtr)_form!.Invoke(() => _form.Handle);
        }

        public IntPtr Handle { get; }

        public static BorderedGameWindow Show(string imagePath, Size clientSize) => new(imagePath, clientSize);

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
