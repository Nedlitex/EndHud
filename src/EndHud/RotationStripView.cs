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
    /// <summary>How many of the next phase's steps the strip shows after the current phase.</summary>
    public const int NextStepsShown = 2;

    /// <summary>What to draw for a tracker position: its phase (see <see cref="ForPhase"/>), with the steps marked when <paramref name="steps"/> says where the player is.</summary>
    /// <param name="steps">Where the player is inside the phase (<see cref="StepFollower"/>); null: every step alike.</param>
    public static StripView Build(Rotation rotation, RotationPosition position, PartyState? state, OperatorLibrary? library,
        Func<OperatorInfo, StepAction, BitmapSource?> skillIcon, Func<OperatorInfo, BitmapSource?> avatar, StepPosition? steps = null)
    {
        var view = ForPhase(rotation, position.Step.PhaseIndex, library, skillIcon, avatar, steps);
        if (steps is not { Ready: false } || steps.Phase != position.Step.PhaseIndex) return view;
        int current = steps.Marks.ToList().IndexOf(StepMark.Current);
        if (current < 0) return view;
        var step = rotation.Played().Where(p => p.PhaseIndex == steps.Phase).ElementAt(current).Step;
        var reasons = steps.Checks.Where(c => c.Holds == false).Select(c => Reason(c, step, state, library)).OfType<string>().ToList();
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
    public static StripView ForPhase(Rotation rotation, int phase, OperatorLibrary? library,
        Func<OperatorInfo, StepAction, BitmapSource?> skillIcon, Func<OperatorInfo, BitmapSource?> avatar, StepPosition? steps = null)
    {
        var played = rotation.Played();
        var current = played.Where(p => p.PhaseIndex == phase).ToList();
        // Marks for another phase (or a different count of steps) aren't this phase's: drawn alike.
        if (steps is not null && (steps.Phase != phase || steps.Marks.Count != current.Count)) steps = null;
        StripNext? next = null;
        if (rotation.NextInFlow(phase) is { } n)
        {
            var upcoming = played.Where(p => p.PhaseIndex == n).Take(NextStepsShown).ToList();
            // The first of them starts a new phase: never drawn overlapping the step before.
            next = new StripNext(upcoming[0].PhaseLabel, upcoming.Select((p, i) => Step(p, library, skillIcon, avatar, i > 0, StripStepState.Upcoming)).ToList());
        }
        return new StripView(rotation.PhaseLabel(phase),
            current.Select((p, i) => Step(p, library, skillIcon, avatar, i > 0, State(steps, i))).ToList(), next, []);
    }

    /// <summary>How the <paramref name="index"/>th step of the phase is drawn.</summary>
    private static StripStepState State(StepPosition? steps, int index) => steps is null ? StripStepState.Upcoming : steps.Marks[index] switch
    {
        StepMark.Done => StripStepState.Done,
        StepMark.Current => steps.Ready ? StripStepState.Ready : StripStepState.Waiting,
        _ => StripStepState.Upcoming,
    };

    private static StripStep Step(PlayedStep p, OperatorLibrary? library,
        Func<OperatorInfo, StepAction, BitmapSource?> skillIcon, Func<OperatorInfo, BitmapSource?> avatar, bool mayOverlap, StripStepState state)
    {
        var op = library?.Find(p.Step.OperatorId);
        // A basic attack has no skill icon: the operator's face fills the circle instead, and no small face in the corner.
        bool basic = p.Step.Action == StepAction.BasicAttack;
        var icon = op is null ? null : basic ? avatar(op) : skillIcon(op, p.Step.Action);
        return new StripStep(icon, op is null || basic ? null : avatar(op),
            p.Step.Label ?? Loc.Get($"Rotation.Action.{p.Step.Action}"), p.Step.Note, state, mayOverlap && p.Step.WithPrevious);
    }

    /// <summary>Why the current step isn't ready, in words.</summary>
    public static string? Reason(ConditionCheck check, RotationStep step, PartyState? state, OperatorLibrary? library)
    {
        string Name(string id) => library?.Find(id)?.NameZh ?? id;
        if (check.Kind == CheckKind.OwnAbility)
        {
            return step.Action switch
            {
                StepAction.BattleSkill => state?.SkillPoints is < 1 ? Loc.Format("Rotation.Reason.SkillPoints", Points(state.SkillPoints), 1)
                    : Loc.Format("Rotation.Reason.Cooling", Name(step.OperatorId), Loc.Get("Rotation.Action.BattleSkill")),
                StepAction.Combo => Loc.Format("Rotation.Reason.Cooling", Name(step.OperatorId), Loc.Get("Rotation.Action.Combo")),
                StepAction.BasicAttack => null, // display-only: nothing to wait for
                _ => Loc.Format("Rotation.Reason.UltimateCharging", Name(step.OperatorId)),
            };
        }
        return check.Condition switch
        {
            SkillPointsCondition c => Loc.Format("Rotation.Reason.SkillPoints", Points(state?.SkillPoints), $"{c.Value:0.#}"),
            ComboCondition c => Loc.Format(c.Ready ? "Rotation.Reason.NeedComboReady" : "Rotation.Reason.NeedComboCooling", Name(c.OperatorId)),
            UltimateCondition c => Loc.Format(c.Ready ? "Rotation.Reason.NeedUltimateReady" : "Rotation.Reason.NeedUltimateUsed", Name(c.OperatorId)),
            UltimateActiveCondition c => Loc.Format(c.Active ? "Rotation.Reason.NeedUltimateActive" : "Rotation.Reason.NeedUltimateOver", Name(c.OperatorId)),
            CounterCondition c => Loc.Format("Rotation.Reason.Counter", Name(c.OperatorId),
                OperatorCounters.Definitions.FirstOrDefault(d => d.Id == c.CounterId)?.NameZh ?? c.CounterId,
                c.Is switch { Comparison.AtLeast => "≥", Comparison.AtMost => "≤", _ => "=" } + c.Value,
                state?.Counters.FirstOrDefault(r => r.CounterId == c.CounterId)?.Value?.ToString() ?? "?"),
            BossStatusCondition c => Loc.Format(c.Present ? "Rotation.Reason.BossLacks" : "Rotation.Reason.BossHas",
                StatusNames.Zh(c.StatusId) ?? c.StatusId),
            ControllingCondition c => Loc.Format("Rotation.Reason.Controlling", Name(c.OperatorId)),
            // An unmet "all": its parts that don't hold. An unmet "any": every part, as alternatives.
            // No phase history here: a "phase shown" part stays unknown, so it isn't listed as unmet.
            AllCondition all => JoinReasons(all.Of.Where(part => state is null || RotationTracker.Evaluate(part, state, PhaseHistory.None) == false), "　", step, state, library),
            AnyCondition any => JoinReasons(any.Of, Loc.Get("Rotation.Reason.Or"), step, state, library) is { } alternatives ? Loc.Format("Rotation.Reason.AnyOf", alternatives) : null,
            NotCondition not => Reason(new ConditionCheck(CheckKind.Expected, not.Of, false), step, state, library) is { } inner ? Loc.Format("Rotation.Reason.Not", inner) : null,
            _ => null,
        };
    }

    /// <summary>The reasons for these conditions, as if each were unmet, joined.</summary>
    private static string? JoinReasons(IEnumerable<StateCondition> parts, string separator, RotationStep step, PartyState? state, OperatorLibrary? library)
    {
        var texts = parts.Select(part => Reason(new ConditionCheck(CheckKind.Expected, part, false), step, state, library)).Where(t => t is not null).ToList();
        return texts.Count == 0 ? null : string.Join(separator, texts);
    }

    /// <summary>
    /// Skill points as the reason shows them: rounded down, so 0.96 of the 1 needed reads "0.9/1",
    /// never a "1/1" that looks enough.
    /// </summary>
    internal static string Points(double? points) =>
        points is { } p ? (Math.Floor(p * 10 + 1e-9) / 10).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) : "?";
}
