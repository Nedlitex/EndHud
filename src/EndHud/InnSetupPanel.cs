using EndHud.Vision.Inn;

namespace EndHud;

/// <summary>
/// Works out the setup panel's plan off the UI thread (the planner takes about a second), once per
/// change of its inputs (<see cref="InnSetupInputs.Key"/>): the setup screens are read every two
/// seconds, and the same inputs are never planned twice. A plan for older inputs is kept until the
/// new one is ready, and is shown meanwhile only while everything it names is still available
/// (<see cref="Showable"/>).
/// </summary>
internal sealed class InnSetupPlanning(Func<InnPlanInput, InnPlan> plan, Action<string>? log = null)
{
    private readonly object _lock = new();
    private readonly Action<string> _log = log ?? (m => DiagnosticLog.Throttled("inn-setup-plan", TimeSpan.FromSeconds(30), m, "ERROR"));
    private string? _requested;
    private InnPlan? _plan;
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
    public InnPlan? Showable(InnSetupInputs inputs)
    {
        if (inputs.Input is not { } input) return null;
        InnPlan? plan;
        string? key;
        lock (_lock) (plan, key) = (_plan, _planKey);
        if (plan is null) return null;
        if (key == inputs.Key) return plan;
        bool chefs = plan.Kitchens.SelectMany(k => k.Chefs).All(c => input.Chefs.Any(o => o.Id == c.Id));
        bool kitchens = plan.Kitchens.All(k => input.Kitchens.Any(i => i.Number == k.Number && i.Places >= k.Chefs.Count))
                        && input.Kitchens.All(i => plan.Kitchens.Any(k => k.Number == i.Number));
        bool dishes = plan.Dishes.All(d => input.Dishes.Any(o => o.Dish.Id == d.Dish.Id));
        bool drinks = plan.Drinks.All(d => input.Drinks.Any(o => o.Drink.Id == d.Drink.Id));
        return chefs && kitchens && dishes && drinks ? plan : null;
    }
}

/// <summary>
/// The setup panel's words (plan S6, C1, C5, C12–C14), every string from i18n/zh-CN.xaml: one line
/// per kitchen with the chefs to put there (avatar and name), then the menu's dishes and the
/// drinks (picture and name). Nothing else: no figures, no swap hints. What's missing is said
/// instead of a plan, and an unreadable theme says only that.
/// </summary>
internal static class InnSetupView
{
    /// <summary>Prefix of an operator's picture id: their avatar, not a dish's picture.</summary>
    public const string OperatorPicture = "op:";

    private static readonly Dictionary<InnSetupGap, string> GapKeys = new()
    {
        [InnSetupGap.ThemeUnreadable] = "Inn.Setup.Gap.ThemeUnreadable",
        [InnSetupGap.NoSetup] = "Inn.Setup.Gap.NoSetup",
        [InnSetupGap.NoKitchens] = "Inn.Setup.Gap.NoKitchens",
        [InnSetupGap.NoChefs] = "Inn.Setup.Gap.NoChefs",
        [InnSetupGap.NoMenu] = "Inn.Setup.Gap.NoMenu",
        [InnSetupGap.NoDrinks] = "Inn.Setup.Gap.NoDrinks",
        [InnSetupGap.MenuRulesUnreadable] = "Inn.Setup.Gap.MenuRulesUnreadable",
    };

    /// <param name="plan">The plan to show (<see cref="InnSetupPlanning.Showable"/>); null while it's being worked out.</param>
    /// <param name="text">The i18n lookup (<see cref="Loc.Get"/>; the tests pass zh-CN.xaml's strings).</param>
    /// <param name="problemKey">Why the inn can only be partly read (<see cref="PartyMonitor.InnProblem"/>), or null.</param>
    public static InnStripModel Build(InnSetupInputs inputs, InnPlan? plan, Func<string, string> text, string? problemKey = null)
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
        if (plan is null)
        {
            lines.Add(InnLine.Of(InnLineStyle.Dim, new InnSegment(text("Inn.Setup.Working"))));
            return new InnStripModel(lines);
        }
        lines.AddRange(PlanLines(plan, text));
        if (!plan.RulesMet) lines.Add(InnLine.Of(InnLineStyle.Note, new InnSegment(text("Inn.Setup.RulesUnmet"))));
        return new InnStripModel(lines);
    }

    private static IEnumerable<InnLine> PlanLines(InnPlan plan, Func<string, string> text)
    {
        foreach (var kitchen in plan.Kitchens.OrderBy(k => k.Number))
        {
            var segments = new List<InnSegment> { new InnSegment(string.Format(text("Inn.Setup.Kitchen"), text($"Inn.Kitchen.{kitchen.Number}"))).Kept() };
            if (kitchen.Chefs.Count == 0) segments.Add(new InnSegment(text("Inn.Nothing")));
            for (int i = 0; i < kitchen.Chefs.Count; i++)
            {
                if (i > 0) segments.Add(new InnSegment(text("Inn.Between")));
                segments.Add(new InnSegment(kitchen.Chefs[i].NameZh, OperatorPicture + kitchen.Chefs[i].Id));
            }
            yield return new InnLine(InnLineStyle.Normal, segments);
        }
        yield return List(text("Inn.Setup.Menu"), plan.Dishes.Select(d => new InnSegment(d.Dish.Zh, d.Dish.Id)), text);
        if (plan.Drinks.Count > 0) yield return List(text("Inn.Setup.Drinks"), plan.Drinks.Select(d => new InnSegment(d.Drink.Zh, d.Drink.Id)), text);
    }

    private static InnLine List(string label, IEnumerable<InnSegment> items, Func<string, string> text)
    {
        var segments = new List<InnSegment> { new InnSegment(label).Kept() };
        foreach (var (item, i) in items.Select((x, i) => (x, i)))
        {
            if (i > 0) segments.Add(new InnSegment(text("Inn.Between")));
            segments.Add(item);
        }
        return new InnLine(InnLineStyle.Normal, segments);
    }

    /// <summary>
    /// What to drag, size and see through while the menu is open out of the inn: three kitchens of
    /// two chefs and a full menu, the size the real panel is at its fullest.
    /// </summary>
    public static InnStripModel Sample(InnData? data, IReadOnlyList<EndHud.Vision.Operators.OperatorInfo> roster, Func<string, string> text)
    {
        var lines = new List<InnLine> { InnLine.Of(InnLineStyle.Heading, new InnSegment(text("Inn.Setup.Sample"))) };
        var cooks = data is null ? [] : roster.Where(o => data.ChefBonusesOf(o).Count > 0).Take(2 * KitchenMath.MaxKitchens).ToList();
        for (int k = 1; k <= KitchenMath.MaxKitchens; k++)
        {
            var chefs = cooks.Skip((k - 1) * 2).Take(2).ToList();
            lines.Add(new InnLine(InnLineStyle.Normal, [
                new InnSegment(string.Format(text("Inn.Setup.Kitchen"), text($"Inn.Kitchen.{k}"))).Kept(),
                .. chefs.SelectMany((c, i) => i == 0
                    ? new[] { new InnSegment(c.NameZh, OperatorPicture + c.Id) }
                    : [new InnSegment(text("Inn.Between")), new InnSegment(c.NameZh, OperatorPicture + c.Id)]),
                .. chefs.Count == 0 ? new[] { new InnSegment(text("Inn.Nothing")) } : []]));
        }
        lines.Add(List(text("Inn.Setup.Menu"), (data?.Dishes ?? []).Take(8).Select(d => new InnSegment(d.Zh, d.Id)), text));
        lines.Add(List(text("Inn.Setup.Drinks"), (data?.Drinks ?? []).Take(3).Select(d => new InnSegment(d.Zh, d.Id)), text));
        return new InnStripModel(lines);
    }
}
