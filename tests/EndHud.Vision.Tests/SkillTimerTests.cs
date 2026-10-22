using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// What Si's skill timer shows (<see cref="SkillTimer"/>) from a run of button readings at given
/// times: only while the changed icon is seen, from the ring when it's read and estimated from the
/// icon's change when it isn't, gone once the icon is back.
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

    [Fact]
    public void Without_any_ring_the_time_is_counted_from_the_icon_change_and_ends_at_zero()
    {
        var timer = new SkillTimer(Duration);

        var first = timer.Update(Changed(), At(0));
        var half = timer.Update(Changed(), At(11));
        var over = timer.Update(Changed(), At(23));

        Assert.NotNull(first);
        Assert.Equal(22, first!.SecondsLeft, 2);
        Assert.Equal(11, half!.SecondsLeft, 2);
        Assert.True(half.Estimated);
        Assert.Null(over);
    }

    [Fact]
    public void An_unknown_reading_shows_nothing_but_keeps_the_start()
    {
        var timer = new SkillTimer(Duration);
        timer.Update(Changed(), At(0));

        Assert.Null(timer.Update(Unknown, At(2)));
        Assert.Null(timer.Update(null, At(3)));
        var view = timer.Update(Changed(), At(4));

        Assert.NotNull(view);
        Assert.Equal(18, view!.SecondsLeft, 2);
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

    [Fact]
    public void Cooling_ends_it_too()
    {
        var timer = new SkillTimer(Duration);
        timer.Update(Changed(0.5), At(0));

        Assert.Null(timer.Update(new SkillRingReading("Si", SkillButtonIcon.Cooling, null), At(1)));
        Assert.Equal(22, timer.Update(Changed(), At(2))!.SecondsLeft, 2);
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
    public async Task On_the_recorded_frames_the_timer_shows_only_while_the_swirl_is_up()
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
