using EndHud.Vision.Inn;
using EndHud.Vision.Operators;

namespace EndHud;

/// <summary>What the setup panel shows: the plan, per kitchen, with substitutes for a player who lacks some of what it names.</summary>
internal sealed record InnSetupAdvice(InnPlan Plan, InnSuggestions Suggestions)
{
    /// <summary>The plan for <paramref name="input"/> and its suggestions (about two seconds over the whole roster: off the UI thread).</summary>
    public static InnSetupAdvice For(InnPlanner planner, InnPlanInput input)
    {
        var plan = planner.Plan(input);
        return new InnSetupAdvice(plan, InnSuggester.Of(planner, input, plan));
    }
}

/// <summary>
/// Works out the setup panel's plan and suggestions off the UI thread (about two seconds), once per
/// change of its inputs (<see cref="InnSetupInputs.Key"/>): the setup screens are read every two
/// seconds, and the same inputs are never planned twice. A plan for older inputs is kept until the
/// new one is ready, and is shown meanwhile only while everything it names is still available
/// (<see cref="Showable"/>).
/// </summary>
internal sealed class InnSetupPlanning(Func<InnPlanInput, InnSetupAdvice> plan, Action<string>? log = null)
{
    private readonly object _lock = new();
    private readonly Action<string> _log = log ?? (m => DiagnosticLog.Throttled("inn-setup-plan", TimeSpan.FromSeconds(30), m, "ERROR"));
    private string? _requested;
    private InnSetupAdvice? _plan;
    private string? _planKey;
    private int _started;

    /// <summary>Plans started so far (tests).</summary>
    public int Started
    {
        get { lock (_lock) return _started; }
    }

    /// <summary>
    /// Starts working out the plan for <paramref name="inputs"/> on the thread pool, unless they have
    /// none (a gap) or are those already planned or being planned. <paramref name="done"/> runs on
    /// the worker when the plan is ready (or failed, logged). Returns the work, or null when none started.
    /// </summary>
    public Task? Request(InnSetupInputs inputs, Action? done = null)
    {
        if (inputs.Input is not { } input) return null;
        lock (_lock)
        {
            if (inputs.Key == _requested) return null;
            _requested = inputs.Key;
            _started++;
        }
        return Task.Run(() =>
        {
            try
            {
                var result = plan(input);
                lock (_lock)
                {
                    _plan = result;
                    _planKey = inputs.Key;
                }
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                _log($"inn: working out the setup plan failed: {ex}");
            }
            done?.Invoke();
        });
    }

    /// <summary>
    /// The plan to show for <paramref name="inputs"/>: the one worked out for them, else the last one
    /// if every chef, dish and drink it names is still in them; null while there's none.
    /// </summary>
    public InnSetupAdvice? Showable(InnSetupInputs inputs)
    {
        if (inputs.Input is not { } input) return null;
        InnSetupAdvice? advice;
        string? key;
        lock (_lock) (advice, key) = (_plan, _planKey);
        if (advice is null) return null;
        if (key == inputs.Key) return advice;
        var plan = advice.Plan;
        bool chefs = plan.Kitchens.SelectMany(k => k.Chefs).All(c => input.Chefs.Any(o => o.Id == c.Id));
        bool kitchens = plan.Kitchens.All(k => input.Kitchens.Any(i => i.Number == k.Number && i.Places >= k.Chefs.Count))
                        && input.Kitchens.All(i => plan.Kitchens.Any(k => k.Number == i.Number));
        bool dishes = plan.Dishes.All(d => input.Dishes.Any(o => o.Dish.Id == d.Dish.Id));
        bool drinks = plan.Drinks.All(d => input.Drinks.Any(o => o.Drink.Id == d.Drink.Id));
        return chefs && kitchens && dishes && drinks ? advice : null;
    }
}

/// <summary>Where a piece of setup advice is shown: under a kitchen's card on 营业设定, in the chef picker, on 菜单设定.</summary>
internal enum InnAdviceSpot
{
    Kitchen1 = 1,
    Kitchen2 = 2,
    Kitchen3 = 3,
    Picker,
    Menu,
}

/// <summary>One piece of setup advice and where it goes.</summary>
internal sealed record InnAdvicePanel(InnAdviceSpot Spot, InnStripModel Model);

/// <summary>
/// The setup panel's words (plan S6; owner, 2026-10-19: "split this to each kitchen"), every string
/// from i18n/zh-CN.xaml. A block per kitchen: its heading (the kitchen and the dish types it cooks,
/// or that it isn't used), then indented rows: 干员 (the plan's chefs, each with their avatar) and
/// 可换 (substitutes for them), 做菜 (the menu dishes it cooks mainly, each with its picture) and
/// 可换 (off-menu dishes for one the player lacks). The drinks last, with theirs. Names only, no
/// figures; a row wraps between names, never inside one, and a wrapped row stays beside its label.
/// What's missing (the theme, the kitchens) is said instead, and an unreadable theme says only that.
/// Nothing asks the player to open a screen or scroll to feed it.
/// </summary>
internal static class InnSetupView
{
    /// <summary>Prefix of an operator's picture id: their avatar, not a dish's picture.</summary>
    public const string OperatorPicture = "op:";

    public static IReadOnlyList<InnAdvicePanel> ForScreen(InnScreenKind? screen, InnSession session, InnSetupAdvice? chefs, InnSetupAdvice? menu,
        InnData data, Func<string, string> text, string? problemKey = null) => [];

    public static IReadOnlyList<InnAdvicePanel> Setup(InnSetupInputs inputs, InnSetupAdvice? advice, InnData data, Func<string, string> text) => [];

    public static IReadOnlyList<InnAdvicePanel> Picker(int? kitchen, InnSetupInputs inputs, InnSetupAdvice? advice, InnData data, Func<string, string> text) => [];

    public static IReadOnlyList<InnAdvicePanel> Menu(InnSetupInputs inputs, InnSetupAdvice? advice, IReadOnlyCollection<string> onMenu, InnData data,
        Func<string, string> text) => [];

    private static readonly Dictionary<InnSetupGap, string> GapKeys = new()
    {
        [InnSetupGap.ThemeUnreadable] = "Inn.Setup.Gap.ThemeUnreadable",
        [InnSetupGap.NoSetup] = "Inn.Setup.Gap.NoSetup",
        [InnSetupGap.NoKitchens] = "Inn.Setup.Gap.NoKitchens",
    };

    /// <param name="advice">The plan and suggestions to show (<see cref="InnSetupPlanning.Showable"/>); null while they're being worked out.</param>
    /// <param name="data">The inn's data: the dish types' names.</param>
    /// <param name="text">The i18n lookup (<see cref="Loc.Get"/>; the tests pass zh-CN.xaml's strings).</param>
    /// <param name="problemKey">Why the inn can only be partly read (<see cref="PartyMonitor.InnProblem"/>), or null.</param>
    public static InnStripModel Build(InnSetupInputs inputs, InnSetupAdvice? advice, InnData data, Func<string, string> text, string? problemKey = null)
    {
        var lines = new List<InnLine> { InnLine.Of(InnLineStyle.Heading, new InnSegment(text("Inn.Setup.Title"))) };
        if (problemKey is not null)
        {
            lines.Add(InnLine.Of(InnLineStyle.Note, new InnSegment(text(problemKey))));
            return new InnStripModel(lines);
        }
        if (inputs.Gaps.Count > 0)
        {
            lines.AddRange(inputs.Gaps.Select(g => InnLine.Of(InnLineStyle.Note, new InnSegment(text(GapKeys[g])))));
            return new InnStripModel(lines);
        }
        // A rule the planner couldn't take in: the plan ignores it, so the player must know.
        lines.AddRange(inputs.RulesNotUnderstood.Select(r => InnLine.Of(InnLineStyle.Note, new InnSegment(string.Format(text("Inn.Setup.RuleNotUnderstood"), r)))));
        if (advice is null)
        {
            lines.Add(InnLine.Of(InnLineStyle.Dim, new InnSegment(text("Inn.Setup.Working"))));
            return new InnStripModel(lines);
        }
        lines.AddRange(Blocks(advice.Suggestions, data, text));
        if (!advice.Plan.RulesMet) lines.Add(InnLine.Of(InnLineStyle.Note, new InnSegment(text("Inn.Setup.RulesUnmet"))));
        return new InnStripModel(lines);
    }

    private static InnSegment Chef(OperatorInfo chef) => new(chef.NameZh, OperatorPicture + chef.Id);

    private static InnSegment Dish(Dish dish) => new(dish.Zh, dish.Id);

    private static InnSegment Drink(InnDrink drink) => new(drink.Zh, drink.Id);

    private static IEnumerable<InnLine> Blocks(InnSuggestions suggestions, InnData data, Func<string, string> text)
    {
        foreach (var kitchen in suggestions.Kitchens.OrderBy(k => k.Number))
        {
            string name = text($"Inn.Kitchen.{kitchen.Number}");
            if (kitchen.Unused)
            {
                yield return InnLine.Of(InnLineStyle.Subheading, new InnSegment(string.Format(text("Inn.Setup.KitchenUnused"), name)));
                continue;
            }
            var types = kitchen.Types.Select(t => data.DishTypes.FirstOrDefault(x => x.Id == t)?.Zh).OfType<string>().ToList();
            yield return InnLine.Of(InnLineStyle.Subheading, new InnSegment(types.Count == 0
                ? name
                : string.Format(text("Inn.Setup.KitchenTypes"), name, string.Join(text("Inn.Setup.TypeSeparator"), types))));
            foreach (var row in Rows(text("Inn.Setup.Chefs"), kitchen.Chefs.Select(Chef), kitchen.ChefSubstitutes.Select(Chef), 1, text))
                yield return row;
            foreach (var row in Rows(text("Inn.Setup.Cook"), kitchen.Dishes.Select(d => Dish(d.Dish)), kitchen.DishSubstitutes.Select(d => Dish(d.Dish)), 1, text))
                yield return row;
        }
        // Only when no kitchen can cook: the menu on its own.
        foreach (var row in Rows(text("Inn.Setup.Cook"), suggestions.Unplaced.Select(d => Dish(d.Dish)), [], 0, text))
            yield return row;
        foreach (var row in Rows(text("Inn.Setup.Drinks"), suggestions.Drinks.Select(d => Drink(d.Drink)), suggestions.DrinkSubstitutes.Select(d => Drink(d.Drink)), 0, text))
            yield return row;
    }

    /// <summary>"label a b c" and under it "可换：d e"; nothing for no names, no 可换 row for no substitutes.</summary>
    private static IEnumerable<InnLine> Rows(string label, IEnumerable<InnSegment> names, IEnumerable<InnSegment> substitutes, int indent, Func<string, string> text)
    {
        var main = names.ToList();
        if (main.Count == 0) yield break;
        yield return Row(label, main, indent, text);
        var others = substitutes.ToList();
        // Under its row, as the owner drew it: a kitchen's rows and their 可换 one step in, the drinks' 可换 one step in.
        if (others.Count > 0) yield return Row(text("Inn.Setup.Substitutes"), others, Math.Max(indent, 1), text);
    }

    /// <summary>A labelled row of names: each name kept with the space after it, so the row wraps between names.</summary>
    private static InnLine Row(string label, IReadOnlyList<InnSegment> names, int indent, Func<string, string> text)
    {
        var segments = new List<InnSegment> { new(label) };
        for (int i = 0; i < names.Count; i++)
        {
            if (i == names.Count - 1)
            {
                segments.Add(names[i]);
                break;
            }
            segments.Add(names[i].Kept());
            segments.Add(new InnSegment(text("Inn.Between")));
        }
        return new InnLine(InnLineStyle.Normal, segments) { Indent = indent, Hanging = true };
    }

    /// <summary>
    /// What to drag, size and see through while the menu is open out of the inn: three kitchens of
    /// two chefs, each with three substitutes, three dishes and three substitutes, then eight drinks
    /// and three substitutes, as large as the real panel gets at its fullest.
    /// </summary>
    public static InnStripModel Sample(InnData? data, IReadOnlyList<OperatorInfo> roster, Func<string, string> text)
    {
        var lines = new List<InnLine> { InnLine.Of(InnLineStyle.Heading, new InnSegment(text("Inn.Setup.Sample"))) };
        if (data is null) return new InnStripModel(lines);
        var cooks = roster.Where(o => data.ChefBonusesOf(o).Count > 0).ToList();
        int per = InnSuggester.MaxSubstitutes, chefs = 2 + per, dishes = 3 + per;
        var kitchens = Enumerable.Range(1, KitchenMath.MaxKitchens).Select(k =>
        {
            var people = cooks.Skip((k - 1) * chefs).Take(chefs).ToList();
            var cooked = data.Dishes.Skip((k - 1) * dishes).Take(dishes).Select(d => new MenuDish(d, null)).ToList();
            return new KitchenSuggestion(k, people.Take(2).ToList(), people.Skip(2).ToList(), cooked.Take(3).ToList(), cooked.Skip(3).ToList(),
                [.. cooked.Take(3).Select(d => d.Dish.Type).Distinct()]);
        }).ToList();
        var drinks = data.Drinks.Select(d => new MenuDrink(d, null)).ToList();
        lines.AddRange(Blocks(new InnSuggestions(kitchens, [], drinks.Take(MenuRules.MostSeen).ToList(), drinks.Skip(MenuRules.MostSeen).Take(per).ToList()), data, text));
        return new InnStripModel(lines);
    }
}
