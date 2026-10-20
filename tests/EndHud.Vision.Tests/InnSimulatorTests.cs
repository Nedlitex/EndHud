using EndHud.Vision.Inn;
using Xunit;
using Xunit.Abstractions;

namespace EndHud.Vision.Tests;

/// <summary>A kitchen's queue, the counter, and a session replayed through them (<see cref="InnSimulator"/>).</summary>
public class InnSimulatorTests(ITestOutputHelper output)
{
    private const int Rating = 4;
    private const int SiQuality = 5;

    private static Dish Dish(string zh) => InnTestData.Dish(zh);

    /// <summary>The eight dishes the 派单 panel of test_yuanyuan2 lists, all unlimited.</summary>
    internal static IReadOnlyList<MenuDish> RecordedMenu { get; } =
        [.. new[] { "白瓜肉丸汤", "莓果甜汤", "果味沙拉", "“勺里游”", "坛子肉", "茄果鳞肉汤", "肉包子", "缘缘菜饭" }.Select(zh => new MenuDish(Dish(zh), null))];

    /// <summary>Kitchen 1 as in test_yuanyuan2, kitchen 2 as test_yuanyuan1's kitchen 3, kitchen 3 with no chefs; the first <paramref name="count"/>.</summary>
    internal static InnKitchens Kitchens(int count = 3) => new(InnTestData.Inn,
        [.. new[] { InnTestData.Kitchen(1, "Perlica", "Avywenna"), InnTestData.Kitchen(2, "Xaihi", "Yvonne"), InnTestData.PlainKitchen(3) }.Take(count)],
        Rating, SiQuality);

    /// <summary>
    /// The order times of test_yuanyuan1's session (the research note: 23 orders, the session starting
    /// at 150 s of the recording) and how many dishes each had (1 dish + drink = 1, 2 dishes = 2,
    /// a gourmand's requests counted as dishes). Which dish each was isn't known from the note, so the
    /// dishes are drawn from <see cref="RecordedMenu"/> at random with a fixed seed: guests pick at random (owner).
    /// </summary>
    /// <summary>
    /// The recorded orders run past 5:00 (the last at 304 s), so the comparisons of ways of dispatching
    /// let the replay serve long after the game's 18 s, until every order is served.
    /// </summary>
    private const double LongRun = 600;

    internal static IReadOnlyList<InnOrder> RecordedOrderTimes(int seed = 1, int tables = 6, IReadOnlyList<MenuDish>? menu = null)
    {
        menu ??= RecordedMenu;
        (double At, int Table, int Dishes)[] recorded =
        [
            (166, 4, 1), (181, 5, 1), (193, 2, 1), (205, 6, 1), (217, 1, 2), (229, 3, 1), (241, 2, 1), (266, 5, 2),
            (267, 4, 1), (280, 1, 1), (299, 3, 1), (313, 2, 2), (322, 5, 1), (334, 6, 2), (346, 1, 1), (361, 3, 1),
            (373, 4, 1), (385, 5, 1), (400, 2, 1), (412, 6, 2), (427, 4, 1), (439, 1, 1), (454, 5, 1),
        ];
        var random = new Random(seed);
        return [.. recorded.Select(r => new InnOrder(r.At - 150, (r.Table - 1) % tables + 1,
            [.. Enumerable.Range(0, r.Dishes).Select(_ => menu[random.Next(menu.Count)].Dish)]))];
    }

    /// <summary>
    /// What a player does without advice: each dish ordered and not yet cooking or on the counter
    /// goes to the first kitchen with room, and nothing is cooked ahead.
    /// </summary>
    internal static IReadOnlyList<InnAction> FirstFreeKitchen(InnSimulator inn)
    {
        var have = inn.Counter.Dishes.Concat(inn.Queues.SelectMany(q => q.Dishes.Select(d => d.Dish))).Select(d => d.Id).ToList();
        var free = inn.Queues.ToDictionary(q => q.Kitchen, q => q.FreeSlots);
        var actions = new List<InnAction>();
        foreach (var order in inn.Outstanding)
        {
            if (have.Remove(order.Dish.Id)) continue;
            if (free.Where(f => f.Value > 0).Select(f => (int?)f.Key).Min() is not { } kitchen) break;
            actions.Add(new QueueDish(kitchen, order.Dish));
            free[kitchen]--;
        }
        return actions;
    }

    [Fact]
    public void A_kitchen_queues_three_dishes_and_refuses_a_fourth()
    {
        var queue = new KitchenQueue(1);

        Assert.True(queue.TryAdd(Dish("肉包子"), 15));
        Assert.True(queue.TryAdd(Dish("坛子肉"), 27));
        Assert.True(queue.TryAdd(Dish("果味沙拉"), 30));
        Assert.False(queue.TryAdd(Dish("缘缘菜饭"), 15));

        Assert.Equal(3, queue.Dishes.Count);
        Assert.False(queue.HasFreeSlot);
        Assert.Equal(72, queue.SecondsUntilEmpty);
        Assert.Equal(42, queue.SecondsUntilOut(1));
        Assert.Throws<ArgumentException>(() => new KitchenQueue(1, Enumerable.Repeat(new QueuedDish(Dish("肉包子"), 15), 4)));
    }

    [Fact]
    public void Dishes_come_out_in_order_and_the_next_starts_at_once()
    {
        var queue = new KitchenQueue(1);
        queue.TryAdd(Dish("肉包子"), 15);
        queue.TryAdd(Dish("坛子肉"), 27);

        Assert.Empty(queue.Tick(10));
        Assert.Equal([Dish("肉包子")], queue.Tick(10));
        Assert.Equal(22, queue.Dishes[0].SecondsLeft); // 5 s of 坛子肉 done
        Assert.Equal([Dish("坛子肉")], queue.Tick(30));
        Assert.Empty(queue.Dishes);
    }

    /// <summary>Any slot can be taken out; the dish cooking loses what it had done, and the next starts from nothing.</summary>
    [Fact]
    public void A_dish_taken_out_while_cooking_is_lost_and_the_next_starts()
    {
        var queue = new KitchenQueue(1);
        queue.TryAdd(Dish("坛子肉"), 27);
        queue.TryAdd(Dish("肉包子"), 15);
        queue.Tick(20);

        var removed = queue.RemoveAt(0);

        Assert.Equal((Dish("坛子肉"), 7.0), (removed.Dish, removed.SecondsLeft));
        Assert.Equal(15, queue.SecondsUntilEmpty);
        Assert.True(queue.TryAdd(Dish("缘缘菜饭"), 15));
    }

    [Fact]
    public void The_counter_holds_eight_and_a_kitchen_waits_when_its_full()
    {
        var counter = new InnCounter();
        for (int i = 0; i < 8; i++) Assert.True(counter.TryPut(Dish("肉包子")));
        Assert.False(counter.TryPut(Dish("肉包子")));

        var queue = new KitchenQueue(1);
        queue.TryAdd(Dish("缘缘菜饭"), 15);
        queue.TryAdd(Dish("肉包子"), 15);
        Assert.Empty(queue.Tick(20, counter.Room));
        Assert.Equal(0, queue.Dishes[0].SecondsLeft);
        Assert.Equal(15, queue.Dishes[1].SecondsLeft); // nothing else cooks meanwhile

        Assert.True(counter.TryTake(Dish("肉包子")));
        Assert.Equal([Dish("缘缘菜饭")], queue.Tick(0.5, counter.Room));
    }

    /// <summary>C7 in the simulator: a policy that queues a fourth dish in one kitchen is refused.</summary>
    [Fact]
    public void The_simulator_refuses_a_fourth_dish_and_a_dish_out_of_stock()
    {
        var menu = new[] { new MenuDish(Dish("肉包子"), 1), new MenuDish(Dish("坛子肉"), null) };
        var inn = new InnSimulator(Kitchens(1), menu);
        int asked = 0;

        var run = inn.Run([new InnOrder(0, 1, [Dish("坛子肉")])], _ => asked++ > 0 ? [] :
            [new QueueDish(1, Dish("坛子肉")), new QueueDish(1, Dish("肉包子")), new QueueDish(1, Dish("肉包子")), new QueueDish(1, Dish("坛子肉")), new QueueDish(1, Dish("坛子肉"))]);

        // The second 肉包子: no stock left. The last 坛子肉: the queue is full.
        Assert.Equal([new QueueDish(1, Dish("肉包子")), new QueueDish(1, Dish("坛子肉"))], run.Refused);
        Assert.Equal(1, run.Served);
    }

    /// <summary>
    /// C3: replaying the recorded order times, following the advice serves every order, sooner on
    /// average and at worst than sending each dish to the first kitchen with room, for 3, 2 and 1 kitchens.
    /// </summary>
    [Theory]
    [InlineData(3, 1)]
    [InlineData(3, 2)]
    [InlineData(3, 3)]
    [InlineData(2, 1)]
    [InlineData(1, 1)]
    public void Following_the_advice_serves_every_order_sooner_than_the_first_free_kitchen(int kitchens, int seed)
    {
        var orders = RecordedOrderTimes(seed);

        var advised = new InnSimulator(Kitchens(kitchens), RecordedMenu).Run(orders, InnSimulator.Following(new DispatchAdvisor(Kitchens(kitchens))), servingAfterClose: LongRun);
        var naive = new InnSimulator(Kitchens(kitchens), RecordedMenu).Run(orders, FirstFreeKitchen, servingAfterClose: LongRun);

        Assert.Equal(orders.Count, advised.Served);
        Assert.Empty(advised.Refused);
        Assert.True(advised.Orders.Average(o => o.Waited!.Value) < naive.Orders.Where(o => o.Waited is not null).Average(o => o.Waited!.Value),
            $"advised {advised.Orders.Average(o => o.Waited!.Value):F1} s, first free kitchen {naive.Orders.Where(o => o.Waited is not null).Average(o => o.Waited!.Value):F1} s");
        Assert.True(advised.Orders.Max(o => o.Waited!.Value) <= naive.Orders.Max(o => o.Waited ?? double.MaxValue));
    }

    /// <summary>C6: one table to six, the replay serves every order.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(6)]
    public void Any_number_of_tables_is_served(int tables)
    {
        var orders = RecordedOrderTimes(1, tables);

        var run = new InnSimulator(Kitchens(), RecordedMenu).Run(orders, InnSimulator.Following(new DispatchAdvisor(Kitchens())), servingAfterClose: LongRun);

        Assert.Equal(orders.Count, run.Served);
        Assert.Empty(run.Refused);
    }

    /// <summary>
    /// Owner, 2026-10-19: the inn serves 18 s after the timer's 00:00, then stops. A dish out at 327 s
    /// is too late; the advisor is told the serving time left, not the timer's.
    /// </summary>
    [Fact]
    public void The_simulated_inn_serves_18_seconds_after_the_timer_then_stops()
    {
        var inn = new InnSimulator(Kitchens(1), RecordedMenu);
        var seen = new List<(double Now, double? ServingLeft)>();

        var run = inn.Run([new InnOrder(0, 1, [Dish("坛子肉")])], sim =>
        {
            seen.Add((sim.Now, sim.State.ServingSecondsLeft));
            return sim.Now == 300 ? [new QueueDish(1, Dish("坛子肉"))] : [];
        });

        Assert.Null(Assert.Single(run.Orders).ServedAt); // 27 s from 300 s: out at 327 s
        Assert.Equal(318, seen.First(s => s.Now == 0).ServingLeft);
        Assert.Equal(8, seen.First(s => s.Now == 310).ServingLeft);
        Assert.True(seen.Max(s => s.Now) <= InnSimulator.SessionSeconds + InnState.ServingAfterCloseSeconds);
    }

    /// <summary>
    /// Owner, 2026-10-19, in the replay: at 285 s table 1's 果味沙拉 (30 s) can still be out in the
    /// 33 s the inn serves, so it waits and is served after 00:00; at 295 s table 2's 坛子肉 can't
    /// (behind the salad: 47 s), so it gets a 肉包子 cooked ahead, a wrong dish, at once.
    /// </summary>
    [Fact]
    public void In_the_replay_the_last_call_is_per_table_and_counts_the_18_seconds()
    {
        var menu = new[] { new MenuDish(Dish("肉包子"), null) };
        var kitchens = Kitchens(1);

        var run = new InnSimulator(kitchens, menu).Run([new InnOrder(285, 1, [Dish("果味沙拉")]), new InnOrder(295, 2, [Dish("坛子肉")])],
            InnSimulator.Following(new DispatchAdvisor(kitchens)));

        Assert.InRange(run.Orders[0].ServedAt!.Value, 314, 318);
        Assert.InRange(run.Orders[1].ServedAt!.Value, 295, 296);
    }

    /// <summary>The counter never holds more than eight, following the advice (cooking ahead stops at its room).</summary>
    [Fact]
    public void Following_the_advice_never_overfills_the_counter()
    {
        var kitchens = Kitchens();
        var advisor = new DispatchAdvisor(kitchens);
        var follow = InnSimulator.Following(advisor);
        int most = 0;
        var inn = new InnSimulator(kitchens, RecordedMenu);

        inn.Run(RecordedOrderTimes(2), sim =>
        {
            int spare = sim.Counter.Dishes.Count + sim.Queues.Sum(q => q.Dishes.Count) - sim.Outstanding.Count;
            most = Math.Max(most, sim.Counter.Dishes.Count);
            Assert.True(spare <= InnCounter.Capacity, $"{spare} dishes cooked ahead at {sim.Now} s");
            return follow(sim);
        }, servingAfterClose: LongRun);

        Assert.InRange(most, 1, InnCounter.Capacity);
    }

    /// <summary>The six dishes of the owner's live session (2026-10-19, theme 海陆宴), as its 派单 panel listed them, unlimited.</summary>
    internal static IReadOnlyList<MenuDish> LiveMenu { get; } =
        [.. new[] { "金丝小贝", "坛子肉", "麻辣香锅", "茄果鳞肉汤", "“勺里游”", "白瓜肉丸汤" }.Select(zh => new MenuDish(Dish(zh), null))];

    /// <summary>
    /// The live session's kitchens: 1 = 秋栗 + 昼雪 (Akekuri, Snowshine), 3 = 弭弗 + 艾维文娜 (Mi_Fu,
    /// Avywenna), both read off its 派单 panel. Kitchen 2's chefs never showed in the frames: a cook
    /// with no bonus stands in for them.
    /// </summary>
    internal static InnKitchens LiveKitchens() => new(InnTestData.Inn,
        [InnTestData.Kitchen(1, "Akekuri", "Snowshine"), InnTestData.PlainKitchen(2), InnTestData.Kitchen(3, "Mi_Fu", "Avywenna")], Rating, 0);

    /// <summary>The live kitchens' cook times are those its 派单 panel showed (frames at 4:48 and 4:38).</summary>
    [Theory]
    [InlineData(1, "金丝小贝", 35)]
    [InlineData(1, "坛子肉", 35)]
    [InlineData(1, "麻辣香锅", 34)]
    [InlineData(1, "茄果鳞肉汤", 40)]
    [InlineData(1, "“勺里游”", 34)]
    [InlineData(1, "白瓜肉丸汤", 39)]
    [InlineData(3, "金丝小贝", 31)]
    [InlineData(3, "坛子肉", 28)]
    [InlineData(3, "麻辣香锅", 28)]
    [InlineData(3, "茄果鳞肉汤", 40)]
    [InlineData(3, "“勺里游”", 28)]
    [InlineData(3, "白瓜肉丸汤", 40)]
    public void The_live_kitchens_cook_as_the_panel_showed(int kitchen, string dish, int seconds)
    {
        Assert.Equal(seconds, LiveKitchens().CookSeconds(kitchen, Dish(dish)));
    }

    /// <summary>
    /// Owner, 2026-10-19: "it does not keep the kitchen busy". Over the recorded order times (dishes
    /// drawn at random, three seeds), the seconds kitchens sit empty while the timer runs, following
    /// the advice: the recording's kitchens and the live session's, with a player acting at once, every
    /// 10 s and every 20 s. Average waits are printed beside, for the report: the rule that decides
    /// where a dish goes is the owner's (out first), not tuned to them.
    /// </summary>
    private static readonly double[] ActEvery = [0, 10, 20];
    private const int Seeds = 20;

    [Fact]
    public void Following_the_advice_keeps_the_kitchens_busy()
    {
        double Idle(InnKitchens kitchens, IReadOnlyList<MenuDish> menu, int seed, double actEvery, out double waited)
        {
            var run = new InnSimulator(kitchens, menu).Run(RecordedOrderTimes(seed, menu: menu), InnSimulator.Following(new DispatchAdvisor(kitchens), actEvery: actEvery), servingAfterClose: LongRun);
            waited = run.Orders.Average(o => o.Waited ?? 999);
            return run.IdleKitchenSeconds;
        }
        double total = 0;
        foreach (var (name, kitchens, menu) in new[] { ("recorded", Kitchens(), RecordedMenu), ("live", LiveKitchens(), LiveMenu) })
        {
            foreach (double every in ActEvery)
            {
                double idle = 0, waited = 0;
                for (int seed = 1; seed <= Seeds; seed++)
                {
                    idle += Idle(kitchens, menu, seed, every, out double wait);
                    waited += wait;
                }
                total += idle;
                output.WriteLine($"{name} kitchens, acting every {every} s, {Seeds} seeds: idle {idle:F1} kitchen-seconds in all, average wait {waited / Seeds:F2} s");
            }
        }
        output.WriteLine($"total idle {total:F1} kitchen-seconds");
        // Before dishes cooked ahead went where they're out first: 193.0 (all of it the recorded
        // kitchens with a player acting every 20 s); after: 104.0.
        Assert.True(total <= 104, $"{total:F1} kitchen-seconds idle");
    }

    // ---- A session played from its guests ----

    private static IReadOnlyList<IReadOnlyList<Dish>> Guests(int count, string zh) => [.. Enumerable.Range(0, count).Select(_ => (IReadOnlyList<Dish>)[Dish(zh)])];

    /// <summary>
    /// Guests come on the clock (a guest every 12 s from 7 s, test_yuanyuan1): with the quickest dish
    /// and every kitchen cooking, the session still serves about 25 orders, not as many as the
    /// kitchens could cook. The first order comes 16 s in, as recorded.
    /// </summary>
    [Fact]
    public void Guests_come_on_their_clock_however_fast_the_kitchens_cook()
    {
        var kitchens = Kitchens(2);
        var menu = new[] { new MenuDish(Dish("肉包子"), null) };

        var run = new InnSimulator(kitchens, menu).RunGuests(new InnGuests(InnState.MaxTables), Guests(60, "肉包子"),
            InnSimulator.Following(new DispatchAdvisor(kitchens)));

        Assert.Equal(16, run.Orders[0].Order.At, 0);
        Assert.Equal((int)InnGuests.Arrivals, run.Orders.Count);
        Assert.InRange(run.Served, (int)InnGuests.Arrivals - 1, (int)InnGuests.Arrivals);
        Assert.All(run.Delivered, d => Assert.Contains(d.Kitchen, new[] { 1, 2 }));
    }

    /// <summary>
    /// A slow menu keeps tables waiting: with one table, one kitchen and a 40 s soup, a guest sits
    /// only when the last one has eaten and paid, so far fewer are seated than the clock brings.
    /// </summary>
    [Fact]
    public void A_table_waiting_for_its_food_seats_no_one_else()
    {
        var kitchens = Kitchens(1);
        var menu = new[] { new MenuDish(Dish("酸辣汤"), null) };

        var run = new InnSimulator(kitchens, menu).RunGuests(new InnGuests(1), Guests(60, "酸辣汤"), InnSimulator.Following(new DispatchAdvisor(kitchens)));

        // Each turn at least: ordering 9 s + eating 12.5 + paying 9 + the next guest 4.5 (the soup cooked ahead).
        Assert.InRange(run.Orders.Count, 2, (int)(InnSimulator.SessionSeconds / InnGuests.TableTurnSeconds) + 1);
        Assert.True(run.Orders.Count < InnGuests.Arrivals / 2);
        Assert.All(run.Orders.Zip(run.Orders.Skip(1)), pair => Assert.True(pair.Second.Order.At - pair.First.ServedAt!.Value >= InnGuests.EatingSeconds + InnGuests.PayingSeconds));
    }
}
