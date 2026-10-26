using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;
using Xunit.Abstractions;

namespace EndHud.Tests;

/// <summary>
/// A condition met must be unmistakable at a glance in a fight (owner, 2026-10-25: "when condition is met, glow more
/// obviously to notify user, right now it is hard to tell"): a badge holding and a condition step lit glow round
/// themselves, over a light scene and a dark one, where one not met shows nothing round it. Rendered off screen over solid
/// backgrounds; set ENDHUD_STRIP_RENDERS to a folder to keep the renders.
/// </summary>
[Collection(WpfCollection.Name)]
public class MetGlowTests(ITestOutputHelper output)
{
    internal static readonly Color LightScene = Color.FromRgb(235, 235, 228);
    internal static readonly Color DarkScene = Color.FromRgb(24, 27, 32);

    private static StripBadge Points(bool holds) => new(null, BadgeGlyph.SkillPoints, "≥2", BadgeSign.None, Not: false, Holds: holds);

    private static StripView OneBadge(bool holds) => new("P", [new StripStep(null, null, "Battle skill", null, StripStepState.Unlit, false, Badges: [Points(holds)])], null, []);

    private static StripView ConditionStep(StripStepState state) =>
        new("P", [new StripStep(null, null, "Condition", null, state, false, new StripCondition(SkillPoints: true))], null, []);

    /// <summary>
    /// How far the ring round a badge (2–8 units out at its sides and below it, where nothing else of the strip is) differs
    /// between the badge holding and not, over each scene: the mean colour distance (0–441). Today's amber edge is inside
    /// the pill, so its surroundings were the same either way.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_badge_holding_lights_up_its_surroundings_over_a_light_and_a_dark_scene(bool light)
    {
        var scene = light ? LightScene : DarkScene;
        var met = Render(OneBadge(true), scene, out var box, light ? "badge-met-light" : "badge-met-dark");
        var unmet = Render(OneBadge(false), scene, out var unmetBox, light ? "badge-unmet-light" : "badge-unmet-dark");
        Assert.Equal(box, unmetBox); // the same place and size: the glow is outside the layout

        double distance = RingDistance(met, unmet, box, 2, 8, above: false);
        output.WriteLine($"{(light ? "light" : "dark")} scene: badge ring distance met vs unmet {distance:0.0}");
        Assert.True(distance >= 40, $"the ring round a holding badge differs by only {distance:0.0} from one not holding");
    }

    /// <summary>The same for a condition step's circle: lit (met), the ring 3–12 units round it glows; unlit it doesn't.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_condition_step_met_lights_up_round_its_circle_over_a_light_and_a_dark_scene(bool light)
    {
        var scene = light ? LightScene : DarkScene;
        var met = Render(ConditionStep(StripStepState.Lit), scene, out var circle, light ? "step-met-light" : "step-met-dark", circle: true);
        var unmet = Render(ConditionStep(StripStepState.Unlit), scene, out _, light ? "step-unmet-light" : "step-unmet-dark", circle: true);

        double distance = RingDistance(met, unmet, circle, 3, 12, above: true);
        output.WriteLine($"{(light ? "light" : "dark")} scene: circle ring distance met vs unmet {distance:0.0}");
        Assert.True(distance >= 40, $"the ring round a met condition step differs by only {distance:0.0} from one not met");
    }

    internal sealed record Rendered(int Width, int Height, byte[] Bgra)
    {
        public (byte R, byte G, byte B) At(int x, int y)
        {
            int i = (y * Width + x) * 4;
            return (Bgra[i + 2], Bgra[i + 1], Bgra[i]);
        }
    }

    /// <summary>The mean colour distance between two renders over the ring <paramref name="from"/>..<paramref name="to"/> units outside <paramref name="box"/>.</summary>
    private static double RingDistance(Rendered a, Rendered b, Rect box, double from, double to, bool above)
    {
        double sum = 0;
        int n = 0;
        for (int y = 0; y < a.Height; y++)
        for (int x = 0; x < a.Width; x++)
        {
            double px = x + 0.5, py = y + 0.5;
            double dx = Math.Max(Math.Max(box.Left - px, px - box.Right), 0), dy = Math.Max(Math.Max(box.Top - py, py - box.Bottom), 0);
            if (!above && py < box.Top) continue;
            double d = Math.Sqrt(dx * dx + dy * dy);
            if (d < from || d > to) continue;
            var (r1, g1, b1) = a.At(x, y);
            var (r2, g2, b2) = b.At(x, y);
            sum += Math.Sqrt(Math.Pow(r1 - r2, 2) + Math.Pow(g1 - g2, 2) + Math.Pow(b1 - b2, 2));
            n++;
        }
        return sum / n;
    }

    /// <summary>The strip drawn over a solid scene, opaque; <paramref name="box"/>: the badge's bounds, or the circle's.</summary>
    internal static Rendered Render(StripView view, Color scene, out Rect box, string name, bool circle = false, StripView? before = null, double opacity = 1)
    {
        Rect found = default;
        var rendered = OnSta(() =>
        {
            var strip = (FrameworkElement)RotationStrip.Build(view, before);
            var root = new Grid { Background = new SolidColorBrush(scene) };
            root.Children.Add(new ContentControl { Content = strip, Opacity = opacity, Margin = new Thickness(30) });
            root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            root.Arrange(new Rect(root.DesiredSize));
            root.UpdateLayout();
            FrameworkElement target = circle
                ? Descendants(strip).OfType<System.Windows.Shapes.Ellipse>().First(e => e.Width is double.NaN && e.Parent is Grid { Width: 80 })
                : Descendants(strip).OfType<FrameworkElement>().First(e => e.Tag is StripBadge);
            found = target.TransformToAncestor(root).TransformBounds(new Rect(0, 0, target.ActualWidth, target.ActualHeight));
            int w = (int)Math.Ceiling(root.DesiredSize.Width), h = (int)Math.Ceiling(root.DesiredSize.Height);
            var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(root);
            if (Environment.GetEnvironmentVariable("ENDHUD_STRIP_RENDERS") is { Length: > 0 } folder)
            {
                Directory.CreateDirectory(folder);
                var png = new PngBitmapEncoder();
                png.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(folder, $"{name}.png"));
                png.Save(file);
            }
            var pixels = new byte[w * h * 4];
            bitmap.CopyPixels(pixels, w * 4, 0);
            return new Rendered(w, h, pixels);
        });
        box = found;
        return rendered;
    }

    internal static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var d in Descendants(child)) yield return d;
        }
    }

    internal static T OnSta<T>(Func<T> work)
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
