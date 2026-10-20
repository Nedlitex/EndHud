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
/// Works out the setup advice off the UI thread, once per change of its inputs (<see cref="InnSetupInputs.Key"/>),
/// in two stages so the advice follows the screen (owner, live 2026-10-19: "the page refreshes too slowly"):
/// <list type="number">
/// <item><b>Fast:</b> the planner's model alone (<see cref="InnSetupAdvice.Fast"/>, well under a second), shown as soon as it's ready.</item>
/// <item><b>Refined:</b> the simulator's choice (<see cref="InnSetupAdvice.For"/>, seconds), which replaces the
/// fast advice only when a panel would show something different (<see cref="InnSetupAdvice.Signature"/>).</item>
/// </list>
/// New inputs cancel the work for the older ones: only the latest inputs' advice is ever stored. A plan for
/// older inputs is kept until the new one is ready, and is shown meanwhile only while it still fits the
/// screen (<see cref="Showable"/>).
/// </summary>
/// <param name="fast">The first, quick advice for an input.</param>
/// <param name="refine">The better advice for an input, stopped by its token; null for one stage only.</param>
/// <param name="log">Where a failure is said (throttled to the log by default).</param>
/// <param name="note">Where each stage's time is said (the log by default).</param>
internal sealed class InnSetupPlanning(Func<InnPlanInput, InnSetupAdvice> fast, Func<InnPlanInput, CancellationToken, InnSetupAdvice>? refine = null,
    Action<string>? log = null, Action<string>? note = null)
{
    private readonly object _lock = new();
    private readonly Action<string> _log = log ?? (m => DiagnosticLog.Throttled("inn-setup-plan", TimeSpan.FromSeconds(30), m, "ERROR"));
    private readonly Action<string> _note = note ?? DiagnosticLog.Info;
    private string? _requested;
    private CancellationTokenSource? _working;
    private InnSetupAdvice? _plan;
    private InnPlanInput? _planInput;
    private string? _planKey;
    private int _started, _replaced;

    /// <summary>Plans started so far (tests).</summary>
    public int Started
    {
        get { lock (_lock) return _started; }
    }

    /// <summary>Times a refined plan replaced the fast one because it differed (tests).</summary>
    public int Replaced
    {
        get { lock (_lock) return _replaced; }
    }

    /// <summary>
    /// Starts working out the advice for <paramref name="inputs"/> on the thread pool, unless they have
    /// none (a gap) or are those already planned or being planned; the work for older inputs is
    /// cancelled. <paramref name="done"/> runs on the worker each time the advice to show changes (the
    /// fast advice; the refined one when it differs) or the work failed (logged). Returns the work
    /// (both stages), or null when none started.
    /// </summary>
    public Task? Request(InnSetupInputs inputs, Action? done = null)
    {
        if (inputs.Input is not { } input) return null;
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
                if (!Store(inputs.Key, input, first, cancel, onlyIfDifferent: false)) return;
                done?.Invoke();
                if (refine is null)
                {
                    _note($"inn: setup advice in {fastMs} ms");
                    return;
                }
                var refined = refine(input, cancel);
                bool replaced = Store(inputs.Key, input, refined, cancel, onlyIfDifferent: true);
                _note($"inn: setup advice in {fastMs} ms (model), refined in {watch.ElapsedMilliseconds} ms (simulator, {(replaced ? "different: shown" : "the same")})");
                if (replaced) done?.Invoke();
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

    /// <summary>
    /// Keeps <paramref name="advice"/> as the plan to show, unless newer inputs were asked for since, or
    /// (<paramref name="onlyIfDifferent"/>, the refined advice) it looks the same as the one shown.
    /// </summary>
    private bool Store(string key, InnPlanInput input, InnSetupAdvice advice, CancellationToken cancel, bool onlyIfDifferent)
    {
        lock (_lock)
        {
            if (cancel.IsCancellationRequested) return false;
            if (onlyIfDifferent && _planKey == key && _plan?.Signature == advice.Signature) return false;
            if (onlyIfDifferent) _replaced++;
            (_plan, _planInput, _planKey) = (advice, input, key);
            return true;
        }
    }

    /// <summary>
    /// The plan to show for <paramref name="inputs"/>: the one worked out for them, else, while theirs
    /// is worked out, the last one (owner, live 2026-10-19: the advice must not flicker or blank as
    /// it's worked out again: it stays on screen until the new one replaces it), so long as it still
    /// fits: every chef it worked around is still placed where it was (a plan for chefs since removed
    /// calls an empty kitchen set: the owner's three empty kitchens, two of them "set", 2026-10-19), and
    /// every chef, dish and drink it names is still there to be picked (a plan naming what's no longer
    /// offered is wrong, not merely old). Null while there's none.
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
        bool stillPlaced = before.Kitchens.All(old => input.Kitchens.FirstOrDefault(k => k.Number == old.Number) is { } now
            && old.Placed.Count <= now.Placed.Count
            && old.Placed.OfType<OperatorInfo>().All(c => now.Placed.Any(p => p?.Id == c.Id)));
        var plan = advice.Plan;
        bool chefs = plan.Kitchens.SelectMany(k => k.Chefs).All(c => c.Id.Length == 0 || input.Chefs.Any(o => o.Id == c.Id));
        bool dishes = plan.Dishes.All(d => input.Dishes.Any(o => o.Dish.Id == d.Dish.Id));
        bool drinks = plan.Drinks.All(d => input.Drinks.Any(o => o.Drink.Id == d.Drink.Id));
        return stillPlaced && chefs && dishes && drinks ? advice : null;
    }
}

/// <summary>Where a piece of setup advice is shown: under a kitchen's card on the setup screen, in the chef picker, on the menu screen.</summary>
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
/// The setup advice's words (owner, live test 2026-10-19), every string from i18n/zh-CN.xaml, as small
/// panels where the game shows the choice they're about (<see cref="InnAdviceLayout"/> places them):
/// <list type="bullet">
/// <item><b>The setup screen:</b> under each kitchen's card, that kitchen's advice only: the dish types it will
/// cook, a "recommended" row with the chefs for its empty places, each with their avatar, and a "substitutes" row for
/// one the player lacks. The chefs already placed are kept and never named (nor suggested anywhere
/// else); a full kitchen says only that it's set.</item>
/// <item><b>A chef picker:</b> the same advice for its kitchen, under a heading naming the kitchen.</item>
/// <item><b>The menu screen:</b> the menu for the chefs actually placed (or, with none placed, for the chefs the
/// plan would pick, and it says so): a dishes row, a tick before those already on the menu, and
/// a drinks row, each with a short substitutes row under it.</item>
/// </list>
/// Nothing anywhere else: not on the theme list, not in a session. What's missing (the theme, the
/// kitchens) is said instead of a guess, in the first spot of the screen.
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

    /// <summary>
    /// The panels for <paramref name="screen"/>: per kitchen on the setup screen, one in a chef picker, one on
    /// the menu screen; none on any other screen (the theme list, the session) or off the inn (null).
    /// </summary>
    /// <param name="chefs">The plan for <see cref="InnSession.SetupInputs"/> (<see cref="InnSetupPlanning.Showable"/>); null while it's worked out.</param>
    /// <param name="menu">The plan for <see cref="InnSession.MenuInputs"/>; null while it's worked out.</param>
    /// <param name="text">The i18n lookup (<see cref="Loc.Get"/>; the tests pass zh-CN.xaml's strings).</param>
    /// <param name="problemKey">Why the inn can only be partly read (<see cref="PartyMonitor.InnProblem"/>), or null.</param>
    public static IReadOnlyList<InnAdvicePanel> ForScreen(InnScreenKind? screen, InnSession session, InnSetupAdvice? chefs, InnSetupAdvice? menu,
        InnData data, Func<string, string> text, string? problemKey = null)
    {
        var first = screen switch
        {
            InnScreenKind.Setup => InnAdviceSpot.Kitchen1,
            InnScreenKind.ChefPicker => InnAdviceSpot.Picker,
            InnScreenKind.Menu => InnAdviceSpot.Menu,
            _ => (InnAdviceSpot?)null,
        };
        if (first is not { } spot) return [];
        if (problemKey is not null) return [new InnAdvicePanel(spot, Note(text(problemKey)))];
        return screen switch
        {
            InnScreenKind.Setup => Setup(session.SetupInputs(), chefs, data, text),
            InnScreenKind.ChefPicker => Picker(session.PickerKitchen, session.SetupInputs(), chefs, data, text),
            _ => Menu(session.MenuInputs(), menu, session.OnMenu, data, text),
        };
    }

    /// <summary>The setup screen: a panel under each kitchen's card.</summary>
    public static IReadOnlyList<InnAdvicePanel> Setup(InnSetupInputs inputs, InnSetupAdvice? advice, InnData data, Func<string, string> text)
    {
        if (inputs.Input is not { } input) return [Gaps(InnAdviceSpot.Kitchen1, inputs, text)];
        return [.. input.Kitchens.OrderBy(k => k.Number).Select(k => new InnAdvicePanel((InnAdviceSpot)k.Number,
            new InnStripModel(advice?.Suggestions.Kitchens.FirstOrDefault(s => s.Number == k.Number) is { } kitchen
                ? KitchenLines(kitchen, data, text)
                : [Working(text)])))];
    }

    /// <summary>A chef picker: its kitchen's advice under a heading naming the kitchen.</summary>
    /// <param name="kitchen">The kitchen the picker is for (<see cref="InnSession.PickerKitchen"/>); null when its title wasn't read.</param>
    public static IReadOnlyList<InnAdvicePanel> Picker(int? kitchen, InnSetupInputs inputs, InnSetupAdvice? advice, InnData data, Func<string, string> text)
    {
        if (inputs.Input is null) return [Gaps(InnAdviceSpot.Picker, inputs, text)];
        if (kitchen is not { } number) return [new InnAdvicePanel(InnAdviceSpot.Picker, Note(text("Inn.Setup.PickerUnknown")))];
        if (!inputs.Input.Kitchens.Any(k => k.Number == number))
            return [new InnAdvicePanel(InnAdviceSpot.Picker, Note(text(GapKeys[InnSetupGap.NoKitchens])))];
        var lines = new List<InnLine> { InnLine.Of(InnLineStyle.Heading, new InnSegment(string.Format(text("Inn.Setup.PickerTitle"), text($"Inn.Kitchen.{number}")))) };
        lines.AddRange(advice?.Suggestions.Kitchens.FirstOrDefault(s => s.Number == number) is { } suggestion
            ? KitchenLines(suggestion, data, text)
            : [Working(text)]);
        return [new InnAdvicePanel(InnAdviceSpot.Picker, new InnStripModel(lines))];
    }

    /// <summary>The menu screen: the menu for the chefs placed, with what's on it already marked.</summary>
    /// <param name="onMenu">The dishes and drinks on the menu now (<see cref="InnSession.OnMenu"/>).</param>
    public static IReadOnlyList<InnAdvicePanel> Menu(InnSetupInputs inputs, InnSetupAdvice? advice, IReadOnlyCollection<string> onMenu, InnData data,
        Func<string, string> text)
    {
        if (inputs.Input is null) return [Gaps(InnAdviceSpot.Menu, inputs, text)];
        var lines = new List<InnLine>();
        // A rule the planner couldn't take in: the plan ignores it, so the player must know.
        lines.AddRange(inputs.RulesNotUnderstood.Select(r => InnLine.Of(InnLineStyle.Note, new InnSegment(string.Format(text("Inn.Setup.RuleNotUnderstood"), r)))));
        if (advice is null)
        {
            lines.Add(Working(text));
            return [new InnAdvicePanel(InnAdviceSpot.Menu, new InnStripModel(lines))];
        }
        lines.Insert(0, InnLine.Of(InnLineStyle.Heading, new InnSegment(text(inputs.ChefsPlanned ? "Inn.Setup.MenuTitleChefsPicked" : "Inn.Setup.MenuTitle"))));
        var suggestions = advice.Suggestions;
        string mark = text("Inn.Setup.OnMenu");
        IReadOnlyList<InnSegment> Marked(InnSegment item) => onMenu.Contains(item.PictureId!) ? [new InnSegment(mark).Kept(), item] : [item];
        var dishSubstitutes = suggestions.Kitchens.SelectMany(k => k.DishSubstitutes).DistinctBy(d => d.Dish.Id).Take(InnSuggester.MaxSubstitutes);
        lines.AddRange(Rows(text("Inn.Setup.Dishes"), advice.Plan.Dishes.Select(d => Marked(Dish(d.Dish))), dishSubstitutes.Select(d => One(Dish(d.Dish))), text));
        lines.AddRange(Rows(text("Inn.Setup.Drinks"), advice.Plan.Drinks.Select(d => Marked(Drink(d.Drink))), suggestions.DrinkSubstitutes.Select(d => One(Drink(d.Drink))), text));
        if (!advice.Plan.RulesMet) lines.Add(InnLine.Of(InnLineStyle.Note, new InnSegment(text("Inn.Setup.RulesUnmet"))));
        // The theme's "sell N choice dishes" out of reach with these chefs: said, so the player isn't surprised.
        if (inputs.Input.ChoiceDishTarget is { } target && advice.Plan.Reasons.FirstOrDefault(r => r.Kind == PlanReasonKind.ChoiceTargetUnreached) is { } unreached)
            lines.Add(InnLine.Of(InnLineStyle.Note, new InnSegment(string.Format(text("Inn.Setup.ChoiceTargetUnreached"), Math.Round(unreached.Value), target))));
        return [new InnAdvicePanel(InnAdviceSpot.Menu, new InnStripModel(lines))];
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
        else lines.AddRange(Rows(text("Inn.Setup.Recommend"), kitchen.Chefs.Select(c => One(Chef(c))), kitchen.ChefSubstitutes.Select(c => One(Chef(c))), text));
        return lines;
    }

    private static InnAdvicePanel Gaps(InnAdviceSpot spot, InnSetupInputs inputs, Func<string, string> text) =>
        new(spot, new InnStripModel([.. inputs.Gaps.Select(g => InnLine.Of(InnLineStyle.Note, new InnSegment(text(GapKeys[g]))))]));

    private static InnStripModel Note(string words) => new([InnLine.Of(InnLineStyle.Note, new InnSegment(words))]);

    private static InnLine Working(Func<string, string> text) => InnLine.Of(InnLineStyle.Dim, new InnSegment(text("Inn.Setup.Working")));

    private static InnSegment Chef(OperatorInfo chef) => new(chef.NameZh, OperatorPicture + chef.Id);

    private static InnSegment Dish(Dish dish) => new(dish.Zh, dish.Id);

    private static InnSegment Drink(InnDrink drink) => new(drink.Zh, drink.Id);

    private static IReadOnlyList<InnSegment> One(InnSegment item) => [item];

    /// <summary>"label a b c" and under it "可换：d e"; nothing for no names, no 可换 row for no substitutes.</summary>
    private static IEnumerable<InnLine> Rows(string label, IEnumerable<IReadOnlyList<InnSegment>> names, IEnumerable<IReadOnlyList<InnSegment>> substitutes, Func<string, string> text)
    {
        var main = names.ToList();
        if (main.Count == 0) yield break;
        yield return Row(label, main, text);
        var others = substitutes.ToList();
        if (others.Count > 0) yield return Row(text("Inn.Setup.Substitutes"), others, text);
    }

    /// <summary>A labelled row of names (a name may carry a mark before it): each kept with the space after it, so the row wraps between names.</summary>
    private static InnLine Row(string label, IReadOnlyList<IReadOnlyList<InnSegment>> names, Func<string, string> text)
    {
        var segments = new List<InnSegment> { new(label) };
        for (int i = 0; i < names.Count; i++)
        {
            var unit = names[i];
            segments.AddRange(unit.Take(unit.Count - 1));
            if (i == names.Count - 1)
            {
                segments.Add(unit[^1]);
                break;
            }
            segments.Add(unit[^1].Kept());
            segments.Add(new InnSegment(text("Inn.Between")));
        }
        return new InnLine(InnLineStyle.Normal, segments) { Hanging = true };
    }
}
