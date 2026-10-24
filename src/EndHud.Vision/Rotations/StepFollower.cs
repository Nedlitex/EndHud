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

    /// <summary>
    /// Passed without being seen used: a later step was seen used for certain, so the player moved on past it.
    /// Drawn faded like a done step, with a slash through it, so the player sees what was missed.
    /// </summary>
    Skipped,
}

/// <summary>Where the player is inside the phase on the strip.</summary>
/// <param name="Phase">The phase (index into <see cref="Rotation.Phases"/>).</param>
/// <param name="Marks">Each step of the phase, in order.</param>
/// <param name="Ready">The first current step can be used now (its own ability is available and its expected state holds).</param>
/// <param name="Checks">That step's checks, for the reasons under the strip.</param>
public sealed record StepPosition(int Phase, IReadOnlyList<StepMark> Marks, bool Ready, IReadOnlyList<ConditionCheck> Checks);

/// <summary>
/// Which step of the phase on the strip the player is on. The phase comes from <see cref="PhaseSelector"/> (the
/// authority); this only follows inside it, and starts over when the phase changes. It runs whenever the strip
/// does, from what the screen shows: battle skills cast (<see cref="ScreenCastJudge"/>: skill points spent and the
/// flash on a button, the player's presses as an assist), the combo bar emptying for a combo, the ultimate going
/// from ready to spent.
/// <list type="number">
/// <item><b>A use in the current group</b> marks that step done (steps used together in either order). Basic, heavy
/// and plunge attacks (display-only: never seen) never hold the marker, and neither does a condition step whose
/// conditions can't be told: the next use of a later step passes them, as done.</item>
/// <item><b>A certain use elsewhere jumps</b> (owner, 2026-10-24: "if somehow we didnt track battle skill but observe
/// combo, we should jump to the combo"): a combo seen used, an ultimate seen spent, a battle skill whose skill point
/// was seen spent and whose slot is known. The nearest later step of this round with the same operator and action is
/// done, every step passed on the way is <see cref="StepMark.Skipped"/> (display-only ones done). A jump does not pass
/// a condition step seen not holding (owner): that later match isn't taken. Other uses (a cast with no point seen
/// spent) only mark the current group.</item>
/// <item>A use matching nothing later in the round but the phase's first group starts a new round there, once a step
/// of this round is done.</item>
/// <item>A use told late (the flash names a cast after a later use already jumped past its step) fills in that skipped
/// step as done.</item>
/// <item>Skill points spent with nobody named marks the current step, if it is a battle skill whose operator could
/// cast (the button wasn't cooling).</item>
/// </list>
/// A condition step is done the reading its conditions hold. A phase whose every step is done or skipped starts
/// again at its first step.
/// <para>It counts, per phase, the rounds <b>played through</b> this fight (<see cref="TimesPlayed"/>, for "phase
/// played"): a round counts once, when every step that can be seen used is done or skipped, its last cast step is
/// done, and at most one cast step was skipped (owner: one missed cast a round is allowed); or when a new round
/// starts on its first group and all but one of its cast steps were done. A phase left half way doesn't count; a
/// phase of nothing but basic or heavy attacks never does.</para>
/// </summary>
public sealed class StepFollower(Rotation rotation)
{
    /// <summary>A step used: by slot (0-3; null for skill points spent by nobody named), and whether it may jump.</summary>
    private readonly record struct Use(int? Slot, StepAction Action, bool Certain, TimeSpan At);

    private readonly IReadOnlyList<PlayedStep> _played = rotation.Played();
    private int _phase = -1;

    /// <summary>Each step this round: <see cref="StepMark.Done"/>, <see cref="StepMark.Skipped"/>, or <see cref="StepMark.Upcoming"/> (not yet).</summary>
    private StepMark[] _marks = [];

    /// <summary>For a skipped step, the time of the use that jumped past it (a use from before then fills it in).</summary>
    private TimeSpan[] _skippedAt = [];

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
    /// <param name="casts">The battle skills cast since <paramref name="before"/> (<see cref="ScreenCastJudge"/>), oldest first.</param>
    /// <param name="history">For "phase shown" conditions in the steps' expected state.</param>
    public StepPosition Update(int phase, PartyState? before, PartyState now, IReadOnlyList<SkillCast> casts, IPhaseHistory history)
    {
        var steps = _played.Where(p => p.PhaseIndex == phase).ToList();
        if (phase != _phase || _marks.Length != steps.Count)
        {
            if (phase != _phase) _playedThisShowing = false; // it came onto the strip
            _phase = phase;
            _marks = Enumerable.Repeat(StepMark.Upcoming, steps.Count).ToArray();
            _skippedAt = new TimeSpan[steps.Count];
            _counted = false;
        }
        var slots = RotationTracker.SlotsByOperator(now);
        // A condition step's conditions in this reading: true, false, or unknown (null). Null for any other step.
        bool? Holds(RotationStep step) => step.Action == StepAction.Condition
            ? RotationTracker.Holds(new AllCondition(step.Expect), now, slots, history)
            : null;
        PassConditions(steps, Holds);
        var uses = casts.Select(c => new Use(c.Slot - 1, StepAction.BattleSkill, c.Certain, c.At)).ToList();
        if (before is not null) uses.AddRange(VisionUses(before, now));
        foreach (var use in uses.OrderBy(u => u.At))
        {
            Apply(use, steps, slots, Holds, before);
            CountIfPlayedThrough(steps);
            PassConditions(steps, Holds);
        }
        if (_marks.Length > 0 && _marks.All(m => m != StepMark.Upcoming)) NewRound();
        return Position(phase, steps, now, slots, history);
    }

    /// <summary>Combos and ultimates seen used between two readings (<see cref="RotationTracker"/>'s rules): certain.</summary>
    private static IEnumerable<Use> VisionUses(PartyState before, PartyState now)
    {
        var at = now.ReadAt ?? TimeSpan.Zero;
        for (int i = 0; i < Math.Min(before.Slots.Count, now.Slots.Count); i++)
        {
            if (RotationTracker.ComboUsed(before.Slots[i], now.Slots[i])) yield return new Use(i, StepAction.Combo, true, at);
            if (before.Slots[i].Ultimate?.Ready == true && now.Slots[i].Ultimate is { Ready: false }) yield return new Use(i, StepAction.Ultimate, true, at);
        }
    }

    private void Apply(Use use, List<PlayedStep> steps, IReadOnlyDictionary<string, int> slots, Func<RotationStep, bool?> holds, PartyState? before)
    {
        bool Matches(RotationStep step) => use.Slot is { } slot
            ? step.Action == use.Action && slots.TryGetValue(step.OperatorId, out int s) && s == slot
            // Points spent with nobody named: a battle skill whose operator could cast.
            : step.Action == StepAction.BattleSkill && slots.TryGetValue(step.OperatorId, out int who)
              && before?.Slots.ElementAtOrDefault(who)?.BattleSkill != BattleSkillState.Cooling;

        // Told late: a step already jumped past by a use from after this one.
        if (use.Slot is not null)
            for (int i = 0; i < steps.Count; i++)
                if (_marks[i] == StepMark.Skipped && use.At < _skippedAt[i] && Matches(steps[i].Step))
                {
                    _marks[i] = StepMark.Done;
                    return;
                }

        int at = Array.IndexOf(_marks, StepMark.Upcoming);
        if (at < 0) return;
        // 1. The current group, past steps that never hold the marker.
        int reach = at;
        for (int start = at; start < steps.Count; start = GroupEnd(steps, start) + 1)
        {
            int end = GroupEnd(steps, start);
            for (int i = start; i <= end; i++)
            {
                if (_marks[i] != StepMark.Upcoming || !Matches(steps[i].Step)) continue;
                for (int j = at; j < start; j++) if (_marks[j] == StepMark.Upcoming) _marks[j] = StepMark.Done; // never seen, or can't be told
                _marks[i] = StepMark.Done;
                return;
            }
            reach = start;
            // A skill not yet seen used holds the marker here, and so does a condition step seen not holding.
            bool held = false;
            for (int i = start; i <= end; i++)
                if (_marks[i] == StepMark.Upcoming && !Passable(steps[i].Step, holds)) held = true;
            if (held) break;
            reach = end + 1;
        }
        // 2. A certain use: the nearest later step of this round it is, unless a condition seen not holding is in the way.
        for (int i = reach; use.Certain && i < steps.Count; i++)
        {
            var step = steps[i].Step;
            if (_marks[i] == StepMark.Upcoming && step.Action == StepAction.Condition && holds(step) == false) break;
            if (_marks[i] != StepMark.Upcoming || !Matches(step)) continue;
            for (int j = at; j < GroupStart(steps, i); j++)
            {
                if (_marks[j] != StepMark.Upcoming) continue;
                if (Passable(steps[j].Step, holds)) _marks[j] = StepMark.Done;
                else
                {
                    _marks[j] = StepMark.Skipped;
                    _skippedAt[j] = use.At;
                }
            }
            _marks[i] = StepMark.Done;
            return;
        }

        // 3. Not later in this round, but the phase's first group: the player started the phase over. An uncertain use
        // only when nothing but steps it passes was left (the round is over).
        if ((use.Certain || reach >= steps.Count) && _marks.Contains(StepMark.Done) && FirstGroupMatches(steps, Matches))
        {
            CountOnNewRound(steps);
            NewRound();
            int end = GroupEnd(steps, 0);
            for (int i = 0; i <= end; i++)
                if (Matches(steps[i].Step))
                {
                    _marks[i] = StepMark.Done;
                    return;
                }
        }
    }

    private static bool FirstGroupMatches(List<PlayedStep> steps, Func<RotationStep, bool> matches)
    {
        if (steps.Count == 0) return false;
        int end = GroupEnd(steps, 0);
        for (int i = 0; i <= end; i++)
            if (matches(steps[i].Step)) return true;
        return false;
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
        for (int at = Array.IndexOf(_marks, StepMark.Upcoming); at >= 0 && steps[at].Step.Action == StepAction.Condition && holds(steps[at].Step) == true; at = Array.IndexOf(_marks, StepMark.Upcoming))
        {
            _marks[at] = StepMark.Done;
            CountIfPlayedThrough(steps);
        }
    }

    private void NewRound()
    {
        Array.Fill(_marks, StepMark.Upcoming);
        Array.Clear(_skippedAt);
        _counted = false;
    }

    /// <summary>A cast step: one that is seen used (not a display-only attack, not a condition).</summary>
    private static bool IsCast(RotationStep step) => !step.Action.IsDisplayOnly() && step.Action != StepAction.Condition;

    /// <summary>
    /// Every step of this round that can be seen used is done or skipped, its last cast step is done, and at most one
    /// cast step was skipped: the phase was played through once more. A phase of nothing but condition steps (or
    /// attacks) never counts.
    /// </summary>
    private void CountIfPlayedThrough(List<PlayedStep> steps)
    {
        if (_counted) return;
        int last = -1, skipped = 0;
        for (int i = 0; i < steps.Count; i++)
        {
            var step = steps[i].Step;
            if (step.Action.IsDisplayOnly()) continue;
            if (_marks[i] == StepMark.Upcoming) return;
            if (!IsCast(step)) continue;
            last = i;
            if (_marks[i] == StepMark.Skipped) skipped++;
        }
        if (last < 0 || _marks[last] != StepMark.Done || skipped > 1) return;
        Count();
    }

    /// <summary>The player started the phase over: the round ending counts if all but one of its cast steps were done.</summary>
    private void CountOnNewRound(List<PlayedStep> steps)
    {
        if (_counted) return;
        int casts = 0, done = 0;
        for (int i = 0; i < steps.Count; i++)
        {
            if (!IsCast(steps[i].Step)) continue;
            casts++;
            if (_marks[i] == StepMark.Done) done++;
        }
        if (casts > 0 && done >= casts - 1 && done > 0) Count();
    }

    private void Count()
    {
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

    /// <summary>The first step of the group <paramref name="at"/> is in.</summary>
    private static int GroupStart(List<PlayedStep> steps, int at)
    {
        while (at > 0 && steps[at].Step.WithPrevious) at--;
        return at;
    }

    /// <summary>The marks; whether the first current step is ready, and its checks.</summary>
    private StepPosition Position(int phase, List<PlayedStep> steps, PartyState now, IReadOnlyDictionary<string, int> slots, IPhaseHistory history)
    {
        var marks = new StepMark[steps.Count];
        int at = Array.IndexOf(_marks, StepMark.Upcoming);
        // The current group: the one the first step not yet passed is in (it may be inside a group, after a step used out of order).
        int groupEnd = at < 0 ? -1 : GroupEnd(steps, GroupStart(steps, at));
        for (int i = 0; i < steps.Count; i++)
            marks[i] = _marks[i] != StepMark.Upcoming ? _marks[i] : i <= groupEnd ? StepMark.Current : StepMark.Upcoming;
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
