using EndHud.Vision.Inn;
using EndHud.Vision.Operators;
using Xunit;
using static EndHud.Tests.InnTestKit;

namespace EndHud.Tests;

/// <summary>
/// The setup panel (plan S6): what it works from (<see cref="InnSession.SetupInputs"/>), the plan
/// worked out off the UI thread once per change (<see cref="InnSetupPlanning"/>), and what the
/// player reads (<see cref="InnSetupView"/>) in each case: a full plan, fewer kitchens and places,
/// no chefs seen, no menu seen, the theme unreadable. The plan never names a chef the screens
/// didn't show.
/// </summary>
public class InnSetupPanelTests
{
    // The chefs the picker named (a screen of the fixture's grid) and the one placed on 营业设定.
    private static readonly string[] Shown = ["Lifeng", "Laevatain", "Yvonne", "Da_Pan", "Akekuri", "Catcher", "Estella", "Fluorite"];
    private const string Placed = "Chen_Qianyu";

    private static InnReading Setup(double at, params SetupKitchen[] kitchens) =>
        new(InnScreenKind.Setup, S(at), Setup: new InnSetup("养身心", Inn.Themes.FirstOrDefault(t => t.Zh == "养身心"), "开启2次满堂彩",
            ["至少有2道带有[素食]或[滋补]特性的菜品"], ["Veggy", "Rich"],
            kitchens.Length > 0 ? kitchens : [new SetupKitchen(1, 2, [null, null], [null, null]), new SetupKitchen(2, 2, [Placed, null], ["陈千语", null]),
                new SetupKitchen(3, 2, [null, null], [null, null])]));

    private static InnReading Picker(double at, params string[] named) =>
        new(InnScreenKind.ChefPicker, S(at), Picker: new ChefPickerReading(
            [.. named.Select(id => new ChefCard(default, [ChefIcon.Entree, ChefIcon.Side], "Salty", [id], id)),
             new ChefCard(default, [ChefIcon.Entree, null], null, [], null)], []));

    /// <summary>The menu screen with every dish and drink offered, none on the menu, and 养身心's Rating 4 requirements.</summary>
    private static InnReading Menu(double at, MenuRequirements? requirements = null, bool drinks = true) =>
        new(InnScreenKind.Menu, S(at), Menu: new InnMenu(MenuTab.AllDishes, (0, 8), (0, 8),
            [.. Inn.Dishes.Select(d => new MenuCard(d.Id, d.Zh, false, true, null)),
             .. (drinks ? Inn.Drinks : []).Select(d => new MenuCard(d.Id, d.Zh, false, true, null))]),
            Requirements: requirements ?? Rating4());

    private static MenuRequirements Rating4() => new(4, 3, 6, 3, [new TraitRule(2, ["Veggy", "Rich"])], []);

    private static InnSession Ready()
    {
        var session = NewSession();
        session.Update(Setup(0));
        session.Update(Picker(2, Shown));
        session.Update(Menu(4));
        return session;
    }

    private static InnPlan PlanOf(InnSetupInputs inputs) => new InnPlanner(Inn).Plan(inputs.Input!);

    private static IReadOnlyList<string> Lines(InnSetupInputs inputs, InnPlan? plan) =>
        [.. InnSetupView.Build(inputs, plan, Text).Lines.Select(l => l.Text)];

    // ---- What it works from ----

    [Fact]
    public void A_full_setup_gives_the_planner_what_the_screens_showed()
    {
        var inputs = Ready().SetupInputs();

        Assert.Empty(inputs.Gaps);
        var input = Assert.IsType<InnPlanInput>(inputs.Input);
        Assert.Equal(Shown.Append(Placed).Order(), input.Chefs.Select(c => c.Id).Order());
        Assert.Equal(["1:2", "2:2", "3:2"], input.Kitchens.Select(k => $"{k.Number}:{k.Places}"));
        Assert.Equal(4, input.Rating);
        Assert.Equal(new[] { "Veggy", "Rich" }, input.HotTraits);
        Assert.Equal((6, 8, 3, 8, 3), (input.Rules.MinDishes, input.Rules.MaxDishes, input.Rules.MinDrinks, input.Rules.MaxDrinks, input.Rules.MinTypes));
        var rule = Assert.Single(input.Rules.TraitRules);
        Assert.Equal((2, "Veggy/Rich"), (rule.Count, string.Join("/", rule.AnyOf)));
        Assert.Equal(Inn.Dishes.Count, input.Dishes.Count);
        Assert.Equal(Inn.Drinks.Count, input.Drinks.Count);
    }

    [Fact]
    public void The_key_changes_with_the_inputs_and_only_then()
    {
        var session = Ready();
        string before = session.SetupInputs().Key;
        session.Update(Picker(6, Shown)); // the same cards again
        Assert.Equal(before, session.SetupInputs().Key);
        session.Update(Picker(8, "Antal")); // scrolled: one more
        Assert.NotEqual(before, session.SetupInputs().Key);
    }

    // ---- What the player reads ----

    [Fact]
    public void A_full_plan_is_one_line_per_kitchen_then_the_menu_then_the_drinks()
    {
        var inputs = Ready().SetupInputs();
        var plan = PlanOf(inputs);
        var model = InnSetupView.Build(inputs, plan, Text);
        var lines = model.Lines.Select(l => l.Text).ToList();

        Assert.Equal("缘缘庄 · 推荐", lines[0]);
        Assert.StartsWith("厨房一：", lines[1]);
        Assert.StartsWith("厨房二：", lines[2]);
        Assert.StartsWith("厨房三：", lines[3]);
        Assert.StartsWith("菜单：", lines[4]);
        Assert.StartsWith("饮品：", lines[5]);
        Assert.True(plan.RulesMet);
        Assert.Equal(6, lines.Count);
        // Every name has its picture: an operator's avatar, a dish's or drink's picture.
        var named = model.Lines.Skip(1).SelectMany(l => l.Segments).Where(s => s.Text != Text("Inn.Between") && !s.Text.EndsWith('：')).ToList();
        Assert.All(named, s => Assert.NotNull(s.PictureId));
        Assert.All(plan.Kitchens.SelectMany(k => k.Chefs), c => Assert.Contains(named, s => s.PictureId == InnSetupView.OperatorPicture + c.Id && s.Text == c.NameZh));
        Assert.Equal(plan.Dishes.Select(d => d.Dish.Zh), model.Lines[4].Segments.Where(s => s.PictureId is not null).Select(s => s.Text));
        // No ids, no figures: only names.
        Assert.DoesNotContain(lines, l => l.Any(char.IsAsciiLetter));
    }

    [Fact]
    public void The_plan_never_names_a_chef_the_screens_did_not_show()
    {
        var session = NewSession();
        session.Update(Setup(0));
        session.Update(Picker(2, "Lifeng", "Yvonne", "Estella"));
        session.Update(Menu(4));
        var inputs = session.SetupInputs();
        var plan = PlanOf(inputs);

        var chefs = plan.Kitchens.SelectMany(k => k.Chefs).Select(c => c.Id).ToList();
        Assert.NotEmpty(chefs);
        Assert.All(chefs, id => Assert.Contains(id, new[] { "Lifeng", "Yvonne", "Estella", Placed }));
    }

    [Fact]
    public void Fewer_kitchens_and_places_give_fewer_lines_and_names()
    {
        var session = NewSession();
        session.Update(Setup(0, new SetupKitchen(1, 2, [null, null], [null, null]), new SetupKitchen(2, 1, [null], [null])));
        session.Update(Picker(2, Shown));
        session.Update(Menu(4));
        var inputs = session.SetupInputs();
        var plan = PlanOf(inputs);
        var lines = Lines(inputs, plan);

        Assert.Equal(["1:2", "2:1"], inputs.Input!.Kitchens.Select(k => $"{k.Number}:{k.Places}"));
        Assert.Equal(2, lines.Count(l => l.StartsWith("厨房")));
        Assert.DoesNotContain(lines, l => l.StartsWith("厨房三"));
        Assert.True(plan.Kitchens.Single(k => k.Number == 2).Chefs.Count <= 1);
    }

    [Fact]
    public void A_kitchen_whose_places_could_not_be_told_counts_only_its_placed_chefs()
    {
        var session = NewSession();
        session.Update(Setup(0, new SetupKitchen(1, 2, [null, null], [null, null]), new SetupKitchen(2, null, [Placed, null], ["陈千语", null]),
            new SetupKitchen(3, null, [null, null], [null, null])));
        session.Update(Picker(2, Shown));
        session.Update(Menu(4));

        Assert.Equal(["1:2", "2:1"], session.SetupInputs().Input!.Kitchens.Select(k => $"{k.Number}:{k.Places}"));
    }

    [Fact]
    public void No_chefs_seen_says_to_open_the_picker_and_scroll_it()
    {
        var session = NewSession();
        session.Update(Setup(0));
        session.Update(Menu(2));
        var inputs = session.SetupInputs();

        Assert.Null(inputs.Input);
        Assert.Equal(["缘缘庄 · 推荐", "还没看到可选的干员：打开「安排干员」并把列表滚动一遍"], Lines(inputs, null));
    }

    [Fact]
    public void No_menu_seen_says_to_open_the_menu()
    {
        var session = NewSession();
        session.Update(Setup(0));
        session.Update(Picker(2, Shown));
        var inputs = session.SetupInputs();

        Assert.Null(inputs.Input);
        Assert.Equal(["缘缘庄 · 推荐", "还没看到菜单：打开「设定菜单」"], Lines(inputs, null));
    }

    [Fact]
    public void Nothing_seen_says_each_thing_to_open()
    {
        var session = NewSession();
        session.Update(new InnReading(InnScreenKind.Menu, S(0), Fresh: false));

        Assert.Equal(["缘缘庄 · 推荐", "还没看到主题：打开「营业设定」", "还没看到可选的干员：打开「安排干员」并把列表滚动一遍", "还没看到菜单：打开「设定菜单」"],
            Lines(session.SetupInputs(), null));
    }

    [Fact]
    public void An_unreadable_theme_says_so_and_nothing_else()
    {
        var session = Ready();
        session.Update(new InnReading(InnScreenKind.Setup, S(6), Setup: null)); // read, and nothing could be read

        var inputs = session.SetupInputs();
        Assert.Null(inputs.Input);
        Assert.Equal(["缘缘庄 · 推荐", "看不清主题（画面被遮挡）"], Lines(inputs, PlanOf(Ready().SetupInputs())));
    }

    [Fact]
    public void A_frame_between_reads_is_not_an_unreadable_theme()
    {
        var session = Ready();
        session.Update(new InnReading(InnScreenKind.Setup, S(6), Fresh: false));
        Assert.Empty(session.SetupInputs().Gaps);
    }

    [Fact]
    public void Unread_menu_requirements_are_not_half_planned()
    {
        var session = NewSession();
        session.Update(Setup(0));
        session.Update(Picker(2, Shown));
        session.Update(Menu(4, new MenuRequirements(4, 3, null, null, [], [])));

        Assert.Equal(["缘缘庄 · 推荐", "看不清菜单要求（画面被遮挡）"], Lines(session.SetupInputs(), null));
    }

    [Fact]
    public void Drinks_needed_and_none_seen_says_to_open_the_drinks_tab()
    {
        var session = NewSession();
        session.Update(Setup(0));
        session.Update(Picker(2, Shown));
        session.Update(Menu(4, drinks: false));

        Assert.Equal(["缘缘庄 · 推荐", "还没看到饮品：在「设定菜单」里点开「饮品」"], Lines(session.SetupInputs(), null));
    }

    [Fact]
    public void While_the_plan_is_worked_out_the_panel_says_so()
    {
        Assert.Equal(["缘缘庄 · 推荐", "正在推荐…"], Lines(Ready().SetupInputs(), null));
    }

    [Fact]
    public void Without_text_recognition_only_what_to_install()
    {
        var model = InnSetupView.Build(Ready().SetupInputs(), null, Text, "Inn.Problem.NoTextRecognition");
        Assert.Equal(["缘缘庄 · 推荐", Text("Inn.Problem.NoTextRecognition")], model.Lines.Select(l => l.Text));
    }

    // ---- The chefs seen: cleared when the setup screens close ----

    [Fact]
    public void A_session_starting_forgets_the_chefs_seen()
    {
        var session = Ready();
        session.Update(Session(10));
        session.Update(Setup(400));
        session.Update(Menu(402));

        Assert.Contains(InnSetupGap.NoChefs, session.SetupInputs().Gaps);
    }

    [Fact]
    public void Leaving_the_setup_screens_forgets_the_chefs_seen()
    {
        var session = Ready();
        session.Tick(S(4 + InnSession.EndAfter.TotalSeconds - 1));
        Assert.Empty(session.SetupInputs().Gaps);

        session.Tick(S(4 + InnSession.EndAfter.TotalSeconds + 1));
        Assert.Contains(InnSetupGap.NoChefs, session.SetupInputs().Gaps);
    }

    // ---- Worked out off the UI thread, once per change ----

    [Fact]
    public async Task The_plan_is_worked_out_once_per_change_of_its_inputs()
    {
        int calls = 0;
        var planning = new InnSetupPlanning(input => { Interlocked.Increment(ref calls); return new InnPlanner(Inn).Plan(input); });
        var session = Ready();

        var first = planning.Request(session.SetupInputs());
        Assert.NotNull(first);
        Assert.Null(planning.Request(session.SetupInputs())); // the same inputs: nothing new
        await first;
        Assert.Null(planning.Request(session.SetupInputs()));
        Assert.NotNull(planning.Showable(session.SetupInputs()));

        session.Update(Picker(6, "Antal"));
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
        var planning = new InnSetupPlanning(input => new InnPlanner(Inn).Plan(input));
        var session = Ready();
        await planning.Request(session.SetupInputs())!;
        var plan = planning.Showable(session.SetupInputs())!;

        // One more chef seen: the older plan still names only chefs that are there.
        session.Update(Picker(6, "Antal"));
        Assert.Same(plan, planning.Showable(session.SetupInputs()));

        // A plan naming a chef no longer there isn't shown.
        var fewer = NewSession();
        fewer.Update(Setup(0));
        fewer.Update(Picker(2, "Antal"));
        fewer.Update(Menu(4));
        Assert.Null(planning.Showable(fewer.SetupInputs()));
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
    public void The_sample_has_every_kitchen_the_menu_and_drinks()
    {
        var lines = InnSetupView.Sample(Inn, Library.Operators, Text).Lines.Select(l => l.Text).ToList();
        Assert.Equal(Text("Inn.Setup.Sample"), lines[0]);
        Assert.Equal(["厨房一：", "厨房二：", "厨房三：", "菜单：", "饮品："], lines.Skip(1).Select(l => l[..(l.IndexOf('：') + 1)]));
        Assert.DoesNotContain(InnSetupView.Sample(Inn, Library.Operators, Text).Lines.SelectMany(l => l.Segments),
            s => s.PictureId?.StartsWith(InnSetupView.OperatorPicture) == true && Library.Operators.First(o => s.PictureId == InnSetupView.OperatorPicture + o.Id) is var op && Inn.ChefBonusesOf(op).Count == 0);
    }
}
