using System.IO;
using System.Text.RegularExpressions;
using EndHud.Vision.Operators;
using EndHud.Vision.Rotations;
using Xunit;

namespace EndHud.Tests;

/// <summary>
/// One wording for conditions (owner, 2026-10-24: "everything that supports condition use the same
/// language"): every kind has one plain statement (<see cref="ConditionWords.Statement"/>: the alert's
/// words, the editor's lists), and a need "需要：…（当前 x）" is made from the same words
/// (<see cref="ConditionWords.Need"/>), the current value where one is read. (The strip shows conditions as badges, no
/// words: <see cref="StripBadgesTests"/>.) Built from the zh-CN strings
/// read from the file, as <see cref="AlertTextTests"/> does.
/// </summary>
public class ConditionWordsTests
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

    private static string? PhaseName(string id) => id == "charge" ? "充能轴 I" : null;

    private static string Statement(StateCondition c) => ConditionWords.Statement(c, null, PhaseName, S);

    private static string? Need(StateCondition c, Vision.PartyState? state = null, IPhaseHistory? history = null) =>
        ConditionWords.Need(c, state, history, null, PhaseName, S);

    private static readonly string[] Squad = ["Si", "Last_Rite", "Arcane", "Tangtang"];

    /// <summary>Si in slot 1 (controlled): her ultimate at 42%, her combo bar at 30%, 1.56 skill points, 5 damage stacks; the enemy has 1 Cryo stack.</summary>
    private static Vision.PartyState Reading()
    {
        var slots = Squad.Select((id, i) => new Vision.PartySlot(i + 1, null, 1.0, "", new Vision.Frame(1, 1, new byte[4]),
            Combo: new Vision.ComboReading(0.3, false),
            Ultimate: new Vision.UltimateReading(false, 0.42),
            Operator: new OperatorMatch(new OperatorInfo(id, id, id, id + ".png"), 1, true))).ToList();
        return new Vision.PartyState(slots, PanelMarks: 8)
        {
            SkillPoints = 1.56,
            ActiveSlot = 1,
            Counters = [new CounterReading("Si", "damage-stacks", 5)],
            BossStatuses = [new Vision.BossStatusReading("Cryo Infliction", 0, 1, 1)],
        };
    }

    private sealed class History(int shown, int played) : IPhaseHistory
    {
        public int? TimesShown(string phaseId) => phaseId == "charge" ? shown : null;

        public int? TimesPlayed(string phaseId) => phaseId == "charge" ? played : null;
    }

    /// <summary>One of every kind (a new kind must be added here: the test below checks the catalogue).</summary>
    internal static readonly StateCondition[] EveryKind =
    [
        new SkillPointsCondition(Comparison.AtMost, 1),
        new ComboCondition("Si", true),
        new ComboChargeCondition("Si", Comparison.AtLeast, 80),
        new BattleSkillCondition("Si", true),
        new UltimateCondition("Si", false),
        new UltimateChargeCondition("Si", Comparison.AtLeast, 90),
        new UltimateActiveCondition("Laevatain", false),
        new BattleSkillActiveCondition("Si", false),
        new BattleSkillEmpoweredCondition("Si", true),
        new TeamBuffCondition("Link", Comparison.AtLeast, 1),
        new CounterCondition("Si", "damage-stacks", Comparison.Below, 3),
        new BossStatusCondition("Cryo Infliction", true, 3),
        new ControllingCondition("Last_Rite"),
        new PhaseShownCondition("charge", Comparison.AtLeast, 2),
        new PhasePlayedCondition("charge", Comparison.Above, 1),
        new AllCondition([new ComboCondition("Arcane", true), new UltimateCondition("Tangtang", true)]),
        new AnyCondition([new ComboCondition("Arcane", true), new UltimateCondition("Tangtang", true)]),
        new NotCondition(new ControllingCondition("Si")),
        new UltimateTimeLeftCondition("Laevatain", Comparison.AtMost, 30),
        new PhaseCurrentCondition("charge", PlayedThrough: true),
        new StepsLitCondition(StepsLitWhich.All),
        new StepsLitCondition(StepsLitWhich.AtLeast, Count: 3),
        new StepsLitCondition(StepsLitWhich.These, Steps: [1, 3]),
    ];

    /// <summary>本轴段步骤已点亮 in words, each of its three choices; "at least" with how many are lit now.</summary>
    [Fact]
    public void The_phases_steps_lit_reads_as_its_choice()
    {
        Assert.Equal(S("Alert.Describe.StepsLit.All"), Statement(new StepsLitCondition(StepsLitWhich.All)));
        Assert.Equal(F("Alert.Describe.StepsLit.AtLeast", "≥", 3), Statement(new StepsLitCondition(StepsLitWhich.AtLeast, Count: 3)));
        Assert.Equal(F("Alert.Describe.StepsLit.These", "1、3"), Statement(new StepsLitCondition(StepsLitWhich.These, Steps: [1, 3])));
        Assert.Equal(F("Rotation.Reason.NeedNow", F("Alert.Describe.StepsLit.AtLeast", "≥", 3), "2"),
            Need(new StepsLitCondition(StepsLitWhich.AtLeast, Count: 3), Reading(), new Lights([true, null, true, false])));
    }

    private sealed class Lights(IReadOnlyList<bool?> lit) : IPhaseHistory
    {
        public int? TimesShown(string phaseId) => null;

        public int? TimesPlayed(string phaseId) => null;

        public IReadOnlyList<bool?>? StepsLit => lit;
    }

    [Fact]
    public void The_samples_cover_every_kind_in_the_catalogue()
    {
        Assert.Equal(ConditionCatalogue.Kinds.Select(k => k.Type).ToHashSet(), EveryKind.Select(c => c.GetType()).ToHashSet());
    }

    /// <summary>Every kind reads as a plain statement, and its need is that statement after "需要：".</summary>
    [Fact]
    public void Every_kind_has_a_statement_and_a_need_made_of_it()
    {
        foreach (var condition in EveryKind)
        {
            string statement = Statement(condition);
            Assert.DoesNotContain("?", statement);
            Assert.DoesNotContain("Alert.", statement);
            Assert.DoesNotContain("{", statement);
            string? need = Need(condition, Reading(), new History(1, 1));
            Assert.NotNull(need);
            Assert.StartsWith(S("Rotation.Reason.Need").Replace("{0}", ""), need);
            Assert.Contains(statement, need);
        }
    }

    [Theory]
    [InlineData(0.96, "0.9")]
    [InlineData(1.4, "1.4")]
    [InlineData(2.0, "2")]
    [InlineData(0.0, "0")]
    public void Skill_points_short_of_what_is_needed_never_read_as_enough(double points, string shown) =>
        Assert.Equal(shown, ConditionWords.Points(points));

    [Fact]
    public void A_need_says_the_current_value_where_one_is_read()
    {
        string stacks = OperatorCounters.SiStacks.NameZh;
        string cryo = StatusNames.Zh("Cryo Infliction")!;

        // "Below" reads "<", not "=", and the counter is found by operator and counter.
        Assert.Equal(F("Rotation.Reason.NeedNow", F("Alert.Describe.Counter", "Si", stacks, "<", 3), "5"), Need(new CounterCondition("Si", "damage-stacks", Comparison.Below, 3), Reading()));
        // 1 Cryo stack of the 3 asked for (before: "the enemy lacks Cryo Infliction").
        Assert.Equal(F("Rotation.Reason.NeedNow", F("Alert.Describe.Status.Stacks", cryo, "≥", 3), "1"), Need(new BossStatusCondition("Cryo Infliction", true, 3), Reading()));
        // "At most" reads as such (before: "技力 1.5/1", as if 1 were needed).
        Assert.Equal(F("Rotation.Reason.NeedNow", F("Alert.Describe.SkillPoints", "≤", "1.0"), "1.5"), Need(new SkillPointsCondition(Comparison.AtMost, 1), Reading()));
        Assert.Equal(F("Rotation.Reason.NeedNow", F("Alert.Describe.UltimateCharge", "Si", "≥", 90), "42%"), Need(new UltimateChargeCondition("Si", Comparison.AtLeast, 90), Reading()));
        Assert.Equal(F("Rotation.Reason.NeedNow", F("Alert.Describe.ComboCharge", "Si", "≥", 80), "30%"), Need(new ComboChargeCondition("Si", Comparison.AtLeast, 80), Reading()));
        Assert.Equal(F("Rotation.Reason.NeedNow", F("Alert.Describe.PhasePlayed", "充能轴 I", ">", 1), "1"), Need(new PhasePlayedCondition("charge", Comparison.Above, 1), Reading(), new History(3, 1)));
        // Nothing read: the need alone.
        Assert.Equal(F("Rotation.Reason.Need", F("Alert.Describe.Combo.Ready", "Si")), Need(new ComboCondition("Si", true), Reading()));
        Assert.Equal(F("Rotation.Reason.Need", F("Alert.Describe.Counter", "Si", stacks, "<", 3)), Need(new CounterCondition("Si", "damage-stacks", Comparison.Below, 3), null));
    }

    /// <summary>战技已强化 in words: the operator's battle skill empowered, or not (未强化).</summary>
    [Fact]
    public void Empowered_reads_as_empowered_or_not()
    {
        Assert.Equal(F("Alert.Describe.BattleSkillEmpowered", "Si"), Statement(new BattleSkillEmpoweredCondition("Si", true)));
        Assert.Equal(F("Alert.Describe.BattleSkillNotEmpowered", "Si"), Statement(new BattleSkillEmpoweredCondition("Si", false)));
        Assert.Contains("战技已强化", S("Alert.Describe.BattleSkillEmpowered"));
        Assert.Contains("战技未强化", S("Alert.Describe.BattleSkillNotEmpowered"));
        Assert.Equal("战技已强化", S("Editor.Condition.BattleSkillEmpowered"));
    }

    /// <summary>连击层数 in words: the buff's Chinese name, the comparison and the stacks; the need says the stacks read now (0 when not shown).</summary>
    [Fact]
    public void Team_buff_stacks_read_as_its_name_and_count()
    {
        string link = StatusNames.Zh("Link")!;
        Assert.Equal("连击", link);
        Assert.Equal(F("Alert.Describe.TeamBuff", link, "≥", 1), Statement(new TeamBuffCondition("Link", Comparison.AtLeast, 1)));
        Assert.Equal("连击层数 ≥ 1", Statement(new TeamBuffCondition("Link", Comparison.AtLeast, 1)));
        Assert.Equal(F("Rotation.Reason.NeedNow", F("Alert.Describe.TeamBuff", link, "≥", 2), "1"),
            Need(new TeamBuffCondition("Link", Comparison.AtLeast, 2), Reading() with { Buffs = [new Vision.TeamBuff("Link", 1)] }));
        Assert.Equal(F("Rotation.Reason.NeedNow", F("Alert.Describe.TeamBuff", link, "≥", 1), "0"), Need(new TeamBuffCondition("Link", Comparison.AtLeast, 1), Reading()));
        Assert.Equal("队伍增益层数", S("Editor.Condition.TeamBuff"));
    }

    /// <summary>One word per meaning (Lead, 2026-10-24): a lasting skill that stopped is 已结束 everywhere, never 未持续.</summary>
    [Fact]
    public void Over_is_one_word_everywhere()
    {
        Assert.Contains("已结束", S("Alert.Describe.UltimateOver"));
        Assert.Contains("已结束", S("Alert.Describe.BattleSkillOver"));
        Assert.DoesNotContain(Strings.Value, kv => kv.Value.Contains("未持续"));
    }
}
