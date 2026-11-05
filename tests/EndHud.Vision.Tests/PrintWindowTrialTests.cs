using EndHud.Vision.Capture;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// When Windows 10's direct copy of the game window (no yellow border) is given up for window
/// capture: copies that keep failing, a window that only ever comes back black, or copies too slow.
/// </summary>
public class PrintWindowTrialTests
{
    private static readonly TimeSpan Quick = TimeSpan.FromMilliseconds(20);

    private static Frame Filled(byte level)
    {
        var bgra = new byte[64 * 36 * 4];
        Array.Fill(bgra, level);
        return new Frame(64, 36, bgra);
    }

    private static Frame Black => Filled(0);
    private static Frame Picture => Filled(120);

    [Fact]
    public void A_window_that_only_ever_comes_back_black_is_given_up()
    {
        var trial = new PrintWindowTrial();
        for (int i = 1; i < PrintWindowTrial.BlackToGiveUp; i++)
            Assert.True(trial.Record(Black, Quick), $"given up after {i} black copies");

        Assert.False(trial.Record(Black, Quick));
        Assert.Contains("black", trial.GaveUp);
    }

    [Fact]
    public void Black_after_a_picture_is_the_games_own_and_keeps_the_copies()
    {
        var trial = new PrintWindowTrial();
        Assert.True(trial.Record(Picture, Quick));
        for (int i = 0; i < 3 * PrintWindowTrial.BlackToGiveUp; i++)
            Assert.True(trial.Record(Black, Quick)); // a loading screen

        Assert.True(trial.PictureSeen);
        Assert.Null(trial.GaveUp);
    }

    [Fact]
    public void Copies_that_keep_failing_are_given_up_and_one_failure_is_not()
    {
        var trial = new PrintWindowTrial();
        for (int round = 0; round < 3; round++)
        {
            for (int i = 1; i < PrintWindowTrial.FailuresToGiveUp; i++) Assert.True(trial.Record(null, Quick, "PrintWindow failed"));
            Assert.True(trial.Record(Picture, Quick)); // a copy in between starts the count again
        }
        for (int i = 1; i < PrintWindowTrial.FailuresToGiveUp; i++) trial.Record(null, Quick, "PrintWindow failed");

        Assert.False(trial.Record(null, Quick, "PrintWindow failed"));
        Assert.Contains("PrintWindow failed", trial.GaveUp);
    }

    [Fact]
    public void Copies_too_slow_on_average_are_given_up_and_one_slow_copy_is_not()
    {
        var slow = PrintWindowTrial.SlowCopy * 2;
        var trial = new PrintWindowTrial();
        Assert.True(trial.Record(Picture, slow));
        for (int i = 1; i < PrintWindowTrial.SlowWindow; i++) Assert.True(trial.Record(Picture, Quick));

        for (int i = 1; i < PrintWindowTrial.SlowWindow; i++) trial.Record(Picture, slow);
        Assert.False(trial.Record(Picture, slow));
        Assert.Contains("too slow", trial.GaveUp);
    }

    [Fact]
    public void Once_given_up_it_stays_given_up()
    {
        var trial = new PrintWindowTrial();
        for (int i = 0; i < PrintWindowTrial.FailuresToGiveUp; i++) trial.Record(null, Quick, "x");

        Assert.False(trial.Record(Picture, Quick));
    }

    [Fact]
    public void A_dark_scene_with_any_light_in_it_is_not_black()
    {
        var frame = Black;
        frame.Bgra[(10 * 64 + 10) * 4 + 1] = 40; // one dim green pixel on the sampling grid
        Assert.False(PrintWindowTrial.LooksBlack(frame));
        Assert.True(PrintWindowTrial.LooksBlack(Black));
    }
}
