namespace EndHud.Vision.Rotations;

/// <summary>How the strip draws a step of the phase it shows.</summary>
public enum StepMark
{
    /// <summary>Seen used this round of the phase: drawn faded.</summary>
    Done,

    /// <summary>The step to use next (several, for steps used together): drawn with chevrons.</summary>
    Current,

    /// <summary>Later in the phase: drawn as always.</summary>
    Upcoming,
}

/// <summary>Where the player is inside the phase on the strip.</summary>
/// <param name="Phase">The phase (index into <see cref="Rotation.Phases"/>).</param>
/// <param name="Marks">Each step of the phase, in order.</param>
/// <param name="Ready">The first current step can be used now (its own ability is available and its expected state holds).</param>
/// <param name="Checks">That step's checks, for the reasons under the strip.</param>
public sealed record StepPosition(int Phase, IReadOnlyList<StepMark> Marks, bool Ready, IReadOnlyList<ConditionCheck> Checks);

/// <summary>
/// Which step of the phase on the strip the player is on. The phase comes from
/// <see cref="PhaseSelector"/> (the authority); this only follows inside it, and starts over when
/// the phase changes. A step is done when it is seen used: a counted press of its slot for a battle
/// skill (<see cref="SkillPressJudge"/>), the combo bar emptying for a combo, the ultimate going
/// from ready to spent. Screen-seen battle-skill cooldowns are NOT used: the press is the cast, and
/// counting both would count one cast twice. A basic-attack step never holds the marker: the next
/// real cast of a later step passes it. A phase played through starts again at its first step.
/// </summary>
public sealed class StepFollower(Rotation rotation)
{
    private readonly IReadOnlyList<PlayedStep> _played = rotation.Played();
    private int _phase = -1;
    private bool[] _done = [];

    /// <summary>The fight ended: the next one starts at the first step.</summary>
    public void EndFight() => _phase = -1;

    /// <param name="phase">The phase on the strip (<see cref="PhaseSelector"/>).</param>
    /// <param name="before">The reading before this one; null for the first.</param>
    /// <param name="castSlots">The slots (1-4) whose battle-skill press counted since <paramref name="before"/>, oldest first.</param>
    /// <param name="history">For "phase shown" conditions in the steps' expected state.</param>
    public StepPosition Update(int phase, PartyState? before, PartyState now, IReadOnlyList<int> castSlots, IPhaseHistory history)
    {
        var steps = _played.Where(p => p.PhaseIndex == phase).ToList();
        if (phase != _phase || _done.Length != steps.Count)
        {
            _phase = phase;
            _done = new bool[steps.Count];
        }
        var slots = RotationTracker.SlotsByOperator(now);
        var uses = castSlots.Select(s => (Slot: s - 1, Action: StepAction.BattleSkill)).ToList();
        if (before is not null) uses.AddRange(VisionUses(before, now));
        foreach (var use in uses) Apply(use, steps, slots);
        if (_done.Length > 0 && _done.All(d => d)) Array.Clear(_done);
        return Position(phase, steps, now, slots, history);
    }

    /// <summary>Combos and ultimates seen used between two readings (<see cref="RotationTracker"/>'s rules).</summary>
    private static IEnumerable<(int Slot, StepAction Action)> VisionUses(PartyState before, PartyState now)
    {
        for (int i = 0; i < Math.Min(before.Slots.Count, now.Slots.Count); i++)
        {
            if (RotationTracker.ComboUsed(before.Slots[i], now.Slots[i])) yield return (i, StepAction.Combo);
            if (before.Slots[i].Ultimate?.Ready == true && now.Slots[i].Ultimate is { Ready: false }) yield return (i, StepAction.Ultimate);
        }
    }

    /// <summary>
    /// Mark the step this use is: in the current group, or past groups of basic attacks only. A use
    /// that matches nothing reachable (a skill off-script, a later step while an earlier skill is
    /// undone) moves nothing.
    /// </summary>
    private void Apply((int Slot, StepAction Action) use, List<PlayedStep> steps, IReadOnlyDictionary<string, int> slots, bool wrapped = false)
    {
        int at = Array.IndexOf(_done, false);
        if (at < 0) return;
        for (int start = at; start < steps.Count; start = GroupEnd(steps, start) + 1)
        {
            int end = GroupEnd(steps, start);
            for (int i = start; i <= end; i++)
            {
                var step = steps[i].Step;
                if (_done[i] || step.Action != use.Action || !slots.TryGetValue(step.OperatorId, out int s) || s != use.Slot) continue;
                for (int j = at; j < start; j++) _done[j] = true; // basic attacks passed
                _done[i] = true;
                return;
            }
            // A skill not yet seen used holds the marker here.
            for (int i = start; i <= end; i++)
                if (!_done[i] && steps[i].Step.Action != StepAction.BasicAttack) return;
        }
        // Only basic attacks were left: the phase is over, the use may be its first step again.
        if (!wrapped)
        {
            Array.Clear(_done);
            Apply(use, steps, slots, wrapped: true);
        }
    }

    /// <summary>The last step of the group starting at <paramref name="at"/>: the steps after it marked WithPrevious belong to it.</summary>
    private static int GroupEnd(List<PlayedStep> steps, int at)
    {
        int end = at;
        while (end + 1 < steps.Count && steps[end + 1].Step.WithPrevious) end++;
        return end;
    }

    /// <summary>The marks; whether the first current step is ready, and its checks.</summary>
    private StepPosition Position(int phase, List<PlayedStep> steps, PartyState now, IReadOnlyDictionary<string, int> slots, IPhaseHistory history)
    {
        var marks = new StepMark[steps.Count];
        int at = Array.IndexOf(_done, false);
        // The current group: the one the first not-done step is in (it may be inside a group, after a step used out of order).
        int groupStart = at;
        while (groupStart > 0 && steps[groupStart].Step.WithPrevious) groupStart--;
        int groupEnd = at < 0 ? -1 : GroupEnd(steps, groupStart);
        for (int i = 0; i < steps.Count; i++)
            marks[i] = _done[i] ? StepMark.Done : i <= groupEnd ? StepMark.Current : StepMark.Upcoming;
        if (at < 0) return new StepPosition(phase, marks, false, []);

        var step = steps[at].Step;
        // A basic attack's availability can't be seen, and there's never anything to wait for.
        bool? own = step.Action == StepAction.BasicAttack ? true : RotationTracker.AbilityAvailable(step, now, slots);
        var checks = new List<ConditionCheck> { new(CheckKind.OwnAbility, null, own) };
        foreach (var condition in step.Expect)
            checks.Add(new ConditionCheck(CheckKind.Expected, condition, RotationTracker.Holds(condition, now, slots, history)));
        bool ready = own == true && checks.All(c => c.Holds != false);
        return new StepPosition(phase, marks, ready, checks);
    }
}
