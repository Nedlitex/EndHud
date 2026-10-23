using System.Text.Json;
using EndHud.Vision;
using EndHud.Vision.Operators;
using Xunit;
using Xunit.Abstractions;

namespace EndHud.Vision.Tests;

/// <summary>
/// Vulnerable and an elemental infliction on the boss at once (Fixtures/vulnerable-and-infliction, the
/// owner's test_rossi.mp4): the game draws both side by side at the end of the HP bar, in the order they
/// were applied, each with its own stack arcs. The boss reading reports each, in its place, right or
/// unknown, never as another status or count.
/// </summary>
public sealed class VulnerableAndInflictionTests(ITestOutputHelper output) : IDisposable
{
    private sealed record Icon(string Status, int? Stacks, bool Hard);

    private sealed record Label(string File, Icon? First, Icon? Second);

    private static readonly Label[] Labels = JsonSerializer.Deserialize<Label[]>(
        File.ReadAllText(CombatFrameTests.Fixture(Path.Combine("vulnerable-and-infliction", "labels.json"))),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    private readonly string _userFolder = Path.Combine(Path.GetTempPath(), "endhud-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_userFolder, recursive: true); } catch (IOException) { }
    }

    public static TheoryData<string> Files() => [.. Labels.Select(l => l.File)];

    private async Task<BossStatusReader> ReaderAsync() => new(await OperatorCache.LoadAsync(_userFolder));

    private static Task<Frame> LoadAsync(string file) => ImageFile.LoadAsync(CombatFrameTests.Fixture(Path.Combine("vulnerable-and-infliction", file)));

    /// <summary>The icons beside the HP bar's end in the reading, by place: the first, then the second.</summary>
    private static (BossStatusReading? First, BossStatusReading? Second) Places(IReadOnlyList<BossStatusReading> statuses, string at)
    {
        var ring = statuses.Where(s => s.Position < 0).ToList();
        Assert.True(ring.All(s => s.Position is BossStatusReading.RingPosition or BossStatusReading.SecondRingPosition), $"{at}: a place other than the two");
        Assert.True(ring.Select(s => s.Position).Distinct().Count() == ring.Count, $"{at}: two readings in one place");
        Assert.True(ring.Select(s => s.StatusId).Distinct().Count() == ring.Count, $"{at}: one status read twice");
        return (ring.SingleOrDefault(s => s.Position == BossStatusReading.RingPosition), ring.SingleOrDefault(s => s.Position == BossStatusReading.SecondRingPosition));
    }

    /// <summary>A labelled icon read right: there (unless hard), its status, its count (or unknown when hard). No icon: nothing read.</summary>
    private static void Check(Icon? label, BossStatusReading? read, string at)
    {
        if (label is null)
        {
            Assert.True(read is null, $"{at}: read {read?.StatusId} {read?.Stacks} where there is none");
            return;
        }
        if (read is null)
        {
            Assert.True(label.Hard, $"{at}: {label.Status} {label.Stacks} not read");
            return;
        }
        Assert.True(read.StatusId == label.Status, $"{at}: read {read.StatusId}, shows {label.Status}");
        if (label.Hard || label.Stacks is null)
            Assert.True(read.Stacks is null || read.Stacks == label.Stacks, $"{at}: {read.Stacks} stacks, labelled {label.Stacks?.ToString() ?? "unknown"}");
        else
            Assert.True(read.Stacks == label.Stacks, $"{at}: {read.Stacks?.ToString() ?? "unknown"} stacks, labelled {label.Stacks}");
    }

    /// <summary>
    /// Both icons are read, each in its place with its count, whichever came first; on a hard frame (a
    /// circle, a ghost, a fade) an icon may be unknown or missing, never another status or count.
    /// </summary>
    [Theory]
    [MemberData(nameof(Files))]
    public async Task Reads_both_icons_in_their_places_right_or_unknown(string file)
    {
        var label = Labels.Single(l => l.File == file);

        var (first, second) = Places((await ReaderAsync()).Read(await LoadAsync(file)), file);

        Check(label.First, first, $"{file} first");
        Check(label.Second, second, $"{file} second");
    }

    [Fact]
    public void The_fixtures_have_both_orders_single_icons_and_transitions()
    {
        static bool Infliction(Icon? i) => i is not null && i.Status != VulnerableRing.StatusId;
        static bool Vulnerable(Icon? i) => i?.Status == VulnerableRing.StatusId;
        Assert.True(Labels.Count(l => Infliction(l.First) && Vulnerable(l.Second) && !l.First!.Hard && !l.Second!.Hard) >= 3, "infliction then Vulnerable");
        Assert.True(Labels.Count(l => Vulnerable(l.First) && Infliction(l.Second) && !l.First!.Hard && !l.Second!.Hard) >= 3, "Vulnerable then infliction");
        Assert.Contains(Labels, l => Infliction(l.First) && l.Second is null);
        Assert.Contains(Labels, l => Vulnerable(l.First) && l.Second is null);
        Assert.True(Labels.Count(l => l.First?.Hard == true || l.Second?.Hard == true) >= 4, "transitions");
        Assert.True(Labels.Count(l => l.First is null) >= 2, "no icon");
    }

    public static TheoryData<string> Screens() => [SimulatedScreen.Qhd.Name, SimulatedScreen.FullHd.Name, SimulatedScreen.Wuxga.Name];

    /// <summary>On smaller screens: never another status or count, and most icons are still read.</summary>
    [Theory]
    [MemberData(nameof(Screens))]
    public async Task On_other_screens_both_icons_are_read_right_or_not_at_all(string screenName)
    {
        var screen = SimulatedScreen.Named(screenName);
        var reader = await ReaderAsync();
        int shown = 0, found = 0;
        foreach (var label in Labels)
        {
            string at = $"{label.File} at {screen.Width}x{screen.Height}";
            var (first, second) = Places(reader.Read(screen.Render(await LoadAsync(label.File))), at);
            foreach (var (icon, read, place) in new[] { (label.First, first, "first"), (label.Second, second, "second") })
            {
                if (icon is null)
                {
                    Assert.True(read is null, $"{at} {place}: read {read?.StatusId} where there is none");
                    continue;
                }
                if (icon.Hard)
                {
                    if (read is not null) Check(icon, read, $"{at} {place}");
                    continue;
                }
                shown++;
                if (read is null) continue;
                found++;
                Check(icon with { Hard = true }, read, $"{at} {place}");
            }
        }
        output.WriteLine($"{found} of {shown} icons read at {screen.Width}x{screen.Height}");
        Assert.True(found >= shown * 0.8, $"{found} of {shown} icons read at {screen.Width}x{screen.Height}");
    }
}
