using System.IO;
using System.Text.RegularExpressions;
using EndHud.Vision.Rotations;
using Xunit;

namespace EndHud.Tests;

/// <summary>
/// An alert's conditions in words (<see cref="AlertText"/>), as the pop-up shows them when the player
/// wrote no text, built from the zh-CN strings (read from the file, so the shared application's
/// strings stay untouched); and the subject that picks the pop-up's icon.
/// </summary>
public class AlertTextTests
{
    private static readonly Lazy<Dictionary<string, string>> Strings = new(() =>
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string file = Path.Combine(dir.FullName, "src", "EndHud", "i18n", "zh-CN.xaml");
            if (!File.Exists(file)) continue;
            return Regex.Matches(File.ReadAllText(file), "x:Key=\"([^\"]+)\">([^<]*)<")
                .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
        }
        throw new DirectoryNotFoundException("src/EndHud/i18n");
    });

    private static string S(string key) => Strings.Value.TryGetValue(key, out var value) ? value : throw new KeyNotFoundException(key);

    private static string F(string key, params object[] args) => string.Format(S(key), args);

    private static string Describe(params StateCondition[] when) => AlertText.Describe(when, null, S);

    [Fact]
    public void Skill_points_read_as_a_comparison()
    {
        Assert.Equal(F("Alert.Describe.SkillPoints", "≥", "1.8"), Describe(new SkillPointsCondition(Comparison.AtLeast, 1.8)));
        Assert.Equal(F("Alert.Describe.SkillPoints", "<", "2.0"), Describe(new SkillPointsCondition(Comparison.Below, 2)));
    }

    [Fact]
    public void Stacks_and_levels_read_with_their_count()
    {
        string cryo = Vision.Operators.StatusNames.Zh("Cryo Infliction")!;
        string corrosion = Vision.Operators.StatusNames.Zh("Corrosion")!;

        Assert.Equal(F("Alert.Describe.Status.Stacks", cryo, "≥", 3), Describe(new BossStatusCondition("Cryo Infliction", true, 3)));
        Assert.Equal(F("Alert.Describe.Status.Level", corrosion, "≥", "III"), Describe(new BossStatusCondition("Corrosion", true, 3)));
        Assert.Equal(F("Alert.Describe.Status.Has", cryo), Describe(new BossStatusCondition("Cryo Infliction", true)));
        Assert.Equal(F("Alert.Describe.Not", F("Alert.Describe.Status.Stacks", cryo, "≥", 3)), Describe(new BossStatusCondition("Cryo Infliction", false, 3)));
    }

    [Fact]
    public void Several_conditions_and_groups_are_joined_in_words()
    {
        string text = Describe(
            new UltimateCondition("Laevatain", true),
            new AnyCondition([new SkillPointsCondition(Comparison.AtLeast, 2), new NotCondition(new ControllingCondition("Ardelia"))]));

        string expected = F("Alert.Describe.Ultimate.Ready", "Laevatain") + S("Alert.Describe.Separator")
            + F("Alert.Describe.Or", F("Alert.Describe.SkillPoints", "≥", "2.0"), F("Alert.Describe.Not", F("Alert.Describe.Controlling", "Ardelia")));
        Assert.Equal(expected, text);
        Assert.Contains(text, c => c >= '一' && c <= '鿿'); // in Chinese, not keys
    }

    [Fact]
    public void Every_condition_kind_has_words()
    {
        string text = Describe(
            new ComboCondition("Camille", false), new ComboChargeCondition("Camille", Comparison.AtLeast, 80),
            new BattleSkillCondition("Arcane", false), new UltimateChargeCondition("Laevatain", Comparison.AtMost, 50),
            new UltimateActiveCondition("Laevatain", false), new CounterCondition("Laevatain", "melting-flame", Comparison.Exactly, 4),
            new AllCondition([new ComboCondition("Arcane", true), new UltimateCondition("Ardelia", false)]));

        Assert.DoesNotContain("Alert.", text);
        Assert.DoesNotContain("?", text);
        Assert.Contains(F("Alert.Describe.Counter", "Laevatain", Vision.Operators.OperatorCounters.MeltingFlame.NameZh, "=", 4), text);
        Assert.Contains(F("Alert.Describe.Group", F("Alert.Describe.And", F("Alert.Describe.Combo.Ready", "Arcane"), F("Alert.Describe.Ultimate.Used", "Ardelia"))), text);
    }

    /// <summary>The owner's rule for the pop-up's icon: the subject of the rule's first condition.</summary>
    [Fact]
    public void The_subject_is_the_first_conditions()
    {
        Assert.Equal(new AlertSubject(AlertSubjectKind.SkillPoints), AlertText.Subject([new SkillPointsCondition(Comparison.AtLeast, 1.8), new BossStatusCondition("Cryo Infliction", true)]));
        Assert.Equal(new AlertSubject(AlertSubjectKind.Status, "Cryo Infliction"), AlertText.Subject([new BossStatusCondition("Cryo Infliction", true, 3)]));
        Assert.Equal(new AlertSubject(AlertSubjectKind.Operator, "Si"), AlertText.Subject([new CounterCondition("Si", "damage-stacks", Comparison.AtLeast, 40)]));
        Assert.Equal(new AlertSubject(AlertSubjectKind.Operator, "Laevatain"), AlertText.Subject([new NotCondition(new UltimateChargeCondition("Laevatain", Comparison.AtMost, 50))]));
        Assert.Equal(new AlertSubject(AlertSubjectKind.Status, "Corrosion"), AlertText.Subject([new AnyCondition([new BossStatusCondition("Corrosion", true, 2), new SkillPointsCondition(Comparison.AtLeast, 3)])]));
        Assert.Equal(new AlertSubject(AlertSubjectKind.None), AlertText.Subject([]));
        Assert.Equal(new AlertSubject(AlertSubjectKind.None), AlertText.Subject([new AnyCondition([])]));
    }
}
