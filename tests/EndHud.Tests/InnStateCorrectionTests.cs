using EndHud.Vision.Inn;
using Xunit;
using static EndHud.Tests.InnTestKit;

namespace EndHud.Tests;

/// <summary>
/// What the session only estimates (each kitchen's queue, the counter, which counter dish covers which order) is simulated
/// from the last state known and put right by every reading that contradicts it (owner, 2026-10-21, test_yuanyuan7: "it
/// should be simulated from the last known state whenever possible and refreshed when see the latest state"). On that
/// session the counter estimate held 15-20 dishes on a counter of 8, and two 福寿全 and two 白鳞高汤 taken to be out for orders
/// kept tables 4, 5 and 6 waiting with no call for over a minute ("we ended up stuck behind a lot of soups").
/// </summary>
public class InnStateCorrectionTests
{
    // A soup (40 s), a side (35 s), a dessert (25 s) and an entree (15 s), base speed (no chef named).
    private const string A = "melon-meatball-soup", C = "mala-xiang-guo", D = "sweetbeans-on-snow", E = "meat-baozi";
    private static readonly string[] Menu = [A, C, D, E];

    private static InnSession Prepared(List<string>? log = null)
    {
        var session = log is null ? NewSession() : new InnSession(Inn, Library.Operators, ReadAllAlong, log: log.Add, warn: log.Add);
        session.Update(SetupScreen(0, 1, 2, 3));
        session.Update(MenuScreen(1, Menu));
        return session;
    }

    private static IReadOnlyList<HeldDish?> Tray(params string[] held) =>
        [.. held.Select(id => (HeldDish?)new HeldDish(id, Choice: false)), .. Enumerable.Repeat<HeldDish?>(null, TrayReader.Slots - held.Length)];

    private static InnReading Frame(double at, IReadOnlyList<TableCard?> tables, IReadOnlyList<HeldDish?>? tray = null) =>
        Session(at, tables: tables) with { Tray = tray };

    /// <summary>The panel open on <paramref name="kitchen"/> for two frames (the panel counts as open, a queue change is confirmed).</summary>
    private static void PanelTwice(InnSession session, double at, IReadOnlyList<TableCard?> tables, int kitchen, params (string, int?)[] queue)
    {
        session.Update(Session(at, tables: tables, panel: Panel(kitchen, 3, Menu, queue)));
        session.Update(Session(at + 0.3, tables: tables, panel: Panel(kitchen, 3, Menu, queue)));
    }

    private static bool Sent(InnAdvice advice, string dish, int table) =>
        advice.Dispatch!.Kitchens.Any(k => k.Queue.Any(q => q.Dish.Id == dish && q.Table == table) || k.Replaces.Any(r => r.Put.Id == dish && r.Table == table));

    /// <summary>
    /// test_yuanyuan7 at 157.5 s: kitchen 1 was taken to hold [福寿全, 八宝饭, 千果珍珠圆]; the panel read [福寿全 9 s, 千果珍珠圆] (the player
    /// had taken 八宝饭 out). Read as "all three came out and the player queued 福寿全 and 千果珍珠圆 anew", it put three dishes on the
    /// counter that weren't there and counted 福寿全 out a second time 9 s later. A dish cooking with 9 s left can't have come
    /// out, nor the two behind it: the queue is taken as read and nothing goes on the counter.
    /// </summary>
    [Fact]
    public void A_dish_taken_out_of_a_queue_is_not_read_as_every_dish_out()
    {
        var session = Prepared();
        var tables = Tables();
        PanelTwice(session, 10, tables, 1, (C, 30), (D, null), (E, null));
        PanelTwice(session, 12, tables, 1, (C, 28), (E, null));
        PanelTwice(session, 13, tables, 1, (C, 27), (E, null)); // read so for a second: taken

        Assert.Equal("", session.CounterSignature());
        Assert.Contains($"1:{C},{E}", session.QueueSignature());
    }

    /// <summary>A queue read after its front dish really did come out (its time was up): that dish is on the counter, as before.</summary>
    [Fact]
    public void A_dish_whose_time_was_up_is_out_when_the_panel_no_longer_shows_it()
    {
        var session = Prepared();
        var tables = Tables();
        PanelTwice(session, 10, tables, 1, (E, 2), (D, null));
        PanelTwice(session, 12.5, tables, 1, (D, 25));

        Assert.Equal($"{E}x1", session.CounterSignature());
        Assert.Contains($"1:{D}", session.QueueSignature());
    }

    /// <summary>
    /// test_yuanyuan7 at 208.5 s: the tray picked up 福寿全 and dropped it 2 s later, and table 1's gourmand read eating at 212.7 s.
    /// A gourmand's card shows no served pips, so no table read served with it, and the dish was taken as put back on the counter,
    /// where it covered table 5's and then table 6's 福寿全 for over 100 s. A dish leaving the hand while a gourmand waits for their
    /// order went to the gourmand.
    /// </summary>
    [Fact]
    public void A_dish_leaving_the_hand_while_a_gourmand_waits_went_to_the_gourmand()
    {
        var log = new List<string>();
        var session = Prepared(log);
        var gourmand = new TableCard(1, TableState.GourmandOrder, [], []);
        var tables = Tables(gourmand, Order(5, D));
        PanelTwice(session, 10, tables, 1, (D, 5));
        session.Update(Frame(20, tables, Tray()));
        // The dessert came out for table 5's order; picked up, then gone from the hand with no guest's card read served.
        foreach (double t in new[] { 21, 21.3 }) session.Update(Frame(t, tables, Tray(D)));
        foreach (double t in new[] { 22, 22.3, 23, 26 }) session.Update(Frame(t, tables, Tray()));

        Assert.DoesNotContain(D, session.CounterSignature());
        Assert.True(Sent(session.Advice(S(26))!, D, 5), "the dessert went to the gourmand: table 5 still needs one");
        Assert.Contains(log, l => l.Contains("gourmand", StringComparison.Ordinal) && l.Contains(D, StringComparison.Ordinal));
    }

    /// <summary>With no gourmand waiting, a dish leaving the hand with no table read served was put back on the counter (放回出餐台), as before.</summary>
    [Fact]
    public void A_dish_leaving_the_hand_with_no_gourmand_waiting_is_back_on_the_counter()
    {
        var session = Prepared();
        var tables = Tables(Order(5, D));
        PanelTwice(session, 10, tables, 1, (D, 5));
        session.Update(Frame(20, tables, Tray()));
        foreach (double t in new[] { 21, 21.3 }) session.Update(Frame(t, tables, Tray(D)));
        foreach (double t in new[] { 22, 22.3, 23, 26 }) session.Update(Frame(t, tables, Tray()));

        Assert.Contains($"{D}x1", session.CounterSignature());
        Assert.False(Sent(session.Advice(S(26))!, D, 5), "the dessert put back covers table 5");
    }

    /// <summary>
    /// A dish out for an order whose table still reads it unserved long after: the estimate is wrong about it (test_yuanyuan7:
    /// 福寿全 "out for" table 6's order for 111 s, white-fin soups for 45 s, while the tables waited and nothing was called). Once
    /// it has been out <see cref="InnSession.CoverTrustedFor"/>, the order is no longer taken as covered: it is called again,
    /// and the correction is logged.
    /// </summary>
    [Fact]
    public void An_order_still_waiting_long_after_its_dish_came_out_is_called_again()
    {
        var log = new List<string>();
        var session = Prepared(log);
        var tables = Tables(Order(4, D));
        PanelTwice(session, 10, tables, 1, (D, 5));
        session.Update(Frame(16, tables));
        Assert.False(Sent(session.Advice(S(16))!, D, 4), "the dessert just came out for table 4");

        double later = 15.3 + InnSession.CoverTrustedFor.TotalSeconds + 1;
        session.Update(Frame(later, tables));

        Assert.True(Sent(session.Advice(S(later))!, D, 4), "table 4 still waits long after its dessert came out: it isn't there");
        Assert.Contains(log, l => l.StartsWith("inn: corrected", StringComparison.Ordinal) && l.Contains(D, StringComparison.Ordinal));
    }

    /// <summary>
    /// The counter holds 8 dishes (owner). The estimate held 15-20 on test_yuanyuan7 from 250 s: more than 8 is a contradiction
    /// with the game's own rule, and the oldest dishes cooked ahead (where the estimate drifts) go first.
    /// </summary>
    [Fact]
    public void The_counter_estimate_never_holds_more_than_the_counter()
    {
        var log = new List<string>();
        var session = Prepared(log);
        var tables = Tables();
        foreach (int k in new[] { 1, 2, 3 }) PanelTwice(session, 10 + k, tables, k, (E, 5), (E, null), (E, null));
        session.Update(Frame(100, tables));

        int onCounter = session.CounterSignature().Split(',', StringSplitOptions.RemoveEmptyEntries).Sum(c => int.Parse(c[(c.IndexOf('x') + 1)..]));
        Assert.Equal(InnCounter.Capacity, onCounter);
        Assert.Contains(log, l => l.StartsWith("inn: corrected", StringComparison.Ordinal) && l.Contains("counter", StringComparison.Ordinal));
    }
}
