using EndHud.Vision.Inn;
using Xunit;
using static EndHud.Tests.InnTestKit;

namespace EndHud.Tests;

/// <summary>
/// What the inn shows during a session, in the app's own zh-CN strings (owner, live 2026-10-20: "remove all hints
/// from the current panel, use the combo instead. the current panel has the requirements of the gourmands"): the
/// panel (<see cref="InnStripView"/>) only the gourmand cards, and every other call a banner
/// (<see cref="InnCallView"/>): each kitchen's next dish, a swap, the last call, Full Blaze, with the dish's picture.
/// </summary>
public class InnStripViewTests
{
    private const string A = "melon-meatball-soup", B = "fin-mato-soup", C = "mala-xiang-guo", D = "sweetbeans-on-snow", E = "meat-baozi";
    private const string Juice = "bottled-duskberry-juice", Tea = "pre-tide-lungjing", Milk = "bottled-milk-a";
    private static readonly string[] Menu = [A, B, C, D, E, Juice, Tea, Milk];

    private static InnStripModel Strip(InnAdvice advice) => InnStripView.Build(advice, Text);

    private static IReadOnlyList<InnBannerModel> Banners(InnAdvice advice) => InnCallView.Of(advice.Calls, Text);

    /// <summary>The banners for an advice made up by a test: the calls as a session would first show them.</summary>
    private static IReadOnlyList<InnBannerModel> Banners(DispatchAdvice dispatch, FullBlazeAdvice? blaze = null) =>
        InnCallView.Of(new InnCallHold().Update(dispatch, blaze, new InnKitchenView((_, _) => 0, _ => true, (_, _) => NotSooner)), Text);

    /// <summary>For a made-up advice: no kitchen has a dish out sooner than the advice says (it chose its kitchens).</summary>
    private const double NotSooner = 1000;

    private static string Kitchen(int n) => Text($"Inn.Kitchen.{n}");

    private static string Queue(int kitchen, string dish) => Format("Inn.Call.Queue", Kitchen(kitchen), Dish(dish).Zh);

    private static string CookAhead(int kitchen, string dish) => Format("Inn.Call.CookAhead", Kitchen(kitchen), Dish(dish).Zh);

    private static string LastCall(string dish, int table) => Format("Inn.Call.LastCall", Dish(dish).Zh, table);

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

    /// <summary>Every dish or drink named on the panel comes with its picture (C14).</summary>
    private static void AssertPictureBesideEveryName(InnStripModel strip)
    {
        var names = Inn.Dishes.Select(d => (d.Zh, d.Id)).Concat(Inn.Drinks.Select(d => (d.Zh, d.Id))).ToDictionary(x => x.Zh, x => x.Id);
        foreach (var segment in strip.Lines.SelectMany(l => l.Segments))
            if (names.TryGetValue(segment.Text, out var id)) Assert.Equal(id, segment.PictureId);
    }

    // ---- The panel: the gourmands only ----

    [Fact]
    public void Without_a_gourmand_the_panel_is_empty_whatever_the_kitchens_do()
    {
        var session = Prepared(1, 2);
        session.Update(Session(10, tables: Tables(Order(3, A, null), Order(4, B)), fire: new FireMeter(100, 10, Ready: true)));
        var advice = session.Advice(S(10))!;

        Assert.NotEmpty(Banners(advice)); // the kitchens' calls are there, as banners
        Assert.Empty(Strip(advice).Lines);
    }

    [Fact]
    public void No_kitchen_or_dispatch_line_ever_appears_in_the_panel()
    {
        var session = Prepared(1, 2);
        var (request, _) = DishRequestMetBy(A, B, C, D, E);
        session.Update(Session(10, tables: Tables(Order(1, A), Order(2, B), new TableCard(6, TableState.GourmandOrder, [], [])),
            fire: new FireMeter(90, 10, false), bubble: new GourmandBubble(request.Text, [request.Text])));

        var lines = Strip(session.Advice(S(10))!).Lines;

        Assert.Equal(Format("Inn.Gourmand.Title", 6), lines[0].Text); // the gourmand's card, and nothing before it
        string[] calls = [Text("Inn.Blaze.StartNow"), Text("Inn.Blaze.GetReady"), "收尾", .. Enumerable.Range(1, 3).Select(Kitchen)];
        Assert.DoesNotContain(lines, l => calls.Any(c => l.Text.StartsWith(c, StringComparison.Ordinal)));
    }

    // ---- The banners ----

    [Fact]
    public void At_the_sessions_start_every_kitchen_has_a_banner_with_a_dish_to_cook_ahead()
    {
        var session = Prepared();
        session.Update(Session(10, left: 300));

        var banners = Banners(session.Advice(S(10))!);

        Assert.Equal(3, banners.Count);
        foreach (var (banner, kitchen) in banners.Select((b, i) => (b, i + 1)))
        {
            Assert.Equal(CookAhead(kitchen, banner.PictureId!), banner.Title);
            Assert.Equal(Text("Inn.Call.CookAheadCaption"), banner.Caption);
        }
    }

    [Fact]
    public void An_ordered_dish_is_its_kitchens_banner_and_an_idle_kitchen_cooks_ahead()
    {
        var session = Prepared(1, 2);
        session.Update(Session(10, tables: Tables(Order(3, A, Juice))));

        var banners = Banners(session.Advice(S(10))!);

        Assert.Equal(2, banners.Count);
        Assert.Equal(Queue(1, A), banners[0].Title);
        Assert.Equal("厨房一：白瓜肉丸汤", banners[0].Title); // as the owner worded it
        Assert.Equal(Format("Inn.Call.ForTable", 3), banners[0].Caption);
        Assert.Equal(A, banners[0].PictureId);
        Assert.StartsWith(Format("Inn.Call.CookAhead", Kitchen(2), ""), banners[1].Title);
    }

    [Fact]
    public void A_kitchens_banner_is_its_next_dish_only_and_a_kitchen_with_nothing_to_queue_has_none()
    {
        var banners = Banners(Kitchens(new KitchenAdvice(1, [new QueueAdvice(Dish(B), 3, 40), new QueueAdvice(Dish(A), 5, 80)], []), new KitchenAdvice(2, [], [])));

        var banner = Assert.Single(banners);
        Assert.Equal(Queue(1, B), banner.Title);
        Assert.Equal(B, banner.PictureId);
    }

    [Fact]
    public void A_swap_names_the_dish_to_take_out_and_the_order_to_put_in()
    {
        var swap = new ReplaceAdvice(1, Slot: 1, Remove: Dish(D), Put: Dish(A), Table: 3, ReadyIn: 40, OtherwiseIn: 75);
        var banners = Banners(Kitchens(new KitchenAdvice(1, [new QueueAdvice(Dish(E), null, 55)], [swap]), new KitchenAdvice(2, [], [])));

        var banner = Assert.Single(banners);
        Assert.Equal("厨房一 换：雪绵豆沙 → 白瓜肉丸汤", banner.Title);
        Assert.Equal(Format("Inn.Call.Swap", Kitchen(1), Dish(D).Zh, Dish(A).Zh), banner.Title);
        Assert.Equal(Format("Inn.Call.ForTable", 3), banner.Caption);
        Assert.Equal(A, banner.PictureId);
    }

    [Theory]
    [InlineData(FullBlazeAdvice.StartNow, "Inn.Blaze.StartNow")]
    [InlineData(FullBlazeAdvice.GetReady, "Inn.Blaze.GetReady")]
    public void The_full_blaze_banner_goes_above_the_kitchens(FullBlazeAdvice blaze, string key)
    {
        var banners = Banners(Kitchens(new KitchenAdvice(1, [new QueueAdvice(Dish(A), 2, 40)], [])), blaze);

        Assert.Equal(Text(key), banners[0].Title);
        Assert.Null(banners[0].PictureId);
        Assert.Equal(Queue(1, A), banners[1].Title);
    }

    [Fact]
    public void The_full_blaze_banners_read_as_the_owner_worded_them()
    {
        // Owner, 2026-10-19: the ready line names the button and its key; "nearly full" stays from fire 80.
        Assert.Equal("开启满堂彩（R）", Banners(Kitchens(), FullBlazeAdvice.StartNow)[0].Title);
        Assert.Equal("满堂彩快满了：先把厨房排满", Banners(Kitchens(), FullBlazeAdvice.GetReady)[0].Title);
    }

    /// <summary>
    /// The last call (owner, 2026-10-19): a banner per dish to hand out, below the kitchens, by table; a wrong one
    /// says it still earns money.
    /// </summary>
    [Fact]
    public void At_the_last_call_each_leftover_has_a_banner_saying_which_table_to_give_it_to()
    {
        var banners = Banners(new DispatchAdvice([new KitchenAdvice(1, [new QueueAdvice(Dish(E), 4, 15)], [])], [], [],
            [new LeftoverAdvice(Dish(A), 2, Dish(A)), new LeftoverAdvice(Dish(B), 3, Dish(C))]));

        Assert.Equal([Queue(1, E), "收尾：白瓜肉丸汤 → 桌2", "收尾：茄果鳞肉汤 → 桌3"], banners.Select(b => b.Title));
        Assert.Equal([Text("Inn.Call.LastCallCaption"), Text("Inn.Call.LastCallWrong")], banners.Skip(1).Select(b => b.Caption));
        Assert.Equal([A, B], banners.Skip(1).Select(b => b.PictureId));
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

        var banners = Banners(session.Advice(S(32))!);

        Assert.Contains(banners, b => b.Title == LastCall(E, 3) && b.Caption == Text("Inn.Call.LastCallWrong"));
        Assert.DoesNotContain(banners, b => b.Title == Queue(1, B));
    }

    /// <summary>
    /// Owner, 2026-10-19: the inn serves 18 s after the timer's 00:00, counted from when the session
    /// first read 00:00 (the timer shows 00:00 from then on). Table 3's 肉包子 (15 s) can still be
    /// cooked just after closing, so it waits for it; 4 s later it can't, and gets the dessert left over.
    /// </summary>
    [Fact]
    public void After_closing_the_18_seconds_count_down_from_when_the_timer_first_read_00_00()
    {
        var session = Prepared(1);
        // A dessert cooked ahead, no table waiting for it: out at 15 s, a spare on the counter.
        session.Update(Session(10, left: 22, panel: Panel(1, 1, Menu[..5], (D, 5))));
        session.Update(Session(30, left: 2));
        session.Update(Session(32, left: 0, tables: Tables(Order(3, E))));

        var justClosed = Banners(session.Advice(S(32))!);
        session.Update(Session(36, left: 0, tables: Tables(Order(3, E))));
        var later = Banners(session.Advice(S(36))!);

        Assert.DoesNotContain(justClosed, b => b.Title.StartsWith("收尾", StringComparison.Ordinal));
        Assert.Contains(justClosed, b => b.Title == Queue(1, E));
        Assert.Contains(later, b => b.Title == LastCall(D, 3) && b.Caption == Text("Inn.Call.LastCallWrong"));
    }

    /// <summary>
    /// Owner, 2026-10-19: the last call is per table and can come before 00:00. With 10 s on the
    /// timer the inn serves 28 s more; table 3's soup takes 40 s, so it gets the 肉包子 left over now.
    /// </summary>
    [Fact]
    public void Before_00_00_a_table_whose_dish_cant_come_out_in_time_gets_a_leftover()
    {
        var session = Prepared(1);
        // 肉包子 cooked ahead: out at 19 s.
        session.Update(Session(5, left: 25, panel: Panel(1, 1, Menu[..5], (E, 14))));
        session.Update(Session(20, left: 10, tables: Tables(Order(3, A))));

        Assert.Contains(Banners(session.Advice(S(20))!), b => b.Title == LastCall(E, 3));
    }

    /// <summary>Owner, 2026-10-19: a table waiting only for a drink is never handed a dish; it won't be served.</summary>
    [Fact]
    public void A_table_waiting_only_for_a_drink_never_gets_a_leftover_dish()
    {
        var session = Prepared(1);
        session.Update(Session(5, left: 25, panel: Panel(1, 1, Menu[..5], (E, 14))));
        session.Update(Session(20, left: 10, tables: Tables(Order(3, Juice), Order(5, new OrderItem(A, Served: true), new OrderItem(Tea, Served: false)))));
        session.Update(Session(30, left: 0, tables: Tables(Order(3, Juice), Order(5, new OrderItem(A, Served: true), new OrderItem(Tea, Served: false)))));

        Assert.DoesNotContain(Banners(session.Advice(S(40))!), b => b.Title.StartsWith("收尾", StringComparison.Ordinal));
    }

    /// <summary>
    /// Owner, 2026-10-19: cooking ahead counts the 18 s too. With 30 s on the timer the inn serves 48 s
    /// more, so an idle kitchen still cooks ahead; with 21 s (39 s of serving) it no longer does.
    /// </summary>
    [Fact]
    public void Cooking_ahead_stops_40_seconds_before_the_inn_stops_serving_not_before_00_00()
    {
        var session = Prepared(1);
        session.Update(Session(10, left: 30));
        var at30 = Banners(session.Advice(S(10))!);
        session.Update(Session(19, left: 21));
        var at21 = Banners(session.Advice(S(19))!);

        Assert.Contains(at30, b => b.Caption == Text("Inn.Call.CookAheadCaption"));
        Assert.DoesNotContain(at21, b => b.Caption == Text("Inn.Call.CookAheadCaption"));
    }

    [Fact]
    public void While_full_blaze_runs_there_is_no_reminder_and_every_kitchen_stays_full()
    {
        var session = Prepared(1, 2);
        var tables = Tables(Order(1, A));
        session.Update(Session(10, left: 200, tables: tables, fire: new FireMeter(100, 10, Ready: true)));
        Assert.Equal(Text("Inn.Blaze.StartNow"), Banners(session.Advice(S(10))!)[0].Title);

        session.Update(Session(12, left: 198, tables: tables, fire: new FireMeter(0, 10, false)));

        var advice = session.Advice(S(12))!;
        Assert.DoesNotContain(Banners(advice), b => b.PictureId is null);
        Assert.All(advice.Dispatch!.Kitchens, k => Assert.Equal(KitchenQueue.Slots, k.Queue.Count));
    }

    /// <summary>An order that can't be read is never guessed: no banner for it, nothing in the panel; the log says so, once.</summary>
    [Fact]
    public void An_unreadable_order_is_logged_once_and_never_guessed()
    {
        var logged = new List<string>();
        var session = new InnSession(Inn, Library.Operators, ReadAllAlong, log: logged.Add, warn: _ => { });
        session.Update(SetupScreen(0, 1, 2));
        session.Update(MenuScreen(1, Menu));
        session.Update(Session(10, tables: Tables(Order(3, A, null))));

        var advice = session.Advice(S(10))!;
        session.Advice(S(10.5));

        Assert.Empty(Strip(advice).Lines);
        Assert.DoesNotContain(advice.Calls, c => c.Table == 3);
        Assert.Single(logged, l => l.Contains("table 3", StringComparison.Ordinal));
    }

    [Fact]
    public void One_kitchen_one_banner()
    {
        var session = Prepared(1);
        session.Update(Session(10, tables: TablesAsRead(Order(1, A, Tea), Order(2, C))));

        var banner = Assert.Single(Banners(session.Advice(S(10))!));
        Assert.Equal(Queue(1, A), banner.Title);
    }

    /// <summary>
    /// Owner, live 2026-10-20: "do not display '还没看到厨房/菜单…', half of the time it is wrong and it is disturbing".
    /// What hasn't been seen yet is never said in the panel (it's logged): with no gourmand, the panel is empty and put away.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Nothing_is_said_about_what_has_not_been_seen_yet(bool noKitchens)
    {
        var strip = Strip(new InnAdvice(null, noKitchens ? InnGap.NoKitchens : InnGap.NoMenu, null, false, [], [], new HashSet<int>(), true));

        Assert.Empty(strip.Lines);
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
        };
        var toCook = new GourmandOption(Dish(C), null, GourmandSource.ToCook, 1, 35);
        var drink = new GourmandOption(null, Drink(Tea), GourmandSource.DrinkBar, null, 0);
        var note = new InnGourmandNote(4, new GourmandAdvice([new GourmandRequestAdvice(request, "plain", options), new GourmandRequestAdvice(request, "other", [toCook]),
            new GourmandRequestAdvice(request, "drink", [drink])], null), null, false);

        var lines = Strip(Advice(Kitchens(new KitchenAdvice(1, [], [])), gourmands: [note], exact: new HashSet<int> { 2 })).Lines;

        string Wrap(string where) => Format("Inn.Where.Wrap", where);
        Assert.Contains(lines, l => l.Text == Text("Inn.Gourmand.Options")
            + Dish(A).Zh + Wrap(Text("Inn.Where.Counter")) + Text("Inn.Between")
            + Dish(B).Zh + Wrap(Format("Inn.Where.Kitchen", Kitchen(2), 35)));
        Assert.Contains(lines, l => l.Text == Text("Inn.Gourmand.Options") + Dish(C).Zh + Wrap(Format("Inn.Where.ToCookAbout", Kitchen(1), 35)));
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

    /// <summary>The larger gourmand panel would cover the open dispatch panel's dish cards (and the mark on one): put away meanwhile.</summary>
    [Fact]
    public void The_gourmand_panel_is_put_away_while_the_dispatch_panel_is_open()
    {
        var session = Prepared(1);
        var (request, _) = DishRequestMetBy(A, B, C, D, E);
        var gourmand = Tables(new TableCard(6, TableState.GourmandOrder, [], []));
        session.Update(Session(10, tables: gourmand, bubble: new GourmandBubble(request.Text, [request.Text])));
        Assert.NotEmpty(Strip(session.Advice(S(10))!).Lines);

        session.Update(Session(11, tables: gourmand, panel: Panel(1, 1, Menu.Take(5).ToList())));
        var open = session.Advice(S(11))!;
        Assert.Single(open.Gourmands); // still known
        Assert.Empty(Strip(open).Lines);

        session.Update(Session(12, tables: gourmand));
        Assert.NotEmpty(Strip(session.Advice(S(12))!).Lines);
    }

    /// <summary>A drink request before any drink of the menu has been seen: nothing to offer yet, and nothing said about it (not "none on the menu", which would be wrong).</summary>
    [Fact]
    public void A_drink_request_before_any_drink_of_the_menu_has_been_seen_says_nothing_about_it()
    {
        var request = new GourmandRequest("said", "x");
        var note = new InnGourmandNote(1, new GourmandAdvice([new GourmandRequestAdvice(request, "plain", [])], null), null, DrinksUnknown: true);

        var lines = Strip(Advice(Kitchens(), gourmands: [note])).Lines;

        Assert.Equal(Format("Inn.Gourmand.Wants", "plain"), lines[^1].Text);
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
    public void The_samples_shown_while_placing_them_are_a_gourmands_card_and_a_banner()
    {
        var strip = InnStripView.Sample(Inn, Text);

        Assert.Equal(Text("Inn.Sample"), strip.Lines[0].Text);
        Assert.Equal(Format("Inn.Gourmand.Title", 3), strip.Lines[1].Text);
        Assert.Contains(strip.Lines, l => l.Segments.Any(s => s.PictureId is not null));
        AssertPictureBesideEveryName(strip);

        var banner = InnCallView.Sample(Inn, Text);
        Assert.Equal(Text("Inn.Call.Sample"), banner.Title);
        Assert.NotNull(banner.PictureId);
    }
}
