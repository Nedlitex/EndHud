using System.IO;
using EndHud.Vision.Inn;
using Xunit;
using static EndHud.Tests.InnTestKit;

namespace EndHud.Tests;

/// <summary>
/// The setup panel (plan S6; owner, 2026-10-19): what it works from (<see cref="InnSession.SetupInputs"/>:
/// the setup screen's theme and kitchens, the built-in roster and dish list, refined by the menu
/// screen when seen, never needing it), the plan and tiers worked out off the UI thread once per
/// change (<see cref="InnSetupPlanning"/>), and what the player reads (<see cref="InnSetupView"/>):
/// tier lists, or what's missing. Nothing asks the player to scroll or open a screen to feed it.
/// </summary>
public class InnSetupPanelTests
{
    private const string Placed = "Chen_Qianyu";

    private static InnReading Setup(double at, params SetupKitchen[] kitchens) =>
        new(InnScreenKind.Setup, S(at), Setup: new InnSetup("养身心", Inn.Themes.FirstOrDefault(t => t.Zh == "养身心"), "开启2次满堂彩",
            ["至少有2道带有[素食]或[滋补]特性的菜品"], ["Veggy", "Rich"],
            kitchens.Length > 0 ? kitchens : [new SetupKitchen(1, 2, [null, null], [null, null]), new SetupKitchen(2, 2, [Placed, null], ["陈千语", null]),
                new SetupKitchen(3, 2, [null, null], [null, null])]));

    /// <summary>The menu screen with every dish and drink offered (some with stock), none on the menu.</summary>
    private static InnReading Menu(double at, MenuRequirements? requirements = null, params (string Id, int Stock)[] stock) =>
        new(InnScreenKind.Menu, S(at), Menu: new InnMenu(MenuTab.AllDishes, (0, 8), (0, 8),
            [.. Inn.Dishes.Select(d => (d.Id, d.Zh)).Concat(Inn.Drinks.Select(d => (d.Id, d.Zh))).Select(c =>
                stock.FirstOrDefault(s => s.Id == c.Id) is { Id: not null } limited
                    ? new MenuCard(c.Id, c.Zh, false, Unlimited: false, Stock: limited.Stock)
                    : new MenuCard(c.Id, c.Zh, false, Unlimited: true, Stock: null))]),
            Requirements: requirements);

    private static InnSession Ready()
    {
        var session = NewSession();
        session.Update(Setup(0));
        return session;
    }

    private static readonly InnPlanner Planner = new(Inn);

    /// <summary>The plan for the setup screen alone, worked out once (two seconds over the whole roster).</summary>
    private static readonly Lazy<InnSetupAdvice> Advice = new(() => InnSetupAdvice.For(Planner, Ready().SetupInputs().Input!));

    private static IReadOnlyList<string> Lines(InnSetupInputs inputs, InnSetupAdvice? advice) =>
        [.. InnSetupView.Build(inputs, advice, Inn, Text).Lines.Select(l => l.Text)];

    // ---- What it works from ----

    [Fact]
    public void The_setup_screen_alone_gives_the_planner_the_built_in_roster_dishes_and_drinks()
    {
        var inputs = Ready().SetupInputs();

        Assert.Empty(inputs.Gaps);
        var input = Assert.IsType<InnPlanInput>(inputs.Input);
        // Every operator who can cook: Si and the Endministrator can't.
        Assert.Equal(Library.Operators.Where(o => !Inn.ChefBonuses.NotChefs.Contains(o.Id)).Select(o => o.Id).Order(), input.Chefs.Select(c => c.Id).Order());
        Assert.DoesNotContain(input.Chefs, c => c.Id is "Si" or "Endministrator");
        Assert.Equal(["1:2", "2:2", "3:2"], input.Kitchens.Select(k => $"{k.Number}:{k.Places}"));
        Assert.Equal(4, input.Rating);
        Assert.Equal(new[] { "Veggy", "Rich" }, input.HotTraits);
        // Rating 4's own rules (menu screen not seen): at least 3 types, 6 dishes, 3 drinks; 8 of each at most.
        Assert.Equal((6, 8, 3, 8, 3), (input.Rules.MinDishes, input.Rules.MaxDishes, input.Rules.MinDrinks, input.Rules.MaxDrinks, input.Rules.MinTypes));
        var rule = Assert.Single(input.Rules.DishRules);
        Assert.Equal((2, "Veggy/Rich"), (rule.Count, string.Join("/", rule.AnyOf)));
        // Every dish and drink up to Rating 4, and those a theme's stars unlock; all unlimited.
        Assert.Equal(Inn.Dishes.Select(d => d.Id), input.Dishes.Select(d => d.Dish.Id));
        Assert.Equal(Inn.Drinks.Select(d => d.Id), input.Drinks.Select(d => d.Drink.Id));
        Assert.All(input.Dishes, d => Assert.Null(d.Stock));
    }

    [Fact]
    public void A_lower_rating_unlocks_fewer_dishes_and_drinks_and_its_rules_are_not_guessed()
    {
        var session = Ready();
        // The menu screen's requirements read, no card on it.
        session.Update(new InnReading(InnScreenKind.Menu, S(2), Menu: new InnMenu(MenuTab.AllDishes, null, null, []),
            Requirements: new MenuRequirements(2, null, null, null, [], [])));

        var input = session.SetupInputs().Input!;

        Assert.Equal(2, input.Rating);
        Assert.Equal(Inn.Dishes.Where(d => d.Unlock.Rating is null or <= 2).Select(d => d.Id), input.Dishes.Select(d => d.Dish.Id));
        Assert.Contains(input.Dishes, d => d.Dish.Unlock.Rating is null); // a theme's stars: can't be seen, kept
        Assert.Equal(Inn.Drinks.Where(d => d.Unlock.Rating is null or <= 2).Select(d => d.Id), input.Drinks.Select(d => d.Drink.Id));
        // Rating 2's rules haven't been seen: no minimums beyond one dish, the counters' 8 at most.
        Assert.Equal((1, 8, 0, 8, 1), (input.Rules.MinDishes, input.Rules.MaxDishes, input.Rules.MinDrinks, input.Rules.MaxDrinks, input.Rules.MinTypes));
    }

    [Fact]
    public void The_menu_screen_refines_stock_and_rules_when_seen()
    {
        var session = Ready();
        string dish = Inn.Dishes[0].Id;
        session.Update(Menu(2, new MenuRequirements(4, 3, 6, 3, [], []), (dish, 3)));

        var input = session.SetupInputs().Input!;

        Assert.Equal(3, input.Dishes.Single(d => d.Dish.Id == dish).Stock);
        Assert.Equal((6, 8, 3, 8, 3), (input.Rules.MinDishes, input.Rules.MaxDishes, input.Rules.MinDrinks, input.Rules.MaxDrinks, input.Rules.MinTypes));
    }

    [Fact]
    public void The_key_changes_with_the_inputs_and_only_then()
    {
        var session = Ready();
        string before = session.SetupInputs().Key;
        session.Update(Setup(2)); // the same screen again
        Assert.Equal(before, session.SetupInputs().Key);
        session.Update(Menu(4, null, (Inn.Dishes[0].Id, 2))); // a dish with stock
        Assert.NotEqual(before, session.SetupInputs().Key);
    }

    [Fact]
    public void A_kitchens_places_count_its_taken_places_when_the_places_could_not_be_told()
    {
        var session = NewSession();
        session.Update(Setup(0, new SetupKitchen(1, 2, [null, null], [null, null]), new SetupKitchen(2, null, [Placed, null], ["陈千语", null]),
            new SetupKitchen(3, null, [null, null], [null, null])));

        Assert.Equal(["1:2", "2:1"], session.SetupInputs().Input!.Kitchens.Select(k => $"{k.Number}:{k.Places}"));
    }

    // ---- What the player reads ----

    [Fact]
    public void A_full_panel_is_a_tier_line_per_kitchen_then_the_menus_tiers_then_the_drinks()
    {
        var inputs = Ready().SetupInputs();
        var advice = Advice.Value;
        var model = InnSetupView.Build(inputs, advice, Inn, Text);
        var lines = model.Lines.Select(l => l.Text).ToList();

        Assert.Equal("缘缘庄 · 推荐（没有的干员/菜换同档的）", lines[0]);
        Assert.Matches(@"^厨房一(（\w+）)?：T1 ", lines[1]);
        Assert.Matches(@"^厨房二(（\w+）)?：T1 ", lines[2]);
        Assert.Matches(@"^厨房三(（\w+）)?：T1 ", lines[3]);
        Assert.StartsWith("菜单：T1 ", lines[4]);
        Assert.StartsWith("饮品：T1 ", lines[5]);
        Assert.True(advice.Plan.RulesMet);
        Assert.Equal(6, lines.Count);
        // A kitchen's role is the dish type's name as the game writes it.
        foreach (var kitchen in advice.Tiers.Kitchens.Where(k => k.Role is not null))
            Assert.Contains($"（{Inn.DishTypes.Single(t => t.Id == kitchen.Role).Zh}）：", lines[kitchen.Number]);
        // Every name has its picture: an operator's avatar, a dish's or drink's picture.
        var marks = new[] { "Inn.Setup.Tier.1", "Inn.Setup.Tier.2", "Inn.Setup.Tier.3", "Inn.Between", "Inn.Setup.Space" }.Select(Text).ToHashSet();
        var named = model.Lines.Skip(1).SelectMany(l => l.Segments).Where(s => !marks.Contains(s.Text) && !s.Text.EndsWith('：')).ToList();
        Assert.All(named, s => Assert.NotNull(s.PictureId));
        Assert.All(advice.Plan.Kitchens.SelectMany(k => k.Chefs), c => Assert.Contains(named, s => s.PictureId == InnSetupView.OperatorPicture + c.Id && s.Text == c.NameZh));
        // The menu's T1 is the plan's menu, in its order, then T2 and T3.
        var menu = model.Lines[4].Segments.Where(s => s.PictureId is not null).Select(s => s.PictureId).ToList();
        Assert.Equal(advice.Tiers.Dishes.SelectMany(t => t).Select(d => d.Dish.Id), menu);
        Assert.Equal(advice.Plan.Dishes.Select(d => d.Dish.Id), menu.Take(advice.Plan.Dishes.Count));
        // No ids, no figures: names and the tier marks only.
        Assert.DoesNotContain(lines.Skip(1), l => System.Text.RegularExpressions.Regex.Replace(l, "T[123] ", "").Any(char.IsAsciiLetterOrDigit));
    }

    [Fact]
    public void Fewer_kitchens_and_places_give_fewer_lines()
    {
        var session = NewSession();
        session.Update(Setup(0, new SetupKitchen(1, 2, [null, null], [null, null]), new SetupKitchen(2, 1, [null], [null])));
        var inputs = session.SetupInputs();
        var advice = InnSetupAdvice.For(Planner, inputs.Input!);
        var lines = Lines(inputs, advice);

        Assert.Equal(["1:2", "2:1"], inputs.Input!.Kitchens.Select(k => $"{k.Number}:{k.Places}"));
        Assert.Equal(2, lines.Count(l => l.StartsWith("厨房")));
        Assert.DoesNotContain(lines, l => l.StartsWith("厨房三"));
        Assert.True(advice.Plan.Kitchens.Single(k => k.Number == 2).Chefs.Count <= 1);
    }

    [Fact]
    public void Nothing_seen_says_only_that_the_theme_is_missing()
    {
        var session = NewSession();
        session.Update(new InnReading(InnScreenKind.Menu, S(0), Fresh: false));

        Assert.Equal(["缘缘庄 · 推荐（没有的干员/菜换同档的）", "还没看到主题：打开「营业设定」"], Lines(session.SetupInputs(), null));
    }

    [Fact]
    public void The_theme_list_without_kitchens_says_the_kitchens_are_missing()
    {
        var session = NewSession();
        session.Update(new InnReading(InnScreenKind.Setup, S(0), Setup: new InnSetup("养身心", Inn.Themes.First(t => t.Zh == "养身心"), null, [], ["Veggy", "Rich"], null)));

        Assert.Equal([InnSetupGap.NoKitchens], session.SetupInputs().Gaps);
    }

    /// <summary>Owner, 2026-10-19: never require scrolling or reading the chef picker, nor opening a screen just to feed the helper.</summary>
    [Fact]
    public void No_line_of_the_panel_asks_to_scroll_or_to_open_the_chef_picker_or_the_menu()
    {
        string xaml = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "EndHud", "i18n", "zh-CN.xaml"));
        var setupLines = xaml.Split('\n').Where(l => l.Contains("x:Key=\"Inn.Setup.")).ToList();

        Assert.NotEmpty(setupLines);
        Assert.DoesNotContain(setupLines, l => l.Contains("滚动") || l.Contains("安排干员") || l.Contains("设定菜单"));
        Assert.Equal([InnSetupGap.ThemeUnreadable, InnSetupGap.NoSetup, InnSetupGap.NoKitchens], Enum.GetValues<InnSetupGap>());
    }

    [Fact]
    public void An_unreadable_theme_says_so_and_nothing_else()
    {
        var session = Ready();
        session.Update(new InnReading(InnScreenKind.Setup, S(6), Setup: null)); // read, and nothing could be read

        var inputs = session.SetupInputs();
        Assert.Null(inputs.Input);
        Assert.Equal(["缘缘庄 · 推荐（没有的干员/菜换同档的）", "看不清主题（画面被遮挡）"], Lines(inputs, Advice.Value));
    }

    [Fact]
    public void A_frame_between_reads_is_not_an_unreadable_theme()
    {
        var session = Ready();
        session.Update(new InnReading(InnScreenKind.Setup, S(6), Fresh: false));
        Assert.Empty(session.SetupInputs().Gaps);
    }

    [Fact]
    public void While_the_plan_is_worked_out_the_panel_says_so()
    {
        Assert.Equal(["缘缘庄 · 推荐（没有的干员/菜换同档的）", "正在推荐…"], Lines(Ready().SetupInputs(), null));
    }

    [Fact]
    public void Without_text_recognition_only_what_to_install()
    {
        var model = InnSetupView.Build(Ready().SetupInputs(), null, Inn, Text, "Inn.Problem.NoTextRecognition");
        Assert.Equal(["缘缘庄 · 推荐（没有的干员/菜换同档的）", Text("Inn.Problem.NoTextRecognition")], model.Lines.Select(l => l.Text));
    }

    // ---- Worked out off the UI thread, once per change ----

    [Fact]
    public async Task The_plan_is_worked_out_once_per_change_of_its_inputs()
    {
        int calls = 0;
        var planning = new InnSetupPlanning(input => { Interlocked.Increment(ref calls); return Advice.Value; });
        var session = Ready();

        var first = planning.Request(session.SetupInputs());
        Assert.NotNull(first);
        Assert.Null(planning.Request(session.SetupInputs())); // the same inputs: nothing new
        await first;
        Assert.Null(planning.Request(session.SetupInputs()));
        Assert.NotNull(planning.Showable(session.SetupInputs()));

        session.Update(Menu(6, null, (Inn.Dishes[0].Id, 2)));
        var second = planning.Request(session.SetupInputs());
        Assert.NotNull(second);
        await second;
        Assert.Equal(2, calls);
        Assert.Equal(2, planning.Started);
        Assert.Null(planning.Request(NewSession().SetupInputs())); // a gap: nothing to plan
    }

    [Fact]
    public async Task An_older_plan_is_shown_meanwhile_only_while_all_it_names_is_still_there()
    {
        var planning = new InnSetupPlanning(_ => Advice.Value);
        var session = Ready();
        await planning.Request(session.SetupInputs())!;
        var advice = planning.Showable(session.SetupInputs())!;

        // A dish's stock read: the older plan still names only what's there.
        session.Update(Menu(6, null, (Inn.Dishes[0].Id, 2)));
        Assert.Same(advice, planning.Showable(session.SetupInputs()));

        // At Rating 1 most of what it names isn't unlocked: not shown.
        var lower = Ready();
        lower.Update(new InnReading(InnScreenKind.Menu, S(4), Menu: new InnMenu(MenuTab.AllDishes, null, null, []),
            Requirements: new MenuRequirements(1, null, null, null, [], [])));
        Assert.Null(planning.Showable(lower.SetupInputs()));
    }

    [Fact]
    public async Task A_planner_failure_is_logged_and_shows_no_plan()
    {
        var logged = new List<string>();
        var planning = new InnSetupPlanning(_ => throw new ArgumentException("bad input"), logged.Add);
        var inputs = Ready().SetupInputs();
        await planning.Request(inputs)!;

        Assert.Null(planning.Showable(inputs));
        Assert.Contains(logged, l => l.Contains("bad input"));
    }

    // ---- The sample shown while the menu is open ----

    [Fact]
    public void The_sample_has_every_kitchen_the_menu_and_drinks_in_tiers()
    {
        var model = InnSetupView.Sample(Inn, Library.Operators, Text);
        var lines = model.Lines.Select(l => l.Text).ToList();
        Assert.Equal(Text("Inn.Setup.Sample"), lines[0]);
        Assert.Equal(["厨房一", "厨房二", "厨房三", "菜单：", "饮品："], lines.Skip(1).Select(l => l.StartsWith("厨房") ? l[..3] : l[..(l.IndexOf('：') + 1)]));
        Assert.All(lines.Skip(1), l => Assert.Contains("T1 ", l));
        Assert.DoesNotContain(model.Lines.SelectMany(l => l.Segments),
            s => s.PictureId?.StartsWith(InnSetupView.OperatorPicture) == true && Library.Operators.First(o => s.PictureId == InnSetupView.OperatorPicture + o.Id) is var op && Inn.ChefBonusesOf(op).Count == 0);
    }
}
