using System.Text.Json;
using EndHud.Vision;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// The Roman numeral level on a reaction's icon (<see cref="ReactionLevel"/>), on frames of the owner's
/// fights labelled by eye (Fixtures/reactions): Corrosion III, Electrification II, III and IV, the same
/// reactions with no badge (level I: the game draws no badge at I), and positions holding other badges
/// (white digits). Only a clearly absent badge is I; a badge hidden in a dark scene stays unknown.
/// </summary>
public class ReactionLevelTests
{
    /// <param name="Hard">An effect or a dim badge: the level may read as unknown, but never wrong.</param>
    private sealed record Label(string File, int Position, string? Status, int? Level, bool Hard = false);

    private static readonly Label[] Labels = JsonSerializer.Deserialize<Label[]>(
        File.ReadAllText(CombatFrameTests.Fixture(Path.Combine("reactions", "labels.json"))),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    /// <summary>
    /// The positions the level is read at: a measured reaction, or another badge (status null). Other
    /// statuses (Combustion) are never asked for a level (<see cref="BossStatusReader"/>).
    /// </summary>
    private static IEnumerable<Label> Read => Labels.Where(l => l.Status is null || ReactionLevel.Measured.Contains(l.Status));

    public static TheoryData<string, int> Positions()
    {
        var data = new TheoryData<string, int>();
        foreach (var label in Read) data.Add(label.File, label.Position);
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

    private static async Task<(Frame Frame, PixelRect Square)> LoadAsync(string file, int position)
    {
        var frame = await ImageFile.LoadAsync(CombatFrameTests.Fixture(Path.Combine("reactions", file)));
        var ui = new UiScaler(frame.Width, frame.Height);
        return (frame, ui.TopCenter(BossStatusReader.FirstSquare.Offset(position * BossStatusReader.Pitch, 0)));
    }

    /// <summary>Applies <paramref name="change"/> to each pixel of <paramref name="area"/> (all of it when null).</summary>
    private static Frame Repaint(Frame frame, PixelRect? area, Func<(byte R, byte G, byte B), (byte R, byte G, byte B)> change)
    {
        var bgra = (byte[])frame.Bgra.Clone();
        var a = area ?? new PixelRect(0, 0, frame.Width, frame.Height);
        for (int y = a.Y; y < a.Bottom; y++)
        for (int x = a.X; x < a.Right; x++)
        {
            int i = (y * frame.Width + x) * 4;
            var (r, g, b) = change((bgra[i + 2], bgra[i + 1], bgra[i]));
            (bgra[i + 2], bgra[i + 1], bgra[i]) = (r, g, b);
        }
        return new Frame(frame.Width, frame.Height, bgra);
    }

    /// <summary>The badge's box (<see cref="ReactionLevel.BadgeFrom"/>..<see cref="ReactionLevel.BadgeTo"/>).</summary>
    private static PixelRect BadgeBox(PixelRect square)
    {
        int x0 = square.X + (int)Math.Round(square.Width * ReactionLevel.BadgeFrom), y0 = square.Y + (int)Math.Round(square.Height * ReactionLevel.BadgeFrom);
        return new PixelRect(x0, y0, square.X + (int)Math.Round(square.Width * ReactionLevel.BadgeTo) - x0, square.Y + (int)Math.Round(square.Height * ReactionLevel.BadgeTo) - y0);
    }

    /// <summary>A badge whose numeral can't be seen (here: its ink painted over in the badge's dark) is a badge, not I.</summary>
    [Fact]
    public async Task A_badge_whose_numeral_is_hidden_is_unknown_not_level_one()
    {
        var (frame, square) = await LoadAsync("corrosion-III-84s.jpg", 1);
        Assert.Equal(3, ReactionLevel.ReadLevel(frame, square));

        var hidden = Repaint(frame, BadgeBox(square), p => p.G > 110 && p.G > p.B + 40 ? ((byte)40, (byte)40, (byte)42) : p);

        Assert.Equal(0, ReactionLevel.Measure(hidden, square).Height);
        Assert.Null(ReactionLevel.ReadLevel(hidden, square));
    }

    /// <summary>
    /// In a dark scene a badge would be dark on dark: an icon with no badge there is unknown, not I. The
    /// Typhoeus fight at 70 s (no badge, the art in full colour, the corner matching the square) and the
    /// same Electrification at 30 s with the whole picture darkened to a fifth.
    /// </summary>
    [Fact]
    public async Task An_icon_without_a_badge_in_a_dark_scene_is_unknown()
    {
        var (night, nightSquare) = await LoadAsync("level-I-typhoeus-70s.jpg", 0);
        Assert.Null(ReactionLevel.ReadLevel(night, nightSquare));

        var (frame, square) = await LoadAsync("other-typhoeus-30s.jpg", 1);
        Assert.Equal(1, ReactionLevel.ReadLevel(frame, square));
        var dark = Repaint(frame, null, p => ((byte)(p.R / 5), (byte)(p.G / 5), (byte)(p.B / 5)));
        Assert.Null(ReactionLevel.ReadLevel(dark, square));
    }

    /// <summary>
    /// Something over the corner that is neither dark nor the square's own grey (a cyan slash, as in the
    /// Zhuang fight) may hide a badge: unknown, not I, even with the icon's art still in full colour.
    /// </summary>
    [Fact]
    public async Task A_corner_covered_by_an_effect_is_unknown()
    {
        var (frame, square) = await LoadAsync("other-typhoeus-30s.jpg", 1);
        Assert.Equal(1, ReactionLevel.ReadLevel(frame, square));

        // The waves' yellow stays; everything else in the badge's box turns light cyan.
        var slash = Repaint(frame, BadgeBox(square), p => p.R > 150 && p.B < 100 ? p : ((byte)120, (byte)200, (byte)220));

        Assert.True(ReactionLevel.Measure(slash, square).InkShare >= ReactionLevel.MinAbsentInk);
        Assert.Null(ReactionLevel.ReadLevel(slash, square));
    }

    /// <summary>A bright effect over the badge's corner (a flash, a slash) hides the badge: unknown, not I.</summary>
    [Fact]
    public async Task A_badge_covered_by_a_bright_effect_is_unknown_not_level_one()
    {
        var (frame, square) = await LoadAsync("electrification-II-36s.jpg", 0);
        Assert.Equal(2, ReactionLevel.ReadLevel(frame, square));

        var flash = Repaint(frame, BadgeBox(square), p => ((byte)(p.R / 4 + 190), (byte)(p.G / 4 + 190), (byte)(p.B / 4 + 190)));

        Assert.Null(ReactionLevel.ReadLevel(flash, square));
    }

    /// <summary>
    /// An icon fading in, or under a white flash, is washed out, its badge too: pale, not dark, the numeral
    /// no longer yellow. That corner can't be told from no badge: unknown, not I.
    /// </summary>
    [Fact]
    public async Task A_washed_out_badge_is_unknown_not_level_one()
    {
        var (frame, square) = await LoadAsync("electrification-II-36s.jpg", 0);
        var box = BadgeBox(square);
        var washedArea = new PixelRect(square.X, square.Y, box.Right - square.X, box.Bottom - square.Y);

        var washed = Repaint(frame, washedArea, p => ((byte)(p.R / 4 + 191), (byte)(p.G / 4 + 191), (byte)(p.B / 4 + 191)));

        Assert.Equal(0, ReactionLevel.Measure(washed, square).Height);
        Assert.Null(ReactionLevel.ReadLevel(washed, square));
    }

    [Fact]
    public void The_fixtures_have_the_levels_seen_and_other_badges()
    {
        Assert.Contains(Labels, l => l.Status == "Corrosion" && l.Level == 3);
        Assert.Contains(Labels, l => l.Status == "Electrification" && l.Level == 2);
        Assert.Contains(Labels, l => l.Status == "Electrification" && l.Level == 4);
        Assert.Contains(Labels, l => l.Status == "Corrosion" && l.Level == 1 && !l.Hard);
        Assert.Contains(Labels, l => l.Status == "Electrification" && l.Level == 1 && !l.Hard);
        Assert.True(Labels.Count(l => l.Level is null) >= 3);
    }

    public static TheoryData<string> Screens() => [SimulatedScreen.Qhd.Name, SimulatedScreen.FullHd.Name];

    /// <summary>On smaller screens the numeral is smaller: a level may go unread, but is never wrong.</summary>
    [Theory]
    [MemberData(nameof(Screens))]
    public async Task On_smaller_screens_the_level_is_right_or_unknown(string screenName)
    {
        var screen = SimulatedScreen.Named(screenName);
        foreach (var label in Read)
        {
            int? level = await ReadAsync(label.File, label.Position, screen);
            Assert.True(level is null || level == label.Level, $"{label.File} position {label.Position} at {screen.Width}x{screen.Height}: {level}, labelled {label.Level?.ToString() ?? "none"}");
        }
    }
}
