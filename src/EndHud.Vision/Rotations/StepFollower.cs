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

    /// <summary>RED: not marked yet.</summary>
    Skipped,
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
/// counting both would count one cast twice. A basic- or heavy-attack step (display-only) never holds the marker: the next
/// real cast of a later step passes it. A condition step (<see cref="StepAction.Condition"/>) is done the reading its
/// conditions hold; seen not holding, it holds the marker; when they can't be told, the next real cast of a later step
/// passes it, as a basic attack is passed (owner, 2026-10-24). A phase played through starts again at its first step.
/// <para>It counts, per phase, the rounds played through this fight (<see cref="TimesPlayed"/>, for
/// "phase played" conditions): a round counts the moment its last step that can be seen used is
/// done (trailing basic or heavy attacks are never seen), once. A phase left half way doesn't count; a phase
/// of nothing but basic or heavy attacks never does.</para>
/// </summary>
public sealed class StepFollower(Rotation rotation)
{
    private readonly IReadOnlyList<PlayedStep> _played = rotation.Played();
    private int _phase = -1;
    private bool[] _done = [];
    private readonly int[] _timesPlayed = new int[rotation.Phases.Count];

    /// <summary>This round of the phase has been counted as played through.</summary>
    private bool _counted;

    /// <summary>The phase followed has been played through since it came onto the strip (<see cref="PhaseCurrentCondition"/>).</summary>
    private bool _playedThisShowing;

    /// <summary>
    /// Has this phase been played through since it last came onto the strip? False for a phase that
    /// isn't the one followed now; null for a phase not in the rotation.
    /// </summary>
    public bool? PlayedThroughThisShowing(string phaseId)
    {
        for (int i = 0; i < rotation.Phases.Count; i++)
            if (rotation.Phases[i].Id == phaseId) return i == _phase && _playedThisShowing;
        return null;
    }

    /// <summary>
    /// Times the phase was played through this fight (<see cref="PhasePlayedCondition"/>); null for a
    /// phase not in the rotation.
    /// </summary>
    public int? TimesPlayed(string phaseId)
    {
        for (int i = 0; i < rotation.Phases.Count; i++)
            if (rotation.Phases[i].Id == phaseId) return _timesPlayed[i];
        return null;
    }

    /// <summary>The fight ended: the next one starts at the first step, every phase played 0 times.</summary>
    public void EndFight()
    {
        _phase = -1;
        _playedThisShowing = false;
        Array.Clear(_timesPlayed);
    }

    /// <param name="phase">The phase on the strip (<see cref="PhaseSelector"/>).</param>
    /// <param name="before">The reading before this one; null for the first.</param>
    /// <param name="castSlots">The slots (1-4) whose battle-skill press counted since <paramref name="before"/>, oldest first.</param>
    /// <param name="history">For "phase shown" conditions in the steps' expected state.</param>
    public StepPosition Update(int phase, PartyState? before, PartyState now, IReadOnlyList<SkillCast> casts, IPhaseHistory history)
    {
        var steps = _played.Where(p => p.PhaseIndex == phase).ToList();
        if (phase != _phase || _done.Length != steps.Count)
        {
            if (phase != _phase) _playedThisShowing = false; // it came onto the strip
            _phase = phase;
            _done = new bool[steps.Count];
            _counted = false;
        }
        var slots = RotationTracker.SlotsByOperator(now);
        // A condition step's conditions in this reading: true, false, or unknown (null). Null for any other step.
        bool? Holds(RotationStep step) => step.Action == StepAction.Condition
            ? RotationTracker.Holds(new AllCondition(step.Expect), now, slots, history)
            : null;
        PassConditions(steps, Holds);
        var uses = casts.Where(c => c.Slot is not null).Select(c => (Slot: c.Slot!.Value - 1, Action: StepAction.BattleSkill)).ToList();
        if (before is not null) uses.AddRange(VisionUses(before, now));
        foreach (var use in uses)
        {
            Apply(use, steps, slots, Holds);
            CountIfPlayedThrough(steps);
            PassConditions(steps, Holds);
        }
        if (_done.Length > 0 && _done.All(d => d)) NewRound();
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
    /// Mark the step this use is: in the current group, or past groups of basic or heavy attacks only. A use
    /// that matches nothing reachable (a skill off-script, a later step while an earlier skill is
    /// undone) moves nothing.
    /// </summary>
    private void Apply((int Slot, StepAction Action) use, List<PlayedStep> steps, IReadOnlyDictionary<string, int> slots, Func<RotationStep, bool?> holds, bool wrapped = false)
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
                for (int j = at; j < start; j++) _done[j] = true; // basic or heavy attacks, or conditions that can't be told, passed
                _done[i] = true;
                return;
            }
            // A skill not yet seen used holds the marker here, and so does a condition step seen not holding.
            for (int i = start; i <= end; i++)
                if (!_done[i] && !Passable(steps[i].Step, holds)) return;
        }
        // Only steps a cast passes were left: the phase is over, the use may be its first step again.
        if (!wrapped)
        {
            NewRound();
            Apply(use, steps, slots, holds, wrapped: true);
        }
    }

    /// <summary>A step the next real cast of a later step passes: a basic or heavy attack, or a condition step whose conditions can't be told.</summary>
    private static bool Passable(RotationStep step, Func<RotationStep, bool?> holds) =>
        step.Action.IsDisplayOnly() || (step.Action == StepAction.Condition && holds(step) is null);

    /// <summary>
    /// The condition steps at the marker whose conditions hold now: done, one after another, each
    /// counting toward the phase played through (a condition step at the end finishes it).
    /// </summary>
    private void PassConditions(List<PlayedStep> steps, Func<RotationStep, bool?> holds)
    {
        for (int at = Array.IndexOf(_done, false); at >= 0 && steps[at].Step.Action == StepAction.Condition && holds(steps[at].Step) == true; at = Array.IndexOf(_done, false))
        {
            _done[at] = true;
            CountIfPlayedThrough(steps);
        }
    }

    private void NewRound()
    {
        Array.Clear(_done);
        _counted = false;
    }

    /// <summary>
    /// Every step of this round that can be seen used is done, condition steps included: the phase was
    /// played through once more. At least one cast must be among them: a phase of nothing but condition
    /// steps (or attacks) never counts.
    /// </summary>
    private void CountIfPlayedThrough(List<PlayedStep> steps)
    {
        if (_counted) return;
        bool seen = false;
        for (int i = 0; i < steps.Count; i++)
        {
            if (steps[i].Step.Action.IsDisplayOnly()) continue;
            if (!_done[i]) return;
            if (steps[i].Step.Action != StepAction.Condition) seen = true;
        }
        if (!seen) return;
        _timesPlayed[_phase]++;
        _counted = true;
        _playedThisShowing = true;
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
        // A basic or heavy attack's availability can't be seen, and there's never anything to wait for; a
        // condition step has no ability of its own, only its conditions.
        bool? own = step.Action.IsDisplayOnly() ? true : RotationTracker.AbilityAvailable(step, now, slots);
        var checks = new List<ConditionCheck> { new(CheckKind.OwnAbility, null, own) };
        foreach (var condition in step.Expect)
            checks.Add(new ConditionCheck(CheckKind.Expected, condition, RotationTracker.Holds(condition, now, slots, history)));
        bool ready = own == true && checks.All(c => c.Holds != false);
        return new StepPosition(phase, marks, ready, checks);
    }
}
