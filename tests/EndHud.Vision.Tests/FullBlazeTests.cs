using EndHud.Vision.Inn;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>When to start Full Blaze (满堂彩, C17): <see cref="FullBlazeAdvisor"/> and <see cref="FullBlazeTracker"/>.</summary>
public class FullBlazeTests
{
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    private static FullBlazeAdvice? Advise(int fire, double left = 180, int ready = 2, double? nextDish = 20, double fullFor = 0, bool running = false) =>
        FullBlazeAdvisor.Advise(fire, running, S(left), ready, nextDish is { } n ? S(n) : null, S(fullFor));

    [Theory]
    [InlineData(0, null)]
    [InlineData(79, null)]
    [InlineData(80, FullBlazeAdvice.GetReady)] // fill every kitchen ahead of the rush
    [InlineData(99, FullBlazeAdvice.GetReady)]
    [InlineData(100, FullBlazeAdvice.StartNow)] // full, a minute or more left, dishes ready
    public void The_fire_decides(int fire, FullBlazeAdvice? expected)
    {
        Assert.Equal(expected, Advise(fire));
    }

    /// <summary>Full with the kitchens nearly empty and a dish finishing within 10 s: wait for it, at most 10 s.</summary>
    [Fact]
    public void Full_with_nothing_ready_and_a_dish_due_waits_for_it_at_most_10_seconds()
    {
        Assert.Equal(FullBlazeAdvice.GetReady, Advise(100, ready: 0, nextDish: 6, fullFor: 0));
        Assert.Equal(FullBlazeAdvice.GetReady, Advise(100, ready: 0, nextDish: 10, fullFor: 4));
        Assert.Equal(FullBlazeAdvice.StartNow, Advise(100, ready: 1, nextDish: 4, fullFor: 6)); // the dish is out
        Assert.Equal(FullBlazeAdvice.StartNow, Advise(100, ready: 0, nextDish: 3, fullFor: 10)); // waited long enough
        Assert.Equal(FullBlazeAdvice.StartNow, Advise(100, ready: 0, nextDish: 25)); // nothing due soon: waiting won't help
        Assert.Equal(FullBlazeAdvice.StartNow, Advise(100, ready: 0, nextDish: null)); // nothing cooking at all
    }

    [Fact]
    public void Under_a_minute_left_and_full_start_at_once()
    {
        Assert.Equal(FullBlazeAdvice.StartNow, Advise(100, left: 59, ready: 0, nextDish: 5));
        Assert.Equal(FullBlazeAdvice.GetReady, Advise(100, left: 60, ready: 0, nextDish: 5));
    }

    [Fact]
    public void While_it_runs_nothing_is_said()
    {
        Assert.Null(Advise(100, running: true));
        Assert.Null(Advise(85, running: true));
    }

    /// <summary>The ready button going while the session continues means R was pressed: running for a minute from that frame.</summary>
    [Fact]
    public void Running_is_inferred_from_the_ready_button_going()
    {
        var tracker = new FullBlazeTracker();

        tracker.Update(S(100), false, null, true);
        Assert.False(tracker.Running);
        tracker.Update(S(110), true, null, true);
        tracker.Update(S(114), true, null, true);
        Assert.Equal(S(4), tracker.FullFor);
        Assert.False(tracker.Running);

        tracker.Update(S(115), null, null, true); // an unreadable frame changes nothing
        Assert.Equal(S(5), tracker.FullFor);

        tracker.Update(S(116), false, null, true);
        Assert.True(tracker.Running);
        Assert.Equal(S(60), tracker.RunningLeft);
        Assert.Equal(TimeSpan.Zero, tracker.FullFor);

        tracker.Update(S(175.5), false, null, true);
        Assert.True(tracker.Running);
        tracker.Update(S(176), false, null, true);
        Assert.False(tracker.Running);
    }

    /// <summary>
    /// test_yuanyuan5 (owner's live session 2026-10-21): Full Blaze started at about 333 s; the meter then showed its
    /// countdown, which the reader didn't know, so the tracker never saw the ready button go and "开启满堂彩（R）" stayed up to
    /// the end. The countdown read makes it running for what the countdown says, and a ready read meanwhile (the game's
    /// "满堂生彩" line over the red circle) changes nothing. Once it's over, a full meter is advised again.
    /// </summary>
    [Fact]
    public void Full_blaze_read_running_takes_the_reminder_down_until_it_is_over()
    {
        var tracker = new FullBlazeTracker();
        tracker.Update(S(320), true, null, true);
        Assert.Equal(FullBlazeAdvice.StartNow, FullBlazeAdvisor.Advise(100, tracker.Running, S(60), 2, null, tracker.FullFor));

        tracker.Update(S(333), null, null, true); // fading as it starts: nothing read
        tracker.Update(S(334), false, S(60), true);
        Assert.True(tracker.Running);
        Assert.Null(FullBlazeAdvisor.Advise(100, tracker.Running, S(50), 2, null, tracker.FullFor));
        tracker.Update(S(350), true, null, true); // the game's own line read as the ready label
        Assert.True(tracker.Running);
        Assert.Equal(TimeSpan.Zero, tracker.FullFor);
        tracker.Update(S(380), false, S(14), true);
        Assert.Equal(S(14), tracker.RunningLeft);

        tracker.Update(S(395), false, null, true);
        Assert.False(tracker.Running);
        tracker.Update(S(500), true, null, true); // full again, later
        Assert.False(tracker.Running);
        Assert.Equal(FullBlazeAdvice.StartNow, FullBlazeAdvisor.Advise(100, tracker.Running, S(30), 2, null, tracker.FullFor));
    }

    /// <summary>The ready button going because the session ended (the results screen) isn't Full Blaze.</summary>
    [Fact]
    public void The_session_ending_is_not_full_blaze()
    {
        var tracker = new FullBlazeTracker();
        tracker.Update(S(10), true, null, true);

        tracker.Update(S(11), false, null, false);

        Assert.False(tracker.Running);
        tracker.Update(S(12), false, null, true);
        Assert.False(tracker.Running);
    }

    /// <summary>
    /// test_yuanyuan1 (research note): the meter went full at 324 s with 2:06 left and was never
    /// pressed. With dishes on the counter the reminder is at once, and it stays until pressed.
    /// </summary>
    [Fact]
    public void The_recorded_full_meter_gets_its_reminder_at_once()
    {
        var tracker = new FullBlazeTracker();
        var said = new List<FullBlazeAdvice?>();
        for (double t = 318; t <= 340; t += 0.5)
        {
            bool full = t >= 324;
            tracker.Update(S(t), full, null, true);
            int fire = full ? FullBlazeAdvisor.Full : 92;
            said.Add(FullBlazeAdvisor.Advise(fire, tracker.Running, S(450 - t), 2, S(12), tracker.FullFor));
        }

        Assert.All(said.Take(12), a => Assert.Equal(FullBlazeAdvice.GetReady, a));
        Assert.All(said.Skip(12), a => Assert.Equal(FullBlazeAdvice.StartNow, a));
    }
}
