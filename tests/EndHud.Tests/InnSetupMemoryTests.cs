using EndHud.Vision.Inn;
using Xunit;
using static EndHud.Tests.InnTestKit;

namespace EndHud.Tests;

/// <summary>
/// What the helper remembers of the setup (owner, live test 2026-10-19: "remember which theme, cook,
/// menu etc currently picked, UNTIL go back to pick theme page (or exit the inn mode), so when we
/// change pages, you don't show unknown because the theme page is gone"). The theme, its rules and
/// hot types, the kitchens with their placed chefs, the menu and its requirements are each taken
/// from a clear reading of their screen and kept across the setup screen, the chef picker, the menu screen and the
/// session; forgotten on the theme list or when no inn screen has shown for
/// <see cref="InnSession.ForgetAfter"/>.
/// </summary>
public class InnSetupMemoryTests
{
    private const string Chen = "Chen_Qianyu";

    /// <summary>The setup screen for the soup theme: Chen Qianyu in kitchen 1, kitchens 2 and 3 empty.</summary>
    private static InnReading Setup(double at, params SetupKitchen[] kitchens) =>
        new(InnScreenKind.Setup, S(at), Setup: new InnSetup("舌上清流", Inn.Themes.FirstOrDefault(t => t.Zh == "舌上清流"), "出售10道[汤羹]类菜品",
            ["至少有1道[汤羹]类菜品"], ["Soup", InnData.Drink],
            kitchens.Length > 0 ? kitchens :
            [
                new SetupKitchen(1, 2, [Chen, null], ["陈千语", null]) { ChefsPlaced = 1 },
                new SetupKitchen(2, 2, [null, null], [null, null]) { ChefsPlaced = 0 },
                new SetupKitchen(3, 2, [null, null], [null, null]) { ChefsPlaced = 0 },
            ]));

    private static InnReading Picker(double at, int? kitchen, bool fresh = true) => new(InnScreenKind.ChefPicker, S(at), Fresh: fresh) { PickerKitchen = kitchen };

    /// <summary>The menu screen: two soups on the menu, Rating 4's requirements and the soup rule read.</summary>
    private static InnReading Menu(double at) =>
        new(InnScreenKind.Menu, S(at), Menu: new InnMenu(MenuTab.AllDishes, (2, 8), (0, 8),
            [new MenuCard("melon-meatball-soup", "白瓜肉丸汤", true, true, null), new MenuCard("fin-mato-soup", "茄果鳞肉汤", true, true, null)]),
            Requirements: new MenuRequirements(4, 3, 6, 3, [new DishRule(1, ["Soup"])], []));

    private static string Kitchens(InnSetupInputs inputs) =>
        string.Join(" ", inputs.Input!.Kitchens.Select(k => $"{k.Number}:{k.Places}[{string.Join(",", k.Placed.Select(c => c?.Id ?? "?"))}]"));

    private static InnSession Walked()
    {
        var session = NewSession();
        session.Update(Setup(0));
        session.Update(Picker(2, 1));
        session.Update(Menu(4));
        return session;
    }

    [Fact]
    public void The_theme_kitchens_and_menu_are_kept_from_the_setup_through_the_picker_to_the_menu()
    {
        var session = Walked();

        var inputs = session.SetupInputs();
        Assert.Empty(inputs.Gaps);
        Assert.Equal(["Soup", InnData.Drink], inputs.Input!.HotTraits);
        Assert.Contains(inputs.Input.Rules.DishRules, r => r.Count == 1 && r.AnyOf.SequenceEqual(["Soup"]));
        Assert.Equal("1:2[Chen_Qianyu] 2:2[] 3:2[]", Kitchens(inputs));
        Assert.Equal(["fin-mato-soup", "melon-meatball-soup"], session.OnMenu.Order(StringComparer.Ordinal));
    }

    /// <summary>The bug the owner saw: a page without the theme said the theme was unknown.</summary>
    [Fact]
    public void A_setup_reading_that_fails_after_a_clear_one_keeps_what_was_read()
    {
        var session = Walked();
        session.Update(new InnReading(InnScreenKind.Setup, S(6), Setup: null)); // read, and nothing could be read

        var inputs = session.SetupInputs();
        Assert.Empty(inputs.Gaps);
        Assert.Equal("1:2[Chen_Qianyu] 2:2[] 3:2[]", Kitchens(inputs));
    }

    [Fact]
    public void The_theme_list_forgets_everything()
    {
        var session = Walked();

        session.Update(new InnReading(InnScreenKind.ThemeList, S(6)));

        Assert.Equal([InnSetupGap.NoSetup], session.SetupInputs().Gaps);
        Assert.Equal([InnSetupGap.NoSetup], session.MenuInputs().Gaps);
        Assert.Empty(session.OnMenu);
        Assert.False(session.Active);
    }

    [Fact]
    public void No_inn_screen_for_a_while_forgets_everything_a_short_gap_does_not()
    {
        var session = Walked();

        session.Tick(S(4) + InnSession.ForgetAfter - S(1));
        Assert.Empty(session.SetupInputs().Gaps);

        session.Tick(S(4) + InnSession.ForgetAfter + S(1));
        Assert.Equal([InnSetupGap.NoSetup], session.SetupInputs().Gaps);
        Assert.Empty(session.OnMenu);
    }

    [Fact]
    public void A_reading_long_after_the_last_one_starts_from_nothing()
    {
        var session = Walked();

        session.Update(Picker(4 + InnSession.ForgetAfter.TotalSeconds + 1, 2));

        Assert.Equal([InnSetupGap.NoSetup], session.SetupInputs().Gaps);
    }

    [Fact]
    public void The_setup_screen_after_a_session_keeps_the_menu()
    {
        var session = Walked();
        session.Update(Session(10));
        session.Update(Setup(20));
        session.Update(Session(25));

        Assert.Null(session.Advice(S(25))!.Gap);
        Assert.NotEmpty(session.OnMenu);
    }

    [Fact]
    public void The_chef_picker_remembers_its_kitchen_until_it_is_read_again()
    {
        var session = Walked();
        Assert.Equal(1, session.PickerKitchen);

        session.Update(Picker(6, 2));
        session.Update(Picker(7, null, fresh: false)); // between reads
        Assert.Equal(2, session.PickerKitchen);

        // A picker read whose title couldn't be read: not the last picker's kitchen (that advice would be another kitchen's).
        session.Update(Picker(9, null));
        Assert.Null(session.PickerKitchen);
    }

    [Fact]
    public void A_place_seen_taken_by_a_chef_not_named_is_kept_as_taken()
    {
        var session = NewSession();
        session.Update(Setup(0, new SetupKitchen(1, 2, [null, null], [null, null]) { ChefsPlaced = 1 }, new SetupKitchen(2, 2, [null, null], [null, null]) { ChefsPlaced = 0 }));

        Assert.Equal("1:2[?] 2:2[]", Kitchens(session.SetupInputs()));
    }

    /// <summary>The menu is planned for the chefs actually placed: only those kitchens, each with exactly its chefs.</summary>
    [Fact]
    public void The_menu_is_planned_for_the_chefs_placed()
    {
        var inputs = Walked().MenuInputs();

        Assert.Empty(inputs.Gaps);
        Assert.False(inputs.ChefsPlanned);
        Assert.Equal("1:1[Chen_Qianyu]", Kitchens(inputs));
        Assert.NotEqual(Walked().SetupInputs().Key, inputs.Key);
    }

    /// <summary>No chef placed anywhere: the menu is planned with the chefs the plan would pick, and says so.</summary>
    [Fact]
    public void With_no_chef_placed_the_menu_is_planned_with_chefs_too()
    {
        var session = NewSession();
        session.Update(Setup(0, new SetupKitchen(1, 2, [null, null], [null, null]) { ChefsPlaced = 0 }, new SetupKitchen(2, 2, [null, null], [null, null]) { ChefsPlaced = 0 }));

        var inputs = session.MenuInputs();

        Assert.True(inputs.ChefsPlanned);
        Assert.Equal("1:2[] 2:2[]", Kitchens(inputs));
    }

    // ---- What the player has (owner, live 2026-10-19: "it never even picked any one that I have") ----

    /// <summary>The menu screen with these dishes and drinks (data ids), none on the menu, stock ∞.</summary>
    private static InnReading Cards(double at, params string[] ids) =>
        new(InnScreenKind.Menu, S(at), Menu: new InnMenu(MenuTab.AllDishes, (0, 8), (0, 8),
            [.. ids.Select(id => new MenuCard(id, id, false, true, null))]));

    [Fact]
    public void Once_the_menu_screen_shows_dishes_only_those_are_planned_and_drinks_likewise()
    {
        var session = NewSession();
        session.Update(Setup(0));
        // Before the menu screen: every dish the rating unlocks (the player's own list can't be known yet).
        Assert.Equal(Inn.Dishes.Count, session.SetupInputs().Input!.Dishes.Count);

        session.Update(Cards(2, "fin-mato-soup", "clay-jar-meat", "goldfloss-chiffon"));
        Assert.Equal(["clay-jar-meat", "fin-mato-soup", "goldfloss-chiffon"], session.MenuInputs().Input!.Dishes.Select(d => d.Dish.Id).Order(StringComparer.Ordinal));
        Assert.Equal(["clay-jar-meat", "fin-mato-soup", "goldfloss-chiffon"], session.SetupInputs().Input!.Dishes.Select(d => d.Dish.Id).Order(StringComparer.Ordinal));
        // No drink card seen yet: the drinks stay the built-in list.
        Assert.Equal(Inn.Drinks.Count, session.MenuInputs().Input!.Drinks.Count);

        // Scrolling on: more dishes, and the drinks tab.
        session.Update(Cards(4, "spoon-swimmer", "pre-tide-lungjing", "bottled-milk-a"));
        Assert.Equal(4, session.MenuInputs().Input!.Dishes.Count);
        Assert.Equal(["bottled-milk-a", "pre-tide-lungjing"], session.MenuInputs().Input!.Drinks.Select(d => d.Drink.Id).Order(StringComparer.Ordinal));
    }

    // ---- Kept until changed (owner, live: "each kitchen's recommended cook refreshes all the time") ----

    /// <summary>
    /// The setup screen read again and again, each reading missing something else as OCR does: a
    /// chef's name, a kitchen's places, a hot trait, the rules, the objective; and a card's stock
    /// unread. None of it changed on screen, so what the advice works from (its key) doesn't change.
    /// </summary>
    [Fact]
    public void Readings_that_miss_a_part_leave_the_advice_inputs_as_they_were()
    {
        var session = NewSession();
        session.Update(Setup(0));
        session.Update(new InnReading(InnScreenKind.Menu, S(1), Menu: new InnMenu(MenuTab.AllDishes, (0, 8), (0, 8),
            [new MenuCard("fin-mato-soup", "茄果鳞肉汤", false, false, 12), new MenuCard("clay-jar-meat", "坛子肉", false, true, null)])));
        string setupKey = session.SetupInputs().Key, menuKey = session.MenuInputs().Key;

        var full = Setup(2).Setup!;
        InnSetup[] jittered =
        [
            full with { Kitchens = [new SetupKitchen(1, 2, [null, null], [null, null]) { ChefsPlaced = 1 }, .. full.Kitchens!.Skip(1)] }, // Chen's name unread
            full with { Kitchens = [new SetupKitchen(1, null, [Chen, null], ["陈千语", null]), .. full.Kitchens!.Skip(1)] }, // the places not told
            full with { HotTraits = ["Soup"] }, // one chip missed
            full with { MenuRules = [] }, // the rules not read
            full with { Objective = null },
        ];
        double at = 3;
        foreach (var setup in jittered)
        {
            session.Update(new InnReading(InnScreenKind.Setup, S(at++), Setup: setup));
            Assert.Equal(setupKey, session.SetupInputs().Key);
            Assert.Equal(menuKey, session.MenuInputs().Key);
        }
        // A card read again with its stock unread keeps the stock read before.
        session.Update(new InnReading(InnScreenKind.Menu, S(at++), Menu: new InnMenu(MenuTab.AllDishes, null, null,
            [new MenuCard("fin-mato-soup", "茄果鳞肉汤", false, false, null)])));
        Assert.Equal(menuKey, session.MenuInputs().Key);
        Assert.Equal(12, session.MenuInputs().Input!.Dishes.Single(d => d.Dish.Id == "fin-mato-soup").Stock);
    }

    /// <summary>What does change on screen changes the inputs: a chef placed, another theme.</summary>
    [Fact]
    public void A_chef_placed_or_another_theme_changes_the_advice_inputs()
    {
        var session = NewSession();
        session.Update(Setup(0));
        string key = session.SetupInputs().Key;

        session.Update(Setup(1, new SetupKitchen(1, 2, [Chen, "Perlica"], ["陈千语", "佩丽卡"]) { ChefsPlaced = 2 },
            new SetupKitchen(2, 2, [null, null], [null, null]) { ChefsPlaced = 0 }, new SetupKitchen(3, 2, [null, null], [null, null]) { ChefsPlaced = 0 }));
        Assert.NotEqual(key, session.SetupInputs().Key);
        Assert.Equal("1:2[Chen_Qianyu,Perlica] 2:2[] 3:2[]", Kitchens(session.SetupInputs()));

        // A chef taken out: the place shows empty.
        session.Update(Setup(2));
        Assert.Equal(key, session.SetupInputs().Key);

        string soupTheme = session.SetupInputs().Key;
        var ocean = Inn.Themes.Single(t => t.Zh == "海陆宴");
        session.Update(new InnReading(InnScreenKind.Setup, S(3), Setup: Setup(3).Setup! with
        {
            ThemeName = "海陆宴", Theme = ocean, Objective = "出售6道珍馐菜品", MenuRules = ["至少有1道带有[肉食]特性的菜品和1道带有[水产]特性的菜品"], HotTraits = ["Meaty", "Finny"],
        }));
        Assert.NotEqual(soupTheme, session.SetupInputs().Key);
        Assert.Equal(["Meaty", "Finny"], session.SetupInputs().Input!.HotTraits);
    }

    // ---- The owner's screenshot, 2026-10-19: three empty kitchens, two of them advised "set" ----

    /// <summary>The setup screen for 养身心 with these kitchens.</summary>
    private static InnReading Wholesome(double at, params SetupKitchen[] kitchens) =>
        new(InnScreenKind.Setup, S(at), Setup: new InnSetup("养身心", Inn.Themes.Single(t => t.Zh == "养身心"), "开启2次满堂彩",
            ["至少有2道带有[素食]或[滋补]特性的菜品"], ["Veggy", "Rich"], kitchens));

    private static SetupKitchen EmptyKitchen(int number) => new(number, 2, [null, null], [null, null]) { ChefsPlaced = 0 };

    private static readonly SetupKitchen[] Staffed =
    [
        new SetupKitchen(1, 2, ["Perlica", "Avywenna"], ["佩丽卡", "艾维文娜"]) { ChefsPlaced = 2 },
        new SetupKitchen(2, 2, [Chen, "Ardelia"], ["陈千语", "艾尔黛拉"]) { ChefsPlaced = 2 },
        EmptyKitchen(3),
    ];

    /// <summary>
    /// The player clears every place (清空当前人员和菜单设定): each place read empty (its grey bar) is a
    /// fact, not a miss. The remembered chefs go, and every kitchen is advised chefs for both places.
    /// </summary>
    [Fact]
    public void Places_read_empty_after_chefs_were_placed_hold_no_chef_and_every_place_is_advised()
    {
        var session = NewSession();
        session.Update(Wholesome(0, Staffed));
        Assert.Equal("1:2[Perlica,Avywenna] 2:2[Chen_Qianyu,Ardelia] 3:2[]", Kitchens(session.SetupInputs()));

        session.Update(Wholesome(2, EmptyKitchen(1), EmptyKitchen(2), EmptyKitchen(3)));

        var inputs = session.SetupInputs();
        Assert.Equal("1:2[] 2:2[] 3:2[]", Kitchens(inputs));
        var advice = InnSetupAdvice.Fast(new InnPlanner(Inn), inputs.Input!);
        var panels = InnSetupView.Setup(inputs, advice, Inn, Text);
        Assert.Equal(3, panels.Count);
        Assert.All(panels, p =>
        {
            var lines = p.Model.Lines.Select(l => l.Text).ToList();
            Assert.DoesNotContain(Text("Inn.Setup.Full"), lines);
            Assert.Contains(lines, l => l.StartsWith(Text("Inn.Setup.Recommend"), StringComparison.Ordinal));
        });
        Assert.All(advice.Suggestions.Kitchens, k => Assert.Equal(2, k.Chefs.Count));
    }

    /// <summary>Another theme read clearly is another setup: no chef of the last one is carried over, even into places this reading couldn't tell.</summary>
    [Fact]
    public void Another_theme_read_clearly_keeps_no_chef_of_the_last_one()
    {
        var session = NewSession();
        session.Update(Wholesome(0, Staffed));

        session.Update(Setup(2,
            new SetupKitchen(1, null, [null, null], [null, null]), // places not told: a miss, but of another setup
            new SetupKitchen(2, null, [null, null], [null, null]),
            EmptyKitchen(3)));

        Assert.Equal(["Soup", InnData.Drink], session.SetupInputs().Input!.HotTraits);
        Assert.DoesNotContain(session.SetupInputs().Input!.Kitchens, k => k.Placed.Count > 0);
    }

    /// <summary>Of the same theme, a place that couldn't be read keeps its chef, and a kitchen whose title was missed keeps what was read of it.</summary>
    [Fact]
    public void An_unreadable_place_or_a_kitchen_missed_keeps_what_was_read_before()
    {
        var session = NewSession();
        session.Update(Wholesome(0, Staffed));
        string key = session.SetupInputs().Key;

        session.Update(Wholesome(2, new SetupKitchen(1, null, [null, null], [null, null]), EmptyKitchen(3))); // kitchen 1 unreadable, kitchen 2 missed

        Assert.Equal("1:2[Perlica,Avywenna] 2:2[Chen_Qianyu,Ardelia] 3:2[]", Kitchens(session.SetupInputs()));
        Assert.Equal(key, session.SetupInputs().Key);
    }

    /// <summary>海陆宴's choice-dish objective, its text and Si's assist reach the planner.</summary>
    [Fact]
    public void Ocean_and_Lands_choice_dish_objective_text_and_Sis_assist_reach_the_planner()
    {
        var session = NewSession();
        var ocean = Inn.Themes.Single(t => t.Zh == "海陆宴");
        session.Update(new InnReading(InnScreenKind.Setup, S(0), Setup: Setup(0).Setup! with
        {
            ThemeName = "海陆宴", Theme = ocean, Objective = "出售6道珍馐菜品", MenuRules = ["至少有1道带有[肉食]特性的菜品和1道带有[水产]特性的菜品"], HotTraits = ["Meaty", "Finny"],
            Description = "大海与陆地交汇，天地之间无数种巧妙味道就此诞生。珍馐赏钱加成提升，出售珍馐菜品可获得更高收益。", SiChoicePercent = 5,
        }));

        var input = session.SetupInputs().Input!;

        Assert.Equal(6, input.ChoiceDishTarget);
        Assert.True(input.ChoiceTipsRaised);
        Assert.Equal(5, input.SiQualityPercent);
        Assert.Equal(["1:Meaty", "1:Finny"], input.Rules.DishRules.Select(r => $"{r.Count}:{string.Join("/", r.AnyOf)}"));
        // Its text names no dishes guests prefer: they pick uniformly, the hot chips only add to the price.
        Assert.Empty(input.Preferred);
    }

    /// <summary>
    /// 养身心's text says what guests prefer ("客人更倾向带有[素食]、[滋补]特性的菜品"): that reaches the
    /// planner, and a later reading missing that sentence keeps it.
    /// </summary>
    [Fact]
    public void The_theme_texts_guest_preference_reaches_the_planner()
    {
        var session = NewSession();
        var wholesome = Inn.Themes.Single(t => t.Zh == "养身心");
        var setup = Setup(0).Setup! with { ThemeName = "养身心", Theme = wholesome, HotTraits = ["Veggy", "Rich"], Description = null };
        session.Update(new InnReading(InnScreenKind.Setup, S(0), Setup: setup));
        var unread = session.SetupInputs();

        session.Update(new InnReading(InnScreenKind.Setup, S(1), Setup: setup with
        {
            Description = "几道山珍素食，轻灵之气轻抚身心。灶火升温更快，可更快开启“满堂彩\"。客人更倾向带有[素食]、[滋补]特性的菜品。",
        }));
        var read = session.SetupInputs();

        // Before the text is read, the hot chips stand in for it.
        Assert.Equal(["Veggy", "Rich"], unread.Input!.Preferred);
        Assert.Equal(["Veggy", "Rich"], read.Input!.Preferred);

        // A later reading that lost the text's last sentence doesn't take the preference away.
        session.Update(new InnReading(InnScreenKind.Setup, S(2), Setup: setup with { Description = "几道山珍素食，轻灵之气轻抚身心。" }));
        Assert.Equal(["Veggy", "Rich"], session.SetupInputs().Input!.Preferred);
    }
}
