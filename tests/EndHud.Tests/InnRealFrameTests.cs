using System.IO;
using EndHud.Vision;
using EndHud.Vision.Inn;
using Xunit;
using static EndHud.Tests.InnTestKit;

namespace EndHud.Tests;

/// <summary>
/// The wire-up end to end with the real readers (<see cref="InnScreens"/>) on frames of the owner's
/// recordings (tests/data/Fixtures/inn, what each shows in its README) and a fight: frame → reading
/// loop → session → the strip's lines. Needs the Simplified Chinese OCR language, like the readers' own tests.
/// </summary>
public class InnRealFrameTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static readonly Lazy<Task<InnScreens>> Screens = new(async () =>
    {
        var text = InnText.TryCreate()
            ?? throw new InvalidOperationException("The inn tests need the Simplified Chinese OCR language (Windows Settings > Language).");
        return new InnScreens(text, await InnPictures.LoadAsync(Folder, Inn), Inn, Library);
    });

    private static Task<Frame> Load(string name) => ImageFile.LoadAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private static async Task<(PartyMonitor Monitor, List<InnReading?> Raised)> Monitor()
    {
        var monitor = new PartyMonitor(TimeSpan.FromMilliseconds(250)) { InnFrames = new InnFrameReader(await Screens.Value) };
        var raised = new List<InnReading?>();
        monitor.InnUpdated += raised.Add;
        return (monitor, raised);
    }

    [Fact]
    public async Task A_fight_is_not_the_inn_even_when_looked_at()
    {
        var (monitor, raised) = await Monitor();

        Assert.False(await monitor.TryInnAsync(await Load("combat-combos-nearly-refilled.jpg"), S(0), squadPanelSeen: false));
        Assert.Empty(raised);
    }

    /// <summary>
    /// The owner's 2026-10-20 frame of the dispatch panel (kitchen 2) under EndHud's banners, through the real readers:
    /// with the panel open no banner shows, and kitchen 2's call is marked on the card its dish was read on.
    /// </summary>
    [Fact]
    public async Task On_the_open_dispatch_panel_the_call_is_a_mark_on_its_dishs_card_not_a_banner()
    {
        var (monitor, raised) = await Monitor();
        Assert.True(await monitor.TryInnAsync(await Load(Path.Combine("inn", "dispatch-panel-banners.jpg")), S(0), squadPanelSeen: false));
        var reading = Assert.Single(raised)!;
        var panel = Assert.IsType<DispatchPanel>(reading.Panel);
        Assert.Equal(2, panel.Kitchen);

        var session = NewSession();
        session.Update(SetupScreen(-2, 1, 2, 3));
        session.Update(MenuScreen(-1, [.. panel.Dishes.Select(d => d.DishId).OfType<string>()]));
        // The panel open on two frames in a row (InnSession.PanelFramesToOpen): the same frame again, a moment later.
        session.Update(reading);
        session.Update(reading with { At = S(0.3) });
        var advice = session.Advice(S(0.3))!;

        Assert.Empty(advice.BannerCalls);
        var call = advice.Calls.Single(c => c.Kitchen == 2);
        var marks = InnPanelMarks.Of(advice.Panel, advice.Calls, Text);
        var card = panel.Dishes.Single(d => d.DishId == call.Dish!.Id).Card;
        output.WriteLine($"kitchen 2: {call.Kind} {call.Dish!.Id} on card {card}");
        Assert.Contains(marks, m => m.OnCard && m.Area == DispatchPanelReader.CardArea(card));
    }

    [Fact]
    public async Task A_gourmands_order_frame_becomes_a_row_for_each_of_both_requests()
    {
        var (monitor, raised) = await Monitor();

        Assert.True(await monitor.TryInnAsync(await Load(Path.Combine("inn", "session-gourmand-order.jpg")), S(0), squadPanelSeen: false));

        var reading = Assert.Single(raised)!;
        Assert.Equal(InnScreenKind.Session, reading.Screen);
        Assert.Equal(TableState.GourmandOrder, reading.TableCards[0]!.State);
        Assert.Equal(2, reading.Bubble!.Requests(Inn).Count);

        var session = NewSession();
        session.Update(SetupScreen(-2, 1, 2, 3));
        session.Update(MenuScreen(-1, "melon-meatball-soup", "fin-mato-soup", "mala-xiang-guo", "goldfloss-chiffon", "bottled-duskberry-juice", "pre-tide-lungjing", "bottled-milk-a"));
        session.Update(reading);
        var note = Assert.Single(session.Advice(S(0))!.Gourmands);
        Assert.Equal(1, note.Table);
        Assert.Equal(2, note.Advice!.Requests.Count);

        var wants = InnGourmandView.Of(session.Advice(S(0))!, Text);
        Assert.Equal([(1, 0), (1, 1)], wants.Select(w => (w.Table, w.Request)));
    }

    /// <summary>
    /// The owner's recorded setup, read by the real readers: the setup screen alone gives a panel under each
    /// kitchen (owner, 2026-10-19), planned around the chefs placed (kitchen 1 full with Perlica and
    /// Avywenna, Chen Qianyu in kitchen 2, kitchen 3 empty); the chef picker for kitchen 1 is told by
    /// its title and shows kitchen 1's advice; the menu screen's two frames then give the menu for
    /// the chefs placed, meeting every requirement on screen (Rating 4: at least 3 types, 6 dishes
    /// and 3 drinks; the recorded theme: at least 2 Veggy or Rich dishes).
    /// </summary>
    [Fact]
    public async Task The_setup_picker_and_menu_screens_give_their_own_advice_from_what_was_read()
    {
        var (monitor, raised) = await Monitor();
        var session = NewSession();
        double at = 0;
        async Task Show(string fixture)
        {
            Assert.True(await monitor.TryInnAsync(await Load(Path.Combine("inn", fixture + ".jpg")), S(at), squadPanelSeen: false), fixture);
            session.Update(raised[^1]!);
            at += 3;
        }
        var planner = new InnPlanner(Inn);
        void Print(IEnumerable<InnAdvicePanel> panels)
        {
            foreach (var panel in panels)
            {
                output.WriteLine($"[{panel.Spot}]");
                foreach (var line in panel.Model.Lines) output.WriteLine("  " + line.Text);
            }
        }

        await Show("setup-chefs-placed");
        var inputs = session.SetupInputs();
        Assert.Empty(inputs.Gaps);
        Assert.Equal("1:Perlica+Avywenna 2:Chen_Qianyu 3:", string.Join(" ", inputs.Input!.Kitchens.Select(k => $"{k.Number}:{string.Join("+", k.Placed.Select(c => c?.Id))}")));
        var chefs = InnSetupAdvice.For(planner, inputs.Input, CancellationToken.None);
        var columns = InnSetupView.ForScreen(InnScreenKind.Setup, session, chefs, null, Inn, Text);
        Print(columns);
        Assert.Equal([InnAdviceSpot.Kitchen1, InnAdviceSpot.Kitchen2, InnAdviceSpot.Kitchen3], columns.Select(p => p.Spot));
        Assert.Contains("已排好", columns[0].Model.Lines.Select(l => l.Text));
        Assert.StartsWith("推荐：", columns[1].Model.Lines[^2].Text);
        Assert.StartsWith("推荐：", columns[2].Model.Lines[^2].Text);

        await Show("chef-picker");
        Assert.Equal(InnScreenKind.ChefPicker, raised[^1]!.Screen);
        Assert.Equal(1, session.PickerKitchen);
        var picker = Assert.Single(InnSetupView.ForScreen(InnScreenKind.ChefPicker, session, chefs, null, Inn, Text));
        Print([picker]);
        Assert.Equal("厨房一 · 推荐", picker.Model.Lines[0].Text);
        Assert.Contains("已排好", picker.Model.Lines.Select(l => l.Text));

        await Show("menu-dishes");
        await Show("menu-drinks");
        var menuInputs = session.MenuInputs();
        Assert.Empty(menuInputs.Gaps);
        Assert.False(menuInputs.ChefsPlanned);
        Assert.Equal((6, 3, 3), (menuInputs.Input!.Rules.MinDishes, menuInputs.Input.Rules.MinDrinks, menuInputs.Input.Rules.MinTypes));
        var menu = InnSetupAdvice.For(planner, menuInputs.Input, CancellationToken.None);
        Assert.Equal("1:Perlica+Avywenna 2:Chen_Qianyu", string.Join(" ", menu.Plan.Kitchens.Select(k => $"{k.Number}:{string.Join("+", k.Chefs.Select(c => c.Id))}")));
        Assert.True(menu.Plan.RulesMet);
        Assert.True(planner.MeetsRules(menuInputs.Input.Rules, menu.Plan.Dishes, menu.Plan.Drinks));
        Assert.True(menu.Plan.Dishes.Count(d => d.Dish.Traits.Contains("Veggy") || d.Dish.Traits.Contains("Rich")) >= 2);
        var panel = Assert.Single(InnSetupView.ForScreen(InnScreenKind.Menu, session, null, menu, Inn, Text));
        Print([panel]);
        Assert.Equal("推荐菜单（按现在的厨师）", panel.Model.Lines[0].Text);
    }

    /// <summary>
    /// The owner's soup theme (owner's screenshot, menu-soup-theme.jpg): the theme's soup rule and Rating
    /// 4's requirements are read off the menu screen by the real readers (the setup is given as remembered
    /// from the setup screen: Chen Qianyu in kitchen 1, its rules and hot types left out, so the rule can only
    /// have come from the frame). The menu planned for Chen Qianyu's kitchen meets every rule: at
    /// least one soup, 3 types, 6 dishes, 3 drinks.
    /// </summary>
    [Fact]
    public async Task The_soup_themes_menu_screen_gives_a_menu_for_the_chefs_placed_with_a_soup()
    {
        var (monitor, raised) = await Monitor();
        var session = NewSession();
        session.Update(new InnReading(InnScreenKind.Setup, S(0), Setup: new InnSetup("舌上清流", Inn.Themes.FirstOrDefault(t => t.Zh == "舌上清流"), null, [], [],
        [
            new SetupKitchen(1, 2, ["Chen_Qianyu", null], ["陈千语", null]) { ChefsPlaced = 1 },
            new SetupKitchen(2, 2, [null, null], [null, null]) { ChefsPlaced = 0 },
            new SetupKitchen(3, 2, [null, null], [null, null]) { ChefsPlaced = 0 },
        ])));

        Assert.True(await monitor.TryInnAsync(await Load(Path.Combine("inn", "menu-soup-theme.jpg")), S(3), squadPanelSeen: false));
        var reading = raised[^1]!;
        Assert.Equal(InnScreenKind.Menu, reading.Screen);
        session.Update(reading);

        var inputs = session.MenuInputs();
        Assert.Empty(inputs.Gaps);
        var rules = inputs.Input!.Rules;
        Assert.Contains(rules.DishRules, r => r.Count == 1 && r.AnyOf.SequenceEqual(["Soup"]));
        Assert.Equal((6, 3, 3), (rules.MinDishes, rules.MinDrinks, rules.MinTypes));
        Assert.Equal("1:1:Chen_Qianyu", string.Join(" ", inputs.Input.Kitchens.Select(k => $"{k.Number}:{k.Places}:{string.Join("+", k.Placed.Select(c => c?.Id))}")));

        var planner = new InnPlanner(Inn);
        var advice = InnSetupAdvice.For(planner, inputs.Input, CancellationToken.None);
        Assert.True(advice.Plan.RulesMet);
        Assert.True(planner.MeetsRules(rules, advice.Plan.Dishes, advice.Plan.Drinks));
        Assert.Contains(advice.Plan.Dishes, d => d.Dish.Type == DishType.Soup);

        var panel = Assert.Single(InnSetupView.ForScreen(InnScreenKind.Menu, session, null, advice, Inn, Text));
        foreach (var line in panel.Model.Lines) output.WriteLine(line.Text);
        Assert.Equal("推荐菜单（按现在的厨师）", panel.Model.Lines[0].Text);
        Assert.Equal(["菜品：", "可换：", "饮品：", "可换："], panel.Model.Lines.Skip(1).Select(l => l.Text[..3]));
        // Nothing is on the menu yet (0/8 and 0/8): nothing marked.
        Assert.DoesNotContain(panel.Model.Lines.SelectMany(l => l.Segments), s => s.Text == "✓");
    }

    /// <summary>
    /// Owner, live 2026-10-21 (test_yuanyuan4, about 1:00): "the recommended menu does not even satisfy the minimum
    /// requirement of the theme ... it requires 3 different kind of dishes, but the recommended ones only contains 2". The
    /// tea-break theme (暇时茶点): Rating 4's at least 3 types, 6 dishes and 3 drinks, and its own "at least 2 soups and 2
    /// desserts", the rule as the setup screen's reading put it then (the old panel said it couldn't read the dessert half:
    /// "至少有2道「[甜品]类菜。", its 品 lost where the line wraps). The menu screen read through the real readers at the top of
    /// its list (menu-soup-dessert-theme.jpg) and a little further down (…-scrolled): only soups and desserts in view yet.
    /// The setup screen's advice is planned from what's remembered: every rule holds, the dessert rule understood.
    /// </summary>
    [Fact]
    public async Task The_tea_themes_advice_meets_every_rule_with_only_soups_and_desserts_seen_on_the_menu()
    {
        var (monitor, raised) = await Monitor();
        var session = NewSession();
        var theme = Inn.Themes.Single(t => t.Zh == "暇时茶点");
        session.Update(new InnReading(InnScreenKind.Setup, S(0), Setup: new InnSetup("暇时茶点", theme, null, ["至少有2道「[汤羹]类菜品和2道「[甜品]类菜。"], [],
        [
            new SetupKitchen(1, 2, [null, null], [null, null]) { ChefsPlaced = 0 },
            new SetupKitchen(2, 2, [null, null], [null, null]) { ChefsPlaced = 0 },
            new SetupKitchen(3, 2, [null, null], [null, null]) { ChefsPlaced = 0 },
        ])));
        foreach (var (fixture, at) in new[] { ("menu-soup-dessert-theme.jpg", 3.0), ("menu-soup-dessert-theme-scrolled.jpg", 4.0) })
        {
            Assert.True(await monitor.TryInnAsync(await Load(Path.Combine("inn", fixture)), S(at), squadPanelSeen: false));
            session.Update(raised[^1]!);
        }

        var inputs = session.SetupInputs();
        Assert.Empty(inputs.RulesNotUnderstood);
        var rules = inputs.Input!.Rules;
        Assert.Equal((6, 3, 3), (rules.MinDishes, rules.MinDrinks, rules.MinTypes));
        Assert.Contains(rules.DishRules, r => r.Count == 2 && r.AnyOf.SequenceEqual(["Soup"]));
        Assert.Contains(rules.DishRules, r => r.Count == 2 && r.AnyOf.SequenceEqual(["Dessert"]));

        var planner = new InnPlanner(Inn);
        foreach (var advice in new[] { InnSetupAdvice.Fast(planner, inputs.Input), InnSetupAdvice.For(planner, inputs.Input, CancellationToken.None) })
        {
            output.WriteLine(string.Join(" ", advice.Plan.Dishes.Select(d => $"{d.Dish.Zh}({d.Dish.Type})")));
            Assert.True(advice.Plan.RulesMet);
            Assert.True(advice.Plan.Dishes.Select(d => d.Dish.Type).Distinct().Count() >= 3, "fewer than 3 dish types");
            Assert.True(planner.MeetsRules(rules, advice.Plan.Dishes, advice.Plan.Drinks));
        }
    }

    /// <summary>
    /// Owner, live 2026-10-21 (test_yuanyuan4, 4:20): table 4's gourmand asks 「清爽无油的汤羹，有吗？」 and 「来杯喝的，谨记，我滴酒
    /// 不沾。」. Through the real readers and the session to the banner for table 4: (1) the soup without seed oil → 白瓜肉丸汤
    /// (茄果鳞肉汤 has seed oil), (2) a drink without alcohol → a drink that has none (陈酿葡萄露 is on the menu and never named).
    /// </summary>
    [Fact]
    public async Task The_owners_gourmand_asking_for_a_soup_and_a_drink_gets_one_row_each()
    {
        var (monitor, raised) = await Monitor();
        Assert.True(await monitor.TryInnAsync(await Load(Path.Combine("inn", "session-gourmand-soup-and-drink.jpg")), S(0), squadPanelSeen: false));
        var reading = Assert.Single(raised)!;
        Assert.Equal(TableState.GourmandOrder, reading.TableCards[3]!.State);

        var session = NewSession();
        session.Update(SetupScreen(-2, 1, 2, 3));
        session.Update(MenuScreen(-1, "melon-meatball-soup", "fin-mato-soup", "sourberry-pudding", "boundary-lung-roll", "vintage-grapedew", "pre-tide-lungjing", "bottled-duskberry-juice"));
        session.Update(reading);
        session.Update(reading with { At = S(0.3) });

        var banner = Assert.Single(InnGourmandView.Banners(InnGourmandView.Of(session.Advice(S(0.3))!, Text), Text));
        foreach (var row in banner.Rows) output.WriteLine($"{row.Number} {row.Request} → {row.Item}");
        Assert.Equal(Format("Inn.Gourmand.Title", 4), banner.Title);
        Assert.Equal(2, banner.Rows.Count);
        string numbers = Text("Inn.Gourmand.Numbers");
        Assert.Equal((numbers[0].ToString(), "melon-meatball-soup"), (banner.Rows[0].Number, banner.Rows[0].PictureId));
        Assert.Equal(numbers[1].ToString(), banner.Rows[1].Number);
        var drink = GourmandCondition.Parse(Inn.Gourmands.Single(g => g.Pool == "normal" && g.Number == 3).Requests[5].When, Inn);
        Assert.True(drink.ForDrink);
        Assert.Equal(Format("Inn.Gourmand.Drink", drink.Describe()), banner.Rows[1].Request);
        Assert.Contains(banner.Rows[1].PictureId, new[] { "pre-tide-lungjing", "bottled-duskberry-juice" });
    }

    [Fact]
    public async Task A_dispatch_panel_frame_gives_the_kitchens_queue_exactly()
    {
        var (monitor, raised) = await Monitor();

        Assert.True(await monitor.TryInnAsync(await Load(Path.Combine("inn", "dispatch-panel-queue.jpg")), S(0), squadPanelSeen: false));

        var reading = Assert.Single(raised)!;
        Assert.Equal(InnScreenKind.DispatchPanel, reading.Screen);
        Assert.Equal(1, reading.Panel!.Kitchen);
        Assert.Equal("melon-meatball-soup", reading.Panel.Queue[0].DishId);

        var session = NewSession();
        session.Update(reading);
        session.Update(reading with { At = S(0.3) });
        var advice = session.Advice(S(0.3))!;
        Assert.Contains(1, advice.ExactKitchens);
        Assert.NotNull(advice.Dispatch);
    }
}
