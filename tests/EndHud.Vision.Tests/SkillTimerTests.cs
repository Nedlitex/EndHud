using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// What Si's skill timer shows (<see cref="SkillTimer"/>) from a run of button readings at given
/// times: from the first sight of the changed icon until the original icon is back (a second cast,
/// a hidden button or a countdown look in between don't end it), from the ring when it's read and
/// estimated from the icon's change when it isn't.
/// </summary>
public class SkillTimerTests
{
    private static readonly TimeSpan Duration = TimeSpan.FromSeconds(22);

    private static SkillRingReading Original => new("Si", SkillButtonIcon.Original, null);

    private static SkillRingReading Unknown => new("Si", SkillButtonIcon.Unknown, null);

    private static SkillRingReading Changed(double? left = null) => new("Si", SkillButtonIcon.Changed, left);

    private static TimeSpan At(double seconds) => TimeSpan.FromSeconds(seconds);

    [Fact]
    public void The_ring_gives_the_time_left_and_the_original_icon_shows_nothing()
    {
        var timer = new SkillTimer(Duration);

        Assert.Null(timer.Update(Original, At(0)));
        var view = timer.Update(Changed(0.5), At(0));

        Assert.NotNull(view);
        Assert.Equal(11, view!.SecondsLeft, 3);
        Assert.Equal(0.5, view.LeftShare, 3);
        Assert.False(view.Estimated);
    }

    [Fact]
    public void A_hidden_ring_is_estimated_from_the_last_ring_read()
    {
        var timer = new SkillTimer(Duration);
        timer.Update(Changed(0.9), At(0));

        var view = timer.Update(Changed(), At(5));

        Assert.NotNull(view);
        Assert.True(view!.Estimated);
        Assert.Equal(0.9 * 22 - 5, view.SecondsLeft, 2);
    }

    /// <summary>Past the estimate's end with the icon still changed: held at 0 (the ring stands still in cutscenes), until the icon is back.</summary>
    [Fact]
    public void Without_any_ring_the_time_is_counted_from_the_icon_change_and_held_at_zero_until_the_icon_is_back()
    {
        var timer = new SkillTimer(Duration);

        var first = timer.Update(Changed(), At(0));
        var half = timer.Update(Changed(), At(11));
        var over = timer.Update(Changed(), At(23));

        Assert.NotNull(first);
        Assert.Equal(22, first!.SecondsLeft, 2);
        Assert.Equal(11, half!.SecondsLeft, 2);
        Assert.True(half.Estimated);
        Assert.Equal((0.0, 0.0, true), (over!.LeftShare, over.SecondsLeft, over.Estimated));
        Assert.Null(timer.Update(Original, At(24)));
    }

    /// <summary>The button hidden (a cutscene, an effect) while it runs: the estimate is shown, and the start kept.</summary>
    [Fact]
    public void An_unknown_reading_shows_the_estimate_and_keeps_the_start()
    {
        var timer = new SkillTimer(Duration);
        timer.Update(Changed(), At(0));

        var hidden = timer.Update(Unknown, At(2));
        var notRead = timer.Update(null, At(3));
        var view = timer.Update(Changed(), At(4));

        Assert.Equal((20.0, true), (Math.Round(hidden!.SecondsLeft, 2), hidden.Estimated));
        Assert.Equal((19.0, true), (Math.Round(notRead!.SecondsLeft, 2), notRead.Estimated));
        Assert.Equal(18, view!.SecondsLeft, 2);
    }

    /// <summary>
    /// Past the estimate with the button not seen (switched to the controller layout, a long
    /// cutscene): nothing shown, so it can't stay up all battle; seen running again, it's back.
    /// </summary>
    [Fact]
    public void Past_the_estimate_an_unseen_button_shows_nothing_until_it_is_seen_again()
    {
        var timer = new SkillTimer(Duration);
        timer.Update(Changed(0.1), At(0));

        Assert.Equal(1.2, timer.Update(Unknown, At(1))!.SecondsLeft, 2);
        Assert.Null(timer.Update(null, At(3)));
        Assert.Null(timer.Update(Unknown, At(60)));
        Assert.Equal(0.05 * 22, timer.Update(new SkillRingReading("Si", SkillButtonIcon.SecondCast, 0.05), At(61))!.SecondsLeft, 2);
    }

    [Fact]
    public void Before_any_cast_an_unknown_reading_shows_nothing()
    {
        var timer = new SkillTimer(Duration);

        Assert.Null(timer.Update(Unknown, At(0)));
        Assert.Null(timer.Update(new SkillRingReading("Si", SkillButtonIcon.Cooling, null), At(1)));
        Assert.Null(timer.Update(null, At(2)));
    }

    /// <summary>
    /// Cast again while it lasts: the crossed-out icon, the ring draining on from where it was
    /// (measured: no refill). It still runs, from the ring, until the original icon is back.
    /// </summary>
    [Fact]
    public void A_second_cast_keeps_it_running_on_the_same_ring_until_the_icon_is_back()
    {
        var timer = new SkillTimer(Duration);
        timer.Update(Changed(0.9), At(0));

        var recast = timer.Update(new SkillRingReading("Si", SkillButtonIcon.SecondCast, 0.85), At(1));
        var ringHidden = timer.Update(new SkillRingReading("Si", SkillButtonIcon.SecondCast, null), At(3));
        var empty = timer.Update(new SkillRingReading("Si", SkillButtonIcon.SecondCast, 0), At(4));

        Assert.Equal((0.85 * 22, false), (Math.Round(recast!.SecondsLeft, 2), recast.Estimated));
        Assert.Equal((0.85 * 22 - 2, true), (Math.Round(ringHidden!.SecondsLeft, 2), ringHidden.Estimated));
        Assert.Equal(0, empty!.SecondsLeft, 2);
        Assert.Null(timer.Update(Original, At(4.5)));
    }

    [Fact]
    public void The_icon_going_back_ends_it_and_the_next_cast_starts_afresh()
    {
        var timer = new SkillTimer(Duration);
        timer.Update(Changed(), At(0));

        Assert.Null(timer.Update(Original, At(20)));
        var next = timer.Update(Changed(), At(40));

        Assert.NotNull(next);
        Assert.Equal(22, next!.SecondsLeft, 2);
        Assert.True(next.Estimated);
    }

    /// <summary>Only the original icon ends it: a countdown look on the button while it runs doesn't.</summary>
    [Fact]
    public void A_countdown_look_does_not_end_it()
    {
        var timer = new SkillTimer(Duration);
        timer.Update(Changed(0.5), At(0));

        var cooling = timer.Update(new SkillRingReading("Si", SkillButtonIcon.Cooling, null), At(1));

        Assert.Equal((10.0, true), (Math.Round(cooling!.SecondsLeft, 2), cooling.Estimated));
        Assert.Equal(9, timer.Update(Changed(), At(2))!.SecondsLeft, 2);
    }

    [Fact]
    public void Clear_forgets_the_start()
    {
        var timer = new SkillTimer(Duration);
        timer.Update(Changed(), At(0));

        timer.Clear();

        Assert.Equal(22, timer.Update(Changed(), At(10))!.SecondsLeft, 2);
    }

    /// <summary>The fixtures in order, as a fight would show them: before the cast, during, after it reverts.</summary>
    [Fact]
    public async Task On_the_recorded_frames_the_timer_shows_from_the_swirl_until_the_butterfly_is_back()
    {
        var timer = new SkillTimer(SkillRing.SiDuration);
        foreach (var label in SiFixtures.SkillFrames.OrderBy(l => Seconds(l.File)))
        {
            var view = timer.Update(SkillRing.ReadSi(await SiFixtures.LoadAsync(label.File), 0), At(Seconds(label.File)));
            if (label.Icon != SkillButtonIcon.Changed)
            {
                Assert.Null(view);
                continue;
            }
            Assert.NotNull(view);
            if (label.SecondsLeft is { } expected) Assert.InRange(view!.SecondsLeft, expected - 1.5, expected + 1.5);
            else Assert.True(view!.Estimated, label.File); // the faded button: estimated from the last ring
        }
    }

    private static double Seconds(string file) =>
        double.Parse(file["skill-".Length..^"s.jpg".Length].Replace('_', '.'), System.Globalization.CultureInfo.InvariantCulture);
}
