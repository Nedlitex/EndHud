using EndHud.Vision.Operators;
using Xunit;
using Xunit.Abstractions;

namespace EndHud.Vision.Tests;

/// <summary>
/// The number on Si's damage-stacks badge (<see cref="BadgeNumber"/>), on the frames of her fight
/// (Fixtures/si, stacks-*), at 4K and on screens of other sizes made from them.
/// </summary>
public class BadgeNumberTests(ITestOutputHelper output)
{
    private static int? Expected(SiFixtures.Label label) => label.Stacks is > 0 ? label.Stacks : null;

    private static int? Read(Frame frame) =>
        BadgeNumber.Read(frame, new UiScaler(frame.Width, frame.Height).BottomCenter(OperatorCounters.SiStacksArea));

    public static TheoryData<string> Files() => [.. SiFixtures.StackFrames.Select(l => l.File)];

    /// <summary>The number or nothing; nothing where no number is drawn (the zero flower, no badge).</summary>
    [Theory]
    [MemberData(nameof(Files))]
    public async Task Reads_every_labelled_number_or_nothing(string file)
    {
        var label = SiFixtures.StackFrames.Single(l => l.File == file);

        int? read = Read(await SiFixtures.LoadAsync(file));

        if (Expected(label) is null) Assert.Null(read);
        else Assert.True(read is null || read == Expected(label), $"{file}: read {read}, expected {Expected(label)}");
    }

    [Fact]
    public async Task Reads_at_least_80_percent_of_the_numbers()
    {
        var numbered = SiFixtures.StackFrames.Where(l => Expected(l) is not null).ToList();
        int read = 0;
        foreach (var label in numbered)
            if (Read(await SiFixtures.LoadAsync(label.File)) == Expected(label)) read++;
        output.WriteLine($"4K: read {read} of {numbered.Count}");
        Assert.True(read >= 0.8 * numbered.Count, $"read {read} of {numbered.Count}");
    }

    [Fact]
    public async Task Never_reads_a_number_on_the_zero_flower()
    {
        foreach (var label in SiFixtures.StackFrames.Where(l => l.Stacks == 0))
            Assert.Null(Read(await SiFixtures.LoadAsync(label.File)));
    }

    public static TheoryData<string> Screens() => [SimulatedScreen.Qhd.Name, SimulatedScreen.FullHd.Name, SimulatedScreen.Wuxga.Name];

    /// <summary>Right or nothing on every frame; at 1080p (digits 13 px tall) still most of them.</summary>
    [Theory]
    [MemberData(nameof(Screens))]
    public async Task Reads_right_or_nothing_on_other_screens(string name)
    {
        var screen = SimulatedScreen.Named(name);
        var numbered = SiFixtures.StackFrames.ToList();
        int read = 0, shown = 0;
        var wrong = new List<string>();
        foreach (var label in numbered)
        {
            int? value = Read(screen.Render(await SiFixtures.LoadAsync(label.File)));
            if (Expected(label) is { } expected)
            {
                shown++;
                if (value == expected) read++;
            }
            if (value is not null && value != Expected(label)) wrong.Add($"{label.File}: {value}");
        }
        output.WriteLine($"{screen.Width}x{screen.Height}: read {read} of {shown}");
        Assert.Empty(wrong);
        Assert.True(read >= 0.6 * shown, $"read {read} of {shown}");
    }
}
