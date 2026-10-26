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
        Assert.True(distance >= 30, $"the ring round a holding badge differs by only {distance:0.0} from one not holding");
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
        Assert.True(distance >= 30, $"the ring round a met condition step differs by only {distance:0.0} from one not met");
    }

    /// <summary>The met glows drawn under a strip: (glowing, pulsing now) for each.</summary>
    private static List<bool> Glows(StripView view, StripView? before = null) => OnSta(() =>
        Descendants(RotationStrip.Build(view, before)).OfType<UIElement>()
            .Select(e => e.Effect).OfType<System.Windows.Media.Effects.DropShadowEffect>()
            .Where(e => e.Color == RotationStrip.MetGlowColor)
            .Select(e => e.HasAnimatedProperties).ToList());

    private static StripView Mixed(bool firstHolds, bool conditionLit, StripStepState second = StripStepState.Unlit) => new("P",
    [
        new StripStep(null, null, "Condition", null, conditionLit ? StripStepState.Lit : StripStepState.Ready, false, new StripCondition(true), [Points(conditionLit)]),
        new StripStep(null, null, "Battle skill", null, second, false, Badges: [Points(firstHolds), Points(false) with { Value = "≥3" }]),
    ], null, [Points(false) with { Value = "≥1" }]);

    /// <summary>Met: a badge holding and a condition step lit glow (two glows each, a tight and a wide one); not met: no glow at all.</summary>
    [Fact]
    public void Met_glows_and_not_met_does_not()
    {
        Assert.Empty(Glows(Mixed(firstHolds: false, conditionLit: false)));
        Assert.Equal(2, Glows(Mixed(firstHolds: true, conditionLit: false)).Count);
        Assert.Equal(6, Glows(Mixed(firstHolds: true, conditionLit: true)).Count); // the badge, the condition step's badge and its circle
        // The finish condition's badges too.
        var finish = new StripView("P", [new StripStep(null, null, "Battle skill", null, StripStepState.Lit, false)], null, [Points(true)]);
        Assert.Equal(2, Glows(finish).Count);
        // A skill step lit (seen used) is not a condition: its own amber glow, not the met one.
        Assert.Empty(Glows(new StripView("P", [new StripStep(null, null, "Battle skill", null, StripStepState.Lit, false)], null, [])));
    }

    /// <summary>
    /// The pulse starts only where a condition has just become met: first shown met, or met now and not on the strip drawn
    /// before. Drawn again with it still met (another step lit, a picture loaded), the glow is steady; nothing not met moves.
    /// </summary>
    [Fact]
    public void The_pulse_starts_only_when_a_condition_becomes_met()
    {
        var none = Mixed(firstHolds: false, conditionLit: false);
        var badge = Mixed(firstHolds: true, conditionLit: false);
        var both = Mixed(firstHolds: true, conditionLit: true);

        Assert.All(Glows(badge, before: none), pulsing => Assert.True(pulsing));
        Assert.All(Glows(badge, before: badge), pulsing => Assert.False(pulsing));
        // The condition step comes true: its badge and its circle pulse, the badge already holding stays steady.
        Assert.Equal([false, false, true, true, true, true], Glows(both, before: badge).Order());
        // Another step lit, nothing newly met: no pulse.
        Assert.All(Glows(Mixed(true, true, StripStepState.Lit), before: both), pulsing => Assert.False(pulsing));
        // Not met: nothing animates anywhere.
        Assert.Empty(Glows(none, before: badge));
        // A badge put first (the step's own ability short) doesn't make the others new.
        var own = both with { Steps = [both.Steps[0], both.Steps[1] with { Badges = [Points(false) with { Value = "≥1" }, .. both.Steps[1].Badges] }] };
        Assert.All(Glows(own, before: both), pulsing => Assert.False(pulsing));
    }

    /// <summary>
    /// The overlay's redraws (<see cref="StripRedraw"/>): a badge pulses on the reading it comes true, and the strip drawn
    /// again for another reason (a step lit, the frame's look changed) keeps it glowing steadily; hidden and shown again,
    /// it is new on screen and pulses.
    /// </summary>
    [Fact]
    public void The_overlays_redraws_pulse_once_per_becoming_met()
    {
        var steps = OnSta(() =>
        {
            var redraw = new StripRedraw();
            List<bool>? Draw(StripView? view) => redraw.Next(view, out var content)
                ? content is null ? [] : Descendants(content).OfType<UIElement>().Select(e => e.Effect)
                    .OfType<System.Windows.Media.Effects.DropShadowEffect>().Where(e => e.Color == RotationStrip.MetGlowColor)
                    .Select(e => e.HasAnimatedProperties).ToList()
                : null;
            var drawn = new List<List<bool>?>
            {
                Draw(Mixed(false, false)),
                Draw(Mixed(true, false)), // comes true
                Draw(Mixed(true, false)), // the same: not drawn again
                Draw(Mixed(true, false, StripStepState.Lit)), // a step lit
            };
            redraw.Invalidate();
            drawn.Add(Draw(Mixed(true, false, StripStepState.Lit))); // the frame's look changed
            drawn.Add(Draw(null)); // hidden
            drawn.Add(Draw(Mixed(true, false, StripStepState.Lit))); // shown again
            return drawn;
        });

        Assert.Equal<List<bool>?>([[], [true, true], null, [false, false], [false, false], [], [true, true]], steps);
    }

    /// <summary>The glow is outside the layout: every step, badge and the strip are the same size and place met or not, at 50, 100 and 200 %.</summary>
    [Theory]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(2.0)]
    public void Met_or_not_the_layout_is_the_same(double scale)
    {
        List<Rect> Boxes(StripView view) => OnSta(() =>
        {
            var strip = (FrameworkElement)RotationStrip.Build(view);
            // The menu's size slider scales the whole strip.
            var root = new Border { Child = strip, LayoutTransform = new ScaleTransform(scale, scale) };
            root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            root.Arrange(new Rect(root.DesiredSize));
            root.UpdateLayout();
            return Descendants(strip).OfType<FrameworkElement>().Where(e => e.Tag is StripBadge or StripStep or StripStepState)
                .Select(e => e.TransformToAncestor(root).TransformBounds(new Rect(0, 0, e.ActualWidth, e.ActualHeight)))
                .Prepend(new Rect(root.DesiredSize)).ToList();
        });

        Assert.Equal(Boxes(Mixed(false, false)), Boxes(Mixed(true, true)));
    }

    /// <summary>The strip's opacity setting dims the glow with the rest: half the opacity, about half the glow's cover.</summary>
    [Fact]
    public void The_glow_follows_the_strips_opacity()
    {
        // How much the glow adds under the badge, over the dark scene: met minus not met, red and green.
        double Glow(double opacity)
        {
            var met = Render(OneBadge(true), DarkScene, out var box, $"badge-met-opacity-{opacity:0.0}", opacity: opacity);
            var unmet = Render(OneBadge(false), DarkScene, out _, $"badge-unmet-opacity-{opacity:0.0}", opacity: opacity);
            double sum = 0;
            int n = 0;
            for (int y = (int)box.Bottom + 3; y < (int)box.Bottom + 8; y++)
            for (int x = (int)box.Left; x < (int)box.Right; x++)
            {
                var (r1, g1, _) = met.At(x, y);
                var (r2, g2, _) = unmet.At(x, y);
                sum += r1 + g1 - r2 - g2;
                n++;
            }
            return sum / n;
        }
        double full = Glow(1), half = Glow(0.5);
        output.WriteLine($"glow under the badge: +{full:0} at 100 %, +{half:0} at 50 %");
        Assert.InRange(half / full, 0.4, 0.6);
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
                ? Descendants(strip).OfType<System.Windows.Shapes.Ellipse>().First(e => e.Width is double.NaN && e.ActualWidth == 80)
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
