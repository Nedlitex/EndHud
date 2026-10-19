using EndHud.Vision.Inn;
using EndHud.Vision.Operators;

namespace EndHud;

/// <summary>What the setup panel shows: the plan, and its tier lists for a player who lacks some of what it names.</summary>
internal sealed record InnSetupAdvice(InnPlan Plan, InnTiers Tiers)
{
    /// <summary>The plan for <paramref name="input"/> and its tier lists (about two seconds over the whole roster: off the UI thread).</summary>
    public static InnSetupAdvice For(InnPlanner planner, InnPlanInput input)
    {
        var plan = planner.Plan(input);
        return new InnSetupAdvice(plan, InnTierList.Of(planner, input, plan));
    }
}

/// <summary>
/// Works out the setup panel's plan and tiers off the UI thread (about two seconds), once per
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

/// <summary>
/// The setup panel's words (plan S6; owner, 2026-10-19), every string from i18n/zh-CN.xaml: tier
/// lists a player follows with what they own. One line per kitchen, with what it cooks best and its
/// chefs in tiers T1, T2, T3 (avatar and name); then the menu's dishes in tiers (T1 is the plan's
/// menu, meeting every rule) and the drinks in tiers (picture and name). Nothing else: no figures.
/// What's missing (the theme, the kitchens) is said instead, and an unreadable theme says only that.
/// Nothing asks the player to open a screen or scroll to feed it.
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
    };

    /// <param name="advice">The plan and tiers to show (<see cref="InnSetupPlanning.Showable"/>); null while they're being worked out.</param>
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
        if (advice is null)
        {
            lines.Add(InnLine.Of(InnLineStyle.Dim, new InnSegment(text("Inn.Setup.Working"))));
            return new InnStripModel(lines);
        }
        lines.AddRange(TierLines(advice.Tiers, data, text));
        if (!advice.Plan.RulesMet) lines.Add(InnLine.Of(InnLineStyle.Note, new InnSegment(text("Inn.Setup.RulesUnmet"))));
        return new InnStripModel(lines);
    }

    private static InnSegment Chef(OperatorInfo chef) => new(chef.NameZh, OperatorPicture + chef.Id);

    private static IEnumerable<InnLine> TierLines(InnTiers tiers, InnData data, Func<string, string> text)
    {
        foreach (var kitchen in tiers.Kitchens.OrderBy(k => k.Number))
        {
            string name = text($"Inn.Kitchen.{kitchen.Number}");
            string label = kitchen.Role is { } role && data.DishTypes.FirstOrDefault(t => t.Id == role)?.Zh is { } type
                ? string.Format(text("Inn.Setup.KitchenRole"), name, type)
                : string.Format(text("Inn.Setup.Kitchen"), name);
            yield return Tiered(label, kitchen.Tiers.Select(t => t.Select(Chef).ToList()).ToList(), text);
        }
        yield return Tiered(text("Inn.Setup.Menu"), tiers.Dishes.Select(t => t.Select(d => new InnSegment(d.Dish.Zh, d.Dish.Id)).ToList()).ToList(), text);
        if (tiers.Drinks.Any(t => t.Count > 0))
            yield return Tiered(text("Inn.Setup.Drinks"), tiers.Drinks.Select(t => t.Select(d => new InnSegment(d.Drink.Zh, d.Drink.Id)).ToList()).ToList(), text);
    }

    /// <summary>"label T1 a b　T2 c d　T3 e": each tier's mark kept with its first name; an empty tier left out; a dash for none at all.</summary>
    private static InnLine Tiered(string label, IReadOnlyList<List<InnSegment>> tiers, Func<string, string> text)
    {
        var segments = new List<InnSegment> { new InnSegment(label).Kept() };
        bool any = false;
        for (int t = 0; t < tiers.Count; t++)
        {
            if (tiers[t].Count == 0) continue;
            if (any) segments.Add(new InnSegment(text("Inn.Between")));
            segments.Add(new InnSegment(text($"Inn.Setup.Tier.{t + 1}")).Kept());
            for (int i = 0; i < tiers[t].Count; i++)
            {
                if (i > 0) segments.Add(new InnSegment(text("Inn.Setup.Space")));
                segments.Add(tiers[t][i]);
            }
            any = true;
        }
        if (!any) segments.Add(new InnSegment(text("Inn.Nothing")));
        return new InnLine(InnLineStyle.Normal, segments);
    }

    /// <summary>
    /// What to drag, size and see through while the menu is open out of the inn: three kitchens with
    /// three tiers of chefs, the menu's tiers and the drinks', the size the real panel is at its fullest.
    /// </summary>
    public static InnStripModel Sample(InnData? data, IReadOnlyList<OperatorInfo> roster, Func<string, string> text)
    {
        var lines = new List<InnLine> { InnLine.Of(InnLineStyle.Heading, new InnSegment(text("Inn.Setup.Sample"))) };
        if (data is null) return new InnStripModel(lines);
        var cooks = roster.Where(o => data.ChefBonusesOf(o).Count > 0).ToList();
        var types = data.DishTypes.ToList();
        for (int k = 1; k <= KitchenMath.MaxKitchens; k++)
        {
            var chefs = cooks.Skip((k - 1) * 2 * InnTierList.MaxPerTier).Take(2 * InnTierList.MaxPerTier).ToList();
            List<List<InnSegment>> tiers = [[.. chefs.Take(2).Select(Chef)], [.. chefs.Skip(2).Take(2).Select(Chef)], [.. chefs.Skip(4).Take(2).Select(Chef)]];
            string label = types.Count >= k
                ? string.Format(text("Inn.Setup.KitchenRole"), text($"Inn.Kitchen.{k}"), types[k - 1].Zh)
                : string.Format(text("Inn.Setup.Kitchen"), text($"Inn.Kitchen.{k}"));
            lines.Add(Tiered(label, tiers, text));
        }
        var dishes = data.Dishes.Select(d => new InnSegment(d.Zh, d.Id)).ToList();
        lines.Add(Tiered(text("Inn.Setup.Menu"), [[.. dishes.Take(MenuRules.MostSeen)], [.. dishes.Skip(8).Take(InnTierList.MaxPerTier)], [.. dishes.Skip(11).Take(InnTierList.MaxPerTier)]], text));
        var drinks = data.Drinks.Select(d => new InnSegment(d.Zh, d.Id)).ToList();
        lines.Add(Tiered(text("Inn.Setup.Drinks"), [[.. drinks.Take(3)], [.. drinks.Skip(3).Take(InnTierList.MaxPerTier)], [.. drinks.Skip(6).Take(InnTierList.MaxPerTier)]], text));
        return new InnStripModel(lines);
    }
}
