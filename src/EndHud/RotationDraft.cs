using EndHud.Vision.Rotations;

namespace EndHud;

/// <summary>A rotation being edited in the editor window: everything changeable in place.</summary>
internal sealed class RotationDraft
{
    public string Id { get; set; } = RotationStore.NewId();
    public string Name { get; set; } = "";

    /// <summary>The four operators, by id; null where none is picked yet.</summary>
    public string?[] Squad { get; } = new string?[4];

    /// <summary>In order: phases without an entry condition are played one after another, looping.</summary>
    public List<PhaseDraft> Phases { get; } = [];

    /// <summary>When several phases' conditions hold, show the last of them (see <see cref="Rotation.LastMatchWins"/>).</summary>
    public bool LastMatchWins { get; set; }

    public static RotationDraft New(string name, IReadOnlyList<string?>? squad = null)
    {
        var draft = new RotationDraft { Name = name };
        for (int i = 0; i < 4 && squad is not null && i < squad.Count; i++) draft.Squad[i] = squad[i];
        return draft;
    }

    public static RotationDraft From(Rotation rotation)
    {
        var draft = new RotationDraft { Id = rotation.Id, Name = rotation.Name, LastMatchWins = rotation.LastMatchWins };
        for (int i = 0; i < 4 && i < rotation.Squad.Count; i++) draft.Squad[i] = rotation.Squad[i];
        foreach (var phase in rotation.Phases)
        {
            draft.Phases.Add(new PhaseDraft
            {
                Id = phase.Id,
                Name = phase.Name,
                When = [.. phase.When],
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
        return draft;
    }

    /// <summary>The rotation, in the lowest format that holds it (so older EndHud can still read one that uses nothing new).</summary>
    public Rotation ToRotation()
    {
        var phases = Phases.Select(p => new RotationPhase(p.Id, p.Name.Trim(), p.Steps.Select(s => new RotationStep(
            s.OperatorId, s.Action, [.. s.Expect], Blank(s.Note), Blank(s.Label), s.WithPrevious)).ToList(), [.. p.When])).ToList();
        return new Rotation(Rotation.FormatNeeded(phases, LastMatchWins), Id, Name.Trim(),
            Squad.Where(id => id is not null).Select(id => id!).ToList(), phases, LastMatchWins);
    }

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
                RotationProblemKind.NoPhases => Loc.Get("Rotation.Problem.NoPhases"),
                RotationProblemKind.PhaseHasNoSteps => Loc.Format("Rotation.Problem.NoSteps", problem.Phase ?? ""),
                RotationProblemKind.FirstStepWithPrevious => Loc.Format("Rotation.Problem.FirstStepWithPrevious", problem.Phase ?? ""),
                RotationProblemKind.StepOperatorNotInSquad => Loc.Format("Rotation.Problem.StepOperator", problem.Phase ?? "", problem.Step ?? 0),
                RotationProblemKind.ConditionOperatorNotInSquad when problem.Step is null => Loc.Format("Rotation.Problem.GateOperator", problem.Phase ?? ""),
                RotationProblemKind.ConditionOperatorNotInSquad => Loc.Format("Rotation.Problem.ConditionOperator", problem.Phase ?? "", problem.Step ?? 0),
                RotationProblemKind.EmptyConditionGroup when problem.Step is null => Loc.Format("Rotation.Problem.EmptyGateGroup", problem.Phase ?? ""),
                RotationProblemKind.EmptyConditionGroup => Loc.Format("Rotation.Problem.EmptyGroup", problem.Phase ?? "", problem.Step ?? 0),
                RotationProblemKind.PhaseShownOwnGate => Loc.Format("Rotation.Problem.PhaseShownOwnGate", problem.Phase ?? ""),
                RotationProblemKind.PhaseShownMissingPhase => Loc.Format("Rotation.Problem.PhaseShownMissingPhase", problem.Phase ?? ""),
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

    /// <summary>Entry condition (all must hold); empty: part of the order.</summary>
    public List<StateCondition> When { get; set; } = [];

    /// <summary>A copy under a new id: its own steps (changing one doesn't change the original), the same entry condition.</summary>
    public PhaseDraft Copy(string name) => new()
    {
        Name = name,
        When = [.. When],
        Steps = Steps.Select(s => new StepDraft
        {
            OperatorId = s.OperatorId,
            Action = s.Action,
            Label = s.Label,
            Note = s.Note,
            WithPrevious = s.WithPrevious,
            Expect = [.. s.Expect],
        }).ToList(),
    };
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
