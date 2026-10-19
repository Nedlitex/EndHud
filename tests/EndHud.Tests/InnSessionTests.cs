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

    [Theory]
    [InlineData(InnScreenKind.Setup)]
    [InlineData(InnScreenKind.Menu)]
    [InlineData(InnScreenKind.ChefPicker)]
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
    public void The_menu_is_kept_until_the_setup_screen_after_a_session()
    {
        var session = Prepared();
        session.Update(Session(10));
        Assert.Null(session.Advice(S(10))!.Gap);

        session.Update(SetupScreen(400, 1, 2, 3)); // setting up the next session
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
    public void A_dish_turning_ready_on_a_card_is_out_of_the_kitchen_even_before_the_estimate_says_so()
    {
        var session = Prepared(1);
        session.Update(Session(10, tables: Tables(Order(2, A)), panel: Panel(1, 1, Menu[..5], (A, 30))));

        // The card says A is ready long before the estimate's 30 s: the kitchen's A is gone, so a
        // second order of A is not covered by it.
        session.Update(Session(12, tables: Tables(Order(2, new OrderItem(A, ItemProgress.Ready)), Order(4, A))));

        var advice = session.Advice(S(12))!;
        Assert.Contains(advice.Dispatch!.For(1).Queue, q => q.Dish.Id == A && q.Table == 4);
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

    [Fact]
    public void A_card_that_cant_be_read_is_taken_as_it_was_for_3_seconds_then_said_to_be_unreadable()
    {
        var session = Prepared(1, 2);
        session.Update(Session(10, tables: Tables(Order(3, A))));

        session.Update(Session(12, tables: TablesAsRead(Empty(1), Empty(2), null, Empty(4), Empty(5), Empty(6))));
        var kept = session.Advice(S(12))!;
        Assert.Contains(Queued(kept), q => q.Table == 3);
        Assert.Empty(kept.UnreadableTables);

        session.Update(Session(14, tables: TablesAsRead(Empty(1), Empty(2), null, Empty(4), Empty(5), Empty(6))));
        var stale = session.Advice(S(14))!;
        Assert.DoesNotContain(Queued(stale), q => q.Table == 3);
        Assert.Equal([3], stale.UnreadableTables);
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

    [Fact]
    public void A_gourmand_is_forgotten_once_they_are_eating()
    {
        var session = Prepared(1);
        var (_, request) = DishRequestMetBy(A, B, C, D, E);
        session.Update(Session(10, tables: Tables(new TableCard(6, TableState.GourmandOrder, [], [])), bubble: new GourmandBubble(request.Text, [request.Text])));

        session.Update(Session(11, tables: Tables(new TableCard(6, TableState.GourmandEating, [], []))));

        Assert.Empty(session.Advice(S(11))!.Gourmands);
    }
}
