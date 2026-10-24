using EndHud.Vision;
using EndHud.Vision.Operators;
using Xunit;

namespace EndHud.Tests;

/// <summary>
/// While the squad naming runs beside the readings, a reading shows the last names it found
/// (<see cref="PartyMonitor.WithNames"/>) instead of waiting for it.
/// </summary>
public class PartyMonitorTests
{
    private static readonly Frame NoPortrait = new(1, 1, new byte[4]);

    private static OperatorMatch Named(string id) => new(new OperatorInfo(id, id, id, id + ".png"), 1, false);

    private static PartySlot Slot(int number, OperatorMatch? named = null) => new(number, 90, 1, "LV.90", NoPortrait) { Operator = named };

    [Fact]
    public void A_reading_shows_the_last_names_while_naming_is_still_running()
    {
        var reading = new PartyState([Slot(1), Slot(2), Slot(3, Named("Perlica")), Slot(4)], PanelMarks: 8);
        OperatorMatch?[] last = [Named("Typhoeus"), null, Named("Arcane"), Named("Liino")];

        var shown = PartyMonitor.WithNames(reading, last);

        // A name this reading already has stays; a slot never named stays unnamed.
        Assert.Equal(["Typhoeus", null, "Perlica", "Liino"], shown.Slots.Select(s => s.Operator?.Operator.Id));
    }

    [Fact]
    public void Off_the_squad_panel_and_before_any_naming_nobody_is_named()
    {
        OperatorMatch?[] last = [Named("Typhoeus"), Named("Arcane"), Named("Perlica"), Named("Liino")];

        // No marks and no level text: a menu or a loading screen.
        PartySlot Unseen(int number) => new(number, null, null, "", NoPortrait);
        var hidden = PartyMonitor.WithNames(new PartyState([Unseen(1), Unseen(2), Unseen(3), Unseen(4)], PanelMarks: 0), last);
        var first = PartyMonitor.WithNames(new PartyState([Slot(1), Slot(2), Slot(3), Slot(4)], PanelMarks: 8), []);

        Assert.All(hidden.Slots, s => Assert.Null(s.Operator));
        Assert.All(first.Slots, s => Assert.Null(s.Operator));
    }

    [Fact]
    public void The_battle_skill_buttons_are_sampled_in_battle_with_keyboard_and_mouse_through_the_window_only()
    {
        var now = TimeSpan.Zero;
        Assert.True(PartyMonitor.SamplesFlashes(HudLayout.KeyboardMouse, fromWindow: true, now, outOfBattle: false));
        // Through an ultimate's cutscene (the panel hidden), and not after.
        Assert.True(PartyMonitor.SamplesFlashes(HudLayout.KeyboardMouse, fromWindow: true, PartyMonitor.FlashSamplingHold, outOfBattle: false));
        Assert.False(PartyMonitor.SamplesFlashes(HudLayout.KeyboardMouse, fromWindow: true, PartyMonitor.FlashSamplingHold + TimeSpan.FromSeconds(0.1), outOfBattle: false));
        // The controller layout's buttons are elsewhere; the screen fallback has no window frames to tap.
        Assert.False(PartyMonitor.SamplesFlashes(HudLayout.Controller, fromWindow: true, now, outOfBattle: false));
        Assert.False(PartyMonitor.SamplesFlashes(null, fromWindow: true, now, outOfBattle: false));
        Assert.False(PartyMonitor.SamplesFlashes(HudLayout.KeyboardMouse, fromWindow: false, now, outOfBattle: false));
        // The mode switch says out of battle (exploring, building): at once, not after the hold.
        Assert.False(PartyMonitor.SamplesFlashes(HudLayout.KeyboardMouse, fromWindow: true, now, outOfBattle: true));
    }
}
