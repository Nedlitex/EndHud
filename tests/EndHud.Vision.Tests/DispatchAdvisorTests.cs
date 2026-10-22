using EndHud.Vision.Inn;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// Which kitchen each ordered dish goes to, what to cook ahead, and when to swap a dish cooked ahead
/// for an order (<see cref="DispatchAdvisor"/>). The kitchens: 1 = 佩丽卡 + 艾维文娜 (菜肴 +20 %,
/// 甜品 +10 %, 甜 +10 %), 2 = 赛希 + 伊冯 (甜品 +30 %, 鲜 +10 %), 3 = a cook with no bonus; Rating 4, Si's 品质把控 II.
/// Cook times there: 坛子肉 27/35/35 s, 白瓜肉丸汤 40/37/40, 果味沙拉 30/35/35, “勺里游” 27/32/35,
/// 肉包子 and 缘缘菜饭 15 everywhere.
/// </summary>
public class DispatchAdvisorTests
{
    private static Dish Dish(string zh) => InnTestData.Dish(zh);

    private static DispatchAdvisor Advisor(int kitchens = 3) => new(InnSimulatorTests.Kitchens(kitchens));

    private static KitchenQueue Queue(int kitchen, params (string Dish, double SecondsLeft)[] dishes) =>
        new(kitchen, dishes.Select(d => new QueuedDish(Dish(d.Dish), d.SecondsLeft)));

    /// <param name="servingLeft">Until the inn stops serving (the timer + <see cref="InnState.ServingAfterCloseSeconds"/>); by default mid-session, far from the end.</param>
    private static InnState State(IReadOnlyList<KitchenQueue> queues, IReadOnlyList<OrderedDish>? outstanding = null,
        IReadOnlyList<string>? counter = null, IReadOnlyList<string>? carried = null, IReadOnlyList<MenuDish>? menu = null, double? servingLeft = 200,
        IReadOnlyList<string>? outForOrders = null, IReadOnlyList<QueuedAsCalled>? asCalled = null, IReadOnlyCollection<string>? theme = null) =>
        new(queues, [.. (counter ?? []).Concat(outForOrders ?? []).Select(Dish)], [.. (outForOrders ?? []).Select(Dish)], [.. (carried ?? []).Select(Dish)],
            outstanding ?? [], menu ?? InnSimulatorTests.RecordedMenu, servingLeft, asCalled ?? [], theme ?? []);

    private static OrderedDish Order(int table, string dish) => new(table, Dish(dish));

    private static IEnumerable<(int Kitchen, QueueAdvice Advice)> All(DispatchAdvice advice) =>
        advice.Kitchens.SelectMany(k => k.Queue.Select(q => (k.Kitchen, q)));

    private static IEnumerable<(int Kitchen, QueueAdvice Advice)> Sent(DispatchAdvice advice) => All(advice).Where(a => !a.Advice.CookAhead);

    [Fact]
    public void An_ordered_dish_goes_to_the_kitchen_that_has_it_out_soonest()
    {
        // Kitchen 1 cooks 坛子肉 fastest (27 s) but has 20 s of 果味沙拉 to go first: 47 s. Kitchens 2 and 3: 35 s.
        var advice = Advisor().Advise(State([Queue(1, ("果味沙拉", 20)), Queue(2), Queue(3)], [Order(1, "坛子肉")]));

        var (kitchen, sent) = Assert.Single(Sent(advice));
        Assert.Equal((2, 1, 35.0), (kitchen, sent.Table!.Value, sent.ReadyIn)); // 2 over 3: 18 % 珍馐 against 13 %
    }

    /// <summary>
    /// A kitchen known to have no chef cannot cook (owner, 2026-10-19): nothing is sent there, cooked
    /// ahead there or swapped there, and it isn't listed as a kitchen to queue in.
    /// </summary>
    [Fact]
    public void A_kitchen_with_no_chef_gets_no_advice_at_all()
    {
        var kitchens = new InnKitchens(InnTestData.Inn, [InnTestData.Kitchen(1, "Perlica", "Avywenna"), InnTestData.EmptyKitchen(2)], 4, 5);
        var advisor = new DispatchAdvisor(kitchens);

        // Kitchen 1 has 20 s of 果味沙拉 to go: an empty kitchen 2 would have 坛子肉 out sooner, were it cooking.
        var advice = advisor.Advise(State([Queue(1, ("果味沙拉", 20)), Queue(2)], [Order(1, "坛子肉")]));
        var opening = advisor.Opening(InnSimulatorTests.RecordedMenu, themeItems: []);

        Assert.Equal([1], advice.Kitchens.Select(k => k.Kitchen));
        Assert.Equal(1, Assert.Single(Sent(advice)).Kitchen);
        Assert.Equal([1], opening.Kitchens.Select(k => k.Kitchen));
        Assert.Null(advisor.SoonestOut(State([Queue(1, ("果味沙拉", 20), ("坛子肉", 27), ("坛子肉", 27)), Queue(2)]), Dish("坛子肉")));
    }

    /// <summary>A kitchen whose chefs aren't known (setup never seen, no panel) is taken to cook, at base speed.</summary>
    [Fact]
    public void A_kitchen_whose_chefs_are_unknown_still_cooks()
    {
        var kitchens = new InnKitchens(InnTestData.Inn, [InnTestData.Kitchen(1, "Perlica", "Avywenna"), new Kitchen(2, []) { ChefsUnknown = true }], 4, 5);

        var advice = new DispatchAdvisor(kitchens).Advise(State([Queue(1, ("果味沙拉", 20)), Queue(2)], [Order(1, "坛子肉")]));

        Assert.Equal((2, 35.0), (Assert.Single(Sent(advice)).Kitchen, Assert.Single(Sent(advice)).Advice.ReadyIn));
    }

    /// <summary>Owner, 2026-10-19: no cooking ahead in the last 40 s; an order is still sent.</summary>
    [Fact]
    public void No_dish_is_cooked_ahead_in_the_last_40_seconds()
    {
        var idle = Advisor().Advise(State([Queue(1), Queue(2), Queue(3)], servingLeft: 39));
        var everywhere = Advisor().Advise(State([Queue(1), Queue(2), Queue(3)], servingLeft: 39), fillEverySlot: true);
        var ordered = Advisor().Advise(State([Queue(1), Queue(2), Queue(3)], [Order(1, "肉包子")], servingLeft: 10));

        Assert.Empty(All(idle));
        Assert.Empty(All(everywhere));
        Assert.Equal(1, Assert.Single(All(ordered)).Advice.Table);
        Assert.Equal(40, DispatchAdvisor.NoCookAheadSeconds);
    }

    /// <summary>Owner, 2026-10-19: never cook ahead a dish that wouldn't come out before the inn closes.</summary>
    [Fact]
    public void A_dish_is_cooked_ahead_only_if_it_comes_out_before_closing()
    {
        var advice = Advisor().Advise(State([Queue(1, ("果味沙拉", 20)), Queue(2), Queue(3)], servingLeft: 45), fillEverySlot: true);

        Assert.NotEmpty(All(advice));
        Assert.All(All(advice), a => Assert.True(a.Advice.ReadyIn <= 45, $"{a.Advice.Dish.Zh} in kitchen {a.Kitchen} out in {a.Advice.ReadyIn} s"));
    }

    /// <summary>
    /// Owner, 2026-10-19: at the very last, serve whatever is left; a wrong dish still earns some
    /// money. Once the inn has closed, a dish on the counter goes to the table that ordered it, and
    /// what's left after that to a table still waiting whose own dish can't come out in the 18 s
    /// the inn keeps serving, oldest first.
    /// </summary>
    [Fact]
    public void Once_the_inn_has_closed_whats_left_goes_to_the_tables_still_waiting_the_right_dish_first()
    {
        // Just closed: 18 s of serving left. Table 4's 缘缘菜饭 is out in 10 s and it waits for it;
        // table 3's 果味沙拉 can't be out before 30 s.
        var advice = Advisor().Advise(State([Queue(1, ("缘缘菜饭", 10)), Queue(2), Queue(3)],
            [Order(4, "缘缘菜饭"), Order(3, "果味沙拉"), Order(2, "坛子肉")], counter: ["肉包子", "坛子肉"], servingLeft: InnState.ServingLeftAfterClose(0)));

        Assert.Equal([new LeftoverAdvice(Dish("坛子肉"), 2, Dish("坛子肉")), new LeftoverAdvice(Dish("肉包子"), 3, Dish("果味沙拉"))], advice.Leftovers);
        Assert.Equal([true, false], advice.Leftovers.Select(l => l.Matches));
        Assert.DoesNotContain(Sent(advice), s => s.Advice.Table == 3); // it gets the leftover instead
        Assert.DoesNotContain(All(advice), a => a.Advice.CookAhead);
    }

    /// <summary>
    /// Owner, 2026-10-19: the last call is per table and can come before 00:00. With 10 s on the
    /// timer the inn serves 28 s more: a table whose dish can't be out by then gets a dish left
    /// over (a wrong one still earns money); a table whose dish can make it waits for it.
    /// </summary>
    [Fact]
    public void Before_closing_a_table_whose_dish_cant_come_out_in_time_gets_a_leftover_and_one_whose_dish_can_waits()
    {
        // Kitchen 1: 果味沙拉 out in 20 s (table 4's: in time), then 坛子肉 out in 47 s (table 5's: too late).
        // Table 3's 白瓜肉丸汤 takes 37 s at best; table 2's 缘缘菜饭 15 s.
        var advice = Advisor().Advise(State([Queue(1, ("果味沙拉", 20), ("坛子肉", 27)), Queue(2), Queue(3)],
            [Order(4, "果味沙拉"), Order(5, "坛子肉"), Order(3, "白瓜肉丸汤"), Order(2, "缘缘菜饭")],
            counter: ["肉包子", "“勺里游”"], servingLeft: InnState.ServingLeft(10)));

        Assert.Equal([new LeftoverAdvice(Dish("肉包子"), 5, Dish("坛子肉")), new LeftoverAdvice(Dish("“勺里游”"), 3, Dish("白瓜肉丸汤"))], advice.Leftovers);
        Assert.DoesNotContain(Sent(advice), s => s.Advice.Table == 3);
        Assert.Contains(Sent(advice), s => s.Advice.Table == 2 && s.Advice.Dish.Id == Dish("缘缘菜饭").Id);
    }

    [Fact]
    public void Far_from_the_end_nothing_is_handed_out_as_a_leftover()
    {
        var advice = Advisor().Advise(State([Queue(1), Queue(2), Queue(3)], [Order(3, "果味沙拉"), Order(2, "坛子肉")], counter: ["肉包子", "坛子肉"], servingLeft: InnState.ServingLeft(30)));

        Assert.Empty(advice.Leftovers);
    }

    [Fact]
    public void The_inn_serves_18_seconds_after_the_timers_00_00()
    {
        Assert.Equal(18, InnState.ServingAfterCloseSeconds);
        Assert.Equal(28, InnState.ServingLeft(10));
        Assert.Equal(13, InnState.ServingLeftAfterClose(5));
        Assert.Equal(0, InnState.ServingLeftAfterClose(30));
    }

    /// <summary>On a tie, the kitchen with the better choice-dish chance, even when it's the later one.</summary>
    [Fact]
    public void A_tie_goes_to_the_better_choice_chance()
    {
        var kitchens = new InnKitchens(InnTestData.Inn, [InnTestData.PlainKitchen(1), InnTestData.Kitchen(2, "Perlica")], 4, 5);
        var advisor = new DispatchAdvisor(kitchens);

        // 白瓜肉丸汤: 40 s in both; 珍馐 13 % in kitchen 1, 18 % with 佩丽卡's 创意.
        var advice = advisor.Advise(State([Queue(1), Queue(2)], [Order(3, "白瓜肉丸汤")]));

        Assert.Equal(2, Assert.Single(Sent(advice)).Kitchen);
    }

    /// <summary>
    /// A dish carried or queued covers an order; one taken to be on the counter doesn't (owner, 2026-10-21: the counter
    /// is only estimated, and an order covered by a dish that isn't really there got no call).
    /// </summary>
    [Fact]
    public void Dishes_already_carried_or_queued_are_not_sent_again_but_one_on_the_counter_is()
    {
        var advice = Advisor().Advise(State([Queue(1, ("果味沙拉", 12)), Queue(2), Queue(3)],
            [Order(1, "坛子肉"), Order(2, "肉包子"), Order(3, "果味沙拉"), Order(4, "缘缘菜饭")], counter: ["肉包子"], carried: ["坛子肉"]));

        Assert.Equal([(2, Dish("肉包子")), (4, Dish("缘缘菜饭"))], Sent(advice).Select(s => (s.Advice.Table!.Value, s.Advice.Dish)).OrderBy(s => s.Item1));
    }

    /// <summary>
    /// A dish out of a kitchen for an order (it was that order's dish in the queue) covers it, as it did while queued; a
    /// dish cooked ahead on the counter covers none. Without the first, an order whose dish had just come out was sent to
    /// a kitchen again and every kitchen's call reshuffled (test_yuanyuan3, 362.4 s). Once closed, it's handed to its table.
    /// </summary>
    [Fact]
    public void A_dish_out_for_an_order_covers_it_and_one_cooked_ahead_covers_none()
    {
        var orders = new[] { Order(1, "坛子肉"), Order(2, "坛子肉") };

        var advice = Advisor().Advise(State([Queue(1), Queue(2), Queue(3)], orders, counter: ["坛子肉"], outForOrders: ["坛子肉"]));
        var closed = Advisor().Advise(State([Queue(1), Queue(2), Queue(3)], orders, outForOrders: ["坛子肉"], servingLeft: InnState.ServingLeftAfterClose(0)));

        Assert.Equal([2], Sent(advice).Select(s => s.Advice.Table!.Value)); // table 1's is out; the one cooked ahead isn't table 2's
        Assert.Contains(new LeftoverAdvice(Dish("坛子肉"), 1, Dish("坛子肉")), closed.Leftovers);
    }

    /// <summary>
    /// The orders on screen stay in their kitchens counting each other: an order shown in kitchen 1 isn't moved for an
    /// empty kitchen 2 that kitchen 2's own order on screen takes. Kitchen 1 has 坛子肉 out in 20 + 27 = 47 s; kitchen 2
    /// would in 35 s, but after its 白瓜肉丸汤 in 72 s; kitchen 3 in 5 + 35 = 40 s, less than 8 s sooner. Before, kitchen 2
    /// counted as empty, the order left kitchen 1 and went to kitchen 3 (test_yuanyuan3, 383.7 s: an order moved to a
    /// kitchen that was showing another).
    /// </summary>
    [Fact]
    public void An_order_on_screen_counts_the_other_orders_on_screen_in_their_kitchens()
    {
        var shown = new[] { new KeptCall(1, Dish("坛子肉"), 3, Remove: null), new KeptCall(2, Dish("白瓜肉丸汤"), 5, Remove: null) };

        var advice = Advisor().Advise(State([Queue(1, ("果味沙拉", 20)), Queue(2), Queue(3, ("肉包子", 5))], [Order(3, "坛子肉"), Order(5, "白瓜肉丸汤")]),
            fillEverySlot: false, shown);

        Assert.Equal(["3:坛子肉"], advice.For(1).Queue.Where(q => !q.CookAhead).Select(q => $"{q.Table}:{q.Dish.Zh}"));
        Assert.Equal(["5:白瓜肉丸汤"], advice.For(2).Queue.Where(q => !q.CookAhead).Select(q => $"{q.Table}:{q.Dish.Zh}"));
    }

    /// <summary>
    /// An order on screen out too late in its kitchen (serving ends in 40 s; kitchen 1 has 坛子肉 out in 20 + 27 = 47 s) goes
    /// where it is out in time (kitchen 2, empty); with no kitchen having it out in time (2 and 3 busy too) it stays where it
    /// is shown: moving it gains nothing. Until 2026-10-22 it went back among the orders and a newer order took its kitchen
    /// (test_yuanyuan4 430.8 s, a gourmand's soup beaten by table 5's pudding in the last seconds).
    /// </summary>
    [Fact]
    public void An_order_on_screen_too_late_everywhere_stays_and_moves_only_to_a_kitchen_in_time()
    {
        var shown = new[] { new KeptCall(1, Dish("坛子肉"), 3, Remove: null) };
        // Table 5 ordered later but is read first: the order the advice takes them in must not move table 3's.
        OrderedDish[] orders = [Order(5, "果味沙拉"), Order(3, "坛子肉")];

        var inTimeElsewhere = Advisor().Advise(State([Queue(1, ("果味沙拉", 20)), Queue(2), Queue(3, ("肉包子", 30))], orders, servingLeft: 40), fillEverySlot: false, shown);
        var lateEverywhere = Advisor().Advise(State([Queue(1, ("果味沙拉", 20)), Queue(2, ("白瓜肉丸汤", 30)), Queue(3, ("肉包子", 30))], orders, servingLeft: 40),
            fillEverySlot: false, shown);

        Assert.DoesNotContain(inTimeElsewhere.For(1).Queue, q => q.Table == 3);
        Assert.Contains(inTimeElsewhere.Kitchens.SelectMany(k => k.Queue), q => q.Table == 3 && q.ReadyIn <= 40);
        Assert.Equal(["3:坛子肉"], lateEverywhere.For(1).Queue.Where(q => !q.CookAhead).Select(q => $"{q.Table}:{q.Dish.Zh}"));
    }

    /// <summary>
    /// Owner, 2026-10-21: the advice never gates on how full the counter is. EndHud only estimates the counter (it is
    /// never read off the screen); on test_yuanyuan4 the estimate went "full" from about 250 s and hid real orders. An
    /// order is sent to a kitchen with the counter full, even with a dish of its kind on it.
    /// </summary>
    [Fact]
    public void An_order_is_sent_to_a_kitchen_with_the_counter_full_even_of_its_own_dish()
    {
        var full = State([Queue(1), Queue(2), Queue(3)], [Order(1, "坛子肉")], counter: ["坛子肉", .. Enumerable.Repeat("肉包子", InnCounter.Capacity - 1)]);

        var advice = Advisor().Advise(full);

        var (_, sent) = Assert.Single(Sent(advice));
        Assert.Equal((1, Dish("坛子肉")), (sent.Table!.Value, sent.Dish));
    }

    /// <summary>
    /// Owner, 2026-10-21: cooking ahead has no counter cap (a dish not used gives its ingredients back, so cooking ahead
    /// is never a waste). With the counter full, the idle kitchens still cook ahead, each up to all but one slot.
    /// </summary>
    [Fact]
    public void Cooking_ahead_is_advised_with_the_counter_full()
    {
        var full = State([Queue(1), Queue(2), Queue(3)], counter: [.. Enumerable.Repeat("缘缘菜饭", InnCounter.Capacity)]);

        var advice = Advisor().Advise(full);

        Assert.Equal(3 * (KitchenQueue.Slots - 1), All(advice).Count());
        Assert.All(All(advice), a => Assert.True(a.Advice.CookAhead));
    }

    /// <summary>Several orders at once: each goes where it's out soonest given the ones sent before it.</summary>
    [Fact]
    public void Several_orders_spread_over_the_kitchens()
    {
        var advice = Advisor().Advise(State([Queue(1), Queue(2), Queue(3)],
            [Order(1, "坛子肉"), Order(2, "坛子肉"), Order(3, "坛子肉"), Order(4, "坛子肉")]));

        // 27 s in kitchen 1; then 35 s in 2 or 3 beats 54 s in 1; then the other; then 1 again (54 s) beats 70 s.
        Assert.Equal([(1, 27.0), (1, 54.0), (2, 35.0), (3, 35.0)],
            Sent(advice).Select(s => (s.Kitchen, s.Advice.ReadyIn)).OrderBy(s => s.Kitchen).ThenBy(s => s.ReadyIn));
    }

    [Fact]
    public void An_order_with_no_kitchen_free_waits_and_one_out_of_stock_is_said()
    {
        var full = new[] { ("肉包子", 10.0), ("肉包子", 15.0), ("肉包子", 15.0) };
        var menu = InnSimulatorTests.RecordedMenu.Select(m => m.Dish.Zh == "坛子肉" ? m with { Stock = 0 } : m).ToList();
        var outstanding = Enumerable.Range(1, 3).Select(t => Order(t, "肉包子")).Concat([Order(4, "缘缘菜饭"), Order(5, "坛子肉")]).ToList();

        var advice = Advisor(1).Advise(State([Queue(1, full)], outstanding, menu: menu));

        Assert.Equal([Order(4, "缘缘菜饭")], advice.Waiting);
        Assert.Equal([Order(5, "坛子肉")], advice.OutOfStock);
        Assert.Empty(All(advice));
    }

    /// <summary>
    /// C3: in random kitchens' states, the one ordered dish always goes to the kitchen with a free
    /// slot that has it out soonest (checked by trying every kitchen), ties to the better 珍馐 chance.
    /// </summary>
    [Fact]
    public void Every_advised_kitchen_is_the_soonest()
    {
        var kitchens = InnSimulatorTests.Kitchens();
        var advisor = new DispatchAdvisor(kitchens);
        var menu = InnSimulatorTests.RecordedMenu.Select(m => m.Dish).ToList();
        var random = new Random(7);

        for (int round = 0; round < 500; round++)
        {
            var queues = kitchens.Kitchens.Select(k => new KitchenQueue(k.Number, Enumerable.Range(0, random.Next(KitchenQueue.Slots + 1))
                .Select(i => { var d = menu[random.Next(menu.Count)]; int cook = kitchens.CookSeconds(k.Number, d); return new QueuedDish(d, i == 0 ? random.Next(1, cook + 1) : cook); }))).ToList();
            // Everything queued is ordered (so nothing can be swapped), and one more dish no queue holds.
            var outstanding = queues.SelectMany(q => q.Dishes).Select((d, i) => new OrderedDish(i % 6 + 1, d.Dish)).ToList();
            var dish = menu[random.Next(menu.Count)];
            outstanding.Add(new OrderedDish(6, dish));

            var advice = advisor.Advise(new InnState(queues, [], [], [], outstanding, InnSimulatorTests.RecordedMenu, ServingSecondsLeft: 200, QueuedAsCalled: [], ThemeItems: []));

            var options = queues.Where(q => q.HasFreeSlot)
                .Select(q => (q.Kitchen, ReadyIn: q.SecondsUntilEmpty + kitchens.CookSeconds(q.Kitchen, dish), Choice: kitchens.ChoicePercent(q.Kitchen, dish)))
                .ToList();
            var sent = Sent(advice).ToList();
            if (options.Count == 0)
            {
                Assert.Empty(sent);
                continue;
            }
            var (kitchen, queued) = Assert.Single(sent);
            double soonest = options.Min(o => o.ReadyIn);
            Assert.Equal(soonest, queued.ReadyIn);
            Assert.Equal(options.Where(o => o.ReadyIn == soonest).Max(o => o.Choice), kitchens.ChoicePercent(kitchen, dish));
        }
    }

    [Fact]
    public void An_idle_kitchen_cooks_ahead_the_slowest_dishes_spread_over_the_menu_leaving_a_slot_free()
    {
        var advice = Advisor().Advise(State([Queue(1), Queue(2), Queue(3)]));

        var ahead = All(advice).ToList();
        Assert.All(ahead, a => Assert.True(a.Advice.CookAhead));
        Assert.All(advice.Kitchens, k => Assert.Equal(KitchenQueue.Slots - 1, k.Queue.Count));
        Assert.Equal(6, ahead.Select(a => a.Advice.Dish.Id).Distinct().Count());
        // Kitchen 1's first: a dish of the type its chefs speed up (菜肴 +20 %), the slowest of those there (果味沙拉, 30 s).
        Assert.Equal("果味沙拉", advice.For(1).Queue[0].Dish.Zh);
        // The quick 15 s dishes come last: not among six of eight.
        Assert.DoesNotContain(ahead, a => a.Advice.Dish.CookSeconds == 15);
    }

    // ---- The calls on screen kept (owner, 2026-10-21: "the recommendation should stay steady") ----

    /// <summary>
    /// An order the player has been told to queue in a kitchen stays there while that's still possible there (owner,
    /// 2026-10-21: "the recommendation should stay steady"; the soonest kitchen decides only when it is first shown): a
    /// free slot, in stock, out before serving ends. Another kitchen having it out far sooner is no reason to move it.
    /// Kitchen 1 is empty and cooks 坛子肉 in 27 s; kitchen 2 in 35 s behind 12 s of 果味沙拉 (47 s).
    /// </summary>
    [Fact]
    public void An_order_shown_in_a_kitchen_stays_there_while_possible()
    {
        InnState With(double servingLeft) => State([Queue(1), Queue(2, ("果味沙拉", 12))], [Order(1, "坛子肉")], servingLeft: servingLeft);
        var shownInTwo = new[] { new KeptCall(2, Dish("坛子肉"), 1, Remove: null) };

        // Kitchen 2 out 20 s after kitchen 1 (47 s against 27 s): the soonest kitchen for an order not shown, kept in 2.
        Assert.Equal(1, Assert.Single(Sent(Advisor(2).Advise(With(200)))).Kitchen);
        Assert.Equal(2, Assert.Single(Sent(Advisor(2).Advise(With(200), fillEverySlot: false, shownInTwo))).Kitchen);

        // Out too late in kitchen 2 for serving (47 s with 40 s left), in time in kitchen 1: it can't stay.
        Assert.Equal(1, Assert.Single(Sent(Advisor(2).Advise(With(40), fillEverySlot: false, shownInTwo))).Kitchen);

        // Kitchen 2 full: it can't be queued there.
        var full = State([Queue(1, ("果味沙拉", 20)), Queue(2, ("果味沙拉", 1), ("肉包子", 15), ("肉包子", 15))], [Order(1, "坛子肉")]);
        Assert.Equal(1, Assert.Single(Sent(Advisor(2).Advise(full, fillEverySlot: false, shownInTwo))).Kitchen);
    }

    /// <summary>
    /// A dish to cook ahead the player has been shown stays the kitchen's dish to cook ahead while that's still possible
    /// there: a free slot and nothing ordered for that kitchen, the dish in stock and out in time. It goes before any other
    /// dish (on test_yuanyuan3 the last two places on the counter, when cooking ahead stopped at the counter's room, went to
    /// other kitchens' dishes and the dish shown was dropped). The fastest dish (肉包子, 15 s) is otherwise cooked ahead last.
    /// </summary>
    [Fact]
    public void A_dish_to_cook_ahead_shown_in_a_kitchen_stays_while_possible()
    {
        var shown = new[] { new KeptCall(2, Dish("肉包子"), null, Remove: null) };
        var empty = new[] { Queue(1), Queue(2), Queue(3) };

        Assert.NotEqual("肉包子", Advisor().Advise(State(empty)).For(2).Queue[0].Dish.Zh);
        Assert.Equal("肉包子", Advisor().Advise(State(empty), fillEverySlot: false, shown).For(2).Queue[0].Dish.Zh);

        // Kitchen 2 with two dishes queued: a new dish to cook ahead would leave it a slot for orders, but the one shown
        // stays while there's a free slot (test_yuanyuan3 at 141.6 s: dropped, and back 1.5 s later as a dish came out).
        var twoQueued = new[] { Queue(1), Queue(2, ("果味沙拉", 20), ("肉包子", 15)), Queue(3) };
        Assert.Empty(Advisor().Advise(State(twoQueued)).For(2).Queue);
        Assert.Equal(["肉包子"], Advisor().Advise(State(twoQueued), fillEverySlot: false, shown).For(2).Queue.Select(q => q.Dish.Zh));

        // The counter full changes nothing (owner, 2026-10-21: no counter cap).
        var fullCounter = Advisor().Advise(State(empty, counter: [.. Enumerable.Repeat("缘缘菜饭", InnCounter.Capacity)]), fillEverySlot: false, shown);
        Assert.Equal("肉包子", fullCounter.For(2).Queue[0].Dish.Zh);

        // Impossible: the dish out of stock, an order sent to that kitchen.
        var noStock = InnSimulatorTests.RecordedMenu.Select(m => m with { Stock = m.Dish.Zh == "肉包子" ? 0 : null }).ToList();
        Assert.DoesNotContain(All(Advisor().Advise(State(empty, menu: noStock), fillEverySlot: false, shown)), a => a.Advice.Dish.Zh == "肉包子");
        var order = Advisor().Advise(State([Queue(1, ("果味沙拉", 20)), Queue(2), Queue(3, ("果味沙拉", 20))], [Order(4, "坛子肉")]), fillEverySlot: false, shown);
        Assert.Equal(["4:坛子肉"], order.For(2).Queue.Select(q => $"{q.Table}:{q.Dish.Zh}"));
    }

    [Fact]
    public void A_kitchen_sent_an_order_cooks_nothing_ahead()
    {
        var advice = Advisor().Advise(State([Queue(1), Queue(2), Queue(3)], [Order(1, "坛子肉")]));

        Assert.Equal([Dish("坛子肉")], advice.For(1).Queue.Select(q => q.Dish));
        Assert.All(advice.For(2).Queue.Concat(advice.For(3).Queue), q => Assert.True(q.CookAhead));
    }

    /// <summary>
    /// How much is cooked ahead depends on the kitchens' free slots only, never on the counter (owner, 2026-10-21): each
    /// idle kitchen up to all but one slot, whatever is on the counter. Before, it stopped at the counter's room (8, counting
    /// what's on it and what's already ahead).
    /// </summary>
    [Theory]
    [InlineData(0, 0, 6)]
    [InlineData(5, 0, 6)]
    [InlineData(8, 0, 6)]
    [InlineData(4, 3, 3)] // one dish queued ahead in each kitchen: one more each
    public void Cooking_ahead_has_no_counter_cap(int onCounter, int queuedAhead, int expected)
    {
        var queues = new[] { Queue(1), Queue(2), Queue(3) };
        for (int i = 0; i < queuedAhead; i++) queues[i] = Queue(i + 1, ("肉包子", 15));

        var advice = Advisor().Advise(State(queues, counter: [.. Enumerable.Repeat("缘缘菜饭", onCounter)]));

        Assert.Equal(expected, All(advice).Count());
    }

    [Fact]
    public void A_dish_with_no_stock_is_never_cooked_ahead_and_unlimited_stock_is()
    {
        var menu = InnSimulatorTests.RecordedMenu.Select(m => m with { Stock = m.Dish.Zh == "肉包子" ? null : m.Dish.Zh == "坛子肉" ? 1 : 0 }).ToList();

        var advice = Advisor().Advise(State([Queue(1), Queue(2), Queue(3)], menu: menu), fillEverySlot: true);

        var dishes = All(advice).Select(a => a.Advice.Dish.Zh).ToList();
        Assert.Equal(1, dishes.Count(d => d == "坛子肉"));
        Assert.Equal(8, dishes.Count(d => d == "肉包子"));
        Assert.Equal(3 * KitchenQueue.Slots, dishes.Count);
    }

    /// <summary>
    /// C16: at the session's start every free slot of every kitchen gets a dish (no counter cap, owner
    /// 2026-10-21), different dishes first, slowest first, each where it is out first (owner,
    /// 2026-10-19: counting what is already queued there, not where it cooks fastest in an empty kitchen).
    /// </summary>
    [Theory]
    [InlineData(1, 3)]
    [InlineData(2, 6)]
    [InlineData(3, 9)]
    public void The_opening_fills_every_kitchen(int kitchens, int expected)
    {
        var advisor = Advisor(kitchens);

        var advice = advisor.Opening(InnSimulatorTests.RecordedMenu, themeItems: []);

        var ahead = All(advice).ToList();
        Assert.Equal(expected, ahead.Count);
        Assert.All(ahead, a => Assert.True(a.Advice.CookAhead));
        Assert.Equal(Math.Min(expected, InnSimulatorTests.RecordedMenu.Count), ahead.Select(a => a.Advice.Dish.Id).Distinct().Count());
        if (kitchens == 3)
        {
            // No theme here. Each dish once before any twice; among those, a type the kitchen's chefs speed up first, then the
            // slowest, each in the kitchen where it is out first (ties: the better 珍馐 chance, then the lower number). Kitchen
            // 1 (菜肴 +20 %) takes its 菜肴 dishes, 果味沙拉 (30 s) then 坛子肉 (27 s, out at 57); the 40 s soups go to 2 and 3,
            // 白瓜肉丸汤 to 2 (37 s there, out at 77), “勺里游” to 3 (out at 75), the 15 s dishes where they're out at 72 and 90.
            // The ninth, every dish now cooked ahead once, the slowest again in the one slot left: a 40 s soup in 2, out at 117.
            // (Before 2026-10-21, "the slowest" alone: kitchen 1 began with a 40 s soup it has no bonus for.)
            Assert.Equal(["果味沙拉", "坛子肉", "肉包子"], advice.For(1).Queue.Select(q => q.Dish.Zh));
            Assert.Equal(["莓果甜汤", "白瓜肉丸汤", "莓果甜汤"], advice.For(2).Queue.Select(q => q.Dish.Zh));
            Assert.Equal(["茄果鳞肉汤", "“勺里游”", "缘缘菜饭"], advice.For(3).Queue.Select(q => q.Dish.Zh));
            Assert.Equal([72.0, 117, 90], advice.Kitchens.Select(k => k.Queue[^1].ReadyIn));
        }
    }

    /// <summary>
    /// Owner, 2026-10-19: a dish goes to the kitchen where it is finished first, counting what that
    /// kitchen is cooking from its progress (the seconds it has left), the dishes waiting behind it in
    /// full, and this dish's cook time there; an idle kitchen wins only when that makes it the
    /// earliest. Kitchen 1 (菜肴 +20 %: 坛子肉 27 s) is cooking a 40 s 白瓜肉丸汤; kitchen 2, a plain cook,
    /// is idle (坛子肉 35 s). With 5 s left, kitchen 1 has it out at 32 s; with 10 s left, at 37 s, and
    /// kitchen 2 wins. The same for an order and for a dish cooked ahead.
    /// </summary>
    [Theory]
    [InlineData(5, 1, 32)]
    [InlineData(10, 2, 35)]
    public void A_dish_goes_where_it_is_finished_first_counting_the_progress_of_the_dish_cooking(double soupLeft, int kitchen, double readyIn)
    {
        var advisor = new DispatchAdvisor(new InnKitchens(InnTestData.Inn, [InnTestData.Kitchen(1, "Perlica", "Avywenna"), InnTestData.PlainKitchen(2)], 4, 5));
        var menu = new[] { new MenuDish(Dish("坛子肉"), null) };

        var ordered = advisor.Advise(State([Queue(1, ("白瓜肉丸汤", soupLeft)), Queue(2)], [Order(1, "坛子肉")], menu: menu));
        // Stock for one dish cooked ahead.
        var ahead = advisor.Advise(State([Queue(1, ("白瓜肉丸汤", soupLeft)), Queue(2)], menu: [new MenuDish(Dish("坛子肉"), 1)]));

        var sent = Assert.Single(Sent(ordered));
        Assert.Equal((kitchen, readyIn), (sent.Kitchen, sent.Advice.ReadyIn));
        var cooked = Assert.Single(All(ahead));
        Assert.Equal((kitchen, readyIn), (cooked.Kitchen, cooked.Advice.ReadyIn));
    }

    /// <summary>
    /// The live session at 4:48 (owner, 2026-10-19, "suggest cooking in a working kitchen while other
    /// kitchens are idle"): the opening, kitchen 1 (秋栗 + 昼雪, slow for every dish on this menu) cooking
    /// 白瓜肉丸汤 with 35 s left, kitchens 2 and 3 empty. The strip put three dishes ahead in kitchens 2 and
    /// 3 each, stacked (kitchen 3's third out at 84 s), and one in kitchen 1, whose second would have been
    /// out at 70 s: each dish had gone where it cooks fastest in an empty kitchen, not where it is out first.
    /// No dish may be advised where it comes out later than it would in a kitchen left with a free slot.
    /// </summary>
    [Fact]
    public void The_opening_puts_each_dish_where_it_is_out_first_not_where_it_cooks_fastest()
    {
        var kitchens = InnSimulatorTests.LiveKitchens();
        var advisor = new DispatchAdvisor(kitchens);

        var advice = advisor.Advise(State([Queue(1, ("白瓜肉丸汤", 35)), Queue(2), Queue(3)], menu: InnSimulatorTests.LiveMenu, servingLeft: 288 + 18), fillEverySlot: true);

        var queued = new Dictionary<int, double> { [1] = 35, [2] = 0, [3] = 0 };
        var count = new Dictionary<int, int> { [1] = 1, [2] = 0, [3] = 0 };
        foreach (var (kitchen, a) in All(advice))
        {
            queued[kitchen] = Math.Max(queued[kitchen], a.ReadyIn);
            count[kitchen]++;
        }
        Assert.Equal(3 * KitchenQueue.Slots, count.Values.Sum()); // every slot: no counter cap
        foreach (var (kitchen, a) in All(advice))
        foreach (int other in count.Keys.Where(k => k != kitchen && count[k] < KitchenQueue.Slots))
            Assert.True(a.ReadyIn <= queued[other] + kitchens.CookSeconds(other, a.Dish),
                $"{a.Dish.Zh} in kitchen {kitchen} out at {a.ReadyIn} s; kitchen {other}, left with a free slot, would have it out at {queued[other] + kitchens.CookSeconds(other, a.Dish)} s");
    }

    /// <summary>Full Blaze's "get ready" fills the free slots of kitchens that also got orders.</summary>
    [Fact]
    public void Filling_every_slot_tops_up_kitchens_with_orders_too()
    {
        var advice = Advisor().Advise(State([Queue(1, ("肉包子", 5)), Queue(2), Queue(3)], [Order(1, "肉包子")]), fillEverySlot: true);

        Assert.All(advice.Kitchens, k => Assert.Equal(k.Kitchen == 1 ? 2 : 3, k.Queue.Count));
    }

    /// <summary>
    /// Two kitchens, both full. Kitchen 1 holds dishes cooked ahead; kitchen 2 only ordered ones. A
    /// new order for “勺里游” (27 s in kitchen 1): take out the dish cooked ahead whose removal has it
    /// out soonest (果味沙拉, 30 s still to wait for) and queue it instead.
    /// </summary>
    [Fact]
    public void A_dish_cooked_ahead_is_swapped_for_an_order_no_kitchen_has_room_for()
    {
        var queues = new[] { Queue(1, ("肉包子", 10), ("坛子肉", 27), ("果味沙拉", 30)), Queue(2, ("白瓜肉丸汤", 30), ("白瓜肉丸汤", 37), ("白瓜肉丸汤", 37)) };
        var outstanding = new[] { Order(1, "白瓜肉丸汤"), Order(2, "白瓜肉丸汤"), Order(3, "白瓜肉丸汤"), Order(4, "“勺里游”") };

        var advice = Advisor(2).Advise(State(queues, outstanding));

        var swap = Assert.Single(advice.For(1).Replaces);
        Assert.Equal(new ReplaceAdvice(1, 2, Dish("果味沙拉"), Dish("“勺里游”"), 4, 10 + 27 + 27, null), swap);
        Assert.Empty(advice.Waiting);
        Assert.Empty(advice.For(2).Replaces);
    }

    /// <summary>A dish a table ordered is never suggested for removal, however much it would help.</summary>
    [Fact]
    public void An_ordered_dish_is_never_swapped_out()
    {
        var queues = new[] { Queue(1, ("肉包子", 15), ("坛子肉", 27), ("果味沙拉", 30)) };
        var ordered = new[] { Order(1, "肉包子"), Order(2, "坛子肉"), Order(3, "果味沙拉") };

        var advice = Advisor(1).Advise(State(queues, [.. ordered, Order(4, "“勺里游”")]));
        Assert.Empty(advice.For(1).Replaces);
        Assert.Equal([Order(4, "“勺里游”")], advice.Waiting);

        // With 果味沙拉 ordered but 坛子肉 not, 坛子肉 is the one taken out.
        var partly = Advisor(1).Advise(State(queues, [ordered[0], ordered[2], Order(4, "“勺里游”")]));
        Assert.Equal(Dish("坛子肉"), Assert.Single(partly.For(1).Replaces).Remove);
    }

    /// <summary>
    /// The swap must beat the best kitchen with room by more than the margin. Kitchen 1: 果味沙拉
    /// cooking well under way, 坛子肉 cooked ahead after it; swapping 坛子肉 for “勺里游” has it out in
    /// 20 + 27 = 47 s. Kitchen 2 has room after an ordered soup with <paramref name="soupLeft"/> s to go (+ 32 s).
    /// </summary>
    [Theory]
    [InlineData(18, false)] // 50 s: within 5 s of 47, so no swap
    [InlineData(30, true)] // 62 s
    public void A_swap_must_win_by_a_margin(double soupLeft, bool swapped)
    {
        var queues = new[] { Queue(1, ("果味沙拉", 20), ("坛子肉", 27)), Queue(2, ("白瓜肉丸汤", soupLeft)) };
        var outstanding = new[] { Order(1, "果味沙拉"), Order(2, "白瓜肉丸汤"), Order(3, "“勺里游”") };

        var advice = Advisor(2).Advise(State(queues, outstanding));

        if (swapped)
        {
            Assert.Equal(new ReplaceAdvice(1, 1, Dish("坛子肉"), Dish("“勺里游”"), 3, 47, soupLeft + 32), Assert.Single(advice.For(1).Replaces));
            Assert.DoesNotContain(Sent(advice), s => s.Kitchen == 2);
        }
        else
        {
            Assert.Empty(advice.For(1).Replaces);
            Assert.Equal((2, soupLeft + 32), Sent(advice).Select(s => (s.Kitchen, s.Advice.ReadyIn)).Single());
        }
    }

    /// <summary>
    /// A swap shown stays a swap while the dish it takes out is still there to take out (owner, 2026-10-21: "the
    /// recommendation should stay steady"). Until then the swap was kept as a plain order in its kitchen, and the next frame's
    /// advice queued the order behind the dish cooked ahead, losing what the swap was for (test_yuanyuan5: shown as a swap and
    /// as a plain order 0.3 s apart, five times). Once that dish is gone (cooked out), the order stays there as a plain one.
    /// </summary>
    [Fact]
    public void A_swap_shown_stays_a_swap_while_its_dish_is_there_to_take_out()
    {
        // The dishes of A_swap_must_win_by_a_margin, named by id (this file's helpers take the game's names).
        static string Named(string id) => InnTestData.Inn.Dishes.Single(d => d.Id == id).Zh;
        string salad = Named("berry-fruity-toss"), soup = Named("melon-meatball-soup"), spoon = Named("spoon-swimmer"), jar = Named("clay-jar-meat");
        var outstanding = new[] { Order(1, salad), Order(2, soup), Order(3, spoon) };
        var shown = new[] { new KeptCall(1, Dish(spoon), 3, Remove: Dish(jar)) };

        var kept = Advisor(2).Advise(State([Queue(1, (salad, 20), (jar, 27)), Queue(2, (soup, 30))], outstanding), fillEverySlot: false, shown);
        Assert.Equal(new ReplaceAdvice(1, 1, Dish(jar), Dish(spoon), 3, 47, 62), Assert.Single(kept.For(1).Replaces));
        Assert.DoesNotContain(Sent(kept), s => s.Advice.Table == 3);

        var cookedOut = Advisor(2).Advise(State([Queue(1, (salad, 20)), Queue(2, (soup, 30))], outstanding), fillEverySlot: false, shown);
        Assert.Empty(cookedOut.For(1).Replaces);
        Assert.Equal(1, Assert.Single(Sent(cookedOut), s => s.Advice.Table == 3).Kitchen);
    }

    /// <summary>
    /// A dish the player queued because a call said so is never suggested for removal while it still comes out in time
    /// (owner's live session 2026-10-21, test_yuanyuan5 at 04:15: "厨房二 预做：界龙肉卷", followed at 118.5 s, then at 128.7 s
    /// "先拿掉界龙肉卷，再排这个" for an order of 鳞肉水饺). The swap of <see cref="A_swap_must_win_by_a_margin"/> (62 s against 47)
    /// isn't advised when 坛子肉 was queued as called: the order goes to kitchen 2. Near the end, where it would no longer
    /// come out in time, it is fair game again.
    /// </summary>
    [Fact]
    public void A_dish_queued_as_a_call_said_is_not_swapped_out_again()
    {
        var queues = new[] { Queue(1, ("果味沙拉", 20), ("坛子肉", 27)), Queue(2, ("白瓜肉丸汤", 30)) };
        var outstanding = new[] { Order(1, "果味沙拉"), Order(2, "白瓜肉丸汤"), Order(3, "“勺里游”") };
        QueuedAsCalled[] told = [new(1, Dish("坛子肉"))];

        var advice = Advisor(2).Advise(State(queues, outstanding, asCalled: told));

        Assert.Empty(advice.For(1).Replaces);
        Assert.Equal((2, 30.0 + 32), Sent(advice).Select(s => (s.Kitchen, s.Advice.ReadyIn)).Single());

        // Told in kitchen 2, not 1: kitchen 1's is still the player's own, swapped.
        Assert.Single(Advisor(2).Advise(State(queues, outstanding, asCalled: [new(2, Dish("坛子肉"))])).For(1).Replaces);
    }

    /// <summary>A dish cooked ahead that is cooking loses its progress when taken out: only one barely started is.</summary>
    [Theory]
    [InlineData(25, true)] // 2 s cooked of 27
    [InlineData(5, false)] // 22 s cooked
    public void A_cooking_dish_is_only_swapped_when_barely_started(double left, bool swapped)
    {
        var queues = new[] { Queue(1, ("坛子肉", left), ("肉包子", 15), ("缘缘菜饭", 15)) };
        var outstanding = new[] { Order(1, "肉包子"), Order(2, "缘缘菜饭"), Order(3, "“勺里游”") };

        var advice = Advisor(1).Advise(State(queues, outstanding));

        if (swapped) Assert.Equal(new ReplaceAdvice(1, 0, Dish("坛子肉"), Dish("“勺里游”"), 3, 15 + 15 + 27, null), Assert.Single(advice.For(1).Replaces));
        else Assert.Equal([Order(3, "“勺里游”")], advice.Waiting);
    }

    /// <summary>C5/C6: one or two kitchens, and tables 1 to 6; anything else is refused.</summary>
    [Fact]
    public void Fewer_kitchens_and_tables_work_and_out_of_range_is_refused()
    {
        var one = Advisor(1).Advise(State([Queue(1)], [Order(6, "坛子肉")]));
        Assert.Equal((1, 27.0), Sent(one).Select(s => (s.Kitchen, s.Advice.ReadyIn)).Single());

        var two = Advisor(2).Advise(State([Queue(1, ("坛子肉", 27), ("坛子肉", 27)), Queue(2)], [Order(1, "坛子肉"), Order(2, "坛子肉"), Order(1, "白瓜肉丸汤")]));
        Assert.Equal(2, Sent(two).Single().Kitchen);

        Assert.Throws<ArgumentException>(() => Advisor(2).Advise(State([Queue(1), Queue(3)])));
        Assert.Throws<ArgumentException>(() => Advisor().Advise(State([Queue(1)], [Order(7, "坛子肉")])));
        Assert.Throws<ArgumentException>(() => Advisor().Advise(State([Queue(1)], [Order(0, "坛子肉")])));
    }

    [Fact]
    public void Soonest_out_names_the_kitchen_and_the_time()
    {
        var state = State([Queue(1, ("坛子肉", 20)), Queue(2), Queue(3)]);

        Assert.Equal(((int, double)?)(2, 37.0), Advisor().SoonestOut(state, Dish("白瓜肉丸汤")));
        Assert.Null(Advisor(1).SoonestOut(State([Queue(1, ("坛子肉", 1), ("坛子肉", 27), ("坛子肉", 27))]), Dish("坛子肉")));
    }

    /// <summary>
    /// test_yuanyuan6 at 201 s (timer 04:54; owner: "the first kitchen has all the bonus for 主食, but the precook suggest a
    /// 甜点, which completely wasted the bonus"): theme 一汤一饭 (its hot types 主食 and 汤羹), kitchen 1 = 秋栗 + 昼雪 (主食
    /// +40 %, 鲜/辣 +5 %), the menu 界龙肉卷, 至臻鳞肉卷, 豌杂拌面 (主食), 莓果甜汤, 茄果鳞肉汤 (汤羹) and 金丝小贝 (甜品). With
    /// one of each matching dish already cooked ahead, "the dish cooked ahead least, then the slowest" picked 金丝小贝, the one
    /// dish neither the theme nor the kitchen favours. Owner decision: theme-matching dishes first; the kitchen's own bonuses
    /// matter.
    /// </summary>
    [Fact]
    public void Cooking_ahead_in_a_main_dish_kitchen_of_a_main_and_soup_theme_picks_a_main_dish()
    {
        var kitchens = new InnKitchens(InnTestData.Inn, [InnTestData.Kitchen(1, "Akekuri", "Snowshine")], 4, 0);
        string[] mains = ["界龙肉卷", "至臻鳞肉卷", "豌杂拌面"], soups = ["莓果甜汤", "茄果鳞肉汤"];
        var menu = mains.Concat(soups).Append("金丝小贝").Select(zh => new MenuDish(Dish(zh), 100)).ToList();
        string[] theme = [nameof(DishType.Entree), nameof(DishType.Soup)];
        Assert.All(mains, zh => Assert.Equal(DishType.Entree, Dish(zh).Type));
        Assert.All(soups, zh => Assert.Equal(DishType.Soup, Dish(zh).Type));

        var spread = new DispatchAdvisor(kitchens).Advise(State([Queue(1)], counter: [.. mains, .. soups], menu: menu, theme: theme));
        var empty = new DispatchAdvisor(kitchens).Advise(State([Queue(1)], menu: menu, theme: theme));
        // No theme read and nothing cooked ahead yet: the kitchen's bonus puts a 主食 before the slower soups and dessert.
        var noTheme = new DispatchAdvisor(kitchens).Advise(State([Queue(1)], menu: menu, theme: []));

        foreach (var advice in new[] { spread, empty, noTheme })
        {
            var ahead = advice.For(1).Queue.Where(q => q.CookAhead).ToList();
            Assert.NotEmpty(ahead);
            Assert.All(ahead, q => Assert.Contains(q.Dish.Zh, mains));
        }
    }
}
