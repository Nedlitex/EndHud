using System.IO.Compression;
using System.Text;
using EndHud.Vision.Alerts;
using EndHud.Vision.Rotations;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// Players paste share codes from strangers, and rotation and alert files can be edited by hand: whatever they
/// hold, reading them ends in the refusals the editors already explain (not a rotation / damaged code / newer),
/// never in another exception, a hang or running out of memory. Sizes are limited well above real use (the
/// largest real rotation: 6.5 KB, 5 phases, 18 steps in a phase, 39 conditions, nested 5 deep).
/// </summary>
public sealed class ShareCodeHardeningTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "endhud-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    /// <summary>A share code holding exactly this text, as <see cref="RotationFormat.ToShareCode"/> packs it.</summary>
    private static string Code(string prefix, string json)
    {
        using var buffer = new MemoryStream();
        using (var deflate = new DeflateStream(buffer, CompressionLevel.SmallestSize)) deflate.Write(Encoding.UTF8.GetBytes(json));
        return prefix + Convert.ToBase64String(buffer.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private const string Squad = "\"squad\":[\"Laevatain\",\"Camille\",\"Arcane\",\"Ardelia\"]";

    private static string Rotation(string phases, int format = 12) => $$"""{"format":{{format}},"id":"x","name":"x",{{Squad}},"phases":[{{phases}}]}""";

    private static string Phase(string steps, string extra = "") => $$"""{"id":"p","name":"p","steps":[{{steps}}]{{extra}}}""";

    private const string Step = """{"operatorId":"Camille","action":"Combo"}""";

    private static string StepExpecting(string conditions) => $$"""{"operatorId":"Camille","action":"Combo","expect":[{{conditions}}]}""";

    private const string Leaf = """{"kind":"skillPoints","is":"AtLeast","value":1}""";

    private static string Nested(int depth) => depth <= 1 ? Leaf : $$"""{"kind":"all","of":[{{Nested(depth - 1)}}]}""";

    private static RotationReadError Refusal(string json) => Assert.Throws<RotationReadException>(() => RotationFormat.FromJson(json)).Error;

    public static TheoryData<string> Broken => new()
    {
        """{"format":1.5,"id":"x"}""",
        """{"format":1e40,"id":"x"}""",
        """{"format":-1e40,"id":"x"}""",
        Rotation(Phase(Step), format: 2).Replace("\"format\":2", "\"format\":2.0000001"),
        Rotation(Phase("null")),
        Rotation(Phase(Step, ",\"when\":[null]")),
        Rotation(Phase(Step, ",\"finish\":[null]")),
        Rotation(Phase(Step, ",\"priorityRules\":[{\"when\":[null],\"priority\":1}]")),
        Rotation(Phase(StepExpecting("null"))),
        Rotation(Phase(StepExpecting("""{"kind":"all","of":[null]}"""))),
        Rotation(Phase(StepExpecting("""{"kind":"any","of":null}"""))),
        Rotation(Phase(StepExpecting("""{"kind":"not","of":null}"""))),
        Rotation(Phase("""{"operatorId":null,"action":"Combo"}""")),
        Rotation(Phase(Step)).Replace("\"Laevatain\"", "null"),
        Rotation(Phase(Step)).Replace("\"phases\":[", "\"phases\":[1,"),
        Rotation(Phase(Step)).Replace("\"name\":\"x\"", "\"name\":{\"a\":1}"),
    };

    [Theory]
    [MemberData(nameof(Broken))]
    public void A_broken_rotation_is_refused_as_not_a_rotation(string json)
    {
        Assert.Equal(RotationReadError.NotARotation, Refusal(json));
        Assert.Equal(RotationReadError.DamagedShareCode,
            Assert.Throws<RotationReadException>(() => RotationFormat.FromShareCode(Code(RotationFormat.ShareCodePrefix, json))).Error);
    }

    [Fact]
    public void Conditions_nest_up_to_the_limit_and_no_deeper()
    {
        Assert.NotNull(RotationFormat.FromJson(Rotation(Phase(StepExpecting(Nested(ReadLimits.MaxConditionDepth)))))); // well above the 5 used
        Assert.Equal(RotationReadError.NotARotation, Refusal(Rotation(Phase(StepExpecting(Nested(ReadLimits.MaxConditionDepth + 1))))));
    }

    [Fact]
    public void Too_many_phases_steps_or_conditions_are_refused()
    {
        Assert.Equal(RotationReadError.NotARotation,
            Refusal(Rotation(string.Join(",", Enumerable.Range(0, ReadLimits.MaxPhases + 1).Select(i => Phase(Step).Replace("\"p\"", $"\"p{i}\""))))));
        Assert.Equal(RotationReadError.NotARotation, Refusal(Rotation(Phase(string.Join(",", Enumerable.Repeat(Step, ReadLimits.MaxStepsPerPhase + 1))))));
        Assert.Equal(RotationReadError.NotARotation, Refusal(Rotation(Phase(StepExpecting(string.Join(",", Enumerable.Repeat(Leaf, ReadLimits.MaxConditions + 1)))))));

        // At the limits it reads.
        Assert.NotNull(RotationFormat.FromJson(Rotation(Phase(string.Join(",", Enumerable.Repeat(Step, ReadLimits.MaxStepsPerPhase))))));
    }

    private static string V1(string sequence, int phases = 1) =>
        $$"""{"format":1,"id":"old","name":"old",{{Squad}},"phases":[{{string.Join(",", Enumerable.Range(0, phases).Select(i => $$"""{"id":"c{{i}}","name":"c","steps":[{{Step}}]}"""))}}],"sequence":[{{sequence}}]}""";

    [Fact]
    public void A_format_1_sequence_repeated_past_the_limits_is_refused_not_expanded()
    {
        // Real ones repeat a phase twice; two billion copies used to be made one by one.
        Assert.Equal(RotationReadError.NotARotation, Refusal(V1($$"""{"phaseId":"c0","times":{{ReadLimits.MaxRepeats + 1}}}""")));
        Assert.Equal(RotationReadError.NotARotation, Refusal(V1("""{"phaseId":"c0","times":2000000000}""")));
        Assert.Equal(RotationReadError.NotARotation,
            Refusal(V1(string.Join(",", Enumerable.Repeat("""{"phaseId":"c0","times":2}""", ReadLimits.MaxPhases)))));
        Assert.Equal(ReadLimits.MaxRepeats, RotationFormat.FromJson(V1($$"""{"phaseId":"c0","times":{{ReadLimits.MaxRepeats}}}""")).Phases.Count);
    }

    [Fact]
    public void A_share_code_that_unpacks_too_large_is_refused_as_damaged_without_unpacking_it_all()
    {
        // Mostly one repeated letter: a few KB packed, over the limit unpacked.
        string json = Rotation(Phase(Step)).Replace("\"name\":\"x\"", $"\"name\":\"{new string('x', ReadLimits.MaxTextBytes)}\"");
        string code = Code(RotationFormat.ShareCodePrefix, json);
        Assert.True(code.Length < 16 * 1024, $"{code.Length} characters");

        Assert.Equal(RotationReadError.DamagedShareCode, Assert.Throws<RotationReadException>(() => RotationFormat.FromShareCode(code)).Error);
        Assert.Equal(AlertReadError.DamagedShareCode,
            Assert.Throws<AlertReadException>(() => AlertFormat.FromShareCode(Code(AlertFormat.ShareCodePrefix, json))).Error);
    }

    [Fact]
    public void A_share_code_longer_than_any_real_one_is_refused_as_damaged()
    {
        string code = RotationFormat.ShareCodePrefix + new string('A', ReadLimits.MaxShareCodeChars + 1);

        Assert.Equal(RotationReadError.DamagedShareCode, Assert.Throws<RotationReadException>(() => RotationFormat.FromShareCode(code)).Error);
        Assert.Equal(AlertReadError.DamagedShareCode,
            Assert.Throws<AlertReadException>(() => AlertFormat.FromShareCode(AlertFormat.ShareCodePrefix + new string('A', ReadLimits.MaxShareCodeChars + 1))).Error);
    }

    [Fact]
    public void A_rotation_file_too_large_to_be_real_is_listed_as_unreadable()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "big.json"), Rotation(Phase(Step)).Replace("\"name\":\"x\"", $"\"name\":\"{new string('x', ReadLimits.MaxTextBytes)}\""));

        var (rotations, unreadable) = new RotationStore(_folder).Load();

        Assert.Empty(rotations);
        Assert.Equal(RotationReadError.NotARotation, Assert.Single(unreadable).Error);
    }

    private static string Alerts(string rules, string format = "6") => $$"""{"format":{{format}},"rules":[{{rules}}]}""";

    private static string Rule(string conditions) => $$"""{"id":"r","when":[{{conditions}}],"message":"m"}""";

    public static TheoryData<string> BrokenAlerts => new()
    {
        Alerts(Rule(Leaf), "1.5"),
        Alerts(Rule(Leaf), "1e40"),
        Alerts("null"),
        Alerts(Rule("null")),
        Alerts(Rule("""{"kind":"all","of":[null]}""")),
        Alerts(Rule("""{"kind":"not","of":null}""")),
        Alerts(Rule(Nested(ReadLimits.MaxConditionDepth + 1))),
        Alerts(Rule(string.Join(",", Enumerable.Repeat(Leaf, ReadLimits.MaxConditions + 1)))),
        Alerts(string.Join(",", Enumerable.Repeat(Rule(Leaf), ReadLimits.MaxAlertRulesRead + 1))),
    };

    [Theory]
    [MemberData(nameof(BrokenAlerts))]
    public void Broken_alert_rules_are_refused_as_not_alerts(string json)
    {
        Assert.Equal(AlertReadError.NotAlerts, Assert.Throws<AlertReadException>(() => AlertFormat.FromJson(json)).Error);
        Assert.Equal(AlertReadError.DamagedShareCode,
            Assert.Throws<AlertReadException>(() => AlertFormat.FromShareCode(Code(AlertFormat.ShareCodePrefix, json))).Error);
    }

    [Fact]
    public void Alert_conditions_nest_up_to_the_limit()
    {
        Assert.Single(AlertFormat.FromJson(Alerts(Rule(Nested(ReadLimits.MaxConditionDepth)))));
    }

    [Fact]
    public void An_alerts_file_too_large_to_be_real_is_kept_aside_as_unreadable()
    {
        Directory.CreateDirectory(_folder);
        string file = Path.Combine(_folder, "alerts.json");
        File.WriteAllText(file, Alerts(Rule(Leaf)).Replace("\"message\":\"m\"", $"\"message\":\"{new string('x', ReadLimits.MaxTextBytes)}\""));

        var load = new AlertStore(file).Load();

        Assert.Empty(load.Rules);
        Assert.NotNull(load.RenamedUnreadable);
    }
}
