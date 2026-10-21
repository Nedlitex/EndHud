using Xunit;
using static EndHud.Tests.InnTestKit;

namespace EndHud.Tests;

/// <summary>
/// Time spent reading the game (<see cref="ReadingClock"/>): it runs between frames, stands still
/// while reading is off, and counts a stall only up to <see cref="ReadingClock.FrameGap"/>.
/// </summary>
public class ReadingClockTests
{
    [Fact]
    public void It_runs_while_frames_are_read()
    {
        var clock = new ReadingClock();
        for (double t = 0; t <= 10; t += 0.25) Assert.Equal(TimeSpan.Zero, clock.Frame(S(t)));

        Assert.Equal(S(10), clock.At(S(10)));
        Assert.Equal(S(10.2), clock.At(S(10.2)));
    }

    [Fact]
    public void It_stands_still_while_reading_is_off_and_says_how_long_was_skipped()
    {
        var clock = new ReadingClock();
        clock.Frame(S(0));
        clock.Frame(S(1));
        clock.Stop(S(1.1));

        Assert.Equal(S(1.1), clock.At(S(30)));
        Assert.Equal(S(58.9), clock.Frame(S(60)));
        Assert.Equal(S(1.1), clock.At(S(60)));
        Assert.Equal(S(2.1), clock.At(S(61)));
    }

    [Fact]
    public void A_stall_counts_only_up_to_the_frame_gap_and_the_clock_never_goes_back()
    {
        var clock = new ReadingClock();
        clock.Frame(S(0));
        var during = clock.At(S(20));

        Assert.Equal(ReadingClock.FrameGap, during);
        Assert.Equal(S(20) - ReadingClock.FrameGap, clock.Frame(S(20)));
        Assert.True(clock.At(S(20)) >= during);
    }

    [Fact]
    public void Before_the_first_frame_and_when_stopped_twice_nothing_counts()
    {
        var clock = new ReadingClock();
        clock.Stop(S(5));
        Assert.Equal(TimeSpan.Zero, clock.At(S(9)));
        Assert.Equal(TimeSpan.Zero, clock.Frame(S(10)));

        clock.Stop(S(11));
        clock.Stop(S(12));
        Assert.Equal(S(1), clock.At(S(40)));
    }
}
