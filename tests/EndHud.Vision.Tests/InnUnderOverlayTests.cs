using EndHud.Vision.Inn;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// The inn with MSI Afterburner's on-screen display (drawn by RivaTuner inside the game's frames) at its default place, the
/// top left, where it covers the inn's title and timer: the only signs of every inn screen until 2026-10-24, so the helper
/// never appeared (51 of 52 inn frames read as "not the inn"). The session, the dispatch panel, the menu and the setup
/// screen are now told by other signs too (<see cref="InnScreen"/>); a chef picker and the theme list are told only by their
/// titles and may read as unknown, never as another screen.
/// </summary>
public class InnUnderOverlayTests
{
    private static readonly InnText Text = InnScreenTests.Text;

    public static TheoryData<string, InnScreenKind> Frames()
    {
        var data = new TheoryData<string, InnScreenKind>();
        foreach (var row in InnScreenTests.Frames()) data.Add((string)row[0], (InnScreenKind)row[1]);
        return data;
    }

    [Theory]
    [MemberData(nameof(Frames))]
    public async Task The_screen_is_told_with_a_display_over_the_title(string fixture, InnScreenKind kind)
    {
        var clean = await InnScreenTests.Load(fixture);
        var failures = new List<string>();
        foreach (var screen in new[] { SimulatedScreen.Uhd, SimulatedScreen.FullHd })
        foreach (bool background in new[] { true, false })
        {
            var frame = SyntheticOsd.Draw(screen.Render(clean), OsdPosition.TopLeft, background);
            var read = await InnScreen.DetectAsync(Text, frame);
            // The session's first frame has its HUD fading in: the Full Blaze meter is too faint to read (its timer reads only
            // at 4K, uncovered). Unknown there is a frame missed; the next ones are told.
            bool titleOnly = kind is InnScreenKind.ChefPicker or InnScreenKind.ThemeList || fixture == "session-start";
            if (titleOnly ? read is not null && read != kind : read != kind)
                failures.Add($"{screen.Width}x{screen.Height}{(background ? " on its background" : "")}: read {read?.ToString() ?? "not the inn"}");
        }
        Assert.True(failures.Count == 0, $"{fixture} ({kind}) with the display at the top left: " + string.Join("; ", failures));
    }

    internal static readonly Lazy<Task<TableCardReader>> Tables = new(async () =>
    {
        string folder = await OperatorCache.FolderAsync();
        var data = InnData.Load(folder) ?? throw new InvalidOperationException("The operator package has no inn data.");
        return new TableCardReader(Text, await InnPictures.LoadAsync(folder, data));
    });

    /// <summary>A card as compared: its state, items (id and served) and pips; "unknown" for null.</summary>
    internal static string Describe(TableCard? card) => card is null ? "unknown"
        : $"{card.State}[{string.Join(" + ", card.Items.Select(i => $"{i.Id}:{(i.Served ? "s" : "w")}"))}]({string.Join("", card.Served.Select(s => s ? "1" : "0"))})";

    /// <summary>
    /// The display at the top centre covers table cards: a covered card is unknown, never "Empty" (the advisor would drop
    /// that table's order) nor anything else it doesn't show (2026-10-24 investigation: occupied cards read Empty 84 times).
    /// </summary>
    [Theory]
    [InlineData("feast-cards-pips")]
    [InlineData("session-four-orders")]
    [InlineData("session-paying-and-waiting")]
    public async Task A_covered_table_card_is_unknown_never_another_state(string fixture)
    {
        var reader = await Tables.Value;
        var source = await InnScreenTests.Load(fixture);
        var failures = new List<string>();
        foreach (var screen in new[] { SimulatedScreen.Uhd, SimulatedScreen.FullHd })
        {
            var clean = screen.Render(source);
            var expected = (await reader.ReadAsync(clean)).Select(Describe).ToList();
            foreach (bool background in new[] { true, false })
            {
                var read = (await reader.ReadAsync(SyntheticOsd.Draw(clean, OsdPosition.TopCentre, background))).Select(Describe).ToList();
                for (int i = 0; i < read.Count; i++)
                    if (read[i] != "unknown" && read[i] != expected[i])
                        failures.Add($"{screen.Width}x{screen.Height}{(background ? " on its background" : "")} table {i + 1}: {expected[i]} read {read[i]}");
            }
        }
        Assert.True(failures.Count == 0, $"{fixture} with the display at the top centre: " + string.Join("; ", failures));
    }

    /// <summary>Told by the title or timer, no sign is named; told by another sign, it is (the app logs that the top left is covered).</summary>
    [Fact]
    public async Task The_sign_is_named_only_when_the_title_was_covered()
    {
        var clean = await InnScreenTests.Load("session-fire-92");

        Assert.Equal((InnScreenKind.Session, (string?)null), await InnScreen.DetectWithSignAsync(Text, clean));
        var covered = await InnScreen.DetectWithSignAsync(Text, SyntheticOsd.Draw(clean, OsdPosition.TopLeft, background: true));
        Assert.Equal(InnScreenKind.Session, covered?.Kind);
        Assert.Equal("the Full Blaze meter", covered?.Sign);
    }

    /// <summary>Frames that aren't the inn (combat, the open world, the squad screen): not the inn by any sign, display or not.</summary>
    public static TheoryData<string> NotTheInn()
    {
        var data = new TheoryData<string>();
        string fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        foreach (string file in Directory.GetFiles(fixtures).Where(f => f.EndsWith(".jpg") || f.EndsWith(".png")).Order())
            data.Add(Path.GetFileName(file));
        foreach (string file in Directory.GetFiles(Path.Combine(fixtures, "panel-or-not"), "*.jpg").Order())
            data.Add(Path.Combine("panel-or-not", Path.GetFileName(file)));
        return data;
    }

    [Theory]
    [MemberData(nameof(NotTheInn))]
    public async Task Other_screens_are_not_the_inn_with_or_without_a_display(string fixture)
    {
        var clean = await ImageFile.LoadAsync(CombatFrameTests.Fixture(fixture));
        Assert.Null(await InnScreen.DetectAsync(Text, clean));
        foreach (var position in SyntheticOsd.Positions)
        foreach (bool background in new[] { true, false })
            Assert.True(await InnScreen.DetectAsync(Text, SyntheticOsd.Draw(clean, position, background)) is null,
                $"{fixture} with the display at {position}{(background ? " on its background" : "")} read as the inn");
    }
}
