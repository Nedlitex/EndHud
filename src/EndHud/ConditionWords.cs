using System.Globalization;
using EndHud.Vision;
using EndHud.Vision.Operators;
using EndHud.Vision.Rotations;

namespace EndHud;

/// <summary>
/// Conditions in words, one wording for every place a condition is shown (owner, 2026-10-24: "the
/// same language"). Every kind has one plain statement (<see cref="Statement"/>), e.g. "技力 ≤ 1.0",
/// "寒冷附着 ≥ 3 层", "祀 战技已结束": what an alert pops up when the player wrote no text, how the
/// alert editor lists such a rule, the rotation editor's lists. (The strip shows conditions as badges, no words:
/// <see cref="StripBadges"/>.) A need is the same statement (<see cref="Need"/>): "需要：寒冷附着 ≥ 3 层（当前 1）", with the
/// value read now where there is one (<see cref="Current"/>). Every word comes from the i18n strings
/// (<c>Alert.Describe.*</c> for the statements, <c>Rotation.Reason.Need</c>/<c>NeedNow</c> around them).
/// </summary>
internal static class ConditionWords
{
    /// <summary>The conditions in words, joined; groups as "A or B" and "(A and B)" in the strings' words.</summary>
    /// <param name="phaseName">A phase's name as the editors list it, by id; null for one that doesn't exist.</param>
    /// <param name="text">The strings (default: the app's, <see cref="Loc.Get"/>).</param>
    public static string Statements(IReadOnlyList<StateCondition> when, OperatorLibrary? library, Func<string, string?> phaseName, Func<string, string>? text = null)
    {
        text ??= Loc.Get;
        var strings = text;
        return string.Join(text("Alert.Describe.Separator"), when.Select(c => One(c, library, phaseName, strings, nested: false)));
    }

    /// <summary>One condition as a plain statement.</summary>
    public static string Statement(StateCondition condition, OperatorLibrary? library, Func<string, string?> phaseName, Func<string, string>? text = null) =>
        One(condition, library, phaseName, text ?? Loc.Get, nested: false);

    /// <summary>
    /// What a condition needs, from its statement: "需要：{statement}", and "（当前 x）" when the value it
    /// compares is read now (<see cref="Current"/>).
    /// </summary>
    public static string Need(StateCondition condition, PartyState? state, IPhaseHistory? history, OperatorLibrary? library, Func<string, string?> phaseName, Func<string, string>? text = null)
    {
        text ??= Loc.Get;
        string statement = Statement(condition, library, phaseName, text);
        return Current(condition, state, history, phaseName) is { } now
            ? string.Format(CultureInfo.InvariantCulture, text("Rotation.Reason.NeedNow"), statement, now)
            : string.Format(CultureInfo.InvariantCulture, text("Rotation.Reason.Need"), statement);
    }

    /// <summary>
    /// The value the condition compares, as read now: skill points, a charge's or cooldown's percentage,
    /// a counter, a status's stacks or level, a phase's showings or plays, a lasting ultimate's time
    /// left, the phase on the strip. Null for a yes/no kind, or when it isn't read.
    /// </summary>
    /// <param name="phaseName">The phases by the names shown; null: ids.</param>
    public static string? Current(StateCondition condition, PartyState? state, IPhaseHistory? history, Func<string, string?>? phaseName = null)
    {
        string Number(int n) => n.ToString(CultureInfo.InvariantCulture);
        PartySlot? Slot(string id) => state?.Slots.FirstOrDefault(s => s.Operator?.Operator.Id == id);
        string Percent(double share) => $"{Math.Round(share * 100).ToString(CultureInfo.InvariantCulture)}%";
        switch (condition)
        {
            case SkillPointsCondition:
                return state?.SkillPoints is { } points ? Points(points) : null;
            case ComboChargeCondition c:
                return Slot(c.OperatorId)?.Combo is { } combo ? Percent(combo.Ready ? 1 : combo.Progress) : null;
            case UltimateChargeCondition c:
                return Slot(c.OperatorId)?.Ultimate is { } ultimate ? Percent(ultimate.Ready ? 1 : ultimate.Progress) : null;
            case CounterCondition c:
                return state?.Counters.FirstOrDefault(r => r.OperatorId == c.OperatorId && r.CounterId == c.CounterId)?.Value is { } value ? Number(value) : null;
            case TeamBuffCondition c:
                return state?.BuffStacks(c.BuffId) is { } buffStacks ? Number(buffStacks) : null;
            case StaggerProgressCondition:
                return state?.StaggerPercent is { } staggerPercent ? $"{Number(staggerPercent)}%" : null;
            case StaggerNodesCondition:
                return state?.Stagger is { } stagger ? Number(stagger.NodesReached) : null;
            case StaggerTimeLeftCondition:
                return state?.StaggerTimeLeftPercent is { } staggerLeft ? $"{Number(staggerLeft)}%" : null;
            case BossStatusCondition { MinStacks: > 1 } c:
                if (state?.BossStatuses is not { } statuses) return null;
                if (statuses.FirstOrDefault(s => s.StatusId == c.StatusId) is not { } status) return Number(0);
                if (status.Stacks is not { } stacks) return null;
                return ReactionLevel.Measured.Contains(c.StatusId) && stacks is >= 1 and <= 4 ? Roman[stacks - 1] : Number(stacks);
            case PhaseShownCondition c:
                return history?.TimesShown(c.PhaseId) is { } shown ? Number(shown) : null;
            case PhasePlayedCondition c:
                return history?.TimesPlayed(c.PhaseId) is { } played ? Number(played) : null;
            case UltimateTimeLeftCondition c:
                if (state?.SkillPoints is null || !LastingUltimates.Operators.Contains(c.OperatorId)) return null;
                var running = state.RunningUltimates ?? new LastingUltimates().Update(state, TimeSpan.Zero);
                return Percent(running.TryGetValue(c.OperatorId, out var view) ? view.LeftShare : 0);
            case PhaseCurrentCondition:
                return history?.CurrentPhase is { } current ? phaseName?.Invoke(current) ?? current : null;
            case StepsLitCondition { Which: StepsLitWhich.AtLeast }:
                return history?.StepsLit is { } lit ? Number(lit.Count(l => l == true)) : null;
            default:
                return null;
        }
    }

    /// <summary>The game's numerals for a reaction's level (I–IV).</summary>
    internal static readonly string[] Roman = ["I", "II", "III", "IV"];

    /// <summary>
    /// Skill points as they are read out: rounded down, so 0.96 of the 1 needed reads "0.9", never a "1" that looks
    /// enough.
    /// </summary>
    internal static string Points(double points) =>
        (Math.Floor(points * 10 + 1e-9) / 10).ToString("0.#", CultureInfo.InvariantCulture);

    private static string One(StateCondition condition, OperatorLibrary? library, Func<string, string?> phaseName, Func<string, string> text, bool nested)
    {
        string F(string key, params object[] args) => string.Format(CultureInfo.InvariantCulture, text(key), args);
        string Who(string id) => library?.Find(id)?.NameZh ?? id;
        string Percent(int percent) => percent.ToString(CultureInfo.InvariantCulture);
        string Phase(string id) => phaseName(id) ?? text("Editor.PhaseMissing");
        return condition switch
        {
            SkillPointsCondition c => F("Alert.Describe.SkillPoints", Symbol(c.Is), c.Value.ToString("0.0#", CultureInfo.InvariantCulture)),
            ComboCondition c => F(c.Ready ? "Alert.Describe.Combo.Ready" : "Alert.Describe.Combo.Used", Who(c.OperatorId)),
            ComboChargeCondition c => F("Alert.Describe.ComboCharge", Who(c.OperatorId), Symbol(c.Is), Percent(c.Percent)),
            BattleSkillCondition c => F(c.Ready ? "Alert.Describe.BattleSkill.Ready" : "Alert.Describe.BattleSkill.Cooling", Who(c.OperatorId)),
            UltimateCondition c => F(c.Ready ? "Alert.Describe.Ultimate.Ready" : "Alert.Describe.Ultimate.Used", Who(c.OperatorId)),
            UltimateChargeCondition c => F("Alert.Describe.UltimateCharge", Who(c.OperatorId), Symbol(c.Is), Percent(c.Percent)),
            UltimateActiveCondition c => F(c.Active ? "Alert.Describe.UltimateActive" : "Alert.Describe.UltimateOver", Who(c.OperatorId)),
            UltimateTimeLeftCondition c => F("Alert.Describe.UltimateTimeLeft", Who(c.OperatorId), Symbol(c.Is), Percent(c.Percent)),
            PhaseCurrentCondition c => F(c.PlayedThrough switch
            {
                true => "Alert.Describe.PhaseCurrent.Played",
                false => "Alert.Describe.PhaseCurrent.NotPlayed",
                null => "Alert.Describe.PhaseCurrent",
            }, Phase(c.PhaseId)),
            BattleSkillActiveCondition c => F(c.Active ? "Alert.Describe.BattleSkillActive" : "Alert.Describe.BattleSkillOver", Who(c.OperatorId)),
            BattleSkillEmpoweredCondition c => F(c.Empowered ? "Alert.Describe.BattleSkillEmpowered" : "Alert.Describe.BattleSkillNotEmpowered", Who(c.OperatorId)),
            CounterCondition c => F("Alert.Describe.Counter", Who(c.OperatorId),
                OperatorCounters.Definitions.FirstOrDefault(d => d.OperatorId == c.OperatorId && d.Id == c.CounterId)?.NameZh ?? c.CounterId,
                Symbol(c.Is), c.Value),
            BossStatusCondition c => Status(c, F),
            TeamBuffCondition c => F("Alert.Describe.TeamBuff", StatusNames.Zh(c.BuffId) ?? c.BuffId, Symbol(c.Is), c.Value),
            StaggerProgressCondition c => F("Alert.Describe.StaggerProgress", Symbol(c.Is), Percent(c.Percent)),
            StaggerNodesCondition c => F("Alert.Describe.StaggerNodes", Symbol(c.Is), c.Count),
            StaggeredCondition c => F(c.Staggered ? "Alert.Describe.Staggered" : "Alert.Describe.NotStaggered"),
            StaggerTimeLeftCondition c => F("Alert.Describe.StaggerTimeLeft", Symbol(c.Is), Percent(c.Percent)),
            ControllingCondition c => F("Alert.Describe.Controlling", Who(c.OperatorId)),
            NotCondition c => F("Alert.Describe.Not", One(c.Of, library, phaseName, text, nested: false)),
            AnyCondition c => Group(c.Of, "Alert.Describe.Or"),
            AllCondition c => Group(c.Of, "Alert.Describe.And"),
            PhaseShownCondition c => F("Alert.Describe.PhaseShown", Phase(c.PhaseId), Symbol(c.Is), c.Times),
            PhasePlayedCondition c => F("Alert.Describe.PhasePlayed", Phase(c.PhaseId), Symbol(c.Is), c.Times),
            StepsLitCondition c => c.Which switch
            {
                StepsLitWhich.All => F("Alert.Describe.StepsLit.All"),
                StepsLitWhich.AtLeast => F("Alert.Describe.StepsLit.AtLeast", Symbol(Comparison.AtLeast), c.Count),
                _ => F("Alert.Describe.StepsLit.These", string.Join(text("Alert.Describe.StepsLit.Joiner"), (c.Steps ?? []).Select(n => n.ToString(CultureInfo.InvariantCulture)))),
            },
            _ => "?",
        };

        string Group(IReadOnlyList<StateCondition> parts, string joiner)
        {
            // "{0} or {1}" (Alert.Describe.Or), folded: the words around the joiner live in the string, not in code.
            string joined = parts.Count == 0 ? "" : parts.Select(p => One(p, library, phaseName, text, nested: true)).Aggregate((a, b) => F(joiner, a, b));
            return parts.Count > 1 && (nested || joiner == "Alert.Describe.And") ? F("Alert.Describe.Group", joined) : joined;
        }
    }

    private static string Status(BossStatusCondition c, Func<string, object[], string> format)
    {
        string name = StatusNames.Zh(c.StatusId) ?? c.StatusId;
        if (c.MinStacks <= 1) return format(c.Present ? "Alert.Describe.Status.Has" : "Alert.Describe.Status.HasNot", [name]);
        string count = ReactionLevel.Measured.Contains(c.StatusId) && c.MinStacks is >= 1 and <= 4
            ? format("Alert.Describe.Status.Level", [name, Symbol(Comparison.AtLeast), Roman[c.MinStacks - 1]])
            : format("Alert.Describe.Status.Stacks", [name, Symbol(Comparison.AtLeast), c.MinStacks]);
        return c.Present ? count : format("Alert.Describe.Not", [count]);
    }

    internal static string Symbol(Comparison comparison) => comparison switch
    {
        Comparison.AtLeast => "≥",
        Comparison.AtMost => "≤",
        Comparison.Below => "<",
        Comparison.Above => ">",
        _ => "=",
    };
}
