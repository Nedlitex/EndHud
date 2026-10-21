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
    /// "排这个（桌1）" over the dining room): its marks go on the first frame that shows it closed for sure (the Full Blaze
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
    /// An order's card read empty for one reading (the HUD fading; 16 times on the recording) is taken as misread: the
    /// order stays in the advice; read so twice in a row, it's gone.
    /// </summary>
    [Fact]
    public void An_orders_card_read_empty_for_one_reading_keeps_the_order()
    {
        var session = Prepared();
        session.Update(Session(10, tables: Tables(Order(3, A))));
        Assert.Contains(session.Advice(S(10))!.Calls, c => c is { Kind: InnCallKind.Queue, Table: 3 });

        session.Update(Session(10.3, tables: Tables()));
        Assert.Contains(Ordered(session.Advice(S(10.3))!), o => o == (3, A));

        session.Update(Session(10.6, tables: Tables(Order(3, A))));
        Assert.Contains(Ordered(session.Advice(S(10.6))!), o => o == (3, A));

        session.Update(Session(10.9, tables: Tables()));
        session.Update(Session(11.2, tables: Tables()));
        Assert.DoesNotContain(Ordered(session.Advice(S(11.2))!), o => o == (3, A));
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
    /// A dish to cook ahead stays while its kitchen is still to cook something ahead (on the recording, near-equal
    /// dishes swapped places every few seconds as the queues moved on); once the kitchen is to cook nothing ahead, it
    /// goes after the grace.
    /// </summary>
    [Fact]
    public void A_dish_to_cook_ahead_stays_while_its_kitchen_still_cooks_ahead()
    {
        var hold = new InnCallHold();
        var shown = Assert.Single(hold.Update(Advice(Kitchen(1, new QueueAdvice(Dish(A), null, 40))), null, View(), S(0)));
        Assert.Equal(InnCallKind.CookAhead, shown.Kind);

        var other = Advice(Kitchen(1, new QueueAdvice(Dish(B), null, 40)));
        Assert.Equal([shown], hold.Update(other, null, View(), S(5)));
        Assert.Equal([shown], hold.Update(other, null, View(), S(10)));

        hold.Update(Advice(Kitchen(1)), null, View(), S(11));
        Assert.Empty(hold.Update(Advice(Kitchen(1)), null, View(), S(11) + InnCallHold.GraceSeconds));
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

    // ---- The gourmands' circles ----

    private static InnAdvice Gourmand(params (string Id, double ReadyIn)[] options) =>
        new(new DispatchAdvice([], [], [], []), null, null, false,
            [new InnGourmandNote(4, new GourmandAdvice([new GourmandRequestAdvice(new GourmandRequest("said", "x"), "plain",
                [.. options.Select(o => new GourmandOption(Dish(o.Id), null, o.ReadyIn == 0 ? GourmandSource.Counter : GourmandSource.ToCook, o.ReadyIn == 0 ? null : 1, o.ReadyIn))])], null), null, false)],
            [], new HashSet<int>(), false);

    /// <summary>
    /// The dish shown for a gourmand stays when another that meets the request moves a little ahead (the queues moving
    /// on), and changes when another is clearly sooner (<see cref="InnWantsHold.ClearlySoonerSeconds"/>: one on the counter
    /// while the one shown is still to cook) or the one shown no longer meets it on the menu.
    /// </summary>
    [Fact]
    public void A_gourmands_circle_stays_when_a_near_equal_dish_moves_ahead()
    {
        var hold = new InnWantsHold();
        Assert.Equal(A, Assert.Single(hold.Update(Gourmand((A, 30), (B, 31)), Text)).ItemId);

        Assert.Equal(A, Assert.Single(hold.Update(Gourmand((B, 28), (A, 30)), Text)).ItemId);

        Assert.Equal(B, Assert.Single(hold.Update(Gourmand((B, 0), (A, 30)), Text)).ItemId);

        Assert.Equal(C, Assert.Single(hold.Update(Gourmand((C, 25)), Text)).ItemId);
    }
}
