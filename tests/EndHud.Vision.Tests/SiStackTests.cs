using System.Text.Json;
using EndHud.Vision.Operators;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>Si's frames (Fixtures/si) and what was seen on them by eye (labels.json, see its README).</summary>
internal static class SiFixtures
{
    /// <param name="Stacks">Si's damage stacks; null where no badge can be seen (stacks-* only).</param>
    /// <param name="Icon">Her battle-skill button's icon (skill-* only).</param>
    /// <param name="DrainedDegrees">How much of the grey ring is gone, clockwise from 12 o'clock.</param>
    /// <param name="RingHidden">The ring can't be told from what's around it.</param>
    internal sealed record Label(string File, int? Stacks, SkillIcon? Icon, double? DrainedDegrees, double? SecondsLeft, bool RingHidden);

    internal static readonly Label[] Labels = JsonSerializer.Deserialize<Label[]>(
        File.ReadAllText(CombatFrameTests.Fixture(Path.Combine("si", "labels.json"))),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } })!;

    internal static IEnumerable<Label> StackFrames => Labels.Where(l => l.File.StartsWith("stacks-", StringComparison.Ordinal));

    internal static IEnumerable<Label> SkillFrames => Labels.Where(l => l.File.StartsWith("skill-", StringComparison.Ordinal));

    internal static Task<Frame> LoadAsync(string file) => ImageFile.LoadAsync(CombatFrameTests.Fixture(Path.Combine("si", file)));
}

/// <summary>Si's damage stacks (<see cref="OperatorCounters.SiStacks"/>) on the frames of her fight.</summary>
public class SiStackTests
{
    public static TheoryData<string> Files() => [.. SiFixtures.StackFrames.Select(l => l.File)];

    [Theory]
    [MemberData(nameof(Files))]
    public async Task Si_stacks_read_right_or_not_at_all(string file)
    {
        var label = SiFixtures.StackFrames.Single(l => l.File == file);
        var frame = await SiFixtures.LoadAsync(file);

        var reading = Assert.Single(OperatorCounters.Read(frame, "Si"));

        Assert.Equal(OperatorCounters.SiStacks.Id, reading.CounterId);
        Assert.True(reading.Value is null || reading.Value == label.Stacks, $"{file}: read {reading.Value}, expected {label.Stacks?.ToString() ?? "nothing"}");
    }

    /// <summary>Every frame with a badge on it, the zero flower included, read; at least 80 % of them.</summary>
    [Fact]
    public async Task Si_stacks_are_read_on_most_frames()
    {
        int read = 0, shown = 0;
        foreach (var label in SiFixtures.StackFrames.Where(l => l.Stacks is not null))
        {
            shown++;
            if (OperatorCounters.Read(await SiFixtures.LoadAsync(label.File), "Si").SingleOrDefault()?.Value == label.Stacks) read++;
        }
        Assert.True(read >= 0.8 * shown, $"read {read} of {shown}");
    }

    [Fact]
    public async Task The_zero_flower_reads_zero()
    {
        foreach (var label in SiFixtures.StackFrames.Where(l => l.Stacks == 0))
            Assert.Equal(0, Assert.Single(OperatorCounters.Read(await SiFixtures.LoadAsync(label.File), "Si")).Value);
    }

    /// <summary>
    /// Laevatain's 熔火 flower, Typhoeus's arrows and signs, and Zhuang Fangyi's grid sit where Si's
    /// badge does: on their frames, reading Si's stacks gives nothing.
    /// </summary>
    [Fact]
    public async Task Another_operators_hud_never_reads_as_Si_stacks()
    {
        var files = Directory.GetFiles(CombatFrameTests.Fixture("battle-hud"), "*.jpg")
            .Concat(Directory.GetFiles(CombatFrameTests.Fixture("counters"), "*.jpg"));
        foreach (string path in files)
        {
            var frame = await ImageFile.LoadAsync(path);
            Assert.All(OperatorCounters.Read(frame, "Si"), r => Assert.Null(r.Value));
        }
    }

    [Fact]
    public void The_fixtures_cover_every_digit_twice_the_zero_flower_and_no_badge()
    {
        string digits = string.Concat(SiFixtures.StackFrames.Where(l => l.Stacks > 0).Select(l => l.Stacks!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        for (char d = '0'; d <= '9'; d++) Assert.True(digits.Count(c => c == d) >= 2, $"digit {d}");
        Assert.True(SiFixtures.StackFrames.Count(l => l.Stacks == 0) >= 2);
        Assert.True(SiFixtures.StackFrames.Count(l => l.Stacks is null) >= 2);
    }
}
