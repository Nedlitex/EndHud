using System.Windows.Media.Imaging;
using EndHud.Vision;
using EndHud.Vision.Operators;
using EndHud.Vision.Rotations;
using Rotation = EndHud.Vision.Rotations.Rotation;

namespace EndHud;

/// <summary>What the rotation strip shows for a tracker position: the steps around the current one, and why it isn't ready.</summary>
internal static class RotationStripView
{
    /// <summary>What to draw: the current phase's steps, then the next phase's first two.</summary>
    public static StripView Build(Rotation rotation, RotationPosition position, PartyState? state, OperatorLibrary? library,
        Func<OperatorInfo, StepAction, BitmapSource?> skillIcon, Func<OperatorInfo, BitmapSource?> avatar)
    {
        var played = rotation.Played();
        int current = position.Step.Index;
        var shown = played.Where(p => p.PhaseLabel == position.Step.PhaseLabel).ToList();
        int after = shown.Count > 0 ? shown[^1].Index + 1 : current + 1;
        for (int i = 0; i < 2 && played.Count > 0; i++) shown.Add(played[(after + i) % played.Count]);

        var steps = new List<StripStep>();
        foreach (var p in shown)
        {
            var op = library?.Find(p.Step.OperatorId);
            var look = p.Index == current ? (position.Ready ? StripStepState.Ready : StripStepState.Waiting)
                : p.Index < current && p.PhaseLabel == position.Step.PhaseLabel ? StripStepState.Done
                : StripStepState.Upcoming;
            steps.Add(new StripStep(op is null ? null : skillIcon(op, p.Step.Action), op is null ? null : avatar(op),
                p.Step.Label ?? Loc.Get($"Rotation.Action.{p.Step.Action}"), p.Step.Note, look, p.Step.WithPrevious));
        }
        var reasons = position.Ready ? [] : position.Checks.Where(c => c.Holds == false && c.Kind != CheckKind.PreviousStepUsed)
            .Select(c => Reason(c, position.Step.Step, state, library)).Where(r => r is not null).Select(r => r!).ToList();
        return new StripView(position.Step.PhaseLabel, steps, reasons);
    }

    /// <summary>Why the current step isn't ready, in words.</summary>
    public static string? Reason(ConditionCheck check, RotationStep step, PartyState? state, OperatorLibrary? library)
    {
        string Name(string id) => library?.Find(id)?.NameZh ?? id;
        if (check.Kind == CheckKind.OwnAbility)
        {
            return step.Action switch
            {
                StepAction.BattleSkill => state?.SkillPoints is < 1 ? Loc.Format("Rotation.Reason.SkillPoints", $"{state.SkillPoints:0.#}", 1)
                    : Loc.Format("Rotation.Reason.Cooling", Name(step.OperatorId), Loc.Get("Rotation.Action.BattleSkill")),
                StepAction.Combo => Loc.Format("Rotation.Reason.Cooling", Name(step.OperatorId), Loc.Get("Rotation.Action.Combo")),
                _ => Loc.Format("Rotation.Reason.UltimateCharging", Name(step.OperatorId)),
            };
        }
        return check.Condition switch
        {
            SkillPointsCondition c => Loc.Format("Rotation.Reason.SkillPoints", $"{state?.SkillPoints:0.#}", $"{c.Value:0.#}"),
            ComboCondition c => Loc.Format(c.Ready ? "Rotation.Reason.NeedComboReady" : "Rotation.Reason.NeedComboCooling", Name(c.OperatorId)),
            UltimateCondition c => Loc.Format(c.Ready ? "Rotation.Reason.NeedUltimateReady" : "Rotation.Reason.NeedUltimateUsed", Name(c.OperatorId)),
            CounterCondition c => Loc.Format("Rotation.Reason.Counter", Name(c.OperatorId),
                OperatorCounters.Definitions.FirstOrDefault(d => d.Id == c.CounterId)?.NameZh ?? c.CounterId,
                c.Is switch { Comparison.AtLeast => "≥", Comparison.AtMost => "≤", _ => "=" } + c.Value,
                state?.Counters.FirstOrDefault(r => r.CounterId == c.CounterId)?.Value?.ToString() ?? "?"),
            BossStatusCondition c => Loc.Format(c.Present ? "Rotation.Reason.BossLacks" : "Rotation.Reason.BossHas",
                StatusNames.Zh(c.StatusId) ?? c.StatusId),
            ControllingCondition c => Loc.Format("Rotation.Reason.Controlling", Name(c.OperatorId)),
            _ => null,
        };
    }
}
