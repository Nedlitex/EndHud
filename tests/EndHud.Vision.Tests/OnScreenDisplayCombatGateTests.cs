using System.Globalization;
using System.Text.Json;
using EndHud.Vision.Operators;
using Xunit;
using Xunit.Abstractions;

namespace EndHud.Vision.Tests;

/// <summary>
/// The combat half of the regression gate for overlays drawn into the game's frames (MSI Afterburner's on-screen
/// display, drawn by RivaTuner, RTSS; see <see cref="OnScreenDisplayGateTests"/> for the bars and the inn): with the
/// synthetic display (<see cref="SyntheticOsd"/>) at each of its five positions, on its background or not, every combat
/// reading must be the clean frame's or unknown, never another value. A value lost is a frame missed; a wrong one is
/// shown to the player and, the display being static, kept by every filter that waits for readings to agree.
/// <para>
/// Read at 4K and at 1080p over the combat, exploration and HUD fixtures (both layouts): the squad panel (seen, in
/// combat, squad size, the controlled slot, skill points, the time bar; per slot fallen, HP, level, combo, ultimate,
/// battle skill), the mode switch, team buffs, the boss's statuses, the squad's counters and Si's button with the squad
/// known. What the app keeps from a reading follows <see cref="PartyTracker"/>: a reading without the squad panel is not
/// taken, so its slot values count as unknown. Each variant's verdicts are written to the test output ("GATE" lines).
/// </para>
/// </summary>
public class OnScreenDisplayCombatGateTests(ITestOutputHelper output)
{
    private static readonly Lazy<Task<(PartyReader Party, TeamBuffReader Buffs, BossStatusReader Boss)>> Readers = new(async () =>
    {
        string user = Path.Combine(Path.GetTempPath(), "endhud-osd-combat-gate-" + Environment.ProcessId);
        var library = await OperatorCache.LoadAsync(user);
        return (new PartyReader(new TextRecognizer()), new TeamBuffReader(library), new BossStatusReader(library));
    });

    /// <summary>The fixture folders sampled (about ten frames each), besides every top-level frame.</summary>
    private static readonly string[] Folders =
    [
        "battle-hud", "controller", "infliction", "vulnerable", "vulnerable-and-infliction", "reactions", "buffs",
        "portrait-counters", "si-timer-second-cast", "ultimate-time", "lasting-ultimate-background", "panel-or-not",
        "small-squads", "counters", "faded-squad",
    ];

    public static TheoryData<string> CombatFrames()
    {
        var data = new TheoryData<string>();
        foreach (string frame in Frames()) data.Add(frame);
        return data;
    }

    internal static IEnumerable<string> Frames()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        static bool Picture(string f) => f.EndsWith(".jpg", StringComparison.Ordinal) || f.EndsWith(".png", StringComparison.Ordinal);
        foreach (string file in Directory.GetFiles(root).Where(Picture).Order(StringComparer.Ordinal))
            yield return Path.GetFileName(file);
        foreach (string folder in Folders)
        {
            var files = Directory.GetFiles(Path.Combine(root, folder)).Where(Picture).Order(StringComparer.Ordinal).ToList();
            int every = files.Count > 12 ? files.Count / 10 : 1;
            for (int i = 0; i < files.Count; i += every) yield return $"{folder}/{Path.GetFileName(files[i])}";
        }
    }

    [Theory]
    [MemberData(nameof(CombatFrames))]
    public async Task Combat_reads_the_same_or_unknown_under_a_display(string fixture)
    {
        var source = await ImageFile.LoadAsync(CombatFrameTests.Fixture(fixture));
        var wrong = new List<string>();
        foreach (var (size, frame) in new[] { ("4K", source), ("1080p", SimulatedScreen.FullHd.Render(source)) })
        {
            var clean = await ReadAsync(fixture, frame);
            foreach (var position in SyntheticOsd.Positions)
            foreach (bool background in new[] { true, false })
            {
                string variant = $"{size} {position}{(background ? " bg" : "")}";
                var covered = await ReadAsync(fixture, SyntheticOsd.Draw(frame, position, background));
                var (unknown, found) = Compare(clean, covered);
                wrong.AddRange(found.Select(w => $"{variant} {w}"));
                output.WriteLine($"GATE\t{fixture}\t{size}\t{position}\t{(background ? "bg" : "nobg")}\tunknown={unknown}\twrong={found.Count}\t{string.Join("; ", found)}");
            }
        }
        Assert.True(wrong.Count == 0, $"{fixture}: {string.Join("; ", wrong)}");
    }

    /// <summary>Fields that are counts of what was seen rather than values shown: compared as unknown when lost.</summary>
    private static readonly HashSet<string> Signs = ["panel", "combat"];

    /// <summary>How many values became unknown, and the wrong ones ("field: clean read covered").</summary>
    internal static (int Unknown, List<string> Wrong) Compare(Dictionary<string, string?> clean, Dictionary<string, string?> covered)
    {
        int unknown = 0;
        var wrong = new List<string>();
        // A reading the app doesn't take (no squad panel): its squad values are unknown.
        bool taken = covered.GetValueOrDefault("panel") == "True";
        foreach (string field in clean.Keys.Union(covered.Keys).Order(StringComparer.Ordinal))
        {
            string? was = clean.GetValueOrDefault(field), read = covered.GetValueOrDefault(field);
            if (was == read || Close(was, read)) continue;
            if (Signs.Contains(field))
            {
                if (was == "True") unknown++;
                else wrong.Add($"{field}: {was ?? "unknown"} read {read ?? "unknown"}");
                continue;
            }
            if (read is null || (!taken && TakenWithPanel(field)))
            {
                unknown++;
                continue;
            }
            wrong.Add($"{field}: {was ?? "unknown"} read {read}");
        }
        return (unknown, wrong);
    }

    /// <summary>A value <see cref="PartyTracker"/> takes only from a reading that saw the squad panel.</summary>
    private static bool TakenWithPanel(string field) => !field.StartsWith("boss", StringComparison.Ordinal) && field != "mode switch";

    /// <summary>Two fractions within 0.03: the same reading (a pixel's rounding at another size).</summary>
    private static bool Close(string? a, string? b) =>
        double.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out double x)
        && double.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out double y)
        && Math.Abs(x - y) <= 0.03;

    private static string F(double v) => v.ToString("F2", CultureInfo.InvariantCulture);

    /// <summary>Every combat value read, by field; a field absent or null is unknown.</summary>
    internal static async Task<Dictionary<string, string?>> ReadAsync(string fixture, Frame frame)
    {
        var (party, buffs, boss) = await Readers.Value;
        var r = new Dictionary<string, string?>();
        var state = await party.ReadAsync(frame);
        r["panel"] = state.PanelVisible.ToString();
        r["combat"] = state.InCombat.ToString();
        r["mode switch"] = state.ModePill == ModePillReading.NotSeen ? null : state.ModePill.ToString();
        r["layout"] = state.Layout.ToString();
        r["squad size"] = state.SquadSize.ToString(CultureInfo.InvariantCulture);
        r["active"] = state.ActiveSlot?.ToString(CultureInfo.InvariantCulture);
        r["skill points"] = state.SkillPoints is { } sp ? F(sp) : null;
        r["time bar"] = state.UltimateTimeLeft is { } t ? F(t) : null;
        foreach (var s in state.Slots)
        {
            string p = $"slot {s.Number} ";
            if (s.Empty) continue;
            r[p + "fallen"] = s.Dead.ToString();
            r[p + "hp"] = s.Hp is { } hp ? F(hp) : null;
            r[p + "level"] = s.Level?.ToString(CultureInfo.InvariantCulture);
            r[p + "combo"] = s.Combo is { } c ? c.Ready ? "ready" : F(c.Progress) : null;
            r[p + "ultimate"] = s.Ultimate is { } u ? u.Ready ? "ready" : F(u.Progress) : null;
            r[p + "battle skill"] = s.BattleSkill?.ToString();
        }
        if (state.InCombat) r["buffs"] = string.Join(" ", buffs.Read(frame).Select(b => $"{b.Id}:{b.Stacks}"));
        foreach (var st in boss.Read(frame)) r[$"boss {st.Position}"] = $"{st.StatusId}:{st.Stacks}";

        // The counters and Si's button, with the squad known (as once named).
        if (Squad(fixture) is { } squad)
        {
            var named = state with
            {
                Slots = [.. state.Slots.Select(s => squad[s.Number - 1] is { Length: > 0 } id && !s.Empty
                    ? s with { Operator = new OperatorMatch(new OperatorInfo(id, id, id, id + ".png"), 1, true) } : s)],
            };
            foreach (var c in OperatorCounters.ReadSquad(frame, named)) r[$"counter {c.OperatorId} {c.CounterId}"] = c.Value?.ToString(CultureInfo.InvariantCulture);
            foreach (var ring in SkillRing.Read(frame, named))
            {
                r[$"ring {ring.OperatorId}"] = ring.Icon == SkillButtonIcon.Unknown ? null : ring.Icon.ToString();
                r[$"ring {ring.OperatorId} left"] = ring.LeftShare is { } left ? F(left) : null;
            }
        }
        // The counters fixtures black out the squad panel's edge: their controlled operator's counters, read directly.
        if (fixture.StartsWith("counters/", StringComparison.Ordinal))
            foreach (var c in OperatorCounters.Read(frame, fixture.Contains("zhuang", StringComparison.Ordinal) ? "Zhuang_Fangyi" : "Typhoeus"))
                r[$"controlled counter {c.CounterId}"] = c.Value?.ToString(CultureInfo.InvariantCulture);
        return r;
    }

    private static readonly Lazy<Dictionary<string, string[]>> Labelled = new(() =>
    {
        var squads = new Dictionary<string, string[]>();
        string labels = CombatFrameTests.Fixture(Path.Combine("portrait-counters", "labels.json"));
        foreach (var e in JsonDocument.Parse(File.ReadAllText(labels)).RootElement.EnumerateArray())
            squads["portrait-counters/" + e.GetProperty("file").GetString()] = [.. e.GetProperty("squad").EnumerateArray().Select(s => s.GetString() ?? "")];
        return squads;
    });

    /// <summary>The squad in slot order where the fixture's labels say it; null where they don't.</summary>
    private static string[]? Squad(string fixture) =>
        Labelled.Value.TryGetValue(fixture, out var squad) ? squad
        // Si's button in its labels: button 2 (slot 3) on the lae-* frames, button 0 on the si-* ones.
        : fixture.StartsWith("si-timer-second-cast/lae-", StringComparison.Ordinal) ? ["", "", "Si", ""]
        : fixture.StartsWith("si-timer-second-cast/si-", StringComparison.Ordinal) ? ["Si", "", "", ""]
        : null;
}
