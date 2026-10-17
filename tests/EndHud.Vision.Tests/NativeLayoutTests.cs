using EndHud.Vision;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// The game lays its HUD out afresh at each screen size, so a native screen can differ by a pixel
/// from the 4K layout scaled down (which is all <see cref="SimulatedScreen"/> makes). Measured on a
/// player's native 1920x1080 frame (GitHub issue 1): the combo bars sit on rows 987–989 with three
/// rows of scenery under them before the HP bar at 993, where scaled 4K has them on 988–990 with
/// two. The HP bars and everything left to right are where scaling puts them.
/// </summary>
public class NativeLayoutTests
{
    public static TheoryData<string> CombatFrames() =>
    [
        "combat-all-ready.jpg", "combat-combos-refilling.jpg", "combat-combos-nearly-refilled.jpg", "battle-low-hp.jpg",
    ];

    [Theory]
    [MemberData(nameof(CombatFrames))]
    public async Task Combo_bars_drawn_a_row_higher_at_native_1080p_read_the_same(string fixture)
    {
        var scaled = SimulatedScreen.FullHd.Render(await ImageFile.LoadAsync(CombatFrameTests.Fixture(fixture)));
        var native = BarsOneRowHigher(scaled);
        var reader = new PartyReader(new TextRecognizer());

        var expected = await reader.ReadAsync(scaled);
        var actual = await reader.ReadAsync(native);

        Assert.True(expected.Slots.Count(s => s.Combo is not null) >= 2, "the scaled frame should show combo bars");
        for (int i = 0; i < PartyLayout.SlotCount; i++)
        {
            var (e, a) = (expected.Slots[i].Combo, actual.Slots[i].Combo);
            if (e is null) continue;
            Assert.True(a is not null, $"slot {i + 1}: combo unreadable, {e.Progress:P0} when scaled");
            Assert.Equal(e.Ready, a.Ready);
            Assert.InRange(a.Progress, e.Progress - 0.04, e.Progress + 0.04);
        }
    }

    /// <summary>
    /// A scaled 1080p frame laid out as the game draws native 1080p: rows 0–990 move up one, and
    /// the row freed above the gap is filled with the gap's scenery (row 992).
    /// </summary>
    private static Frame BarsOneRowHigher(Frame f)
    {
        Assert.Equal((1920, 1080), (f.Width, f.Height));
        int stride = f.Width * 4;
        var pixels = (byte[])f.Bgra.Clone();
        Buffer.BlockCopy(f.Bgra, stride, pixels, 0, 990 * stride);
        Buffer.BlockCopy(f.Bgra, 992 * stride, pixels, 990 * stride, stride);
        return new Frame(f.Width, f.Height, pixels);
    }
}
