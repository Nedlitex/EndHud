namespace EndHud.Vision.Operators;

/// <param name="Portrait">The portrait the answer is for (what a correction will learn).</param>
/// <param name="Match">The answer; null = not recognised (yet).</param>
/// <param name="Suggestions">Every operator, most likely first — for the correction picker.</param>
public sealed record SlotIdentity(Frame Portrait, OperatorMatch? Match, IReadOnlyList<OperatorInfo> Suggestions);

/// <summary>
/// Names the operators in the squad panel, out of the box, and learns from the user.
/// <list type="bullet">
/// <item>Portraits the user taught are checked first; a hit there is near-certain.</item>
/// <item>Otherwise each portrait is compared with every operator by the character model: with
/// the built-in in-game portrait and the wiki avatar (<see cref="PortraitMatcher.Differences"/>). One frame can mislead (the HUD dims, the
/// low-HP tint turns everything pink, a small screen blurs the portrait), so each slot keeps its
/// last <see cref="Window"/> comparisons and names the operator closest over all of them, once it
/// has <see cref="MinReadings"/>.</item>
/// <item>A portrait unlike the slot's last one (pixel similarity under
/// <see cref="OtherOperatorThreshold"/>) when the squad panel comes back after being gone is a
/// different operator: the slot starts over. While the panel stays, it's an effect over the same one.</item>
/// <item>When the panel comes back out of combat, or after <see cref="SquadMayChangeAfter"/> away
/// (from the team menu, a map, a loading screen),
/// every slot is named again from scratch, showing its old name until the new one is in: the
/// squad may have changed, and two operators' portraits can look alike in pixels.</item>
/// <item>Except while the squad panel stays on screen (<see cref="PresenceMemory"/>): the squad is
/// changed in the team menu, which hides the panel, while in battle the game draws portraits
/// see-through over bright effects, which look like someone else or no one. So a slot whose answer
/// is settled (a taught portrait, or a full <see cref="Window"/> of comparisons) keeps its name
/// for as long as the panel stays; an early answer still firms up first. When the panel comes back
/// after being gone (a menu, a loading screen), a kept answer is checked once against the portrait
/// the slot shows then.</item>
/// <item><see cref="CorrectAsync"/> — the user picking the right operator for a slot — unlearns
/// any wrong portraits and learns this one. Portraits are only learned from the user: a wrong
/// answer learned automatically would repeat itself forever.</item>
/// </list>
/// The model takes about 0.2 s per portrait, so each reading compares one slot (the one that needs
/// it most), and a slot is compared only while its window fills, when another operator appears, or
/// when its portrait changes (at most every <see cref="RecheckInterval"/>). Except while slots have
/// no name yet (a battle just started, a new squad): then every such slot is compared each reading,
/// so the squad is named in about <see cref="MinReadings"/> readings instead of four times that.
/// </summary>
public sealed class SquadIdentifier(OperatorLibrary library)
{
    /// <summary>Comparisons kept per slot.</summary>
    public const int Window = 6;

    /// <summary>Comparisons a slot needs before it's named from the wiki avatars.</summary>
    public const int MinReadings = 3;

    /// <summary>How often a slot whose portrait keeps changing is compared again, once its window is full.</summary>
    public static readonly TimeSpan RecheckInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Pixel similarity to the slot's last compared portrait below which it's another operator.
    /// Measured on the recordings at 4K down to 1366x768: the same operator in other frames scored
    /// 0.80 or more, different operators 0.64 or less.
    /// </summary>
    public const double OtherOperatorThreshold = 0.72;

    // At or above this the portrait is the same as the last one compared (only the world behind changed).
    private const double SamePortraitThreshold = 0.92;

    private sealed class SlotEvidence
    {
        public readonly Queue<float[]> Differences = new();
        public Frame? Portrait;               // the last portrait compared (matcher size)
        public DateTime ComparedAt;
        public Frame? Cleared;                // the user said "not recognised" for this portrait
        public SlotIdentity? Before;          // shown while a returning panel's slot is named again
        public bool Provisional;              // compared before the wiki avatars were ready (see ProvisionalMatchMax)
    }

    /// <summary>
    /// Until the wiki avatars are described for a portrait size (up to a minute the first time),
    /// portraits are compared with the built-in in-game references alone, which leave out the
    /// newest operators. Measured: an operator with a reference matched it at 0.052 or less; Si,
    /// who has none, came closest to Arcane at 0.19 and was named Arcane (and Purrchena), and kept
    /// it. So those comparisons name a slot only this close, never settle it, and are dropped when
    /// the first full comparison comes in.
    /// </summary>
    public const double ProvisionalMatchMax = 0.12;

    private readonly PortraitMatcher _matcher = new(library);
    private readonly object _lock = new();
    private readonly SlotIdentity?[] _last = new SlotIdentity?[PartyLayout.SlotCount];
    private readonly SlotEvidence[] _evidence = Enumerable.Range(0, PartyLayout.SlotCount).Select(_ => new SlotEvidence()).ToArray();
    private readonly Func<DateTime> _now = () => DateTime.UtcNow;
    private readonly PresenceMemory _panel = new();
    private readonly bool[] _kept = new bool[PartyLayout.SlotCount]; // named while the panel stays
    private bool _panelWasThere;
    private DateTime _panelSeenAt = DateTime.MinValue;

    /// <summary>
    /// Gone at least this long, the squad may have changed (the team menu takes longer than this;
    /// the gaps in battle the "kept" rule is for, ultimate animations and see-through portraits,
    /// last a couple of seconds). With a controller the panel isn't drawn out of combat at all, so
    /// it comes back from the team menu straight into a fight.
    /// </summary>
    public static readonly TimeSpan SquadMayChangeAfter = TimeSpan.FromSeconds(6);

    /// <param name="wikiAvatarsGate">Tests: the wiki avatars are described only once this completes.</param>
    internal SquadIdentifier(OperatorLibrary library, Func<DateTime> now, IReadOnlyList<PortraitReference>? references = null, Task? wikiAvatarsGate = null)
        : this(library)
    {
        _now = now;
        _matcher = new PortraitMatcher(library, references ?? PortraitReferences.BuiltIn) { WikiAvatarsGate = wikiAvatarsGate ?? Task.CompletedTask };
    }

    public OperatorLibrary Library => library;

    /// <summary>The latest answer per slot (index 0 = slot 1), for the correction picker.</summary>
    public IReadOnlyList<SlotIdentity?> Current
    {
        get { lock (_lock) return _last.ToArray(); }
    }

    /// <summary>
    /// Wait until the wiki avatars are described for this screen (in the background, about 0.2 s
    /// per operator the first time; cached after). Until then only taught portraits are recognised.
    /// <see cref="IdentifyAsync"/> starts it without waiting.
    /// </summary>
    public Task PrepareAsync(PartyState state) =>
        state.Slots.Count == 0 || library.IsEmpty ? Task.CompletedTask : _matcher.PrepareWikiAvatars(ScreenPortrait(state.Slots[0]).Width);

    public Task<PartyState> IdentifyAsync(PartyState state) => Task.Run(() => Identify(state));

    private static Frame ScreenPortrait(PartySlot slot) => slot.ScreenPortrait ?? slot.Portrait;

    private PartyState Identify(PartyState state)
    {
        lock (_evidence)
        {
            // The tracker repeats the last reading while the panel is out of sight: not "seen".
            bool seen = state.PanelVisible && state.ReadingsSinceSeen == 0;
            bool panelThere = _panel.Update(seen);
            bool panelReturns = panelThere && !_panelWasThere;
            _panelWasThere = panelThere;
            var now = _now();
            bool squadMayHaveChanged = panelReturns && (!state.InCombat || now - _panelSeenAt >= SquadMayChangeAfter);
            if (seen) _panelSeenAt = now;
            if (!panelThere) Array.Clear(_kept);
            if (!state.PanelVisible || library.IsEmpty) return KeepNames(state, panelThere);

            var answers = new SlotIdentity?[state.Slots.Count];
            int compare = -1, comparePriority = 0;
            var unnamed = new List<int>();
            for (int k = 0; k < state.Slots.Count; k++)
            {
                var slot = state.Slots[k];
                // A fallen operator's portrait is the death mark: nothing to match. They're still
                // whoever was in the slot (answers[k] stays null: keep the last answer).
                if (slot.Dead) continue;

                // Settled, panel still there: stays named (see the class summary), and isn't compared.
                int i = slot.Number - 1;
                if (squadMayHaveChanged && Settled(i) is { Match.FromLearned: false } before)
                {
                    // Back out of combat or after a long absence, where the team menu may have changed
                    // the squad: named again from scratch. A portrait alike in pixels is no proof
                    // (Camille and Zhuang Fangyi in slot 1 score 0.81, over OtherOperatorThreshold);
                    // the old name shows meanwhile.
                    _evidence[i].Differences.Clear();
                    _evidence[i].Portrait = null;
                    _evidence[i].Before = before;
                }
                else if (panelThere && !_kept[i] && Settled(i) is not null)
                {
                    _kept[i] = !panelReturns || SamePortrait(_evidence[i], slot.Portrait);
                }
                if (_kept[i] && Settled(i) is { } named)
                {
                    answers[k] = named;
                    continue;
                }

                if (_matcher.MatchLearned(slot.Portrait) is { } learned)
                {
                    var rest = library.Operators.Where(o => o.Id != learned.Operator.Id).OrderBy(o => o.NameZh, StringComparer.Ordinal);
                    answers[k] = new SlotIdentity(slot.Portrait, learned, [learned.Operator, .. rest]);
                    continue;
                }

                // Another operator can only have come in while the panel was gone (the team menu).
                int priority = Priority(_evidence[slot.Number - 1], slot.Portrait, now, mayBeAnother: !panelThere || panelReturns,
                    avatarsReady: _matcher.HasWikiAvatars(ScreenPortrait(slot).Width));
                if (priority > comparePriority) (compare, comparePriority) = (k, priority);
                if (priority > 0 && (_evidence[slot.Number - 1].Differences.Count < MinReadings || _evidence[slot.Number - 1].Provisional)) unnamed.Add(k);
            }

            // One model run per reading, for the slot that needs it most; every slot still without
            // enough comparisons for a name, when there are any.
            foreach (int k in unnamed.Count > 0 ? unnamed : compare >= 0 ? [compare] : [])
            {
                bool complete = _matcher.HasWikiAvatars(ScreenPortrait(state.Slots[k]).Width);
                if (_matcher.Differences(ScreenPortrait(state.Slots[k])) is not { } differences) continue;
                var evidence = _evidence[state.Slots[k].Number - 1];
                if (complete && evidence.Provisional)
                {
                    // Named again with the avatars; a name from the in-game references shows meanwhile.
                    lock (_lock) if (_last[state.Slots[k].Number - 1] is { Match: not null } shown) evidence.Before = shown;
                    evidence.Differences.Clear();
                }
                evidence.Provisional = !complete;
                evidence.Differences.Enqueue(differences);
                while (evidence.Differences.Count > Window) evidence.Differences.Dequeue();
                evidence.Portrait = state.Slots[k].Portrait;
                evidence.ComparedAt = now;
            }

            var slots = new List<PartySlot>(state.Slots.Count);
            for (int k = 0; k < state.Slots.Count; k++)
            {
                var slot = state.Slots[k];
                int i = slot.Number - 1;
                if (slot.Dead)
                {
                    lock (_lock) slots.Add(slot with { Operator = _last[i]?.Match });
                    continue;
                }

                var identity = answers[k] ?? FromEvidence(slot.Portrait, _evidence[i]);
                lock (_lock) _last[i] = identity;
                slots.Add(slot with { Operator = identity.Match });
            }
            return state with { Slots = slots };
        }
    }

    /// <summary>Whether the player said the operator in this slot (1–4) isn't recognised (see <see cref="CorrectAsync"/>).</summary>
    public bool MarkedUnrecognised(int slotNumber)
    {
        lock (_evidence) return slotNumber is >= 1 and <= PartyLayout.SlotCount && _evidence[slotNumber - 1].Cleared is not null;
    }

    /// <summary>The slot's answer, if it's settled: from a taught portrait, or over a full window of comparisons.</summary>
    private SlotIdentity? Settled(int i)
    {
        lock (_lock)
            return _last[i] is { Match: { } match } last && (match.FromLearned || (_evidence[i].Differences.Count >= Window && !_evidence[i].Provisional)) ? last : null;
    }

    /// <summary>Whether the portrait is still the one the slot's answer was worked out from (not another operator).</summary>
    private bool SamePortrait(SlotEvidence evidence, Frame portrait) =>
        evidence.Portrait is not null && _matcher.SimilarityToLearned(portrait, evidence.Portrait) >= OtherOperatorThreshold;

    /// <summary>The panel can't be read this time: while it counts as there, the slots keep their names.</summary>
    private PartyState KeepNames(PartyState state, bool panelThere)
    {
        if (!panelThere) return state;
        lock (_lock)
            return state with { Slots = state.Slots.Select(s => s.Operator is null && s.Number <= _last.Length ? s with { Operator = _last[s.Number - 1]?.Match } : s).ToList() };
    }

    /// <summary>
    /// How much the slot needs a model run now (0 = not at all): another operator appeared (it
    /// starts over), then a window still filling (emptiest first), then a changed portrait due a recheck.
    /// </summary>
    private int Priority(SlotEvidence evidence, Frame portrait, DateTime now, bool mayBeAnother, bool avatarsReady)
    {
        // The wiki avatars are in now: compare with them at once (see ProvisionalMatchMax).
        if (evidence.Provisional && avatarsReady) return Window + 2;
        if (evidence.Cleared is { } cleared)
        {
            if (_matcher.SimilarityToLearned(portrait, cleared) >= SamePortraitThreshold) return 0;
            evidence.Cleared = null; // someone else now
        }

        double similarity = evidence.Portrait is null ? 0 : _matcher.SimilarityToLearned(portrait, evidence.Portrait);
        if (mayBeAnother && evidence.Portrait is not null && similarity < OtherOperatorThreshold)
        {
            evidence.Differences.Clear();
            evidence.Portrait = null;
            return Window + 2;
        }
        if (evidence.Differences.Count < Window) return Window + 1 - evidence.Differences.Count;
        return similarity < SamePortraitThreshold && now - evidence.ComparedAt >= RecheckInterval ? 1 : 0;
    }

    /// <summary>The operator closest on average over the slot's comparisons, once there are enough.</summary>
    private SlotIdentity FromEvidence(Frame portrait, SlotEvidence evidence)
    {
        int count = evidence.Differences.Count;
        if (count < MinReadings && evidence.Before is { Match: { } kept } before)
        {
            // The old name shows while the slot is named again, as long as the comparisons so far
            // agree with it; once they don't, the squad changed and nobody is shown meanwhile.
            if (count == 0 || ClosestSoFar(evidence) == kept.Operator.Id) return before;
        }
        evidence.Before = null;
        if (count == 0) return new SlotIdentity(portrait, null, library.Operators.OrderBy(o => o.NameZh, StringComparer.Ordinal).ToList());

        var mean = new double[library.Operators.Count];
        foreach (var row in evidence.Differences)
            for (int j = 0; j < mean.Length; j++) mean[j] += row[j] / count;

        var ranked = Enumerable.Range(0, mean.Length).OrderBy(j => mean[j]).ToList();
        var suggestions = ranked.Select(j => library.Operators[j]).ToList();
        if (count < MinReadings || evidence.Cleared is not null || (evidence.Provisional && mean[ranked[0]] > ProvisionalMatchMax))
            return new SlotIdentity(portrait, null, suggestions);
        return new SlotIdentity(portrait, new OperatorMatch(suggestions[0], 1 - mean[ranked[0]], FromLearned: false), suggestions);
    }

    /// <summary>The operator closest on average over the slot's comparisons so far.</summary>
    private string ClosestSoFar(SlotEvidence evidence)
    {
        var mean = new double[library.Operators.Count];
        foreach (var row in evidence.Differences)
            for (int j = 0; j < mean.Length; j++) mean[j] += row[j];
        return library.Operators[Array.IndexOf(mean, mean.Min())].Id;
    }

    /// <summary>
    /// The user says slot <paramref name="slotNumber"/> is <paramref name="actual"/> (null: leave it
    /// unrecognised). Unlearns portraits that produced a wrong answer, then learns this one.
    /// </summary>
    public async Task CorrectAsync(int slotNumber, OperatorInfo? actual)
    {
        int i = slotNumber - 1;
        SlotIdentity? current;
        lock (_lock) current = _last[i];
        if (current is null) return;

        if (current.Match is { FromLearned: true } wrong && wrong.Operator.Id != actual?.Id)
        {
            library.Unlearn(wrong.Operator.Id, learned =>
                _matcher.SimilarityToLearned(current.Portrait, learned) >= PortraitMatcher.LearnedThreshold);
        }

        if (actual is not null) await library.LearnAsync(actual, current.Portrait);
        lock (_evidence) _evidence[i].Cleared = actual is null ? current.Portrait : null;

        lock (_lock)
        {
            _last[i] = current with
            {
                Match = actual is null ? null : new OperatorMatch(actual, 1, FromLearned: true),
            };
        }
    }
}
