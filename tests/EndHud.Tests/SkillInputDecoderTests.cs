using EndHud.SkillInput;
using Xunit;

namespace EndHud.Tests;

/// <summary>
/// <see cref="SkillInputDecoder"/>: pad data in, "slot N was cast" out. On a controller, hold L1/LB and
/// press the face button in the slot's place on the diamond: left 1, top 2, right 3, bottom 4 (owner,
/// 2026-10-23). (The keyboard is hotkeys: <see cref="SkillHotkeysTests"/>.)
/// </summary>
public class SkillInputDecoderTests
{
    private static SkillInputDecoder Defaults() => new();

    [Theory]
    [InlineData("West", 1)]
    [InlineData("North", 2)]
    [InlineData("East", 3)]
    [InlineData("South", 4)]
    public void Each_chord_casts_its_slot(string button, int slot)
    {
        var decoder = Defaults();
        var face = Enum.Parse<PadButtons>(button);

        Assert.Empty(decoder.Pad(1, PadButtons.Shoulder));
        Assert.Equal([slot], decoder.Pad(1, PadButtons.Shoulder | face));
    }

    [Fact]
    public void Pressing_the_shoulder_and_the_face_button_together_casts()
    {
        Assert.Equal([2], Defaults().Pad(1, PadButtons.Shoulder | PadButtons.North));
    }

    [Theory]
    [InlineData("West")]
    [InlineData("North")]
    [InlineData("East")]
    [InlineData("South")]
    public void A_face_button_without_the_shoulder_casts_nothing(string button)
    {
        var decoder = Defaults();
        var face = Enum.Parse<PadButtons>(button);

        Assert.Empty(decoder.Pad(1, face));
        Assert.Empty(decoder.Pad(1, PadButtons.None));
        Assert.Empty(decoder.Pad(1, face));
    }

    [Fact]
    public void Holding_the_chord_casts_once()
    {
        var decoder = Defaults();

        Assert.Equal([1], decoder.Pad(1, PadButtons.Shoulder | PadButtons.West));
        Assert.Empty(decoder.Pad(1, PadButtons.Shoulder | PadButtons.West));
        Assert.Empty(decoder.Pad(1, PadButtons.Shoulder));
        Assert.Equal([1], decoder.Pad(1, PadButtons.Shoulder | PadButtons.West));
    }

    [Fact]
    public void A_face_button_held_before_the_shoulder_casts_nothing()
    {
        var decoder = Defaults();

        Assert.Empty(decoder.Pad(1, PadButtons.South));
        Assert.Empty(decoder.Pad(1, PadButtons.South | PadButtons.Shoulder));
    }

    [Fact]
    public void Two_pads_are_followed_apart()
    {
        var decoder = Defaults();

        Assert.Equal([1], decoder.Pad(1, PadButtons.Shoulder | PadButtons.West));
        Assert.Equal([1], decoder.Pad(2, PadButtons.Shoulder | PadButtons.West)); // not a repeat: another pad
        Assert.Empty(decoder.Pad(1, PadButtons.Shoulder | PadButtons.West));
    }

    [Fact]
    public void A_forgotten_pad_starts_afresh()
    {
        var decoder = Defaults();

        decoder.Pad(1, PadButtons.Shoulder | PadButtons.West);
        decoder.Forget(1);

        Assert.Equal([1], decoder.Pad(1, PadButtons.Shoulder | PadButtons.West));
    }

    [Fact]
    public void Sony_usages_map_to_their_places()
    {
        Assert.Equal(PadButtons.Shoulder | PadButtons.West, SkillInputDecoder.FromSonyUsages([5, 1]));
        Assert.Equal(PadButtons.South, SkillInputDecoder.FromSonyUsages([2]));
        Assert.Equal(PadButtons.East, SkillInputDecoder.FromSonyUsages([3]));
        Assert.Equal(PadButtons.North, SkillInputDecoder.FromSonyUsages([4]));
        Assert.Equal(PadButtons.None, SkillInputDecoder.FromSonyUsages([6, 7, 8, 9, 10, 13, 14])); // R1, triggers, share, options, PS, touchpad
    }

    [Fact]
    public void Xbox_lb_and_x_casts_slot_1() => Assert.Equal([1], XboxChord(0x0100 | 0x4000));

    [Fact]
    public void Xbox_lb_and_y_casts_slot_2() => Assert.Equal([2], XboxChord(0x0100 | 0x8000));

    [Fact]
    public void Xbox_lb_and_b_casts_slot_3() => Assert.Equal([3], XboxChord(0x0100 | 0x2000));

    [Fact]
    public void Xbox_lb_and_a_casts_slot_4() => Assert.Equal([4], XboxChord(0x0100 | 0x1000));

    [Fact]
    public void Xbox_rb_and_a_casts_nothing() => Assert.Empty(XboxChord(0x0200 | 0x1000));

    [Fact]
    public void Xbox_a_alone_casts_nothing() => Assert.Empty(XboxChord(0x1000));

    private static IReadOnlyList<int> XboxChord(ushort buttons) =>
        Defaults().Pad(-1, SkillInputDecoder.FromXInput(buttons));
}
