using EndHud.Vision.Alerts;
using EndHud.Vision.Rotations;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// The alert rules' file (<see cref="AlertStore"/>), every way a rule changes: create (save), read
/// (load), correct (rewritten in place under its id), remove (only that rule), protect (an unreadable
/// file renamed, never lost; a newer file never overwritten; imports never replace; at most 100),
/// carry (share codes and files).
/// </summary>
public sealed class AlertStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "endhud-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private string FilePath => Path.Combine(_folder, "alerts.json");

    private static AlertRule SkillPoints(double value, string message) => AlertRule.New([new SkillPointsCondition(Comparison.AtLeast, value)], message);

    private static AlertRule CryoThree() => AlertRule.New([new BossStatusCondition("Cryo Infliction", true, 3)], "Freeze ready");

    [Fact]
    public void Saves_and_reads_back()
    {
        var store = new AlertStore(FilePath);
        AlertRule[] rules = [SkillPoints(1.8, "Cast now"), CryoThree()];

        store.Save(rules);
        var load = new AlertStore(FilePath).Load();

        Assert.Equal(rules, load.Rules);
        Assert.Null(load.RenamedUnreadable);
        Assert.False(load.Newer);
        Assert.Contains("\"format\": 1", File.ReadAllText(FilePath));
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Fact]
    public void A_missing_file_is_no_rules()
    {
        var load = new AlertStore(FilePath).Load();

        Assert.Empty(load.Rules);
        Assert.Null(load.RenamedUnreadable);
        Assert.False(load.Newer);
    }

    [Fact]
    public void Correcting_keeps_the_id_and_rewrites()
    {
        var store = new AlertStore(FilePath);
        var first = SkillPoints(1.8, "Cast now");
        var second = CryoThree();
        store.Save([first, second]);

        var corrected = first with { When = [new SkillPointsCondition(Comparison.AtLeast, 2.0)], Message = "Cast" };
        store.Save(AlertStore.Corrected(store.Load().Rules, corrected));
        var rules = store.Load().Rules;

        Assert.Equal(2, rules.Count);
        Assert.Equal(corrected, rules[0]);           // in its place, same id
        Assert.Equal(first.Id, rules[0].Id);
        Assert.Equal(second, rules[1]);
    }

    [Fact]
    public void Deleting_removes_only_that_rule()
    {
        var store = new AlertStore(FilePath);
        var a = SkillPoints(1.8, "a");
        var b = SkillPoints(2.5, "b");
        var c = CryoThree();
        store.Save([a, b, c]);

        store.Save(AlertStore.Removed(store.Load().Rules, b.Id));

        Assert.Equal([a, c], store.Load().Rules);
    }

    [Fact]
    public void An_unreadable_file_is_renamed_not_lost()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(FilePath, "{ this is not json");

        var store = new AlertStore(FilePath);
        var load = store.Load();

        Assert.Empty(load.Rules);
        Assert.NotNull(load.RenamedUnreadable);
        // A later load (the editor, opened after the overlay's) can still say where the old rules went.
        Assert.Null(store.Load().RenamedUnreadable);
        Assert.Equal(load.RenamedUnreadable, store.RenamedEarlier);
        Assert.StartsWith("alerts.unreadable-", Path.GetFileName(load.RenamedUnreadable));
        Assert.Equal("{ this is not json", File.ReadAllText(load.RenamedUnreadable!));
        Assert.False(File.Exists(FilePath));
        // Starting afresh works, and the renamed copy stays.
        new AlertStore(FilePath).Save([CryoThree()]);
        Assert.True(File.Exists(load.RenamedUnreadable));
    }

    [Fact]
    public void A_newer_file_is_left_alone_and_save_is_refused()
    {
        Directory.CreateDirectory(_folder);
        const string newer = "{ \"format\": 4, \"rules\": [] }";
        File.WriteAllText(FilePath, newer);
        var store = new AlertStore(FilePath);

        var load = store.Load();

        Assert.True(load.Newer);
        Assert.Empty(load.Rules);
        Assert.Null(load.RenamedUnreadable);
        Assert.Throws<InvalidOperationException>(() => store.Save([CryoThree()]));
        // Even a store that never loaded it doesn't overwrite it.
        Assert.Throws<InvalidOperationException>(() => new AlertStore(FilePath).Save([CryoThree()]));
        Assert.Equal(newer, File.ReadAllText(FilePath));
    }

    [Fact]
    public void Import_never_replaces_and_skips_duplicates()
    {
        var mine = SkillPoints(1.8, "Cast now");
        AlertRule[] current = [mine];
        // The same rule shared back (same id even), a copy of it under another id, and a new one.
        AlertRule[] incoming = [mine, mine with { Id = "other" }, CryoThree()];

        var import = AlertStore.Import(current, incoming);

        Assert.Equal(1, import.Added);
        Assert.Equal(2, import.Duplicates);
        Assert.Equal(0, import.OverLimit);
        Assert.Equal(2, import.Rules.Count);
        Assert.Equal(mine, import.Rules[0]);                         // untouched
        Assert.True(import.Rules[1].SameAs(incoming[2]));
        Assert.NotEqual(incoming[2].Id, import.Rules[1].Id);         // a new id: never collides with one here
    }

    [Fact]
    public void Import_stops_at_100()
    {
        var current = Enumerable.Range(0, 98).Select(i => SkillPoints(i / 100.0, $"r{i}")).ToList();
        var incoming = Enumerable.Range(0, 5).Select(i => SkillPoints(2 + i / 10.0, $"new{i}")).ToList();

        var import = AlertStore.Import(current, incoming);

        Assert.Equal(AlertFile.MaxRules, import.Rules.Count);
        Assert.Equal(2, import.Added);
        Assert.Equal(3, import.OverLimit);
        Assert.Throws<InvalidOperationException>(() => new AlertStore(FilePath).Save([.. import.Rules, CryoThree()]));
    }

    [Fact]
    public void Share_code_round_trips_a_nested_any_with_stacks()
    {
        var rule = AlertRule.New(
        [
            new AnyCondition([new BossStatusCondition("Cryo Infliction", true, 3), new AllCondition([new SkillPointsCondition(Comparison.Below, 1.5), new NotCondition(new UltimateCondition("Laevatain", true))])]),
            new BossStatusCondition("Corrosion", true, 2),
        ], "Freeze ready, all four");

        string code = AlertFormat.ToShareCode([rule]);

        Assert.StartsWith("EHA1:", code);
        Assert.Equal([rule], AlertFormat.FromShareCode(code));
        Assert.Equal([rule], AlertFormat.FromShareCode(code[..20] + "\n " + code[20..])); // wrapped by a chat
        Assert.Equal([rule], AlertFormat.FromJson(AlertFormat.ToJson([rule])));
    }

    /// <summary>
    /// "Phase played" (owner, 2026-10-23) is offered in alerts: a rule using it is written at alerts
    /// format 2, so a build from before refuses it as "newer" instead of renaming the file as
    /// unreadable; rules without it stay at format 1. The code keeps its "EHA1:" prefix (the code's own version).
    /// </summary>
    [Fact]
    public void A_phase_played_rule_round_trips_at_format_2_and_others_stay_at_1()
    {
        var played = AlertRule.New([new NotCondition(new PhasePlayedCondition("charge", Comparison.AtLeast, 2)), new SkillPointsCondition(Comparison.AtLeast, 2)], "");

        string code = AlertFormat.ToShareCode([played]);

        Assert.StartsWith("EHA1:", code);
        Assert.Equal([played], AlertFormat.FromShareCode(code));
        Assert.Contains("\"format\": 2", AlertFormat.ToJson([played, CryoThree()]));
        Assert.Contains("\"kind\": \"phasePlayed\"", AlertFormat.ToJson([played]));
        Assert.Contains("\"format\": 1", AlertFormat.ToJson([CryoThree()]));
        Assert.Equal([played], AlertFormat.FromJson(AlertFormat.ToJson([played])));
        Assert.Empty(played.Problems());
        Assert.Equal(AlertReadError.NewerFormat, Assert.Throws<AlertReadException>(() => AlertFormat.FromJson("{ \"format\": 4, \"rules\": [] }")).Error);
    }

    /// <summary>An alerts file written before "phase played" (format 1) loads as it was.</summary>
    [Fact]
    public void A_format_1_file_still_loads()
    {
        const string json = """{ "format": 1, "rules": [ { "id": "a", "when": [ { "kind": "skillPoints", "is": "AtLeast", "value": 1.8 } ], "message": "可以放战技了" } ] }""";

        var rule = Assert.Single(AlertFormat.FromJson(json));

        Assert.Equal(new AlertRule("a", [new SkillPointsCondition(Comparison.AtLeast, 1.8)], "可以放战技了"), rule);
    }

    [Fact]
    public void A_damaged_code_is_refused()
    {
        string code = AlertFormat.ToShareCode([CryoThree()]);

        Assert.Equal(AlertReadError.DamagedShareCode, Assert.Throws<AlertReadException>(() => AlertFormat.FromShareCode(code[..^6])).Error);
        Assert.Equal(AlertReadError.NewerFormat, Assert.Throws<AlertReadException>(() => AlertFormat.FromShareCode("EHA2:abc")).Error);
        Assert.Equal(AlertReadError.NotAlerts, Assert.Throws<AlertReadException>(() => AlertFormat.FromShareCode("EHR1:abc")).Error);
        Assert.Equal(AlertReadError.NotAlerts, Assert.Throws<AlertReadException>(() => AlertFormat.FromShareCode("hello")).Error);
        // A rotation file is not alert rules.
        Assert.Equal(AlertReadError.NotAlerts, Assert.Throws<AlertReadException>(() => AlertFormat.FromJson("{ \"format\": 4, \"id\": \"x\", \"phases\": [] }")).Error);
    }

    [Fact]
    public void A_rule_names_its_problems()
    {
        Assert.Equal([AlertProblem.NoConditions], AlertRule.New([], "x").Problems());
        Assert.Equal([AlertProblem.EmptyConditionGroup], AlertRule.New([new AnyCondition([])], "x").Problems());
        // "Phase shown" names a phase of the rotation followed, like "phase played" (owner, 2026-10-24: the same full set everywhere).
        Assert.Empty(AlertRule.New([new NotCondition(new PhaseShownCondition("p", Comparison.AtLeast, 1))], "x").Problems());
        Assert.Empty(CryoThree().Problems());
    }
}
