using System.Drawing;
using System.Windows.Forms;
using EndHud.Vision;
using EndHud.Vision.Capture;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// The real capture path: a recorded combat frame is shown in an ordinary window, captured with
/// Windows.Graphics.Capture exactly as the game window is, and must read the same as the file.
/// Shows a full-screen window for a few seconds.
/// </summary>
[Trait("Category", "Desktop")] // needs an interactive desktop session (see .github/workflows/tests.yml)
public class WindowCaptureTests
{
    [Fact]
    public async Task Captured_window_reads_the_same_as_the_recorded_frame()
    {
        string fixture = CombatFrameTests.Fixture("combat-combos-nearly-refilled.jpg");
        var reader = new PartyReader(new TextRecognizer());

        using var window = StandInGameWindow.Show(fixture);
        using var capture = new WindowCapture(window.Handle);
        await capture.GetFrameAsync(); // the first frame can predate the window's first full paint
        var captured = await capture.GetFrameAsync();

        Assert.Equal(window.ClientSize, new Size(captured.Width, captured.Height));
        var expected = await reader.ReadAsync((await ImageFile.LoadAsync(fixture)).Resize(captured.Width, captured.Height));
        var actual = await reader.ReadAsync(captured);

        Assert.True(actual.PanelVisible);
        for (int i = 0; i < 4; i++)
        {
            var (e, a) = (expected.Slots[i], actual.Slots[i]);
            Assert.InRange(a.Hp!.Value, e.Hp!.Value - 0.03, e.Hp.Value + 0.03);
            Assert.Equal(e.Combo!.Ready, a.Combo!.Ready);
            Assert.InRange(a.Combo.Progress, e.Combo.Progress - 0.04, e.Combo.Progress + 0.04);
            Assert.Equal(e.Ultimate!.Ready, a.Ultimate!.Ready);
        }
    }

    /// <summary>A borderless window on its own UI thread showing one image, redrawn like a game.</summary>
    private sealed class StandInGameWindow : IDisposable
    {
        private readonly Thread _thread;
        private readonly ManualResetEventSlim _shown = new();
        private Form? _form;

        private StandInGameWindow(string imagePath)
        {
            _thread = new Thread(() =>
            {
                Application.SetHighDpiMode(HighDpiMode.PerMonitorV2); // physical pixels, like the game
                var image = Image.FromFile(imagePath);
                var screen = Screen.PrimaryScreen!.Bounds;
                var size = new Size(Math.Min(image.Width, screen.Width), Math.Min(image.Height, screen.Height));
                _form = new ImageForm(image)
                {
                    Text = "EndHud capture test",
                    FormBorderStyle = FormBorderStyle.None,
                    StartPosition = FormStartPosition.Manual,
                    Bounds = new Rectangle(screen.Location, size),
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
            ClientSize = (Size)_form.Invoke(() => _form.ClientSize);
        }

        public IntPtr Handle { get; }
        public Size ClientSize { get; }

        public static StandInGameWindow Show(string imagePath) => new(imagePath);

        public void Dispose()
        {
            _form?.BeginInvoke(() => _form.Close());
            _thread.Join(TimeSpan.FromSeconds(5));
        }
    }

    /// <summary>Paints one image to fill the client area, double-buffered so no frame is half-drawn.</summary>
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
