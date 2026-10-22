using EndHud.Vision.Inn;
using Xunit;
using Xunit.Abstractions;
using static EndHud.Tests.InnTestKit;

namespace EndHud.Tests;

/// <summary>
/// A gourmand's dish is an order for the kitchens like any table's (owner, 2026-10-22: "table 1 wants 肉卷, but it is NEVER
/// suggested in the kitchen"; 1a00587): the dish their tile names gets a kitchen's call, the call goes once the request reads
/// resolved (its pip), a dish in hand covers it as it does a table's order, and a table ordering the same dish gets its own.
/// The dish called is the one the tile shows, held as steady as the tile (owner, 2026-10-21: "the recommendation should stay
/// steady").
/// </summary>
public class InnGourmandOrderTests(ITestOutputHelper output)
{
    private const string A = "melon-meatball-soup", B = "fin-mato-soup", C = "mala-xiang-guo", D = "sweetbeans-on-snow", E = "meat-baozi";
    private static readonly string[] Menu = [A, B, C, D, E, "bottled-duskberry-juice", "pre-tide-lungjing", "bottled-milk-a"];
    private static readonly string[] Dishes = [A, B, C, D, E];

    private static InnSession Prepared()
    {
        var session = NewSession();
        session.Update(SetupScreen(0, 1, 2, 3));
        session.Update(MenuScreen(1, Menu));
        return session;
    }

    private static Gourmand Feast(int number) => Inn.Gourmands.Single(g => g.Pool == "feast" && g.Number == number);

    /// <summary>
    /// Feast gourmand 9's "不是甜品的、酸的" (only <see cref="B"/>, fin-mato-soup, meets it on this menu) and "不要奶香的" (a
    /// drink): one dish for the kitchens.
    /// </summary>
    private static GourmandBubble SourDishAndADrink()
    {
        string[] lines = [Feast(9).Requests[2].Text, Feast(9).Requests[3].Text];
        return new GourmandBubble(string.Concat(lines), lines) { Starts = [0, 1], Resolved = [false, false] };
    }

    private static TableCard Gourmand(int table, params bool[] pips) => new(table, TableState.GourmandOrder, [], pips.Length > 0 ? pips : [false, false]);

    private static IReadOnlyList<HeldDish?> Tray(params string[] held) =>
        [.. held.Select(id => (HeldDish?)new HeldDish(id, Choice: false)), .. Enumerable.Repeat<HeldDish?>(null, TrayReader.Slots - held.Length)];

    private static InnReading Frame(double at, IReadOnlyList<TableCard?> tables, GourmandBubble? bubble = null, IReadOnlyList<HeldDish?>? tray = null,
        DispatchPanel? panel = null) =>
        Session(at, tables: tables, bubble: bubble, panel: panel) with { Tray = tray ?? Tray() };

    /// <summary>How many of <paramref name="dish"/> the advice sends to a kitchen for <paramref name="table"/>.</summary>
    private static int Sent(InnAdvice advice, string dish, int table) =>
        advice.Dispatch!.Kitchens.Sum(k => k.Queue.Count(q => q.Dish.Id == dish && q.Table == table) + k.Replaces.Count(r => r.Put.Id == dish && r.Table == table));

    private static bool Called(InnAdvice advice, string dish, int table) =>
        advice.Calls.Any(c => c.Kind is InnCallKind.Queue or InnCallKind.Swap && c.Dish?.Id == dish && c.Table == table);

    /// <summary>The items <paramref name="table"/>'s tile names, by request, as the overlay draws them.</summary>
    private static IReadOnlyList<InnWant> Tile(InnAdvice advice, int table) =>
        InnGourmandView.Of(advice.Gourmands.Single(g => g.Table == table), Text);

    /// <summary>The gourmand at table 6 says what they want; nothing else is ordered.</summary>
    private static InnSession GourmandAtSix(IReadOnlyList<HeldDish?>? tray = null)
    {
        var session = Prepared();
        for (double t = 10; t < 11; t += 0.3) session.Update(Frame(t, Tables(Gourmand(6)), SourDishAndADrink(), tray));
        return session;
    }

    [Fact]
    public void A_gourmands_dish_gets_a_kitchens_call_the_one_their_tile_names()
    {
        var session = GourmandAtSix();
        var advice = session.Advice(S(10.9))!;

        Assert.Equal(B, Tile(advice, 6)[0].ItemId);
        Assert.Equal(1, Sent(advice, B, 6));
        Assert.True(Called(advice, B, 6), "the gourmand's dish has no kitchen's call: " + string.Join(", ", advice.Calls.Select(c => c.Key)));
        // The drink is served from the bar: no kitchen is told anything for it.
        Assert.All(advice.Dispatch!.Kitchens.SelectMany(k => k.Queue).Where(q => q.Table == 6), q => Assert.Equal(B, q.Dish.Id));
    }

    [Fact]
    public void A_gourmands_dish_is_no_longer_called_once_its_request_reads_resolved()
    {
        var session = GourmandAtSix();
        Assert.True(Called(session.Advice(S(10.9))!, B, 6));

        // The soup served: its pip reads resolved on two readings in a row (12.3 s). The advice drops it at once; its call,
        // like any order's that goes, after InnCallHold.GraceSeconds (a reading that dropped it may come right back).
        InnAdvice advice = null!;
        for (double t = 12; t < 12.3 + InnCallHold.GraceSeconds.TotalSeconds + 0.6; t += 0.3)
        {
            session.Update(Frame(t, Tables(Gourmand(6, true, false))));
            advice = session.Advice(S(t))!;
            if (t >= 12.3) Assert.Equal(0, Sent(advice, B, 6));
        }

        Assert.False(Called(advice, B, 6), "a call for the gourmand's dish after their request read resolved");
        // The drink still waits, its tile still up.
        Assert.Single(Tile(advice, 6));
    }

    /// <summary>
    /// A request keeps its number when the one before it is resolved: the tile's rows are numbered by what the gourmand
    /// said (① the soup, ② the drink), so with the soup served the drink is still ②, never renumbered ① (open since
    /// test_yuanyuan8: the remaining request read as the first again).
    /// </summary>
    [Fact]
    public void A_request_keeps_its_number_when_the_one_before_it_is_resolved()
    {
        var session = GourmandAtSix();
        var both = InnGourmandView.Banners(Tile(session.Advice(S(10.9))!, 6), Text).Single();
        string numbers = Text("Inn.Gourmand.Numbers");
        Assert.Equal([numbers[0].ToString(), numbers[1].ToString()], both.Rows.Select(r => r.Number));

        for (double t = 12; t < 13; t += 0.3) session.Update(Frame(t, Tables(Gourmand(6, true, false))));
        var wants = Tile(session.Advice(S(12.9))!, 6);
        var left = InnGourmandView.Banners(wants, Text).Single();

        Assert.Equal(1, Assert.Single(wants).Request);
        Assert.Equal(numbers[1].ToString(), Assert.Single(left.Rows).Number);
        Assert.Equal(both.Rows[1].Request, left.Rows[0].Request);
        // The tile keeps its table's spot.
        Assert.Equal(InnGourmandLayout.Arrange([both], 1920, 1080, 1).Places, InnGourmandLayout.Arrange([left], 1920, 1080, 1).Places);
    }

    [Fact]
    public void A_dish_in_hand_covers_a_gourmands_request_no_kitchen_is_told_to_cook_another()
    {
        var session = GourmandAtSix(Tray(B));
        var advice = session.Advice(S(10.9))!;

        Assert.Equal(B, Tile(advice, 6)[0].ItemId);
        Assert.Equal(0, Sent(advice, B, 6));
        Assert.False(Called(advice, B, 6), "the gourmand's soup is in hand, yet a call says to queue it");
    }

    /// <summary>
    /// The gourmand's dish and a table's order of the same dish are two dishes to cook (both need one, owner, 2026-10-21: "one
    /// dish in one circle, unless two tables each need it"); one in hand covers one of them, and only one more is cooked.
    /// </summary>
    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 1)]
    public void A_table_ordering_the_gourmands_dish_too_gets_its_own_unless_one_is_in_hand(bool oneInHand, int cooked)
    {
        var session = Prepared();
        var tables = Tables(Order(3, B), Gourmand(6));
        for (double t = 10; t < 11; t += 0.3) session.Update(Frame(t, tables, SourDishAndADrink(), oneInHand ? Tray(B) : Tray()));
        var advice = session.Advice(S(10.9))!;

        int sent = Sent(advice, B, 3) + Sent(advice, B, 6);
        output.WriteLine($"in hand {oneInHand}: table 3 {Sent(advice, B, 3)}, gourmand {Sent(advice, B, 6)}; calls {string.Join(", ", advice.Calls.Select(c => c.Key))}");
        Assert.Equal(cooked, sent);
        Assert.Equal(cooked, advice.Calls.Count(c => c.Kind is InnCallKind.Queue or InnCallKind.Swap && c.Dish?.Id == B));
    }

    /// <summary>
    /// The dish called for a gourmand is the one their tile shows, every frame, and it doesn't switch while it still meets
    /// their request (the replays of the owner's sessions, before the fix: y3 277.2 s kitchen 3's fin-mato-soup for table 4
    /// became berry-sweet-sip with nothing done to it; y4, y5 and y7 alike, all a gourmand's dish following the advice's
    /// best-ranked option of the moment while the tile held another). Normal gourmand 3's "不辣的、不咸的汤羹" (melon-meatball-soup
    /// and fin-mato-soup both meet it, 40 s each); table 3's order of the other soup is queued in kitchen 1 and cooks on,
    /// so that soup comes out sooner and sooner than the gourmand's still to cook.
    /// </summary>
    [Fact]
    public void A_gourmands_called_dish_is_their_tiles_and_holds_while_it_meets_the_request()
    {
        var request = Inn.Gourmands.Single(g => g.Pool == "normal" && g.Number == 3).Requests[0];
        var bubble = new GourmandBubble(request.Text, [request.Text]);
        var session = Prepared();
        var display = new InnDisplay();
        var tables = Tables(Order(3, A), Gourmand(6));
        var called = new List<string>();
        for (double t = 10; t < 40; t += 0.3)
        {
            // From 11 s table 3's soup is in kitchen 1, cooking (40 s).
            var panel = t >= 11 ? Panel(1, 3, Dishes, (A, (int?)Math.Max(1, (int)Math.Round(40 - (t - 11))))) : null;
            display.Show(session, Frame(t, tables, t < 11 ? bubble : null, panel: panel), S(t));
            var views = display.Views(session, S(t), callsEnabled: true, Text);
            var advice = display.LastAdvice!;
            var forGourmand = advice.Dispatch!.Kitchens.SelectMany(k => k.Queue.Where(q => q.Table == 6).Select(q => q.Dish.Id)
                .Concat(k.Replaces.Where(r => r.Table == 6).Select(r => r.Put.Id))).ToList();
            var tile = Tile(advice, 6).Single();
            output.WriteLine($"{t:F1}: tile {tile.ItemId}, shown {views.Wants.SingleOrDefault(w => w.Table == 6)?.ItemId}, sent {string.Join(",", forGourmand)}; calls {string.Join(", ", advice.Calls.Select(c => c.Key))}");
            if (forGourmand.Count == 0) continue;
            Assert.Equal([tile.ItemId!], forGourmand);
            // What the player sees on the tile (held, InnWantsHold) is what the kitchens are told.
            if (views.Wants.SingleOrDefault(w => w.Table == 6) is { } shown) Assert.Equal(tile.ItemId, shown.ItemId);
            called.Add(forGourmand[0]);
        }
        output.WriteLine(string.Join(" ", called.Distinct()));
        Assert.NotEmpty(called);
        Assert.Single(called.Distinct());
    }
}
