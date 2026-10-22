using EndHud.Vision.Inn;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// A gourmand's (老饕) request in plain words (<see cref="GourmandCondition.Describe"/>) and what to
/// give them (<see cref="GourmandAdvisor"/>), with the kitchens and menu of <see cref="DispatchAdvisorTests"/>.
/// </summary>
public class GourmandAdvisorTests
{
    private static InnData Inn => InnTestData.Inn;

    private static Dish Dish(string zh) => InnTestData.Dish(zh);

    private static GourmandAdvisor Advisor() => new(new DispatchAdvisor(InnSimulatorTests.Kitchens()));

    /// <summary>Drinks for the tests: 瓶装优质兽奶 (奶香), 潮前龙井 (茶香), 瓶装霞莓饮 (果香), 直饮水.</summary>
    private static IReadOnlyList<MenuDrink> Drinks { get; } =
        [.. new[] { "瓶装优质兽奶", "潮前龙井", "瓶装霞莓饮", "直饮水" }.Select(zh => new MenuDrink(InnTestData.Drink(zh), null))];

    private static InnState State(IReadOnlyList<KitchenQueue>? queues = null, IReadOnlyList<string>? counter = null, IReadOnlyList<string>? carried = null) =>
        new(queues ?? [new KitchenQueue(1), new KitchenQueue(2), new KitchenQueue(3)], [.. (counter ?? []).Select(Dish)], OutForOrders: [], [.. (carried ?? []).Select(Dish)], [],
            InnSimulatorTests.RecordedMenu, ServingSecondsLeft: 200, QueuedAsCalled: [], ThemeItems: []);

    private static GourmandCondition Condition(string text) => GourmandCondition.Parse(text, Inn);

    private static GourmandRequest Request(string when) => new("", when);

    [Theory]
    [InlineData("非[滋补]", "不要滋补的")]
    [InlineData("非<金石稻>", "不要含金石稻的")]
    [InlineData("非[奶香]", "不要奶香的")]
    [InlineData("非主食", "不要主食")]
    [InlineData("主食", "主食")]
    [InlineData("[咸]", "咸的")]
    [InlineData("[咸]或[辣]", "咸的或辣的")]
    [InlineData("汤羹&非[辣]&非[咸]", "不辣的、不咸的汤羹")]
    [InlineData("主食&<金石稻>", "含金石稻的主食")]
    [InlineData("甜品&([酸]或[甜])", "（酸的或甜的）甜品")]
    [InlineData("非汤羹&[咸]", "不是汤羹的、咸的")]
    [InlineData("[辣]&<优质肉块>", "辣的、含优质肉块的")]
    [InlineData("<(优质)鳞肉>", "含鳞肉的")]
    [InlineData("[咸]&非<肉块>&非<优质肉块>", "咸的、不含普通肉块的、不含优质肉块的")] // 肉块 comes in two grades
    [InlineData("<面粉>", "含面粉的")] // 面粉 in one grade only: no "普通"
    public void A_condition_reads_in_plain_words(string condition, string words)
    {
        Assert.Equal(words, Condition(condition).Describe());
    }

    /// <summary>Every condition in the data reads in words, with none of the table's notation left.</summary>
    [Fact]
    public void Every_condition_in_the_data_reads_in_words()
    {
        foreach (string when in Inn.Gourmands.SelectMany(g => g.Requests.Select(r => r.When).Prepend(g.Likes)).Distinct())
        {
            string words = Condition(when).Describe();
            Assert.False(string.IsNullOrWhiteSpace(words), when);
            Assert.True(words.IndexOfAny(['[', ']', '<', '>', '&', '(', ')']) < 0, $"{when} → {words}");
            Assert.DoesNotContain("非", words);
        }
    }

    /// <summary>
    /// The two gourmands of test_yuanyuan1 (GourmandTests): 强壮警员 asks for "随便上点，别是滋补药膳" and
    /// "来点喝的清清口，别是奶"; 少年工程师 for "随便来点什么，但不要稻谷类的". Each gets things that meet them.
    /// </summary>
    [Fact]
    public void The_recorded_gourmands_get_what_meets_them()
    {
        var police = Inn.Gourmands.Single(g => g.Pool == "normal" && g.Number == 5);
        var engineer = Inn.Gourmands.Single(g => g.Pool == "normal" && g.Number == 6);
        GourmandRequest[] policeAsked = [police.Requests.Single(r => r.When == "非[滋补]"), police.Requests.Single(r => r.When == "非[奶香]")];
        var engineerAsked = engineer.Requests.Single(r => r.When == "非<金石稻>");

        var forPolice = Advisor().Advise(policeAsked, State(counter: ["坛子肉"]), Drinks);
        var forEngineer = Advisor().Advise([engineerAsked], State(), Drinks);

        Assert.Equal(["不要滋补的", "不要奶香的"], forPolice.Requests.Select(r => r.Description));
        var serve = Assert.IsAssignableFrom<IReadOnlyList<GourmandOption>>(forPolice.Serve);
        Assert.Equal((Dish("坛子肉"), GourmandSource.Counter), (serve[0].Dish, serve[0].From)); // on the counter: serve it now
        Assert.True(serve[1].Drink is { } drink && !drink.Traits.Contains("Milky"));
        Assert.DoesNotContain(forPolice.Requests[0].Options, o => o.Dish!.Traits.Contains("Rich"));
        Assert.DoesNotContain(forPolice.Requests[1].Options, o => o.Drink!.Zh == "瓶装优质兽奶");

        var options = forEngineer.Requests.Single().Options;
        Assert.NotEmpty(options);
        Assert.All(options, o => Assert.DoesNotContain(o.Dish!.Ingredients, i => i.Id == "Amber Rice"));
        Assert.Equal("不要含金石稻的", forEngineer.Requests.Single().Description);
    }

    /// <summary>Carried first, then the counter, then what comes out of a kitchen soonest (queued, or queued now).</summary>
    [Fact]
    public void Options_rank_carried_then_counter_then_soonest_out()
    {
        var state = State([new KitchenQueue(1, [new QueuedDish(Dish("“勺里游”"), 10)]), new KitchenQueue(2), new KitchenQueue(3)],
            counter: ["坛子肉"], carried: ["果味沙拉"]);

        var sides = Advisor().Options(Condition("菜肴"), state, Drinks);
        var soups = Advisor().Options(Condition("汤羹"), state, Drinks);

        Assert.Equal([("果味沙拉", GourmandSource.Carried, 0.0), ("坛子肉", GourmandSource.Counter, 0.0), ("“勺里游”", GourmandSource.Queued, 10.0)],
            sides.Select(o => (o.Dish!.Zh, o.From, o.ReadyIn)));
        // 白瓜肉丸汤 37 s in kitchen 2; 莓果甜汤 and 茄果鳞肉汤 40 s (kitchen 1 is busy for 10 s first).
        Assert.Equal([("白瓜肉丸汤", 2, 37.0), ("莓果甜汤", 2, 40.0), ("茄果鳞肉汤", 2, 40.0)],
            soups.Select(o => (o.Dish!.Zh, o.Kitchen!.Value, o.ReadyIn)));
        Assert.All(soups, o => Assert.Equal(GourmandSource.ToCook, o.From));
    }

    /// <summary>
    /// "[甜]" then "菜肴": 坛子肉 (a sweet side) is carried, 莓果甜汤 (a sweet soup) is out in 20 s.
    /// Served first, 坛子肉 would fill the sweet request and the soup couldn't fill the side one; so
    /// the soup goes first and 坛子肉 after it, which is also sooner than cooking another side.
    /// </summary>
    [Fact]
    public void Two_requests_are_served_in_the_order_that_fills_both()
    {
        var state = State([new KitchenQueue(1, [new QueuedDish(Dish("莓果甜汤"), 20)]), new KitchenQueue(2), new KitchenQueue(3)], carried: ["坛子肉"]);

        var advice = Advisor().Advise([Request("[甜]"), Request("菜肴")], state, Drinks);

        Assert.Equal([("莓果甜汤", GourmandSource.Queued), ("坛子肉", GourmandSource.Carried)], advice.Serve!.Select(o => (o.Dish!.Zh, o.From)));
    }

    [Fact]
    public void A_serving_order_puts_a_thing_meeting_both_requests_last()
    {
        GourmandCondition[] requests = [Condition("[甜]"), Condition("菜肴")];
        var both = new GourmandOption(Dish("坛子肉"), null, GourmandSource.Carried, null, 0);
        var firstOnly = new GourmandOption(Dish("莓果甜汤"), null, GourmandSource.Counter, null, 5);
        var secondOnly = new GourmandOption(Dish("果味沙拉"), null, GourmandSource.Counter, null, 0);

        Assert.Equal([firstOnly, both], GourmandAdvisor.ServingOrder(requests, [both, firstOnly]));
        Assert.Equal([secondOnly, firstOnly], GourmandAdvisor.ServingOrder(requests, [firstOnly, secondOnly])); // any order works: soonest first
        Assert.Null(GourmandAdvisor.ServingOrder(requests, [firstOnly, firstOnly with { ReadyIn = 9 }])); // nothing fills the side request
        Assert.Null(GourmandAdvisor.ServingOrder(requests, [firstOnly]));
    }

    [Fact]
    public void Nothing_on_the_menu_meeting_a_request_says_so()
    {
        // The menu has no dessert, and a drink with none left doesn't count.
        var advice = Advisor().Advise([Request("甜品"), Request("[茶香]")], State(),
            [new MenuDrink(InnTestData.Drink("潮前龙井"), 0), new MenuDrink(InnTestData.Drink("直饮水"), null)]);

        Assert.All(advice.Requests, r => Assert.Empty(r.Options));
        Assert.Null(advice.Serve);
    }

    [Fact]
    public void A_gourmand_asks_for_one_or_two_things()
    {
        Assert.Throws<ArgumentException>(() => Advisor().Advise([], State(), Drinks));
        Assert.Throws<ArgumentException>(() => Advisor().Advise([Request("主食"), Request("菜肴"), Request("汤羹")], State(), Drinks));
    }
}
