using EndHud.Vision.Input;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// Presses for a recording (endhud-cli trace-video --presses): lines "t,slot", seconds from the
/// start of the clip and the squad slot cast; '#' starts a comment.
/// </summary>
public sealed class FileSkillPressesTests
{
    private static SkillPress Press(int slot, double seconds) => new(slot, TimeSpan.FromSeconds(seconds));

    [Fact]
    public void Lines_are_read_in_time_order()
    {
        var presses = FileSkillPresses.Parse("3.5,2\n1.25,4\n2,1\n");

        Assert.Equal([Press(4, 1.25), Press(1, 2), Press(2, 3.5)], presses.TakeUntil(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void Comments_and_blank_lines_are_skipped()
    {
        var presses = FileSkillPresses.Parse("# t,slot\r\n\r\n  1.5 , 3   # Ardelia's battle skill\r\n   \r\n# the end\r\n");

        Assert.Equal([Press(3, 1.5)], presses.TakeUntil(TimeSpan.FromSeconds(10)));
    }

    [Theory]
    [InlineData("1,2\n1.5\n", 2)]       // no slot
    [InlineData("1,2\n\nx,1\n", 3)]     // no time
    [InlineData("1,5\n", 1)]            // not a squad slot
    [InlineData("-1,2\n", 1)]           // before the start
    public void A_bad_line_says_which_line(string text, int line)
    {
        var error = Assert.Throws<FormatException>(() => FileSkillPresses.Parse(text));
        Assert.Contains($"line {line}", error.Message);
    }

    [Fact]
    public void Presses_are_taken_up_to_the_reading_time()
    {
        var presses = FileSkillPresses.Parse("1,1\n1.25,2\n2,3\n");

        Assert.Empty(presses.TakeUntil(TimeSpan.FromSeconds(0.75)));
        Assert.Equal([Press(1, 1), Press(2, 1.25)], presses.TakeUntil(TimeSpan.FromSeconds(1.25))); // at the reading's time counts
        Assert.Empty(presses.TakeUntil(TimeSpan.FromSeconds(1.5))); // taken once
        Assert.Equal([Press(3, 2)], presses.TakeUntil(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void The_decimal_point_is_a_point_whatever_the_machines_language()
    {
        var culture = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Assert.Equal([Press(2, 1.5)], FileSkillPresses.Parse("1.5,2").TakeUntil(TimeSpan.FromSeconds(2)));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = culture;
        }
    }
}
