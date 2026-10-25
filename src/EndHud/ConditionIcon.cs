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

    /// <summary>An operator's battle skill empowered (the glow is on that button): their battle skill's icon.</summary>
    BattleSkill,

    /// <summary>
    /// A skill of an operator's (<see cref="AlertSubject.Action"/>): its icon. Only a strip badge's subject
    /// (<see cref="ConditionIcon.BadgeSubject"/>), where a condition about a combo, an ultimate or a battle skill shows that
    /// skill; the alert pop-up keeps the operator's avatar for those.
    /// </summary>
    Skill,
}

/// <param name="Id">The status id or operator id; null for <see cref="AlertSubjectKind.SkillPoints"/> and <see cref="AlertSubjectKind.None"/>.</param>
/// <param name="Action">Which skill, for <see cref="AlertSubjectKind.Skill"/>.</param>
internal sealed record AlertSubject(AlertSubjectKind Kind, string? Id = null, StepAction? Action = null);

/// <summary>
/// The icon of a list of conditions, one rule wherever conditions get one (owner, 2026-10-22, for the
/// alert pop-up; the condition step's circle on the strip uses the same): the subject of the first
/// condition (inside a "not" or a group, its first part). Skill points draw their glyph, a boss
/// status its icon, an operator their avatar ("battle skill empowered": their battle skill's icon); anything else (or a picture not loaded yet) "!".
/// The strip's badges (<see cref="StripBadges"/>) take one condition each by the same rule, with a skill's own icon for a
/// condition about that skill (<see cref="BadgeSubject"/>).
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
            // A team buff's icon is the wiki's status icon (Link's), as the gauge row draws it.
            TeamBuffCondition b => new AlertSubject(AlertSubjectKind.Status, b.BuffId),
            BattleSkillEmpoweredCondition e => new AlertSubject(AlertSubjectKind.BattleSkill, e.OperatorId),
            NotCondition n => SubjectOf(n.Of),
            AllCondition { Of.Count: > 0 } all => SubjectOf(all.Of[0]),
            AnyCondition { Of.Count: > 0 } any => SubjectOf(any.Of[0]),
            _ => ConditionCatalogue.OperatorOf(c) is { } id ? new AlertSubject(AlertSubjectKind.Operator, id) : new AlertSubject(AlertSubjectKind.None),
        };
    }

    /// <summary>
    /// What one condition's badge on the strip shows: a combo's, an ultimate's or a battle skill's condition (ready, charge,
    /// running, time left) that skill's icon, so badges about one operator's different skills tell apart; anything else
    /// the shared rule's subject (<see cref="Subject"/>): a status's or buff's icon, the battle skill for "empowered", the
    /// skill points, the operator's avatar (a counter, controlled).
    /// </summary>
    public static AlertSubject BadgeSubject(StateCondition condition) => condition switch
    {
        ComboCondition c => new AlertSubject(AlertSubjectKind.Skill, c.OperatorId, StepAction.Combo),
        ComboChargeCondition c => new AlertSubject(AlertSubjectKind.Skill, c.OperatorId, StepAction.Combo),
        UltimateCondition c => new AlertSubject(AlertSubjectKind.Skill, c.OperatorId, StepAction.Ultimate),
        UltimateChargeCondition c => new AlertSubject(AlertSubjectKind.Skill, c.OperatorId, StepAction.Ultimate),
        UltimateActiveCondition c => new AlertSubject(AlertSubjectKind.Skill, c.OperatorId, StepAction.Ultimate),
        UltimateTimeLeftCondition c => new AlertSubject(AlertSubjectKind.Skill, c.OperatorId, StepAction.Ultimate),
        BattleSkillCondition c => new AlertSubject(AlertSubjectKind.Skill, c.OperatorId, StepAction.BattleSkill),
        BattleSkillActiveCondition c => new AlertSubject(AlertSubjectKind.Skill, c.OperatorId, StepAction.BattleSkill),
        _ => Subject([condition]),
    };

    /// <summary>
    /// The picture for the subject: a status's icon, an operator's avatar, or an operator's battle skill icon (their avatar
    /// while it isn't loaded, or when the wiki has none); null for skill points, nothing in particular, or one not loaded yet.
    /// </summary>
    public static BitmapSource? Picture(AlertSubject subject, OperatorLibrary? library, Func<string, BitmapSource?> statusIcon, Func<OperatorInfo, BitmapSource?> avatar,
        Func<OperatorInfo, StepAction, BitmapSource?> skillIcon) => subject switch
    {
        { Kind: AlertSubjectKind.Status, Id: { } status } => statusIcon(status),
        { Kind: AlertSubjectKind.Operator, Id: { } op } when library?.Find(op) is { } info => avatar(info),
        { Kind: AlertSubjectKind.BattleSkill, Id: { } op } when library?.Find(op) is { } info => skillIcon(info, StepAction.BattleSkill) ?? avatar(info),
        { Kind: AlertSubjectKind.Skill, Id: { } op, Action: { } action } when library?.Find(op) is { } info => skillIcon(info, action) ?? avatar(info),
        _ => null,
    };
}
