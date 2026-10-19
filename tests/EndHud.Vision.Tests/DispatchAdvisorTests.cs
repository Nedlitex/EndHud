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

    /// <param name="secondsLeft">Until the inn closes; by default mid-session, far from the end.</param>
    private static InnState State(IReadOnlyList<KitchenQueue> queues, IReadOnlyList<OrderedDish>? outstanding = null,
        IReadOnlyList<string>? counter = null, IReadOnlyList<string>? carried = null, IReadOnlyList<MenuDish>? menu = null, double? secondsLeft = 200) =>
        new(queues, [.. (counter ?? []).Select(Dish)], [.. (carried ?? []).Select(Dish)], outstanding ?? [], menu ?? InnSimulatorTests.RecordedMenu, secondsLeft);

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
        var opening = advisor.Opening(InnSimulatorTests.RecordedMenu);

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
        var idle = Advisor().Advise(State([Queue(1), Queue(2), Queue(3)], secondsLeft: 39));
        var everywhere = Advisor().Advise(State([Queue(1), Queue(2), Queue(3)], secondsLeft: 39), fillEverySlot: true);
        var ordered = Advisor().Advise(State([Queue(1), Queue(2), Queue(3)], [Order(1, "肉包子")], secondsLeft: 10));

        Assert.Empty(All(idle));
        Assert.Empty(All(everywhere));
        Assert.Equal(1, Assert.Single(All(ordered)).Advice.Table);
        Assert.Equal(40, DispatchAdvisor.NoCookAheadSeconds);
    }

    /// <summary>Owner, 2026-10-19: never cook ahead a dish that wouldn't come out before the inn closes.</summary>
    [Fact]
    public void A_dish_is_cooked_ahead_only_if_it_comes_out_before_closing()
    {
        var advice = Advisor().Advise(State([Queue(1, ("果味沙拉", 20)), Queue(2), Queue(3)], secondsLeft: 45), fillEverySlot: true);

        Assert.NotEmpty(All(advice));
        Assert.All(All(advice), a => Assert.True(a.Advice.ReadyIn <= 45, $"{a.Advice.Dish.Zh} in kitchen {a.Kitchen} out in {a.Advice.ReadyIn} s"));
    }

    /// <summary>
    /// Owner, 2026-10-19: at the very last, serve whatever is left; a wrong dish still earns some
    /// money. Once the inn has closed, a dish on the counter goes to the table that ordered it, and
    /// what's left after that to a table still waiting with nothing cooking for it, oldest first.
    /// </summary>
    [Fact]
    public void Once_the_inn_has_closed_whats_left_goes_to_the_tables_still_waiting_the_right_dish_first()
    {
        var advice = Advisor().Advise(State([Queue(1, ("缘缘菜饭", 10)), Queue(2), Queue(3)],
            [Order(4, "缘缘菜饭"), Order(3, "果味沙拉"), Order(2, "坛子肉")], counter: ["肉包子", "坛子肉"], secondsLeft: 0));

        Assert.Equal([new LeftoverAdvice(Dish("坛子肉"), 2, Dish("坛子肉")), new LeftoverAdvice(Dish("肉包子"), 3, Dish("果味沙拉"))], advice.Leftovers);
        Assert.Equal([true, false], advice.Leftovers.Select(l => l.Matches));
        Assert.DoesNotContain(Sent(advice), s => s.Advice.Table == 3); // it gets the leftover instead
        Assert.Empty(All(advice).Where(a => a.Advice.CookAhead));
    }

    [Fact]
    public void Before_the_inn_closes_nothing_is_handed_out_as_a_leftover()
    {
        var advice = Advisor().Advise(State([Queue(1), Queue(2), Queue(3)], [Order(3, "果味沙拉"), Order(2, "坛子肉")], counter: ["肉包子", "坛子肉"], secondsLeft: 30));

        Assert.Empty(advice.Leftovers);
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

    [Fact]
    public void Dishes_already_carried_on_the_counter_or_queued_are_not_sent_again()
    {
        var advice = Advisor().Advise(State([Queue(1, ("果味沙拉", 12)), Queue(2), Queue(3)],
            [Order(1, "坛子肉"), Order(2, "肉包子"), Order(3, "果味沙拉"), Order(4, "缘缘菜饭")], counter: ["肉包子"], carried: ["坛子肉"]));

        var (_, sent) = Assert.Single(Sent(advice));
        Assert.Equal((4, Dish("缘缘菜饭")), (sent.Table!.Value, sent.Dish));
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

            var advice = advisor.Advise(new InnState(queues, [], [], outstanding, InnSimulatorTests.RecordedMenu, SecondsLeft: 200));

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
        // Kitchen 1's first: one of the dishes slowest there (40 s).
        Assert.Contains(advice.For(1).Queue[0].Dish.Zh, new[] { "白瓜肉丸汤", "茄果鳞肉汤" });
        // The quick 15 s dishes come last: not among six of eight.
        Assert.DoesNotContain(ahead, a => a.Advice.Dish.CookSeconds == 15);
    }

    [Fact]
    public void A_kitchen_sent_an_order_cooks_nothing_ahead()
    {
        var advice = Advisor().Advise(State([Queue(1), Queue(2), Queue(3)], [Order(1, "坛子肉")]));

        Assert.Equal([Dish("坛子肉")], advice.For(1).Queue.Select(q => q.Dish));
        Assert.All(advice.For(2).Queue.Concat(advice.For(3).Queue), q => Assert.True(q.CookAhead));
    }

    /// <summary>Never more cooked ahead than the counter (8) has room for, counting what's on it and what's already ahead.</summary>
    [Theory]
    [InlineData(0, 0, 6)]
    [InlineData(5, 0, 3)]
    [InlineData(7, 0, 1)]
    [InlineData(8, 0, 0)]
    [InlineData(4, 3, 1)] // 4 on the counter, 3 queued ahead: 1 more
    public void Cooking_ahead_never_overfills_the_counter(int onCounter, int queuedAhead, int expected)
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
        Assert.Equal(7, dishes.Count(d => d == "肉包子"));
        Assert.Equal(8, dishes.Count);
    }

    /// <summary>
    /// C16: at the session's start every free slot of every kitchen gets a dish, no more in all than
    /// the counter holds, different dishes first, slowest first, each where it cooks fastest.
    /// </summary>
    [Theory]
    [InlineData(1, 3)]
    [InlineData(2, 6)]
    [InlineData(3, 8)] // nine slots, but the counter holds eight
    public void The_opening_fills_every_kitchen(int kitchens, int expected)
    {
        var advisor = Advisor(kitchens);

        var advice = advisor.Opening(InnSimulatorTests.RecordedMenu);

        var ahead = All(advice).ToList();
        Assert.Equal(expected, ahead.Count);
        Assert.All(ahead, a => Assert.True(a.Advice.CookAhead));
        Assert.Equal(Math.Min(expected, InnSimulatorTests.RecordedMenu.Count), ahead.Select(a => a.Advice.Dish.Id).Distinct().Count());
        if (kitchens == 3)
        {
            // Slowest first, each in the free kitchen that cooks it fastest (ties: out sooner, then the
            // better 珍馐 chance): kitchen 1 takes the 40 s soup, 莓果甜汤 (37 s there) and 果味沙拉
            // (30 s); 白瓜肉丸汤 is fastest in 2 (37 s); with 1 full, 坛子肉 (35 s in 2 or 3) goes
            // where it's out sooner, and so on.
            Assert.Equal(["茄果鳞肉汤", "莓果甜汤", "果味沙拉"], advice.For(1).Queue.Select(q => q.Dish.Zh));
            Assert.Equal(["白瓜肉丸汤", "“勺里游”"], advice.For(2).Queue.Select(q => q.Dish.Zh));
            Assert.Equal(["坛子肉", "肉包子", "缘缘菜饭"], advice.For(3).Queue.Select(q => q.Dish.Zh));
        }
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
}
