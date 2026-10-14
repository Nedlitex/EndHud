using EndHud.Vision;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// The combo hint shown shortly before a combo is ready (<see cref="ComboHints.Lead"/>), with the
/// seconds left: worked out from how fast the cooldown bar is filling.
/// </summary>
public class ComboCountdownTests
{
    private static readonly Frame NoPortrait = new(1, 1, new byte[4]);

    private static PartyState Combat(ComboReading slot1) => new(
        [new PartySlot(1, null, 1, "", NoPortrait, slot1), new PartySlot(2, null, 1, "", NoPortrait, new ComboReading(1, true))], PanelMarks: 8);

    private static ComboReading Cooling(double progress) => new(progress, false);
    private static readonly ComboReading Ready = new(1, true);
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    /// <summary>A bar filling 20% a second: 0.2 at 0 s, 0.4 at 1 s, ... (ready at 4 s).</summary>
    private static ComboHints Filling(TimeSpan lead, double until)
    {
        var hints = new ComboHints { Lead = lead };
        for (double t = 0; t <= until + 1e-9; t += 1) hints.Update(Combat(Cooling(0.2 + 0.2 * t)), S(t));
        return hints;
    }

    [Fact]
    public void Shows_the_seconds_left_once_within_the_lead()
    {
        var hints = new ComboHints { Lead = S(1) };
        hints.Update(Combat(Cooling(0.2)), S(0));
        hints.Update(Combat(Cooling(0.4)), S(1));
        Assert.Empty(hints.Update(Combat(Cooling(0.6)), S(2))); // 2 s left: not yet

        var reading = Combat(Cooling(0.8));
        Assert.Equal([1], hints.Update(reading, S(3)));
        Assert.Equal(1.0, hints.SecondsLeft(1, S(3))!.Value, 1);
        Assert.Equal([1], hints.Update(reading, S(3.5))); // the same reading, half a second later
        Assert.Equal(0.5, hints.SecondsLeft(1, S(3.5))!.Value, 1);
    }

    [Fact]
    public void Once_ready_it_is_the_ready_hint_without_seconds()
    {
        var hints = Filling(S(1), until: 3);

        Assert.Equal([1], hints.Update(Combat(Ready), S(4)));
        Assert.Null(hints.SecondsLeft(1, S(4)));
    }

    [Fact]
    public void No_early_hint_without_a_lead()
    {
        var hints = Filling(TimeSpan.Zero, until: 2);

        Assert.Empty(hints.Update(Combat(Cooling(0.8)), S(3)));
    }

    [Fact]
    public void No_early_hint_before_the_filling_speed_is_known()
    {
        var hints = new ComboHints { Lead = S(3) };

        Assert.Empty(hints.Update(Combat(Cooling(0.9)), S(0))); // one reading: no speed yet
    }

    [Fact]
    public void Ready_combos_that_never_cooled_get_no_countdown()
    {
        var hints = new ComboHints { Lead = S(1) };
        hints.Update(Combat(Cooling(0.5)), S(0));

        Assert.Null(hints.SecondsLeft(2, S(0)));
    }

    /// <summary>A bar that fills unevenly: fast at first (an optimistic estimate), then slowly.</summary>
    [Fact]
    public void Once_up_it_stays_until_ready_even_when_the_estimate_runs_out()
    {
        var hints = new ComboHints { Lead = S(1) };
        hints.Update(Combat(Cooling(0.2)), S(0));
        hints.Update(Combat(Cooling(0.6)), S(1));
        var atTwo = Combat(Cooling(0.9));
        Assert.Equal([1], hints.Update(atTwo, S(2))); // 0.29 s left at this speed
        Assert.Equal([1], hints.Update(atTwo, S(2.5))); // no new reading yet, the estimate has run out: still up
        Assert.Equal(0, hints.SecondsLeft(1, S(2.5)));

        // Slower now: the estimate runs out (0.6 s on, no new reading), but the combo isn't ready yet.
        var slow = Combat(Cooling(0.9));
        Assert.Equal([1], hints.Update(slow, S(2.6)));
        Assert.True(hints.SecondsLeft(1, S(2.6)) is >= 0 and <= 0.1, "below 0.1 s: shown as \"about to be ready\"");
        Assert.Equal([1], hints.Update(Combat(Cooling(0.93)), S(3))); // a new reading: still up, still not back up
        Assert.True(hints.SecondsLeft(1, S(3)) <= 0.1);
        Assert.Equal([1], hints.Update(Combat(Ready), S(3.5)));
        Assert.Null(hints.SecondsLeft(1, S(3.5)));
    }

    [Fact]
    public void The_seconds_left_never_go_back_up()
    {
        var hints = new ComboHints { Lead = S(1) };
        hints.Update(Combat(Cooling(0.2)), S(0));
        hints.Update(Combat(Cooling(0.4)), S(1));
        hints.Update(Combat(Cooling(0.6)), S(2));
        hints.Update(Combat(Cooling(0.8)), S(3));
        double first = hints.SecondsLeft(1, S(3))!.Value;

        hints.Update(Combat(Cooling(0.82)), S(3.25)); // a slow reading: the estimate alone would say more
        double next = hints.SecondsLeft(1, S(3.25))!.Value;

        Assert.True(next <= first, $"{next} after {first}");
    }

    [Fact]
    public void A_new_cooldown_ends_the_countdown()
    {
        var hints = new ComboHints { Lead = S(1) };
        hints.Update(Combat(Cooling(0.2)), S(0));
        hints.Update(Combat(Cooling(0.6)), S(1));
        Assert.Equal([1], hints.Update(Combat(Cooling(0.9)), S(2)));

        Assert.Empty(hints.Update(Combat(Cooling(0.05)), S(2.5))); // used again (refreshed then spent): starts over
    }
}
