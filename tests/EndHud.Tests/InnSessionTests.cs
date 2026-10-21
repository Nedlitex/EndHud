using EndHud.Vision.Inn;
using Xunit;
using static EndHud.Tests.InnTestKit;

namespace EndHud.Tests;

/// <summary>
/// What the inn helper remembers (<see cref="InnSession"/>): when a session starts and ends, where
/// the kitchens and the menu come from, how a kitchen's queue is read and then estimated, and how
/// orders, Full Blaze and gourmands turn into advice.
/// </summary>
public class InnSessionTests
{
    // Soups (40 s), a side (35 s), a dessert (25 s) and an entree (15 s); and three drinks.
    private const string A = "melon-meatball-soup", B = "fin-mato-soup", C = "mala-xiang-guo", D = "sweetbeans-on-snow", E = "meat-baozi";
    private const string Juice = "bottled-duskberry-juice", Tea = "pre-tide-lungjing", Milk = "bottled-milk-a";
    private static readonly string[] Menu = [A, B, C, D, E, Juice, Tea, Milk];

    /// <summary>A session after the setup (these kitchens staffed) and menu screens.</summary>
    private static InnSession Prepared(params int[] kitchens)
    {
        var session = NewSession();
        session.Update(SetupScreen(0, kitchens.Length == 0 ? [1, 2, 3] : kitchens));
        session.Update(MenuScreen(1, Menu));
        return session;
    }

    private static IEnumerable<QueueAdvice> Queued(InnAdvice advice) => advice.Dispatch!.Kitchens.SelectMany(k => k.Queue);

    [Fact]
    public void A_session_starts_on_its_first_frame_and_ends_when_none_has_been_seen_for_15_seconds()
    {
        var session = Prepared();
        Assert.False(session.Active);
        Assert.Null(session.Advice(S(2)));

        session.Update(Session(10));
        Assert.True(session.Active);
        Assert.NotNull(session.Advice(S(20)));

        session.Tick(S(26));
        Assert.False(session.Active);
        Assert.Null(session.Advice(S(26)));
    }

    /// <summary>
    /// A session goes on through an alt-tab (owner 2026-10-20: the reading turns off whenever another
    /// window is in front): time with the reading off doesn't end it, nor forget the setup.
    /// </summary>
    [Fact]
    public void A_session_survives_time_the_game_was_in_the_background()
    {
        var clock = new ReadingClock();
        var session = NewSession(readingTime: clock.At);
        clock.Frame(S(0));
        session.Update(SetupScreen(0, 1, 2, 3));
        clock.Frame(S(1));
        session.Update(MenuScreen(1, Menu));
        for (double t = 1.25; t <= 10; t += 0.25) clock.Frame(S(t));
        session.Update(Session(10, tables: Tables(Order(3, A))));
        clock.Stop(S(10.2));

        session.Tick(S(40));
        Assert.True(session.Active);

        clock.Frame(S(60));
        session.Update(Session(60, left: 250, tables: Tables(Order(3, A))));
        var advice = session.Advice(S(60))!;
        Assert.Null(advice.Gap);
        Assert.False(advice.Opening);
    }

    /// <summary>With frames read all along, a session not seen for 15 s still ends.</summary>
    [Fact]
    public void Time_spent_reading_other_screens_still_ends_the_session()
    {
        var clock = new ReadingClock();
        var session = NewSession(readingTime: clock.At);
        clock.Frame(S(0));
        session.Update(Session(0));
        for (double t = 0.25; t <= 16; t += 0.25) clock.Frame(S(t));

        session.Tick(S(16));

        Assert.False(session.Active);
    }

    [Theory]
    [InlineData(InnScreenKind.Setup)]
    [InlineData(InnScreenKind.Menu)]
    [InlineData(InnScreenKind.ChefPicker)]
    [InlineData(InnScreenKind.ThemeList)]
    public void A_setup_or_menu_screen_ends_the_session(InnScreenKind screen)
    {
        var session = Prepared();
        session.Update(Session(10));

        session.Update(new InnReading(screen, S(11)));

        Assert.False(session.Active);
    }

    [Fact]
    public void The_timer_going_up_is_a_new_session_and_nothing_of_the_last_one_carries_over()
    {
        var session = Prepared();
        session.Update(Session(10, left: 20, tables: Tables(Order(3, A))));
        Assert.False(session.Advice(S(10))!.Opening);

        session.Update(Session(30, left: 299));

        var advice = session.Advice(S(30))!;
        Assert.True(advice.Opening);
        Assert.DoesNotContain(Queued(advice), q => q.Table is not null);
    }

    [Fact]
    public void Kitchens_come_from_the_setup_screen_and_only_the_staffed_ones_are_in_play()
    {
        var session = Prepared(1, 2);
        session.Update(Session(10));

        Assert.Equal([1, 2], session.Advice(S(10))!.Dispatch!.Kitchens.Select(k => k.Kitchen));
    }

    [Fact]
    public void Without_the_setup_and_menu_screens_the_dispatch_panel_gives_the_kitchens_and_the_menu()
    {
        var session = NewSession();
        session.Update(Session(0));
        Assert.Equal(InnGap.NoKitchens, session.Advice(S(0))!.Gap);

        session.Update(Session(1, panel: Panel(2, kitchens: 3, menu: [A, B, C])));

        var advice = session.Advice(S(1))!;
        Assert.Null(advice.Gap);
        Assert.Equal([1, 2, 3], advice.Dispatch!.Kitchens.Select(k => k.Kitchen));
        Assert.All(Queued(advice), q => Assert.Contains(q.Dish.Id, new[] { A, B, C }));
    }

    [Fact]
    public void Kitchens_without_a_menu_say_the_menu_is_missing_instead_of_guessing_one()
    {
        var session = NewSession();
        session.Update(SetupScreen(0, 1, 2));
        session.Update(Session(1));

        Assert.Equal(InnGap.NoMenu, session.Advice(S(1))!.Gap);
    }

    [Fact]
    public void The_menu_is_forgotten_when_the_inn_was_left_in_between()
    {
        var session = Prepared();
        session.Update(Session(10));
        Assert.Null(session.Advice(S(10))!.Gap);

        session.Update(SetupScreen(400, 1, 2, 3)); // back in the inn long after: the menu read before is gone (see InnSetupMemoryTests)
        session.Update(Session(410));

        Assert.Equal(InnGap.NoMenu, session.Advice(S(410))!.Gap);
    }

    [Fact]
    public void At_the_sessions_start_every_kitchen_is_filled_with_dishes_to_cook_ahead_before_any_order()
    {
        var session = Prepared();
        session.Update(Session(10, left: 300));

        var advice = session.Advice(S(10))!;

        Assert.True(advice.Opening);
        Assert.All(advice.Dispatch!.Kitchens, k => Assert.NotEmpty(k.Queue));
        Assert.All(Queued(advice), q => Assert.True(q.CookAhead));
        Assert.InRange(Queued(advice).Count(), 3, InnCounter.Capacity);
    }

    [Fact]
    public void An_order_goes_to_the_kitchen_that_has_it_out_soonest_and_idle_kitchens_cook_ahead()
    {
        var session = Prepared(1, 2);
        session.Update(Session(10, tables: Tables(Order(3, A, Juice))));

        var advice = session.Advice(S(10))!;

        var first = advice.Dispatch!.For(1).Queue[0];
        Assert.Equal((A, 3), (first.Dish.Id, first.Table!.Value));
        Assert.NotEmpty(advice.Dispatch.For(2).Queue);
        Assert.All(advice.Dispatch.For(2).Queue, q => Assert.True(q.CookAhead));
        Assert.DoesNotContain(Queued(advice), q => q.Dish.Id == Juice); // drinks need no kitchen
    }

    [Fact]
    public void A_queue_read_off_the_panel_is_exact_then_moved_on_by_cook_times_its_dish_coming_out_onto_the_counter()
    {
        var session = Prepared(1, 2);
        // The panel read on two frames in a row: open (InnSession.PanelFramesToOpen).
        session.Update(Session(9.7, tables: Tables(Order(5, E)), panel: Panel(1, 2, Menu[..5], (A, 10))));
        session.Update(Session(10, tables: Tables(Order(5, E)), panel: Panel(1, 2, Menu[..5], (A, 10))));
        Assert.Equal([1], session.Advice(S(10))!.ExactKitchens);

        // 5 s on, A is still cooking: a new order of A is covered by it, nothing more is sent.
        session.Update(Session(15, tables: Tables(Order(5, E), Order(2, A))));
        var cooking = session.Advice(S(15))!;
        Assert.Empty(cooking.ExactKitchens);
        Assert.DoesNotContain(Queued(cooking), q => q.Dish.Id == A && q.Table == 2);

        // 12 s on it's out, on the counter: still covered, and kitchen 1's queue is free again.
        session.Update(Session(22, tables: Tables(Order(5, E), Order(2, A))));
        var cooked = session.Advice(S(22))!;
        Assert.DoesNotContain(Queued(cooked), q => q.Dish.Id == A && q.Table == 2);
    }

    [Fact]
    public void A_dish_turning_served_on_a_card_is_out_of_the_kitchen_even_before_the_estimate_says_so()
    {
        var session = Prepared(1);
        session.Update(Session(10, tables: Tables(Order(2, A)), panel: Panel(1, 1, Menu[..5], (A, 30))));

        // The card says A is served (its pip green, or the auto-serve spinner on it) long before the
        // estimate's 30 s: the kitchen's A is gone, so a second order of A is not covered by it.
        session.Update(Session(12, tables: Tables(Order(2, new OrderItem(A, Served: true)), Order(4, A))));

        var advice = session.Advice(S(12))!;
        Assert.Contains(advice.Dispatch!.For(1).Queue, q => q.Dish.Id == A && q.Table == 4);
    }

    /// <summary>
    /// The owner's 养身心 setup: kitchens 1 and 2 staffed, kitchen 3's places both empty. Kitchen 3
    /// cannot cook (owner, 2026-10-19): even with its dispatch tab opened (naming nobody), it gets no
    /// line, no dish to cook ahead and no order.
    /// </summary>
    [Fact]
    public void A_kitchen_the_setup_screen_showed_without_a_chef_is_not_a_kitchen_to_queue_in()
    {
        var session = NewSession();
        session.Update(new InnReading(InnScreenKind.Setup, S(0), Setup: new InnSetup("theme", null, null, [], [],
        [
            new SetupKitchen(1, 2, [null, null], ["chef", "chef"]) { ChefsPlaced = 2 },
            new SetupKitchen(2, 2, [null, null], ["chef", null]) { ChefsPlaced = 1 },
            new SetupKitchen(3, 2, [null, null], [null, null]) { ChefsPlaced = 0 },
        ])));
        session.Update(MenuScreen(1, Menu));
        session.Update(Session(10, tables: Tables(Order(1, A), Order(2, B), Order(3, C), Order(4, D)), panel: Panel(3, 3, Menu[..5])));

        var advice = session.Advice(S(10))!;

        Assert.Equal([1, 2], advice.Dispatch!.Kitchens.Select(k => k.Kitchen));
    }

    /// <summary>A kitchen whose chefs weren't seen (no setup screen; the panel names nobody) is still taken to cook.</summary>
    [Fact]
    public void A_kitchen_whose_chefs_were_never_seen_is_still_a_kitchen_to_queue_in()
    {
        var session = NewSession();
        session.Update(MenuScreen(1, Menu));
        session.Update(Session(10, tables: Tables(Order(1, A)), panel: Panel(1, 3, Menu[..5])));

        var advice = session.Advice(S(10))!;

        Assert.Equal([1, 2, 3], advice.Dispatch!.Kitchens.Select(k => k.Kitchen));
        Assert.Contains(Queued(advice), q => q.Table == 1 && q.Dish.Id == A);
    }

    [Fact]
    public void A_served_item_is_neither_cooked_nor_dispatched_while_the_rest_of_its_order_is()
    {
        var session = Prepared(1, 2);
        // Table 6: A carries the blue auto-serve spinner (served), B is still to come.
        session.Update(Session(10, tables: Tables(Order(6, new OrderItem(A, Served: true), new OrderItem(B, Served: false)))));

        var advice = session.Advice(S(10))!;

        Assert.DoesNotContain(Queued(advice), q => q.Table == 6 && q.Dish.Id == A);
        Assert.Contains(Queued(advice), q => q.Table == 6 && q.Dish.Id == B);
    }

    [Fact]
    public void An_order_with_a_picture_not_told_apart_is_left_out_and_its_table_said_to_be_unreadable()
    {
        var session = Prepared(1, 2);
        session.Update(Session(10, tables: Tables(Order(3, A, null), Order(4, B))));

        var advice = session.Advice(S(10))!;

        Assert.Equal([3], advice.UnreadableTables);
        Assert.DoesNotContain(Queued(advice), q => q.Table == 3);
        Assert.Contains(Queued(advice), q => q.Table == 4 && q.Dish.Id == B);
    }

    /// <summary>
    /// An order's card that can't be read keeps its order until a reading says otherwise (owner, 2026-10-21: a suggestion
    /// stays until the order is served or gone). Until then it was dropped after 3 s and called again when the card was
    /// read: on test_yuanyuan3 table 2's "勺里游" went at 268.5 s and came back 1.5 s later.
    /// </summary>
    [Fact]
    public void A_card_that_cant_be_read_keeps_its_order_until_read_otherwise()
    {
        var session = Prepared(1, 2);
        session.Update(Session(10, tables: Tables(Order(3, A))));
        var unread = TablesAsRead(Empty(1), Empty(2), null, Empty(4), Empty(5), Empty(6));

        foreach (double t in new[] { 12.0, 14, 20, 40 })
        {
            session.Update(Session(t, tables: unread));
            var kept = session.Advice(S(t))!;
            Assert.Contains(Queued(kept), q => q.Table == 3 && q.Dish.Id == A);
            Assert.Empty(kept.UnreadableTables);
        }

        // Read as served: done.
        session.Update(Session(40.3, tables: Tables(Order(3, new OrderItem(A, Served: true)))));
        Assert.DoesNotContain(Queued(session.Advice(S(40.3))!), q => q.Table == 3);
    }

    /// <summary>
    /// The dispatch panel putting the estimate right moves dishes on and off the counter too: a dish the estimate still had
    /// cooking that the panel no longer shows came out (it's on the counter, for the next order of it); a dish the estimate
    /// had cooked out that the panel still shows isn't on the counter. Before, only the kitchen's count changed, and an
    /// order was sent to a kitchen for a dish already out (test_yuanyuan3, 176.4 s) or taken as covered by one still cooking.
    /// </summary>
    [Fact]
    public void The_panel_putting_a_queue_right_moves_dishes_on_and_off_the_counter()
    {
        // Out early: the estimate has A cooking 28 s more, the panel shows the queue empty. Table 3 then orders A: it's on the counter.
        var early = Prepared(1, 2);
        early.Update(Session(10, panel: Panel(1, 2, Menu.Take(5).ToList(), (A, 30))));
        early.Update(Session(10.3, panel: Panel(1, 2, Menu.Take(5).ToList(), (A, 30))));
        early.Update(Session(12, panel: Panel(1, 2, Menu.Take(5).ToList())));
        early.Update(Session(12.3, tables: Tables(Order(3, A))));
        early.Update(Session(12.6, tables: Tables(Order(3, A))));
        Assert.DoesNotContain(Queued(early.Advice(S(12.6))!), q => q.Table == 3);

        // Still cooking: the estimate cooked A out (its time read 2 s), the panel shows it 20 s from done. Table 3's A is
        // still to come from that kitchen, nothing on the counter covers a second order of A.
        var late = Prepared(1, 2);
        late.Update(Session(10, panel: Panel(1, 2, Menu.Take(5).ToList(), (A, 2))));
        late.Update(Session(10.3, panel: Panel(1, 2, Menu.Take(5).ToList(), (A, 2))));
        late.Update(Session(14, tables: Tables(Order(3, A))));
        late.Update(Session(14.3, panel: Panel(1, 2, Menu.Take(5).ToList(), (A, 20))));
        late.Update(Session(14.6, tables: Tables(Order(3, A), Order(4, A))));
        late.Update(Session(14.9, tables: Tables(Order(3, A), Order(4, A))));
        var advice = late.Advice(S(14.9))!;
        Assert.Single(Queued(advice), q => q.Table is 3 or 4 && q.Dish.Id == A);
    }

    /// <summary>
    /// Frames the session's screen was missed on (held by the reading loop, owner 2026-10-19: the
    /// strip flashed) keep the session and the last reading: the orders stay in the advice, no
    /// card turns unreadable for frames that read nothing.
    /// </summary>
    [Fact]
    public void Held_frames_keep_the_session_and_the_last_reading()
    {
        var session = Prepared(1, 2);
        session.Update(Session(10, tables: Tables(Order(3, A))));

        foreach (double t in new[] { 11, 13, 15, 17 }) session.Update(new InnReading(InnScreenKind.Session, S(t), Fresh: false));
        var held = session.Advice(S(17))!;

        Assert.True(session.Active);
        Assert.Contains(Queued(held), q => q.Table == 3 && q.Dish.Id == A);
        Assert.Empty(held.UnreadableTables);
    }

    [Fact]
    public void Fewer_tables_and_one_kitchen_work_the_same()
    {
        var session = Prepared(1);
        session.Update(Session(10, tables: TablesAsRead(Order(1, A, Tea), Order(2, C))));

        var advice = session.Advice(S(10))!;

        var kitchen = Assert.Single(advice.Dispatch!.Kitchens);
        Assert.Equal([(A, 1), (C, 2)], kitchen.Queue.Select(q => (q.Dish.Id, q.Table!.Value)));
    }

    [Fact]
    public void Full_blaze_is_called_when_full_readied_for_from_80_and_quiet_while_it_runs()
    {
        var session = Prepared(1, 2);
        session.Update(Session(10, left: 200, tables: Tables(Order(1, A)), fire: new FireMeter(50, 3, false)));
        Assert.Null(session.Advice(S(10))!.Blaze);

        session.Update(Session(11, left: 199, tables: Tables(Order(1, A)), fire: new FireMeter(85, 9, false)));
        var near = session.Advice(S(11))!;
        Assert.Equal(FullBlazeAdvice.GetReady, near.Blaze);
        Assert.All(near.Dispatch!.Kitchens, k => Assert.Equal(KitchenQueue.Slots, k.Queue.Count)); // every slot filled for the rush

        session.Update(Session(12, left: 198, tables: Tables(Order(1, A)), fire: new FireMeter(100, 10, Ready: true)));
        Assert.Equal(FullBlazeAdvice.StartNow, session.Advice(S(12))!.Blaze);

        // The ready button went while the session goes on: the player pressed R.
        session.Update(Session(14, left: 196, tables: Tables(Order(1, A)), fire: new FireMeter(0, 10, false)));
        var running = session.Advice(S(14))!;
        Assert.True(running.BlazeRunning);
        Assert.Null(running.Blaze);
    }

    private static (Gourmand Gourmand, GourmandRequest Request) DishRequestMetBy(params string[] menu) =>
        Inn.Gourmands.SelectMany(g => g.Requests.Select(r => (g, r)))
            .First(x => GourmandCondition.Parse(x.r.When, Inn) is { ForDrink: false } c && menu.Any(id => c.MetBy(Dish(id)))
                        && Inn.MatchSaid(x.r.Text).FirstOrDefault()?.Request == x.r);

    [Fact]
    public void A_gourmands_request_read_off_the_bubble_is_put_in_plain_words_with_what_meets_it()
    {
        var session = Prepared(1, 2);
        var (_, request) = DishRequestMetBy(A, B, C, D, E);
        session.Update(Session(10, tables: Tables(new TableCard(6, TableState.GourmandOrder, [], [])), bubble: new GourmandBubble(request.Text, [request.Text])));

        var note = Assert.Single(session.Advice(S(10))!.Gourmands);

        Assert.Equal(6, note.Table);
        var asked = Assert.Single(note.Advice!.Requests);
        Assert.Equal(request, asked.Request);
        Assert.Equal(GourmandCondition.Parse(request.When, Inn).Describe(), asked.Description);
        Assert.NotEmpty(asked.Options);
        Assert.All(asked.Options, o => Assert.True(o.Meets(GourmandCondition.Parse(request.When, Inn))));
    }

    [Fact]
    public void A_request_not_recognised_is_kept_as_read_and_logged_once()
    {
        var warnings = new List<string>();
        var session = NewSession(warnings);
        session.Update(SetupScreen(0, 1));
        session.Update(MenuScreen(1, Menu));
        var gourmand = Tables(new TableCard(2, TableState.GourmandArrived, [], []));

        session.Update(Session(10, tables: gourmand, bubble: new GourmandBubble("xyz qrs tuv", ["xyz qrs tuv"])));
        session.Update(Session(11, tables: gourmand, bubble: new GourmandBubble("xyz qrs tuv", ["xyz qrs tuv"])));

        var note = Assert.Single(session.Advice(S(11))!.Gourmands);
        Assert.Null(note.Advice);
        Assert.Equal("xyz qrs tuv", note.UnreadText);
        Assert.Single(warnings);
    }

    /// <summary>
    /// Forgotten once their card reads them eating on two readings in a row: an order's card read otherwise once is
    /// taken as misread (the replay of the owner's 2026-10-20 recording: 16 orders' cards read empty for one reading).
    /// </summary>
    [Fact]
    public void A_gourmand_is_forgotten_once_two_readings_in_a_row_show_them_eating()
    {
        var session = Prepared(1);
        var (_, request) = DishRequestMetBy(A, B, C, D, E);
        session.Update(Session(10, tables: Tables(new TableCard(6, TableState.GourmandOrder, [], [])), bubble: new GourmandBubble(request.Text, [request.Text])));
        var eating = Tables(new TableCard(6, TableState.GourmandEating, [], []));

        session.Update(Session(11, tables: eating));
        Assert.Single(session.Advice(S(11))!.Gourmands);

        session.Update(Session(11.3, tables: eating));
        Assert.Empty(session.Advice(S(11.3))!.Gourmands);
    }

    /// <summary>
    /// Owner, live 2026-10-20 (test_yuanyuan3, 4:29): the gourmand panel "disappears … if you cannot see but the order is not
    /// fulfilled, just keep showing". The HUD fades while the camera moves; on the faded frame every table card reads
    /// empty (the fixture session-hud-faded.jpg through the real reader), then the cards come back unreadable for a
    /// moment. The request is kept from the moment it's read until the table's gourmand order is fulfilled, not tied
    /// to the bubble or the card being seen.
    /// </summary>
    [Fact]
    public void A_gourmands_request_stays_through_faded_and_unreadable_frames_until_the_table_eats()
    {
        var session = Prepared(1);
        var (_, request) = DishRequestMetBy(A, B, C, D, E);
        var ordering = new TableCard(4, TableState.GourmandOrder, [], []);
        session.Update(Session(10, tables: Tables(ordering), bubble: new GourmandBubble(request.Text, [request.Text])));
        Assert.Equal(4, Assert.Single(session.Advice(S(10))!.Gourmands).Table);

        // The faded HUD: every card read as empty, for a moment.
        for (double t = 10.5; t <= 12; t += 0.5) session.Update(Session(t, tables: Tables()));
        Assert.Equal(4, Assert.Single(session.Advice(S(12))!.Gourmands).Table);

        // Then no card readable for long (the bubble long gone).
        for (double t = 12.5; t <= 40; t += 0.5) session.Update(Session(t, tables: TablesAsRead(null, null, null, null, null, null)));
        Assert.Equal(4, Assert.Single(session.Advice(S(40))!.Gourmands).Table);

        session.Update(Session(41, tables: Tables(ordering)));
        Assert.Equal(4, Assert.Single(session.Advice(S(41))!.Gourmands).Table);

        // Fulfilled: the gourmand eats (read so twice).
        session.Update(Session(42, tables: Tables(new TableCard(4, TableState.GourmandEating, [], []))));
        session.Update(Session(42.3, tables: Tables(new TableCard(4, TableState.GourmandEating, [], []))));
        Assert.Empty(session.Advice(S(42.3))!.Gourmands);
    }

    /// <summary>
    /// A table read in another state (paying, empty, a new guest) is done with its gourmand once it has read so for
    /// <see cref="InnSession.GourmandGoneAfter"/> with no gourmand state between: the eating card can be missed, a
    /// faded card can't last that long.
    /// </summary>
    [Fact]
    public void A_gourmands_table_read_in_another_state_for_a_while_is_done()
    {
        var session = Prepared(1);
        var (_, request) = DishRequestMetBy(A, B, C, D, E);
        session.Update(Session(10, tables: Tables(new TableCard(4, TableState.GourmandOrder, [], [])), bubble: new GourmandBubble(request.Text, [request.Text])));

        // Read paying at 20, confirmed by the next reading at 21: from then on it's another state.
        var paying = Tables(new TableCard(4, TableState.Paying, [], []));
        session.Update(Session(20, tables: paying));
        session.Update(Session(21, tables: paying));
        session.Update(Session(22, tables: paying));
        Assert.Single(session.Advice(S(22))!.Gourmands);

        session.Update(Session(21 + InnSession.GourmandGoneAfter.TotalSeconds, tables: paying));
        Assert.Empty(session.Advice(S(21 + InnSession.GourmandGoneAfter.TotalSeconds))!.Gourmands);
    }

    [Fact]
    public void A_gourmands_request_ends_with_the_session()
    {
        var session = Prepared(1);
        var (_, request) = DishRequestMetBy(A, B, C, D, E);
        session.Update(Session(10, tables: Tables(new TableCard(4, TableState.GourmandOrder, [], [])), bubble: new GourmandBubble(request.Text, [request.Text])));

        session.Update(SetupScreen(30, 1));
        session.Update(Session(31, tables: TablesAsRead(null, null, null, null, null, null)));

        Assert.Empty(session.Advice(S(31))!.Gourmands);
    }
    // ---- Two gourmands (owner, live 2026-10-21, test_yuanyuan4 4:16–5:02) ----

    private static GourmandBubble Said(int pool, params int[] requests)
    {
        var gourmand = Inn.Gourmands.Single(g => g.Pool == "normal" && g.Number == pool);
        string[] lines = [.. requests.Select(r => gourmand.Requests[r].Text)];
        return new GourmandBubble(string.Concat(lines), lines);
    }

    /// <summary>Table 2's gourmand (normal 4): 「嗯……想吃甜品，最好有点酸。」「来一些不带水产的汤羹。」</summary>
    private static readonly GourmandBubble TableTwoSays = Said(4, 1, 4);

    /// <summary>Table 4's gourmand (normal 3, session-gourmand-soup-and-drink.jpg): 「清爽无油的汤羹，有吗？」「来杯喝的，谨记，我滴酒不沾。」</summary>
    private static readonly GourmandBubble TableFourSays = Said(3, 3, 5);

    private static TableCard Gourmand(int table, TableState state) => new(table, state, [], []);

    /// <summary>
    /// Table 4's circles showed 酸莓布丁 and 白瓜肉丸汤 from 4:16 to 5:02 while its gourmand asked for a soup without seed
    /// oil and a drink without alcohol (the bubble read right, InnBubbleReaderTests): another gourmand's words, kept. Table 2's
    /// gourmand had said them; read again while table 4's gourmand had just sat down, they went to table 4 (the table with
    /// no words yet), and table 4's own bubble, two requests like the first, never replaced them.
    /// </summary>
    [Fact]
    public void A_bubble_still_showing_another_tables_words_is_not_taken_for_a_new_gourmands()
    {
        var session = Prepared(1, 2);
        session.Update(Session(10, tables: Tables(Gourmand(2, TableState.GourmandOrder)), bubble: TableTwoSays));
        // Table 4's gourmand sits down while table 2's bubble is still read.
        session.Update(Session(11, tables: Tables(Gourmand(2, TableState.GourmandOrder), Gourmand(4, TableState.GourmandArrived)), bubble: TableTwoSays));
        session.Update(Session(11.3, tables: Tables(Gourmand(2, TableState.GourmandOrder), Gourmand(4, TableState.GourmandArrived)), bubble: TableTwoSays));
        // Table 2 is served and eats; table 4 speaks.
        for (double t = 12; t <= 13; t += 0.3)
            session.Update(Session(t, tables: Tables(Gourmand(2, TableState.GourmandEating), Gourmand(4, TableState.GourmandOrder)), bubble: TableFourSays));

        var note = Assert.Single(session.Advice(S(13))!.Gourmands);
        Assert.Equal(4, note.Table);
        Assert.Equal(TableFourSays.Lines, note.Advice!.Requests.Select(r => r.Request.Text));
        Assert.Equal([false, true], note.Advice.Requests.Select(r => GourmandCondition.Parse(r.Request.When, Inn).ForDrink));
        // The soup without seed oil is 白瓜肉丸汤, not 茄果鳞肉汤 (seed oil); the drink one without alcohol.
        Assert.Equal(A, note.Advice.Requests[0].Options[0].Id);
        Assert.All(note.Advice.Requests[1].Options, o => Assert.NotNull(o.Drink));
        Assert.NotEmpty(note.Advice.Requests[1].Options);
    }

    /// <summary>
    /// A gourmand already served: their bubble read again (still on screen as they eat, or the camera passing) is not a
    /// new gourmand's words, whatever table has just got one.
    /// </summary>
    [Fact]
    public void The_words_of_a_gourmand_already_served_are_nobodys_new_request()
    {
        var session = Prepared(1, 2);
        session.Update(Session(10, tables: Tables(Gourmand(2, TableState.GourmandOrder)), bubble: TableTwoSays));
        session.Update(Session(11, tables: Tables(Gourmand(2, TableState.GourmandEating))));
        session.Update(Session(11.3, tables: Tables(Gourmand(2, TableState.GourmandEating))));
        Assert.Empty(session.Advice(S(11.3))!.Gourmands);

        session.Update(Session(12, tables: Tables(Gourmand(2, TableState.GourmandEating), Gourmand(4, TableState.GourmandArrived)), bubble: TableTwoSays));
        Assert.Empty(session.Advice(S(12))!.Gourmands);

        session.Update(Session(13, tables: Tables(Gourmand(2, TableState.GourmandEating), Gourmand(4, TableState.GourmandOrder)), bubble: TableFourSays));
        var note = Assert.Single(session.Advice(S(13))!.Gourmands);
        Assert.Equal(4, note.Table);
        Assert.Equal(TableFourSays.Lines, note.Advice!.Requests.Select(r => r.Request.Text));
    }

    /// <summary>
    /// A new gourmand at the same table (the last one left without their eating card being read): their own words replace
    /// the last one's, even when as many.
    /// </summary>
    [Fact]
    public void A_new_gourmands_words_at_a_table_replace_the_last_ones()
    {
        var session = Prepared(1, 2);
        session.Update(Session(10, tables: Tables(Gourmand(4, TableState.GourmandOrder)), bubble: TableTwoSays));
        session.Update(Session(11, tables: Tables(Gourmand(4, TableState.GourmandArrived)), bubble: TableFourSays));

        var note = Assert.Single(session.Advice(S(11))!.Gourmands);
        Assert.Equal(TableFourSays.Lines, note.Advice!.Requests.Select(r => r.Request.Text));
    }
}
