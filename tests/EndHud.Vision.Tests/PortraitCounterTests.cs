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
    private sealed record CounterLabel(int Slot, string Counter, int? Value, bool Unreadable);

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
        Assert.Contains(shown, s => s.Value is null); // a portrait with no badge drawn
        Assert.Contains(Labels, l => !l.InCombat);
    }
}
