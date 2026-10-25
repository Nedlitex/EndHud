namespace EndHud.Vision.Rotations;

/// <summary>How the strip draws a step of the phase it shows.</summary>
public enum StepMark
{
    /// <summary>Seen used this round of the phase (a condition step: its conditions were met): lit.</summary>
    Lit,

    /// <summary>The step to use next, the first unlit one in written order (several, for steps used together): drawn with chevrons.</summary>
    Current,

    /// <summary>Not seen used yet this round.</summary>
    Unlit,

    /// <summary>
    /// A step EndHud can never see used (a basic, heavy or plunge attack): drawn in its place, neither lit nor
    /// waited on, and never counted for finishing the phase.
    /// </summary>
    Neutral,
}

/// <summary>Where the player is inside the phase on the strip.</summary>
/// <param name="Phase">The phase (index into <see cref="Rotation.Phases"/>).</param>
/// <param name="Marks">Each step of the phase, in order.</param>
/// <param name="Ready">The first current step can be used now (its own ability is available and its expected state holds).</param>
/// <param name="Checks">That step's checks, for the reasons under the strip.</param>
public sealed record StepPosition(int Phase, IReadOnlyList<StepMark> Marks, bool Ready, IReadOnlyList<ConditionCheck> Checks);

/// <summary>
/// The steps of the phase on the strip, lit up as they are seen used (owner, 2026-10-24: "skip over is bad ... a
/// rotation is fixed order but in reality it can be played in different orders ... define a finish condition for the
/// rotation similar to entry condition"). The phase comes from <see cref="PhaseSelector"/> (the authority); this only
/// follows inside it, and every light goes out when the phase changes or the fight ends. It runs whenever the strip
/// does, from what the screen shows: battle skills cast (<see cref="ScreenCastJudge"/>: skill points spent and the
/// flash on a button, the player's presses as an assist), the combo bar emptying for a combo, the ultimate going from
/// ready to spent.
/// <list type="bullet">
/// <item><b>A use lights the first unlit step of the phase with the same operator and action</b>, wherever it is in the
/// phase: in any order, nothing is ever skipped, and a step not seen stays unlit.</item>
/// <item>Skill points spent with nobody named light the step under the chevron, if it is a battle skill whose
/// operator could cast (the button wasn't cooling).</item>
/// <item>A condition step lights the reading its conditions hold, wherever it is, and stays lit for the round; while
/// they don't hold or can't be told, it stays unlit.</item>
/// <item>Basic, heavy and plunge attacks are never seen: they are <see cref="StepMark.Neutral"/>, never lit, never
/// waited on and never counted.</item>
/// <item>The chevron is on the first unlit step in written order that isn't neutral (with the rest of its group).</item>
/// </list>
/// <para><b>Finishing.</b> The round is over when the phase's finish condition comes true
/// (<see cref="RotationPhase.Finish"/>; any condition, and "the phase's steps lit": all, at least N, these); without
/// one, when every step that isn't neutral is lit (at least one, so a phase of nothing but attacks never finishes by
/// itself). It then counts as played through once more (<see cref="TimesPlayed"/>, for "phase played";
/// <see cref="PlayedThroughThisShowing"/>, for "the current phase, played through this showing"), and every light
/// goes out for a new round. A finish condition that keeps holding counts again only after it was seen not holding;
/// one that can't be told changes nothing.</para>
/// </summary>
public sealed class StepFollower(Rotation rotation)
{
    /// <summary>A step used: by slot (0-3; null for skill points spent by nobody named).</summary>
    private readonly record struct Use(int? Slot, StepAction Action, TimeSpan At);

    private readonly IReadOnlyList<PlayedStep> _played = rotation.Played();
    private int _phase = -1;

    /// <summary>Each step of the phase this round: lit or not.</summary>
    private bool[] _lit = [];

    /// <summary>Each step of the phase: lights up (not a basic, heavy or plunge attack).</summary>
    private bool[] _countable = [];

    /// <summary>The finish condition held when last told this showing: it counts again only after it was seen not holding.</summary>
    private bool _finishHeld;

    /// <summary>
    /// The phase's steps this round, in written order (for "the phase's steps lit"): true lit, false not yet, null a
    /// step that never lights. Null before the first reading of a fight.
    /// </summary>
    public IReadOnlyList<bool?>? StepsLit => _phase < 0 ? null : _lit.Select((lit, i) => _countable[i] ? lit : (bool?)null).ToList();

    private readonly int[] _timesPlayed = new int[rotation.Phases.Count];

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

    /// <summary>The fight ended: the next one starts with every light out, every phase played 0 times.</summary>
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
        if (phase != _phase || _lit.Length != steps.Count)
        {
            if (phase != _phase) _playedThisShowing = false; // it came onto the strip
            _phase = phase;
            _lit = new bool[steps.Count];
            _countable = steps.Select(p => Countable(p.Step)).ToArray();
            _finishHeld = false;
        }
        var slots = RotationTracker.SlotsByOperator(now);
        LightConditions(steps, now, slots, history);
        FinishIfOver(steps, now, slots, history);
        var uses = casts.Select(c => new Use(c.Slot - 1, StepAction.BattleSkill, c.At)).ToList();
        if (before is not null) uses.AddRange(VisionUses(before, now));
        foreach (var use in uses.OrderBy(u => u.At))
        {
            Light(use, steps, slots, before);
            FinishIfOver(steps, now, slots, history);
        }
        return Position(phase, steps, now, slots, history);
    }

    /// <summary>Combos and ultimates seen used between two readings (<see cref="RotationTracker"/>'s rules).</summary>
    private static IEnumerable<Use> VisionUses(PartyState before, PartyState now)
    {
        var at = now.ReadAt ?? TimeSpan.Zero;
        for (int i = 0; i < Math.Min(before.Slots.Count, now.Slots.Count); i++)
        {
            if (RotationTracker.ComboUsed(before.Slots[i], now.Slots[i])) yield return new Use(i, StepAction.Combo, at);
            if (before.Slots[i].Ultimate?.Ready == true && now.Slots[i].Ultimate is { Ready: false }) yield return new Use(i, StepAction.Ultimate, at);
        }
    }

    /// <summary>A step that lights up and counts for finishing: a skill, or a condition step (not a basic, heavy or plunge attack).</summary>
    internal static bool Countable(RotationStep step) => !step.Action.IsDisplayOnly();

    /// <summary>The first unlit step of the phase with the use's operator and action; for points spent by nobody named, the chevron's battle skill if its operator could cast.</summary>
    private void Light(Use use, List<PlayedStep> steps, IReadOnlyDictionary<string, int> slots, PartyState? before)
    {
        if (use.Slot is not { } slot)
        {
            int at = Chevron(steps);
            if (at >= 0 && steps[at].Step is { Action: StepAction.BattleSkill } step && slots.TryGetValue(step.OperatorId, out int who)
                && before?.Slots.ElementAtOrDefault(who)?.BattleSkill != BattleSkillState.Cooling)
                _lit[at] = true;
            return;
        }
        for (int i = 0; i < steps.Count; i++)
        {
            var step = steps[i].Step;
            if (_lit[i] || step.Action != use.Action || !slots.TryGetValue(step.OperatorId, out int s) || s != slot) continue;
            _lit[i] = true;
            return;
        }
    }

    /// <summary>The condition steps whose conditions hold now light up, wherever they are; lit, they stay lit this round.</summary>
    private void LightConditions(List<PlayedStep> steps, PartyState now, IReadOnlyDictionary<string, int> slots, IPhaseHistory history)
    {
        for (int i = 0; i < steps.Count; i++)
            if (!_lit[i] && steps[i].Step.Action == StepAction.Condition && RotationTracker.Holds(new AllCondition(steps[i].Step.Expect), now, slots, history) == true)
                _lit[i] = true;
    }

    /// <summary>
    /// The phase's finish condition came true (none: every countable step lit, at least one): played through once more,
    /// and every light goes out for a new round. It counts on the reading it comes true; one that keeps holding counts
    /// again only after it was seen not holding; one that can't be told changes nothing. A finish already holding when
    /// the phase comes onto the strip counts on that reading, as an entry condition would. In the new round, the
    /// condition steps still met light again at once.
    /// </summary>
    private void FinishIfOver(List<PlayedStep> steps, PartyState now, IReadOnlyDictionary<string, int> slots, IPhaseHistory history)
    {
        var finish = rotation.Phases[_phase].Finish;
        bool? held = finish.Count == 0
            ? _countable.Contains(true) && _lit.Where((_, i) => _countable[i]).All(lit => lit)
            : RotationTracker.Holds(new AllCondition(finish), now, slots, new WithLights(history, StepsLit!));
        if (held is not { } holds) return;
        bool cameTrue = holds && !_finishHeld;
        _finishHeld = holds;
        if (!cameTrue) return;
        _timesPlayed[_phase]++;
        _playedThisShowing = true;
        Array.Clear(_lit);
        LightConditions(steps, now, slots, history);
    }

    /// <summary>The phase history, with this round's lights for "the phase's steps lit".</summary>
    private sealed class WithLights(IPhaseHistory history, IReadOnlyList<bool?> lights) : IPhaseHistory
    {
        public int? TimesShown(string phaseId) => history.TimesShown(phaseId);

        public int? TimesPlayed(string phaseId) => history.TimesPlayed(phaseId);

        public string? CurrentPhase => history.CurrentPhase;

        public bool? PlayedThroughThisShowing(string phaseId) => history.PlayedThroughThisShowing(phaseId);

        public IReadOnlyList<bool?>? StepsLit => lights;
    }

    /// <summary>The first unlit countable step in written order; -1 when there is none.</summary>
    private int Chevron(List<PlayedStep> steps)
    {
        for (int i = 0; i < steps.Count; i++)
            if (!_lit[i] && Countable(steps[i].Step)) return i;
        return -1;
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

    /// <summary>The marks; whether the chevron's step is ready, and its checks.</summary>
    private StepPosition Position(int phase, List<PlayedStep> steps, PartyState now, IReadOnlyDictionary<string, int> slots, IPhaseHistory history)
    {
        var marks = new StepMark[steps.Count];
        for (int i = 0; i < steps.Count; i++)
            marks[i] = !Countable(steps[i].Step) ? StepMark.Neutral : _lit[i] ? StepMark.Lit : StepMark.Unlit;
        int at = Chevron(steps);
        if (at < 0)
        {
            // Every step lit and the finish condition not holding: nothing to point at, so what the phase waits for
            // is its finish condition (the strip's reasons), never a silent strip.
            var finish = rotation.Phases[phase].Finish;
            var lights = new WithLights(history, StepsLit!);
            return new StepPosition(phase, marks, false,
                finish.Select(c => new ConditionCheck(CheckKind.Expected, c, RotationTracker.Holds(c, now, slots, lights))).ToList());
        }
        // The chevron's group: its unlit steps are all current (steps used together, in either order).
        for (int i = GroupStart(steps, at); i <= GroupEnd(steps, at); i++)
            if (marks[i] == StepMark.Unlit) marks[i] = StepMark.Current;

        var step = steps[at].Step;
        // A condition step has no ability of its own, only its conditions.
        bool? own = RotationTracker.AbilityAvailable(step, now, slots);
        var checks = new List<ConditionCheck> { new(CheckKind.OwnAbility, null, own) };
        foreach (var condition in step.Expect)
            checks.Add(new ConditionCheck(CheckKind.Expected, condition, RotationTracker.Holds(condition, now, slots, history)));
        bool ready = own == true && checks.All(c => c.Holds != false);
        return new StepPosition(phase, marks, ready, checks);
    }
}
