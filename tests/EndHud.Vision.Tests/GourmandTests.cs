using EndHud.Vision.Inn;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// Gourmands' (老饕) requests and what meets them (<see cref="GourmandCondition"/>), checked
/// against the dishes and drinks in inn/inn-data.json. The expectations are the notes of the NGA
/// quick-reference table the requests come from (缘缘庄老饕需求速查表), and the owner's recording.
/// </summary>
public class GourmandTests
{
    private static readonly InnData Inn =
        InnData.Parse(File.ReadAllBytes(Path.Combine(OperatorCache.RepositoryRoot(), InnData.FolderName, InnData.FileName)))!;

    private static GourmandCondition Condition(string text) => GourmandCondition.Parse(text, Inn);

    private static string[] DishesMeeting(string condition) =>
        [.. Inn.Dishes.Where(Condition(condition).MetBy).Select(d => d.Zh).Order(StringComparer.Ordinal)];

    private static string[] DrinksMeeting(string condition) =>
        [.. Inn.Drinks.Where(Condition(condition).MetBy).Select(d => d.Zh).Order(StringComparer.Ordinal)];

    [Fact]
    public void Every_request_reads_and_some_dish_or_drink_meets_it()
    {
        Assert.Equal(7, Inn.Gourmands.Count(g => g.Pool == "normal"));
        Assert.Equal(15, Inn.Gourmands.Count(g => g.Pool == "feast"));
        foreach (var request in Inn.Gourmands.SelectMany(g => g.Requests))
        {
            var condition = Condition(request.When);
            Assert.True(Inn.Dishes.Any(condition.MetBy) || Inn.Drinks.Any(condition.MetBy), $"nothing meets \"{request.Text}\" ({request.When})");
        }
    }

    /// <summary>"让我见识见识一百三十香": the three dearest dishes, one entree, one side, one soup.</summary>
    [Fact]
    public void Spice_mix_130_is_in_the_three_dearest_dishes()
    {
        Assert.Equal(["界龙肉卷", "簇锦豆腐羹", "糖醋宝鳞"], DishesMeeting("<一百三十香>"));
    }

    [Theory]
    [InlineData("[咸]&<面粉>", "金丝小贝", true)] // "金丝小贝可解"
    [InlineData("<(优质)鳞肉>", "鳞香肉丝", false)] // "鳞香肉丝没有鳞肉"
    [InlineData("[水产]", "酸菜鳞肉", false)] // "酸菜鳞片没有水产标签"
    [InlineData("菜肴&<茄果>", "茄果鳞肉汤", false)] // "番茄鳞汤不是菜肴"
    [InlineData("<球豆>&非<砂糖>", "雪绵豆沙", true)] // "雪棉豆沙有[甜]但不带砂糖也是可以交的"
    [InlineData("[辣]&非[酸]", "酸辣汤", false)] // "别上酸辣汤"
    public void The_tables_notes_hold(string condition, string dish, bool met)
    {
        Assert.Equal(met, Condition(condition).MetBy(Inn.Dishes.Single(d => d.Zh == dish)));
    }

    /// <summary>"饮品还是得喝有青叶莓的": "全菜单只有瓶装霞莓饮" (the only Fruity drink).</summary>
    [Fact]
    public void Drink_requests_are_met_by_drinks_only()
    {
        Assert.Equal(["瓶装霞莓饮"], DrinksMeeting("[果香]"));
        Assert.Empty(DishesMeeting("[果香]"));
        Assert.True(Condition("非[酒香]").ForDrink);
        Assert.False(Condition("非[滋补]").ForDrink);
        Assert.Empty(DrinksMeeting("非[滋补]"));
    }

    [Fact]
    public void A_quality_ingredient_needs_the_higher_grade_and_either_grade_takes_both()
    {
        // 菜籽油 (Seed Oil) is plain in 炸薯条; 多味菇 (Relishroom) is [A] only in some dishes.
        Assert.Contains("奶油蘑菇汤", DishesMeeting("汤羹&<优质多味菇>"));
        Assert.DoesNotContain("菌菇蒸饺", DishesMeeting("<优质多味菇>"));
        Assert.Contains("菌菇蒸饺", DishesMeeting("<(优质)多味菇>"));
        Assert.Contains("菌菇蒸饺", DishesMeeting("<多味菇>"));
        Assert.DoesNotContain("奶油蘑菇汤", DishesMeeting("<多味菇>"));
    }

    /// <summary>
    /// Lines Windows OCR read off the speech bubbles in the owner's recording (test_yuanyuan1, read
    /// at half size): at 3:40 normal gourmand 5 (强壮警员), at 5:03 normal gourmand 6 (少年工程师).
    /// Bubbles wrap, so lines come cut off, and with OCR's slips ("亻日" for "但", stray marks).
    /// </summary>
    [Theory]
    [InlineData("一随便上点，别是滋补药膳", 5, "非[滋补]")]
    [InlineData("来点喝的清清口，别是奶", 5, "非[奶香]")]
    [InlineData("{喝的清清口，别是奶", 5, "非[奶香]")]
    [InlineData("随便来点什么，亻日不要稻", 6, "非<金石稻>")]
    [InlineData("什么，但不要稻", 6, "非<金石稻>")]
    [InlineData("一随便来点什么，但不要稻", 6, "非<金石稻>")]
    public void A_line_off_a_speech_bubble_finds_its_request(string read, int gourmand, string when)
    {
        var best = Inn.MatchSaid(read)[0];

        Assert.Equal(("normal", gourmand, when), (best.Gourmand.Pool, best.Gourmand.Number, best.Request.When));
    }

    /// <summary>
    /// "随便来点什么，但不要稻谷类的" and "…但不要甜品的" are one gourmand's two requests: the words they
    /// share (随便, 来点, 不要) count for little, the one that tells them apart (要稻) for most. Read
    /// with OCR's slip and cut off, the line is still only the first.
    /// </summary>
    [Fact]
    public void A_word_only_one_request_has_outweighs_the_words_many_share()
    {
        var match = Assert.Single(Inn.MatchSaid("随便来点什么，亻日不要稻"));

        Assert.Equal("非<金石稻>", match.Request.When);
    }

    /// <summary>
    /// A bubble's lines, as the live session read them (owner, 2026-10-19): each request is found
    /// on its own lines, however the OCR cut them. The first bubble is gourmand 5's two requests
    /// (「来点汤，要酸口，促消化。」「上好的肉！做点菜！」), the second gourmand 2's
    /// (「锅气十足的菜，啊，千万不要辣。」「酸酸甜甜，果汁之类的有没有？」), with their first
    /// characters cut and 千 read as 干.
    /// </summary>
    [Theory]
    [InlineData(new[] { "来点汤，要酸口，促消", "化。", "上好的肉！做点菜！" }, "汤羹&[酸]", "菜肴&<优质肉块>")]
    [InlineData(new[] { "点汤，要酸口，促消", "匕", "好的肉！做点菜！" }, "汤羹&[酸]", "菜肴&<优质肉块>")]
    [InlineData(new[] { "十足的菜，啊，干万", "甜甜，果汁之类的有7" }, "菜肴&非[辣]", "[果香]")]
    [InlineData(new[] { "随便上占别是滋补药膳", "种就行！", "点喝的清清口，别是奶", "的就行。" }, "非[滋补]", "非[奶香]")]
    public void Each_request_of_a_bubble_is_found_on_its_own_lines(string[] lines, string first, string second)
    {
        var bubble = new GourmandBubble(string.Concat(lines), lines);

        Assert.Equal([first, second], bubble.Requests(Inn).Select(m => m.Request.When));
    }

    /// <summary>
    /// The text the live session logged for that bubble, its lines run together and "化。上" lost:
    /// cut at its commas too, both requests are still found (only the soup was before).
    /// </summary>
    [Fact]
    public void Even_run_together_as_logged_live_both_requests_are_found()
    {
        const string logged = "点汤，要酸口，促消匕好的肉！做点菜！";

        Assert.Equal(["汤羹&[酸]", "菜肴&<优质肉块>"], new GourmandBubble(logged, [logged]).Requests(Inn).Select(m => m.Request.When));
    }

    /// <summary>The inn's other talk, read off the same recording, is no gourmand's request.</summary>
    [Theory]
    [InlineData("不用谢，菜已经帮你上齐了。")]
    [InlineData("厨房人员和祀的协助可额外提升珍馐产出概率")]
    [InlineData("上菜还算顺利，看来灶火要烧得")]
    [InlineData("多备些菜吧，接下来要变得更热")]
    [InlineData("尽快上齐菜品饮品，可获得剩余收益")]
    [InlineData("嗯．·．．生意还不错。")]
    public void Other_talk_matches_no_request(string read)
    {
        Assert.Empty(Inn.MatchSaid(read));
    }

    [Fact]
    public void What_the_recorded_gourmands_asked_for_is_met_by_these()
    {
        Assert.DoesNotContain("白瓜肉丸汤", DishesMeeting("非[滋补]"));
        string[] notMilky = ["大碗碎茶", "梨花香茶", "潮前龙井", "游人酒", "玉米汁", "瓶装兽奶", "瓶装脆蔗汁", "瓶装霞莓饮", "直饮水", "陈酿葡萄露"];
        Assert.Equal(notMilky.Order(StringComparer.Ordinal), DrinksMeeting("非[奶香]"));
        Assert.DoesNotContain("八宝饭", DishesMeeting("非<金石稻>"));
        Assert.Contains("金丝小贝", DishesMeeting("非<金石稻>"));
    }

    /// <summary>
    /// Every request in the data, said exactly as the data has it, is found as itself (owner, 2026-10-21, test_yuanyuan8: the
    /// feast pool's tenth gourmand asks for a drink in two characters and a dish in one, and those were never found: a line
    /// had to have three two-character pieces).
    /// </summary>
    [Fact]
    public void Every_request_said_exactly_is_found_as_itself()
    {
        var missed = Inn.Gourmands.SelectMany(g => g.Requests.Select((r, i) => (g, r, i)))
            .Where(x => Inn.MatchSaid(x.r.Text).FirstOrDefault()?.Request != x.r)
            .Select(x => $"{x.g.Pool} {x.g.Number} request {x.i}")
            .ToList();
        Assert.True(missed.Count == 0, string.Join(", ", missed));
    }

    /// <summary>
    /// A gourmand's two requests in one bubble, a line each, or wrapped at ten characters as the game's bubble wraps them
    /// (test_yuanyuan8: every one of the fourteen feast gourmands' bubbles is one of these pairs): both found, in order, and
    /// nothing else. Every pair of every gourmand in the data.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    public void A_gourmands_two_requests_on_their_own_lines_are_both_found(int wrapAt)
    {
        var missed = new List<string>();
        foreach (var g in Inn.Gourmands)
        for (int i = 0; i < g.Requests.Count; i++)
        for (int j = 0; j < g.Requests.Count; j++)
        {
            if (i == j) continue;
            string[] lines = [.. Wrap(g.Requests[i].Text, wrapAt), .. Wrap(g.Requests[j].Text, wrapAt)];
            var found = new GourmandBubble(string.Concat(lines), lines).Requests(Inn).Select(m => m.Request).ToList();
            if (!found.SequenceEqual([g.Requests[i], g.Requests[j]])) missed.Add($"{g.Pool} {g.Number} requests {i}+{j}");
        }
        Assert.True(missed.Count == 0, $"{missed.Count}: {string.Join(", ", missed)}");

        static IEnumerable<string> Wrap(string text, int at) =>
            at <= 0 ? [text] : Enumerable.Range(0, (text.Length + at - 1) / at).Select(k => text.Substring(k * at, Math.Min(at, text.Length - k * at)));
    }

    [Theory]
    [InlineData("甜品&([酸]或[甜])")]
    [InlineData("主食&非[美观]&非[雅致]")]
    [InlineData("[肉食]或<(优质)肉块>")]
    public void Grouping_and_or_and_not_read(string condition)
    {
        Assert.NotNull(Condition(condition));
    }

    [Theory]
    [InlineData("[脆]", "unknown trait")]
    [InlineData("<龙虾>", "unknown ingredient")]
    [InlineData("汤羹&", "ends too soon")]
    [InlineData("(汤羹", "expected")]
    [InlineData("汤羹 甜品", "unexpected")]
    public void A_condition_that_cant_be_read_says_why(string condition, string why)
    {
        Assert.Contains(why, Assert.Throws<FormatException>(() => Condition(condition)).Message);
    }
}
