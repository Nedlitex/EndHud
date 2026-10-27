namespace EndHud.Vision.Rotations;

/// <summary>
/// Which phase of a rotation applies now, from what's on screen. A phase's condition (<c>when</c>)
/// says when it applies: each reading, the first phase in the list whose condition holds is the
/// one shown (the last one, with <see cref="Rotation.LastMatchWins"/>); when a phase sets a priority
/// (<see cref="RotationPhase.Priority"/>, <see cref="RotationPhase.PriorityRules"/>), the biggest
/// priority among those that hold wins, and only equal ones go by list order. Every phase at 0 (as
/// every rotation from before priorities) is list order alone.
/// <list type="bullet">
/// <item>When none holds and every condition could be read, the first phase without a condition
/// is shown: it's the "otherwise" phase. (Before, the phase already shown stayed, so once a phase
/// with a condition had been shown, one without could never come back: a player's 充能轴 I "while
/// Laevatain's ultimate is at most 50%" never gave way to 充能轴 II.)</item>
/// <item>When none holds but some condition can't be read (a cutscene, a reading lost), the phase
/// already shown stays; so does it when every phase has a condition and none holds.</item>
/// <item>At the start, before anything holds, the first phase without a condition is shown, or the
/// first phase when every phase has one.</item>
/// </list>
/// <para>A phase on the strip, until it is played through, judges "the current phase is" in its own condition as it
/// stood when it came on: its condition is when to switch to it, not how long to stay. A player's 充能轴 II "when the
/// current phase is 充能轴 I, played through" (GitHub issue 5) held for the one reading that switched to it, then failed
/// (the current phase was now II), and the strip went back to I; II was never followed, and with both phases' finish
/// conditions alike the strip flickered between them. The rest of the condition is read live, so a phase "while Si's
/// skill runs" still gives way when it stops.</para>
/// <para>This only picks the phase; it doesn't follow the steps inside it. That is
/// <see cref="StepFollower"/>'s job, from the battle skills, combos and ultimates seen used.</para>
/// <para>It counts how many times each phase has come onto the strip this fight, for "phase shown"
/// conditions (<see cref="PhaseShownCondition"/>); <see cref="EndFight"/> forgets them.</para>
/// </summary>
public sealed class PhaseSelector(Rotation rotation) : IPhaseHistory
{
    private readonly IReadOnlyList<PlayedStep> _played = rotation.Played();
    private readonly AllCondition?[] _conditions = rotation.Phases.Select(p => p.Gated ? new AllCondition(p.When) : null).ToArray();
    private readonly (AllCondition When, int Priority)[][] _rules =
        rotation.Phases.Select(p => p.PriorityRules.Select(r => (new AllCondition(r.When), r.Priority)).ToArray()).ToArray();
    private readonly int? _otherwise = Array.FindIndex(rotation.Phases.ToArray(), p => !p.Gated) is var i and >= 0 ? i : null;
    private int? _current;
    // The phase on the strip before the one on it now, and whether it had been played through when it gave way.
    private (string Phase, bool? PlayedThrough)? _cameFrom;
    private readonly int[] _timesShown = new int[rotation.Phases.Count];
    private StepFollower? _plays;

    public Rotation Rotation => rotation;

    /// <summary>Times the phase was played through this fight, from the step follower of the last <see cref="Update"/>; null before the first.</summary>
    public int? TimesPlayed(string phaseId) => _plays?.TimesPlayed(phaseId);

    /// <summary>
    /// The phase on the strip (<see cref="PhaseCurrentCondition"/>): during an <see cref="Update"/>, the
    /// one shown by the reading before, as the counts are; null before the first reading of a fight.
    /// </summary>
    public string? CurrentPhase => _current is { } i ? rotation.Phases[i].Id : null;

    /// <summary>Played through since it last came onto the strip, from the step follower of the last <see cref="Update"/>; null before the first.</summary>
    public bool? PlayedThroughThisShowing(string phaseId) => _plays?.PlayedThroughThisShowing(phaseId);

    /// <summary>The lights of the phase on the strip this round, from the step follower of the last <see cref="Update"/>; null before the first.</summary>
    public IReadOnlyList<bool?>? StepsLit => _plays?.StepsLit;

    /// <summary>Times the phase has come onto the strip this fight (the one on it now included); null for a phase not in the rotation.</summary>
    public int? TimesShown(string phaseId)
    {
        for (int i = 0; i < rotation.Phases.Count; i++)
            if (rotation.Phases[i].Id == phaseId) return _timesShown[i];
        return null;
    }

    /// <summary>The fight ended: the next one starts with nothing shown and every count at 0.</summary>
    public void EndFight()
    {
        _current = null;
        _cameFrom = null;
        Array.Clear(_timesShown);
    }

    /// <summary>Take a reading; where the strip is now (the first step of the phase that applies).</summary>
    /// <param name="plays">The step follower of this rotation, which counts the phases played through.</param>
    public RotationPosition Update(PartyState state, StepFollower plays)
    {
        _plays = plays;
        // The conditions see the counts as they were before this reading's switch, so a gate on
        // another phase's count opens on the reading after that phase first shows.
        int? before = _current;
        int? holding = null;
        int best = 0;
        bool unknown = false;
        // The phase on the strip, not played through yet, sees the phase it came from as current, as when it came on.
        IPhaseHistory? asEntered = before is { } shown && _cameFrom is { } from && plays?.PlayedThroughThisShowing(rotation.Phases[shown].Id) == false
            ? new AsEntered(this, from.Phase, from.PlayedThrough)
            : null;
        for (int i = 0; i < _conditions.Length; i++)
        {
            if (_conditions[i] is not { } condition) continue;
            switch (RotationTracker.Evaluate(condition, state, i == before && asEntered is not null ? asEntered : this))
            {
                case true:
                    // The biggest priority wins; an equal one goes by list order: the first stays, or
                    // with "last match wins" the later one takes over.
                    int priority = PriorityOf(i, state);
                    if (holding is null || priority > best || (priority == best && rotation.LastMatchWins))
                        (holding, best) = (i, priority);
                    break;
                case null:
                    unknown = true;
                    break;
            }
        }

        if (holding is not null) _current = holding;
        else if (!unknown && _otherwise is { } otherwise) _current = otherwise;
        _current ??= _otherwise ?? 0;
        if (_current != before)
        {
            _timesShown[_current.Value]++; // it came onto the strip
            _cameFrom = before is { } gone ? (rotation.Phases[gone].Id, plays?.PlayedThroughThisShowing(rotation.Phases[gone].Id)) : null;
        }
        return new RotationPosition(_played.First(p => p.PhaseIndex == _current), true, []);
    }

    /// <summary>
    /// The history as the phase on the strip saw it when it came on: the phase it came from is current (played through as
    /// it was then); itself and every other phase as now.
    /// </summary>
    private sealed class AsEntered(PhaseSelector selector, string cameFrom, bool? cameFromPlayed) : IPhaseHistory
    {
        private IPhaseHistory Now => selector;

        public int? TimesShown(string phaseId) => Now.TimesShown(phaseId);

        public int? TimesPlayed(string phaseId) => Now.TimesPlayed(phaseId);

        public string? CurrentPhase => Now.CurrentPhase;

        public bool? PlayedThroughThisShowing(string phaseId) => Now.PlayedThroughThisShowing(phaseId);

        public IReadOnlyList<bool?>? StepsLit => Now.StepsLit;

        public bool? IsCurrent(string phaseId, bool? playedThrough) =>
            phaseId != cameFrom ? Now.IsCurrent(phaseId, playedThrough)
            : playedThrough is not { } wanted ? true
            : cameFromPlayed is { } played ? played == wanted : null;
    }

    /// <summary>
    /// The phase's priority in this reading: the first of its rules whose condition holds, else its base
    /// number. A rule that can't be read doesn't hold.
    /// </summary>
    private int PriorityOf(int phase, PartyState state)
    {
        foreach (var (condition, priority) in _rules[phase])
            if (RotationTracker.Evaluate(condition, state, this) == true) return priority;
        return rotation.Phases[phase].Priority;
    }
}
