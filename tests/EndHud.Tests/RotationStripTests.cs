using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace EndHud.Tests;

/// <summary>
/// The rotation strip, rendered off screen: the ready step's cyan ring, the reasons when it isn't,
/// overlapping steps, and a see-through band (no dark box) that fades out at both ends.
/// </summary>
[Collection(WpfCollection.Name)]
public class RotationStripTests
{
    private static StripView Guide(StripStepState current, IReadOnlyList<string>? reasons = null, bool together = true) => new("充能轴 I",
    [
        new StripStep(null, null, "战技", null, StripStepState.Lit, false),
        new StripStep(null, null, "连携技", null, current, false),
        new StripStep(null, null, "连携技", null, StripStepState.Unlit, together),
        new StripStep(null, null, "战技·意", "飞天", StripStepState.Unlit, false),
    ], null, reasons ?? []);

    private sealed record Rendered(int Width, int Height, byte[] Bgra)
    {
        /// <summary>Average opacity of the outermost 4 columns on one side.</summary>
        public double EdgeAlpha(bool left)
        {
            double sum = 0;
            for (int y = 0; y < Height; y++)
            for (int k = 0; k < 4; k++)
            {
                int x = left ? k : Width - 1 - k;
                sum += Bgra[(y * Width + x) * 4 + 3];
            }
            return sum / (Height * 4);
        }

        public int Count(Func<byte, byte, byte, bool> match)
        {
            int n = 0;
            for (int i = 0; i < Bgra.Length; i += 4)
                if (Bgra[i + 3] > 200 && match(Bgra[i + 2], Bgra[i + 1], Bgra[i])) n++;
            return n;
        }
    }

    private static Rendered Render(StripView view) => OnSta(() =>
    {
        var element = (FrameworkElement)RotationStrip.Build(view);
        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        element.Arrange(new Rect(element.DesiredSize));
        int w = (int)Math.Ceiling(element.DesiredSize.Width), h = (int)Math.Ceiling(element.DesiredSize.Height);
        var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var pixels = new byte[w * h * 4];
        bitmap.CopyPixels(pixels, w * 4, 0);
        return new Rendered(w, h, pixels);
    });

    private static bool Cyan(byte r, byte g, byte b) => r < 110 && g > 170 && b > 200;
    private static bool Warning(byte r, byte g, byte b) => r > 230 && g is > 115 and < 160 && b is > 100 and < 145;

    [Fact]
    public void The_step_to_use_now_is_cyan_and_has_no_reasons()
    {
        var ready = Render(Guide(StripStepState.Ready));

        Assert.True(ready.Count(Cyan) > 100, $"{ready.Count(Cyan)} cyan pixels");
        Assert.Equal(0, ready.Count(Warning));
    }

    [Fact]
    public void A_step_not_ready_yet_has_no_cyan_and_says_why_in_red()
    {
        var waiting = Render(Guide(StripStepState.Waiting, ["技力 1.4/2"]));

        Assert.Equal(0, waiting.Count(Cyan));
        Assert.True(waiting.Count(Warning) > 20, $"{waiting.Count(Warning)} red pixels");
        Assert.True(waiting.Height > Render(Guide(StripStepState.Waiting)).Height); // the reasons' line
    }

    private static bool Amber(byte r, byte g, byte b) => r > 230 && g is > 165 and < 220 && b < 120;

    /// <summary>A step seen used is lit: its amber ring shows; with it unlit, no amber anywhere.</summary>
    [Fact]
    public void A_lit_step_shows_amber_and_an_unlit_one_does_not()
    {
        var lit = Render(Guide(StripStepState.Ready));
        var unlit = Render(new StripView("充能轴 I",
            [new StripStep(null, null, "战技", null, StripStepState.Unlit, false), new StripStep(null, null, "连携技", null, StripStepState.Ready, false)], null, []));

        Assert.True(lit.Count(Amber) > 100, $"{lit.Count(Amber)} amber pixels");
        Assert.Equal(0, unlit.Count(Amber));
    }

    private static bool White(byte r, byte g, byte b) => r > 225 && g > 225 && b > 225;

    [Fact]
    public void The_step_the_player_is_on_stands_out_even_when_not_ready()
    {
        var waiting = Render(Guide(StripStepState.Waiting));
        var later = Render(Guide(StripStepState.Unlit));

        // Its white ring, on top of the white text both have.
        Assert.True(waiting.Count(White) > later.Count(White) + 100, $"{waiting.Count(White)} vs {later.Count(White)} white pixels");
    }

    [Fact]
    public void There_is_no_dark_box_behind_it_and_the_band_fades_out_at_both_ends()
    {
        var strip = Render(Guide(StripStepState.Waiting, ["技力 1.4/2"]));

        // Over a white scene, what turns dark: only the icons' discs and the text outlines, never a
        // backdrop. (The see-through band leaves white at about 150; the old dark box made it 90.)
        int dark = 0;
        for (int i = 0; i < strip.Bgra.Length; i += 4)
        {
            int clear = 255 - strip.Bgra[i + 3]; // premultiplied: colour + white behind what isn't covered
            int over = Math.Max(strip.Bgra[i] + clear, Math.Max(strip.Bgra[i + 1] + clear, strip.Bgra[i + 2] + clear));
            if (over < 110) dark++;
        }
        double share = dark / (double)(strip.Width * strip.Height);
        Assert.True(share < 0.12, $"{share:P0} of the strip is a dark backdrop");

        Assert.True(strip.EdgeAlpha(left: true) < 12, $"left edge alpha {strip.EdgeAlpha(left: true):0}");
        Assert.True(strip.EdgeAlpha(left: false) < 12, $"right edge alpha {strip.EdgeAlpha(left: false):0}");
    }

    [Theory]
    [InlineData(0.96, "0.9")]
    [InlineData(1.4, "1.4")]
    [InlineData(2.0, "2")]
    [InlineData(0.0, "0")]
    public void Skill_points_short_of_what_is_needed_never_read_as_enough(double points, string shown) =>
        Assert.Equal(shown, RotationStripView.Points(points));

    [Fact]
    public void Steps_used_together_overlap()
    {
        var apart = Render(Guide(StripStepState.Ready, together: false));
        var together = Render(Guide(StripStepState.Ready, together: true));

        Assert.True(apart.Width - together.Width >= 25, $"{apart.Width} vs {together.Width}");
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
}
