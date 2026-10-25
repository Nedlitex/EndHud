using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace EndHud.Tests;

/// <summary>
/// The rotation strip, rendered off screen: the ready step's cyan ring, no words when it isn't, overlapping
/// steps, a see-through band (no dark box) that fades out at both ends, and the conditions' badges: bright or dim,
/// never over a neighbouring step's.
/// </summary>
[Collection(WpfCollection.Name)]
public class RotationStripTests
{
    private static StripView Guide(StripStepState current, IReadOnlyList<StripBadge>? badges = null, bool together = true) => new("充能轴 I",
    [
        new StripStep(null, null, "战技", null, StripStepState.Lit, false),
        new StripStep(null, null, "连携技", null, current, false, Badges: badges),
        new StripStep(null, null, "连携技", null, StripStepState.Unlit, together),
        new StripStep(null, null, "战技·意", "飞天", StripStepState.Unlit, false),
    ], null, []);

    /// <summary>The current step short of a skill point: its dim skill-point badge.</summary>
    private static readonly StripBadge ShortOfPoints = new(null, BadgeGlyph.SkillPoints, "≥1", BadgeSign.None, Not: false, Holds: false);

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

    /// <summary>Not ready yet: no cyan, and no words in red under the strip (owner, 2026-10-25), only its badge under the step.</summary>
    [Fact]
    public void A_step_not_ready_yet_has_no_cyan_and_no_red_words()
    {
        var waiting = Render(Guide(StripStepState.Waiting, [ShortOfPoints]));

        Assert.Equal(0, waiting.Count(Cyan));
        Assert.Equal(0, waiting.Count(Warning));
        Assert.True(waiting.Height > Render(Guide(StripStepState.Waiting)).Height); // the badge's row
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
        var strip = Render(Guide(StripStepState.Waiting, [ShortOfPoints]));

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

    [Fact]
    public void Steps_used_together_overlap()
    {
        var apart = Render(Guide(StripStepState.Ready, together: false));
        var together = Render(Guide(StripStepState.Ready, together: true));

        Assert.True(apart.Width - together.Width >= 25, $"{apart.Width} vs {together.Width}");
    }

    // ---- Badges (owner, 2026-10-25) ----

    /// <summary>A badge holding now is drawn bright, one not holding (or unknown) dim; the step's own look is the same either way.</summary>
    [Fact]
    public void A_badge_is_bright_while_its_condition_holds_and_dim_while_it_does_not()
    {
        var bright = ShortOfPoints with { Holds = true };

        var drawn = OnSta(() => Badges(RotationStrip.Build(Guide(StripStepState.Waiting, [bright, ShortOfPoints]))).Select(b => (b.Badge, b.Element.Opacity)).ToList());

        Assert.Equal([(bright, 1.0), (ShortOfPoints, RotationStrip.DimBadgeOpacity)], drawn);
        Assert.True(RotationStrip.DimBadgeOpacity <= 0.5);
    }

    /// <summary>A badge under a step not seen used yet is as bright as its condition: the step's dimming isn't put on it.</summary>
    [Fact]
    public void A_badge_under_an_unlit_step_is_not_dimmed_with_the_step()
    {
        var holding = ShortOfPoints with { Holds = true };
        var view = new StripView("P", [new StripStep(null, null, "战技", null, StripStepState.Unlit, false, Badges: [holding])], null, []);

        double opacity = OnSta(() =>
        {
            var badge = Badges(RotationStrip.Build(view)).Single().Element;
            double total = 1;
            for (DependencyObject? at = badge; at is not null; at = LogicalTreeHelper.GetParent(at))
                if (at is UIElement element) total *= element.Opacity;
            return total;
        });

        Assert.Equal(1.0, opacity);
    }

    /// <summary>"Not" (not ready, absent, not running): a slash across the picture; a plain badge has none.</summary>
    [Fact]
    public void A_badge_asking_for_the_opposite_has_a_slash()
    {
        int Slashes(StripBadge badge) => OnSta(() => Descendants(Badges(RotationStrip.Build(Guide(StripStepState.Ready, [badge]))).Single().Element)
            .OfType<System.Windows.Shapes.Line>().Count());

        Assert.Equal(1, Slashes(ShortOfPoints with { Not = true, Value = null }));
        Assert.Equal(0, Slashes(ShortOfPoints));
    }

    /// <summary>
    /// The owner's ask: badges never overlap a neighbouring step's, in a phase with many steps and groups. Three steps used
    /// together (each drawn overlapping the one before), each with three conditions, the widest values there are; and the
    /// finish condition's badges at the end.
    /// </summary>
    [Fact]
    public void Badges_never_overlap_a_neighbouring_steps_even_in_a_group()
    {
        StripBadge B(string value, bool not = false) => new(null, BadgeGlyph.Other, value, BadgeSign.Running, not, Holds: true);
        IReadOnlyList<StripBadge> Three() => [B("≥100%"), B("=4", not: true), B("≥III"), B("<2") with { Or = true }];
        var view = new StripView("爆发轴",
        [
            new StripStep(null, null, "终结技", null, StripStepState.Lit, false, Badges: Three()),
            new StripStep(null, null, "连携技", null, StripStepState.Ready, false, Badges: Three()),
            new StripStep(null, null, "连携技", null, StripStepState.Unlit, true, Badges: Three()),
            new StripStep(null, null, "连携技", "连击", StripStepState.Unlit, true, Badges: Three()),
            new StripStep(null, null, "战技", null, StripStepState.Unlit, false, Badges: Three()),
            new StripStep(null, null, "战技", null, StripStepState.Unlit, false),
        ], null, Three());

        AssertNoOverlap(view);
    }

    /// <summary>The two recommended rotations waiting for the next release, every phase, as the overlay would draw them mid-round.</summary>
    [Fact]
    public void The_recommended_rotations_badges_never_overlap_a_neighbouring_steps()
    {
        string folder = Path.Combine(RepositoryRoot(), "rotations", "next-release");
        var picture = OnSta(() =>
        {
            var bitmap = new System.Windows.Media.Imaging.WriteableBitmap(4, 4, 96, 96, PixelFormats.Bgra32, null);
            bitmap.Freeze();
            return (System.Windows.Media.Imaging.BitmapSource)bitmap;
        });
        int badged = 0;
        foreach (string file in Directory.GetFiles(folder, "*.json"))
        {
            var rotation = Vision.Rotations.RotationFormat.FromJson(File.ReadAllText(file));
            var library = RotationStripViewTests.Operators([.. rotation.Squad]);
            for (int phase = 0; phase < rotation.Phases.Count; phase++)
            {
                var view = RotationStripView.ForPhase(rotation, phase, library, (_, _) => picture, _ => picture, statusIcon: _ => picture);
                badged += view.Steps.Count(s => s.Badges.Count > 0);
                AssertNoOverlap(view);
            }
        }
        Assert.True(badged >= 6, $"{badged} steps with badges"); // 卡缪's 强化战技 steps, 诀's ultimate, the 熔火 condition steps
    }

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "EndHud.sln"))) return dir.FullName;
        throw new DirectoryNotFoundException("EndHud.sln");
    }

    /// <summary>
    /// Every badge lies between the midpoints to its step's neighbours (so it can't reach a neighbour's), no two badges of
    /// different steps touch, and the finish condition's are after the last step's circle.
    /// </summary>
    private static void AssertNoOverlap(StripView view) => OnSta(() =>
    {
        var strip = (FrameworkElement)RotationStrip.Build(view);
        strip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        strip.Arrange(new Rect(strip.DesiredSize));
        strip.UpdateLayout();
        Rect Bounds(FrameworkElement e) => e.TransformToAncestor(strip).TransformBounds(new Rect(0, 0, e.ActualWidth, e.ActualHeight));

        var steps = Descendants(strip).OfType<FrameworkElement>().Where(e => e.Tag is StripStep).ToList();
        var centres = steps.Select(s => Bounds(s).Left + Bounds(s).Width / 2).ToList();
        var owned = new List<(int Step, Rect Box)>();
        for (int i = 0; i < steps.Count; i++)
        {
            double left = i == 0 ? double.NegativeInfinity : (centres[i - 1] + centres[i]) / 2;
            double right = i == steps.Count - 1 ? double.PositiveInfinity : (centres[i] + centres[i + 1]) / 2;
            foreach (var (_, element) in Badges(steps[i]))
            {
                var box = Bounds(element);
                Assert.True(box.Left >= left - 0.5 && box.Right <= right + 0.5,
                    $"step {i + 1}'s badge {box} reaches past its neighbours' midpoints {left:0.#}..{right:0.#}");
                owned.Add((i, box));
            }
        }
        var finish = Badges(strip).Select(b => Bounds(b.Element)).Where(box => !owned.Any(o => o.Box == box)).ToList();
        Assert.Equal(view.Finish.Count, finish.Count);
        double lastCircle = steps.Count == 0 ? 0 : centres[^1] + 40;
        foreach (var box in finish)
        {
            Assert.True(box.Left >= lastCircle - 0.5, $"a finish badge {box} is under the last step (its circle ends at {lastCircle:0.#})");
            owned.Add((-1, box));
        }
        foreach (var a in owned)
        foreach (var b in owned.Where(b => b.Step != a.Step))
        {
            var both = Rect.Intersect(a.Box, b.Box);
            Assert.True(both.IsEmpty || both.Width * both.Height < 0.5, $"badges of steps {a.Step + 1} and {b.Step + 1} overlap: {a.Box} and {b.Box}");
        }
        return 0;
    });

    /// <summary>The badges drawn under <paramref name="root"/>, by what each was drawn for.</summary>
    private static List<(StripBadge Badge, FrameworkElement Element)> Badges(DependencyObject root) =>
        Descendants(root).OfType<FrameworkElement>().Where(e => e.Tag is StripBadge).Select(e => ((StripBadge)e.Tag, e)).ToList();

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var d in Descendants(child)) yield return d;
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
}
