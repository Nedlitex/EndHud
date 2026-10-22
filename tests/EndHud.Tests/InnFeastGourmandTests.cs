using EndHud.Vision.Inn;
using Xunit;
using static EndHud.Tests.InnTestKit;

namespace EndHud.Tests;

/// <summary>
/// The session's gourmands in the feast mode (every guest a gourmand of the data's feast pool), each case one that
/// test_yuanyuan8 (owner, 2026-10-21: "MOST of the gourmand requirement IS NOT recognized") showed going wrong, measured
/// against the recording's requests read by eye (<see cref="InnGourmandTruthTests"/>).
/// </summary>
public class InnFeastGourmandTests
{
    private const string A = "melon-meatball-soup", B = "fin-mato-soup", C = "mala-xiang-guo", D = "sweetbeans-on-snow", E = "meat-baozi";
    private static readonly string[] Menu = [A, B, C, D, E, "bottled-duskberry-juice", "pre-tide-lungjing", "bottled-milk-a"];

    private static InnSession Prepared()
    {
        var session = NewSession();
        session.Update(SetupScreen(0, 1, 2));
        session.Update(MenuScreen(1, Menu));
        return session;
    }

    private static Gourmand Feast(int number) => Inn.Gourmands.Single(g => g.Pool == "feast" && g.Number == number);

    /// <summary>A feast gourmand's bubble saying these requests, a line each (the icons seen, none resolved unless said).</summary>
    private static GourmandBubble Says(int number, int[] requests, bool[]? resolved = null)
    {
        string[] lines = [.. requests.Select(r => Feast(number).Requests[r].Text)];
        return new GourmandBubble(string.Concat(lines), lines)
        {
            Starts = [.. Enumerable.Range(0, lines.Length)],
            Resolved = resolved ?? [.. lines.Select(_ => false)],
        };
    }

    private static TableCard Gourmand(int table, TableState state, params bool[] pips) => new(table, state, [], pips.Length > 0 ? pips : [false, false]);

    private static IReadOnlyList<string> Shown(InnSession session, double at, int table) =>
        [.. session.Advice(S(at))!.Gourmands.SingleOrDefault(g => g.Table == table)?.Advice?.Requests.Select(r => r.Request.Text) ?? []];

    private static IReadOnlyList<string> Texts(int number, params int[] requests) => [.. requests.Select(r => Feast(number).Requests[r].Text)];

    /// <summary>
    /// A gourmand's words show the moment their order is taken (their card turns to the order). At 247.8 s table 4's gourmand
    /// (feast 12) ordered while table 6's was still choosing; the words went to table 6, the newest table "asking", and stayed
    /// there 49 s while table 4's tile never showed.
    /// </summary>
    [Fact]
    public void Words_go_to_the_gourmand_who_ordered_not_one_still_choosing()
    {
        var session = Prepared();
        session.Update(Session(10, tables: Tables(Gourmand(4, TableState.GourmandArrived))));
        session.Update(Session(12, tables: Tables(Gourmand(4, TableState.GourmandArrived), Gourmand(6, TableState.GourmandOrdering))));
        session.Update(Session(12.3, tables: Tables(Gourmand(4, TableState.GourmandArrived), Gourmand(6, TableState.GourmandOrdering))));
        // Table 4's order is taken: the card turns on this reading, its bubble shows the words.
        session.Update(Session(14, tables: Tables(Gourmand(4, TableState.GourmandOrder), Gourmand(6, TableState.GourmandOrdering)), bubble: Says(12, [0, 4])));
        session.Update(Session(14.3, tables: Tables(Gourmand(4, TableState.GourmandOrder), Gourmand(6, TableState.GourmandOrdering)), bubble: Says(12, [0, 4])));

        Assert.Equal(Texts(12, 0, 4), Shown(session, 14.3, 4));
        Assert.Empty(Shown(session, 14.3, 6));
    }

    /// <summary>
    /// The HUD fading while the camera moves reads every card empty, in test_yuanyuan8 for 2.1 to 3.9 s at a time: at 369.9 s
    /// a fade of 3.9 s forgot tables 1, 2 and 4's words, never read again (their tiles gone 34 to 62 s before they were served).
    /// </summary>
    [Fact]
    public void A_fade_longer_than_three_seconds_forgets_no_gourmand()
    {
        var session = Prepared();
        session.Update(Session(10, tables: Tables(Gourmand(1, TableState.GourmandArrived))));
        session.Update(Session(11, tables: Tables(Gourmand(1, TableState.GourmandOrder)), bubble: Says(5, [2, 4])));
        session.Update(Session(11.3, tables: Tables(Gourmand(1, TableState.GourmandOrder)), bubble: Says(5, [2, 4])));
        for (double t = 20; t <= 24; t += 0.3) session.Update(Session(t, tables: Tables()));
        session.Update(Session(24.3, tables: Tables(Gourmand(1, TableState.GourmandOrder))));
        session.Update(Session(24.6, tables: Tables(Gourmand(1, TableState.GourmandOrder))));

        Assert.Equal(Texts(5, 2, 4), Shown(session, 24.6, 1));
    }

    /// <summary>
    /// After a fade every order reads new at once; a gourmand ordering after it is still the newest, and an earlier one whose
    /// words were never read gets theirs when their bubble is seen later (test_yuanyuan8 405.3 s: table 5's words went to table 2).
    /// </summary>
    [Fact]
    public void After_a_fade_new_words_go_to_the_table_that_ordered_last()
    {
        var session = Prepared();
        // Table 2 ordered at 10 (its bubble not seen); table 5's gourmand arrives.
        session.Update(Session(9, tables: Tables(Gourmand(2, TableState.GourmandArrived))));
        session.Update(Session(10, tables: Tables(Gourmand(2, TableState.GourmandOrder), Gourmand(5, TableState.GourmandArrived))));
        session.Update(Session(10.3, tables: Tables(Gourmand(2, TableState.GourmandOrder), Gourmand(5, TableState.GourmandArrived))));
        for (double t = 20; t <= 22; t += 0.3) session.Update(Session(t, tables: Tables()));
        session.Update(Session(22.3, tables: Tables(Gourmand(2, TableState.GourmandOrder), Gourmand(5, TableState.GourmandArrived))));
        // Table 5's order taken, its words read.
        session.Update(Session(25, tables: Tables(Gourmand(2, TableState.GourmandOrder), Gourmand(5, TableState.GourmandOrder)), bubble: Says(4, [4, 0])));
        session.Update(Session(25.3, tables: Tables(Gourmand(2, TableState.GourmandOrder), Gourmand(5, TableState.GourmandOrder)), bubble: Says(4, [4, 0])));
        // Later table 2's bubble is seen.
        session.Update(Session(30, tables: Tables(Gourmand(2, TableState.GourmandOrder), Gourmand(5, TableState.GourmandOrder)), bubble: Says(10, [1, 3])));

        Assert.Equal(Texts(4, 4, 0), Shown(session, 30, 5));
        Assert.Equal(Texts(10, 1, 3), Shown(session, 30, 2));
    }

    /// <summary>Two bubbles on one frame (test_yuanyuan8 359.5 s, tables 4 and 6): each to its own table.</summary>
    [Fact]
    public void Two_bubbles_on_a_frame_each_go_to_their_own_table()
    {
        var session = Prepared();
        session.Update(Session(9, tables: Tables(Gourmand(6, TableState.GourmandArrived))));
        // Table 6 ordered at 10, its bubble not read until both show.
        session.Update(Session(10, tables: Tables(Gourmand(6, TableState.GourmandOrder))));
        session.Update(Session(19, tables: Tables(Gourmand(6, TableState.GourmandOrder), Gourmand(4, TableState.GourmandArrived))));
        var both = Session(20, tables: Tables(Gourmand(6, TableState.GourmandOrder), Gourmand(4, TableState.GourmandOrder)), bubble: Says(11, [2, 3]))
            with { Bubbles = [Says(11, [2, 3]), Says(14, [1, 3])] };
        session.Update(both);

        Assert.Equal(Texts(14, 1, 3), Shown(session, 20, 6));
        Assert.Equal(Texts(11, 2, 3), Shown(session, 20, 4));
    }

    /// <summary>
    /// Two bubbles side by side found as one patch (test_yuanyuan8 303 s, tables 3 and 5): one gourmand's words never go to the
    /// table holding the other's.
    /// </summary>
    [Fact]
    public void Two_gourmands_words_in_one_bubble_each_go_to_their_own_table()
    {
        var session = Prepared();
        session.Update(Session(9, tables: Tables(Gourmand(5, TableState.GourmandArrived), Gourmand(3, TableState.GourmandArrived))));
        session.Update(Session(10, tables: Tables(Gourmand(5, TableState.GourmandOrder), Gourmand(3, TableState.GourmandArrived)), bubble: Says(11, [0, 4])));
        session.Update(Session(20, tables: Tables(Gourmand(5, TableState.GourmandOrder), Gourmand(3, TableState.GourmandOrder)), bubble: Says(13, [2, 4])));
        string[] lines = [Feast(13).Requests[4].Text, Feast(11).Requests[0].Text, Feast(11).Requests[4].Text];
        session.Update(Session(30, tables: Tables(Gourmand(5, TableState.GourmandOrder), Gourmand(3, TableState.GourmandOrder)),
            bubble: new GourmandBubble(string.Concat(lines), lines)));

        Assert.Equal(Texts(11, 0, 4), Shown(session, 30, 5));
        Assert.Equal(Texts(13, 2, 4), Shown(session, 30, 3));
    }

    /// <summary>
    /// A request resolved (its pip on the card mint green, yellow-green or red: served, imperfect, failed) asks nothing more;
    /// the tile shows what's left, and goes when nothing is (test_yuanyuan8 192 s: table 1's tea served, its dish still due).
    /// Two readings in a row must say so (a pip is a few pixels).
    /// </summary>
    [Fact]
    public void A_request_resolved_on_its_pip_is_no_longer_asked()
    {
        var session = Prepared();
        session.Update(Session(9, tables: Tables(Gourmand(1, TableState.GourmandArrived))));
        session.Update(Session(10, tables: Tables(Gourmand(1, TableState.GourmandOrder)), bubble: Says(10, [1, 3])));
        session.Update(Session(15, tables: Tables(Gourmand(1, TableState.GourmandOrder, false, true))));
        Assert.Equal(Texts(10, 1, 3), Shown(session, 15, 1));

        session.Update(Session(15.3, tables: Tables(Gourmand(1, TableState.GourmandOrder, false, true))));
        Assert.Equal(Texts(10, 1), Shown(session, 15.3, 1));

        session.Update(Session(20, tables: Tables(Gourmand(1, TableState.GourmandOrder, true, true))));
        session.Update(Session(20.3, tables: Tables(Gourmand(1, TableState.GourmandOrder, true, true))));
        Assert.DoesNotContain(session.Advice(S(20.3))!.Gourmands, g => g.Table == 1);
    }

    /// <summary>The bubble's icon a face says the same (test_yuanyuan8 290 s: table 2's drink served, its icon a green face).</summary>
    [Fact]
    public void A_request_resolved_on_its_bubble_is_no_longer_asked()
    {
        var session = Prepared();
        session.Update(Session(9, tables: Tables(Gourmand(2, TableState.GourmandArrived))));
        session.Update(Session(10, tables: Tables(Gourmand(2, TableState.GourmandOrder)), bubble: Says(7, [0, 3])));
        session.Update(Session(12, tables: Tables(Gourmand(2, TableState.GourmandOrder)), bubble: Says(7, [0, 3], [false, true])));

        Assert.Equal(Texts(7, 0), Shown(session, 12, 2));
    }

    /// <summary>
    /// A gourmand's bubble read now and then with an OCR slip that names another of their requests (test_yuanyuan8 311.7 s:
    /// table 1's first line read as the fifth gourmand's first request, not their third): the words most readings agree on stand.
    /// </summary>
    [Fact]
    public void The_words_most_readings_agree_on_stand()
    {
        var session = Prepared();
        session.Update(Session(9, tables: Tables(Gourmand(1, TableState.GourmandArrived))));
        for (double t = 10; t < 12; t += 0.3)
            session.Update(Session(t, tables: Tables(Gourmand(1, TableState.GourmandOrder)), bubble: Says(5, [2, 4])));
        session.Update(Session(12, tables: Tables(Gourmand(1, TableState.GourmandOrder)), bubble: Says(5, [0])));

        Assert.Equal(Texts(5, 2, 4), Shown(session, 12, 1));
    }
}
