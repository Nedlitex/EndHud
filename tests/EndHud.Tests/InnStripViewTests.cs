using EndHud.Vision.Inn;
using Xunit;
using static EndHud.Tests.InnTestKit;

namespace EndHud.Tests;

/// <summary>
/// What the inn strip says (<see cref="InnStripView"/>), in the app's own zh-CN strings: one line per
/// kitchen with the dishes to queue (pictures beside every name), the Full Blaze reminder above
/// them, a swap, what can't be read, and the gourmand card.
/// </summary>
public class InnStripViewTests
{
    private const string A = "melon-meatball-soup", B = "fin-mato-soup", C = "mala-xiang-guo", D = "sweetbeans-on-snow", E = "meat-baozi";
    private const string Juice = "bottled-duskberry-juice", Tea = "pre-tide-lungjing", Milk = "bottled-milk-a";
    private static readonly string[] Menu = [A, B, C, D, E, Juice, Tea, Milk];

    private static InnStripModel Strip(InnAdvice advice, string? problem = null) => InnStripView.Build(advice, Text, problem);

    private static string Kitchen(int n) => Text($"Inn.Kitchen.{n}");

    private static string QueueLabel(int n) => Format("Inn.Queue", Kitchen(n));

    private static InnSession Prepared(params int[] kitchens)
    {
        var session = NewSession();
        session.Update(SetupScreen(0, kitchens.Length == 0 ? [1, 2, 3] : kitchens));
        session.Update(MenuScreen(1, Menu));
        return session;
    }

    private static InnAdvice Advice(DispatchAdvice dispatch, FullBlazeAdvice? blaze = null, IReadOnlyList<InnGourmandNote>? gourmands = null,
        IReadOnlyList<int>? unreadable = null, IReadOnlySet<int>? exact = null) =>
        new(dispatch, null, blaze, false, gourmands ?? [], unreadable ?? [], exact ?? new HashSet<int>(), false);

    private static DispatchAdvice Kitchens(params KitchenAdvice[] kitchens) => new(kitchens, [], [], []);

    /// <summary>Every dish or drink named on the strip comes with its picture (C14).</summary>
    private static void AssertPictureBesideEveryName(InnStripModel strip)
    {
        var names = Inn.Dishes.Select(d => (d.Zh, d.Id)).Concat(Inn.Drinks.Select(d => (d.Zh, d.Id))).ToDictionary(x => x.Zh, x => x.Id);
        foreach (var segment in strip.Lines.SelectMany(l => l.Segments))
            if (names.TryGetValue(segment.Text, out var id)) Assert.Equal(id, segment.PictureId);
    }

    [Fact]
    public void At_the_sessions_start_every_kitchens_line_is_already_filled_with_dishes_to_cook_ahead()
    {
        var session = Prepared();
        session.Update(Session(10, left: 300));

        var strip = Strip(session.Advice(S(10))!);

        Assert.Equal([QueueLabel(1), QueueLabel(2), QueueLabel(3)], strip.Lines.Select(l => l.Segments[0].Text));
        foreach (var line in strip.Lines)
        {
            var dishes = line.Segments.Select((s, i) => (s, i)).Where(x => x.s.PictureId is not null).ToList();
            Assert.NotEmpty(dishes);
            Assert.All(dishes, d => Assert.Equal(Text("Inn.CookAhead"), line.Segments[d.i - 1].Text));
        }
        AssertPictureBesideEveryName(strip);
    }

    [Fact]
    public void An_ordered_dish_is_listed_for_its_kitchen_and_an_idle_kitchen_cooks_ahead_marked_as_such()
    {
        var session = Prepared(1, 2);
        session.Update(Session(10, tables: Tables(Order(3, A, Juice))));

        var strip = Strip(session.Advice(S(10))!);

        Assert.Equal(QueueLabel(1) + Dish(A).Zh, strip.Lines[0].Text);
        Assert.Equal([A], strip.Lines[0].Segments.Select(s => s.PictureId).OfType<string>());
        Assert.StartsWith(QueueLabel(2) + Text("Inn.CookAhead"), strip.Lines[1].Text);
        Assert.Equal(2, strip.Lines.Count);
        AssertPictureBesideEveryName(strip);
    }

    [Fact]
    public void Two_dishes_for_one_kitchen_are_listed_in_order()
    {
        var strip = Strip(Advice(Kitchens(new KitchenAdvice(1, [new QueueAdvice(Dish(B), 3, 40), new QueueAdvice(Dish(A), 5, 80)], []))));

        Assert.Equal(QueueLabel(1) + Dish(B).Zh + Text("Inn.Between") + Dish(A).Zh, strip.Lines[0].Text);
        Assert.Equal([B, A], strip.Lines[0].Segments.Select(s => s.PictureId).OfType<string>());
    }

    [Fact]
    public void A_kitchen_with_nothing_to_queue_says_so_with_a_dash()
    {
        var strip = Strip(Advice(Kitchens(new KitchenAdvice(1, [new QueueAdvice(Dish(A), 2, 40)], []), new KitchenAdvice(2, [], []))));

        Assert.Equal(QueueLabel(2) + Text("Inn.Nothing"), strip.Lines[1].Text);
        Assert.Equal("—", Text("Inn.Nothing"));
    }

    [Fact]
    public void A_swap_replaces_the_kitchens_line_and_names_the_dish_to_take_out_and_the_order_to_put_in()
    {
        var swap = new ReplaceAdvice(1, Slot: 1, Remove: Dish(D), Put: Dish(A), Table: 3, ReadyIn: 40, OtherwiseIn: 75);
        var strip = Strip(Advice(Kitchens(new KitchenAdvice(1, [new QueueAdvice(Dish(E), null, 55)], [swap]), new KitchenAdvice(2, [], []))));

        var line = strip.Lines[0];
        Assert.Equal(Format("Inn.Replace", Kitchen(1)) + Text("Inn.CookAhead") + Dish(D).Zh + Text("Inn.ReplaceArrow") + Dish(A).Zh + Format("Inn.ForTable", 3), line.Text);
        Assert.Equal([D, A], line.Segments.Select(s => s.PictureId).OfType<string>());
        Assert.DoesNotContain(strip.Lines, l => l.Text.StartsWith(QueueLabel(1)));
        Assert.Equal(" → ", Text("Inn.ReplaceArrow")); // spaces kept around the arrow
    }

    [Theory]
    [InlineData(FullBlazeAdvice.StartNow, "Inn.Blaze.StartNow")]
    [InlineData(FullBlazeAdvice.GetReady, "Inn.Blaze.GetReady")]
    public void The_full_blaze_reminder_is_one_line_above_the_kitchens_in_the_accent_colour(FullBlazeAdvice blaze, string key)
    {
        var strip = Strip(Advice(Kitchens(new KitchenAdvice(1, [], [])), blaze));

        Assert.Equal(InnLineStyle.Accent, strip.Lines[0].Style);
        Assert.Equal(Text(key), strip.Lines[0].Text);
        Assert.Equal(QueueLabel(1) + Text("Inn.Nothing"), strip.Lines[1].Text);
    }

    [Fact]
    public void The_full_blaze_lines_read_as_the_owner_worded_them()
    {
        // Owner, 2026-10-19: the ready line names the button and its key; "nearly full" stays from fire 80.
        Assert.Equal("开启满堂彩（R）", Strip(Advice(Kitchens(), FullBlazeAdvice.StartNow)).Lines[0].Text);
        Assert.Equal("满堂彩快满了：先把厨房排满", Strip(Advice(Kitchens(), FullBlazeAdvice.GetReady)).Lines[0].Text);
    }

    /// <summary>
    /// The last call (owner, 2026-10-19): once the inn has closed, a line per dish to hand out, above
    /// the kitchens, the right dish first; a wrong one says it still earns money.
    /// </summary>
    [Fact]
    public void At_the_last_call_each_leftover_has_a_line_saying_which_table_to_give_it_to()
    {
        var advice = Advice(new DispatchAdvice([new KitchenAdvice(1, [], [])], [], [],
            [new LeftoverAdvice(Dish(A), 2, Dish(A)), new LeftoverAdvice(Dish(B), 3, Dish(C))]));

        var strip = Strip(advice);

        Assert.Equal(["收尾：把剩下的白瓜肉丸汤 端给桌2", "收尾：把剩下的茄果鳞肉汤 端给桌3（不对也有钱）"], strip.Lines.Take(2).Select(l => l.Text));
        Assert.Equal([A, B], strip.Lines.Take(2).Select(l => l.Segments.Single(s => s.PictureId is not null).PictureId));
        Assert.Equal(QueueLabel(1) + Text("Inn.Nothing"), strip.Lines[2].Text);
    }

    /// <summary>
    /// End to end: a dish cooked ahead comes out, the timer reaches 0:00, and the table still waiting
    /// (for a soup nobody is cooking) is told to take it; the soup isn't sent to a kitchen.
    /// </summary>
    [Fact]
    public void When_the_inn_closes_a_dish_left_on_the_counter_goes_to_a_table_still_waiting()
    {
        var session = Prepared(1);
        session.Update(Session(10, left: 20, tables: Tables(Order(4, A)), panel: Panel(1, 1, Menu[..5], (A, 5), (E, null))));
        // 肉包子 (cooked ahead, 15 s) is out by 30 s; so is table 4's soup, served at once.
        session.Update(Session(30, left: 1, tables: Tables(Order(4, new OrderItem(A, Served: true)))));
        session.Update(Session(32, left: 0, tables: Tables(Order(4, new OrderItem(A, Served: true)), Order(3, B))));

        var strip = Strip(session.Advice(S(32))!);

        Assert.Contains("收尾：把剩下的肉包子 端给桌3（不对也有钱）", strip.Lines.Select(l => l.Text));
        Assert.DoesNotContain(strip.Lines, l => l.Segments.Any(s => s.PictureId == B) && l.Text.StartsWith(QueueLabel(1)));
    }

    [Fact]
    public void While_full_blaze_runs_there_is_no_reminder_and_every_kitchen_stays_full()
    {
        var session = Prepared(1, 2);
        var tables = Tables(Order(1, A));
        session.Update(Session(10, left: 200, tables: tables, fire: new FireMeter(100, 10, Ready: true)));
        Assert.Equal(Text("Inn.Blaze.StartNow"), Strip(session.Advice(S(10))!).Lines[0].Text);

        session.Update(Session(12, left: 198, tables: tables, fire: new FireMeter(0, 10, false)));

        var strip = Strip(session.Advice(S(12))!);
        Assert.DoesNotContain(strip.Lines, l => l.Style == InnLineStyle.Accent);
        Assert.All(strip.Lines, l => Assert.Equal(KitchenQueue.Slots, l.Segments.Count(s => s.PictureId is not null)));
    }

    [Fact]
    public void An_unreadable_order_is_one_line_below_the_kitchens_and_never_guessed()
    {
        var session = Prepared(1, 2);
        session.Update(Session(10, tables: Tables(Order(3, A, null))));

        var strip = Strip(session.Advice(S(10))!);

        Assert.Equal(Format("Inn.Unreadable", 3), strip.Lines[^1].Text);
        Assert.Equal(InnLineStyle.Note, strip.Lines[^1].Style);
        Assert.DoesNotContain(strip.Lines, l => l.Text.Contains(Format("Inn.ForTable", 3)));
    }

    [Fact]
    public void Dishes_no_kitchen_has_room_for_and_dishes_out_of_stock_are_said()
    {
        var advice = Advice(new DispatchAdvice([new KitchenAdvice(1, [], [])], [new OrderedDish(2, Dish(B))], [new OrderedDish(4, Dish(C))], []));

        var strip = Strip(advice);

        Assert.Contains(strip.Lines, l => l.Text == Text("Inn.Waiting") + Dish(B).Zh + Format("Inn.ForTable", 2));
        Assert.Contains(strip.Lines, l => l.Text == Text("Inn.OutOfStock") + Dish(C).Zh + Format("Inn.ForTable", 4));
    }

    [Fact]
    public void One_kitchen_one_line()
    {
        var session = Prepared(1);
        session.Update(Session(10, tables: TablesAsRead(Order(1, A, Tea), Order(2, C))));

        var strip = Strip(session.Advice(S(10))!);

        var line = Assert.Single(strip.Lines);
        Assert.Equal(QueueLabel(1) + Dish(A).Zh + Text("Inn.Between") + Dish(C).Zh, line.Text);
    }

    [Theory]
    [InlineData(true, "Inn.Gap.NoKitchens")]
    [InlineData(false, "Inn.Gap.NoMenu")]
    public void What_is_missing_is_said_in_place_of_the_kitchens(bool noKitchens, string key)
    {
        var strip = Strip(new InnAdvice(null, noKitchens ? InnGap.NoKitchens : InnGap.NoMenu, null, false, [], [], new HashSet<int>(), true));

        Assert.Equal(Text(key), Assert.Single(strip.Lines).Text);
    }

    [Fact]
    public void Without_text_recognition_the_strip_says_what_to_install()
    {
        var strip = Strip(new InnAdvice(null, InnGap.NoKitchens, null, false, [], [], new HashSet<int>(), true), "Inn.Problem.NoTextRecognition");

        // Only that: "open the dispatch panel" would not help, nothing on it can be read.
        Assert.Equal(Text("Inn.Problem.NoTextRecognition"), Assert.Single(strip.Lines).Text);
    }

    // ---- The gourmand card ----

    private static (GourmandRequest Request, GourmandCondition Condition) DishRequestMetBy(params string[] menu) =>
        Inn.Gourmands.SelectMany(g => g.Requests)
            .Select(r => (r, GourmandCondition.Parse(r.When, Inn)))
            .First(x => !x.Item2.ForDrink && menu.Any(id => x.Item2.MetBy(Dish(id))) && Inn.MatchSaid(x.r.Text).FirstOrDefault()?.Request == x.r);

    [Fact]
    public void A_gourmand_card_names_the_table_the_request_in_plain_words_and_what_to_serve_with_pictures()
    {
        var session = Prepared(1, 2);
        var (request, condition) = DishRequestMetBy(A, B, C, D, E);
        session.Update(Session(10, tables: Tables(new TableCard(6, TableState.GourmandOrder, [], [])), bubble: new GourmandBubble(request.Text, [request.Text])));

        var lines = Strip(session.Advice(S(10))!).Lines.SkipWhile(l => l.Style != InnLineStyle.Heading).ToList();

        Assert.Equal(Format("Inn.Gourmand.Title", 6), lines[0].Text);
        Assert.Equal(Format("Inn.Gourmand.Wants", condition.Describe()), lines[1].Text);
        Assert.Equal(Text("Inn.Gourmand.Options"), lines[2].Segments[0].Text);
        var named = lines[2].Segments.Where(s => s.PictureId is not null).ToList();
        Assert.InRange(named.Count, 1, InnStripView.OptionsShown);
        Assert.All(named, s => Assert.True(condition.MetBy(Dish(s.PictureId!))));
        AssertPictureBesideEveryName(new InnStripModel(lines));
    }

    [Fact]
    public void Where_each_option_is_on_the_counter_in_a_kitchen_about_or_exactly_or_at_the_drink_bar()
    {
        var request = new GourmandRequest("said", "x");
        var options = new GourmandOption[]
        {
            new(Dish(A), null, GourmandSource.Counter, null, 0),
            new(Dish(B), null, GourmandSource.Queued, 2, 34.2),
            new(Dish(C), null, GourmandSource.ToCook, 1, 35),
        };
        var drink = new GourmandOption(null, Drink(Tea), GourmandSource.DrinkBar, null, 0);
        var note = new InnGourmandNote(4, new GourmandAdvice([new GourmandRequestAdvice(request, "plain", options), new GourmandRequestAdvice(request, "drink", [drink])], null), null, false);

        var lines = Strip(Advice(Kitchens(new KitchenAdvice(1, [], [])), gourmands: [note], exact: new HashSet<int> { 2 })).Lines;

        string Wrap(string where) => Format("Inn.Where.Wrap", where);
        Assert.Contains(lines, l => l.Text == Text("Inn.Gourmand.Options")
            + Dish(A).Zh + Wrap(Text("Inn.Where.Counter")) + Text("Inn.Between")
            + Dish(B).Zh + Wrap(Format("Inn.Where.Kitchen", Kitchen(2), 35)) + Text("Inn.Between")
            + Dish(C).Zh + Wrap(Format("Inn.Where.ToCookAbout", Kitchen(1), 35)));
        Assert.Contains(lines, l => l.Text == Text("Inn.Gourmand.Options") + Drink(Tea).Zh + Wrap(Text("Inn.Where.DrinkBar")));
    }

    [Fact]
    public void A_request_not_recognised_says_so_with_the_text_read()
    {
        var note = new InnGourmandNote(2, null, "read text", false);

        var lines = Strip(Advice(Kitchens(), gourmands: [note])).Lines;

        Assert.Equal([Format("Inn.Gourmand.Title", 2), Text("Inn.Gourmand.Unknown"), Format("Inn.Gourmand.Read", "read text")], lines.Select(l => l.Text));
        Assert.Equal([InnLineStyle.Heading, InnLineStyle.Note, InnLineStyle.Dim], lines.Select(l => l.Style));
    }

    [Fact]
    public void A_request_nothing_on_the_menu_meets_says_so()
    {
        // A request no dish of a one-dish menu meets.
        var request = Inn.Gourmands.SelectMany(g => g.Requests)
            .First(r => GourmandCondition.Parse(r.When, Inn) is { ForDrink: false } c && !c.MetBy(Dish(E)) && Inn.MatchSaid(r.Text).FirstOrDefault()?.Request == r);
        var lonely = NewSession();
        lonely.Update(SetupScreen(0, 1));
        lonely.Update(MenuScreen(1, E));
        lonely.Update(Session(10, tables: Tables(new TableCard(5, TableState.GourmandOrder, [], [])), bubble: new GourmandBubble(request.Text, [request.Text])));

        var lines = Strip(lonely.Advice(S(10))!).Lines.SkipWhile(l => l.Style != InnLineStyle.Heading).ToList();

        Assert.Equal(Format("Inn.Gourmand.Title", 5), lines[0].Text);
        Assert.Equal(Text("Inn.Gourmand.NoneOnMenu"), lines[2].Text);
    }

    [Fact]
    public void A_drink_request_before_any_drink_of_the_menu_has_been_seen_says_that_instead_of_none()
    {
        var request = new GourmandRequest("said", "x");
        var note = new InnGourmandNote(1, new GourmandAdvice([new GourmandRequestAdvice(request, "plain", [])], null), null, DrinksUnknown: true);

        var lines = Strip(Advice(Kitchens(), gourmands: [note])).Lines;

        Assert.Equal(Text("Inn.Gourmand.NoDrinksSeen"), lines[^1].Text);
    }

    [Fact]
    public void For_two_requests_the_order_to_serve_them_in_is_given()
    {
        var request = new GourmandRequest("said", "x");
        var first = new GourmandOption(Dish(A), null, GourmandSource.Counter, null, 0);
        var second = new GourmandOption(null, Drink(Milk), GourmandSource.DrinkBar, null, 0);
        var note = new InnGourmandNote(3, new GourmandAdvice([new GourmandRequestAdvice(request, "one", [first]), new GourmandRequestAdvice(request, "two", [second])], [first, second]), null, false);

        var lines = Strip(Advice(Kitchens(), gourmands: [note])).Lines;

        Assert.Equal(Text("Inn.Gourmand.Serve") + Dish(A).Zh + Text("Inn.ReplaceArrow") + Drink(Milk).Zh, lines[^1].Text);
    }

    [Fact]
    public void The_sample_shown_while_placing_the_strip_looks_like_a_session_of_three_kitchens()
    {
        var strip = InnStripView.Sample(Inn, Text);

        Assert.Equal(Text("Inn.Sample"), strip.Lines[0].Text);
        Assert.Equal([QueueLabel(1), QueueLabel(2), QueueLabel(3)], strip.Lines.Skip(1).Select(l => l.Segments[0].Text));
        AssertPictureBesideEveryName(strip);
    }
}
