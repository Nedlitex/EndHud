using EndHud.Vision.Inn;
using Xunit;
using Xunit.Abstractions;
using static EndHud.Tests.InnTestKit;

namespace EndHud.Tests;

/// <summary>
/// The session's calls held steady (owner, live 2026-10-20: the per-kitchen suggestions "keep changing during the
/// session", hard to follow): <see cref="InnCallHold"/> through <see cref="InnSession.Advice"/>. A call stays until
/// it's done, no longer valid, or clearly beaten; a real change (a new order, the dish queued) shows at once.
/// </summary>
public class InnCallTests(ITestOutputHelper output)
{
    // Soups (40 s), a side (35 s), a dessert (25 s) and an entree (15 s); and a drink.
    private const string A = "melon-meatball-soup", B = "fin-mato-soup", C = "mala-xiang-guo", D = "sweetbeans-on-snow", E = "meat-baozi";
    private const string Juice = "bottled-duskberry-juice";
    private static readonly string[] Menu = [A, B, C, D, E, Juice];
    private static readonly string[] Dishes = [A, B, C, D, E];

    private static InnSession Prepared()
    {
        var session = NewSession();
        session.Update(SetupScreen(0, 1, 2));
        session.Update(MenuScreen(1, Menu));
        return session;
    }

    private static string Keys(IEnumerable<InnCall> calls) => string.Join(" ", calls.Select(c => c.Key));

    /// <summary>What the strip put first for each kitchen before calls were held: the advice's first swap or dish, frame by frame.</summary>
    private static string RawFirsts(InnAdvice advice) => string.Join(" ", advice.Dispatch!.Kitchens.Select(k =>
        k.Replaces.FirstOrDefault() is { } s ? $"{k.Kitchen}:swap:{s.Put.Id}:{s.Table}"
        : k.Queue.FirstOrDefault() is { } q ? $"{k.Kitchen}:{q.Dish.Id}:{q.Table}" : $"{k.Kitchen}:-"));

    /// <summary>
    /// Ten seconds of a session read four times a second: both kitchens cooking one dish with about as long left,
    /// so an order would come out about as soon in either; the dispatch panel shows one kitchen then the other, its
    /// seconds left read a little off each time (±1.5 s); table 3's card can't be read on every fifth frame.
    /// </summary>
    private static (int Raw, int Held, InnSession Session, double At) Jittery()
    {
        var session = Prepared();
        string? lastRaw = null, lastHeld = null;
        int raw = 0, held = 0;
        double t = 10;
        for (int frame = 0; frame < 40; frame++, t += 0.25)
        {
            double jitter = frame % 2 == 0 ? 1.5 : -1.5;
            int kitchen = frame % 2 + 1;
            var panel = Panel(kitchen, 2, Dishes, (kitchen == 1 ? B : C, (int?)Math.Round(30 - (t - 10) + (kitchen == 1 ? jitter : -jitter))));
            var tables = frame % 5 == 4 ? TablesAsRead(Empty(1), Empty(2), null, Empty(4), Empty(5), Empty(6)) : Tables(Order(3, A));
            session.Update(Session(t, left: 280 - (t - 10), tables: tables, panel: panel));
            var advice = session.Advice(S(t))!;
            string r = RawFirsts(advice), h = Keys(advice.Calls);
            if (lastRaw is not null && r != lastRaw) raw++;
            if (lastHeld is not null && h != lastHeld) held++;
            (lastRaw, lastHeld) = (r, h);
        }
        return (raw, held, session, t);
    }

    [Fact]
    public void Jittery_readings_leave_the_calls_on_screen_as_they_are()
    {
        var (raw, held, session, at) = Jittery();
        output.WriteLine($"10 s of jittery readings (40 frames): the advice's first suggestion per kitchen changed {raw} times, the calls {held} times");

        // Until 2026-10-21 the advice was worked out afresh each frame and its first suggestion per kitchen changed 19 times
        // on this script (the calls held it steady after); now the advice itself keeps the calls on screen
        // (DispatchAdvisor.Advise with KeptCall), so neither changes.
        Assert.Equal(0, raw);
        Assert.Equal(0, held);
        var calls = session.Advice(S(at))!.Calls;
        Assert.Single(calls, c => c is { Kind: InnCallKind.Queue, Table: 3 } && c.Dish!.Id == A); // the order, called in one kitchen only
    }

    [Fact]
    public void A_new_order_is_called_at_once()
    {
        var (_, _, session, t) = Jittery();
        var before = session.Advice(S(t))!.Calls;

        session.Update(Session(t, left: 270, tables: Tables(Order(3, A), Order(5, E)), panel: Panel(1, 2, Dishes, (B, 20))));
        var after = session.Advice(S(t))!.Calls;

        Assert.DoesNotContain(before, c => c.Table == 5);
        Assert.Contains(after, c => c is { Kind: InnCallKind.Queue, Table: 5 } && c.Dish!.Id == E);
    }

    [Fact]
    public void A_call_whose_dish_was_queued_is_done_at_once()
    {
        var (_, _, session, t) = Jittery();
        var call = session.Advice(S(t))!.Calls.Single(c => c.Table == 3);
        int kitchen = call.Kitchen!.Value;
        string cooking = kitchen == 1 ? B : C;

        // The player queues the soup in that kitchen: the panel shows it behind the dish cooking.
        session.Update(Session(t + 0.25, left: 270, tables: Tables(Order(3, A)), panel: Panel(kitchen, 2, Dishes, (cooking, 19), (A, null))));
        var calls = session.Advice(S(t + 0.25))!.Calls;

        Assert.DoesNotContain(calls, c => c.Key == call.Key);
        Assert.DoesNotContain(calls, c => c.Table == 3); // queued: nothing left to call for it
    }

    /// <summary>
    /// A new order the advice puts behind another order in kitchen 1 (one call a kitchen: it can't show there yet) shows at
    /// once in kitchen 2, which shows only a dish to cook ahead, when kitchen 2 has it out no more than
    /// <see cref="InnCallHold.ClearlyEarlierSeconds"/> later: 40 s against 38 s. Before, only a kitchen with no call took
    /// it, and on test_yuanyuan4 (230.7 s) the order waited 5 s for a call.
    /// </summary>
    [Fact]
    public void A_kitchen_showing_a_dish_to_cook_ahead_takes_a_new_order_put_behind_another_elsewhere()
    {
        var hold = new InnCallHold();
        var view = new InnKitchenView((_, _) => 0, _ => true, (kitchen, _) => kitchen == 2 ? 40 : 19);
        static KitchenAdvice Kitchen(int number, params QueueAdvice[] queue) => new(number, queue, []);

        var before = hold.Update(new DispatchAdvice([Kitchen(1, new QueueAdvice(Dish(A), 5, 19)), Kitchen(2, new QueueAdvice(Dish(B), null, 50))], [], [], []),
            null, view, S(10));
        var after = hold.Update(new DispatchAdvice([Kitchen(1, new QueueAdvice(Dish(A), 5, 19), new QueueAdvice(Dish(A), 1, 38)), Kitchen(2, new QueueAdvice(Dish(B), null, 50))],
            [], [], []), null, view, S(10.3));

        Assert.Equal($"Queue:1:{A}:5: CookAhead:2:{B}::", Keys(before));
        Assert.Equal($"Queue:1:{A}:5: Queue:2:{A}:1:", Keys(after));
    }

    /// <summary>A dish to cook ahead stays on screen while it's still to be cooked ahead there, whichever near-equal dish the advice puts first.</summary>
    [Fact]
    public void A_dish_to_cook_ahead_stays_until_queued_and_an_order_replaces_it()
    {
        var session = Prepared();
        session.Update(Session(10, panel: Panel(1, 2, Dishes)));
        var first = session.Advice(S(10))!.Calls.Single(c => c.Kitchen == 1);
        Assert.Equal(InnCallKind.CookAhead, first.Kind);

        for (double t = 10.25; t < 14; t += 0.25)
        {
            session.Update(Session(t, panel: Panel(1, 2, Dishes)));
            Assert.Contains(session.Advice(S(t))!.Calls, c => c.Key == first.Key);
        }

        // An order comes in that kitchen 1 is to cook: it replaces the dish to cook ahead at once.
        session.Update(Session(14, tables: Tables(Order(2, E)), panel: Panel(1, 2, Dishes)));
        var calls = session.Advice(S(14))!.Calls;
        Assert.Contains(calls, c => c is { Kind: InnCallKind.Queue, Table: 2 });
    }

    /// <summary>
    /// Calls stack in a fixed order: the kitchens by number, then Full Blaze, then the last call by table (owner, 2026-10-21:
    /// "the recommendation should stay steady"; Full Blaze coming or going above the kitchens moved every kitchen's banner).
    /// </summary>
    [Fact]
    public void Calls_stack_by_kitchen_number_then_full_blaze()
    {
        var session = Prepared();
        session.Update(Session(10, tables: Tables(Order(1, A), Order(2, B)), fire: new FireMeter(90, 4, false)));
        var calls = session.Advice(S(10))!.Calls;

        var kitchens = calls.Where(c => c.Kitchen is not null).Select(c => c.Kitchen!.Value).ToList();
        Assert.Equal(kitchens.Order(), kitchens);
        Assert.Equal(kitchens.Distinct().Count(), kitchens.Count);
        int blaze = calls.ToList().FindIndex(c => c.Kind is InnCallKind.BlazeNow or InnCallKind.BlazeGetReady);
        Assert.Equal(calls.Count - 1, blaze);
    }

    /// <summary>
    /// Each kitchen keeps its row for the session (<see cref="InnBannerRows"/>): a kitchen's banner going leaves its row
    /// empty, so the banners below don't move; Full Blaze and the last calls come below the kitchens, so they move none.
    /// </summary>
    [Fact]
    public void A_kitchens_banner_keeps_its_row_whatever_comes_and_goes()
    {
        var rows = new InnBannerRows();
        InnCall Ahead(int kitchen, string dish) => new(InnCallKind.CookAhead, kitchen, Dish(dish));
        int RowOf(IReadOnlyList<InnBannerRow> shown, int kitchen) => shown.ToList().FindIndex(r => r.Banner?.Key.Contains($":{kitchen}:", StringComparison.Ordinal) == true);

        int[] cooking = [1, 2, 3];
        var all = rows.Rows(cooking, [Ahead(1, A), Ahead(2, B), Ahead(3, C)], Text);
        Assert.Equal([0, 1, 2], new[] { RowOf(all, 1), RowOf(all, 2), RowOf(all, 3) });

        // Kitchen 1's banner goes, Full Blaze comes: kitchens 2 and 3 stay in rows 1 and 2, kitchen 1's row stays, empty.
        var after = rows.Rows(cooking, [Ahead(2, B), Ahead(3, C), new InnCall(InnCallKind.BlazeNow)], Text);
        Assert.Equal([1, 2], new[] { RowOf(after, 2), RowOf(after, 3) });
        Assert.Null(after[0].Banner);
        Assert.Equal("BlazeNow::::", after[3].Banner!.Key);

        // A new session starts with no rows; a kitchen that cooks has its row from the start, so kitchen 3's first banner
        // comes in under kitchens 1 and 2's empty rows, and Full Blaze below it doesn't move when kitchen 1's first comes.
        rows.Clear();
        var three = rows.Rows(cooking, [Ahead(3, C), new InnCall(InnCallKind.BlazeNow)], Text);
        Assert.Equal((2, 3), (RowOf(three, 3), three.ToList().FindIndex(r => r.Banner?.Key == "BlazeNow::::")));
        var one = rows.Rows(cooking, [Ahead(1, A), Ahead(3, C), new InnCall(InnCallKind.BlazeNow)], Text);
        Assert.Equal((0, 2, 3), (RowOf(one, 1), RowOf(one, 3), one.ToList().FindIndex(r => r.Banner?.Key == "BlazeNow::::")));

        // A table given two of a dish at the last call: one banner.
        var twice = new InnCall(InnCallKind.LastCall, Dish: Dish(A), Table: 2);
        Assert.Single(rows.Rows(cooking, [twice, twice], Text), r => r.Banner?.Key == twice.Key);
    }
    /// <summary>
    /// The stack as the overlay draws it (<see cref="InnBannerStack"/>, on a WPF panel): a banner whose call stands is the same
    /// element on every update (never drawn or slid in again), a kitchen's row keeps its place in the stack while its banner
    /// changes or goes, and the banners below it stay where they are.
    /// </summary>
    [Fact]
    public void The_drawn_stack_keeps_standing_banners_and_every_kitchens_place()
    {
        OnSta(() =>
        {
            var panel = new System.Windows.Controls.StackPanel();
            var stack = new InnBannerStack(panel, model => InnCallView.Banner(model, _ => null));
            var rows = new InnBannerRows();
            int[] cooking = [1, 2, 3];
            InnCall Ahead(int kitchen, string dish) => new(InnCallKind.CookAhead, kitchen, Dish(dish));
            void Show(params InnCall[] calls) => stack.Update(rows.Rows(cooking, calls, Text));

            Show(Ahead(1, A), Ahead(2, B), Ahead(3, C));
            var one = stack.BannerIn("kitchen:1");
            var two = stack.BannerIn("kitchen:2");
            var three = stack.BannerIn("kitchen:3");
            var place = new[] { 1, 2, 3 }.Select(k => panel.Children.IndexOf(stack.HolderOf($"kitchen:{k}")!)).ToList();

            // The same calls again: the same elements.
            Show(Ahead(1, A), Ahead(2, B), Ahead(3, C));
            Assert.Same(one, stack.BannerIn("kitchen:1"));
            Assert.Same(two, stack.BannerIn("kitchen:2"));

            // Kitchen 1's call goes and Full Blaze comes: kitchens 2 and 3 are the same elements in the same places.
            Show(Ahead(2, B), Ahead(3, C), new InnCall(InnCallKind.BlazeNow));
            Assert.Null(stack.BannerIn("kitchen:1"));
            Assert.Same(two, stack.BannerIn("kitchen:2"));
            Assert.Same(three, stack.BannerIn("kitchen:3"));
            Assert.Equal(place, new[] { 1, 2, 3 }.Select(k => panel.Children.IndexOf(stack.HolderOf($"kitchen:{k}")!)));

            // Kitchen 2's call changes: a new element in the same row; kitchen 3 untouched.
            Show(Ahead(2, D), Ahead(3, C), new InnCall(InnCallKind.BlazeNow));
            Assert.NotSame(two, stack.BannerIn("kitchen:2"));
            Assert.Same(three, stack.BannerIn("kitchen:3"));
            Assert.Equal(place, new[] { 1, 2, 3 }.Select(k => panel.Children.IndexOf(stack.HolderOf($"kitchen:{k}")!)));
            return 0;
        });
    }

    private static T OnSta<T>(Func<T> work)
    {
        T result = default!;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { result = work(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw failure;
        return result;
    }
}
