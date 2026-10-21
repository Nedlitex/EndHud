using EndHud.Vision.Inn;
using Xunit;
using Xunit.Abstractions;
using static EndHud.Tests.InnTestKit;

namespace EndHud.Tests;

/// <summary>
/// Where the setup advice shows and what it says (owner, live test 2026-10-19: "recommendation for
/// cook only shows when picking cook, and the cook for each kitchen shows under each kitchen, not as
/// a single panel. and then in the choose menu window, shows the recommended menu based on current
/// state"). On the setup screen one small panel under each kitchen's card with only that kitchen's chefs; in a
/// chef picker that kitchen's; on the menu screen the menu for the chefs placed; nothing on the theme list.
/// Real plans over the whole built-in roster and dish list (<see cref="InnSetupView"/>).
/// </summary>
public class InnSetupViewTests(ITestOutputHelper output)
{
    private const string Chen = "Chen_Qianyu";

    private static readonly InnPlanner Planner = new(Inn);

    private static SetupKitchen Empty(int n) => new(n, 2, [null, null], [null, null]) { ChefsPlaced = 0 };

    private static SetupKitchen With(int n, params string[] ids) =>
        new(n, 2, [.. ids, .. Enumerable.Repeat<string?>(null, 2 - ids.Length)], [.. ids.Select(id => Library.Operators.Single(o => o.Id == id).NameZh), .. Enumerable.Repeat<string?>(null, 2 - ids.Length)])
        { ChefsPlaced = ids.Length };

    /// <summary>The setup screen for the soup theme (at least one soup dish; hot: soup and drinks) with these kitchens.</summary>
    private static InnReading Soup(double at, params SetupKitchen[] kitchens) =>
        new(InnScreenKind.Setup, S(at), Setup: new InnSetup("舌上清流", Inn.Themes.FirstOrDefault(t => t.Zh == "舌上清流"), "出售10道[汤羹]类菜品",
            ["至少有1道[汤羹]类菜品"], ["Soup", InnData.Drink], kitchens));

    /// <summary>
    /// The menu screen scrolled through: every dish and drink offered (the player has them all), a
    /// soup already on the menu; Rating 4's requirements and the soup rule. (Once the menu screen
    /// shows cards, only those are planned.)
    /// </summary>
    private static InnReading MenuScreen(double at) =>
        new(InnScreenKind.Menu, S(at), Menu: new InnMenu(MenuTab.AllDishes, (1, 8), (0, 8),
            [.. Inn.Dishes.Select(d => new MenuCard(d.Id, d.Zh, d.Id == "melon-meatball-soup", true, null)), .. Inn.Drinks.Select(d => new MenuCard(d.Id, d.Zh, false, true, null))]),
            Requirements: new MenuRequirements(4, 3, 6, 3, [new DishRule(1, ["Soup"])], []));

    private static InnSession Session(params InnReading[] readings)
    {
        var session = NewSession();
        foreach (var r in readings) session.Update(r);
        return session;
    }

    /// <summary>The owner's case: the soup theme, Chen Qianyu placed in kitchen 1, kitchens 2 and 3 empty.</summary>
    private static readonly Lazy<(InnSession Session, InnSetupAdvice Chefs, InnSetupAdvice Menu)> ChenInKitchen1 = new(() =>
    {
        var session = Session(Soup(0, With(1, Chen), Empty(2), Empty(3)), MenuScreen(2));
        return (session, InnSetupAdvice.For(Planner, session.SetupInputs().Input!, CancellationToken.None), InnSetupAdvice.For(Planner, session.MenuInputs().Input!, CancellationToken.None));
    });

    /// <summary>Kitchen 1 full (Perlica, Avywenna), Chen Qianyu in kitchen 2, kitchen 3 empty: the owner's recorded line-up, under the soup theme.</summary>
    private static readonly Lazy<(InnSession Session, InnSetupAdvice Chefs)> Kitchen1Full = new(() =>
    {
        var session = Session(Soup(0, With(1, "Perlica", "Avywenna"), With(2, Chen), Empty(3)));
        return (session, InnSetupAdvice.For(Planner, session.SetupInputs().Input!, CancellationToken.None));
    });

    private IReadOnlyList<string> Lines(InnAdvicePanel panel)
    {
        var lines = panel.Model.Lines.Select(l => l.Text).ToList();
        output.WriteLine($"[{panel.Spot}]");
        foreach (string line in lines) output.WriteLine("  " + line);
        return lines;
    }

    private static IReadOnlyList<string> Chefs(InnAdvicePanel panel, string label) =>
        [.. panel.Model.Lines.Where(l => l.Text.StartsWith(label, StringComparison.Ordinal)).SelectMany(l => l.Segments)
            .Where(s => s.PictureId?.StartsWith(InnSetupView.OperatorPicture, StringComparison.Ordinal) == true)
            .Select(s => s.PictureId![InnSetupView.OperatorPicture.Length..])];

    // ---- The setup screen: a panel under each kitchen ----

    [Fact]
    public void The_setup_screen_has_a_panel_under_each_kitchen_with_only_its_chefs()
    {
        var (session, chefs, _) = ChenInKitchen1.Value;

        var panels = InnSetupView.ForScreen(InnScreenKind.Setup, session, chefs, null, Inn, Text);

        Assert.Equal([InnAdviceSpot.Kitchen1, InnAdviceSpot.Kitchen2, InnAdviceSpot.Kitchen3], panels.Select(p => p.Spot));
        foreach (var panel in panels) Lines(panel);
        // Kitchen 1 is half placed: one chef for its empty place, Chen Qianyu kept and never named.
        Assert.Single(Chefs(panels[0], "推荐："));
        Assert.Equal(2, Chefs(panels[1], "推荐：").Count);
        Assert.Equal(2, Chefs(panels[2], "推荐：").Count);
        var recommended = panels.SelectMany(p => Chefs(p, "推荐：")).ToList();
        Assert.Equal(recommended.Count, recommended.Distinct().Count());
        Assert.DoesNotContain(Chen, panels.SelectMany(p => Chefs(p, "推荐：").Concat(Chefs(p, "可换："))));
        Assert.All(panels, p => Assert.InRange(Chefs(p, "可换：").Count, 1, InnSuggester.MaxSubstitutes));
        // Every panel: what it cooks, then the recommended and substitutes rows; names only, every chef with their avatar; no other kitchen named.
        Assert.All(panels, p => Assert.Matches(@"^做(主食|菜肴|汤羹|甜品)(、(主食|菜肴|汤羹|甜品))*$", p.Model.Lines[0].Text));
        Assert.All(panels, p => Assert.Equal(["推荐：", "可换："], p.Model.Lines.Skip(1).Select(l => l.Text[..3])));
        Assert.All(panels, p => Assert.DoesNotContain(p.Model.Lines, l => l.Text.Contains("厨房")));
        // Substitutes are never a chef recommended anywhere, nor twice; no ids or figures, only words.
        var substitutes = panels.SelectMany(p => Chefs(p, "可换：")).ToList();
        Assert.Empty(substitutes.Intersect(recommended));
        Assert.Equal(substitutes.Count, substitutes.Distinct().Count());
        Assert.All(panels.SelectMany(p => p.Model.Lines), l => Assert.DoesNotContain(l.Text, char.IsAsciiLetterOrDigit));
    }

    [Fact]
    public void A_full_kitchen_says_it_is_set_and_its_chefs_are_suggested_nowhere()
    {
        var (session, chefs) = Kitchen1Full.Value;

        var panels = InnSetupView.ForScreen(InnScreenKind.Setup, session, chefs, null, Inn, Text);

        var full = panels.Single(p => p.Spot == InnAdviceSpot.Kitchen1);
        Assert.Contains("已排好", Lines(full));
        Assert.Empty(Chefs(full, "推荐："));
        Assert.Empty(Chefs(full, "可换："));
        Assert.Single(Chefs(panels.Single(p => p.Spot == InnAdviceSpot.Kitchen2), "推荐："));
        Assert.Equal(2, Chefs(panels.Single(p => p.Spot == InnAdviceSpot.Kitchen3), "推荐：").Count);
        string[] placed = ["Perlica", "Avywenna", Chen];
        Assert.DoesNotContain(panels.SelectMany(p => Chefs(p, "推荐：").Concat(Chefs(p, "可换："))), placed.Contains);
    }

    [Fact]
    public void While_the_plan_is_worked_out_each_kitchen_says_so()
    {
        var (session, _, _) = ChenInKitchen1.Value;

        var panels = InnSetupView.ForScreen(InnScreenKind.Setup, session, null, null, Inn, Text);

        Assert.Equal(3, panels.Count);
        Assert.All(panels, p => Assert.Equal(["正在推荐…"], Lines(p)));
    }

    /// <summary>Owner, live 2026-10-20: no "还没看到…" lines ("half of the time it is wrong and it is disturbing"): nothing read yet, no panel.</summary>
    [Fact]
    public void Nothing_read_yet_shows_no_panel()
    {
        Assert.Empty(InnSetupView.ForScreen(InnScreenKind.Setup, NewSession(), null, null, Inn, Text));
        Assert.Empty(InnSetupView.ForScreen(InnScreenKind.ChefPicker, NewSession(), null, null, Inn, Text));
        Assert.Empty(InnSetupView.ForScreen(InnScreenKind.Menu, NewSession(), null, null, Inn, Text));
    }

    // ---- The chef picker: that kitchen's advice ----

    [Fact]
    public void The_chef_picker_shows_the_advice_of_its_kitchen()
    {
        var (session, chefs, _) = ChenInKitchen1.Value;
        session.Update(new InnReading(InnScreenKind.ChefPicker, S(10)) { PickerKitchen = 2 });

        var panel = Assert.Single(InnSetupView.ForScreen(InnScreenKind.ChefPicker, session, chefs, null, Inn, Text));
        var column = InnSetupView.ForScreen(InnScreenKind.Setup, session, chefs, null, Inn, Text).Single(p => p.Spot == InnAdviceSpot.Kitchen2);

        Assert.Equal(InnAdviceSpot.Picker, panel.Spot);
        var lines = Lines(panel);
        Assert.Equal("厨房二 · 推荐", lines[0]);
        Assert.Equal(column.Model.Lines.Select(l => l.Text), lines.Skip(1));
        Assert.Equal(2, Chefs(panel, "推荐：").Count);
    }

    [Fact]
    public void A_chef_picker_whose_kitchen_was_not_read_shows_nothing()
    {
        var (session, chefs, _) = ChenInKitchen1.Value;

        Assert.Empty(InnSetupView.Picker(null, session.SetupInputs(), chefs, Inn, Text));
    }

    // ---- The menu screen: the menu for the chefs placed ----

    /// <summary>
    /// The soup theme with Chen Qianyu in kitchen 1: the menu is planned for her kitchen alone, meets every
    /// rule (Rating 4: ≥3 types, ≥6 dishes, ≥3 drinks; the soup theme: ≥1 soup), marks what's on the menu
    /// already, and has a short substitutes row under the dishes and under the drinks.
    /// </summary>
    [Fact]
    public void The_menu_screen_shows_the_menu_for_the_chefs_placed_meeting_every_rule()
    {
        var (session, chefs, menu) = ChenInKitchen1.Value;

        var panel = Assert.Single(InnSetupView.ForScreen(InnScreenKind.Menu, session, chefs, menu, Inn, Text));
        var lines = Lines(panel);

        Assert.Equal(InnAdviceSpot.Menu, panel.Spot);
        Assert.Equal(["1:Chen_Qianyu"], menu.Plan.Kitchens.Select(k => $"{k.Number}:{string.Join("+", k.Chefs.Select(c => c.Id))}"));
        Assert.True(menu.Plan.RulesMet);
        Assert.Contains(menu.Plan.Dishes, d => d.Dish.Type == DishType.Soup);
        Assert.True(menu.Plan.Dishes.Select(d => d.Dish.Type).Distinct().Count() >= 3);
        Assert.InRange(menu.Plan.Dishes.Count, 6, 8);
        Assert.InRange(menu.Plan.Drinks.Count, 3, 8);

        Assert.Equal(["推荐菜单（按现在的厨师）", "菜品：", "可换：", "饮品：", "可换："], lines.Select(l => l.StartsWith("推荐菜单", StringComparison.Ordinal) ? l : l[..3]));
        var dishes = panel.Model.Lines[1].Segments.Where(s => s.PictureId is not null).Select(s => s.PictureId).ToList();
        Assert.Equal(menu.Plan.Dishes.Select(d => d.Dish.Id), dishes);
        var drinks = panel.Model.Lines[3].Segments.Where(s => s.PictureId is not null).Select(s => s.PictureId).ToList();
        Assert.Equal(menu.Plan.Drinks.Select(d => d.Drink.Id), drinks);
        Assert.All([panel.Model.Lines[2], panel.Model.Lines[4]], l => Assert.InRange(l.Segments.Count(s => s.PictureId is not null), 1, InnSuggester.MaxSubstitutes));
        Assert.All(panel.Model.Lines, l => Assert.DoesNotContain(l.Text, char.IsAsciiLetterOrDigit));

        // ✓ marks exactly what's on the menu already, right before it: a recommended dish and drink put on.
        string dish = menu.Plan.Dishes[1].Dish.Id, drink = menu.Plan.Drinks[0].Drink.Id;
        // A dish put on that the panel neither recommends nor lists as a substitute: no ✓ anywhere. (It was a fixed
        // soup; on this soup theme the plan now puts every soup it can on, matching dishes first.)
        var shown = panel.Model.Lines.SelectMany(l => l.Segments).Select(s => s.PictureId).OfType<string>().ToHashSet();
        string other = Inn.Dishes.Select(d => d.Id).First(id => !shown.Contains(id));
        var withSome = Assert.Single(InnSetupView.Menu(session.MenuInputs(), menu, [dish, drink, other], Inn, Text));
        Lines(withSome);
        List<string?> Marked(InnLine line) =>
            [.. line.Segments.Select((s, i) => (s, i)).Where(x => x.s.Text == "✓").Select(x => line.Segments[x.i + 1].PictureId)];
        Assert.Equal([dish], Marked(withSome.Model.Lines[1]));
        Assert.Equal([drink], Marked(withSome.Model.Lines[3]));
        Assert.Empty(Marked(withSome.Model.Lines[2]));
    }

    [Fact]
    public void With_no_chef_placed_the_menu_says_the_chefs_were_picked_too()
    {
        var session = Session(Soup(0, Empty(1), Empty(2)), MenuScreen(2));
        var inputs = session.MenuInputs();
        var advice = InnSetupAdvice.For(Planner, inputs.Input!, CancellationToken.None);

        var panel = Assert.Single(InnSetupView.Menu(inputs, advice, session.OnMenu, Inn, Text));

        Assert.Equal("推荐菜单（还没排厨师，按推荐的厨师算）", Lines(panel)[0]);
        Assert.True(advice.Plan.RulesMet);
    }

    [Fact]
    public void A_menu_rule_not_understood_is_said_under_the_menu()
    {
        var session = Session(new InnReading(InnScreenKind.Setup, S(0), Setup: new InnSetup("舌上清流", null, null, ["至少有3道很奇怪的菜品"], ["Soup"], [With(1, Chen)])));
        var inputs = session.MenuInputs();

        var panel = Assert.Single(InnSetupView.Menu(inputs, null, [], Inn, Text));

        Assert.Equal(["看不懂菜单要求：至少有3道很奇怪的菜品（推荐没有算上它）", "正在推荐…"], Lines(panel));
    }

    // ---- Nothing anywhere else ----

    [Theory]
    [InlineData(InnScreenKind.ThemeList)]
    [InlineData(InnScreenKind.Session)]
    [InlineData(InnScreenKind.DispatchPanel)]
    [InlineData(null)]
    public void Nothing_shows_on_the_theme_list_or_off_the_setup_screens(InnScreenKind? screen)
    {
        var (session, chefs, menu) = ChenInKitchen1.Value;

        Assert.Empty(InnSetupView.ForScreen(screen, session, chefs, menu, Inn, Text));
    }

    /// <summary>
    /// 海陆宴 as the owner played it (live 2026-10-19): the owner's chefs placed, the owner's dishes
    /// seen on the menu screen. The menu has a 肉食 and a 水产 dish (the rule's two halves), mostly the
    /// dearer dishes, and says the objective "出售6道珍馐菜品" can't be reached with these chefs.
    /// </summary>
    [Fact]
    public void Ocean_and_Lands_menu_meets_both_halves_and_says_when_the_choice_dish_objective_is_out_of_reach()
    {
        string[] owned = ["fin-mato-soup", "clay-jar-meat", "goldfloss-chiffon", "spoon-swimmer", "melon-meatball-soup", "mala-xiang-guo", "roll-of-fin-supreme",
            "berry-fruity-toss", "eight-treasure-rice", "tonic-congee-greens", "melon-in-sauce", "fin-savory-shreds", "salted-cheese-crackers",
            "bottled-duskberry-juice", "pre-tide-lungjing", "vintage-grapedew", "bottled-milk-a"];
        var ocean = Inn.Themes.Single(t => t.Zh == "海陆宴");
        var session = Session(
            new InnReading(InnScreenKind.Setup, S(0), Setup: new InnSetup("海陆宴", ocean, "出售6道珍馐菜品", ["至少有1道带有[肉食]特性的菜品和1道带有[水产]特性的菜品"],
                ["Meaty", "Finny"], [With(1, "Akekuri", "Snowshine"), With(2, "Gilberta", "Yvonne"), With(3, "Mi_Fu", "Avywenna")])
            {
                Description = "大海与陆地交汇，天地之间无数种巧妙味道就此诞生。珍馐赏钱加成提升，出售珍馐菜品可获得更高收益。", SiChoicePercent = 5,
            }),
            new InnReading(InnScreenKind.Menu, S(2), Menu: new InnMenu(MenuTab.AllDishes, (0, 8), (0, 8), [.. owned.Select(id => new MenuCard(id, id, false, true, null))]),
                Requirements: new MenuRequirements(4, 3, 6, 3, [], [])));
        var advice = InnSetupAdvice.For(Planner, session.MenuInputs().Input!, CancellationToken.None);

        var panel = Assert.Single(InnSetupView.ForScreen(InnScreenKind.Menu, session, null, advice, Inn, Text));
        var lines = Lines(panel);

        Assert.Contains(advice.Plan.Dishes, d => d.Dish.Traits.Contains("Meaty"));
        Assert.Contains(advice.Plan.Dishes, d => d.Dish.Traits.Contains("Finny"));
        Assert.All(advice.Plan.Dishes, d => Assert.Contains(d.Dish.Id, owned));
        Assert.True(advice.Plan.Dishes.Count(d => d.Dish.Price >= 80) > advice.Plan.Dishes.Count / 2);
        Assert.Contains(lines, l => l.StartsWith("按现在的厨师，预计只能卖出约", StringComparison.Ordinal) && l.EndsWith("达不到主题加分项的6道", StringComparison.Ordinal));
    }
}
