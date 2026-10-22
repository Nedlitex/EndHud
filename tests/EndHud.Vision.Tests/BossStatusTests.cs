using System.Text.Json;
using EndHud.Vision;
using EndHud.Vision.Operators;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// The boss's statuses, matched against the wiki's status icons (<see cref="OperatorCache"/>), on the
/// boss fight's frames (Fixtures/battle-hud) labelled by eye. Icons the wiki doesn't have (in this
/// fight: a clock, white slashes, a tray with a diamond, a pink face) must not be reported.
/// </summary>
public sealed class BossStatusTests : IDisposable
{
    private sealed record Label(string File, string[] BossStatuses);

    private static readonly Label[] Labels = JsonSerializer.Deserialize<Label[]>(
        File.ReadAllText(CombatFrameTests.Fixture(Path.Combine("battle-hud", "labels.json"))),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    private readonly string _userFolder = Path.Combine(Path.GetTempPath(), "endhud-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_userFolder, recursive: true); } catch (IOException) { }
    }

    public static TheoryData<string> Files() => [.. Labels.Select(l => l.File)];

    private async Task<BossStatusReader> ReaderAsync() => new(await OperatorCache.LoadAsync(_userFolder));

    [Theory]
    [MemberData(nameof(Files))]
    public async Task Names_the_statuses_the_wiki_has_and_nothing_else(string file)
    {
        var label = Labels.Single(l => l.File == file);
        var frame = await ImageFile.LoadAsync(CombatFrameTests.Fixture(Path.Combine("battle-hud", file)));

        var statuses = (await ReaderAsync()).Read(frame);

        Assert.Equal(label.BossStatuses, statuses.Select(s => s.StatusId));
    }

    [Fact]
    public async Task The_reading_carries_infliction_stacks()
    {
        var frame = await ImageFile.LoadAsync(CombatFrameTests.Fixture(Path.Combine("infliction", "si-cryo-57_5s.jpg")));

        var ring = Assert.Single((await ReaderAsync()).Read(frame), s => s.Position == BossStatusReading.RingPosition);

        Assert.Equal(("Cryo Infliction", 4), (ring.StatusId, ring.Stacks));
    }

    [Fact]
    public async Task The_reading_carries_a_reaction_level()
    {
        var frame = await ImageFile.LoadAsync(CombatFrameTests.Fixture(Path.Combine("reactions", "corrosion-III-84s.jpg")));

        var statuses = (await ReaderAsync()).Read(frame);

        var corrosion = Assert.Single(statuses, s => s.StatusId == "Corrosion");
        Assert.Equal((1, 3), (corrosion.Position, corrosion.Stacks));
    }

    /// <summary>A reaction shown with no badge is named, and its level is unknown (not guessed as I).</summary>
    [Fact]
    public async Task A_reaction_without_a_badge_has_no_level()
    {
        var frame = await ImageFile.LoadAsync(CombatFrameTests.Fixture(Path.Combine("reactions", "other-typhoeus-30s.jpg")));

        var electrification = Assert.Single((await ReaderAsync()).Read(frame), s => s.StatusId == "Electrification");

        Assert.Null(electrification.Stacks);
    }

    [Fact]
    public void Counts_are_offered_only_for_inflictions_and_measured_reactions()
    {
        Assert.Contains("Cryo Infliction", BossStatusReader.CountsRead);
        Assert.Contains("Corrosion", BossStatusReader.CountsRead);
        Assert.Contains("Electrification", BossStatusReader.CountsRead);
        Assert.DoesNotContain("Vulnerable", BossStatusReader.CountsRead);
        Assert.DoesNotContain("Combustion", BossStatusReader.CountsRead);
        Assert.DoesNotContain("Solidification", BossStatusReader.CountsRead);
    }

    public static TheoryData<string> Screens() => [SimulatedScreen.Qhd.Name, SimulatedScreen.FullHd.Name];

    /// <summary>Smaller screens draw the icons smaller: a status may go unrecognised, but never wrongly named.</summary>
    [Theory]
    [MemberData(nameof(Screens))]
    public async Task On_smaller_screens_statuses_are_named_right_or_not_at_all(string screenName)
    {
        var screen = SimulatedScreen.Named(screenName);
        var reader = await ReaderAsync();
        int found = 0, expected = 0;
        foreach (var label in Labels)
        {
            var frame = screen.Render(await ImageFile.LoadAsync(CombatFrameTests.Fixture(Path.Combine("battle-hud", label.File))));
            var named = reader.Read(frame).Select(s => s.StatusId).ToList();
            Assert.True(named.All(label.BossStatuses.Contains),
                $"{label.File} at {screen.Width}x{screen.Height}: {string.Join(", ", named)}; expected only {string.Join(", ", label.BossStatuses)}");
            found += named.Count;
            expected += label.BossStatuses.Length;
        }
        Assert.True(found >= expected * 0.6, $"{found} of {expected} statuses named at {screen.Width}x{screen.Height}");
    }
}
