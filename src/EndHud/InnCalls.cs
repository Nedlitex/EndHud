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
/// <item><b>no longer valid:</b> the kitchen has no free slot; for a dish to cook ahead, the advice no longer cooks it
/// ahead in that kitchen (the advice keeps it there while it can, <see cref="Kept"/>: so no stock, the end rules, the counter
/// full, an order sent there); for a swap, neither the swap nor its order stands; the kitchen no longer cooks;</item>
/// <item><b>clearly beaten:</b> an order (or a swap for one) is called where a dish to cook ahead was; for an
/// order, the advice now has it out at least <see cref="ClearlyEarlierSeconds"/> sooner in another kitchen (the advice
/// keeps it where it is otherwise, <see cref="Kept"/>): it moves there at once.</item>
/// </list>
/// Anything else keeps the call on screen: ties and near-equal choices keep the current one. One order is
/// called in one kitchen only. Full Blaze and the last call are the advice's own (it doesn't flicker).
/// <para>A call no longer valid stays up for <see cref="GraceSeconds"/> (a dish to cook ahead: <see cref="CookAheadGraceSeconds"/>) first, and stays on if it's valid again by then
/// (owner, live 2026-10-20: "it is still flashing ... should just stay, not flash all the time"): a reading that drops
/// an order for a moment (a card misread as the HUD fades, the panel's queue read a frame short) would otherwise take
/// the banner down and put it up again a frame later, sliding out and in. Done is done at once.</para>
/// </summary>
internal sealed class InnCallHold
{
    /// <summary>An order moves to another kitchen only when it comes out there at least this much sooner.</summary>
    public const double ClearlyEarlierSeconds = DispatchAdvisor.KeepWithinSeconds;

    /// <summary>A call no longer valid is taken down only once it has stayed so this long.</summary>
    public static readonly TimeSpan GraceSeconds = TimeSpan.FromSeconds(1.5);

    /// <summary>
    /// A dish to cook ahead the advice no longer cooks ahead in its kitchen (the counter's room forecast short, as dishes
    /// are served and cooked out it comes back) stays this long first: on test_yuanyuan4 kitchen 3's dish lost the counter's
    /// last places for 3.3 s (189.3 s) and the kitchen then got another dish; on test_yuanyuan3 for 5.6 s (271.0 s), back
    /// 0.6 s after it was taken down. Nothing is urgent about a dish to cook ahead; its kitchen full still takes it down
    /// after <see cref="GraceSeconds"/>, and an order takes its place at once.
    /// </summary>
    public static readonly TimeSpan CookAheadGraceSeconds = TimeSpan.FromSeconds(10);

    private const string NoLongerAdvised = "no longer advised";

    private static TimeSpan GraceFor(InnCall call, string why) =>
        call.Kind == InnCallKind.CookAhead && why == NoLongerAdvised ? CookAheadGraceSeconds : GraceSeconds;

    private readonly Dictionary<int, Held> _held = [];
    private readonly List<string> _changes = [];

    /// <summary>What the last <see cref="Update"/> changed of the calls shown, and why, in words (the replay's trace, InnReplayTests).</summary>
    public IReadOnlyList<string> Changes => _changes;

    /// <param name="Queued">The kitchen's <see cref="InnKitchenView.Queued"/> for the call's dish when it was first shown.</param>
    /// <param name="InvalidSince">When it was first found no longer valid (and has been since); null while it stands.</param>
    private sealed record Held(InnCall Call, int Queued, TimeSpan? InvalidSince = null);

    /// <summary>
    /// The calls on screen for the advice to keep (<see cref="DispatchAdvisor.Advise(InnState, bool, IReadOnlyList{KeptCall})"/>):
    /// each kitchen's order or dish to cook ahead not yet done, so the advice plans around what the player was told
    /// instead of the hold arguing with it afterwards.
    /// </summary>
    public IReadOnlyList<KeptCall> Kept(InnKitchenView view) =>
    [
        .. _held.Values.Where(h => !Done(h, view) && h.Call is { Kind: InnCallKind.Queue or InnCallKind.CookAhead, Kitchen: not null, Dish: not null })
            .OrderBy(h => h.Call.Kitchen)
            .Select(h => new KeptCall(h.Call.Kitchen!.Value, h.Call.Dish!, h.Call.Kind == InnCallKind.Queue ? h.Call.Table : null)),
    ];

    /// <summary>Forgets every call (the session ended).</summary>
    public void Clear()
    {
        _held.Clear();
        _changes.Clear();
    }

    /// <summary>The calls to show now, <paramref name="now"/> (the readings' clock), given this moment's advice.</summary>
    public IReadOnlyList<InnCall> Update(DispatchAdvice dispatch, FullBlazeAdvice? blaze, InnKitchenView view, TimeSpan now)
    {
        // Every order the advice still sends to a kitchen, and every dish it cooks ahead, counted (a table may want
        // two of a dish; a dish may be cooked ahead twice). A call shown claims one, so one is never called twice.
        // A dish to cook ahead is under its kitchen (as table -kitchen): the advice keeps each kitchen's own dish to cook
        // ahead while it can (KeptCall), so one shown in kitchen 2 never stands for the one the advice put in kitchen 3.
        var wanted = new Dictionary<(string Dish, int Table), int>();
        void Add((string, int) order, int n) => wanted[order] = wanted.GetValueOrDefault(order) + n;
        foreach (var kitchen in dispatch.Kitchens)
        {
            foreach (var q in kitchen.Queue) Add((q.Dish.Id, q.Table ?? -kitchen.Kitchen), 1);
            foreach (var swap in kitchen.Replaces) Add((swap.Put.Id, swap.Table), 1);
        }
        bool Claim(InnCall call)
        {
            if (WantedKey(call) is not { } key) return true;
            if (wanted.GetValueOrDefault(key) <= 0) return false;
            Add(key, -1);
            return true;
        }
        void Release(InnCall call)
        {
            if (WantedKey(call) is { } key) Add(key, 1);
        }

        // The calls shown that still stand claim their orders first.
        _changes.Clear();
        var cooking = dispatch.Kitchens.ToDictionary(k => k.Kitchen);
        foreach (var (number, held) in _held.OrderBy(h => h.Key).ToList())
        {
            if (Done(held, view))
            {
                _held.Remove(number);
                _changes.Add($"{held.Call.Key} dropped: done (queued)");
                continue;
            }
            // An order the advice now sends to another kitchen: it keeps a shown order where it is unless another kitchen
            // has it out clearly sooner (KeptCall), so this is that decision, and the order moves at once.
            if (held.Call is { Kind: InnCallKind.Queue, Dish: { } ordered, Table: { } table } && cooking.TryGetValue(number, out var here)
                && !here.Queue.Any(q => q.Table == table && q.Dish.Id == ordered.Id)
                && dispatch.Kitchens.FirstOrDefault(k => k.Kitchen != number && k.Queue.Any(q => q.Table == table && q.Dish.Id == ordered.Id)) is { } there)
            {
                _held.Remove(number);
                _changes.Add($"{held.Call.Key} moved: out clearly sooner in kitchen {there.Kitchen}");
                continue;
            }
            string? why = !cooking.TryGetValue(number, out var advice) ? "the kitchen no longer cooks"
                : !Valid(held.Call, advice, wanted, view) ? (view.HasRoom(number) ? NoLongerAdvised : "the kitchen is full")
                : !Claim(held.Call) ? "its order is called elsewhere"
                : null;
            if (why is null)
            {
                if (held.InvalidSince is not null) _held[number] = held with { InvalidSince = null };
                continue;
            }
            var since = held.InvalidSince ?? now;
            if (now - since < GraceFor(held.Call, why))
            {
                // Kept a moment: what dropped it may be a reading that comes right back.
                if (held.InvalidSince is null) _changes.Add($"{held.Call.Key} kept a moment: {why}");
                _held[number] = held with { InvalidSince = since };
                continue;
            }
            _held.Remove(number);
            _changes.Add($"{held.Call.Key} dropped: {why}");
        }

        foreach (var advice in dispatch.Kitchens.OrderBy(k => k.Kitchen))
        {
            var candidate = Candidate(advice, wanted);
            if (_held.TryGetValue(advice.Kitchen, out var held))
            {
                if (candidate is null || candidate.Key == held.Call.Key) continue;
                // A call kept a moment (no longer valid) gives way to an order at once, else stays as it is: the reading
                // that dropped it may come right back. A call that stands gives way only when clearly beaten.
                if (held.InvalidSince is not null ? candidate.Kind is not (InnCallKind.Queue or InnCallKind.Swap) : !Beats(candidate, held.Call, dispatch, view))
                    continue;
                // Only a call that stands claimed its order; one kept a moment claimed nothing to give back.
                if (held.InvalidSince is null) Release(held.Call);
                _changes.Add($"{held.Call.Key} beaten by {candidate.Key}");
            }
            if (candidate is null || !Claim(candidate)) continue;
            _held[advice.Kitchen] = new Held(candidate, candidate.Dish is { } dish ? view.Queued(advice.Kitchen, dish.Id) : 0);
        }

        // An order the advice sends to a kitchen whose call stands for something else: a kitchen with no call (or one
        // only kept a moment) takes it, if it has it out no more than ClearlyEarlierSeconds later (the advice swapping two
        // kitchens' orders is no reason to move the one shown, and the new order is called at once all the same).
        foreach (var free in dispatch.Kitchens.Where(k => (!_held.TryGetValue(k.Kitchen, out var h) || h.InvalidSince is not null) && view.HasRoom(k.Kitchen))
                     .OrderBy(k => k.Kitchen))
        {
            var order = dispatch.Kitchens.Where(k => k.Kitchen != free.Kitchen).SelectMany(k => k.Queue)
                .Where(q => q.Table is { } table && wanted.GetValueOrDefault((q.Dish.Id, table)) > 0
                            && view.ReadyIn(free.Kitchen, q.Dish) <= q.ReadyIn + ClearlyEarlierSeconds)
                .OrderBy(q => q.ReadyIn).FirstOrDefault();
            if (order is null) continue;
            var call = new InnCall(InnCallKind.Queue, free.Kitchen, order.Dish, order.Table);
            Claim(call);
            if (_held.TryGetValue(free.Kitchen, out var kept)) _changes.Add($"{kept.Call.Key} replaced by {call.Key}, an order (it no longer stood)");
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
            if (wanted.GetValueOrDefault((q.Dish.Id, q.Table ?? -advice.Kitchen)) <= 0) continue; // called in another kitchen
            return q.Table is { } table ? new InnCall(InnCallKind.Queue, advice.Kitchen, q.Dish, table) : new InnCall(InnCallKind.CookAhead, advice.Kitchen, q.Dish);
        }
        return null;
    }

    /// <summary>What a call claims of this moment's advice: its order (dish, table), or its kitchen's dish to cook ahead (dish, -kitchen).</summary>
    private static (string Dish, int Table)? WantedKey(InnCall call) => call switch
    {
        { Dish: null } => null,
        { Kind: InnCallKind.CookAhead, Kitchen: { } kitchen } => (call.Dish.Id, -kitchen),
        _ => (call.Dish.Id, call.Table ?? 0),
    };

    /// <summary>The call's dish was put in its kitchen's queue since it was shown.</summary>
    private static bool Done(Held held, InnKitchenView view) =>
        held.Call is { Kitchen: { } kitchen, Dish: { } dish } && view.Queued(kitchen, dish.Id) > held.Queued;

    private static bool Valid(InnCall call, KitchenAdvice advice, Dictionary<(string Dish, int Table), int> wanted, InnKitchenView view)
    {
        int kitchen = advice.Kitchen;
        return call.Kind switch
        {
            InnCallKind.Queue => view.HasRoom(kitchen) && wanted.GetValueOrDefault((call.Dish!.Id, call.Table!.Value)) > 0,
            // Still this kitchen's dish to cook ahead: the advice keeps it there while that's possible (KeptCall), so its
            // going means it no longer is (no stock, the counter full, the end, an order sent to this kitchen).
            InnCallKind.CookAhead => view.HasRoom(kitchen) && wanted.GetValueOrDefault((call.Dish!.Id, -kitchen)) > 0,
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
