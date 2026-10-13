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
/// <item>Each reading, every step gets a score: of its checks that can be seen now, the share that
/// hold (smoothed, so a step with more checks confirming it wins over one with fewer). The checks: its own ability is available; the previous step's ability was seen being used
/// in the last <see cref="RecentReadings"/> readings (not seen counts as unknown, not as no: at the
/// start of a fight an empty ultimate looks just like a spent one); and its expected state. Checks
/// that can't be seen (another operator's counter, a status not read this time) don't count.</item>
/// <item>The first reading takes the best-scoring step.</item>
/// <item>After that the position moves on only when the current step's ability is seen being used
/// (combo ready → cooling, ultimate ready → spent, that slot's battle skill button turning to a
/// countdown), so it waits on a step that isn't ready yet even when a later one is. Steps marked
/// <see cref="RotationStep.WithPrevious"/> may come in either order.</item>
/// <item>It jumps elsewhere only when the screen clearly says it's somewhere else: another step
/// scoring <see cref="JumpMargin"/> more than the current one for <see cref="JumpReadings"/> readings
/// in a row (the player went off-script, or EndHud missed a step).</item>
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

    public Rotation Rotation => rotation;

    /// <summary>Take a reading; returns the position, or null when the rotation has no steps or the squad isn't known.</summary>
    public RotationPosition? Update(PartyState state)
    {
        if (_steps.Count == 0 || !state.PanelVisible) return Current(state);
        var slots = SlotsByOperator(state);
        if (slots.Count == 0) return null;

        _reading++;
        if (_previous is { } last) RememberUses(last, state);

        // 1. Steps seen done since the last reading move the position on.
        if (_position is { } at && _previous is { } before)
        {
            foreach (int done in DoneSteps(at, before, state, slots)) _doneInGroup.Add(done);
            int groupEnd = GroupEnd(at);
            if (_doneInGroup.Contains(at) && Enumerable.Range(at, groupEnd - at + 1).All(_doneInGroup.Contains))
            {
                _position = (groupEnd + 1) % _steps.Count;
                _doneInGroup.Clear();
                _jumpTarget = null;
            }
        }
        _previous = state;

        // 2. The best-matching step.
        int best = 0;
        double bestScore = double.NegativeInfinity;
        for (int i = 0; i < _steps.Count; i++)
        {
            double score = Score(i, state, slots).Score;
            if (score > bestScore) (best, bestScore) = (i, score);
        }

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
                _position = best;
                _jumpTarget = null;
                _doneInGroup.Clear();
            }
        }
        else
        {
            _jumpTarget = null;
        }
        return Current(state);
    }

    private RotationPosition? Current(PartyState state)
    {
        if (_position is not { } at) return null;
        var (_, ready, checks) = Score(at, state, SlotsByOperator(state));
        return new RotationPosition(_steps[at], ready, checks);
    }

    /// <summary>The last step of the group starting at <paramref name="at"/> (steps marked WithPrevious belong to it).</summary>
    private int GroupEnd(int at)
    {
        int end = at;
        while (end + 1 < _steps.Count && _steps[end + 1].Step.WithPrevious) end++;
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
                StepAction.Combo => was.Combo?.Ready == true && @is.Combo is { Ready: false },
                StepAction.Ultimate => was.Ultimate?.Ready == true && @is.Ultimate is { Ready: false },
                StepAction.BattleSkill => was.BattleSkill != BattleSkillState.Cooling && @is.BattleSkill == BattleSkillState.Cooling,
                _ => false,
            };
            if (used) yield return i;
        }
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
        // The step before this one's group (steps used together may come in either order).
        int groupStart = index;
        while (groupStart > 0 && _steps[groupStart].Step.WithPrevious) groupStart--;
        var previous = _steps[(groupStart - 1 + _steps.Count) % _steps.Count].Step;
        if (!ReferenceEquals(previous, step))
            checks.Add(new ConditionCheck(CheckKind.PreviousStepUsed, null, AbilityUsed(previous, slots)));
        foreach (var condition in step.Expect) checks.Add(new ConditionCheck(CheckKind.Expected, condition, Holds(condition, state, slots)));

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
    private void RememberUses(PartyState before, PartyState now)
    {
        for (int i = 0; i < Math.Min(before.Slots.Count, now.Slots.Count); i++)
        {
            var (was, @is) = (before.Slots[i], now.Slots[i]);
            if (was.Combo?.Ready == true && @is.Combo is { Ready: false }) _usedAt[(i, StepAction.Combo)] = _reading;
            if (was.Ultimate?.Ready == true && @is.Ultimate is { Ready: false }) _usedAt[(i, StepAction.Ultimate)] = _reading;
            if (was.BattleSkill != BattleSkillState.Cooling && @is.BattleSkill == BattleSkillState.Cooling) _usedAt[(i, StepAction.BattleSkill)] = _reading;
        }
    }

    private static bool? AbilityAvailable(RotationStep step, PartyState state, IReadOnlyDictionary<string, int> slots)
    {
        if (!slots.TryGetValue(step.OperatorId, out int slot)) return null;
        var s = state.Slots[slot];
        return step.Action switch
        {
            StepAction.Combo => s.Combo?.Ready,
            StepAction.Ultimate => s.Ultimate?.Ready,
            StepAction.BattleSkill => s.BattleSkill == BattleSkillState.Cooling ? false
                : state.SkillPoints is { } sp ? sp >= 1 : null,
            _ => null,
        };
    }

    private static bool? Holds(StateCondition condition, PartyState state, IReadOnlyDictionary<string, int> slots)
    {
        switch (condition)
        {
            case SkillPointsCondition c:
                return state.SkillPoints is { } sp ? Compare(sp, c.Is, c.Value) : null;
            case ComboCondition c:
                return slots.TryGetValue(c.OperatorId, out int cs) && state.Slots[cs].Combo is { } combo ? combo.Ready == c.Ready : null;
            case UltimateCondition c:
                return slots.TryGetValue(c.OperatorId, out int us) && state.Slots[us].Ultimate is { } ult ? ult.Ready == c.Ready : null;
            case CounterCondition c:
                var reading = state.Counters.FirstOrDefault(r => r.OperatorId == c.OperatorId && r.CounterId == c.CounterId);
                return reading?.Value is { } value ? Compare(value, c.Is, c.Value) : null;
            case BossStatusCondition c:
                if (state.BossStatuses is not { } statuses) return null;
                if (c.MinStacks > 1) return null; // stack badges aren't read yet
                return statuses.Any(s => s.StatusId == c.StatusId) == c.Present;
            case ControllingCondition c:
                if (state.ActiveSlot is not { } active) return null;
                return slots.TryGetValue(c.OperatorId, out int controlled) ? controlled == active - 1 : false;
            default:
                return null;
        }
    }

    private static bool Compare(double value, Comparison comparison, double target) => comparison switch
    {
        Comparison.AtLeast => value >= target - 1e-6,
        Comparison.AtMost => value <= target + 1e-6,
        _ => Math.Abs(value - target) < 0.05,
    };

    /// <summary>Operator id → slot index (0-based), from the identified squad.</summary>
    private static IReadOnlyDictionary<string, int> SlotsByOperator(PartyState state)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < state.Slots.Count; i++)
            if (state.Slots[i].Operator?.Operator.Id is { } id) map.TryAdd(id, i);
        return map;
    }
}
