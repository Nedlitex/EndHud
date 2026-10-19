namespace EndHud.Vision.Inn;

/// <summary>A drink on the session's menu and how many are left.</summary>
/// <param name="Stock">Null: unlimited (库存∞, and potable water always).</param>
public sealed record MenuDrink(InnDrink Drink, int? Stock);

/// <summary>Where something that meets a gourmand's request is.</summary>
public enum GourmandSource
{
    /// <summary>The player is carrying it.</summary>
    Carried,

    /// <summary>On the counter.</summary>
    Counter,

    /// <summary>In a kitchen's queue.</summary>
    Queued,

    /// <summary>Not made yet: queue it (in <see cref="GourmandOption.Kitchen"/>).</summary>
    ToCook,

    /// <summary>A drink, from the drink bar.</summary>
    DrinkBar,
}

/// <summary>A dish or drink that meets a request, where it is and how soon it can be served.</summary>
/// <param name="Kitchen">For <see cref="GourmandSource.Queued"/> and <see cref="GourmandSource.ToCook"/>: which kitchen.</param>
/// <param name="ReadyIn">Seconds until it's ready to serve: 0 when carried, on the counter or a drink.</param>
public sealed record GourmandOption(Dish? Dish, InnDrink? Drink, GourmandSource From, int? Kitchen, double ReadyIn)
{
    public string Id => Dish?.Id ?? Drink!.Id;

    public bool Meets(GourmandCondition condition) => Dish is not null ? condition.MetBy(Dish) : condition.MetBy(Drink!);
}

/// <summary>One request: what it says, in plain words, and what on the menu meets it, best first.</summary>
/// <param name="Options">Empty when nothing on the menu meets it.</param>
public sealed record GourmandRequestAdvice(GourmandRequest Request, string Description, IReadOnlyList<GourmandOption> Options);

/// <param name="Serve">One dish or drink per request, in the order to serve them so each fills a request perfectly; null when some request can't be met.</param>
public sealed record GourmandAdvice(IReadOnlyList<GourmandRequestAdvice> Requests, IReadOnlyList<GourmandOption>? Serve);

/// <summary>
/// What to give a gourmand (老饕, plan S5, C4): each request in plain words
/// (<see cref="GourmandCondition.Describe"/>) and the menu's dishes and drinks that meet it, ranked:
/// what's carried or on the counter first, then what comes out of a kitchen soonest (already
/// queued, or queued now in the kitchen that has it out soonest). For a gourmand with two requests,
/// which two to serve and in which order: a dish fills the first request it meets (the players'
/// table's rule), so a dish meeting both must come after the one meeting only the first.
/// </summary>
public sealed class GourmandAdvisor(DispatchAdvisor dispatch)
{
    /// <summary>A gourmand asks for one or two things (the research note: 2, 2 and 1 requests).</summary>
    public const int MaxRequests = 2;

    /// <summary>Options weighed per request when choosing what to serve together (the best few are plenty).</summary>
    private const int WeighedPerRequest = 12;

    /// <summary>The advice for a gourmand's <paramref name="requests"/>.</summary>
    /// <exception cref="FormatException">A request's condition can't be read (the data's own checks prevent this).</exception>
    public GourmandAdvice Advise(IReadOnlyList<GourmandRequest> requests, InnState state, IReadOnlyList<MenuDrink> drinks)
    {
        if (requests.Count is < 1 or > MaxRequests)
            throw new ArgumentException($"{requests.Count} requests; a gourmand asks for 1 to {MaxRequests}.", nameof(requests));
        var conditions = requests.Select(r => GourmandCondition.Parse(r.When, dispatch.Kitchens.Inn)).ToList();
        var instances = conditions.Select(c => Instances(c, state, drinks).ToList()).ToList();
        var advice = requests.Select((r, i) => new GourmandRequestAdvice(r, conditions[i].Describe(), Ranked(instances[i]))).ToList();
        instances = [.. instances.Select(i => i.Take(WeighedPerRequest).ToList())];
        return new GourmandAdvice(advice, instances.Any(i => i.Count == 0) ? null : BestServing(conditions, instances));
    }

    /// <summary>The menu's dishes and drinks that meet <paramref name="condition"/>, best first, each dish once from where it's soonest.</summary>
    public IReadOnlyList<GourmandOption> Options(GourmandCondition condition, InnState state, IReadOnlyList<MenuDrink> drinks) =>
        Ranked(Instances(condition, state, drinks));

    private static List<GourmandOption> Ranked(IEnumerable<GourmandOption> instances) =>
        [.. instances.GroupBy(o => o.Id).Select(g => g.First()).OrderBy(Rank)];

    /// <summary>Carried, then the counter, then soonest; drinks keep the menu's order.</summary>
    private static (int, double) Rank(GourmandOption o) => (o.From is GourmandSource.Carried or GourmandSource.Counter ? (int)o.From : 2, o.ReadyIn);

    /// <summary>Every single thing that meets <paramref name="condition"/>, best first: one per dish carried, on the counter or queued, and one to cook per menu dish.</summary>
    private IEnumerable<GourmandOption> Instances(GourmandCondition condition, InnState state, IReadOnlyList<MenuDrink> drinks)
    {
        if (condition.ForDrink)
            return drinks.Where(d => d.Stock is not <= 0 && condition.MetBy(d.Drink)).Select(d => new GourmandOption(null, d.Drink, GourmandSource.DrinkBar, null, 0));

        var options = new List<GourmandOption>();
        options.AddRange(state.Carried.Where(condition.MetBy).Select(d => new GourmandOption(d, null, GourmandSource.Carried, null, 0)));
        options.AddRange(state.Counter.Where(condition.MetBy).Select(d => new GourmandOption(d, null, GourmandSource.Counter, null, 0)));
        foreach (var queue in state.Queues)
            for (int slot = 0; slot < queue.Dishes.Count; slot++)
                if (condition.MetBy(queue.Dishes[slot].Dish))
                    options.Add(new GourmandOption(queue.Dishes[slot].Dish, null, GourmandSource.Queued, queue.Kitchen, queue.SecondsUntilOut(slot)));
        foreach (var menu in state.Menu.Where(m => m.Stock is not <= 0 && condition.MetBy(m.Dish)))
        {
            // The kitchen with room that has it out soonest; with none free, the one whose queue clears first.
            var (kitchen, readyIn) = dispatch.SoonestOut(state, menu.Dish)
                ?? state.Queues.Select(q => (q.Kitchen, q.SecondsUntilEmpty + dispatch.Kitchens.CookSeconds(q.Kitchen, menu.Dish))).MinBy(x => x.Item2);
            options.Add(new GourmandOption(menu.Dish, null, GourmandSource.ToCook, kitchen, readyIn));
        }
        return options.OrderBy(Rank).ThenBy(o => o.Id, StringComparer.Ordinal);
    }

    /// <summary>
    /// One thing per request, served in an order that fills every request perfectly, ready soonest
    /// (all of them ready, then their total, then the best ranked). Null when no choice does.
    /// </summary>
    private static IReadOnlyList<GourmandOption>? BestServing(IReadOnlyList<GourmandCondition> conditions, IReadOnlyList<List<GourmandOption>> instances)
    {
        IReadOnlyList<GourmandOption>? best = null;
        (double, double) bestScore = (double.MaxValue, double.MaxValue);
        foreach (var pick in Picks(instances, 0, []))
        {
            if (ServingOrder(conditions, pick) is not { } order) continue;
            var score = (pick.Max(o => o.ReadyIn), pick.Sum(o => o.ReadyIn));
            if (score.CompareTo(bestScore) < 0)
            {
                best = order;
                bestScore = score;
            }
        }
        return best;
    }

    /// <summary>Every choice of one per request, a thing carried, on the counter or queued used at most once.</summary>
    private static IEnumerable<List<GourmandOption>> Picks(IReadOnlyList<List<GourmandOption>> instances, int request, List<GourmandOption> chosen)
    {
        if (request == instances.Count)
        {
            yield return [.. chosen];
            yield break;
        }
        foreach (var option in instances[request])
        {
            bool single = option.From is GourmandSource.Carried or GourmandSource.Counter or GourmandSource.Queued;
            if (single && chosen.Any(c => ReferenceEquals(c, option))) continue;
            chosen.Add(option);
            foreach (var pick in Picks(instances, request + 1, chosen)) yield return pick;
            chosen.RemoveAt(chosen.Count - 1);
        }
    }

    /// <summary>
    /// The order to serve <paramref name="items"/> in so each fills its own request: served one by
    /// one, a thing fills the first request not yet filled that it meets. The soonest ready first
    /// where the order doesn't matter; null when no order fills every request.
    /// </summary>
    public static IReadOnlyList<GourmandOption>? ServingOrder(IReadOnlyList<GourmandCondition> requests, IReadOnlyList<GourmandOption> items)
    {
        if (items.Count != requests.Count) return null;
        foreach (var order in Orders([.. items.OrderBy(i => i.ReadyIn)]))
        {
            var filled = new bool[requests.Count];
            bool perfect = true;
            foreach (var item in order)
            {
                int request = Enumerable.Range(0, requests.Count).FirstOrDefault(r => !filled[r] && item.Meets(requests[r]), -1);
                if (request < 0)
                {
                    perfect = false;
                    break;
                }
                filled[request] = true;
            }
            if (perfect) return order;
        }
        return null;
    }

    /// <summary>Every order of <paramref name="items"/>, the given one first.</summary>
    private static IEnumerable<List<GourmandOption>> Orders(List<GourmandOption> items)
    {
        if (items.Count <= 1)
        {
            yield return items;
            yield break;
        }
        for (int first = 0; first < items.Count; first++)
        {
            var rest = items.Where((_, i) => i != first).ToList();
            foreach (var tail in Orders(rest)) yield return [items[first], .. tail];
        }
    }
}
