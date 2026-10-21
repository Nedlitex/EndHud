using EndHud.Vision.Inn;
using EndHud.Vision.Operators;
using Xunit;
using Xunit.Abstractions;

namespace EndHud.Vision.Tests;

/// <summary>
/// The chef picker (<see cref="ChefPickerReader"/>) on chef-picker.jpg at 4K, 1440p and 1080p:
/// its twelve readable grid cards against the labels in tests/data/Fixtures/inn/README.md (each card
/// named by comparing its portrait with the wiki avatar). A card may be left unnamed; a card named wrong fails.
/// </summary>
public class InnChefPickerReaderTests(ITestOutputHelper output)
{
    // The README's labels, by row then column.
    private static readonly string[][] Labels =
    [
        ["Lifeng", "Laevatain", "Yvonne", "Da_Pan"],
        ["Akekuri", "Catcher", "Estella", "Fluorite"],
        ["Antal", "Alesh", "Ardelia", "Last_Rite"],
    ];

    private static readonly Lazy<Task<(ChefPickerReader Reader, PortraitMatcher Matcher, InnData Data)>> Setup = new(async () =>
    {
        string folder = await OperatorCache.FolderAsync();
        var data = InnData.Load(folder) ?? throw new InvalidOperationException("The operator package has no inn data.");
        var library = await OperatorCache.LoadAsync(Path.Combine(Path.GetTempPath(), "endhud-inn-picker-tests-user"));
        var matcher = new PortraitMatcher(library);
        return (new ChefPickerReader(data, matcher), matcher, data);
    });

    public static TheoryData<string> Screens() => [SimulatedScreen.Uhd.Name, SimulatedScreen.Qhd.Name, SimulatedScreen.FullHd.Name];

    private static async Task<IReadOnlyList<ChefCard>> Read(SimulatedScreen screen)
    {
        var (reader, matcher, _) = await Setup.Value;
        var source = await InnScreenTests.Load("chef-picker");
        var frame = screen == SimulatedScreen.Uhd ? source : screen.Render(source);
        // The avatars are described in the background on first use: wait for them, as the app does by reading again.
        double scale = Math.Min(frame.Width / (double)UiScaler.ReferenceWidth, frame.Height / (double)UiScaler.ReferenceHeight);
        await matcher.PrepareWikiAvatars((int)Math.Round(240 * scale));
        return reader.Read(frame);
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public async Task Every_card_named_is_who_it_is_and_most_are_named(string screenName)
    {
        var screen = SimulatedScreen.Named(screenName);
        var cards = await Read(screen);
        double scale = screen.Width / (double)UiScaler.ReferenceWidth;
        int named = 0, wrong = 0;
        foreach (var card in cards)
        {
            // Which grid place the card is in: columns 270.5 px apart from x 109, rows 425.5 apart from y 310 (4K).
            int column = (int)Math.Round((card.Place.X / scale - 109) / 270.5), row = (int)Math.Round((card.Place.Y / scale - 310) / 425.5);
            string expected = Labels[row][column];
            output.WriteLine($"r{row + 1}c{column + 1}: icons {string.Join("+", card.Icons)}, flavor {card.Flavor}, " +
                             $"candidates {string.Join(" ", card.Candidates)} -> {card.OperatorId ?? "(unnamed)"} (is {expected}); portrait closest {card.Portrait?.Id} {card.Portrait?.Difference:F3}");
            if (card.OperatorId is null) continue;
            named++;
            if (card.OperatorId != expected) wrong++;
        }
        output.WriteLine($"{screen.Width}x{screen.Height}: {cards.Count} cards found, {named} named, {wrong} wrong");
        Assert.Equal(12, cards.Count);
        Assert.Equal(0, wrong);
        Assert.True(named >= 12, $"only {named} of 12 cards named");
    }

    [Fact]
    public async Task A_portrait_that_is_not_the_profile_leaves_the_card_unnamed()
    {
        var (reader, matcher, _) = await Setup.Value;
        var roster = matcher.Library.Operators;
        float[] Row(string closest, float distance)
        {
            var row = Enumerable.Repeat(0.9f, roster.Count).ToArray();
            row[roster.ToList().FindIndex(o => o.Id == closest)] = distance;
            return row;
        }
        // The portrait is Yvonne; the profile says Laevatain: disagree.
        Assert.Null(reader.Name(Row("Yvonne", 0.05f), ["Laevatain"]));
        // The portrait is no one the roster has.
        Assert.Null(reader.Name(Row("Laevatain", 0.2f), ["Laevatain"]));
        // Agree.
        Assert.Equal("Laevatain", reader.Name(Row("Laevatain", 0.05f), ["Laevatain"]));
        // Two candidates the portrait can't tell apart.
        var close = Row("Catcher", 0.05f);
        close[roster.ToList().FindIndex(o => o.Id == "Mi_Fu")] = 0.07f;
        Assert.Null(reader.Name(close, ["Catcher", "Mi_Fu"]));
        Assert.Null(reader.Name(null, ["Catcher"]));
    }

    [Fact]
    public async Task A_profile_gives_only_the_operators_who_have_it()
    {
        var (reader, _, _) = await Setup.Value;
        Assert.Equal(["Laevatain"], reader.Candidates([ChefIcon.Side, ChefIcon.Choice], "Spicy"));
        Assert.Equal(["Laevatain"], reader.Candidates([ChefIcon.Choice, ChefIcon.Side], "Spicy"));
        // Si can't cook: Yvonne alone.
        Assert.Equal(["Yvonne"], reader.Candidates([ChefIcon.Dessert, ChefIcon.Dessert], "Savory"));
        Assert.Equal(4, reader.Candidates([ChefIcon.Entree, ChefIcon.Side], "Salty").Count);
        Assert.Empty(reader.Candidates([ChefIcon.Entree, null], "Salty"));
        Assert.Empty(reader.Candidates([ChefIcon.Entree, ChefIcon.Side], null));
    }

    [Fact]
    public async Task Other_screens_have_no_cards()
    {
        var (reader, _, _) = await Setup.Value;
        Assert.Empty(reader.Read(await InnScreenTests.Load("menu-dishes")));
    }
}
