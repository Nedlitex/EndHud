using EndHud.Vision;
using EndHud.Vision.Operators;
using EndHud.Vision.Rotations;

namespace EndHud;

/// <summary>Conditions in words: one statement per kind, and the "need" form built from it.</summary>
internal static class ConditionWords
{
    public static string Statement(StateCondition condition, OperatorLibrary? library, Func<string, string?> phaseName, Func<string, string>? text = null) =>
        AlertText.Describe([condition], library, phaseName, text);

    public static string? Current(StateCondition condition, PartyState? state, IPhaseHistory? history) => null;

    public static string? Need(StateCondition condition, PartyState? state, IPhaseHistory? history, OperatorLibrary? library, Func<string, string?> phaseName, Func<string, string>? text = null) => null;
}
