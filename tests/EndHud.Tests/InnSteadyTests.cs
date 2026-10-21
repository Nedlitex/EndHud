using EndHud.Vision.Inn;
using Xunit;
using static EndHud.Tests.InnTestKit;

namespace EndHud.Tests;

/// <summary>
/// The session's displays held through uncertain readings (owner, live 2026-10-20: "it is still flashing, the display of
/// both banner and the circle should just stay, not flash all the time ... also the suggestion keeps flashing too"),
/// one test per cause found by replaying the owner's recording (InnReplayTests): the dispatch panel read the other way
/// for a frame or a few, an order's card misread for one reading, an order or a dish to cook ahead dropping out of
/// one moment's advice, near-equal dishes swapping places for a gourmand, a dish's card not read on one frame.
/// </summary>
public class InnSteadyTests
{
    // Soups (40 s), a side (35 s), a dessert (25 s) and an entree (15 s).
    private const string A = "melon-meatball-soup", B = "fin-mato-soup", C = "mala-xiang-guo", D = "sweetbeans-on-snow", E = "meat-baozi";
    private static readonly string[] Dishes = [A, B, C, D, E];

    private static InnSession Prepared()
    {
        var session = NewSession();
        session.Update(SetupScreen(0, 1, 2));
        session.Update(MenuScreen(1, Dishes));
        return session;
    }

    private static DispatchPanel KitchenOne(params int[] unread) => new(1, 2, [null, null], [null, null], null,
        [.. Dishes.Select((id, i) => new KitchenDish(unread.Contains(i) ? null : id, Dish(id).Zh, Dish(id).CookSeconds) { Card = i })],
        [.. Enumerable.Repeat(new QueueSlot(false, null, null), KitchenQueue.Slots)]);

    // ---- The dispatch panel ----

    /// <summary>
    /// Read closed on a frame or a few while open (48 single frames and a dozen runs of 2–5 on the recording): the banners
    /// stay down until no frame has read the panel for <see cref="InnSession.PanelClosedAfter"/> (the marks don't: below).
    /// </summary>
    [Fact]
    public void A_panel_read_closed_for_a_moment_keeps_the_banners_down()
    {
        var session = Prepared();
        session.Update(Session(10, panel: KitchenOne()));
        session.Update(Session(10.3, panel: KitchenOne()));
        Assert.Empty(session.Advice(S(10.3))!.BannerCalls);

        session.Update(Session(10.6));
        session.Update(Session(11.4));
        var moment = session.Advice(S(11.4))!;
        Assert.Empty(moment.BannerCalls);
        Assert.Equal(1, moment.Panel?.Kitchen);

        session.Update(Session(10.3 + InnSession.PanelClosedAfter.TotalSeconds));
        var closed = session.Advice(S(10.3 + InnSession.PanelClosedAfter.TotalSeconds))!;
        Assert.NotEmpty(closed.BannerCalls);
        Assert.Null(closed.Panel);
    }

    /// <summary>
    /// The panel closed (owner, live 2026-10-21: "the recommendation leaks after the kitchen is closed", the card mark
    /// "queue this (table 1)" over the dining room): its marks go on the first frame that shows it closed for sure (the Full Blaze
    /// meter it covers read), through the overlay's own display, while the banners stay down for
    /// <see cref="InnSession.PanelClosedAfter"/> as before. A frame that merely misses the panel (its tabs misread while
    /// it's open, the meter still covered) keeps them: that is the 2026-10-20 flashing.
    /// </summary>
    [Fact]
    public void A_panel_seen_closed_takes_its_marks_away_at_once()
    {
        var session = Prepared();
        var display = new InnDisplay();
        display.Show(session, Session(10, panel: KitchenOne()), S(10));
        display.Show(session, Session(10.3, panel: KitchenOne()), S(10.3));
        Assert.NotEmpty(display.Views(session, S(10.3), callsEnabled: true, Text).Marks);

        // Missed for a frame, nothing it covers seen: still open as far as anyone can tell.
        display.Show(session, Session(10.6), S(10.6));
        Assert.NotEmpty(display.Views(session, S(10.6), callsEnabled: true, Text).Marks);

        // The meter it covers read: it's closed. No mark, at once; the banners still held down a moment.
        display.Show(session, Session(10.9, fire: new FireMeter(14, 6, Ready: false)), S(10.9));
        var closed = display.Views(session, S(10.9), callsEnabled: true, Text);
        Assert.Empty(closed.Marks);
        Assert.Empty(closed.Calls);
        // A tick later, and a frame missing the meter: still no mark.
        Assert.Empty(display.Views(session, S(11.1), callsEnabled: true, Text).Marks);
        display.Show(session, Session(11.2), S(11.2));
        Assert.Empty(display.Views(session, S(11.2), callsEnabled: true, Text).Marks);

        // Read open again: the marks are back with it.
        display.Show(session, Session(11.4, panel: KitchenOne()), S(11.4));
        Assert.NotEmpty(display.Views(session, S(11.4), callsEnabled: true, Text).Marks);
    }

    /// <summary>Read open on one frame only (26 times on the recording): the banners stay up.</summary>
    [Fact]
    public void A_panel_read_open_on_one_frame_leaves_the_banners_up()
    {
        var session = Prepared();
        session.Update(Session(10));
        var shown = session.Advice(S(10))!.BannerCalls;
        Assert.NotEmpty(shown);

        session.Update(Session(10.3, panel: KitchenOne()));
        Assert.Equal(shown, session.Advice(S(10.3))!.BannerCalls);
        Assert.Null(session.Advice(S(10.3))!.Panel);

        session.Update(Session(10.6));
        Assert.Equal(shown, session.Advice(S(10.6))!.BannerCalls);
    }

    /// <summary>
    /// The card of the dish called is marked even on a frame its name wasn't read: the card it was read on before in that
    /// kitchen (the menu doesn't change in a session), never a guess about a card not read before.
    /// </summary>
    [Fact]
    public void A_dishs_card_not_read_on_a_frame_keeps_its_mark()
    {
        var session = Prepared();
        session.Update(Session(10, panel: KitchenOne()));
        session.Update(Session(10.3, panel: KitchenOne()));
        var advice = session.Advice(S(10.3))!;
        var call = advice.Calls.Single(c => c.Kitchen == 1);
        int card = Array.IndexOf(Dishes, call.Dish!.Id);
        var marked = InnPanelMarks.Of(advice.Panel, advice.Calls, Text).Single(m => m.OnCard);
        Assert.Equal(DispatchPanelReader.CardArea(card), marked.Area);

        session.Update(Session(10.6, panel: KitchenOne(unread: card)));
        var missed = session.Advice(S(10.6))!;
        Assert.Equal(marked, InnPanelMarks.Of(missed.Panel, missed.Calls, Text).Single(m => m.OnCard));
    }

    // ---- Table cards ----

    /// <summary>
    /// An order with a dish not yet served whose card reads empty (or ordering) is misread: guests never leave, and a table
    /// empties only after eating and paying. The HUD fading reads every card empty, on the two recordings for up to 3.3 s
    /// (test_yuanyuan4 at 383.4 s: three tables at once, and every kitchen's call went and came back 1.5 s later). The order
    /// stays until every reading has said so for <see cref="InnSession.OrderGoneAfter"/>.
    /// </summary>
    [Fact]
    public void An_orders_card_read_empty_while_the_hud_fades_keeps_the_order()
    {
        var session = Prepared();
        session.Update(Session(10, tables: Tables(Order(3, A))));
        Assert.Contains(session.Advice(S(10))!.Calls, c => c is { Kind: InnCallKind.Queue, Table: 3 });

        session.Update(Session(10.3, tables: Tables()));
        Assert.Contains(Ordered(session.Advice(S(10.3))!), o => o == (3, A));
        session.Update(Session(10.6, tables: Tables(Order(3, A))));
        Assert.Contains(Ordered(session.Advice(S(10.6))!), o => o == (3, A));

        // 3.3 s of empty readings, as on the recordings: kept.
        for (double t = 10.9; t <= 14.2; t += 0.3) session.Update(Session(t, tables: Tables()));
        Assert.Contains(Ordered(session.Advice(S(14.2))!), o => o == (3, A));

        // Empty on every reading for OrderGoneAfter: gone.
        double gone = 10.9 + InnSession.OrderGoneAfter.TotalSeconds;
        for (double t = 14.5; t <= gone + 0.3; t += 0.3) session.Update(Session(t, tables: Tables()));
        Assert.DoesNotContain(Ordered(session.Advice(S(gone + 0.3))!), o => o == (3, A));
    }

    /// <summary>An order read eating (every dish served, the eating card) is done at the second reading, as before: no wait.</summary>
    [Fact]
    public void An_order_read_eating_is_done_without_waiting()
    {
        var session = Prepared();
        session.Update(Session(10, tables: Tables(Order(3, A))));
        session.Update(Session(10.3, tables: Tables(new TableCard(3, TableState.Eating, [], []))));
        session.Update(Session(10.6, tables: Tables(new TableCard(3, TableState.Eating, [], []))));
        Assert.DoesNotContain(Ordered(session.Advice(S(10.6))!), o => o == (3, A));
    }

    /// <summary>
    /// The dispatch panel's tabs read short for a frame (test_yuanyuan4 at 168.6 s and 179.1–179.7 s: kitchens "1 of 1",
    /// "1 of 2"): the mark on a tab of a kitchen with a dish to queue stays, the tabs being as many as ever read this session.
    /// </summary>
    [Fact]
    public void A_tab_count_read_short_keeps_the_tab_marks()
    {
        var session = Prepared();
        var two = KitchenOne() with { Kitchens = 2 };
        session.Update(Session(10, panel: two));
        session.Update(Session(10.3, panel: two));
        Assert.Contains(InnPanelMarks.Of(session.Advice(S(10.3))!.Panel, session.Advice(S(10.3))!.Calls, Text), m => !m.OnCard);

        session.Update(Session(10.6, panel: two with { Kitchens = 1 }));
        var advice = session.Advice(S(10.6))!;
        Assert.Contains(InnPanelMarks.Of(advice.Panel, advice.Calls, Text), m => !m.OnCard);
    }

    /// <summary>
    /// A panel read with fewer tabs than this session has read is uncertain and taken as a frame the panel was missed on.
    /// On test_yuanyuan3's readings (taken before the panel reader learned to tell the dark bar behind the theme line from
    /// the panel) such a "kitchen 1 of 1" with an empty queue showed for up to four frames over the dining room, each time
    /// taking kitchen 1's dishes for out on the counter and taking the banners down; on test_yuanyuan4 the real panel read
    /// "1 of 1" for a frame.
    /// </summary>
    [Fact]
    public void A_panel_read_with_fewer_tabs_than_before_changes_nothing()
    {
        var session = Prepared();
        var menu = Dishes.ToList();
        var queued = Panel(1, 2, menu, (A, 30), (B, null));
        session.Update(Session(10, panel: queued));
        session.Update(Session(10.3, panel: queued));
        for (double t = 11; t <= 20; t++) session.Update(Session(t));
        var banners = session.Advice(S(20))!.BannerCalls;
        Assert.NotEmpty(banners);

        foreach (double t in new[] { 20.3, 20.6, 20.9, 21.2 }) session.Update(Session(t, panel: Panel(1, 1, menu)));
        var advice = session.Advice(S(21.2))!;
        Assert.Contains($"1:{A},{B}", session.QueueSignature());
        Assert.Equal("", session.CounterSignature());
        Assert.Equal(banners, advice.BannerCalls);
        Assert.Null(advice.Panel);
    }

    /// <summary>
    /// The seconds left on the dish cooking not read on a frame (test_yuanyuan4 at 179.1 s and 180.6 s) leave its estimate as it
    /// was; until now it was taken to have its whole cook time left, 30 s more for that frame.
    /// </summary>
    [Fact]
    public void The_cooking_dishs_time_not_read_keeps_its_estimate()
    {
        var session = Prepared();
        DispatchPanel Queue(int? left, int kitchen = 1, string dish = A) =>
            KitchenOne() with { Kitchen = kitchen, Queue = [new QueueSlot(true, dish, left), new QueueSlot(false, null, null), new QueueSlot(false, null, null)] };
        // Kitchen 2 busy for 30 s more (10 s into its dish: too far in to swap it out).
        session.Update(Session(9.4, panel: Queue(30, kitchen: 2, dish: B)));
        session.Update(Session(9.7, panel: Queue(30, kitchen: 2, dish: B)));
        session.Update(Session(10, panel: Queue(9)));
        session.Update(Session(10.3, panel: Queue(9)));
        session.Update(Session(10.6, panel: Queue(null)));
        session.Update(Session(10.9, tables: Tables(Order(3, E))));
        session.Update(Session(11.2, tables: Tables(Order(3, E))));

        // Kitchen 1 has its soup out in about 8 s: the order's entree (15 s) is out there in about 23 s, not in kitchen 2 in about 44.
        var order = session.Advice(S(11.2))!.Dispatch!.Kitchens.SelectMany(k => k.Queue).Single(q => q.Table == 3);
        Assert.True(order.ReadyIn <= 25, $"ready in {order.ReadyIn} s");
    }

    private static IEnumerable<(int?, string)> Ordered(InnAdvice advice) =>
        advice.Dispatch!.Kitchens.SelectMany(k => k.Queue).Select(q => (q.Table, q.Dish.Id));

    // ---- The calls ----

    private static DispatchAdvice Advice(params KitchenAdvice[] kitchens) => new(kitchens, [], [], []);

    private static KitchenAdvice Kitchen(int number, params QueueAdvice[] queue) => new(number, queue, []);

    /// <summary>A kitchen view with every kitchen free, nothing queued unless said.</summary>
    private static InnKitchenView View(Func<int, string, int>? queued = null) =>
        new(queued ?? ((_, _) => 0), _ => true, (_, dish) => dish.CookSeconds);

    /// <summary>
    /// An order missing from one moment's advice (a reading that dropped it) keeps its banner for
    /// <see cref="InnCallHold.GraceSeconds"/>; still missing after that, it's taken down; queued, at once.
    /// </summary>
    [Fact]
    public void A_call_whose_order_drops_out_of_the_advice_for_a_moment_stays_up()
    {
        var hold = new InnCallHold();
        var order = Advice(Kitchen(1, new QueueAdvice(Dish(A), 3, 40)));
        var shown = Assert.Single(hold.Update(order, null, View(), S(0)));

        Assert.Equal([shown], hold.Update(Advice(Kitchen(1)), null, View(), S(0.5)));
        Assert.Equal([shown], hold.Update(order, null, View(), S(0.8)));

        hold.Update(Advice(Kitchen(1)), null, View(), S(1));
        Assert.Equal([shown], hold.Update(Advice(Kitchen(1)), null, View(), S(2)));
        Assert.Empty(hold.Update(Advice(Kitchen(1)), null, View(), S(1) + InnCallHold.GraceSeconds));

        // Done (its dish went into the kitchen's queue): down at once, no waiting.
        var again = new InnCallHold();
        again.Update(order, null, View(), S(0));
        Assert.Empty(again.Update(Advice(Kitchen(1)), null, View((k, id) => k == 1 && id == A ? 1 : 0), S(0.3)));
    }

    /// <summary>
    /// A dish to cook ahead shown in a kitchen is handed to the advice to keep (<see cref="InnCallHold.Kept"/>; the advice
    /// keeps it there while it can, so near-equal dishes no longer swap places), and stands for that kitchen only: the
    /// advice cooking the same dish ahead in another kitchen doesn't hold it up. On test_yuanyuan4 at 167.7 s kitchen 2's
    /// soup, no longer to be cooked ahead there, claimed the soup the advice had put in kitchen 3, whose call went instead.
    /// </summary>
    [Fact]
    public void A_dish_to_cook_ahead_stands_for_its_own_kitchen_only()
    {
        var hold = new InnCallHold();
        var both = Advice(Kitchen(1, new QueueAdvice(Dish(A), null, 40)), Kitchen(2, new QueueAdvice(Dish(A), null, 40)));
        var shown = hold.Update(both, null, View(), S(0));
        Assert.Equal([(1, A), (2, A)], shown.Select(c => (c.Kitchen!.Value, c.Dish!.Id)));
        Assert.Equal([(1, A), (2, A)], hold.Kept(View()).Select(k => (k.Kitchen, k.Dish.Id)));

        // Kitchen 1 no longer open to cooking ahead; the advice keeps kitchen 2's.
        var onlyTwo = Advice(Kitchen(1), Kitchen(2, new QueueAdvice(Dish(A), null, 40)));
        hold.Update(onlyTwo, null, View(), S(1));
        var after = hold.Update(onlyTwo, null, View(), S(1) + InnCallHold.GraceSeconds);
        Assert.Equal([(2, A)], after.Select(c => (c.Kitchen!.Value, c.Dish!.Id)));
    }

    /// <summary>
    /// An order shown in a kitchen that the advice now sends to another (it keeps a shown order in place unless another
    /// kitchen has it out clearly sooner) moves there at once: on test_yuanyuan3 at 364.2 s the player queued another
    /// table's dish elsewhere and kitchen 2 came free, and kitchen 1's banner stayed on for 15 s while the advice said
    /// kitchen 2, until an unrelated order at 379.2 s switched both at once.
    /// </summary>
    [Fact]
    public void An_order_the_advice_moves_to_another_kitchen_moves_at_once()
    {
        var hold = new InnCallHold();
        var shown = Assert.Single(hold.Update(Advice(Kitchen(1, new QueueAdvice(Dish(A), 5, 45)), Kitchen(2)), null, View(), S(0)));
        Assert.Equal(1, shown.Kitchen);

        var moved = hold.Update(Advice(Kitchen(1), Kitchen(2, new QueueAdvice(Dish(A), 5, 32))), null, View(), S(3));
        Assert.Equal([(2, A, 5)], moved.Select(c => (c.Kitchen!.Value, c.Dish!.Id, c.Table!.Value)));
    }

    /// <summary>An order is called at once in a kitchen whose call is only kept a moment: real news doesn't wait.</summary>
    [Fact]
    public void An_order_takes_a_kitchen_whose_call_is_only_kept_a_moment()
    {
        var hold = new InnCallHold();
        hold.Update(Advice(Kitchen(1, new QueueAdvice(Dish(A), 3, 40))), null, View(), S(0));

        var calls = hold.Update(Advice(Kitchen(1, new QueueAdvice(Dish(E), 5, 15))), null, View(), S(0.3));

        Assert.Equal((InnCallKind.Queue, 5, E), (Assert.Single(calls).Kind, calls[0].Table, calls[0].Dish!.Id));
    }

    // ---- The gourmands' banners ----

    private static InnAdvice Gourmand(params (string Id, double ReadyIn)[] options) => Gourmand(1, options);

    private static InnAdvice Gourmand(int requests, params (string Id, double ReadyIn)[] options) =>
        new(new DispatchAdvice([], [], [], []), null, null, false,
            [new InnGourmandNote(4, new GourmandAdvice([.. Enumerable.Range(0, requests).Select(r => new GourmandRequestAdvice(new GourmandRequest("said" + r, "x"), "plain" + r,
                [.. options.Select(o => new GourmandOption(Dish(o.Id), null, o.ReadyIn == 0 ? GourmandSource.Counter : GourmandSource.ToCook, o.ReadyIn == 0 ? null : 1, o.ReadyIn))]))], null), null, false)],
            [], new HashSet<int>(), false);

    /// <summary>
    /// The dish shown for a gourmand stays when another that meets the request moves a little ahead (the queues moving
    /// on), and changes when another is clearly sooner (<see cref="InnWantsHold.ClearlySoonerSeconds"/>: one on the counter
    /// while the one shown is still to cook) or the one shown no longer meets it on the menu.
    /// </summary>
    [Fact]
    public void A_gourmands_row_stays_when_a_near_equal_dish_moves_ahead()
    {
        var hold = new InnWantsHold();
        Assert.Empty(hold.Update(Gourmand((A, 30), (B, 31)), Text, S(0)));
        Assert.Equal(A, Assert.Single(hold.Update(Gourmand((A, 30), (B, 31)), Text, S(0.6))).ItemId);

        Assert.Equal(A, Assert.Single(hold.Update(Gourmand((B, 28), (A, 30)), Text, S(1))).ItemId);

        Assert.Equal(B, Assert.Single(hold.Update(Gourmand((B, 0), (A, 30)), Text, S(1.5))).ItemId);

        Assert.Equal(C, Assert.Single(hold.Update(Gourmand((C, 25)), Text, S(2))).ItemId);
    }

    /// <summary>
    /// A gourmand's banner first goes up once their rows have read the same for <see cref="InnWantsHold.ShowAfter"/>
    /// (measured on the 2026-10-20 recording: a half-faded bubble read with one request, both 0.3 s later, put a banner up
    /// and took it down within 0.3 s): it goes up once, with both requests. Once up, a change shows at once, in place.
    /// </summary>
    [Fact]
    public void A_gourmands_banner_goes_up_once_what_it_says_has_stood_a_moment()
    {
        var hold = new InnWantsHold();
        Assert.Empty(hold.Update(Gourmand(1, (A, 30)), Text, S(0)));
        Assert.Empty(hold.Update(Gourmand(2, (A, 30)), Text, S(0.3)));
        Assert.Empty(hold.Update(Gourmand(2, (A, 30)), Text, S(0.6)));
        Assert.Equal(2, hold.Update(Gourmand(2, (A, 30)), Text, S(0.9)).Count);

        Assert.Equal(B, hold.Update(Gourmand(2, (B, 0)), Text, S(1.2))[0].ItemId);
    }
}
