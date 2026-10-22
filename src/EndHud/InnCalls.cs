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

    /// <summary>
    /// Calls are stacked in this order: the kitchens by number, then Full Blaze, then the last call by table. The kitchens
    /// first, each in a row kept for the session (<see cref="InnBannerRows"/>), so nothing coming or going moves them.
    /// </summary>
    public (int Group, int Number) Order => Kind switch
    {
        InnCallKind.BlazeNow or InnCallKind.BlazeGetReady => (1, 0),
        InnCallKind.LastCall => (2, Table ?? 0),
        _ => (0, Kitchen ?? 0),
    };

    /// <summary>A kitchen's call (a dish to queue, cook ahead or swap): it goes in that kitchen's row.</summary>
    public bool ForKitchen => Order.Group == 0 && Kitchen is not null;
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
/// <item><b>impossible:</b> the kitchen has no free slot; for an order, the advice no longer sends it to that kitchen
/// (it keeps it there while it's possible, <see cref="Kept"/>: so no stock, out too late for serving, the order gone);
/// for a dish to cook ahead, the advice no longer cooks it ahead in that kitchen (likewise kept while possible: so no
/// stock, the end rules, an order sent there); for a swap, neither the swap nor its order stands; the kitchen no longer
/// cooks;</item>
/// <item><b>an order comes first:</b> an order (or a swap for one) is called where a dish to cook ahead was.</item>
/// </list>
/// Nothing else takes a call down (owner, 2026-10-21: "the recommendation should stay steady"): the soonest kitchen
/// decides where an order goes only when it is first shown, and an order shown is never moved to another kitchen that
/// has it out sooner since, nor replaced by another order. A dish to cook ahead never gives way to another dish to cook
/// ahead. One order is called in one kitchen only. Full Blaze and the last call are the advice's own (it doesn't flicker).
/// <para>A call no longer valid stays up for <see cref="GraceSeconds"/> (a dish to cook ahead: <see cref="CookAheadGraceSeconds"/>) first, and stays on if it's valid again by then
/// (owner, live 2026-10-20: "it is still flashing ... should just stay, not flash all the time"): a reading that drops
/// an order for a moment (a card misread as the HUD fades, the panel's queue read a frame short) would otherwise take
/// the banner down and put it up again a frame later, sliding out and in. Done is done at once.</para>
/// </summary>
internal sealed class InnCallHold
{
    /// <summary>
    /// An order not shown yet is called at once in a kitchen with no order to show (none, or a dish to cook ahead) when
    /// it comes out there no more than this after the kitchen the advice sends it to (whose call is another order's):
    /// on test_yuanyuan4 at 230.7 s a second order of a dish went behind the first in kitchen 1 and waited 5 s for a call
    /// while kitchen 2 had it out as soon. Only for a new order: one already shown never moves (<see cref="InnCallHold"/>).
    /// </summary>
    public const double ShowElsewhereWithinSeconds = 8;

    /// <summary>A call no longer valid is taken down only once it has stayed so this long.</summary>
    public static readonly TimeSpan GraceSeconds = TimeSpan.FromSeconds(1.5);

    /// <summary>
    /// A dish to cook ahead the advice no longer cooks ahead in its kitchen stays this long first: nothing is urgent about
    /// it, and what drops it can come back a moment later. Measured when cooking ahead stopped at the counter's room (until
    /// 2026-10-21): on test_yuanyuan4 kitchen 3's dish lost the counter's last places for 3.3 s (189.3 s) and the kitchen
    /// then got another dish; on test_yuanyuan3 for 5.6 s (271.0 s), back 0.6 s after it was taken down. With no counter cap
    /// it still covers a kitchen holding two dishes for a moment. Its kitchen full still takes it down after
    /// <see cref="GraceSeconds"/>, and an order takes its place at once.
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
    /// <param name="ShownAt">When it was first shown.</param>
    /// <param name="InvalidSince">When it was first found no longer valid (and has been since); null while it stands.</param>
    private sealed record Held(InnCall Call, int Queued, TimeSpan ShownAt, TimeSpan? InvalidSince = null);

    /// <summary>
    /// The calls on screen for the advice to keep (<see cref="DispatchAdvisor.Advise(InnState, bool, IReadOnlyList{KeptCall})"/>):
    /// each kitchen's order or dish to cook ahead not yet done, so the advice plans around what the player was told
    /// instead of the hold arguing with it afterwards.
    /// </summary>
    /// <remarks>
    /// A swap shown is its order kept in its kitchen too: the advice otherwise sent that order to another kitchen and took
    /// that kitchen's dish to cook ahead from it, while the swap went on claiming the order, so the other kitchen's dish to
    /// cook ahead was dropped for nothing (test_yuanyuan4 206.1 s: kitchen 1's cyan circle gone for 3.6 s, the order still
    /// kitchen 2's swap).
    /// </remarks>
    public IReadOnlyList<KeptCall> Kept(InnKitchenView view) =>
    [
        .. _held.Values.Where(h => !Done(h, view) && h.Call is { Kind: InnCallKind.Queue or InnCallKind.CookAhead or InnCallKind.Swap, Kitchen: not null, Dish: not null })
            .OrderBy(h => h.Call.Kitchen)
            .Select(h => new KeptCall(h.Call.Kitchen!.Value, h.Call.Dish!, h.Call.Kind == InnCallKind.CookAhead ? null : h.Call.Table, h.Call.Kind == InnCallKind.Swap ? h.Call.Remove : null)),
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

        // The calls shown claim their orders first, those kept a moment too (so their order isn't called in another kitchen
        // meanwhile): the kitchens whose call claimed one, to give it back if it's replaced.
        _changes.Clear();
        var claimed = new HashSet<int>();
        var cooking = dispatch.Kitchens.ToDictionary(k => k.Kitchen);
        foreach (var (number, held) in _held.OrderBy(h => h.Key).ToList())
        {
            if (Done(held, view))
            {
                _held.Remove(number);
                _changes.Add($"{held.Call.Key} dropped: done (queued)");
                continue;
            }
            string? why = !cooking.TryGetValue(number, out var advice) ? "the kitchen no longer cooks"
                : !Valid(held.Call, advice, wanted, view) ? (view.HasRoom(number) ? NoLongerAdvised : "the kitchen is full")
                : !Claim(held.Call) ? "its order is called elsewhere"
                : null;
            if (why is null)
            {
                claimed.Add(number);
                var standing = held with { InvalidSince = null };
                _held[number] = standing;
                // A swap the advice no longer makes, its order still sent here as a plain dish to queue (what it would take out
                // has cooked out, a slot is free): shown as that order. test_yuanyuan7 from 261 s, "take out boundary-lung-roll first" (the swap circle) stood 100 s
                // over table 6's immortal-galore-of-fortune after the boundary-lung-roll was out.
                if (held.Call.Kind == InnCallKind.Swap && !advice!.Replaces.Any(s => s.Remove.Id == held.Call.Remove!.Id && s.Put.Id == held.Call.Dish!.Id && s.Table == held.Call.Table))
                {
                    var order = new InnCall(InnCallKind.Queue, number, held.Call.Dish, held.Call.Table);
                    _changes.Add($"{held.Call.Key} {NowPlain} {order.Key}");
                    _held[number] = standing with { Call = order };
                }
                continue;
            }
            var since = held.InvalidSince ?? now;
            if (now - since < GraceFor(held.Call, why))
            {
                // Kept a moment: what dropped it may be a reading that comes right back.
                if (Claim(held.Call)) claimed.Add(number);
                if (held.InvalidSince is null) _changes.Add($"{held.Call.Key} kept a moment: {why}");
                _held[number] = held with { InvalidSince = since };
                continue;
            }
            _held.Remove(number);
            _changes.Add($"{held.Call.Key} dropped: {why}");
        }

        // A dish another kitchen's circle shows (owner, 2026-10-21: one dish in one circle, unless two tables each need it).
        bool ShownElsewhere(string dish, int kitchen) => _held.Any(h => h.Key != kitchen && h.Value.Call.Dish?.Id == dish);
        foreach (var advice in dispatch.Kitchens.OrderBy(k => k.Kitchen))
        {
            var candidate = Candidate(advice, wanted, dish => ShownElsewhere(dish, advice.Kitchen));
            if (_held.TryGetValue(advice.Kitchen, out var held))
            {
                if (candidate is null || candidate.Key == held.Call.Key) continue;
                // Only an order displaces a call, and only a dish to cook ahead or a call kept a moment (no longer valid:
                // the reading that dropped it may come right back, so nothing else replaces it meanwhile).
                if (!IsOrder(candidate) || (held.InvalidSince is null && held.Call.Kind != InnCallKind.CookAhead)) continue;
                if (claimed.Remove(advice.Kitchen)) Release(held.Call);
                _changes.Add($"{held.Call.Key} beaten by {candidate.Key}");
            }
            if (candidate is null || !Claim(candidate)) continue;
            _held[advice.Kitchen] = new Held(candidate, candidate.Dish is { } dish ? view.Queued(advice.Kitchen, dish.Id) : 0, now);
        }

        // An order the advice sends to a kitchen whose call stands for something else: a kitchen with no call, one only
        // kept a moment, or one showing a dish to cook ahead (an order comes first) takes it, if it has it out no more than
        // ShowElsewhereWithinSeconds later: the new order is called at once all the same.
        foreach (var free in dispatch.Kitchens
                     .Where(k => (!_held.TryGetValue(k.Kitchen, out var h) || h.InvalidSince is not null || h.Call.Kind == InnCallKind.CookAhead) && view.HasRoom(k.Kitchen))
                     .OrderBy(k => k.Kitchen))
        {
            var order = dispatch.Kitchens.Where(k => k.Kitchen != free.Kitchen).SelectMany(k => k.Queue)
                .Where(q => q.Table is { } table && wanted.GetValueOrDefault((q.Dish.Id, table)) > 0
                            && view.ReadyIn(free.Kitchen, q.Dish) <= q.ReadyIn + ShowElsewhereWithinSeconds)
                .OrderBy(q => q.ReadyIn).FirstOrDefault();
            if (order is null) continue;
            var call = new InnCall(InnCallKind.Queue, free.Kitchen, order.Dish, order.Table);
            Claim(call);
            if (_held.TryGetValue(free.Kitchen, out var kept))
            {
                if (claimed.Remove(free.Kitchen)) Release(kept.Call);
                _changes.Add(kept.InvalidSince is null ? $"{kept.Call.Key} replaced by {call.Key}, an order before a dish to cook ahead"
                    : $"{kept.Call.Key} replaced by {call.Key}, an order (it no longer stood)");
            }
            _held[free.Kitchen] = new Held(call, view.Queued(free.Kitchen, order.Dish.Id), now);
        }

        // One dish in one circle (owner, 2026-10-21, test_yuanyuan7 at 00:46: eight-treasure-rice for table 3 in kitchen 1 and to cook ahead in
        // kitchen 3, "duplicated in multiple kitchens"): a dish to cook ahead whose dish another circle shows gives way, to an
        // order or swap, or to a dish to cook ahead shown before it; its kitchen takes the next dish its advice cooks ahead that
        // no circle shows, or shows nothing. Two orders of a dish for two tables stay: each names its own table.
        foreach (var (number, held) in _held.Where(h => h.Value.Call.Kind == InnCallKind.CookAhead)
                     .OrderByDescending(h => h.Value.ShownAt).ThenByDescending(h => h.Key).ToList())
        {
            var other = _held.Where(o => o.Key != number && o.Value.Call.Dish?.Id == held.Call.Dish!.Id
                                         && (o.Value.Call.Kind != InnCallKind.CookAhead || o.Value.ShownAt < held.ShownAt || (o.Value.ShownAt == held.ShownAt && o.Key < number)))
                .Select(o => (int?)o.Key).FirstOrDefault();
            if (other is null) continue;
            if (claimed.Remove(number)) Release(held.Call);
            _held.Remove(number);
            var next = cooking.TryGetValue(number, out var advice) ? Candidate(advice, wanted, dish => ShownElsewhere(dish, number)) : null;
            if (next is not null && view.HasRoom(number) && Claim(next))
            {
                _held[number] = new Held(next, next.Dish is { } dish ? view.Queued(number, dish.Id) : 0, now);
                claimed.Add(number);
                _changes.Add($"{held.Call.Key} replaced by {next.Key}: {DishShown} {other}");
            }
            else _changes.Add($"{held.Call.Key} dropped: {DishShown} {other}");
        }

        var calls = new List<InnCall>();
        if (blaze == FullBlazeAdvice.StartNow) calls.Add(new InnCall(InnCallKind.BlazeNow));
        else if (blaze == FullBlazeAdvice.GetReady) calls.Add(new InnCall(InnCallKind.BlazeGetReady));
        calls.AddRange(_held.Values.Select(h => h.Call));
        calls.AddRange(dispatch.Leftovers.Select(l => new InnCall(InnCallKind.LastCall, Dish: l.Dish, Table: l.Table, Matches: l.Matches)));
        return [.. calls.OrderBy(c => c.Order.Group).ThenBy(c => c.Order.Number)];
    }

    /// <summary>A swap shown as its order once nothing is left to take out (the replay's trace: "Swap:… now a plain order: Queue:…").</summary>
    public const string NowPlain = "now a plain order:";

    /// <summary>Why a dish to cook ahead gave way: the kitchen named after it shows the same dish ("dropped: …", the replay's trace).</summary>
    public const string DishShown = "its dish is shown in kitchen";

    /// <summary>
    /// The kitchen's first call in this moment's advice: a swap, else the first dish to queue, an order not called elsewhere, a
    /// dish to cook ahead that no other circle shows (<paramref name="shownElsewhere"/>).
    /// </summary>
    private static InnCall? Candidate(KitchenAdvice advice, Dictionary<(string Dish, int Table), int> wanted, Func<string, bool> shownElsewhere)
    {
        foreach (var swap in advice.Replaces)
            if (wanted.GetValueOrDefault((swap.Put.Id, swap.Table)) > 0)
                return new InnCall(InnCallKind.Swap, advice.Kitchen, swap.Put, swap.Table, swap.Remove);
        foreach (var q in advice.Queue)
        {
            if (wanted.GetValueOrDefault((q.Dish.Id, q.Table ?? -advice.Kitchen)) <= 0) continue; // called in another kitchen
            if (q.Table is null && shownElsewhere(q.Dish.Id)) continue; // one dish in one circle
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
            // Still sent to this kitchen: the advice keeps an order shown where it is while that's possible (KeptCall), so
            // its sending it elsewhere means it no longer is (out too late here, the panel's queue read full).
            InnCallKind.Queue => view.HasRoom(kitchen) && wanted.GetValueOrDefault((call.Dish!.Id, call.Table!.Value)) > 0
                                 && advice.Queue.Any(q => q.Table == call.Table && q.Dish.Id == call.Dish.Id),
            // Still this kitchen's dish to cook ahead: the advice keeps it there while that's possible (KeptCall), so its
            // going means it no longer is (no stock, the end, an order sent to this kitchen).
            InnCallKind.CookAhead => view.HasRoom(kitchen) && wanted.GetValueOrDefault((call.Dish!.Id, -kitchen)) > 0,
            // Still this kitchen's swap, or its order still sent to this kitchen (the advice keeps a swap's order there while it is
            // possible, Kept). Until 2026-10-21 any kitchen wanting the order kept the swap: test_yuanyuan7 at 341.5 s the order
            // could no longer come out of kitchen 2 in time, the advice sent it to kitchen 1, and the swap went on claiming it,
            // so kitchen 1 never showed it.
            InnCallKind.Swap => advice.Replaces.Any(s => s.Remove.Id == call.Remove!.Id && s.Put.Id == call.Dish!.Id && s.Table == call.Table)
                                || (view.HasRoom(kitchen) && wanted.GetValueOrDefault((call.Dish!.Id, call.Table!.Value)) > 0
                                    && advice.Queue.Any(q => q.Table == call.Table && q.Dish.Id == call.Dish.Id)),
            _ => false,
        };
    }

    private static bool IsOrder(InnCall call) => call.Kind is InnCallKind.Queue or InnCallKind.Swap;
}
