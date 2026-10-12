namespace EndHud.Vision.Operators;

/// <param name="Portrait">The portrait the answer is for (what a correction will learn).</param>
/// <param name="Match">The answer; null = not recognised.</param>
/// <param name="Suggestions">Every operator, most likely first — for the correction picker.</param>
public sealed record SlotIdentity(Frame Portrait, OperatorMatch? Match, IReadOnlyList<OperatorInfo> Suggestions);

/// <summary>
/// Names the operators in the squad panel and learns from the user.
/// <list type="bullet">
/// <item>Learned in-game portraits are checked first; a hit there is near-certain.</item>
/// <item>Otherwise the wiki icons give a best guess, shown only when it clearly wins.</item>
/// <item><see cref="CorrectAsync"/> — the user picking the right operator for a slot — unlearns
/// any wrong portraits and learns this one. It's the only way portraits get learned: a wrong
/// guess learned automatically would repeat itself forever.</item>
/// </list>
/// A slot whose portrait hasn't changed since the last reading reuses the last answer.
/// </summary>
public sealed class SquadIdentifier(OperatorLibrary library)
{
    // Above this the portrait is the same operator as last reading (only the world behind changed).
    private const double SamePortraitThreshold = 0.92;

    private readonly PortraitMatcher _matcher = new(library);
    private readonly object _lock = new();
    private readonly SlotIdentity?[] _last = new SlotIdentity?[PartyLayout.SlotCount];

    public OperatorLibrary Library => library;

    /// <summary>The latest answer per slot (index 0 = slot 1), for the correction picker.</summary>
    public IReadOnlyList<SlotIdentity?> Current
    {
        get { lock (_lock) return _last.ToArray(); }
    }

    public Task<PartyState> IdentifyAsync(PartyState state) => Task.Run(() => Identify(state));

    private PartyState Identify(PartyState state)
    {
        if (!state.PanelVisible || library.IsEmpty) return state;

        var slots = new List<PartySlot>(state.Slots.Count);
        foreach (var slot in state.Slots)
        {
            int i = slot.Number - 1;
            SlotIdentity? previous;
            lock (_lock) previous = _last[i];

            SlotIdentity identity;
            if (previous is not null
                && _matcher.SimilarityToLearned(slot.Portrait, previous.Portrait) >= SamePortraitThreshold)
            {
                identity = previous;
            }
            else
            {
                identity = IdentifyPortrait(slot.Portrait);
                lock (_lock) _last[i] = identity;
            }
            slots.Add(slot with { Operator = identity.Match });
        }
        return state with { Slots = slots };
    }

    private SlotIdentity IdentifyPortrait(Frame portrait)
    {
        if (_matcher.MatchLearned(portrait) is { } learned)
        {
            var rest = library.Operators.Where(o => o.Id != learned.Operator.Id).OrderBy(o => o.NameZh, StringComparer.Ordinal);
            return new SlotIdentity(portrait, learned, [learned.Operator, .. rest]);
        }

        var (guess, ranking) = _matcher.MatchIcon(portrait);
        return new SlotIdentity(portrait, guess, ranking.Select(r => r.Operator).ToList());
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

        if (actual is not null)
        {
            await library.LearnAsync(actual, current.Portrait);
            if (library.Calibration is null) _matcher.Calibrate(current.Portrait, actual);
        }

        lock (_lock)
        {
            _last[i] = current with
            {
                Match = actual is null ? null : new OperatorMatch(actual, 1, FromLearned: true),
            };
        }
    }
}
