using System.Text.Json;
using EndHud.Vision.Operators;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// Counters of the operators who aren't controlled, read on their squad portraits
/// (<see cref="PortraitCounters"/>), on frames labelled by eye (Fixtures/portrait-counters): right or
/// unknown, never another number, and live in the gauge row (<see cref="CounterMemory"/>).
/// </summary>
public class PortraitCounterTests
{
    /// <param name="Value">What the portrait shows; null when no counter is drawn there.</param>
    /// <param name="Unreadable">A flash over it: the value or null, never another number.</param>
    /// <param name="NoBadge">Si's 0 with no badge drawn (her portrait clear), rather than the hollow flower.</param>
    private sealed record CounterLabel(int Slot, string Counter, int? Value, bool Unreadable, bool NoBadge = false);

    /// <param name="Squad">Slot order; "" for an operator without a counter.</param>
    private sealed record Label(string File, string[] Squad, int Active, bool InCombat, CounterLabel[] Counters);

    private static readonly Label[] Labels = JsonSerializer.Deserialize<Label[]>(
        File.ReadAllText(CombatFrameTests.Fixture(Path.Combine("portrait-counters", "labels.json"))),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    private static readonly string[] CounterOperators = ["Si", "Laevatain", "Typhoeus", "Zhuang_Fangyi"];

    // An operator the readers have no counter for (the fixtures' other squad members aren't named).
    private const string NoCounter = "no-counter";

    public static TheoryData<string> Files() => [.. Labels.Select(l => l.File)];

    public static TheoryData<string> FilesInCombat() => [.. Labels.Where(l => l.InCombat).Select(l => l.File)];

    private static Label LabelOf(string file) => Labels.Single(l => l.File == file);

    private static PartyState Named(PartyState state, IReadOnlyList<string> squad) => state with
    {
        Slots = state.Slots.Select((slot, i) =>
        {
            string id = squad[i].Length > 0 ? squad[i] : NoCounter;
            return slot with { Operator = new OperatorMatch(new OperatorInfo(id, id, id, id + ".png"), 1, true) };
        }).ToList(),
    };

    private static async Task<(Frame Frame, PartyState State)> ReadAsync(string file)
    {
        var frame = await ImageFile.LoadAsync(CombatFrameTests.Fixture(Path.Combine("portrait-counters", file)));
        return (frame, await new PartyReader(new TextRecognizer()).ReadAsync(frame));
    }

    [Theory]
    [MemberData(nameof(Files))]
    public async Task Reads_the_counter_on_the_portrait_of_each_operator_who_isnt_controlled(string file)
    {
        var label = LabelOf(file);
        var (frame, raw) = await ReadAsync(file);
        var state = Named(raw, label.Squad);
        Assert.Equal(label.Active, state.ActiveSlot);

        var readings = PortraitCounters.Read(frame, state);

        // Every counter of every operator who isn't controlled, and nothing else (none out of combat).
        Assert.Equal(label.Counters.Select(c => (label.Squad[c.Slot - 1], c.Counter)).Order(), readings.Select(r => (r.OperatorId, r.CounterId)).Order());
        foreach (var c in label.Counters)
        {
            int? value = readings.Single(r => r.OperatorId == label.Squad[c.Slot - 1] && r.CounterId == c.Counter).Value;
            if (c.Unreadable) Assert.True(value is null || value == c.Value, $"{c.Counter} in slot {c.Slot}: read {value}, shows {c.Value?.ToString() ?? "nothing readable"}");
            else Assert.True(value == c.Value, $"{c.Counter} in slot {c.Slot}: read {value?.ToString() ?? "null"}, shows {c.Value?.ToString() ?? "nothing"}");
        }
    }

    /// <summary>
    /// A portrait with no counter on it (an operator without one) read as if its operator had one:
    /// nothing. The game's portraits and the scenery behind them aren't counters. Not Laevatain's
    /// flower: a dark or pink patch of a portrait reads as one, and no outline tells them apart (see
    /// <see cref="PortraitCounters.OutlineShows"/>); her flower is read only on her own portrait,
    /// where it was drawn in every in-combat frame.
    /// </summary>
    [Theory]
    [MemberData(nameof(FilesInCombat))]
    public async Task A_portrait_without_a_counter_reads_as_nothing_for_every_counter_but_the_flower(string file)
    {
        var label = LabelOf(file);
        var (frame, raw) = await ReadAsync(file);
        for (int i = 0; i < label.Squad.Length; i++)
        {
            if (i + 1 == label.Active || label.Squad[i].Length > 0) continue;
            foreach (string op in CounterOperators.Where(o => o != "Laevatain"))
            {
                var squad = label.Squad.Select((s, j) => j == i ? op : "").ToArray();
                var readings = PortraitCounters.Read(frame, Named(raw, squad));
                Assert.All(readings, r => Assert.True(r.Value is null, $"slot {i + 1} as {op}: {r.CounterId} read {r.Value}"));
            }
        }
    }

    /// <summary>
    /// What the overlay does with a reading (PartyMonitor, then the gauge row): the squad's counters
    /// include the switched-out operators', and they are live, not the faded last-seen value.
    /// </summary>
    [Theory]
    [InlineData("laevatain-28s.jpg")]
    [InlineData("si-43s.jpg")]
    [InlineData("typhoeus-43_5s.jpg")]
    [InlineData("zhuang-33s.jpg")]
    public async Task A_switched_out_operators_counter_is_live_in_the_gauge_row(string file)
    {
        var label = LabelOf(file);
        var (frame, raw) = await ReadAsync(file);
        var state = Named(raw, label.Squad);
        var memory = new CounterMemory();

        var counters = memory.Update(state with { Counters = OperatorCounters.ReadSquad(frame, state) });

        foreach (var c in label.Counters)
        {
            var gauge = counters.Single(g => g.OperatorId == label.Squad[c.Slot - 1] && g.Counter.Id == c.Counter);
            Assert.Equal((c.Value, true), (gauge.Value, gauge.Live));
        }
    }

    /// <summary>
    /// Si's badge gone from her portrait in combat is 0 (owner, 2026-10-23): read so where her
    /// portrait shows clearly with no badge on it.
    /// </summary>
    [Theory]
    [MemberData(nameof(SiWithoutBadge))]
    public async Task Si_with_no_badge_on_a_clear_portrait_reads_zero(string file)
    {
        var label = LabelOf(file);
        var (frame, raw) = await ReadAsync(file);

        var si = PortraitCounters.Read(frame, Named(raw, label.Squad)).Single(r => r.OperatorId == "Si");

        Assert.Equal(0, si.Value);
    }

    public static TheoryData<string> SiWithoutBadge() =>
        [.. Labels.Where(l => l.Counters.Any(c => c.Counter == OperatorCounters.SiStacks.Id && c.Value == 0 && c.NoBadge)).Select(l => l.File)];

    /// <summary>
    /// As a fight ends the skill points go first and the level text fades in under the portraits
    /// while the combo bars still show (test_si_background1 76.5 s): no badge then isn't read as 0.
    /// The same clear no-badge frames, the skill-point bar taken away, read unknown.
    /// </summary>
    [Theory]
    [MemberData(nameof(SiWithoutBadge))]
    public async Task No_badge_isnt_zero_once_the_skill_points_are_gone(string file)
    {
        var label = LabelOf(file);
        var (frame, raw) = await ReadAsync(file);

        var si = PortraitCounters.Read(frame, Named(raw, label.Squad) with { SkillPoints = null, SkillPointMax = null }).Single(r => r.OperatorId == "Si");

        Assert.Null(si.Value);
    }

    /// <summary>
    /// A badge whose number can't be read is never taken for no badge: on every frame with a number
    /// on Si's badge, the number blacked out (as a hit's shake or an effect smears it), Si's stacks
    /// read unknown, never 0.
    /// </summary>
    [Fact]
    public async Task A_badge_whose_number_cant_be_read_is_never_zero()
    {
        int tried = 0;
        foreach (var label in Labels.Where(l => l.Counters.Any(c => c.Counter == OperatorCounters.SiStacks.Id && c.Value > 0)))
        {
            var (frame, raw) = await ReadAsync(label.File);
            var state = Named(raw, label.Squad);
            int slot = label.Counters.Single(c => c.Counter == OperatorCounters.SiStacks.Id).Slot;
            var number = PortraitCounters.PlaceOn(frame, state.Layout, slot, OperatorCounters.SiStacks)!.Value.Map(OperatorCounters.SiStacksArea).ClampTo(frame.Width, frame.Height);
            for (int y = number.Y; y < number.Bottom; y++)
            for (int x = number.X; x < number.Right; x++)
            {
                var (r, g, b) = frame.GetPixel(x, y);
                if (Math.Min(r, Math.Min(g, b)) < 140) continue; // only the white digits
                int i = (y * frame.Width + x) * 4;
                frame.Bgra[i] = frame.Bgra[i + 1] = frame.Bgra[i + 2] = 30;
            }

            var si = PortraitCounters.Read(frame, state).Single(r => r.OperatorId == "Si");

            Assert.True(si.Value is null, $"{label.File}: read {si.Value} with the number blacked out");
            tried++;
        }
        Assert.True(tried >= 10, $"{tried} frames");
    }

    [Fact]
    public async Task Nothing_is_read_on_the_portraits_of_a_panel_remembered_while_out_of_sight()
    {
        var label = LabelOf("si-43s.jpg");
        var (frame, raw) = await ReadAsync(label.File);

        Assert.Empty(PortraitCounters.Read(frame, Named(raw, label.Squad) with { ReadingsSinceSeen = 1 }));
    }

    /// <summary>The controller layout hasn't been recorded with a counter on a portrait: nothing is read there.</summary>
    [Fact]
    public async Task Nothing_is_read_on_the_portraits_in_the_controller_layout()
    {
        var frame = await ImageFile.LoadAsync(CombatFrameTests.Fixture(Path.Combine("controller", "fullbattle3-laevatain-two-flames.jpg")));
        var raw = await new PartyReader(new TextRecognizer()).ReadAsync(frame);
        Assert.Equal(HudLayout.Controller, raw.Layout);

        Assert.Empty(PortraitCounters.Read(frame, Named(raw, ["Si", "Laevatain", "", ""])));
    }

    [Fact]
    public void The_fixtures_cover_every_counter_at_zero_and_others_and_si_in_two_slots()
    {
        var shown = Labels.SelectMany(l => l.Counters.Select(c => (c.Counter, c.Value, c.Slot))).ToList();
        foreach (var counter in OperatorCounters.Definitions)
        {
            var values = shown.Where(s => s.Counter == counter.Id && s.Value is not null).Select(s => s.Value).Distinct().ToList();
            Assert.True(values.Count >= 3, $"{counter.Id}: {values.Count} values");
            if (counter != OperatorCounters.Sign) Assert.Contains(0, values);
        }
        Assert.Equal([1, 3], shown.Where(s => s.Counter == OperatorCounters.SiStacks.Id).Select(s => s.Slot).Distinct().Order());
        Assert.Contains(shown, s => s.Value is null); // a counter that can't be told (a fade)
        // Si's 0 with no badge, in both her slots, and stacks to read where she has her badge.
        var noBadge = Labels.SelectMany(l => l.Counters.Where(c => c.NoBadge)).ToList();
        Assert.All(noBadge, c => Assert.Equal((OperatorCounters.SiStacks.Id, (int?)0), (c.Counter, c.Value)));
        Assert.Equal([1, 3], noBadge.Select(c => c.Slot).Distinct().Order());
        Assert.Contains(Labels, l => !l.InCombat);
    }
}
