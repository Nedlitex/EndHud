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
        output.WriteLine($"10 s of jittery readings (40 frames): the first suggestion per kitchen changed {raw} times before (worked out afresh each frame), the calls {held} times after (held)");

        Assert.True(raw > 0, "the script should make the advice flip, or it proves nothing");
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

    /// <summary>Calls stack in a fixed order: Full Blaze first, then the kitchens by number, then the last call by table.</summary>
    [Fact]
    public void Calls_stack_by_kitchen_number()
    {
        var session = Prepared();
        session.Update(Session(10, tables: Tables(Order(1, A), Order(2, B)), fire: new FireMeter(90, 4, false)));
        var calls = session.Advice(S(10))!.Calls;

        var kitchens = calls.Where(c => c.Kitchen is not null).Select(c => c.Kitchen!.Value).ToList();
        Assert.Equal(kitchens.Order(), kitchens);
        Assert.Equal(kitchens.Distinct().Count(), kitchens.Count);
        int blaze = calls.ToList().FindIndex(c => c.Kind is InnCallKind.BlazeNow or InnCallKind.BlazeGetReady);
        Assert.True(blaze <= 0, "Full Blaze goes first");
    }
}
