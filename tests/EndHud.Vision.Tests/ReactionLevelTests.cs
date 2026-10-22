using System.Text.Json;
using EndHud.Vision;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// The Roman numeral level on a reaction's icon (<see cref="ReactionLevel"/>), on frames of the owner's
/// fights labelled by eye (Fixtures/reactions): Corrosion III, Electrification II, III and IV, and
/// positions holding other badges (white digits), the same reactions with no badge, or no badge at all.
/// </summary>
public class ReactionLevelTests
{
    /// <param name="Hard">An effect or a dim badge: the level may read as unknown, but never wrong.</param>
    private sealed record Label(string File, int Position, string? Status, int? Level, bool Hard = false);

    private static readonly Label[] Labels = JsonSerializer.Deserialize<Label[]>(
        File.ReadAllText(CombatFrameTests.Fixture(Path.Combine("reactions", "labels.json"))),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    public static TheoryData<string, int> Positions()
    {
        var data = new TheoryData<string, int>();
        foreach (var label in Labels) data.Add(label.File, label.Position);
        return data;
    }

    private static async Task<int?> ReadAsync(string file, int position, SimulatedScreen? screen = null)
    {
        var frame = await ImageFile.LoadAsync(CombatFrameTests.Fixture(Path.Combine("reactions", file)));
        if (screen is not null) frame = screen.Render(frame);
        var ui = new UiScaler(frame.Width, frame.Height);
        return ReactionLevel.ReadLevel(frame, ui.TopCenter(BossStatusReader.FirstSquare.Offset(position * BossStatusReader.Pitch, 0)));
    }

    [Theory]
    [MemberData(nameof(Positions))]
    public async Task Reads_the_level_or_nothing_where_there_is_none(string file, int position)
    {
        var label = Labels.Single(l => l.File == file && l.Position == position);

        int? level = await ReadAsync(file, position);

        if (label.Hard) Assert.True(level is null || level == label.Level, $"{file} position {position}: {level}, labelled {label.Level}");
        else Assert.Equal(label.Level, level);
    }

    [Fact]
    public void The_fixtures_have_the_levels_seen_and_other_badges()
    {
        Assert.Contains(Labels, l => l.Status == "Corrosion" && l.Level == 3);
        Assert.Contains(Labels, l => l.Status == "Electrification" && l.Level == 2);
        Assert.Contains(Labels, l => l.Status == "Electrification" && l.Level == 4);
        Assert.Contains(Labels, l => l.Status is not null && ReactionLevel.Measured.Contains(l.Status) && l.Level is null);
        Assert.True(Labels.Count(l => l.Level is null) >= 3);
    }

    public static TheoryData<string> Screens() => [SimulatedScreen.Qhd.Name, SimulatedScreen.FullHd.Name];

    /// <summary>On smaller screens the numeral is smaller: a level may go unread, but is never wrong.</summary>
    [Theory]
    [MemberData(nameof(Screens))]
    public async Task On_smaller_screens_the_level_is_right_or_unknown(string screenName)
    {
        var screen = SimulatedScreen.Named(screenName);
        foreach (var label in Labels)
        {
            int? level = await ReadAsync(label.File, label.Position, screen);
            Assert.True(level is null || level == label.Level, $"{label.File} position {label.Position} at {screen.Width}x{screen.Height}: {level}, labelled {label.Level?.ToString() ?? "none"}");
        }
    }
}
