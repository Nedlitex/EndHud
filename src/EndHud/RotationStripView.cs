using System.Windows.Media.Imaging;
using EndHud.Vision;
using EndHud.Vision.Operators;
using EndHud.Vision.Rotations;
using Rotation = EndHud.Vision.Rotations.Rotation;

namespace EndHud;

/// <summary>
/// What the rotation strip shows for a tracker position: the phase it's in. Every step looks the
/// same: EndHud can't see which button the player pressed, so it doesn't mark a current step (no
/// chevrons, nothing greyed out as done, no reasons why "this" step isn't ready).
/// </summary>
internal static class RotationStripView
{
    /// <summary>What to draw: the current phase's steps, then the first two of the phase the flow plays next.</summary>
    public static StripView Build(Rotation rotation, RotationPosition position, PartyState? state, OperatorLibrary? library,
        Func<OperatorInfo, StepAction, BitmapSource?> skillIcon, Func<OperatorInfo, BitmapSource?> avatar)
    {
        var played = rotation.Played();
        int phase = position.Step.PhaseIndex;
        var shown = played.Where(p => p.PhaseIndex == phase).ToList();
        if (rotation.NextInFlow(phase) is { } next) shown.AddRange(played.Where(p => p.PhaseIndex == next).Take(2));

        var steps = new List<StripStep>();
        foreach (var p in shown)
        {
            var op = library?.Find(p.Step.OperatorId);
            steps.Add(new StripStep(op is null ? null : skillIcon(op, p.Step.Action), op is null ? null : avatar(op),
                p.Step.Label ?? Loc.Get($"Rotation.Action.{p.Step.Action}"), p.Step.Note, StripStepState.Upcoming, p.Step.WithPrevious));
        }
        return new StripView(position.Step.PhaseLabel, steps, []);
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
                _ => Loc.Format("Rotation.Reason.UltimateCharging", Name(step.OperatorId)),
            };
        }
        return check.Condition switch
        {
            SkillPointsCondition c => Loc.Format("Rotation.Reason.SkillPoints", Points(state?.SkillPoints), $"{c.Value:0.#}"),
            ComboCondition c => Loc.Format(c.Ready ? "Rotation.Reason.NeedComboReady" : "Rotation.Reason.NeedComboCooling", Name(c.OperatorId)),
            UltimateCondition c => Loc.Format(c.Ready ? "Rotation.Reason.NeedUltimateReady" : "Rotation.Reason.NeedUltimateUsed", Name(c.OperatorId)),
            CounterCondition c => Loc.Format("Rotation.Reason.Counter", Name(c.OperatorId),
                OperatorCounters.Definitions.FirstOrDefault(d => d.Id == c.CounterId)?.NameZh ?? c.CounterId,
                c.Is switch { Comparison.AtLeast => "≥", Comparison.AtMost => "≤", _ => "=" } + c.Value,
                state?.Counters.FirstOrDefault(r => r.CounterId == c.CounterId)?.Value?.ToString() ?? "?"),
            BossStatusCondition c => Loc.Format(c.Present ? "Rotation.Reason.BossLacks" : "Rotation.Reason.BossHas",
                StatusNames.Zh(c.StatusId) ?? c.StatusId),
            ControllingCondition c => Loc.Format("Rotation.Reason.Controlling", Name(c.OperatorId)),
            // An unmet "all": its parts that don't hold. An unmet "any": every part, as alternatives.
            AllCondition all => JoinReasons(all.Of.Where(part => state is null || RotationTracker.Evaluate(part, state) == false), "　", step, state, library),
            AnyCondition any => JoinReasons(any.Of, Loc.Get("Rotation.Reason.Or"), step, state, library) is { } alternatives ? Loc.Format("Rotation.Reason.AnyOf", alternatives) : null,
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
