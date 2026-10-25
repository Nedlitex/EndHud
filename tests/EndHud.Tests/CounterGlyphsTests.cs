using System.Windows.Media;
using System.Windows.Media.Imaging;
using EndHud.Vision.Operators;
using EndHud.Vision.Rotations;
using Xunit;

namespace EndHud.Tests;

/// <summary>
/// Owner, 2026-10-25: "the counter showing in the rotation condition should be the actual counter icon, not the
/// character icon, or otherwise it is unclear what it means". The wiki has no picture of any counter, so EndHud draws
/// each one (owner's choice): a condition on a counter shows that drawing wherever conditions get a picture.
/// </summary>
public class CounterGlyphsTests
{
    [Fact]
    public void Every_counter_EndHud_reads_has_a_drawn_glyph()
    {
        foreach (var counter in OperatorCounters.Definitions)
        {
            var glyph = Assert.IsType<DrawingImage>(CounterGlyphs.For(counter.Id));
            Assert.True(glyph.IsFrozen, counter.Id); // shared across threads and windows
            Assert.True(glyph.Width > 0 && Math.Abs(glyph.Width - glyph.Height) < 1e-6, $"{counter.Id}: {glyph.Width}x{glyph.Height}, not square");
            Assert.Same(glyph, CounterGlyphs.For(counter.Id)); // one instance: badges compare by it
        }
        Assert.Null(CounterGlyphs.For("no-such-counter"));
    }

    [Fact]
    public void The_glyphs_are_told_apart()
    {
        var all = OperatorCounters.Definitions.Select(d => CounterGlyphs.For(d.Id)).ToList();
        Assert.Equal(all.Count, all.Distinct().Count());
    }

    /// <summary>A counter condition's picture is its glyph, in the alert pop-up, a condition step and a badge alike.</summary>
    [Fact]
    public void A_counter_condition_pictures_its_glyph_and_an_unknown_counter_the_avatar()
    {
        var face = InnKitchenCircleTests.OnSta(() =>
        {
            var bitmap = new WriteableBitmap(2, 2, 96, 96, PixelFormats.Bgra32, null);
            bitmap.Freeze();
            return (BitmapSource)bitmap;
        });
        var library = RotationStripViewTests.Operators(["Laevatain", "Si"]);
        ImageSource? Picture(StateCondition c) =>
            ConditionIcon.Picture(ConditionIcon.Subject([c]), library, _ => null, _ => face, (_, _) => null);

        var flame = new CounterCondition("Laevatain", "melting-flame", Comparison.Exactly, 4);
        Assert.Equal(new AlertSubject(AlertSubjectKind.Counter, "Laevatain", Counter: "melting-flame"), ConditionIcon.Subject([flame]));
        Assert.Same(CounterGlyphs.For("melting-flame"), Picture(flame));
        Assert.Same(CounterGlyphs.For("damage-stacks"), Picture(new NotCondition(new CounterCondition("Si", "damage-stacks", Comparison.AtLeast, 3))));
        // Also with no library loaded yet: the drawing needs none.
        Assert.Same(CounterGlyphs.For("melting-flame"), ConditionIcon.Picture(ConditionIcon.Subject([flame]), null, _ => null, _ => face, (_, _) => null));
        // A counter from a newer file that this build draws no glyph for: the operator's avatar, as before.
        Assert.Same(face, Picture(new CounterCondition("Laevatain", "no-such-counter", Comparison.AtLeast, 1)));
    }
}
