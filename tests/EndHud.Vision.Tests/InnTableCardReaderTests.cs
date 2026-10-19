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
    // "gourmand ordering", "gourmand order", "gourmand eating", or the order's items "id:progress"
    // joined by " + " (progress: w waiting, m being made, r ready).
    public static TheoryData<string, string[]> Frames() => new()
    {
        { "session-start", ["-", "-", "-", "-", "-", "-"] },
        { "session-first-order", ["-", "-", "-", "ordering", "-", "-"] },
        { "session-fire-0", ["-", "-", "-", "ordering", "-", "-"] },
        { "dispatch-panel-queue", ["ordering", "-", "-", "-", "-", "-"] },
        { "session-fire-meter", ["melon-meatball-soup:w + bottled-duskberry-juice:r", "fin-mato-soup:w + berry-fruity-toss:w", "ordering", "-", "-", "-"] },
        {
            "session-four-orders",
            ["-", "fin-mato-soup:m + bottled-duskberry-juice:r", "-", "melon-meatball-soup:w + bottled-milk-a:r",
             "mala-xiang-guo:w + pre-tide-lungjing:r", "mala-xiang-guo:w + bottled-duskberry-juice:w"]
        },
        {
            "session-gourmand-arrives",
            ["gourmand ordering", "eating", "-", "melon-meatball-soup:w + bottled-milk-a:r",
             "mala-xiang-guo:m + pre-tide-lungjing:r", "mala-xiang-guo:w + bottled-duskberry-juice:r"]
        },
        {
            "session-gourmand-order",
            ["gourmand order", "eating", "-", "melon-meatball-soup:w + bottled-milk-a:r",
             "mala-xiang-guo:r + pre-tide-lungjing:r", "mala-xiang-guo:w + bottled-duskberry-juice:r"]
        },
        {
            "session-dispatch-panel",
            ["paying", "fin-mato-soup:w + pre-tide-lungjing:r", "eating", "-", "gourmand ordering", "mala-xiang-guo:w + bottled-duskberry-juice:r"]
        },
        {
            "session-paying-and-waiting",
            ["fin-mato-soup:w + bottled-duskberry-juice:r", "paying", "gourmand", "mala-xiang-guo:w + bottled-duskberry-juice:r", "gourmand eating", "paying"]
        },
        {
            "session-gourmand-one-request",
            ["fin-mato-soup:w + bottled-duskberry-juice:r", "paying", "gourmand", "mala-xiang-guo:w + bottled-duskberry-juice:r", "paying", "paying"]
        },
        {
            "session-two-dishes",
            ["fin-mato-soup:r + bottled-duskberry-juice:r", "ordering", "gourmand eating", "mala-xiang-guo:w + bottled-duskberry-juice:r", "ordering", "paying"]
        },
        {
            "session-fire-92",
            ["fin-mato-soup:r + bottled-duskberry-juice:r", "ordering", "gourmand eating", "mala-xiang-guo:w + bottled-duskberry-juice:r", "ordering", "paying"]
        },
        {
            "session-fire-ready",
            ["ordering", "goldfloss-chiffon:r + mala-xiang-guo:w", "-", "paying", "goldfloss-chiffon:m + bottled-duskberry-juice:r",
             "spoon-swimmer:w + goldfloss-chiffon:w"]
        },
        {
            "session-closed",
            ["mala-xiang-guo:w + pre-tide-lungjing:m", "-", "melon-meatball-soup:w + bottled-milk-a:r", "eating", "ordering",
             "sweetbeans-on-snow:w + mala-xiang-guo:r"]
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
        _ => string.Join(" + ", card.Items.Select(i => $"{i.Id ?? "?"}:{i.Progress switch { ItemProgress.Making => "m", ItemProgress.Ready => "r", _ => "w" }}")),
    };
}
