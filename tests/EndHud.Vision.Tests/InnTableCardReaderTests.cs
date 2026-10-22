using EndHud.Vision.Inn;
using Xunit;
using Xunit.Abstractions;

namespace EndHud.Vision.Tests;

/// <summary>
/// The table cards (<see cref="TableCardReader"/>) on every session frame of Fixtures/inn, against
/// what each card shows (labelled by hand, frame beside the pictures; see the fixtures' README).
/// At 4K every card is read exactly; at 1440p and 1080p the accuracy is measured and no card may be
/// read wrong (a card or an item left unread is allowed, never a wrong one).
/// </summary>
public class InnTableCardReaderTests(ITestOutputHelper output)
{
    private static readonly Lazy<Task<TableCardReader>> Reader = new(async () =>
    {
        string folder = await OperatorCache.FolderAsync();
        var data = InnData.Load(folder) ?? throw new InvalidOperationException("The operator package has no inn data.");
        return new TableCardReader(InnScreenTests.Text, await InnPictures.LoadAsync(folder, data));
    });

    // A card as the labels write it: "-" empty, "ordering", "eating", "paying", "gourmand",
    // "gourmand ordering", "gourmand order", "gourmand eating", or the order's items "id:state"
    // joined by " + " (state: s served, its pip green or the blue auto-serve spinner on it; w not).
    public static TheoryData<string, string[]> Frames() => new()
    {
        { "session-start", ["-", "-", "-", "-", "-", "-"] },
        { "session-first-order", ["-", "-", "-", "ordering", "-", "-"] },
        { "session-fire-0", ["-", "-", "-", "ordering", "-", "-"] },
        { "dispatch-panel-queue", ["ordering", "-", "-", "-", "-", "-"] },
        { "session-fire-meter", ["melon-meatball-soup:w + bottled-duskberry-juice:s", "fin-mato-soup:w + berry-fruity-toss:w", "ordering", "-", "-", "-"] },
        {
            "session-four-orders",
            ["-", "fin-mato-soup:s + bottled-duskberry-juice:s", "-", "melon-meatball-soup:w + bottled-milk-a:s",
             "mala-xiang-guo:w + pre-tide-lungjing:s", "mala-xiang-guo:w + bottled-duskberry-juice:w"]
        },
        {
            "session-gourmand-arrives",
            ["gourmand ordering", "eating", "-", "melon-meatball-soup:w + bottled-milk-a:s",
             "mala-xiang-guo:s + pre-tide-lungjing:s", "mala-xiang-guo:w + bottled-duskberry-juice:s"]
        },
        {
            "session-gourmand-order",
            ["gourmand order", "eating", "-", "melon-meatball-soup:w + bottled-milk-a:s",
             "mala-xiang-guo:s + pre-tide-lungjing:s", "mala-xiang-guo:w + bottled-duskberry-juice:s"]
        },
        {
            "session-dispatch-panel",
            ["paying", "fin-mato-soup:w + pre-tide-lungjing:s", "eating", "-", "gourmand ordering", "mala-xiang-guo:w + bottled-duskberry-juice:s"]
        },
        {
            "session-paying-and-waiting",
            ["fin-mato-soup:w + bottled-duskberry-juice:s", "paying", "gourmand", "mala-xiang-guo:w + bottled-duskberry-juice:s", "gourmand eating", "paying"]
        },
        {
            "session-gourmand-one-request",
            ["fin-mato-soup:w + bottled-duskberry-juice:s", "paying", "gourmand", "mala-xiang-guo:w + bottled-duskberry-juice:s", "paying", "paying"]
        },
        {
            "session-two-dishes",
            ["fin-mato-soup:s + bottled-duskberry-juice:s", "ordering", "gourmand eating", "mala-xiang-guo:w + bottled-duskberry-juice:s", "ordering", "paying"]
        },
        {
            "session-fire-92",
            ["fin-mato-soup:s + bottled-duskberry-juice:s", "ordering", "gourmand eating", "mala-xiang-guo:w + bottled-duskberry-juice:s", "ordering", "paying"]
        },
        {
            "session-fire-ready",
            ["ordering", "goldfloss-chiffon:s + mala-xiang-guo:w", "-", "paying", "goldfloss-chiffon:s + bottled-duskberry-juice:s",
             "spoon-swimmer:w + goldfloss-chiffon:w"]
        },
        {
            "session-closed",
            ["mala-xiang-guo:w + pre-tide-lungjing:s", "-", "melon-meatball-soup:w + bottled-milk-a:s", "eating", "ordering",
             "sweetbeans-on-snow:w + mala-xiang-guo:s"]
        },
    };

    public static TheoryData<string> SmallerScreens() => [SimulatedScreen.Qhd.Name, SimulatedScreen.FullHd.Name];

    [Theory]
    [MemberData(nameof(Frames))]
    public async Task Every_card_is_read_at_4K(string fixture, string[] expected)
    {
        var read = await (await Reader.Value).ReadAsync(await InnScreenTests.Load(fixture));

        Assert.Equal(expected, read.Select(Describe));
    }

    /// <summary>
    /// Every labelled card and ordered item, at each size: how many were read right, left unread, or
    /// read wrong. None may be wrong; the plan asks ≥ 95 % of orders read right (C2).
    /// </summary>
    [Theory]
    [MemberData(nameof(SmallerScreens))]
    public async Task Smaller_screens_read_95_percent_and_nothing_wrong(string screenName)
    {
        var screen = SimulatedScreen.Named(screenName);
        var reader = await Reader.Value;
        int cards = 0, cardsRight = 0, items = 0, itemsRight = 0;
        var wrong = new List<string>();
        foreach (var row in Frames())
        {
            string fixture = (string)row[0];
            var expected = (string[])row[1];
            var read = await reader.ReadAsync(screen.Render(await InnScreenTests.Load(fixture)));
            for (int i = 0; i < TableCardReader.Tables; i++)
            {
                cards++;
                string got = Describe(read[i]);
                if (got == expected[i]) cardsRight++;
                else
                {
                    output.WriteLine($"{fixture} table {i + 1}: read \"{got}\", is \"{expected[i]}\"");
                    if (read[i] is not null && !PartlyUnread(got, expected[i])) wrong.Add($"{fixture} table {i + 1}: read \"{got}\", is \"{expected[i]}\"");
                }
                if (expected[i].Contains(':'))
                {
                    var want = expected[i].Split(" + ");
                    var have = read[i]?.State == TableState.Order ? got.Split(" + ") : [];
                    items += want.Length;
                    itemsRight += want.Where((w, k) => k < have.Length && have[k] == w).Count();
                }
            }
        }
        output.WriteLine($"{screen.Width}x{screen.Height}: cards {cardsRight}/{cards} right, items {itemsRight}/{items} right ({100.0 * itemsRight / items:0.0} %)");
        Assert.Empty(wrong);
        Assert.True(itemsRight >= items * 0.95, $"{itemsRight} of {items} items read right");
    }

    /// <summary>
    /// A gourmand's order card has a pip per request under its number: the card's cream until the request is resolved, then
    /// mint green (served), yellow-green (served imperfectly) or red (failed). test_yuanyuan8 (the feast mode): at 231 s
    /// tables 1 and 2 have their drinks served and table 5 its dish failed; at 365 s table 1's dish was served imperfectly and
    /// both of table 6's requests failed. Until 2026-10-21 only the order cards' lime green counted, and no gourmand's
    /// request ever read resolved.
    /// </summary>
    [Theory]
    [InlineData("feast-cards-pips", 1, new[] { false, true })]
    [InlineData("feast-cards-pips", 2, new[] { false, true })]
    [InlineData("feast-cards-pips", 5, new[] { true, false })]
    [InlineData("feast-bubble-faces", 1, new[] { true, false })]
    [InlineData("feast-bubble-faces", 6, new[] { true, true })]
    public async Task A_gourmands_requests_read_resolved_by_their_pips(string fixture, int table, bool[] resolved)
    {
        foreach (var screen in new[] { SimulatedScreen.Uhd, SimulatedScreen.Qhd, SimulatedScreen.FullHd })
        {
            var frame = await InnScreenTests.Load(fixture);
            var read = await (await Reader.Value).ReadAsync(screen == SimulatedScreen.Uhd ? frame : screen.Render(frame));

            var card = read[table - 1];
            Assert.Equal(TableState.GourmandOrder, card?.State);
            Assert.Equal(resolved, card!.Served);
        }
    }

    /// <summary>An item read as unknown ("?") where the rest of the card matches.</summary>
    private static bool PartlyUnread(string got, string expected)
    {
        var g = got.Split(" + ");
        var e = expected.Split(" + ");
        return g.Length == e.Length && g.Zip(e).All(p => p.First == p.Second || p.First.StartsWith("?:") && p.First[1..] == p.Second[p.Second.IndexOf(':')..]);
    }

    internal static string Describe(TableCard? card) => card is null ? "unreadable" : card.State switch
    {
        TableState.Empty => "-",
        TableState.Ordering => "ordering",
        TableState.Eating => "eating",
        TableState.Paying => "paying",
        TableState.GourmandArrived => "gourmand",
        TableState.GourmandOrdering => "gourmand ordering",
        TableState.GourmandOrder => "gourmand order",
        TableState.GourmandEating => "gourmand eating",
        _ => string.Join(" + ", card.Items.Select(i => $"{i.Id ?? "?"}:{(i.Served ? "s" : "w")}")),
    };
}
