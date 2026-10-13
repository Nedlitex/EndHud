using EndHud.Vision.Rotations;

namespace EndHud;

/// <summary>A rotation being edited in the editor window: everything changeable in place.</summary>
internal sealed class RotationDraft
{
    public string Id { get; set; } = RotationStore.NewId();
    public string Name { get; set; } = "";

    /// <summary>The four operators, by id; null where none is picked yet.</summary>
    public string?[] Squad { get; } = new string?[4];

    public List<PhaseDraft> Phases { get; } = [];
    public List<PhaseUseDraft> Sequence { get; } = [];

    public static RotationDraft New(string name, IReadOnlyList<string?>? squad = null)
    {
        var draft = new RotationDraft { Name = name };
        for (int i = 0; i < 4 && squad is not null && i < squad.Count; i++) draft.Squad[i] = squad[i];
        return draft;
    }

    public static RotationDraft From(Rotation rotation)
    {
        var draft = new RotationDraft { Id = rotation.Id, Name = rotation.Name };
        for (int i = 0; i < 4 && i < rotation.Squad.Count; i++) draft.Squad[i] = rotation.Squad[i];
        foreach (var phase in rotation.Phases)
        {
            draft.Phases.Add(new PhaseDraft
            {
                Id = phase.Id,
                Name = phase.Name,
                Steps = phase.Steps.Select(s => new StepDraft
                {
                    OperatorId = s.OperatorId,
                    Action = s.Action,
                    Label = s.Label,
                    Note = s.Note,
                    WithPrevious = s.WithPrevious,
                    Expect = [.. s.Expect],
                }).ToList(),
            });
        }
        foreach (var use in rotation.Sequence) draft.Sequence.Add(new PhaseUseDraft { PhaseId = use.PhaseId, Times = use.Times });
        return draft;
    }

    public Rotation ToRotation() => new(
        Rotation.CurrentFormat,
        Id,
        Name.Trim(),
        Squad.Where(id => id is not null).Select(id => id!).ToList(),
        Phases.Select(p => new RotationPhase(p.Id, p.Name.Trim(), p.Steps.Select(s => new RotationStep(
            s.OperatorId, s.Action, [.. s.Expect], Blank(s.Note), Blank(s.Label), s.WithPrevious)).ToList())).ToList(),
        Sequence.Select(u => new PhaseUse(u.PhaseId, u.Times)).ToList());

    /// <summary>What's wrong, as sentences for the player (empty when it can be saved).</summary>
    public IReadOnlyList<string> Problems()
    {
        var messages = new List<string>();
        if (string.IsNullOrWhiteSpace(Name)) messages.Add(Loc.Get("Rotation.Problem.NoName"));
        foreach (var problem in ToRotation().Problems())
        {
            string? message = problem.Kind switch
            {
                RotationProblemKind.SquadNotFourOperators => Loc.Get("Rotation.Problem.Squad"),
                RotationProblemKind.NoPhasesInSequence => Loc.Get("Rotation.Problem.NoPhases"),
                RotationProblemKind.UnknownPhase => Loc.Get("Rotation.Problem.UnknownPhase"),
                RotationProblemKind.TimesOutOfRange => Loc.Format("Rotation.Problem.Times", problem.Phase ?? ""),
                RotationProblemKind.PhaseHasNoSteps => Loc.Format("Rotation.Problem.NoSteps", problem.Phase ?? ""),
                RotationProblemKind.StepOperatorNotInSquad => Loc.Format("Rotation.Problem.StepOperator", problem.Phase ?? "", problem.Step ?? 0),
                RotationProblemKind.ConditionOperatorNotInSquad => Loc.Format("Rotation.Problem.ConditionOperator", problem.Phase ?? "", problem.Step ?? 0),
                _ => null,
            };
            if (message is not null && !messages.Contains(message)) messages.Add(message);
        }
        return messages;
    }

    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}

internal sealed class PhaseDraft
{
    public string Id { get; set; } = RotationStore.NewId();
    public string Name { get; set; } = "";
    public List<StepDraft> Steps { get; set; } = [];
}

internal sealed class PhaseUseDraft
{
    public string PhaseId { get; set; } = "";
    public int Times { get; set; } = 1;
}

internal sealed class StepDraft
{
    public string OperatorId { get; set; } = "";
    public StepAction Action { get; set; } = StepAction.BattleSkill;
    public string? Label { get; set; }
    public string? Note { get; set; }
    public bool WithPrevious { get; set; }
    public List<StateCondition> Expect { get; set; } = [];
}
