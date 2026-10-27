using System.Globalization;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EndHud.Vision;
using EndHud.Vision.Operators;
using EndHud.Vision.Rotations;

namespace EndHud;

/// <summary>
/// Conditions as the strip's badges (<see cref="StripBadge"/>; owner, 2026-10-25: "like 连击 icon >= 1, fire icon >= 1,
/// etc? not text"). One badge per condition: its picture by the shared icon rule (<see cref="ConditionIcon.BadgeSubject"/>),
/// the comparison and number ("≥1", "=4", "&lt;2", "≥90%", a reaction's level "≥II"), a slash for the opposite (not
/// ready, absent, not running, "not"), and a sign where kinds share a skill's icon (running, empowered). A group gives a
/// badge per part ("or": a "/" between them); a "not" over a group is pushed down to its parts (not (a and b) = not a or
/// not b). Each badge is bright by its own condition, judged on the current reading.
/// </summary>
internal static class StripBadges
{
    /// <summary>Where the badges' pictures come from.</summary>
    /// <param name="StepPicture">The picture of the phase's step numbered so (from 1), for "these steps lit".</param>
    internal sealed record Pictures(OperatorLibrary? Library, Func<OperatorInfo, StepAction, BitmapSource?> SkillIcon, Func<OperatorInfo, BitmapSource?> Avatar,
        Func<string, BitmapSource?> StatusIcon, Func<int, ImageSource?> StepPicture);

    /// <param name="holds">Whether a condition holds now (null: can't be told); asked of each badge's own condition, a
    /// "not" as a <see cref="NotCondition"/> of it.</param>
    /// <param name="text">The strings, for a unit a badge carries (seconds); default: the app's (<see cref="Loc.Get"/>).</param>
    public static IReadOnlyList<StripBadge> Of(IReadOnlyList<StateCondition> conditions, Func<StateCondition, bool?> holds, Pictures pictures, Func<string, string>? text = null)
    {
        text ??= Loc.Get;
        var badges = new List<StripBadge>();
        foreach (var condition in conditions) Add(condition, not: false, or: false);
        return badges;

        // or: the first badge this adds comes after a "/" (an "or" before it).
        void Add(StateCondition condition, bool not, bool or)
        {
            switch (condition)
            {
                case NotCondition n:
                    Add(n.Of, !not, or);
                    break;
                // "and" (or a "not" over an "or"): every part, side by side.
                case AllCondition { Of: var parts } when !not:
                    Parts(parts, not, or, any: false);
                    break;
                case AnyCondition { Of: var parts } when not:
                    Parts(parts, not, or, any: false);
                    break;
                // "or" (or a "not" over an "and"): any one part, a "/" between them.
                case AllCondition { Of: var parts }:
                    Parts(parts, not, or, any: true);
                    break;
                case AnyCondition { Of: var parts }:
                    Parts(parts, not, or, any: true);
                    break;
                case StepsLitCondition { Which: StepsLitWhich.These, Steps: { Count: > 0 } steps }:
                    // A badge per step named, its picture; "not" over them: any one not lit.
                    for (int i = 0; i < steps.Count; i++)
                    {
                        // One step named: the condition itself, as it was checked (the finish condition's check knows this round's lights).
                        StateCondition one = steps.Count == 1 ? condition : new StepsLitCondition(StepsLitWhich.These, Steps: [steps[i]]);
                        var picture = pictures.StepPicture(steps[i]);
                        badges.Add(new StripBadge(picture, picture is null ? BadgeGlyph.Steps : BadgeGlyph.Picture, null, BadgeSign.None, not,
                            Holds(not ? new NotCondition(one) : one), i == 0 ? or : not));
                    }
                    break;
                default:
                    badges.Add(Leaf(condition, not, Holds(not ? new NotCondition(condition) : condition), or));
                    break;
            }
        }

        void Parts(IReadOnlyList<StateCondition> parts, bool not, bool or, bool any)
        {
            for (int i = 0; i < parts.Count; i++) Add(parts[i], not, i == 0 ? or : any);
        }

        bool Holds(StateCondition condition) => holds(condition) == true;

        StripBadge Leaf(StateCondition condition, bool not, bool bright, bool or)
        {
            var subject = ConditionIcon.BadgeSubject(condition);
            var picture = ConditionIcon.Picture(subject, pictures.Library, pictures.StatusIcon, pictures.Avatar, pictures.SkillIcon);
            var glyph = condition is StepsLitCondition ? BadgeGlyph.Steps
                : subject.Kind == AlertSubjectKind.SkillPoints ? BadgeGlyph.SkillPoints
                : subject.Kind == AlertSubjectKind.None ? BadgeGlyph.Other
                : BadgeGlyph.Picture;
            var (value, sign, opposite) = condition switch
            {
                SkillPointsCondition c => (Compare(c.Is, Number(c.Value)), BadgeSign.None, false),
                ComboCondition c => (null, BadgeSign.None, !c.Ready),
                ComboChargeCondition c => (Compare(c.Is, $"{c.Percent}%"), BadgeSign.None, false),
                UltimateCondition c => (null, BadgeSign.None, !c.Ready),
                UltimateChargeCondition c => (Compare(c.Is, $"{c.Percent}%"), BadgeSign.None, false),
                BattleSkillCondition c => (null, BadgeSign.None, !c.Ready),
                UltimateActiveCondition c => (null, BadgeSign.Running, !c.Active),
                BattleSkillActiveCondition c => (null, BadgeSign.Running, !c.Active),
                BattleSkillEmpoweredCondition c => (null, BadgeSign.Empowered, !c.Empowered),
                UltimateTimeLeftCondition c => (Compare(c.Is, $"{c.Percent}%"), BadgeSign.Running, false),
                CounterCondition c => (Compare(c.Is, Number(c.Value)), BadgeSign.None, false),
                TeamBuffCondition c => (Compare(c.Is, Number(c.Value)), BadgeSign.None, false),
                StaggerProgressCondition c => (Compare(c.Is, $"{c.Percent}%"), BadgeSign.None, false),
                StaggerNodesCondition c => (Compare(c.Is, Number(c.Count)), BadgeSign.None, false),
                // Staggered and its time left share the countdown's picture: the time left carries the running sign.
                StaggeredCondition c => (null, BadgeSign.None, !c.Staggered),
                StaggerTimeLeftCondition c => (Compare(c.Is, $"{c.Percent}%"), BadgeSign.Running, false),
                // Seconds, with the unit from the strings (Rotation.Badge.Seconds).
                TimeInPhaseCondition c => (Compare(c.Is, string.Format(CultureInfo.InvariantCulture, text("Rotation.Badge.Seconds"), ConditionWords.Seconds(c.Seconds))), BadgeSign.None, false),
                // Present: its stacks (a reaction's level in the game's numerals). Absent: a slash; "not absent" is present, at least 1.
                BossStatusCondition { Present: true } c => (Stacks(c), BadgeSign.None, false),
                BossStatusCondition => (not ? Compare(Comparison.AtLeast, "1") : null, BadgeSign.None, true),
                PhaseShownCondition c => (Compare(c.Is, Number(c.Times)), BadgeSign.None, false),
                PhasePlayedCondition c => (Compare(c.Is, Number(c.Times)), BadgeSign.None, false),
                StepsLitCondition { Which: StepsLitWhich.AtLeast } c => (Compare(Comparison.AtLeast, Number(c.Count)), BadgeSign.None, false),
                // Controlled, a phase on the strip, every step lit, anything new: the picture alone.
                _ => ((string?)null, BadgeSign.None, false),
            };
            return new StripBadge(picture, glyph, value, sign, not != opposite, bright, or);
        }
    }

    /// <summary>
    /// The current step's own ability, as a badge: only a battle skill short of skill points, the point it needs, dim
    /// (owner, 2026-10-25: no words on the strip). A battle skill cooling, a combo or an ultimate not ready get none:
    /// the white chevron already says "not yet", and its own icon is the step's.
    /// </summary>
    /// <param name="own">The step's own-ability check (<see cref="CheckKind.OwnAbility"/>).</param>
    public static StripBadge? OwnAbility(RotationStep step, ConditionCheck own, PartyState? state) =>
        step.Action == StepAction.BattleSkill && own.Holds == false && state?.SkillPoints is < 1
            ? new StripBadge(null, BadgeGlyph.SkillPoints, Compare(Comparison.AtLeast, "1"), BadgeSign.None, Not: false, Holds: false)
            : null;

    private static string Compare(Comparison comparison, string number) => ConditionWords.Symbol(comparison) + number;

    private static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Stacks(BossStatusCondition c) =>
        Compare(Comparison.AtLeast, ReactionLevel.Measured.Contains(c.StatusId) && c.MinStacks is >= 1 and <= 4
            ? ConditionWords.Roman[c.MinStacks - 1]
            : Number(Math.Max(1, c.MinStacks)));
}
