using System.IO;
using System.Text.RegularExpressions;
using EndHud.Vision.Operators;
using EndHud.Vision.Rotations;
using Xunit;

namespace EndHud.Tests;

/// <summary>
/// One wording for conditions (owner, 2026-10-24: "everything that supports condition use the same
/// language"): every kind has one plain statement (<see cref="ConditionWords.Statement"/>: the alert's
/// words, the condition step's caption), and the strip's "需要：…（当前 x）" is made from the same words
/// (<see cref="ConditionWords.Need"/>), the current value where one is read. Built from the zh-CN strings
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

        public bool PlayedIgnored => false;
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
    ];

    [Fact]
    public void The_samples_cover_every_kind_in_the_catalogue()
    {
        Assert.Equal(ConditionCatalogue.Kinds.Select(k => k.Type).ToHashSet(), EveryKind.Select(c => c.GetType()).ToHashSet());
    }

    /// <summary>Every kind reads as a plain statement, and the strip's need is that statement after "需要：".</summary>
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

    /// <summary>The strip gives a reason for every kind (before: nothing for combo or ultimate %, battle skill ready, phase shown or played).</summary>
    [Fact]
    public void The_strip_gives_a_reason_for_every_kind()
    {
        var step = new RotationStep("Si", StepAction.BattleSkill);
        foreach (var condition in EveryKind)
            Assert.NotNull(RotationStripView.Reason(new ConditionCheck(CheckKind.Expected, condition, false), step, Reading(), null, new History(1, 1)));
    }

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

    /// <summary>One word per meaning (Lead, 2026-10-24): a lasting skill that stopped is 已结束 everywhere, never 未持续.</summary>
    [Fact]
    public void Over_is_one_word_everywhere()
    {
        Assert.Contains("已结束", S("Alert.Describe.UltimateOver"));
        Assert.Contains("已结束", S("Alert.Describe.BattleSkillOver"));
        Assert.DoesNotContain(Strings.Value, kv => kv.Value.Contains("未持续"));
    }
}
