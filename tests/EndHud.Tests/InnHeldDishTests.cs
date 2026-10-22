using EndHud.Vision.Inn;
using Xunit;
using static EndHud.Tests.InnTestKit;

namespace EndHud.Tests;

/// <summary>
/// The dishes in the player's hand (the tray, <see cref="TrayReader"/>) in the session (owner's live session 2026-10-21,
/// test_yuanyuan5: at 258 s the player held 至臻鳞肉卷 and "勺里游", and the call said "厨房三：至臻鳞肉卷 / 桌4 点的", cooking another
/// for table 4 although one was in hand).
/// </summary>
public class InnHeldDishTests
{
    // Soups (40 s), a side (35 s), a dessert (25 s) and an entree (15 s); and three drinks.
    private const string A = "melon-meatball-soup", B = "fin-mato-soup", C = "mala-xiang-guo", D = "sweetbeans-on-snow", E = "meat-baozi";
    private const string Juice = "bottled-duskberry-juice", Tea = "pre-tide-lungjing", Milk = "bottled-milk-a";
    private static readonly string[] Menu = [A, B, C, D, E, Juice, Tea, Milk];

    private static InnSession Prepared(params int[] kitchens)
    {
        var session = NewSession();
        session.Update(SetupScreen(0, kitchens.Length == 0 ? [1, 2, 3] : kitchens));
        session.Update(MenuScreen(1, Menu));
        return session;
    }

    private static IReadOnlyList<HeldDish?> Tray(params string[] held) =>
        [.. held.Select(id => (HeldDish?)new HeldDish(id, Choice: false)), .. Enumerable.Repeat<HeldDish?>(null, TrayReader.Slots - held.Length)];

    private static InnReading Frame(double at, IReadOnlyList<TableCard?> tables, IReadOnlyList<HeldDish?>? tray, DispatchPanel? panel = null) =>
        Session(at, tables: tables, panel: panel) with { Tray = tray };

    private static bool Sent(InnAdvice advice, string dish, int table) =>
        advice.Dispatch!.Kitchens.Any(k => k.Queue.Any(q => q.Dish.Id == dish && q.Table == table) || k.Replaces.Any(r => r.Put.Id == dish && r.Table == table));

    private static bool Called(InnAdvice advice, string dish, int table) =>
        advice.Calls.Any(c => c.Kind is InnCallKind.Queue or InnCallKind.Swap && c.Dish?.Id == dish && c.Table == table);

    [Fact]
    public void A_dish_in_hand_covers_an_order_of_it_no_kitchen_is_told_to_cook_another()
    {
        var session = Prepared(1, 2, 3);
        var tables = Tables(Order(4, A, Juice));
        session.Update(Frame(10, tables, Tray(A, D)));
        session.Update(Frame(10.3, tables, Tray(A, D)));

        var advice = session.Advice(S(10.3))!;

        Assert.False(Sent(advice, A, 4), "table 4's soup is in hand, yet the advice sends it to a kitchen");
        Assert.False(Called(advice, A, 4), "table 4's soup is in hand, yet a call says to queue it");
    }

    [Fact]
    public void A_tray_read_differently_on_one_frame_only_is_not_taken()
    {
        var session = Prepared(1, 2, 3);
        var tables = Tables(Order(4, A));
        session.Update(Frame(10, tables, Tray(A)));

        Assert.True(Sent(session.Advice(S(10))!, A, 4));
    }

    [Fact]
    public void A_tray_with_a_picture_not_told_apart_says_nothing()
    {
        var session = Prepared(1, 2, 3);
        var tables = Tables(Order(4, A));
        IReadOnlyList<HeldDish?> unclear = [new HeldDish(A, false), new HeldDish(null, false), null, null, null];
        session.Update(Frame(10, tables, unclear));
        session.Update(Frame(10.3, tables, unclear));

        Assert.True(Sent(session.Advice(S(10.3))!, A, 4));
    }

    /// <summary>
    /// A dish picked up was the counter's: it isn't counted twice. Two soups out for two orders, one picked up: one in hand
    /// and one on the counter cover the two orders, and served from the hand, the counter's still covers the other, though
    /// the card turned served a frame before the tray dropped it.
    /// </summary>
    [Fact]
    public void A_dish_picked_up_leaves_the_counter_and_served_from_hand_leaves_the_counter_alone()
    {
        var session = Prepared(1);
        var tables = Tables(Order(2, A), Order(5, A));
        // Two soups queued for them, read on the panel (two frames: open), then out.
        session.Update(Session(9.7, tables: tables, panel: Panel(1, 1, Menu[..5], (A, 5), (A, null))));
        session.Update(Session(10, tables: tables, panel: Panel(1, 1, Menu[..5], (A, 5), (A, null))));
        session.Update(Frame(60, tables, Tray()));
        Assert.Contains($"{A}x2", session.CounterSignature());

        // One picked up.
        session.Update(Frame(61, tables, Tray(A)));
        session.Update(Frame(61.3, tables, Tray(A)));
        Assert.Equal($"{A}x1,{A}x1@hand", session.CounterSignature());
        Assert.False(Sent(session.Advice(S(61.3))!, A, 2));
        Assert.False(Sent(session.Advice(S(61.3))!, A, 5));

        // Served to table 2: the card first, the tray a frame later.
        var served = Tables(Order(2, new OrderItem(A, Served: true)), Order(5, A));
        session.Update(Frame(62, served, Tray(A)));
        session.Update(Frame(62.3, served, Tray()));
        session.Update(Frame(62.6, served, Tray()));
        session.Update(Frame(70, served, Tray()));

        Assert.Equal($"{A}x1", session.CounterSignature());
        Assert.False(Sent(session.Advice(S(70))!, A, 5), "table 5's soup is on the counter");
    }

    /// <summary>
    /// The tray drawn a few frames after the card: served on the card, still in the tray for three more readings. It is the
    /// same dish, not a second one picked up: the counter's soup stays, and covers table 5.
    /// </summary>
    [Fact]
    public void A_tray_a_few_frames_late_after_serving_takes_nothing_off_the_counter()
    {
        var session = Prepared(1);
        var tables = Tables(Order(2, A), Order(5, A));
        session.Update(Session(9.7, tables: tables, panel: Panel(1, 1, Menu[..5], (A, 5), (A, null))));
        session.Update(Session(10, tables: tables, panel: Panel(1, 1, Menu[..5], (A, 5), (A, null))));
        session.Update(Frame(60, tables, Tray()));
        session.Update(Frame(61, tables, Tray(A)));
        session.Update(Frame(61.3, tables, Tray(A)));

        var served = Tables(Order(2, new OrderItem(A, Served: true)), Order(5, A));
        foreach (double t in new[] { 62, 62.3, 62.6 }) session.Update(Frame(t, served, Tray(A)));
        foreach (double t in new[] { 62.9, 63.2, 70 }) session.Update(Frame(t, served, Tray()));

        Assert.Equal($"{A}x1", session.CounterSignature());
        Assert.False(Sent(session.Advice(S(70))!, A, 5), "table 5's soup is on the counter");
    }

    [Fact]
    public void A_dish_put_down_with_no_table_served_is_back_on_the_counter()
    {
        var session = Prepared(1);
        var tables = Tables(Order(2, A));
        session.Update(Frame(10, tables, Tray(A)));
        session.Update(Frame(10.3, tables, Tray(A)));
        Assert.Equal($"{A}x1@hand", session.CounterSignature());

        session.Update(Frame(11, tables, Tray()));
        session.Update(Frame(11.3, tables, Tray()));
        session.Update(Frame(11.3 + InnSession.HandSettlesAfter.TotalSeconds, tables, Tray()));

        Assert.Equal($"{A}x1", session.CounterSignature());
        Assert.False(Sent(session.Advice(S(15))!, A, 2), "table 2's soup was put back out for it");
    }

    [Fact]
    public void Drinks_in_hand_are_not_dishes()
    {
        var session = Prepared(1);
        var tables = Tables(Order(2, A, Juice));
        session.Update(Frame(10, tables, Tray(Juice)));
        session.Update(Frame(10.3, tables, Tray(Juice)));

        Assert.Equal("", session.CounterSignature());
        Assert.True(Sent(session.Advice(S(10.3))!, A, 2));
    }
}
