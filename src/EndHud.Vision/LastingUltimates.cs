namespace EndHud.Vision;

/// <param name="OperatorId">Whose ultimate it is.</param>
/// <param name="LeftShare">The part of its time still to run, 0–1.</param>
/// <param name="Estimated">Not read on the bar this time (another operator's is shown there, or the bar is
/// hidden for a moment): carried on from its own readings at the rate it drained while it was seen (shown "≈").</param>
public sealed record LastingUltimateView(string OperatorId, double LeftShare, bool Estimated);

/// <summary>
/// Whose lasting ultimate the time bar (<see cref="UltimateTimeBar"/>, <see cref="PartyState.UltimateTimeLeft"/>)
/// shows, across readings, and how much each running one has left.
/// <para>
/// Measured (Fixtures/lasting-ultimate-background): the bar is NOT the controlled operator's. An
/// ultimate cast from the background (Zhuang Fangyi's, with Perlica controlled) runs its bar under
/// the controlled operator's HP the whole time. With several running, the bar shows the controlled
/// operator's own when she has one, and otherwise one running for an operator in the background.
/// </para>
/// Each reading of the bar is given to the operator whose colour it is (<see cref="OwnerByColour"/>:
/// Laevatain red, Zhuang Fangyi gold, Yvonne teal), if she's in the squad; a bar of another colour,
/// or of an operator not in the squad, to no one. Only when its colour isn't read
/// (<see cref="PartyState.UltimateTimeColour"/> null) is it given by inference, to:
/// <list type="number">
/// <item>the controlled operator, if her ultimate lasts and was just spent (she cast it: the bar is
/// hers), or hers runs, unless she'd be about out by now and the bar goes on another one;</item>
/// <item>else the running one it goes on: the nearest to where each would be by now, at the rate it
/// drained;</item>
/// <item>else a new one, among the operators with a lasting ultimate that isn't running and doesn't
/// show ready (a ready one wasn't cast): the one spent most recently, else the only one; else no one
/// (it isn't shown on a guess).</item>
/// </list>
/// While the controlled operator isn't read (a switch under way), a reading goes to a new one just
/// spent, else to a running one only when exactly one fits.
/// The ones not on the bar this reading are carried on, estimated, until they'd be out; one never
/// seen draining isn't shown (there's no rate to carry it on with). Their time stands still while
/// the panel is out of sight (the game pauses it for a cutscene). No bar for
/// <see cref="NoBarEnds"/> means none is running (the game would show one).
/// </summary>
public sealed class LastingUltimates
{
    /// <summary>
    /// Operators whose ultimate lasts with the time bar, as measured on the owner's recordings
    /// (Laevatain red, Zhuang Fangyi gold, Yvonne teal: all three on test_zhuang_laevatine_background1).
    /// A bar that can't be given to one of them is shown for no one.
    /// </summary>
    public static readonly IReadOnlySet<string> Operators = new HashSet<string> { "Laevatain", "Zhuang_Fangyi", "Yvonne" };

    /// <summary>
    /// Each known lasting ultimate's bar colour, as a hue (degrees), measured on the owner's
    /// recordings: on the fixtures (Fixtures/lasting-ultimate-background and ultimate-time, at 4K and
    /// on ten simulated 16:9-class screens) Laevatain red 4–8, Zhuang Fangyi gold 48–51 (a dimmed
    /// gold bar, #AE941E, is still 49–50); every 0.25 s of the two Zhuang recordings, red 3–8 (15
    /// readings), gold 47–51 (134). Yvonne's teal (named by the owner, 2026-10-23): 171 on the
    /// fixture at every size, 171–176 on the recording (9 readings; her bar is on screen only while
    /// she's controlled, 41.25–41.5 s and 44.25–45.75 s). Within <see cref="SameHueWithin"/> that
    /// leaves 12 degrees of room or more on each; the nearest two (red and gold) are 44 apart.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, double> BarHues = new Dictionary<string, double>
    {
        ["Laevatain"] = 6,
        ["Zhuang_Fangyi"] = 50,
        ["Yvonne"] = 173,
    };

    /// <summary>A bar is a known operator's when its hue is this close to hers (the nearest two are 44 apart).</summary>
    public const double SameHueWithin = 15;

    /// <summary>Whose lasting ultimate a bar of this colour is (<see cref="BarHues"/>); null when it's none of the known ones.</summary>
    public static string? OwnerByColour(BarColour colour)
    {
        foreach (var (id, hue) in BarHues)
        {
            double off = Math.Abs(colour.Hue - hue) % 360;
            if (Math.Min(off, 360 - off) <= SameHueWithin) return id;
        }
        return null;
    }

    /// <summary>How close a reading must be to where a running one would be by now to go on it.</summary>
    public const double ContinuesWithin = 0.15;

    /// <summary>An ultimate spent this recently (ready, then not) may be the one that just started a bar.</summary>
    public static readonly TimeSpan SpentRecently = TimeSpan.FromSeconds(15);

    /// <summary>A run's rate is measured over at least this long of its own readings.</summary>
    public static readonly TimeSpan RateAfter = TimeSpan.FromSeconds(1);

    /// <summary>
    /// No bar on screen this long (readings that saw the panel) and none is running. Shorter gaps are
    /// carried over: the tracker drops the bar at once when the controlled operator changes during
    /// a gap (measured: 1 s on the Zhuang + Laevatain recording, 52.5–53 s).
    /// </summary>
    public static readonly TimeSpan NoBarEnds = TimeSpan.FromSeconds(2);

    private sealed class Run(double left, TimeSpan at)
    {
        public double Left { get; private set; } = left;
        public TimeSpan At { get; private set; } = at;
        public double? Rate { get; private set; } // share per second, from its own readings
        private (double Left, TimeSpan At) _anchor = (left, at);

        public double Predicted(TimeSpan now) => Math.Max(0, Left - (Rate ?? 0) * (now - At).TotalSeconds);

        /// <summary>The game was paused this long (the panel out of sight, a cutscene): the time left stood still.</summary>
        public void Pause(TimeSpan paused)
        {
            At += paused;
            _anchor = (_anchor.Left, _anchor.At + paused);
        }

        /// <summary>
        /// Read on the bar. The rate is taken over the last second or more of its own readings;
        /// a stretch where it didn't move (the game paused for a cutscene) keeps the rate it had.
        /// </summary>
        public void Seen(double left, TimeSpan now)
        {
            (Left, At) = (left, now);
            if (left > _anchor.Left) _anchor = (left, now);
            else if (now - _anchor.At >= RateAfter)
            {
                if (_anchor.Left > left) Rate = (_anchor.Left - left) / (now - _anchor.At).TotalSeconds;
                _anchor = (left, now);
            }
        }
    }

    private readonly Dictionary<string, Run> _running = [];
    private readonly Dictionary<string, TimeSpan> _readyAt = [];
    private readonly Dictionary<string, TimeSpan> _spentAt = [];
    private string? _lastOwner;
    private TimeSpan? _noBarSince;
    private TimeSpan? _lastAt;

    /// <returns>Each running lasting ultimate, by operator id.</returns>
    public IReadOnlyDictionary<string, LastingUltimateView> Update(PartyState state, TimeSpan now)
    {
        var squad = state.Slots.Where(s => !s.Empty && s.Operator is not null).ToDictionary(s => s.Number, s => s.Operator!.Operator.Id);
        // A reading kept by the tracker while the panel is out of sight (a cutscene) is the last one
        // again: nothing new in it, only the running ones carried on.
        bool fresh = state.ReadingsSinceSeen == 0;
        if (fresh) NoteSpent(state, now);
        // The time left stands still while the panel is out of sight (measured: Zhuang's bar lost
        // 0.02 over 2 s of cutscene and 0.04 over 5.5 s, against 0.04 a second while seen).
        else if (_lastAt is { } last && now > last)
            foreach (var run in _running.Values) run.Pause(now - last);
        _lastAt = now;

        string? owner = null;
        if (state.UltimateTimeLeft is not { } bar)
        {
            if (fresh) _noBarSince ??= now;
            if (_noBarSince is { } since && now - since >= NoBarEnds) _running.Clear();
        }
        else if (!fresh) owner = _lastOwner;
        else
        {
            _noBarSince = null;
            string? controlled = state.ActiveSlot is { } active && squad.TryGetValue(active, out var c) ? c : null;
            // The bar's colour says whose it is; only when it wasn't read is the owner worked out.
            owner = state.UltimateTimeColour is { } colour
                ? OwnerByColour(colour) is { } byColour && squad.ContainsValue(byColour) ? byColour : null
                : Owner(bar, state, controlled, now);
            if (owner is not null)
            {
                if (!_running.TryGetValue(owner, out var run))
                {
                    _running[owner] = run = new Run(bar, now);
                    _spentAt.Remove(owner); // that spending started this run
                }
                run.Seen(bar, now);
                // The game shows the controlled operator's own when she has one: the bar is someone
                // else's, so hers has ended.
                if (controlled is not null && controlled != owner) _running.Remove(controlled);
            }
        }
        _lastOwner = owner;

        var views = new Dictionary<string, LastingUltimateView>();
        foreach (var (id, run) in _running.ToList())
        {
            if (id == owner)
            {
                views[id] = new LastingUltimateView(id, state.UltimateTimeLeft!.Value, Estimated: false);
                continue;
            }
            double left = run.Predicted(now);
            if (left <= 0 || !squad.ContainsValue(id))
            {
                _running.Remove(id);
                continue;
            }
            if (run.Rate is not null) views[id] = new LastingUltimateView(id, left, Estimated: true);
        }
        return views;
    }

    /// <summary>Forget every running ultimate (the battle ended).</summary>
    public void Clear()
    {
        _running.Clear();
        _readyAt.Clear();
        _spentAt.Clear();
        _lastOwner = null;
        _noBarSince = null;
        _lastAt = null;
    }

    private string? Owner(double bar, PartyState state, string? controlled, TimeSpan now)
    {
        // The running ones this reading could go on, nearest first.
        var fits = _running
            .Select(r => (Id: r.Key, Off: Math.Abs(r.Value.Predicted(now) - bar)))
            .Where(r => r.Off <= ContinuesWithin)
            .OrderBy(r => r.Off)
            .Select(r => r.Id)
            .ToList();
        string? continued = fits.FirstOrDefault();

        // Who could have started a new one: an operator with a lasting ultimate, not running, whose
        // ultimate doesn't show ready (a ready one hasn't been cast); first the one spent last.
        var waiting = state.Slots
            .Where(s => !s.Empty && !s.Dead && s.Operator?.Operator.Id is { } id && Operators.Contains(id) && !_running.ContainsKey(id) && s.Ultimate?.Ready != true)
            .Select(s => s.Operator!.Operator.Id)
            .Distinct()
            .ToList();
        string? spent = waiting.Where(id => SpentLately(id, now)).OrderByDescending(id => _spentAt[id]).FirstOrDefault();
        string? onlyOne = waiting.Count == 1 ? waiting[0] : null;

        if (state.ActiveSlot is null)
        {
            // A switch under way: a new one only from a spending just seen, else only when it can only be one.
            if (spent is not null) return spent;
            return fits.Count switch { 1 => continued, > 1 => null, _ => onlyOne };
        }

        if (controlled is not null && Operators.Contains(controlled))
        {
            if (_running.TryGetValue(controlled, out var hers))
            {
                bool hersOver = continued is not null && continued != controlled && hers.Predicted(now) <= ContinuesWithin;
                if (!hersOver) return controlled;
            }
            else if (waiting.Contains(controlled) && SpentLately(controlled, now)) return controlled; // she just cast hers
        }
        return continued ?? spent ?? onlyOne;
    }

    private bool SpentLately(string id, TimeSpan now) => _spentAt.TryGetValue(id, out var at) && now - at <= SpentRecently;

    /// <summary>Each operator's ultimate seen ready and then not: just spent.</summary>
    private void NoteSpent(PartyState state, TimeSpan now)
    {
        foreach (var slot in state.Slots)
        {
            if (slot.Empty || slot.Dead || slot.Operator?.Operator.Id is not { } id || slot.Ultimate is not { } ultimate) continue;
            if (ultimate.Ready) _readyAt[id] = now;
            else if (_readyAt.Remove(id)) _spentAt[id] = now;
        }
    }
}
