using EndHud.Vision.Inn;
using Xunit;
using Xunit.Abstractions;
using static EndHud.Tests.InnTestKit;

namespace EndHud.Tests;

/// <summary>
/// A recommendation on screen stays until it's clearly beaten (owner, live 2026-10-20: "the recommended recipe
/// keeps changing, it is very hard to follow"; the log showed three new plans in 3.5 s on the menu screen while
/// the owner picked dishes and scrolled). <see cref="InnSetupPlanning"/> with the real judge
/// (<see cref="InnPlanJudge"/>) and the real planner.
/// </summary>
public class InnSetupStabilityTests(ITestOutputHelper output)
{
    private static readonly InnPlanner Planner = new(Inn);
    private static readonly InnPlanJudge Judge = new(Planner);

    private static InnReading Wholesome(double at) =>
        new(InnScreenKind.Setup, S(at), Setup: new InnSetup("养身心", Inn.Themes.Single(t => t.Zh == "养身心"), "开启2次满堂彩",
            ["至少有2道带有[素食]或[滋补]特性的菜品"], ["Veggy", "Rich"],
            [
                new SetupKitchen(1, 2, ["Perlica", "Avywenna"], ["佩丽卡", "艾维文娜"]) { ChefsPlaced = 2 },
                new SetupKitchen(2, 2, ["Chen_Qianyu", "Ardelia"], ["陈千语", "艾尔黛拉"]) { ChefsPlaced = 2 },
                new SetupKitchen(3, 2, [null, null], [null, null]) { ChefsPlaced = 0 },
            ]));

    /// <summary>The dishes the theme's rating offers, in the data's order, and the drinks: what the menu screen lists.</summary>
    private static (IReadOnlyList<string> Dishes, IReadOnlyList<string> Drinks) Offered()
    {
        int rating = Inn.Themes.Single(t => t.Zh == "养身心").Rating;
        return ([.. Inn.Dishes.Where(d => d.Unlock.Rating is not { } r || r <= rating).Select(d => d.Id)],
            [.. Inn.Drinks.Where(d => d.Unlock.Rating is not { } r || r <= rating).Select(d => d.Id)]);
    }

    private static InnReading MenuShowing(double at, IEnumerable<string> ids, IReadOnlySet<string> onMenu) =>
        new(InnScreenKind.Menu, S(at), Menu: new InnMenu(MenuTab.AllDishes, null, null,
            [.. ids.Select(id => new MenuCard(id, id, OnMenu: onMenu.Contains(id), Unlimited: true, Stock: null))]));

    /// <param name="clock">The planning's clock; null for the real one.</param>
    private static InnSetupPlanning Planning(IInnPlanJudge judge, Func<TimeSpan>? clock = null) =>
        new(judge, input => InnSetupAdvice.Fast(Planner, input), (input, cancel) => InnSetupAdvice.For(Planner, input, cancel), note: _ => { }, clock: clock);

    /// <summary>
    /// The owner's way through the menu screen: the drinks tab, then the dishes scrolled into view four at a time
    /// half a second apart, a pause, then the dishes the advice names put on the menu one by one, a second apart.
    /// The advice is asked for on every reading and on the overlay's tick in the pause. Returns the times the
    /// advice on screen changed. <paramref name="settle"/>: the planning is told the time (false: as before, it
    /// wasn't, and planned on every new card).
    /// </summary>
    private static async Task<(int Changes, int Steps)> PickTheMenu(InnSetupPlanning planning, bool settle)
    {
        var (dishes, drinks) = Offered();
        var session = NewSession();
        session.Update(Wholesome(0));
        var seen = new List<string>();
        var onMenu = new HashSet<string>();
        double t = 1;
        int steps = 0;
        async Task Ask(double at)
        {
            if (planning.Request(session.MenuInputs(), settle ? S(at) : Settled) is { } work) await work;
        }
        async Task Step(InnReading reading)
        {
            session.Update(reading);
            steps++;
            await Ask(reading.At.TotalSeconds);
        }

        await Step(MenuShowing(t, drinks, onMenu));
        for (int i = 0; i < dishes.Count; i += 4)
        {
            var view = dishes.Skip(i).Take(4).ToList();
            seen.AddRange(view);
            t += 0.5;
            await Step(MenuShowing(t, view, onMenu));
        }
        // The pause: the overlay's tick asks again once the list has been still.
        t += InnSetupPlanning.ScrollSettle.TotalSeconds + 0.5;
        await Ask(t);
        // The player follows the advice: each dish it names goes on the menu, the card read with its red frame.
        var advice = planning.Showable(session.MenuInputs())!;
        foreach (var dish in advice.Plan.Dishes)
        {
            onMenu.Add(dish.Dish.Id);
            t += 1;
            await Step(MenuShowing(t, seen.Where(id => id == dish.Dish.Id), onMenu));
        }
        return (planning.Changes, steps);
    }

    /// <summary>
    /// The scripted picking: before (any newer plan that looks different replaces the one shown), the advice on
    /// screen changed at nearly every card scrolled into view; after, it changes only when clearly beaten.
    /// </summary>
    [Fact]
    public async Task Picking_the_menu_the_advice_changes_only_when_clearly_beaten()
    {
        // Before: every plan counted as just shown (a clock that never moves), so any newer one that looks different replaces it.
        var (before, steps) = await PickTheMenu(Planning(Replacing, Frozen), settle: false);
        var (after, _) = await PickTheMenu(Planning(Judge), settle: true);

        output.WriteLine($"scripted picking ({steps} readings): advice on screen changed {before} times before (replaced when different), {after} times after (kept until clearly beaten)");
        // At most: the plan for the drinks alone (every dish taken to be there), the one for the first cards seen (the
        // first no longer fits), the one once the list is still; each perhaps first quick, then refined.
        Assert.True(after < before, $"after {after}, before {before}");
        Assert.InRange(after, 1, 5);
    }

    /// <summary>Putting a recommended dish on the menu is the player following the advice: nothing is planned again, the advice is the same object.</summary>
    [Fact]
    public async Task A_recommended_dish_put_on_the_menu_leaves_the_advice_as_it_is()
    {
        var (dishes, drinks) = Offered();
        var session = NewSession();
        session.Update(Wholesome(0));
        session.Update(MenuShowing(1, drinks, new HashSet<string>()));
        session.Update(MenuShowing(2, dishes, new HashSet<string>()));
        var planning = new InnSetupPlanning(Judge, input => InnSetupAdvice.Fast(Planner, input), note: _ => { });
        await planning.Request(session.MenuInputs(), Settled)!;
        var shown = planning.Showable(session.MenuInputs())!;

        var recommended = shown.Plan.Dishes[0].Dish.Id;
        session.Update(MenuShowing(3, [recommended], new HashSet<string> { recommended }));

        Assert.Null(planning.Request(session.MenuInputs(), Settled)); // the inputs are the same: nothing to plan
        Assert.Same(shown, planning.Showable(session.MenuInputs()));
        Assert.Contains(recommended, session.OnMenu); // only the tick changes
    }

    /// <summary>The fast plan for the full menu and the inputs it was planned for.</summary>
    private static (InnPlanInput Input, InnSetupAdvice Advice) FullMenuPlan()
    {
        var (dishes, drinks) = Offered();
        var session = NewSession();
        session.Update(Wholesome(0));
        session.Update(MenuShowing(1, drinks, new HashSet<string>()));
        session.Update(MenuShowing(2, dishes, new HashSet<string>()));
        var input = session.MenuInputs().Input!;
        return (input, InnSetupAdvice.Fast(Planner, input));
    }

    /// <summary>
    /// In the planning: a newer plan that looks different but is no better (for newer inputs it still fits, or as
    /// the simulator's refinement arriving late) leaves the plan on screen as it is; a now-invalid plan is replaced.
    /// </summary>
    [Fact]
    public async Task The_planning_keeps_the_shown_plan_unless_it_no_longer_fits()
    {
        var (dishes, drinks) = Offered();
        var session = NewSession();
        session.Update(Wholesome(0));
        session.Update(MenuShowing(1, drinks, new HashSet<string>()));
        session.Update(MenuShowing(2, dishes, new HashSet<string>()));
        var first = InnSetupAdvice.Fast(Planner, session.MenuInputs().Input!);
        var noBetter = first with { Plan = first.Plan with { Dishes = [.. first.Plan.Dishes.Reverse()] } };
        int asked = 0;
        var planning = new InnSetupPlanning(Judge, _ => Interlocked.Increment(ref asked) == 1 ? first : noBetter, (_, _) =>
        {
            Thread.Sleep(InnSetupPlanning.RefinedWithin + TimeSpan.FromMilliseconds(200)); // late: the quick one shows first
            return noBetter;
        }, note: _ => { });

        await planning.Request(session.MenuInputs(), Settled)!;
        Assert.Same(first, planning.Showable(session.MenuInputs())); // the refinement, no better, didn't replace it

        // Newer inputs the plan still fits (a stock read): their plan is no better, the shown one stays.
        session.Update(new InnReading(InnScreenKind.Menu, S(3), Menu: new InnMenu(MenuTab.AllDishes, null, null,
            [new MenuCard(dishes[^1], dishes[^1], OnMenu: false, Unlimited: false, Stock: 9)])));
        await planning.Request(session.MenuInputs(), Settled)!;
        Assert.Same(first, planning.Showable(session.MenuInputs()));
        Assert.Equal(1, planning.Changes);
    }

    /// <summary>A plan that looks different (the same dishes in another order: the planner's noise) but earns the same is no reason to change.</summary>
    [Fact]
    public void A_plan_that_looks_different_but_is_no_better_does_not_replace_the_one_shown()
    {
        var (input, shown) = FullMenuPlan();
        var reordered = shown with { Plan = shown.Plan with { Dishes = [.. shown.Plan.Dishes.Reverse()] } };
        Assert.NotEqual(shown.Signature, reordered.Signature);

        Assert.True(Judge.Fits(input, input, shown, chefsAsPlanned: true));
        Assert.False(Judge.ClearlyBetter(input, reordered, shown));
    }

    /// <summary>
    /// A dish newly seen that beats the plan only a little doesn't replace it; the shown plan with its best dish
    /// swapped for the cheapest offered (a clearly worse plan) is replaced by the real one.
    /// </summary>
    [Fact]
    public void Only_a_clearly_better_plan_replaces_the_one_shown()
    {
        var (input, best) = FullMenuPlan();
        var planned = best.Plan.Dishes.Select(d => d.Dish.Id).ToHashSet();
        var cheapest = input.Dishes.Where(d => !planned.Contains(d.Dish.Id)).OrderBy(d => d.Dish.Price).First();
        var dearest = best.Plan.Dishes.OrderByDescending(d => d.Dish.Price).First();
        var worse = best with { Plan = best.Plan with { Dishes = [.. best.Plan.Dishes.Select(d => d == dearest ? cheapest : d)] } };

        Assert.True(Judge.ClearlyBetter(input, best, worse));
        Assert.False(Judge.ClearlyBetter(input, worse, best));
        Assert.False(Judge.ClearlyBetter(input, best, best));
    }

    /// <summary>A plan that no longer fits is replaced whatever its score: a chef placed that it didn't name, a dish no longer offered.</summary>
    [Fact]
    public void A_plan_that_no_longer_fits_is_not_kept()
    {
        var (input, shown) = FullMenuPlan();
        var named = shown.Plan.Kitchens.SelectMany(k => k.Chefs).Select(c => c.Id).ToHashSet();
        var other = input.Chefs.First(c => !named.Contains(c.Id));
        var kitchen3 = input.Kitchens.FirstOrDefault(k => k.Number == 3);
        if (kitchen3 is not null)
        {
            var placedOther = input with { Kitchens = [.. input.Kitchens.Select(k => k.Number == 3 ? k with { Placed = [other] } : k)] };
            Assert.False(Judge.Fits(placedOther, input, shown, chefsAsPlanned: true));
            Assert.True(Judge.Fits(placedOther, input, shown, chefsAsPlanned: false)); // shown meanwhile while the new plan is worked out
        }

        var gone = shown.Plan.Dishes[0].Dish.Id;
        var withoutIt = input with { Dishes = [.. input.Dishes.Where(d => d.Dish.Id != gone)] };
        Assert.False(Judge.Fits(withoutIt, input, shown, chefsAsPlanned: false));
    }

    /// <summary>
    /// A plan is kept only for the theme and kitchens it was worked out for: nothing but its not fitting replaces a plan
    /// once seen (owner, test_yuanyuan6), so another theme (other hot types, another preference) or a kitchen more or
    /// less must not fit, even with the same chefs offered and nothing placed.
    /// </summary>
    [Fact]
    public void A_plan_fits_only_its_own_theme_and_kitchens()
    {
        static SetupKitchen Empty(int number) => new(number, 2, [null, null], [null, null]) { ChefsPlaced = 0 };
        static InnReading Theme(string id, IReadOnlyList<string> hot)
        {
            var theme = Inn.Themes.Single(t => t.Id == id);
            return new InnReading(InnScreenKind.Setup, S(0), Setup: new InnSetup(theme.Zh, theme, null, [], hot, [Empty(1), Empty(2), Empty(3)]));
        }
        var wholesome = NewSession();
        wholesome.Update(Theme("truly-wholesome", ["Veggy", "Rich"]));
        var input = wholesome.SetupInputs().Input!;
        var shown = InnSetupAdvice.Fast(Planner, input);
        Assert.True(Judge.Fits(input, input, shown, chefsAsPlanned: true));

        var soupAndEntree = NewSession();
        soupAndEntree.Update(Theme("soup-entree", ["Entree", "Soup"]));
        Assert.False(Judge.Fits(soupAndEntree.SetupInputs().Input!, input, shown, chefsAsPlanned: false));

        var twoKitchens = input with { Kitchens = [.. input.Kitchens.Where(k => k.Number != 3)] };
        Assert.False(Judge.Fits(twoKitchens, input, shown, chefsAsPlanned: false));
        var morePlaces = input with { Kitchens = [.. input.Kitchens.Select(k => k.Number == 3 ? k with { Places = 3 } : k)] };
        Assert.False(Judge.Fits(morePlaces, input, shown, chefsAsPlanned: false));
    }

    // ---- The kitchens' panels follow the chefs placed (owner, live 2026-10-21, debug_yuanyuan1) ----

    /// <summary>The setup screen: kitchen 1 full (Perlica, Avywenna), kitchens 2 and 3 with these chefs placed (operator ids).</summary>
    private static InnReading Placing(double at, IReadOnlyList<string> kitchen2, IReadOnlyList<string> kitchen3)
    {
        static SetupKitchen Kitchen(int number, IReadOnlyList<string> ids)
        {
            string?[] chefs = [.. ids, .. Enumerable.Repeat<string?>(null, 2 - ids.Count)];
            return new SetupKitchen(number, 2, chefs, [.. chefs.Select(id => id is null ? null : Library.Operators.Single(o => o.Id == id).NameZh)])
            {
                ChefsPlaced = ids.Count,
            };
        }
        return new(InnScreenKind.Setup, S(at), Setup: new InnSetup("养身心", Inn.Themes.Single(t => t.Zh == "养身心"), "开启2次满堂彩",
            ["至少有2道带有[素食]或[滋补]特性的菜品"], ["Veggy", "Rich"],
            [
                new SetupKitchen(1, 2, ["Perlica", "Avywenna"], ["佩丽卡", "艾维文娜"]) { ChefsPlaced = 2 },
                Kitchen(2, kitchen2),
                Kitchen(3, kitchen3),
            ]));
    }

    /// <summary>The plan worked out with kitchen 1 full and kitchens 2 and 3 empty, and the planning showing it.</summary>
    private static async Task<(InnSession Session, InnSetupPlanning Planning, InnSetupAdvice Shown)> KitchenOneFull()
    {
        var session = NewSession();
        session.Update(Placing(0, [], []));
        var planning = new InnSetupPlanning(Judge, input => InnSetupAdvice.Fast(Planner, input), note: _ => { });
        await planning.Request(session.SetupInputs(), Settled)!;
        var shown = planning.Showable(session.SetupInputs())!;
        Assert.All(new[] { 2, 3 }, n => Assert.Equal(2, shown.Plan.Kitchens.Single(k => k.Number == n).Chefs.Count));
        return (session, planning, shown);
    }

    private static IReadOnlyList<string> Ids(InnSetupAdvice advice, int kitchen) => [.. advice.Plan.Kitchens.Single(k => k.Number == kitchen).Chefs.Select(c => c.Id)];

    private static async Task Show(InnSession session, InnSetupPlanning planning, InnReading reading)
    {
        session.Update(reading);
        if (planning.Request(session.SetupInputs(), Settled) is { } work) await work;
    }

    private static string Say(IReadOnlyList<InnAdvicePanel> panels) =>
        string.Join(" | ", panels.Select(p => $"{p.Spot}: {string.Join(" / ", p.Model.Lines.Select(l => l.Text))}"));

    /// <summary>
    /// The owner's sequence (debug_yuanyuan1): kitchen 1's chefs were placed before the plan was worked out, so its panel
    /// says it's set; then kitchens 2 and 3 were filled with exactly the chefs recommended. The plan stays (the player
    /// follows it), and every kitchen's panel now says it's set, as kitchen 1's does: no panel goes on recommending
    /// chefs for places already filled with them.
    /// </summary>
    [Fact]
    public async Task Following_the_plan_every_full_kitchen_says_it_is_set()
    {
        var (session, planning, shown) = await KitchenOneFull();

        await Show(session, planning, Placing(2, Ids(shown, 2), []));
        await Show(session, planning, Placing(4, Ids(shown, 2), Ids(shown, 3)));

        var advice = planning.Showable(session.SetupInputs());
        Assert.NotNull(advice);
        var panels = InnSetupView.Setup(session.SetupInputs(), advice, Inn, Text);
        output.WriteLine(Say(panels));
        Assert.All(new[] { 2, 3 }, n => Assert.Equal(Ids(shown, n), Ids(advice, n))); // the plan followed is the plan kept
        Assert.Equal([InnAdviceSpot.Kitchen1, InnAdviceSpot.Kitchen2, InnAdviceSpot.Kitchen3], panels.Select(p => p.Spot));
        Assert.All(panels, p =>
        {
            Assert.Equal(Text("Inn.Setup.Full"), p.Model.Lines[^1].Text);
            Assert.DoesNotContain(p.Model.Lines, l => l.Text.StartsWith(Text("Inn.Setup.Recommend"), StringComparison.Ordinal));
        });
    }

    /// <summary>
    /// A kitchen half filled by the plan's second chef: its panel names only the chef still missing (the first), never
    /// the one already in; the kitchen filled beside it says it's set.
    /// </summary>
    [Fact]
    public async Task A_half_filled_kitchen_names_only_the_chef_still_missing()
    {
        var (session, planning, shown) = await KitchenOneFull();
        var kitchen3 = Ids(shown, 3);

        await Show(session, planning, Placing(2, Ids(shown, 2), [kitchen3[1]]));

        var advice = planning.Showable(session.SetupInputs());
        Assert.NotNull(advice);
        var panels = InnSetupView.Setup(session.SetupInputs(), advice, Inn, Text);
        output.WriteLine(Say(panels));
        Assert.Equal(Text("Inn.Setup.Full"), panels.Single(p => p.Spot == InnAdviceSpot.Kitchen2).Model.Lines[^1].Text);
        var recommend = panels.Single(p => p.Spot == InnAdviceSpot.Kitchen3).Model.Lines
            .Single(l => l.Text.StartsWith(Text("Inn.Setup.Recommend"), StringComparison.Ordinal));
        Assert.Equal([InnSetupView.OperatorPicture + kitchen3[0]], recommend.Segments.Select(s => s.PictureId).OfType<string>());
    }
}
