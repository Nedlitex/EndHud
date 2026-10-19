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
}
