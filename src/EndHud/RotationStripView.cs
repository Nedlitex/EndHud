using System.Windows.Media.Imaging;
using EndHud.Vision;
using EndHud.Vision.Operators;
using EndHud.Vision.Rotations;
using Rotation = EndHud.Vision.Rotations.Rotation;

namespace EndHud;

/// <summary>
/// What the rotation strip shows for a tracker position: the phase it's in. Every step looks the
/// same unless the player has turned on reading their battle-skill keys; then done steps fade and
/// the current one has chevrons (cyan: use it now; white: not yet, with why under the strip).
/// </summary>
internal static class RotationStripView
{
    /// <summary>
    /// What decides whether the strip must be drawn again: the same key, the same picture. A condition
    /// step's "holds" is in it, or its glow would never follow the game.
    /// </summary>
    internal static string? Key(StripView? view)
    {
        if (view is null) return null;
        static string Keys(IEnumerable<StripStep> steps) =>
            string.Join(",", steps.Select(s => $"{s.Action}{s.State}{s.Icon is not null}{s.Avatar is not null}{s.Note}{s.Condition?.Holds}{s.Condition?.SkillPoints}"));
        return $"{view.Phase}|{Keys(view.Steps)}|{view.Next?.Phase}|{Keys(view.Next?.Steps ?? [])}|{string.Join(",", view.Reasons)}";
    }

    /// <summary>How many of the next phase's steps the strip shows after the current phase.</summary>
    public const int NextStepsShown = 2;

    /// <summary>What to draw for a tracker position: its phase (see <see cref="ForPhase"/>), with the steps marked when <paramref name="steps"/> says where the player is.</summary>
    /// <param name="steps">Where the player is inside the phase (<see cref="StepFollower"/>); null: every step alike.</param>
    /// <param name="history">The phases shown and played this fight (the phase selector), for the reasons' current counts.</param>
    public static StripView Build(Rotation rotation, RotationPosition position, PartyState? state, OperatorLibrary? library,
        Func<OperatorInfo, StepAction, BitmapSource?> skillIcon, Func<OperatorInfo, BitmapSource?> avatar, StepPosition? steps = null, IPhaseHistory? history = null,
        Func<string, BitmapSource?>? statusIcon = null)
    {
        // A condition step glows while its conditions hold, keys read or not (owner, 2026-10-24).
        bool? Holds(RotationStep step) => state is null ? null : RotationTracker.Evaluate(new AllCondition(step.Expect), state, history ?? PhaseHistory.None);
        // An attack by whoever is controlled shows them: the operator under the chevron in this reading.
        var controlled = state?.ActiveSlot is int active && active >= 1 && active <= state.Slots.Count && state.Slots[active - 1].Operator?.Operator.Id is { } id
            ? library?.Find(id) : null;
        var view = ForPhase(rotation, position.Step.PhaseIndex, library, skillIcon, avatar, steps, statusIcon, Holds, controlled);
        if (steps is not { Ready: false } || steps.Phase != position.Step.PhaseIndex) return view;
        int current = steps.Marks.ToList().IndexOf(StepMark.Current);
        if (current < 0) return view;
        var step = rotation.Played().Where(p => p.PhaseIndex == steps.Phase).ElementAt(current).Step;
        var reasons = steps.Checks.Where(c => c.Holds == false).Select(c => Reason(c, step, state, library, history, PhaseName(rotation))).OfType<string>().ToList();
        return view with { Reasons = reasons };
    }

    /// <summary>
    /// What to drag and size while the menu is open, out of battle: the first phase of the rotation
    /// for the last squad seen, so it's the real size; a generic strip if there's none.
    /// </summary>
    public static StripView Sample(Rotation? rotation, OperatorLibrary? library,
        Func<OperatorInfo, StepAction, BitmapSource?> skillIcon, Func<OperatorInfo, BitmapSource?> avatar)
    {
        if (rotation?.Played() is [var first, ..]) return ForPhase(rotation, first.PhaseIndex, library, skillIcon, avatar);
        StripStep Step(StepAction action) =>
            new(null, null, Loc.Get($"Rotation.Action.{action}"), null, StripStepState.Upcoming, false);
        return new StripView(Loc.Get("Rotation.Sample"), [Step(StepAction.BattleSkill), Step(StepAction.Combo), Step(StepAction.Ultimate)], null, []);
    }

    /// <summary>
    /// A phase's steps, then the first two of the phase the flow plays next, kept apart so the strip
    /// can mark where this phase ends. The editor's preview uses this too, so it shows what the game
    /// will.
    /// </summary>
    /// <param name="steps">Where the player is inside this phase; null (the editor's preview, or no keys read): every step alike.</param>
    /// <param name="statusIcon">A boss status's icon, for a condition step about one.</param>
    /// <param name="holds">Whether a condition step's conditions hold now (null: not known, e.g. the editor's preview: drawn dim).</param>
    /// <param name="controlled">The operator controlled now, for an attack by whoever is controlled; null when not known (the editor's preview).</param>
    public static StripView ForPhase(Rotation rotation, int phase, OperatorLibrary? library,
        Func<OperatorInfo, StepAction, BitmapSource?> skillIcon, Func<OperatorInfo, BitmapSource?> avatar, StepPosition? steps = null,
        Func<string, BitmapSource?>? statusIcon = null, Func<RotationStep, bool?>? holds = null, OperatorInfo? controlled = null)
    {
        var look = new Look(library, skillIcon, avatar, statusIcon ?? (_ => null), PhaseName(rotation), controlled);
        var played = rotation.Played();
        var current = played.Where(p => p.PhaseIndex == phase).ToList();
        // Marks for another phase (or a different count of steps) aren't this phase's: drawn alike.
        if (steps is not null && (steps.Phase != phase || steps.Marks.Count != current.Count)) steps = null;
        StripNext? next = null;
        if (rotation.NextInFlow(phase) is { } n)
        {
            var upcoming = played.Where(p => p.PhaseIndex == n).Take(NextStepsShown).ToList();
            // The first of them starts a new phase: never drawn overlapping the step before.
            // The next phase's preview: its conditions aren't asked (null: dim).
            next = new StripNext(upcoming[0].PhaseLabel, upcoming.Select((p, i) => Step(p, look, i > 0, StripStepState.Upcoming, null)).ToList());
        }
        return new StripView(rotation.PhaseLabel(phase),
            current.Select((p, i) => Step(p, look, i > 0, State(steps, i), holds?.Invoke(p.Step))).ToList(), next, []);
    }

    /// <summary>Where a step's pictures and words come from.</summary>
    private sealed record Look(OperatorLibrary? Library, Func<OperatorInfo, StepAction, BitmapSource?> SkillIcon, Func<OperatorInfo, BitmapSource?> Avatar,
        Func<string, BitmapSource?> StatusIcon, Func<string, string?> PhaseName, OperatorInfo? Controlled);

    /// <summary>How the <paramref name="index"/>th step of the phase is drawn.</summary>
    private static StripStepState State(StepPosition? steps, int index) => steps is null ? StripStepState.Upcoming : steps.Marks[index] switch
    {
        StepMark.Done => StripStepState.Done,
        StepMark.Skipped => StripStepState.Skipped,
        StepMark.Current => steps.Ready ? StripStepState.Ready : StripStepState.Waiting,
        _ => StripStepState.Upcoming,
    };

    private static StripStep Step(PlayedStep p, Look look, bool mayOverlap, StripStepState state, bool? holds)
    {
        var (library, skillIcon, avatar) = (look.Library, look.SkillIcon, look.Avatar);
        if (p.Step.Action == StepAction.Condition)
        {
            // The first condition's icon, the alert pop-up's rule (ConditionIcon); the conditions in words under it.
            var subject = ConditionIcon.Subject(p.Step.Expect);
            return new StripStep(ConditionIcon.Picture(subject, library, look.StatusIcon, avatar), null,
                p.Step.Label ?? Loc.Get("Rotation.Action.Condition"),
                p.Step.Note ?? ConditionWords.Statements(p.Step.Expect, library, look.PhaseName),
                state, false, new StripCondition(holds, subject.Kind == AlertSubjectKind.SkillPoints));
        }
        // "Whoever is controlled": the one controlled now; nobody known, no face (the label says what it is).
        var op = p.Step.OperatorId == RotationStep.Controlled ? look.Controlled : library?.Find(p.Step.OperatorId);
        // A basic, heavy or plunge attack has no skill icon: the operator's face fills the circle instead, and no small face in the corner.
        bool basic = p.Step.Action.IsDisplayOnly();
        var icon = op is null ? null : basic ? avatar(op) : skillIcon(op, p.Step.Action);
        return new StripStep(icon, op is null || basic ? null : avatar(op),
            p.Step.Label ?? Loc.Get($"Rotation.Action.{p.Step.Action}"), p.Step.Note, state, mayOverlap && p.Step.WithPrevious);
    }

    /// <summary>A rotation's phases by the strip's names ("充能轴 II"), by id; null for one it doesn't have.</summary>
    internal static Func<string, string?> PhaseName(Rotation rotation) => id =>
        rotation.Phases.Select((p, i) => (p.Id, i)).Where(p => p.Id == id).Select(p => rotation.PhaseLabel(p.i)).FirstOrDefault();

    /// <summary>
    /// Why the current step isn't ready, in words. Its own ability: skill points short, cooling,
    /// charging. A condition: what it needs, in the same words as everywhere else
    /// (<see cref="ConditionWords.Need"/>: "需要：寒冷附着 ≥ 3 层（当前 1）"); an unmet "all" gives the
    /// needs of its parts that don't hold.
    /// </summary>
    /// <param name="history">The phases shown and played this fight, for "phase shown/played" (their current count, and an "all" telling its unmet parts).</param>
    /// <param name="phaseName">The phases by the strip's names; null: ids as they are.</param>
    public static string? Reason(ConditionCheck check, RotationStep step, PartyState? state, OperatorLibrary? library, IPhaseHistory? history = null, Func<string, string?>? phaseName = null)
    {
        string Name(string id) => library?.Find(id)?.NameZh ?? id;
        if (check.Kind == CheckKind.OwnAbility)
        {
            return step.Action switch
            {
                StepAction.BattleSkill => state?.SkillPoints is < 1 ? Loc.Format("Rotation.Reason.SkillPoints", Points(state.SkillPoints), 1)
                    : Loc.Format("Rotation.Reason.Cooling", Name(step.OperatorId), Loc.Get("Rotation.Action.BattleSkill")),
                StepAction.Combo => Loc.Format("Rotation.Reason.Cooling", Name(step.OperatorId), Loc.Get("Rotation.Action.Combo")),
                StepAction.Ultimate => Loc.Format("Rotation.Reason.UltimateCharging", Name(step.OperatorId)),
                _ => null, // display-only: nothing to wait for
            };
        }
        if (check.Condition is not { } condition) return null;
        phaseName ??= id => id;
        // An unmet "all": the needs of its parts that don't hold (a part that can't be told isn't listed).
        if (condition is AllCondition all)
        {
            var unmet = all.Of.Where(part => state is null || RotationTracker.Evaluate(part, state, history ?? PhaseHistory.None) == false)
                .Select(part => Reason(new ConditionCheck(CheckKind.Expected, part, false), step, state, library, history, phaseName)).OfType<string>().ToList();
            return unmet.Count == 0 ? null : string.Join("　", unmet);
        }
        return ConditionWords.Need(condition, state, history, library, phaseName);
    }

    /// <summary>
    /// Skill points as the reason shows them: rounded down, so 0.96 of the 1 needed reads "0.9/1",
    /// never a "1/1" that looks enough.
    /// </summary>
    internal static string Points(double? points) =>
        points is { } p ? (Math.Floor(p * 10 + 1e-9) / 10).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) : "?";
}
