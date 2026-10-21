using EndHud.Vision.Inn;

namespace EndHud;

/// <summary>What a call to the player is about.</summary>
internal enum InnCallKind
{
    /// <summary>Queue <see cref="InnCall.Dish"/> in <see cref="InnCall.Kitchen"/> for <see cref="InnCall.Table"/>'s order.</summary>
    Queue,

    /// <summary>Queue <see cref="InnCall.Dish"/> in <see cref="InnCall.Kitchen"/> to cook ahead (预做): no order claims it yet.</summary>
    CookAhead,

    /// <summary>In <see cref="InnCall.Kitchen"/>, take out <see cref="InnCall.Remove"/> (cooked ahead) and queue <see cref="InnCall.Dish"/> for <see cref="InnCall.Table"/>.</summary>
    Swap,

    /// <summary>The last call: give <see cref="InnCall.Dish"/>, on the counter or carried, to <see cref="InnCall.Table"/>.</summary>
    LastCall,

    /// <summary>Start Full Blaze now.</summary>
    BlazeNow,

    /// <summary>Full Blaze is nearly full: fill every kitchen first.</summary>
    BlazeGetReady,
}

/// <summary>
/// One thing to tell the player during a session, shown as a banner for as long as it stands
/// (<see cref="InnCallHold"/>): the dish to queue next in a kitchen, a swap, the last call, Full Blaze.
/// </summary>
/// <param name="Matches">For <see cref="InnCallKind.LastCall"/>: the dish is the one the table ordered (otherwise a wrong one, still worth some money).</param>
internal sealed record InnCall(InnCallKind Kind, int? Kitchen = null, Dish? Dish = null, int? Table = null, Dish? Remove = null, bool Matches = true)
{
    /// <summary>Who it is: two calls with one key are the same call (its banner stays as it is).</summary>
    public string Key => $"{Kind}:{Kitchen}:{Dish?.Id}:{Table}:{Remove?.Id}";

    /// <summary>Calls are stacked in this order: Full Blaze, then the kitchens by number, then the last call by table.</summary>
    public (int Group, int Number) Order => Kind switch
    {
        InnCallKind.BlazeNow or InnCallKind.BlazeGetReady => (0, 0),
        InnCallKind.LastCall => (2, Table ?? 0),
        _ => (1, Kitchen ?? 0),
    };
}

/// <summary>What <see cref="InnCallHold"/> asks of the session about a kitchen, as it is now.</summary>
/// <param name="Queued">Dishes of an id ever seen put in a kitchen's queue: those in it now and those cooked out of it since the session began.</param>
/// <param name="HasRoom">The kitchen's queue has a free slot.</param>
/// <param name="ReadyIn">Seconds until the dish is out in that kitchen if queued now (what's in its queue + the dish's cook time there).</param>
internal sealed record InnKitchenView(Func<int, string, int> Queued, Func<int, bool> HasRoom, Func<int, Dish, double> ReadyIn);

/// <summary>
/// The kitchens' calls held steady (owner, live 2026-10-20: the suggestions "keep changing during the session").
/// The dispatch advice is worked out afresh on every frame, and a queue time estimated a second off, a reading
/// that jitters, or two near-equal dishes flip what it puts first. A call once shown for a kitchen stays until:
/// <list type="bullet">
/// <item><b>done:</b> its dish was put in that kitchen's queue (<see cref="InnKitchenView.Queued"/> went up), or
/// its order is no longer waiting for a kitchen (queued elsewhere, served, gone);</item>
/// <item><b>no longer valid:</b> the kitchen has no free slot; for a dish to cook ahead, the advice no longer
/// cooks it ahead there (no stock, the end rules, a kitchen nearly full); for a swap, neither the swap nor its
/// order stands; the kitchen no longer cooks;</item>
/// <item><b>clearly beaten:</b> an order (or a swap for one) is called where a dish to cook ahead was; for an
/// order, the advice now has it out at least <see cref="ClearlyEarlierSeconds"/> sooner in another kitchen.</item>
/// </list>
/// Anything else keeps the call on screen: ties and near-equal choices keep the current one. One order is
/// called in one kitchen only. Full Blaze and the last call are the advice's own (it doesn't flicker).
/// </summary>
internal sealed class InnCallHold
{
    /// <summary>An order moves to another kitchen only when it comes out there at least this much sooner.</summary>
    public const double ClearlyEarlierSeconds = 5;

    private readonly Dictionary<int, Held> _held = [];
    private readonly List<string> _changes = [];

    /// <summary>What the last <see cref="Update"/> changed of the calls shown, and why, in words (the replay's trace, InnReplayTests).</summary>
    public IReadOnlyList<string> Changes => _changes;

    /// <param name="Queued">The kitchen's <see cref="InnKitchenView.Queued"/> for the call's dish when it was first shown.</param>
    private sealed record Held(InnCall Call, int Queued);

    /// <summary>Forgets every call (the session ended).</summary>
    public void Clear()
    {
        _held.Clear();
        _changes.Clear();
    }

    /// <summary>The calls to show now, given this moment's advice.</summary>
    public IReadOnlyList<InnCall> Update(DispatchAdvice dispatch, FullBlazeAdvice? blaze, InnKitchenView view)
    {
        // Every order the advice still sends to a kitchen, and every dish it cooks ahead, counted (a table may want
        // two of a dish; a dish may be cooked ahead twice). A call shown claims one, so one is never called twice.
        // A dish to cook ahead is under table 0.
        var wanted = new Dictionary<(string Dish, int Table), int>();
        void Add((string, int) order, int n) => wanted[order] = wanted.GetValueOrDefault(order) + n;
        foreach (var kitchen in dispatch.Kitchens)
        {
            foreach (var q in kitchen.Queue) Add((q.Dish.Id, q.Table ?? 0), 1);
            foreach (var swap in kitchen.Replaces) Add((swap.Put.Id, swap.Table), 1);
        }
        bool Claim(InnCall call)
        {
            if (call.Dish is not { } dish) return true;
            var key = (dish.Id, call.Table ?? 0);
            if (wanted.GetValueOrDefault(key) <= 0) return false;
            Add(key, -1);
            return true;
        }
        void Release(InnCall call)
        {
            if (call.Dish is { } dish) Add((dish.Id, call.Table ?? 0), 1);
        }

        // The calls shown that still stand claim their orders first.
        _changes.Clear();
        var cooking = dispatch.Kitchens.ToDictionary(k => k.Kitchen);
        foreach (var (number, held) in _held.OrderBy(h => h.Key).ToList())
        {
            string? why = !cooking.TryGetValue(number, out var advice) ? "the kitchen no longer cooks"
                : Done(held, view) ? "done (queued)"
                : !Valid(held.Call, advice, wanted, view) ? (view.HasRoom(number) ? "no longer advised" : "the kitchen is full")
                : !Claim(held.Call) ? "its order is called elsewhere"
                : null;
            if (why is null) continue;
            _held.Remove(number);
            _changes.Add($"{held.Call.Key} dropped: {why}");
        }

        foreach (var advice in dispatch.Kitchens.OrderBy(k => k.Kitchen))
        {
            var candidate = Candidate(advice, wanted);
            if (_held.TryGetValue(advice.Kitchen, out var held))
            {
                if (candidate is null || candidate.Key == held.Call.Key || !Beats(candidate, held.Call, dispatch, view)) continue;
                Release(held.Call);
                _changes.Add($"{held.Call.Key} beaten by {candidate.Key}");
            }
            if (candidate is null || !Claim(candidate)) continue;
            _held[advice.Kitchen] = new Held(candidate, candidate.Dish is { } dish ? view.Queued(advice.Kitchen, dish.Id) : 0);
        }

        // An order the advice sends to a kitchen whose call stands for something else: a kitchen with no call takes it,
        // if it has it out no more than ClearlyEarlierSeconds later (the advice swapping two kitchens' orders is no
        // reason to move the one shown, and the new order is called at once all the same).
        foreach (var free in dispatch.Kitchens.Where(k => !_held.ContainsKey(k.Kitchen) && view.HasRoom(k.Kitchen)).OrderBy(k => k.Kitchen))
        {
            var order = dispatch.Kitchens.Where(k => k.Kitchen != free.Kitchen).SelectMany(k => k.Queue)
                .Where(q => q.Table is { } table && wanted.GetValueOrDefault((q.Dish.Id, table)) > 0
                            && view.ReadyIn(free.Kitchen, q.Dish) <= q.ReadyIn + ClearlyEarlierSeconds)
                .OrderBy(q => q.ReadyIn).FirstOrDefault();
            if (order is null) continue;
            var call = new InnCall(InnCallKind.Queue, free.Kitchen, order.Dish, order.Table);
            Claim(call);
            _held[free.Kitchen] = new Held(call, view.Queued(free.Kitchen, order.Dish.Id));
        }

        var calls = new List<InnCall>();
        if (blaze == FullBlazeAdvice.StartNow) calls.Add(new InnCall(InnCallKind.BlazeNow));
        else if (blaze == FullBlazeAdvice.GetReady) calls.Add(new InnCall(InnCallKind.BlazeGetReady));
        calls.AddRange(_held.Values.Select(h => h.Call));
        calls.AddRange(dispatch.Leftovers.Select(l => new InnCall(InnCallKind.LastCall, Dish: l.Dish, Table: l.Table, Matches: l.Matches)));
        return [.. calls.OrderBy(c => c.Order.Group).ThenBy(c => c.Order.Number)];
    }

    /// <summary>The kitchen's first call in this moment's advice: a swap, else the first dish to queue, an order not called elsewhere.</summary>
    private static InnCall? Candidate(KitchenAdvice advice, Dictionary<(string Dish, int Table), int> wanted)
    {
        foreach (var swap in advice.Replaces)
            if (wanted.GetValueOrDefault((swap.Put.Id, swap.Table)) > 0)
                return new InnCall(InnCallKind.Swap, advice.Kitchen, swap.Put, swap.Table, swap.Remove);
        foreach (var q in advice.Queue)
        {
            if (wanted.GetValueOrDefault((q.Dish.Id, q.Table ?? 0)) <= 0) continue; // called in another kitchen
            return q.Table is { } table ? new InnCall(InnCallKind.Queue, advice.Kitchen, q.Dish, table) : new InnCall(InnCallKind.CookAhead, advice.Kitchen, q.Dish);
        }
        return null;
    }

    /// <summary>The call's dish was put in its kitchen's queue since it was shown.</summary>
    private static bool Done(Held held, InnKitchenView view) =>
        held.Call is { Kitchen: { } kitchen, Dish: { } dish } && view.Queued(kitchen, dish.Id) > held.Queued;

    private static bool Valid(InnCall call, KitchenAdvice advice, Dictionary<(string Dish, int Table), int> wanted, InnKitchenView view)
    {
        int kitchen = advice.Kitchen;
        return call.Kind switch
        {
            InnCallKind.Queue => view.HasRoom(kitchen) && wanted.GetValueOrDefault((call.Dish!.Id, call.Table!.Value)) > 0,
            // Still cooked ahead, here or in another kitchen (the advice swapping which kitchen cooks it ahead and which
            // takes an order is no news): a near-equal dish put first doesn't replace it.
            InnCallKind.CookAhead => view.HasRoom(kitchen) && wanted.GetValueOrDefault((call.Dish!.Id, 0)) > 0,
            InnCallKind.Swap => advice.Replaces.Any(s => s.Remove.Id == call.Remove!.Id && s.Put.Id == call.Dish!.Id && s.Table == call.Table)
                                || (view.HasRoom(kitchen) && wanted.GetValueOrDefault((call.Dish!.Id, call.Table!.Value)) > 0),
            _ => false,
        };
    }

    /// <summary>The kitchen's new first call is clearly better than the one shown (which still stands).</summary>
    private static bool Beats(InnCall candidate, InnCall held, DispatchAdvice dispatch, InnKitchenView view)
    {
        bool candidateOrder = candidate.Kind is InnCallKind.Queue or InnCallKind.Swap;
        if (held.Kind == InnCallKind.CookAhead) return candidateOrder;
        if (!candidateOrder) return false;
        // The order shown: is it now out clearly sooner in another kitchen? Then it moves there, and this kitchen
        // gets its next call.
        var dish = held.Dish!;
        double here = view.ReadyIn(held.Kitchen!.Value, dish);
        double elsewhere = dispatch.Kitchens.Where(k => k.Kitchen != held.Kitchen)
            .SelectMany(k => k.Queue.Where(q => q.Table == held.Table && q.Dish.Id == dish.Id).Select(q => q.ReadyIn))
            .DefaultIfEmpty(double.PositiveInfinity).Min();
        return elsewhere + ClearlyEarlierSeconds <= here;
    }
}
