using System.Windows.Media.Imaging;
using EndHud.Vision.Operators;
using EndHud.Vision.Rotations;

namespace EndHud;

/// <summary>What conditions are about, for their icon: the skill points, a boss status, an operator, or nothing in particular.</summary>
internal enum AlertSubjectKind
{
    /// <summary>No single subject (an empty rule): the "!" glyph.</summary>
    None,

    SkillPoints,

    /// <summary>A boss status (an infliction, a reaction): its icon.</summary>
    Status,

    /// <summary>An operator's skill, charge, counter or being controlled: their avatar.</summary>
    Operator,
}

/// <param name="Id">The status id or operator id; null for <see cref="AlertSubjectKind.SkillPoints"/> and <see cref="AlertSubjectKind.None"/>.</param>
internal sealed record AlertSubject(AlertSubjectKind Kind, string? Id = null);

/// <summary>
/// The icon of a list of conditions, one rule wherever conditions get one (owner, 2026-10-22, for the
/// alert pop-up; the condition step's circle on the strip uses the same): the subject of the first
/// condition (inside a "not" or a group, its first part). Skill points draw their glyph, a boss
/// status its icon, an operator their avatar; anything else (or a picture not loaded yet) "!".
/// </summary>
internal static class ConditionIcon
{
    /// <summary>What the conditions are about: the first one's subject.</summary>
    public static AlertSubject Subject(IReadOnlyList<StateCondition> when)
    {
        if (when.Count == 0) return new AlertSubject(AlertSubjectKind.None);
        return SubjectOf(when[0]);

        static AlertSubject SubjectOf(StateCondition c) => c switch
        {
            SkillPointsCondition => new AlertSubject(AlertSubjectKind.SkillPoints),
            BossStatusCondition s => new AlertSubject(AlertSubjectKind.Status, s.StatusId),
            NotCondition n => SubjectOf(n.Of),
            AllCondition { Of.Count: > 0 } all => SubjectOf(all.Of[0]),
            AnyCondition { Of.Count: > 0 } any => SubjectOf(any.Of[0]),
            _ => ConditionCatalogue.OperatorOf(c) is { } id ? new AlertSubject(AlertSubjectKind.Operator, id) : new AlertSubject(AlertSubjectKind.None),
        };
    }

    /// <summary>The picture for the subject: a status's icon or an operator's avatar; null for skill points, nothing in particular, or one not loaded yet.</summary>
    public static BitmapSource? Picture(AlertSubject subject, OperatorLibrary? library, Func<string, BitmapSource?> statusIcon, Func<OperatorInfo, BitmapSource?> avatar) => subject switch
    {
        { Kind: AlertSubjectKind.Status, Id: { } status } => statusIcon(status),
        { Kind: AlertSubjectKind.Operator, Id: { } op } when library?.Find(op) is { } info => avatar(info),
        _ => null,
    };
}
