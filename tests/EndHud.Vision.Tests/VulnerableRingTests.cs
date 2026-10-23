using System.Text.Json;
using EndHud.Vision;
using Xunit;
using Xunit.Abstractions;

namespace EndHud.Vision.Tests;

/// <summary>
/// 破防 (Vulnerable) in the ring at the end of the boss's HP bar (<see cref="VulnerableRing"/>), on frames
/// of the owner's physical-team recording labelled by eye (Fixtures/vulnerable): a white shield with a
/// slash, and white arcs for 1-4 stacks. The count is right or unknown, never another number.
/// </summary>
public class VulnerableRingTests(ITestOutputHelper output)
{
    private sealed record Label(string File, bool Vulnerable, int? Stacks, bool Hard);

    private static readonly Label[] Labels = JsonSerializer.Deserialize<Label[]>(
        File.ReadAllText(CombatFrameTests.Fixture(Path.Combine("vulnerable", "labels.json"))),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    public static TheoryData<string> Files() => [.. Labels.Select(l => l.File)];

    private static Task<Frame> LoadAsync(string folder, string file) => ImageFile.LoadAsync(CombatFrameTests.Fixture(Path.Combine(folder, file)));

    /// <summary>
    /// 破防 is read where it's shown and nowhere else. Its count is the label; on a hard frame (the
    /// add-a-stack flash, a beam, the HUD fading in) it may be unknown instead, never another number.
    /// </summary>
    [Theory]
    [MemberData(nameof(Files))]
    public async Task Reads_vulnerable_and_its_stacks_or_says_the_count_is_unknown(string file)
    {
        var label = Labels.Single(l => l.File == file);

        var reading = VulnerableRing.Read(await LoadAsync("vulnerable", file));

        Assert.Equal(label.Vulnerable, reading is not null);
        if (reading is null) return;
        Assert.Equal(VulnerableRing.StatusId, reading.Element);
        if (label.Stacks is null || label.Hard)
            Assert.True(reading.Stacks is null || reading.Stacks == label.Stacks, $"{file}: {reading.Stacks} stacks, labelled {label.Stacks?.ToString() ?? "unknown"}");
        else
            Assert.Equal(label.Stacks, reading.Stacks);
    }

    [Fact]
    public void The_fixtures_have_every_stack_count_and_frames_without_the_ring()
    {
        for (int stacks = 1; stacks <= 4; stacks++)
            Assert.True(Labels.Count(l => l.Stacks == stacks && !l.Hard) >= 3, $"{stacks} stacks");
        Assert.True(Labels.Count(l => !l.Vulnerable) >= 4);
        Assert.Contains(Labels, l => l.File.StartsWith("flash-", StringComparison.Ordinal));
    }

    /// <summary>The elemental infliction rings, the reaction frames and the boss fight's frames show no 破防.</summary>
    [Theory]
    [InlineData("infliction")]
    [InlineData("reactions")]
    [InlineData("battle-hud")]
    public async Task Frames_without_the_shield_never_read_vulnerable(string folder)
    {
        var files = Directory.GetFiles(CombatFrameTests.Fixture(folder)).Where(f => f.EndsWith(".jpg", StringComparison.Ordinal) || f.EndsWith(".png", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(files);
        foreach (var file in files)
            Assert.True(VulnerableRing.Read(await ImageFile.LoadAsync(file)) is null, $"{folder}/{Path.GetFileName(file)} read as 破防");
    }

    public static TheoryData<string> Screens() => [SimulatedScreen.Qhd.Name, SimulatedScreen.FullHd.Name, SimulatedScreen.Wuxga.Name];

    /// <summary>On smaller screens: 破防 is never read where it isn't, its count is right or unknown, and most are still read.</summary>
    [Theory]
    [MemberData(nameof(Screens))]
    public async Task On_other_screens_vulnerable_is_read_right_or_not_at_all(string screenName)
    {
        var screen = SimulatedScreen.Named(screenName);
        int shown = 0, found = 0, counted = 0, clean = 0;
        foreach (var label in Labels)
        {
            var reading = VulnerableRing.Read(screen.Render(await LoadAsync("vulnerable", label.File)));
            string at = $"{label.File} at {screen.Width}x{screen.Height}";
            if (!label.Vulnerable)
            {
                Assert.True(reading is null, $"{at}: read as 破防");
                continue;
            }
            shown++;
            if (reading is null) continue;
            found++;
            Assert.True(reading.Stacks is null || reading.Stacks == label.Stacks, $"{at}: {reading.Stacks} stacks, labelled {label.Stacks?.ToString() ?? "unknown"}");
            if (!label.Hard)
            {
                clean++;
                if (reading.Stacks == label.Stacks) counted++;
            }
        }
        output.WriteLine($"{found} of {shown} 破防 rings found, {counted} of {clean} clean counts read at {screen.Width}x{screen.Height}");
        Assert.True(found >= shown * 0.8, $"{found} of {shown} 破防 rings found at {screen.Width}x{screen.Height}");
    }
}
