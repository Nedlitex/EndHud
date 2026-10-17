using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// The count on a stack badge (<see cref="StackDigits"/>), on badges cut from the owner's controller
/// recordings (Fixtures/stack-digits: the digit is the file name's first character), at 4K and scaled
/// down as a smaller screen would draw them.
/// </summary>
public class StackDigitTests
{
    public static TheoryData<string> Badges() =>
        [.. Directory.GetFiles(CombatFrameTests.Fixture("stack-digits"), "*.png").Select(Path.GetFileName)!];

    [Theory]
    [MemberData(nameof(Badges))]
    public async Task Reads_the_digit(string file)
    {
        var badge = await ImageFile.LoadAsync(CombatFrameTests.Fixture(Path.Combine("stack-digits", file)));

        Assert.Equal(file[0] - '0', StackDigits.Read(badge, new PixelRect(0, 0, badge.Width, badge.Height)));
    }

    [Theory]
    [MemberData(nameof(Badges))]
    public async Task Reads_the_digit_at_1080p(string file)
    {
        var badge = (await ImageFile.LoadAsync(CombatFrameTests.Fixture(Path.Combine("stack-digits", file)))).ResizeSmooth(13, 13);

        Assert.Equal(file[0] - '0', StackDigits.Read(badge, new PixelRect(0, 0, badge.Width, badge.Height)));
    }

    [Fact]
    public void The_fixtures_cover_every_digit_known()
    {
        var files = Directory.GetFiles(CombatFrameTests.Fixture("stack-digits"), "*.png").Select(Path.GetFileName).ToList();
        foreach (var (digit, _) in StackDigits.Shapes) Assert.Contains(files, f => f![0] - '0' == digit);
    }

    /// <summary>A dark square with nothing in it, or an even white blot: not a digit.</summary>
    [Fact]
    public void A_blank_or_blotted_badge_is_not_read()
    {
        var dark = Solid(20);
        var white = Solid(240);

        Assert.Null(StackDigits.Read(dark, new PixelRect(0, 0, 26, 26)));
        Assert.Null(StackDigits.Read(white, new PixelRect(0, 0, 26, 26)));
    }

    private static Frame Solid(byte grey) => new(26, 26, Enumerable.Repeat(new[] { grey, grey, grey, (byte)255 }, 26 * 26).SelectMany(p => p).ToArray());
}
