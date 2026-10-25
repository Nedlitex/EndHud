using System.IO;
using System.Windows;
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

    private enum Spot { Lit, Rim, Outside }

    /// <summary>
    /// Owner, 2026-10-25: "make the counter look more like what the game shows, eg laevatine looks different *use a more
    /// vector style". Each glyph is drawn in the game's 4K pixels, so it is laid over the frame it was measured on and
    /// compared point by point: where the game's counter is lit, the glyph's lit colour; where the game draws its dark
    /// outline or backing (between Laevatain's petals, round Typhoeus's bars and dots, under Zhuang Fangyi's diamonds and
    /// Si's badge), the glyph's dark rim; outside the game's shape, nothing. Each within 50 a channel of the frame.
    /// </summary>
    [Theory]
    [InlineData("melting-flame", "battle-hud/battle-47s.jpg", // 熔火 4: the pink petals, the dark X round them and between
        "L1520,1965 L1552,1965 L1518,1999 L1552,1999 R1535,1964 R1518,1981 R1515,2011 R1505,1962 O1535,1950 O1500,1981")]
    [InlineData("hunting-arrow", "counters/native-20s.jpg", // 猎矢 4: the cyan bars and their end triangles, glowing dark teal between
        "L1520,1959 L1520,1973 L1515,2001 L1491,1962 R1520,1966 R1500,1966 R1530,2010 O1480,1958")]
    [InlineData("sign", "counters/native-33s.jpg", // 启示 8: the white dots on their dark backing
        "L1551,1958 L1566,1972 L1551,1987 L1566,2001 R1559,1972 R1551,1966 R1559,1994 O1580,1980")]
    [InlineData("sunderblade", "counters/zhuang-96s.jpg", // 青霆剑 9: the diamonds on their dark diamond
        "L1523,1948 L1510,1961 L1496,1974 L1523,1974 L1523,2000 R1516,1954 R1503,1967 R1516,1980 R1529,1993 R1523,1938 O1523,1930 O1495,1945")]
    [InlineData("damage-stacks", "si/stacks-14s.jpg", // 伤害层数 0: the green lobes, the hollow middle showing the backing
        "L1535,1962 L1535,2001 L1515,1981 L1555,1981 R1535,1981")]
    [InlineData("damage-stacks", "si/stacks-50_75s.jpg", // her badge's dark stepped backing, on a light scene
        "R1535,1952 R1535,2010 R1506,1981 R1564,1981 O1512,1958 O1500,1955")]
    public void Every_glyph_is_the_game_counter_laid_over_the_frame_it_was_measured_on(string counter, string fixture, string spots)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture);
        var (game, gameStride) = InnKitchenCircleTests.OnSta(() =>
        {
            var frame = new FormatConvertedBitmap(BitmapFrame.Create(new Uri(path)), PixelFormats.Bgra32, null, 0);
            var pixels = new byte[frame.PixelWidth * frame.PixelHeight * 4];
            frame.CopyPixels(pixels, frame.PixelWidth * 4, 0);
            return (pixels, frame.PixelWidth * 4);
        });
        var glyph = Assert.IsType<DrawingImage>(CounterGlyphs.For(counter));
        var (drawn, origin, stride) = InnKitchenCircleTests.OnSta(() =>
        {
            var bounds = glyph.Drawing.Bounds;
            int w = (int)Math.Ceiling(bounds.Width) + 1, h = (int)Math.Ceiling(bounds.Height) + 1;
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.PushTransform(new TranslateTransform(-Math.Floor(bounds.X), -Math.Floor(bounds.Y)));
                dc.DrawDrawing(glyph.Drawing);
            }
            var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var pixels = new byte[w * h * 4];
            bitmap.CopyPixels(pixels, w * 4, 0);
            return (pixels, new Point(Math.Floor(bounds.X), Math.Floor(bounds.Y)), w * 4);
        });

        var wrong = new List<string>();
        foreach (string token in spots.Split(' '))
        {
            var spot = token[0] switch { 'L' => Spot.Lit, 'R' => Spot.Rim, _ => Spot.Outside };
            var xy = token[1..].Split(',').Select(int.Parse).ToArray();
            int g = xy[1] * gameStride + xy[0] * 4;
            int dx = xy[0] - (int)origin.X, dy = xy[1] - (int)origin.Y;
            bool inside = dx >= 0 && dy >= 0 && dx * 4 < stride && dy * stride < drawn.Length; // beyond the drawing: nothing drawn there
            int d = inside ? dy * stride + dx * 4 : 0;
            byte alpha = inside ? drawn[d + 3] : (byte)0;
            (int R, int G, int B) Unpremultiplied() => alpha == 0 ? (0, 0, 0) : (drawn[d + 2] * 255 / alpha, drawn[d + 1] * 255 / alpha, drawn[d] * 255 / alpha);
            var want = (R: (int)game[g + 2], G: (int)game[g + 1], B: (int)game[g]);
            var got = Unpremultiplied();
            bool ok = spot == Spot.Outside
                ? alpha < 30
                : alpha > 220 && Math.Abs(got.R - want.R) <= 50 && Math.Abs(got.G - want.G) <= 50 && Math.Abs(got.B - want.B) <= 50;
            if (!ok) wrong.Add($"{spot} {token[1..]}: game {want}, glyph {got} alpha {alpha}");
        }
        Assert.True(wrong.Count == 0, $"{counter} over {fixture}:{Environment.NewLine}" + string.Join(Environment.NewLine, wrong));
    }
}
