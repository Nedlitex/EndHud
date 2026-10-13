using System.Text.Json;
using EndHud.Vision;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// The elemental infliction ring at the end of the boss's HP bar (<see cref="InflictionRing"/>), on
/// frames of the boss fight labelled by eye (Fixtures/infliction), including frames where colour
/// fills the ring's place but there is no ring.
/// </summary>
public class InflictionRingTests
{
    private sealed record Label(string File, string? Infliction);

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
    public void The_fixtures_have_both_elements_seen_and_frames_without_a_ring()
    {
        Assert.Contains(Labels, l => l.Infliction == "Heat Infliction");
        Assert.Contains(Labels, l => l.Infliction == "Nature Infliction");
        Assert.True(Labels.Count(l => l.Infliction is null) >= 3);
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
}
