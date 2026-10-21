using EndHud.Vision.Inn;
using EndHud.Vision.Operators;

namespace EndHud;

/// <summary>What the setup advice shows: the plan, per kitchen, with substitutes for a player who lacks some of what it names.</summary>
internal sealed record InnSetupAdvice(InnPlan Plan, InnSuggestions Suggestions)
{
    /// <summary>
    /// The plan the simulator chooses for <paramref name="input"/> (<see cref="InnPlanner.Plan"/>) and its
    /// suggestions: seconds over the whole roster, off the UI thread; stopped by <paramref name="cancel"/>.
    /// </summary>
    public static InnSetupAdvice For(InnPlanner planner, InnPlanInput input, CancellationToken cancel)
    {
        var plan = planner.Plan(input, cancel);
        cancel.ThrowIfCancellationRequested();
        return new InnSetupAdvice(plan, InnSuggester.Of(planner, input, plan));
    }

    /// <summary>
    /// The plan of the model's quick search (<see cref="InnPlanner.PlanQuickly"/>, no simulation) and its
    /// suggestions: what's shown first, while <see cref="For"/> refines it.
    /// </summary>
    public static InnSetupAdvice Fast(InnPlanner planner, InnPlanInput input)
    {
        var plan = planner.PlanQuickly(input);
        return new InnSetupAdvice(plan, InnSuggester.Of(planner, input, plan));
    }

    /// <summary>
    /// Everything of this advice a panel shows (<see cref="InnSetupView"/>): per kitchen its chefs, substitutes,
    /// dishes and types, whether full or unused; the menu's dishes and drinks and their substitutes; whether the
    /// rules are met; the choice dishes short of the objective. Two advices with one signature look the same.
    /// </summary>
    public string Signature
    {
        get
        {
            static string Ids(IEnumerable<string> ids) => string.Join(",", ids);
            var kitchens = Suggestions.Kitchens.OrderBy(k => k.Number).Select(k =>
                $"{k.Number}:{Ids(k.Chefs.Select(c => c.Id))}/{Ids(k.ChefSubstitutes.Select(c => c.Id))}/{Ids(k.Dishes.Select(d => d.Dish.Id))}"
                + $"/{Ids(k.DishSubstitutes.Select(d => d.Dish.Id))}/{Ids(k.Types.Select(t => t.ToString()))}/{k.Placed}/{k.Full}");
            var unreached = Plan.Reasons.FirstOrDefault(r => r.Kind == PlanReasonKind.ChoiceTargetUnreached);
            return string.Join("|", Ids(kitchens), Ids(Plan.Kitchens.OrderBy(k => k.Number).Select(k => $"{k.Number}:{Ids(k.Chefs.Select(c => c.Id))}")),
                Ids(Plan.Dishes.Select(d => d.Dish.Id)), Ids(Plan.Drinks.Select(d => d.Drink.Id)), Ids(Suggestions.DrinkSubstitutes.Select(d => d.Drink.Id)),
                Plan.RulesMet, unreached is null ? "-" : Math.Round(unreached.Value).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}

/// <summary>
/// Decides whether a plan on screen stays (owner, live 2026-10-20: "the recommended recipe keeps changing, it is
/// very hard to follow"): a plan shown stays while it still fits the inputs, and another replaces it only when
/// clearly better. The seam the planning's tests replace.
/// </summary>
internal interface IInnPlanJudge
{
    /// <summary>
    /// <paramref name="shown"/>, worked out for <paramref name="planFor"/>, still fits <paramref name="input"/>: the
    /// chefs it worked around are still placed, every chef it names is there to pick, every dish and drink it names
    /// is offered, and its menu still meets the rules (when it did). <paramref name="chefsAsPlanned"/>: also every
    /// chef placed since is one it named for that kitchen (to keep it as the advice; while a new plan is worked
    /// out, the last one shows meanwhile without this).
    /// </summary>
    bool Fits(InnPlanInput input, InnPlanInput planFor, InnSetupAdvice shown, bool chefsAsPlanned);

    /// <summary><paramref name="candidate"/> beats <paramref name="shown"/> for <paramref name="input"/> by a clear margin.</summary>
    bool ClearlyBetter(InnPlanInput input, InnSetupAdvice candidate, InnSetupAdvice shown);
}

/// <summary>The judge over the planner: the plans compared as the planner compares them, with a margin on income.</summary>
internal sealed class InnPlanJudge(InnPlanner planner) : IInnPlanJudge
{
    /// <summary>
    /// How much more a plan must earn than the one shown to replace it (a share: 5 %), on the same theme match
    /// and objective. Smaller differences are the planner's noise (a quick search, a newly seen dish worth about
    /// the same), not a reason to make the player start over.
    /// </summary>
    public const double ClearlyMoreIncome = 0.05;

    public bool Fits(InnPlanInput input, InnPlanInput planFor, InnSetupAdvice shown, bool chefsAsPlanned)
    {
        var plan = shown.Plan;
        // The chefs it worked around are all still placed where they were (a plan for chefs since removed calls an empty kitchen set).
        bool stillPlaced = planFor.Kitchens.All(old => input.Kitchens.FirstOrDefault(k => k.Number == old.Number) is { } now
            && old.Placed.Count <= now.Placed.Count
            && old.Placed.OfType<OperatorInfo>().All(c => now.Placed.Any(p => p?.Id == c.Id)));
        // A chef placed since is one the plan named for that kitchen (the player following it), never another.
        bool placedAsPlanned = !chefsAsPlanned || input.Kitchens.All(now => now.Placed.OfType<OperatorInfo>().All(c =>
            planFor.Kitchens.FirstOrDefault(k => k.Number == now.Number)?.Placed.Any(p => p?.Id == c.Id) == true
            || plan.Kitchens.FirstOrDefault(k => k.Number == now.Number)?.Chefs.Any(p => p.Id == c.Id) == true));
        bool chefs = plan.Kitchens.SelectMany(k => k.Chefs).All(c => c.Id.Length == 0 || input.Chefs.Any(o => o.Id == c.Id));
        bool dishes = plan.Dishes.All(d => input.Dishes.Any(o => o.Dish.Id == d.Dish.Id));
        bool drinks = plan.Drinks.All(d => input.Drinks.Any(o => o.Drink.Id == d.Drink.Id));
        if (!(stillPlaced && placedAsPlanned && chefs && dishes && drinks)) return false;
        return !plan.RulesMet || planner.MeetsRules(input.Rules, Dishes(input, shown), Drinks(input, shown));
    }

    public bool ClearlyBetter(InnPlanInput input, InnSetupAdvice candidate, InnSetupAdvice shown)
    {
        if (candidate.Plan.Simulated is { } simulated)
        {
            // The simulator's plan against the one shown played in the simulator too, on these inputs.
            var before = planner.Simulate(input, Kitchens(shown), Dishes(input, shown), Drinks(input, shown));
            return Clearly(simulated.Match.CompareTo(before.Match), simulated.MeetsTarget, before.MeetsTarget, simulated.Income, before.Income);
        }
        var now = planner.Evaluate(input, Kitchens(candidate), Dishes(input, candidate), Drinks(input, candidate));
        var old = planner.Evaluate(input, Kitchens(shown), Dishes(input, shown), Drinks(input, shown));
        return Clearly(now.Match.CompareTo(old.Match), now.MeetsTarget, old.MeetsTarget, now.Income, old.Income);
    }

    /// <summary>Matching the theme better, or reaching the objective the other misses, or <see cref="ClearlyMoreIncome"/> more income on equal terms.</summary>
    private static bool Clearly(int match, bool meets, bool meetsBefore, double income, double incomeBefore) =>
        match != 0 ? match > 0
        : meets != meetsBefore ? meets
        : income > incomeBefore * (1 + ClearlyMoreIncome) + InnPlanner.Tolerance;

    private static List<Kitchen> Kitchens(InnSetupAdvice advice) => [.. advice.Plan.Kitchens.Select(k => new Kitchen(k.Number, k.Chefs))];

    /// <summary>The plan's dishes as offered in <paramref name="input"/> (their stock now).</summary>
    private static List<MenuDish> Dishes(InnPlanInput input, InnSetupAdvice advice) =>
        [.. advice.Plan.Dishes.Select(d => input.Dishes.FirstOrDefault(o => o.Dish.Id == d.Dish.Id) ?? d)];

    private static List<MenuDrink> Drinks(InnPlanInput input, InnSetupAdvice advice) =>
        [.. advice.Plan.Drinks.Select(d => input.Drinks.FirstOrDefault(o => o.Drink.Id == d.Drink.Id) ?? d)];
}

/// <summary>
/// Works out the setup advice off the UI thread, once per change of its inputs (<see cref="InnSetupInputs.Key"/>),
/// in two stages so the advice follows the screen (owner, live 2026-10-19: "the page refreshes too slowly"):
/// <list type="number">
/// <item><b>Fast:</b> the planner's model alone (<see cref="InnSetupAdvice.Fast"/>, well under a second).</item>
/// <item><b>Refined:</b> the simulator's choice (<see cref="InnSetupAdvice.For"/>, seconds). With nothing on
/// screen that fits, it's shown directly when it comes within <see cref="RefinedWithin"/>; otherwise the fast
/// advice shows meanwhile.</item>
/// </list>
/// A plan on screen stays (owner, live 2026-10-20: "the recommended recipe keeps changing, it is very hard to
/// follow"): while it still fits the inputs (<see cref="IInnPlanJudge.Fits"/>: the player following it, a card
/// seen while scrolling), a newer plan, fast or refined, replaces it only when clearly better
/// (<see cref="IInnPlanJudge.ClearlyBetter"/>). New inputs cancel the work for the older ones: only the latest
/// inputs' advice is ever stored.
/// </summary>
/// <param name="judge">Whether a plan shown stays (<see cref="InnPlanJudge"/>).</param>
/// <param name="fast">The first, quick advice for an input.</param>
/// <param name="refine">The better advice for an input, stopped by its token; null for one stage only.</param>
/// <param name="log">Where a failure is said (throttled to the log by default).</param>
/// <param name="note">Where each stage's time is said (the log by default).</param>
internal sealed class InnSetupPlanning(IInnPlanJudge judge, Func<InnPlanInput, InnSetupAdvice> fast,
    Func<InnPlanInput, CancellationToken, InnSetupAdvice>? refine = null, Action<string>? log = null, Action<string>? note = null)
{
    /// <summary>
    /// With nothing on screen, the refined advice is shown directly when it's ready this soon after the inputs
    /// changed, rather than the fast advice and then a swap a moment later.
    /// </summary>
    public static readonly TimeSpan RefinedWithin = TimeSpan.FromSeconds(1);

    /// <summary>
    /// While the menu screen keeps showing cards not seen before (the player scrolling the list), a plan on
    /// screen that still fits isn't worked out again; once no new card has shown for this long, it is, once,
    /// and replaces the one shown only when clearly better. A batch of cards scrolled into view a moment apart
    /// otherwise made a new plan each (owner, live 2026-10-20).
    /// </summary>
    public static readonly TimeSpan ScrollSettle = TimeSpan.FromSeconds(1.5);

    private readonly object _lock = new();
    private readonly Action<string> _log = log ?? (m => DiagnosticLog.Throttled("inn-setup-plan", TimeSpan.FromSeconds(30), m, "ERROR"));
    private readonly Action<string> _note = note ?? DiagnosticLog.Info;
    private string? _requested;
    private CancellationTokenSource? _working;
    private InnSetupAdvice? _plan;
    private InnPlanInput? _planInput; // the inputs the plan shown was worked out for
    private string? _planKey; // the latest inputs it was judged to fit
    private int _started, _replaced, _changes;

    /// <summary>Plans started so far (tests).</summary>
    public int Started
    {
        get { lock (_lock) return _started; }
    }

    /// <summary>Times a refined plan replaced the one shown (tests).</summary>
    public int Replaced
    {
        get { lock (_lock) return _replaced; }
    }

    /// <summary>Times the advice on screen changed to look different (<see cref="InnSetupAdvice.Signature"/>; tests).</summary>
    public int Changes
    {
        get { lock (_lock) return _changes; }
    }

    /// <summary>
    /// Starts working out the advice for <paramref name="inputs"/> on the thread pool, unless they have
    /// none (a gap) or are those already planned or being planned, or the player is scrolling the menu
    /// (<see cref="ScrollSettle"/>) and the plan on screen still fits; the work for older inputs is
    /// cancelled. <paramref name="done"/> runs on the worker each time the advice to show changes or the
    /// work failed (logged). Returns the work (both stages), or null when none started.
    /// </summary>
    /// <param name="now">The time now on <see cref="InnClock"/> (the clock of the readings).</param>
    public Task? Request(InnSetupInputs inputs, TimeSpan now, Action? done = null)
    {
        if (inputs.Input is not { } input) return null;
        lock (_lock)
            if (inputs.Key == _requested) return null;
        // Still scrolling: asked again on a later frame or tick, once the list has been still for a moment.
        if (inputs.NewCardAt is { } seen && now - seen < ScrollSettle && ShowingFor(input)) return null;
        CancellationToken cancel;
        lock (_lock)
        {
            if (inputs.Key == _requested) return null;
            _requested = inputs.Key;
            _started++;
            // Not disposed: the cancelled work may still be checking its token; it holds nothing to free.
            _working?.Cancel();
            _working = new CancellationTokenSource();
            cancel = _working.Token;
        }
        return Task.Run(() =>
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var first = fast(input);
                long fastMs = watch.ElapsedMilliseconds;
                cancel.ThrowIfCancellationRequested();
                if (refine is null)
                {
                    if (Offer(inputs.Key, input, first, cancel, refined: false) is Outcome.Shown) done?.Invoke();
                    _note($"inn: setup advice in {fastMs} ms");
                    return;
                }
                Task<InnSetupAdvice>? early = null;
                if (!ShowingFor(input))
                {
                    // Nothing on screen fits: the refined advice directly, if it comes soon enough.
                    early = Task.Run(() => refine(input, cancel));
                    var left = RefinedWithin - watch.Elapsed;
                    if (left > TimeSpan.Zero && Finished(early, left))
                    {
                        var outcome = Offer(inputs.Key, input, early.GetAwaiter().GetResult(), cancel, refined: true);
                        _note($"inn: setup advice in {watch.ElapsedMilliseconds} ms (simulator, shown directly)");
                        if (outcome is Outcome.Shown) done?.Invoke();
                        return;
                    }
                }
                var shownFirst = Offer(inputs.Key, input, first, cancel, refined: false);
                if (shownFirst is Outcome.Cancelled) return;
                if (shownFirst is Outcome.Shown) done?.Invoke();
                var better = early is not null ? early.GetAwaiter().GetResult() : refine(input, cancel);
                var replaced = Offer(inputs.Key, input, better, cancel, refined: true);
                _note($"inn: setup advice in {fastMs} ms (model), refined in {watch.ElapsedMilliseconds} ms (simulator, {Say(replaced)})");
                if (replaced is Outcome.Shown) done?.Invoke();
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            {
                // Newer inputs were asked for: their own work shows its advice.
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                _log($"inn: working out the setup plan failed: {ex}");
                done?.Invoke();
            }
        });
    }

    private enum Outcome
    {
        Cancelled,
        Same,
        Kept,
        Shown,
    }

    private static string Say(Outcome outcome) => outcome switch
    {
        Outcome.Shown => "different: shown",
        Outcome.Kept => "different, not clearly better: kept",
        _ => "the same",
    };

    /// <summary>The task finished within <paramref name="wait"/> (whatever its outcome: the caller reads it).</summary>
    private static bool Finished(Task task, TimeSpan wait)
    {
        try
        {
            return task.Wait(wait);
        }
        catch (AggregateException)
        {
            return true;
        }
    }

    /// <summary>A plan is on screen that fits <paramref name="input"/>.</summary>
    private bool ShowingFor(InnPlanInput input)
    {
        InnSetupAdvice? shown;
        InnPlanInput? shownFor;
        lock (_lock) (shown, shownFor) = (_plan, _planInput);
        return shown is not null && shownFor is not null && judge.Fits(input, shownFor, shown, chefsAsPlanned: true);
    }

    /// <summary>
    /// <paramref name="advice"/> for the latest inputs: shown when nothing fitting is on screen or it's clearly
    /// better than what is (<see cref="IInnPlanJudge"/>); otherwise the plan on screen stays and now stands for
    /// these inputs. Nothing when newer inputs were asked for since.
    /// </summary>
    private Outcome Offer(string key, InnPlanInput input, InnSetupAdvice advice, CancellationToken cancel, bool refined)
    {
        InnSetupAdvice? shown;
        InnPlanInput? shownFor;
        lock (_lock) (shown, shownFor) = (_plan, _planInput);
        bool replace = shown is null || shownFor is null || !judge.Fits(input, shownFor, shown, chefsAsPlanned: true)
                       || (shown.Signature != advice.Signature && judge.ClearlyBetter(input, advice, shown));
        lock (_lock)
        {
            if (cancel.IsCancellationRequested || !ReferenceEquals(shown, _plan)) return Outcome.Cancelled;
            if (!replace)
            {
                _planKey = key;
                return shown!.Signature == advice.Signature ? Outcome.Same : Outcome.Kept;
            }
            bool looksDifferent = shown?.Signature != advice.Signature;
            if (looksDifferent) _changes++;
            if (refined && looksDifferent) _replaced++;
            (_plan, _planInput, _planKey) = (advice, input, key);
            return looksDifferent ? Outcome.Shown : Outcome.Same;
        }
    }

    /// <summary>
    /// The plan to show for <paramref name="inputs"/>: the one judged for them, else, while theirs is worked out,
    /// the last one (owner, live 2026-10-19: the advice must not flicker or blank as it's worked out again) so
    /// long as it still fits them (<see cref="IInnPlanJudge.Fits"/>: a plan for chefs since removed calls an
    /// empty kitchen set, the owner's three empty kitchens, two of them "set", 2026-10-19; a plan naming what's no
    /// longer offered is wrong, not merely old). Null while there's none.
    /// </summary>
    public InnSetupAdvice? Showable(InnSetupInputs inputs)
    {
        if (inputs.Input is not { } input) return null;
        InnSetupAdvice? advice;
        InnPlanInput? before;
        string? key;
        lock (_lock) (advice, before, key) = (_plan, _planInput, _planKey);
        if (advice is null || before is null) return null;
        if (key == inputs.Key) return advice;
        return judge.Fits(input, before, advice, chefsAsPlanned: false) ? advice : null;
    }
}

/// <summary>Where a piece of setup advice is shown: under a kitchen's card on the setup screen.</summary>
internal enum InnAdviceSpot
{
    Kitchen1 = 1,
    Kitchen2 = 2,
    Kitchen3 = 3,
}

/// <summary>One piece of setup advice and where it goes.</summary>
internal sealed record InnAdvicePanel(InnAdviceSpot Spot, InnStripModel Model);

/// <summary>
/// The setup advice's words (owner, live test 2026-10-19), every string from i18n/zh-CN.xaml, as small panels under
/// each kitchen's card on the setup screen (<see cref="InnAdviceLayout"/> places them): that kitchen's advice only, the
/// dish types it will cook, a "recommended" row with the chefs for its empty places, each with their avatar, and a
/// "substitutes" row for one the player lacks. The chefs already placed are kept and never named (nor suggested
/// anywhere else); a full kitchen says only that it's set.
/// Nothing anywhere else: not on the theme list, the chef picker or the menu screen (owner, live 2026-10-21: the
/// recommendation there "refreshes too slowly and it is often out of sync, just delete that for now"), not in a
/// session. What hasn't been read (the theme, the kitchens) shows no panel at all, never a guess and never a "not
/// seen yet" line (owner, live 2026-10-20: "half of the time it is wrong and it is disturbing").
/// </summary>
internal static class InnSetupView
{
    /// <summary>Prefix of an operator's picture id: their avatar, not a dish's picture.</summary>
    public const string OperatorPicture = "op:";

    /// <summary>The panels for <paramref name="screen"/>: per kitchen on the setup screen; none on any other screen or off the inn (null).</summary>
    /// <param name="chefs">The plan for <see cref="InnSession.SetupInputs"/> (<see cref="InnSetupPlanning.Showable"/>); null while it's worked out.</param>
    /// <param name="text">The i18n lookup (<see cref="Loc.Get"/>; the tests pass zh-CN.xaml's strings).</param>
    public static IReadOnlyList<InnAdvicePanel> ForScreen(InnScreenKind? screen, InnSession session, InnSetupAdvice? chefs, InnData data, Func<string, string> text) =>
        screen == InnScreenKind.Setup ? Setup(session.SetupInputs(), chefs, data, text) : [];

    /// <summary>The setup screen: a panel under each kitchen's card.</summary>
    public static IReadOnlyList<InnAdvicePanel> Setup(InnSetupInputs inputs, InnSetupAdvice? advice, InnData data, Func<string, string> text)
    {
        if (inputs.Input is not { } input) return [];
        return [.. input.Kitchens.OrderBy(k => k.Number).Select(k => new InnAdvicePanel((InnAdviceSpot)k.Number,
            new InnStripModel(advice?.Suggestions.Kitchens.FirstOrDefault(s => s.Number == k.Number) is { } kitchen
                ? KitchenLines(kitchen, data, text)
                : [Working(text)])))];
    }

    /// <summary>A kitchen's advice: the dish types it cooks, then its chefs and their substitutes, or that it's set.</summary>
    private static List<InnLine> KitchenLines(KitchenSuggestion kitchen, InnData data, Func<string, string> text)
    {
        var lines = new List<InnLine>();
        var types = kitchen.Types.Select(t => data.DishTypes.FirstOrDefault(x => x.Id == t)?.Zh).OfType<string>().ToList();
        if (types.Count > 0)
            lines.Add(InnLine.Of(InnLineStyle.Subheading, new InnSegment(string.Format(text("Inn.Setup.Cooks"), string.Join(text("Inn.Setup.TypeSeparator"), types)))));
        if (kitchen.Full) lines.Add(InnLine.Of(InnLineStyle.Dim, new InnSegment(text("Inn.Setup.Full"))));
        else if (kitchen.Unused) lines.Add(InnLine.Of(InnLineStyle.Dim, new InnSegment(text("Inn.Setup.KitchenUnused"))));
        else lines.AddRange(Rows(text("Inn.Setup.Recommend"), kitchen.Chefs.Select(Chef), kitchen.ChefSubstitutes.Select(Chef), text));
        return lines;
    }

    private static InnLine Working(Func<string, string> text) => InnLine.Of(InnLineStyle.Dim, new InnSegment(text("Inn.Setup.Working")));

    private static InnSegment Chef(OperatorInfo chef) => new(chef.NameZh, OperatorPicture + chef.Id);

    /// <summary>"label a b c" and under it "可换：d e"; nothing for no names, no 可换 row for no substitutes.</summary>
    private static IEnumerable<InnLine> Rows(string label, IEnumerable<InnSegment> names, IEnumerable<InnSegment> substitutes, Func<string, string> text)
    {
        var main = names.ToList();
        if (main.Count == 0) yield break;
        yield return Row(label, main, text);
        var others = substitutes.ToList();
        if (others.Count > 0) yield return Row(text("Inn.Setup.Substitutes"), others, text);
    }

    /// <summary>A labelled row of names: each kept with the space after it, so the row wraps between names.</summary>
    private static InnLine Row(string label, IReadOnlyList<InnSegment> names, Func<string, string> text)
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
        return new InnLine(InnLineStyle.Normal, segments) { Hanging = true };
    }
}
