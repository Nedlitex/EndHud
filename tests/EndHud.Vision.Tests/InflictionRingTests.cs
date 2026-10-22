using System.Text.Json;
using EndHud.Vision;
using Xunit;
using Xunit.Abstractions;

namespace EndHud.Vision.Tests;

/// <summary>
/// The elemental infliction ring at the end of the boss's HP bar (<see cref="InflictionRing"/>), on
/// frames of the boss fight labelled by eye (Fixtures/infliction), including frames where colour
/// fills the ring's place but there is no ring.
/// </summary>
public class InflictionRingTests(ITestOutputHelper output)
{
    private sealed record Label(string File, string? Infliction, int? Stacks);

    private static readonly Label[] Labels = JsonSerializer.Deserialize<Label[]>(
        File.ReadAllText(CombatFrameTests.Fixture(Path.Combine("infliction", "labels.json"))),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    public static TheoryData<string> Files() => [.. Labels.Select(l => l.File)];

    private static Task<Frame> LoadAsync(string file) => ImageFile.LoadAsync(CombatFrameTests.Fixture(Path.Combine("infliction", file)));

    [Theory]
    [MemberData(nameof(Files))]
    public async Task Names_the_element_or_says_there_is_none(string file)
    {
        var frame = await LoadAsync(file);

        Assert.Equal(Labels.Single(l => l.File == file).Infliction, InflictionRing.Read(frame));
    }

    [Fact]
    public void The_fixtures_have_the_elements_seen_and_frames_without_a_ring()
    {
        Assert.Contains(Labels, l => l.Infliction == "Heat Infliction");
        Assert.Contains(Labels, l => l.Infliction == "Nature Infliction");
        Assert.Contains(Labels, l => l.Infliction == "Cryo Infliction");
        Assert.True(Labels.Count(l => l.Infliction is null) >= 3);
    }

    /// <summary>Every ring's stack count is its label or unknown, never a wrong number.</summary>
    [Theory]
    [MemberData(nameof(Files))]
    public async Task Reads_the_stack_count_or_says_it_cannot(string file)
    {
        var label = Labels.Single(l => l.File == file);
        var frame = await LoadAsync(file);

        int? stacks = InflictionRing.ReadWithStacks(frame)?.Stacks;

        Assert.True(stacks is null || stacks == label.Stacks, $"{file}: {stacks} stacks, labelled {label.Stacks?.ToString() ?? "none"}");
    }

    [Fact]
    public void The_fixtures_have_every_stack_count()
    {
        for (int stacks = 1; stacks <= 4; stacks++)
            Assert.Contains(Labels, l => l.Stacks == stacks);
    }

    [Fact]
    public async Task Most_stack_counts_are_read()
    {
        int read = 0, rings = 0;
        var missed = new List<string>();
        foreach (var label in Labels.Where(l => l.Stacks is not null))
        {
            rings++;
            if (InflictionRing.ReadWithStacks(await LoadAsync(label.File))?.Stacks == label.Stacks) read++;
            else missed.Add(label.File);
        }
        Assert.True(read >= rings * 0.8, $"{read} of {rings} stack counts read; missed {string.Join(", ", missed)}");
    }

    public static TheoryData<string> Screens() => [SimulatedScreen.Qhd.Name, SimulatedScreen.FullHd.Name, SimulatedScreen.Wuxga.Name];

    /// <summary>On smaller screens: never a wrong element, never a ring that isn't there, and most rings still read.</summary>
    [Theory]
    [MemberData(nameof(Screens))]
    public async Task On_other_screens_the_ring_is_read_right_or_not_at_all(string screenName)
    {
        var screen = SimulatedScreen.Named(screenName);
        int read = 0, rings = 0;
        foreach (var label in Labels)
        {
            string? found = InflictionRing.Read(screen.Render(await LoadAsync(label.File)));
            Assert.True(found is null || found == label.Infliction, $"{label.File} at {screen.Width}x{screen.Height}: {found}, expected {label.Infliction ?? "none"}");
            if (label.Infliction is not null)
            {
                rings++;
                if (found is not null) read++;
            }
        }
        Assert.True(read >= rings * 0.8, $"{read} of {rings} rings read at {screen.Width}x{screen.Height}");
    }

    /// <summary>On smaller screens the stack count is right or unknown, never wrong.</summary>
    [Theory]
    [MemberData(nameof(Screens))]
    public async Task On_other_screens_the_stack_count_is_right_or_unknown(string screenName)
    {
        var screen = SimulatedScreen.Named(screenName);
        int read = 0, rings = 0;
        foreach (var label in Labels.Where(l => l.Stacks is not null))
        {
            int? stacks = InflictionRing.ReadWithStacks(screen.Render(await LoadAsync(label.File)))?.Stacks;
            Assert.True(stacks is null || stacks == label.Stacks, $"{label.File} at {screen.Width}x{screen.Height}: {stacks} stacks, labelled {label.Stacks}");
            rings++;
            if (stacks is not null) read++;
        }
        output.WriteLine($"{read} of {rings} stack counts read at {screen.Width}x{screen.Height}");
    }
}
