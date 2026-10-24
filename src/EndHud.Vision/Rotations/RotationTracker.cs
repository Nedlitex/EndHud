namespace EndHud.Vision.Rotations;

public enum CheckKind
{
    /// <summary>The step's own ability is available (combo / ultimate ready; a skill point and no cooldown for a battle skill).</summary>
    OwnAbility,

    /// <summary>The previous step's ability has been used (it's cooling or spent): the player is past it.</summary>
    PreviousStepUsed,

    /// <summary>One of the step's expected-state conditions.</summary>
    Expected,
}

/// <summary>One thing the current step expects, checked against this reading.</summary>
/// <param name="Condition">For <see cref="CheckKind.Expected"/>: the condition.</param>
/// <param name="Holds">True / false, or null when it can't be seen right now.</param>
public sealed record ConditionCheck(CheckKind Kind, StateCondition? Condition, bool? Holds);

/// <summary>Where the player is in the rotation.</summary>
/// <param name="Step">The current step (index into <see cref="Rotation.Played"/>).</param>
/// <param name="Ready">Its own ability is available and every expected condition that can be seen holds: time to use it.</param>
/// <param name="Checks">The current step's conditions and whether each holds (for the strip).</param>
public sealed record RotationPosition(PlayedStep Step, bool Ready, IReadOnlyList<ConditionCheck> Checks);

/// <summary>
/// Finds the player's place in a rotation from what's on screen; there is nothing to reset.
/// <list type="bullet">
/// <item>Phases with an entry condition: the moment one's condition turns true (it wasn't seen true
/// before; unseen readings don't count either way), the position moves to that phase's first step,
/// even mid-phase. If several turn true together, the first in the list wins. The condition isn't
/// checked again inside the phase, and a phase entered this way can't be left by the recovery jump
/// below: it plays to its end, or until another phase's condition turns true.</item>
/// <item>Each reading, every step gets a score: of its checks that can be seen now, the share that
/// hold (smoothed, so a step with more checks confirming it wins over one with fewer). The checks:
/// its own ability is available; the step before it (in its phase, or the end of the phase the flow
/// comes from) was seen being used in the last <see cref="RecentReadings"/> readings (not seen
/// counts as unknown, not as no); and its expected state. Checks that can't be seen don't count.</item>
/// <item>The first reading takes the best-scoring step of the phases without an entry condition
/// (unless a condition already holds: see above).</item>
/// <item>After that the position moves on only when the current step's ability is seen being used
/// (combo ready → cooling, ultimate ready → spent, that slot's battle skill button turning to a
/// countdown); steps marked <see cref="RotationStep.WithPrevious"/> may come in either order. After a
/// phase's last step comes the next phase without an entry condition (<see cref="Rotation.NextInFlow"/>).</item>
/// <item>It jumps elsewhere only when the screen clearly says it's somewhere else: another step
/// scoring <see cref="JumpMargin"/> more than the current one for <see cref="JumpReadings"/> readings
/// in a row (the player went off-script, or EndHud missed a step). It only jumps within the current
/// phase or to phases without an entry condition.</item>
/// </list>
/// </summary>
public sealed class RotationTracker(Rotation rotation)
{
    public const int JumpReadings = 2;
    public const double JumpMargin = 0.34;

    /// <summary>How long an ability counts as "just used": 40 readings, 10 s at the default 250 ms.</summary>
    public const int RecentReadings = 40;

    private int _reading;
    private readonly Dictionary<(int Slot, StepAction Action), int> _usedAt = new();

    private readonly IReadOnlyList<PlayedStep> _steps = rotation.Played();
    private int? _position;
    private int? _jumpTarget;
    private int _jumpReadings;
    private PartyState? _previous;
    private readonly HashSet<int> _doneInGroup = [];

    /// <summary>Each phase's entry condition as last seen (true / false; null: never seen).</summary>
    private readonly bool?[] _gateSeen = new bool?[rotation.Phases.Count];

    /// <summary>The phase entered through its entry condition, while the position is in it.</summary>
    private int? _enteredByGate;

    public Rotation Rotation => rotation;

    /// <summary>Take a reading; returns the position, or null when the rotation has no steps or the squad isn't known.</summary>
    public RotationPosition? Update(PartyState state)
    {
        if (_steps.Count == 0 || !state.PanelVisible) return Current(state);
        var slots = SlotsByOperator(state);
        if (slots.Count == 0) return null;

        _reading++;
        if (_previous is { } last) RememberUses(last, state);

        // 1. Entry conditions that turn true now: straight into that phase.
        if (GateTurnedTrue(state, slots) is { } entered)
        {
            MoveTo(FirstStep(entered));
            _enteredByGate = entered;
            _previous = state;
            return Current(state);
        }

        // 2. Steps seen done since the last reading move the position on.
        if (_position is { } at && _previous is { } before)
        {
            foreach (int done in DoneSteps(at, before, state, slots)) _doneInGroup.Add(done);
            int groupEnd = GroupEnd(at);
            if (_doneInGroup.Contains(at) && Enumerable.Range(at, groupEnd - at + 1).All(_doneInGroup.Contains))
            {
                int phase = _steps[at].PhaseIndex;
                if (groupEnd < LastStep(phase)) MoveTo(groupEnd + 1);
                else
                {
                    // The phase is done: on to the next phase of the flow.
                    _enteredByGate = null;
                    MoveTo(rotation.NextInFlow(phase) is { } next ? FirstStep(next) : FirstStep(phase));
                }
            }
        }
        _previous = state;

        // 3. The best-matching step, among the steps the player could be on.
        int best = -1;
        double bestScore = double.NegativeInfinity;
        for (int i = 0; i < _steps.Count; i++)
        {
            if (!Reachable(i)) continue;
            double score = Score(i, state, slots).Score;
            if (score > bestScore) (best, bestScore) = (i, score);
        }
        if (best < 0) return Current(state);

        if (_position is not { } current)
        {
            _position = best;
        }
        else if (best != current && bestScore - Score(current, state, slots).Score >= JumpMargin)
        {
            // Somewhere else: only after it has clearly won JumpReadings readings in a row.
            _jumpReadings = _jumpTarget == best ? _jumpReadings + 1 : 1;
            _jumpTarget = best;
            if (_jumpReadings >= JumpReadings)
            {
                if (_steps[best].PhaseIndex != _steps[current].PhaseIndex) _enteredByGate = null;
                MoveTo(best);
            }
        }
        else
        {
            _jumpTarget = null;
        }
        return Current(state);
    }

    /// <summary>
    /// Update every entry condition's last-seen value, and return the first phase (in list order)
    /// whose condition turned true now, unless the position is already in it.
    /// </summary>
    private int? GateTurnedTrue(PartyState state, IReadOnlyDictionary<string, int> slots)
    {
        int? turned = null;
        for (int p = 0; p < rotation.Phases.Count; p++)
        {
            var phase = rotation.Phases[p];
            if (!phase.Gated || phase.Steps.Count == 0) continue;
            // This step tracker isn't what the overlay shows phases with (PhaseSelector is), so it counts nothing.
            bool? now = HoldsAll(phase.When, state, slots, PhaseHistory.None);
            if (now is null) continue; // unseen: neither opens nor closes it
            bool turnedTrue = now == true && _gateSeen[p] != true;
            _gateSeen[p] = now;
            if (turnedTrue && turned is null && (_position is not { } at || _steps[at].PhaseIndex != p)) turned = p;
        }
        return turned;
    }

    /// <summary>Steps the position may be found at: its own phase, and the phases of the flow (unless inside a phase entered by its condition).</summary>
    private bool Reachable(int step)
    {
        int phase = _steps[step].PhaseIndex;
        if (_position is { } at && _steps[at].PhaseIndex == phase) return true;
        if (_enteredByGate is not null) return false;
        return !rotation.Phases[phase].Gated;
    }

    private void MoveTo(int step)
    {
        _position = step;
        _doneInGroup.Clear();
        _jumpTarget = null;
        _jumpReadings = 0;
    }

    private int FirstStep(int phase) => _steps.First(s => s.PhaseIndex == phase).Index;

    private int LastStep(int phase) => _steps.Last(s => s.PhaseIndex == phase).Index;

    private RotationPosition? Current(PartyState state)
    {
        if (_position is not { } at) return null;
        var (_, ready, checks) = Score(at, state, SlotsByOperator(state));
        return new RotationPosition(_steps[at], ready, checks);
    }

    /// <summary>The last step of the group starting at <paramref name="at"/> (steps marked WithPrevious belong to it), within its phase.</summary>
    private int GroupEnd(int at)
    {
        int end = at;
        while (end + 1 < _steps.Count && _steps[end + 1].PhaseIndex == _steps[at].PhaseIndex && _steps[end + 1].Step.WithPrevious) end++;
        return end;
    }

    private IEnumerable<int> DoneSteps(int at, PartyState before, PartyState now, IReadOnlyDictionary<string, int> slots)
    {
        for (int i = at; i <= GroupEnd(at); i++)
        {
            var step = _steps[i].Step;
            if (!slots.TryGetValue(step.OperatorId, out int slot)) continue;
            var (was, @is) = (before.Slots[slot], now.Slots[slot]);
            bool used = step.Action switch
            {
                StepAction.Combo => ComboUsed(was, @is),
                StepAction.Ultimate => was.Ultimate?.Ready == true && @is.Ultimate is { Ready: false },
                StepAction.BattleSkill => was.BattleSkill != BattleSkillState.Cooling && @is.BattleSkill == BattleSkillState.Cooling,
                // Display-only: EndHud can't see a basic attack, so it's never seen "used".
                StepAction.BasicAttack => false,
                _ => false,
            };
            if (used) yield return i;
        }
    }

    /// <summary>
    /// The step the player does just before <paramref name="index"/>'s group: the one before it in its
    /// phase, or for a phase's first step, the last step of the phase the flow comes from. None for
    /// the first step of a phase entered by its condition (it can come after anything).
    /// </summary>
    private RotationStep? StepBefore(int index)
    {
        int groupStart = index;
        while (_steps[groupStart].StepInPhase > 0 && _steps[groupStart].Step.WithPrevious) groupStart--;
        var at = _steps[groupStart];
        if (at.StepInPhase > 0) return _steps[groupStart - 1].Step;
        if (rotation.Phases[at.PhaseIndex].Gated) return null;
        return rotation.PreviousInFlow(at.PhaseIndex) is { } previous ? _steps[LastStep(previous)].Step : null;
    }

    /// <summary>
    /// The share of the step's visible checks that hold (for finding the position), whether it's
    /// ready (own ability and expected state; the previous-step check only helps find the position),
    /// and each check.
    /// </summary>
    private (double Score, bool Ready, IReadOnlyList<ConditionCheck> Checks) Score(int index, PartyState state,
        IReadOnlyDictionary<string, int> slots)
    {
        var step = _steps[index].Step;
        var checks = new List<ConditionCheck> { new(CheckKind.OwnAbility, null, AbilityAvailable(step, state, slots)) };
        if (StepBefore(index) is { } previous && !ReferenceEquals(previous, step))
            checks.Add(new ConditionCheck(CheckKind.PreviousStepUsed, null, AbilityUsed(previous, slots)));
        foreach (var condition in step.Expect) checks.Add(new ConditionCheck(CheckKind.Expected, condition, Holds(condition, state, slots, PhaseHistory.None)));

        // The share that hold, smoothed so more checks seen counts for more: one check out of one
        // (0.75) loses to three out of three (0.875); nothing seen is 0.5.
        var known = checks.Where(c => c.Holds is not null).ToList();
        double score = (known.Count(c => c.Holds == true) + 0.5) / (known.Count + 1);
        var forReady = checks.Where(c => c.Kind != CheckKind.PreviousStepUsed).ToList();
        bool ready = forReady[0].Holds == true && forReady.All(c => c.Holds != false);
        return (score, ready, checks);
    }

    /// <summary>Was the step's ability seen being used in the last <see cref="RecentReadings"/> readings? Null when not seen.</summary>
    private bool? AbilityUsed(RotationStep step, IReadOnlyDictionary<string, int> slots)
    {
        if (!slots.TryGetValue(step.OperatorId, out int slot)) return null;
        return _usedAt.TryGetValue((slot, step.Action), out int at) && _reading - at <= RecentReadings ? true : null;
    }

    /// <summary>Note every ability seen being used between two readings (any slot, not just the current step's).</summary>
    /// <summary>
    /// The combo bar went from full to nearly empty. A bar just under full can dip below "ready" and
    /// back (read 0.91–0.99 after, in the recordings), while every use read 0–6% after it.
    /// </summary>
    internal static bool ComboUsed(PartySlot was, PartySlot now) =>
        was.Combo?.Ready == true && now.Combo is { Ready: false, Progress: < ComboUsedBelow };

    internal const double ComboUsedBelow = 0.3;

    private void RememberUses(PartyState before, PartyState now)
    {
        for (int i = 0; i < Math.Min(before.Slots.Count, now.Slots.Count); i++)
        {
            var (was, @is) = (before.Slots[i], now.Slots[i]);
            if (ComboUsed(was, @is)) _usedAt[(i, StepAction.Combo)] = _reading;
            if (was.Ultimate?.Ready == true && @is.Ultimate is { Ready: false }) _usedAt[(i, StepAction.Ultimate)] = _reading;
            if (was.BattleSkill != BattleSkillState.Cooling && @is.BattleSkill == BattleSkillState.Cooling) _usedAt[(i, StepAction.BattleSkill)] = _reading;
        }
    }

    internal static bool? AbilityAvailable(RotationStep step, PartyState state, IReadOnlyDictionary<string, int> slots)
    {
        if (!slots.TryGetValue(step.OperatorId, out int slot)) return null;
        var s = state.Slots[slot];
        return step.Action switch
        {
            StepAction.Combo => s.Combo?.Ready,
            StepAction.Ultimate => s.Ultimate?.Ready,
            StepAction.BattleSkill => s.BattleSkill == BattleSkillState.Cooling ? false
                : state.SkillPoints is { } sp ? sp >= 1 : null,
            // Display-only: whether a basic attack is "available" can't be seen.
            StepAction.BasicAttack => null,
            _ => null,
        };
    }

    /// <summary>Does the condition hold in this reading? Null when it can't be seen.</summary>
    /// <param name="history">How often each phase has been shown this fight: the phase selector passes
    /// itself; anything outside one passes <see cref="PhaseHistory.None"/> ("phase shown" is unknown).</param>
    public static bool? Evaluate(StateCondition condition, PartyState state, IPhaseHistory history) =>
        Holds(condition, state, SlotsByOperator(state), history);

    /// <summary>
    /// Is this condition ignored, as if it weren't there? Only while the battle-skill keys aren't read
    /// (<see cref="IPhaseHistory.PlayedIgnored"/>), and only when it is made of nothing but "phase
    /// played" parts (<see cref="ConditionWalk.OnlyPlayed"/>).
    /// </summary>
    public static bool Ignored(StateCondition condition, IPhaseHistory history) => false;

    /// <summary>All of them hold: false if any is false, else unknown if any can't be seen, else true.</summary>
    private static bool? HoldsAll(IEnumerable<StateCondition> conditions, PartyState state, IReadOnlyDictionary<string, int> slots, IPhaseHistory history)
    {
        bool unknown = false;
        foreach (var condition in conditions)
        {
            switch (Holds(condition, state, slots, history))
            {
                case false: return false;
                case null: unknown = true; break;
            }
        }
        return unknown ? null : true;
    }

    /// <summary>One of them holds: true if any is true, else unknown if any can't be seen, else false.</summary>
    private static bool? HoldsAny(IEnumerable<StateCondition> conditions, PartyState state, IReadOnlyDictionary<string, int> slots, IPhaseHistory history)
    {
        bool unknown = false;
        foreach (var condition in conditions)
        {
            switch (Holds(condition, state, slots, history))
            {
                case true: return true;
                case null: unknown = true; break;
            }
        }
        return unknown ? null : false;
    }

    /// <summary>Does the condition hold in this reading? Null when it can't be seen.</summary>
    internal static bool? Holds(StateCondition condition, PartyState state, IReadOnlyDictionary<string, int> slots, IPhaseHistory history)
    {
        switch (condition)
        {
            case AllCondition all:
                return HoldsAll(all.Of, state, slots, history);
            case AnyCondition any:
                return HoldsAny(any.Of, state, slots, history);
            case NotCondition not:
                return !Holds(not.Of, state, slots, history);
            case PhaseShownCondition c:
                return history.TimesShown(c.PhaseId) is { } shown ? Compare(shown, c.Is, c.Times) : null;
            case SkillPointsCondition c:
                return state.SkillPoints is { } sp ? Compare(sp, c.Is, c.Value) : null;
            case ComboCondition c:
                return slots.TryGetValue(c.OperatorId, out int cs) && state.Slots[cs].Combo is { } combo ? combo.Ready == c.Ready : null;
            case UltimateCondition c:
                return slots.TryGetValue(c.OperatorId, out int us) && state.Slots[us].Ultimate is { } ult ? ult.Ready == c.Ready : null;
            case UltimateChargeCondition c:
                return slots.TryGetValue(c.OperatorId, out int ucs) && state.Slots[ucs].Ultimate is { } charge
                    ? Compare(charge.Ready ? 100 : charge.Progress * 100, c.Is, c.Percent)
                    : null;
            case UltimateActiveCondition c:
                return UltimateRunning(c.OperatorId, state) is { } running ? running == c.Active : null;
            case ComboChargeCondition c:
                return slots.TryGetValue(c.OperatorId, out int ccs) && state.Slots[ccs].Combo is { } bar
                    ? Compare(bar.Ready ? 100 : bar.Progress * 100, c.Is, c.Percent)
                    : null;
            case BattleSkillCondition c:
                return slots.TryGetValue(c.OperatorId, out int bss) && state.Slots[bss].BattleSkill is { } skill
                    ? (skill == BattleSkillState.Available) == c.Ready
                    : null;
            case CounterCondition c:
                var reading = state.Counters.FirstOrDefault(r => r.OperatorId == c.OperatorId && r.CounterId == c.CounterId);
                return reading?.Value is { } value ? Compare(value, c.Is, c.Value) : null;
            case BossStatusCondition c:
                if (state.BossStatuses is not { } statuses) return null;
                var match = statuses.FirstOrDefault(s => s.StatusId == c.StatusId);
                if (c.MinStacks <= 1) return (match is not null) == c.Present;
                if (match is null) return !c.Present;            // not there: 0 stacks
                if (match.Stacks is not { } stacks) return null; // there, but its count isn't read (covered, or not a counted status)
                return (stacks >= c.MinStacks) == c.Present;
            case ControllingCondition c:
                if (state.ActiveSlot is not { } active) return null;
                return slots.TryGetValue(c.OperatorId, out int controlled) ? controlled == active - 1 : false;
            default:
                return null;
        }
    }

    /// <summary>
    /// Is this operator's lasting ultimate running? The time bar isn't the controlled operator's (an
    /// ultimate cast from the background runs its bar under whoever is controlled): it holds for
    /// the ones <see cref="LastingUltimates"/> has running, on the bar or carried on in the
    /// background (<see cref="PartyState.RunningUltimates"/>; worked out from this reading alone
    /// when the caller didn't). Null when it can't be told: the bottom HUD out of sight (no skill
    /// points read), or a bar on screen that isn't known to be hers or anyone's for an operator
    /// whose ultimate isn't one of the known lasting ones (it could be hers, as Yvonne's teal one was before it was measured).
    /// </summary>
    private static bool? UltimateRunning(string operatorId, PartyState state)
    {
        if (state.SkillPoints is null) return null;
        var running = state.RunningUltimates ?? new LastingUltimates().Update(state, TimeSpan.Zero);
        if (running.ContainsKey(operatorId)) return true;
        if (LastingUltimates.Operators.Contains(operatorId) || state.UltimateTimeLeft is null) return false;
        return null;
    }

    private static bool Compare(double value, Comparison comparison, double target) => comparison switch
    {
        Comparison.AtLeast => value >= target - 1e-6,
        Comparison.AtMost => value <= target + 1e-6,
        Comparison.Below => value < target - 1e-6,
        Comparison.Above => value > target + 1e-6,
        _ => Math.Abs(value - target) < 0.05,
    };

    /// <summary>Operator id → slot index (0-based), from the identified squad.</summary>
    internal static IReadOnlyDictionary<string, int> SlotsByOperator(PartyState state)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < state.Slots.Count; i++)
            if (state.Slots[i].Operator?.Operator.Id is { } id) map.TryAdd(id, i);
        return map;
    }
}
