using System.Globalization;
using EndHud.Vision.Operators;
using EndHud.Vision.Rotations;

namespace EndHud;

/// <summary>What an alert is about, for its icon: the skill points, a boss status, an operator, or nothing in particular.</summary>
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
/// An alert's conditions in words (what the pop-up says when the player wrote no text, and how the
/// editor lists such a rule), e.g. "skill points ≥ 1.8", "Cryo Infliction ≥ 3 stacks", "Laevatain's ultimate ready" (in the
/// player's language); and its subject,
/// which picks the pop-up's icon. Every word comes from the i18n strings (keys <c>Alert.Describe.*</c>).
/// </summary>
internal static class AlertText
{
    /// <summary>The conditions in words, joined; groups as "A or B" and "(A and B)" in the strings' words.</summary>
    /// <param name="text">The strings (default: the app's, <see cref="Loc.Get"/>).</param>
    public static string Describe(IReadOnlyList<StateCondition> when, OperatorLibrary? library, Func<string, string>? text = null)
    {
        text ??= Loc.Get;
        return string.Join(text("Alert.Describe.Separator"), when.Select(c => One(c, library, text, nested: false)));
    }

    private static string One(StateCondition condition, OperatorLibrary? library, Func<string, string> text, bool nested)
    {
        string F(string key, params object[] args) => string.Format(CultureInfo.InvariantCulture, text(key), args);
        string Who(string id) => library?.Find(id)?.NameZh ?? id;
        string Percent(int percent) => percent.ToString(CultureInfo.InvariantCulture);
        return condition switch
        {
            SkillPointsCondition c => F("Alert.Describe.SkillPoints", Symbol(c.Is), c.Value.ToString("0.0#", CultureInfo.InvariantCulture)),
            ComboCondition c => F(c.Ready ? "Alert.Describe.Combo.Ready" : "Alert.Describe.Combo.Used", Who(c.OperatorId)),
            ComboChargeCondition c => F("Alert.Describe.ComboCharge", Who(c.OperatorId), Symbol(c.Is), Percent(c.Percent)),
            BattleSkillCondition c => F(c.Ready ? "Alert.Describe.BattleSkill.Ready" : "Alert.Describe.BattleSkill.Cooling", Who(c.OperatorId)),
            UltimateCondition c => F(c.Ready ? "Alert.Describe.Ultimate.Ready" : "Alert.Describe.Ultimate.Used", Who(c.OperatorId)),
            UltimateChargeCondition c => F("Alert.Describe.UltimateCharge", Who(c.OperatorId), Symbol(c.Is), Percent(c.Percent)),
            UltimateActiveCondition c => F(c.Active ? "Alert.Describe.UltimateActive" : "Alert.Describe.UltimateOver", Who(c.OperatorId)),
            CounterCondition c => F("Alert.Describe.Counter", Who(c.OperatorId),
                OperatorCounters.Definitions.FirstOrDefault(d => d.OperatorId == c.OperatorId && d.Id == c.CounterId)?.NameZh ?? c.CounterId,
                Symbol(c.Is), c.Value),
            BossStatusCondition c => Status(c, F),
            ControllingCondition c => F("Alert.Describe.Controlling", Who(c.OperatorId)),
            NotCondition c => F("Alert.Describe.Not", One(c.Of, library, text, nested: false)),
            AnyCondition c => Group(c.Of, "Alert.Describe.Or"),
            AllCondition c => Group(c.Of, "Alert.Describe.And"),
            // Not offered for alerts; a shared file could hold one.
            PhaseShownCondition => text("Alert.Describe.PhaseShown"),
            _ => "?",
        };

        string Group(IReadOnlyList<StateCondition> parts, string joiner)
        {
            // "{0} or {1}" (Alert.Describe.Or), folded: the words around the joiner live in the string, not in code.
            string joined = parts.Count == 0 ? "" : parts.Select(p => One(p, library, text, nested: true)).Aggregate((a, b) => F(joiner, a, b));
            return parts.Count > 1 && (nested || joiner == "Alert.Describe.And") ? F("Alert.Describe.Group", joined) : joined;
        }
    }

    private static string Status(BossStatusCondition c, Func<string, object[], string> format)
    {
        string name = StatusNames.Zh(c.StatusId) ?? c.StatusId;
        if (c.MinStacks <= 1) return format(c.Present ? "Alert.Describe.Status.Has" : "Alert.Describe.Status.HasNot", [name]);
        string count = ReactionLevelWords(c.StatusId, c.MinStacks, format) ?? format("Alert.Describe.Status.Stacks", [name, Symbol(Comparison.AtLeast), c.MinStacks]);
        return c.Present ? count : format("Alert.Describe.Not", [count]);

        static string? ReactionLevelWords(string id, int level, Func<string, object[], string> format) =>
            Vision.ReactionLevel.Measured.Contains(id) && level is >= 1 and <= 4
                ? format("Alert.Describe.Status.Level", [StatusNames.Zh(id) ?? id, Symbol(Comparison.AtLeast), new[] { "I", "II", "III", "IV" }[level - 1]])
                : null;
    }

    private static string Symbol(Comparison comparison) => comparison switch
    {
        Comparison.AtLeast => "≥",
        Comparison.AtMost => "≤",
        Comparison.Below => "<",
        Comparison.Above => ">",
        _ => "=",
    };

    /// <summary>
    /// What the alert is about, for its icon: its first condition (inside a "not" or a group, the first
    /// part). Skill points, a boss status, or the operator a skill, charge or counter belongs to.
    /// </summary>
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
            _ => OperatorOf(c) is { } id ? new AlertSubject(AlertSubjectKind.Operator, id) : new AlertSubject(AlertSubjectKind.None),
        };
    }

    /// <summary>The operator a condition is about, if any.</summary>
    public static string? OperatorOf(StateCondition c) => c switch
    {
        ComboCondition x => x.OperatorId,
        ComboChargeCondition x => x.OperatorId,
        BattleSkillCondition x => x.OperatorId,
        UltimateCondition x => x.OperatorId,
        UltimateChargeCondition x => x.OperatorId,
        UltimateActiveCondition x => x.OperatorId,
        CounterCondition x => x.OperatorId,
        ControllingCondition x => x.OperatorId,
        _ => null,
    };
}
