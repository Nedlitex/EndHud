using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace EndHud.Tests;

/// <summary>The rotation strip, rendered off screen: the ready step's gold ring, the reasons when it isn't, overlapping steps.</summary>
public class RotationStripTests
{
    private static StripView Guide(StripStepState current, IReadOnlyList<string>? reasons = null, bool together = true) => new("充能轴 I",
    [
        new StripStep(null, null, "战技", null, StripStepState.Done, false),
        new StripStep(null, null, "连携技", null, current, false),
        new StripStep(null, null, "连携技", null, StripStepState.Upcoming, together),
        new StripStep(null, null, "战技·意", "飞天", StripStepState.Upcoming, false),
    ], reasons ?? []);

    private sealed record Rendered(int Width, int Height, byte[] Bgra)
    {
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

    private static bool Gold(byte r, byte g, byte b) => r > 200 && g > 170 && g < 215 && b < 80;
    private static bool Warning(byte r, byte g, byte b) => r > 220 && g is > 100 and < 150 && b is > 80 and < 130;

    [Fact]
    public void The_step_to_use_now_has_a_gold_ring_and_no_reasons()
    {
        var ready = Render(Guide(StripStepState.Ready));

        Assert.True(ready.Count(Gold) > 100, $"{ready.Count(Gold)} gold pixels");
        Assert.Equal(0, ready.Count(Warning));
    }

    [Fact]
    public void A_step_not_ready_yet_has_no_gold_and_says_why_in_red()
    {
        var waiting = Render(Guide(StripStepState.Waiting, ["技力 1.4/2"]));

        Assert.Equal(0, waiting.Count(Gold));
        Assert.True(waiting.Count(Warning) > 20, $"{waiting.Count(Warning)} red pixels");
        Assert.True(waiting.Height > Render(Guide(StripStepState.Waiting)).Height); // the reasons' line
    }

    private static bool White(byte r, byte g, byte b) => r > 225 && g > 225 && b > 225;

    [Fact]
    public void The_step_the_player_is_on_stands_out_even_when_not_ready()
    {
        var waiting = Render(Guide(StripStepState.Waiting));
        var later = Render(Guide(StripStepState.Upcoming));

        // Its white ring, on top of the white text both have.
        Assert.True(waiting.Count(White) > later.Count(White) + 100, $"{waiting.Count(White)} vs {later.Count(White)} white pixels");
    }

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
