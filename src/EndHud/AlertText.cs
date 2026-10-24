using EndHud.Vision.Operators;
using EndHud.Vision.Rotations;

namespace EndHud;

/// <summary>
/// An alert's conditions in words (what the pop-up says when the player wrote no text, and how the
/// editor lists such a rule) and its subject, which picks the pop-up's icon: the shared wording
/// (<see cref="ConditionWords"/>) and icon rule (<see cref="ConditionIcon"/>), as alerts use them.
/// </summary>
internal static class AlertText
{
    /// <summary>The conditions in words, joined (<see cref="ConditionWords.Statements"/>).</summary>
    /// <param name="phaseName">A phase's name as the alert editor lists it (rotation and phase), by id; null for one no rotation has.</param>
    /// <param name="text">The strings (default: the app's, <see cref="Loc.Get"/>).</param>
    public static string Describe(IReadOnlyList<StateCondition> when, OperatorLibrary? library, Func<string, string?> phaseName, Func<string, string>? text = null) =>
        ConditionWords.Statements(when, library, phaseName, text);

    /// <summary>What the alert is about, for its icon (<see cref="ConditionIcon.Subject"/>).</summary>
    public static AlertSubject Subject(IReadOnlyList<StateCondition> when) => ConditionIcon.Subject(when);

    /// <summary>The operator a condition is about, if any (<see cref="ConditionCatalogue"/>).</summary>
    public static string? OperatorOf(StateCondition c) => ConditionCatalogue.OperatorOf(c);
}
