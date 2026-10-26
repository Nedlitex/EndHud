using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EndHud.Vision;
using EndHud.Vision.Operators;
using EndHud.Vision.Rotations;
using Xunit;
using Rotation = EndHud.Vision.Rotations.Rotation;

namespace EndHud.Tests;

/// <summary>
/// The battle skill's key in a chip above its step's circle (owner, 2026-10-25: "in the rotation, show the key for battle
/// skill (1-4) show above the circle"): the digit of the operator's squad slot with keyboard and mouse, the face button
/// in that slot's direction with a controller, taken from the squad as the game has it on this reading ("if the squad
/// order changes, but the overall operators are the same, the battle skill key will change based on squad"), and never
/// over the step's label, a neighbour or a badge at any strip size.
/// </summary>
[Collection(WpfCollection.Name)]
public class StripKeyChipTests
{
    private static readonly string[] Squad = ["Laevatain", "Camille", "Arcane", "Ardelia"];

    private static readonly Rotation Rotation = new(Rotation.CurrentFormat, "r", "r", Squad,
    [
        new RotationPhase("p", "充能轴",
        [
            new RotationStep("Ardelia", StepAction.BattleSkill),
            new RotationStep("Camille", StepAction.Combo),
            new RotationStep("Laevatain", StepAction.BattleSkill) { Label = "强化战技" },
            new RotationStep("Arcane", StepAction.Ultimate),
            new RotationStep("Arcane", StepAction.BasicAttack),
        ]),
    ]);

    /// <summary>A battle reading of these operators in this slot order (null: nobody recognised in that slot).</summary>
    internal static PartyState Reading(IReadOnlyList<string?> bySlot, HudLayout layout = HudLayout.KeyboardMouse)
    {
        var slots = bySlot.Select((id, i) => new PartySlot(i + 1, null, 1.0, "", new Vision.Frame(1, 1, new byte[4]), Combo: new ComboReading(1, true),
            Operator: id is null ? null : new OperatorMatch(new OperatorInfo(id, id, id, id + ".png"), 1, true))).ToList();
        return new PartyState(slots, PanelMarks: 8) { SkillPoints = 2, ActiveSlot = 1, Layout = layout };
    }

    private static StripView View(PartyState state) =>
        RotationStripView.Build(Rotation, new RotationPosition(Rotation.Played()[0], true, []), state, RotationStripViewTests.Operators(Squad),
            (_, _) => null, _ => null);

    [Fact]
    public void A_battle_skill_shows_the_key_of_its_operators_slot_and_no_other_step_does()
    {
        var view = View(Reading(Squad));

        Assert.Equal([new StripKey(4, false), null, new StripKey(1, false), null, null], view.Steps.Select(s => s.Key));
    }

    /// <summary>
    /// The owner (2026-10-25): "if the squad order changes, but the overall operators are the same, the battle skill key will
    /// change based on squad". The same four in another order: the same rotation, other keys, and the strip is drawn again.
    /// </summary>
    [Fact]
    public void The_same_four_operators_in_another_order_change_the_keys_and_the_strip_is_drawn_again()
    {
        var before = View(Reading(Squad));
        var reordered = View(Reading(["Ardelia", "Arcane", "Laevatain", "Camille"]));
        var again = View(Reading(Squad));

        Assert.Equal([new StripKey(1, false), null, new StripKey(3, false), null, null], reordered.Steps.Select(s => s.Key));
        Assert.NotEqual(RotationStripView.Key(before), RotationStripView.Key(reordered));
        Assert.Equal(RotationStripView.Key(before), RotationStripView.Key(again));
    }

    [Fact]
    public void With_a_controller_the_key_is_the_face_button_of_the_slots_direction_and_the_strip_is_drawn_again()
    {
        var keyboard = View(Reading(Squad));
        var pad = View(Reading(Squad, HudLayout.Controller));
        var held = View(Reading(Squad, HudLayout.ControllerSkillsHeld));

        Assert.Equal([new StripKey(4, true), null, new StripKey(1, true), null, null], pad.Steps.Select(s => s.Key));
        Assert.Equal(pad.Steps.Select(s => s.Key), held.Steps.Select(s => s.Key));
        Assert.NotEqual(RotationStripView.Key(keyboard), RotationStripView.Key(pad));
        // Left □ slot 1, top △ 2, right ○ 3, bottom × 4.
        Assert.Equal([FaceButton.Square, FaceButton.Triangle, FaceButton.Circle, FaceButton.Cross], Enumerable.Range(1, 4).Select(slot => new StripKey(slot, true).Button));
    }

    [Fact]
    public void An_operator_not_in_the_recognised_squad_gets_no_key()
    {
        var view = View(Reading(["Laevatain", "Camille", "Arcane", null]));

        Assert.Null(view.Steps[0].Key); // Ardelia's slot not named yet
        Assert.Equal(new StripKey(1, false), view.Steps[2].Key);
        Assert.Null(StripKeys.Of(null));
    }

    /// <summary>The editor's preview: keys only while the recognised squad is the rotation's (in any order), by the game's slots.</summary>
    [Fact]
    public void The_editors_preview_shows_keys_only_for_the_squad_recognised_now()
    {
        string?[] reordered = ["Arcane", "Ardelia", "Camille", "Laevatain"];

        var keys = StripKeys.ForPreview(Rotation, reordered, HudLayout.Controller);
        var preview = RotationStripView.ForPhase(Rotation, 0, RotationStripViewTests.Operators(Squad), (_, _) => null, _ => null, keys: keys);

        Assert.Equal([new StripKey(2, true), null, new StripKey(4, true), null, null], preview.Steps.Select(s => s.Key));
        Assert.Null(StripKeys.ForPreview(Rotation, [null, null, null, null], HudLayout.KeyboardMouse));
        Assert.Null(StripKeys.ForPreview(Rotation, ["Laevatain", "Camille", "Arcane", "Si"], HudLayout.KeyboardMouse));
    }

    // ---- Drawn ----

    [Fact]
    public void The_chip_is_drawn_above_the_circle_with_the_digit_or_the_face_button()
    {
        var (digits, glyphs) = OnSta(() =>
        {
            var keyboard = Chips(RotationStrip.Build(View(Reading(Squad))));
            var pad = Chips(RotationStrip.Build(View(Reading(Squad, HudLayout.Controller))));
            return (keyboard.Select(c => string.Concat(Descendants(c.Element).OfType<TextBlock>().Select(t => t.Text))).ToList(),
                pad.Select(c => (Text: Descendants(c.Element).OfType<TextBlock>().Count(), Shapes: Descendants(c.Element).OfType<System.Windows.Shapes.Shape>().Count())).ToList());
        });

        // Ardelia's and Laevatain's, then Ardelia's again in the next phase's preview (the phase plays again).
        Assert.Equal(["4", "1", "4"], digits);
        Assert.Equal(3, glyphs.Count);
        Assert.All(glyphs, g => Assert.True(g.Text == 0 && g.Shapes >= 1, $"{g.Text} words, {g.Shapes} shapes"));
    }

    /// <summary>
    /// The owner's placement: above the circle, never over the step's label, a neighbour's label, chevrons or circle, another
    /// chip or a badge; at 50, 100 and 200 % (the size slider), with keyboard digits and controller glyphs. A group of three
    /// battle skills used together (each overlapping the one before) with the longest label, and the current step's chevrons.
    /// </summary>
    [Theory]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(2.0)]
    public void Chips_never_overlap_a_label_a_neighbour_or_a_badge_even_in_a_group(double scale)
    {
        StripBadge B(string value) => new(null, BadgeGlyph.Other, value, BadgeSign.None, false, Holds: true);
        StripStep Skill(string label, int slot, StripStepState state, bool together, bool pad) =>
            new(null, null, label, null, state, together, Badges: [B("≥100%"), B("=4")]) { Key = new StripKey(slot, pad) };
        foreach (bool pad in new[] { false, true })
        {
            var view = new StripView("爆发轴",
            [
                Skill("强化战技", 1, StripStepState.Lit, false, pad),
                Skill("强化战技", 2, StripStepState.Ready, true, pad),
                Skill("强化战技", 3, StripStepState.Unlit, true, pad),
                new StripStep(null, null, "连携技", null, StripStepState.Unlit, true),
                Skill("战技", 4, StripStepState.Unlit, false, pad),
            ], new StripNext("充能轴 I", [Skill("战技", 2, StripStepState.Plain, false, pad), Skill("强化战技", 1, StripStepState.Plain, true, pad)]), [B("≥2")]);

            Assert.Equal(6, AssertNoOverlap(view, scale, $"group-{(pad ? "controller" : "keyboard")}-{scale * 100:0}"));
        }
    }

    /// <summary>The two recommended rotations waiting for the next release, every phase, keyboard and controller, at 50, 100 and 200 %.</summary>
    [Theory]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(2.0)]
    public void The_recommended_rotations_chips_never_overlap(double scale)
    {
        string folder = Path.Combine(RepositoryRoot(), "rotations");
        var picture = OnSta(() =>
        {
            var bitmap = new WriteableBitmap(4, 4, 96, 96, PixelFormats.Bgra32, null);
            bitmap.Freeze();
            return (BitmapSource)bitmap;
        });
        int chips = 0;
        foreach (string file in Directory.GetFiles(folder, "*.json"))
        {
            var rotation = RotationFormat.FromJson(File.ReadAllText(file));
            var library = RotationStripViewTests.Operators([.. rotation.Squad]);
            foreach (bool pad in new[] { false, true })
            {
                // The game's order isn't the file's: the squad read in reverse.
                var reading = Reading([.. rotation.Squad.Reverse()], pad ? HudLayout.Controller : HudLayout.KeyboardMouse);
                var played = rotation.Played();
                for (int phase = 0; phase < rotation.Phases.Count; phase++)
                {
                    int count = played.Count(p => p.PhaseIndex == phase);
                    if (count == 0) continue;
                    // Mid-round: the first step lit, the second current (its chevrons), the rest not yet.
                    var marks = Enumerable.Range(0, count).Select(i => i == 0 ? StepMark.Lit : i == 1 ? StepMark.Current : StepMark.Unlit).ToList();
                    var view = RotationStripView.ForPhase(rotation, phase, library, (_, _) => picture, _ => picture,
                        new StepPosition(phase, marks, true, []), _ => picture, state: reading, keys: StripKeys.Of(reading));
                    chips += AssertNoOverlap(view, scale, $"{Path.GetFileNameWithoutExtension(file)}-p{phase + 1}-{(pad ? "controller" : "keyboard")}-{scale * 100:0}");
                }
            }
        }
        Assert.True(chips >= 10, $"{chips} chips"); // every battle skill of both, keyboard and controller
    }

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "EndHud.sln"))) return dir.FullName;
        throw new DirectoryNotFoundException("EndHud.sln");
    }

    /// <summary>
    /// Every chip sits above its own circle (centred on it, its bottom above the circle's middle) and touches no label,
    /// chevrons, other circle, other chip or badge. Returns how many chips were drawn. With ENDHUD_STRIP_RENDERS set, the
    /// strip is also saved there as a PNG for review.
    /// </summary>
    private static int AssertNoOverlap(StripView view, double scale, string name) => OnSta(() =>
    {
        var strip = (FrameworkElement)RotationStrip.Build(view);
        // Measured in the space around the scaled strip, so the bounds are the scaled ones (a transform to the scaled
        // element itself leaves its own scale out).
        var root = new Grid { Background = Brushes.DimGray };
        root.Children.Add(new Border { Child = strip, LayoutTransform = new ScaleTransform(scale, scale) });
        root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        root.Arrange(new Rect(root.DesiredSize));
        root.UpdateLayout();
        Rect Bounds(FrameworkElement e) => e.TransformToAncestor(root).TransformBounds(new Rect(0, 0, e.ActualWidth, e.ActualHeight));
        static bool Touch(Rect a, Rect b) { var both = Rect.Intersect(a, b); return !both.IsEmpty && both.Width * both.Height >= 0.5; }

        var steps = Descendants(strip).OfType<StackPanel>().Where(e => e.Tag is StripStep).ToList();
        var chips = new List<(int Step, Rect Box)>();
        var others = new List<(int Step, string What, Rect Box)>();
        for (int i = 0; i < steps.Count; i++)
        {
            var inner = steps[i].Children.OfType<StackPanel>().First(p => p.Tag is StripStepState);
            var label = inner.Children.OfType<TextBlock>().Single();
            var chevrons = inner.Children.OfType<Canvas>().Single();
            var circle = inner.Children.OfType<Grid>().Single();
            var ring = Bounds(circle.Children.OfType<System.Windows.Shapes.Ellipse>().First());
            others.Add((i, "label", Bounds(label)));
            others.Add((i, "chevrons", Bounds(chevrons)));
            others.Add((i, "circle", ring));
            foreach (var (_, badge) in Badges(steps[i])) others.Add((i, "badge", Bounds(badge)));
            foreach (var (key, element) in Chips(steps[i]))
            {
                var box = Bounds(element);
                Assert.True(Math.Abs(box.Left + box.Width / 2 - (ring.Left + ring.Width / 2)) < 1, $"{name}: step {i + 1}'s chip {box} isn't centred over its circle {ring}");
                Assert.True(box.Bottom <= ring.Top + ring.Height / 2, $"{name}: step {i + 1}'s chip {box} reaches below its circle's middle {ring}");
                Assert.True(box.Top >= Bounds(label).Bottom - 0.01, $"{name}: step {i + 1}'s chip {box} is over its label {Bounds(label)}");
                chips.Add((i, box));
            }
        }
        foreach (var finish in Badges(strip).Select(b => Bounds(b.Element)).Where(box => !others.Any(o => o.Box == box))) others.Add((-1, "finish badge", finish));
        foreach (var chip in chips)
        {
            foreach (var other in others.Where(o => o.Step != chip.Step || o.What != "circle"))
                Assert.False(Touch(chip.Box, other.Box), $"{name}: step {chip.Step + 1}'s chip {chip.Box} overlaps step {other.Step + 1}'s {other.What} {other.Box}");
            foreach (var other in chips.Where(c => c.Step != chip.Step))
                Assert.False(Touch(chip.Box, other.Box), $"{name}: the chips of steps {chip.Step + 1} and {other.Step + 1} overlap");
        }

        if (Environment.GetEnvironmentVariable("ENDHUD_STRIP_RENDERS") is { Length: > 0 } folder)
        {
            Directory.CreateDirectory(folder);
            int w = (int)Math.Ceiling(root.DesiredSize.Width), h = (int)Math.Ceiling(root.DesiredSize.Height);
            var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(root);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(folder, name + ".png"));
            png.Save(file);
        }
        return chips.Count;
    });

    private static List<(StripKey Key, FrameworkElement Element)> Chips(DependencyObject root) =>
        Descendants(root).OfType<FrameworkElement>().Where(e => e.Tag is StripKey).Select(e => ((StripKey)e.Tag, e)).ToList();

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
