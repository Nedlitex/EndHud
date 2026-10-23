using EndHud.Vision.Operators;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// The counters the gauges show (<see cref="CounterMemory"/>): a counter not read this time (an
/// effect over it, a layout whose portraits aren't read) keeps the last value seen, marked as not
/// live. Read on a portrait, it's live (<see cref="PortraitCounterTests"/>).
/// </summary>
public class CounterMemoryTests
{
    private static readonly Frame NoPortrait = new(1, 1, new byte[4]);

    private static PartyState Reading(string[] squad, params CounterReading[] counters) => new(
        squad.Select((id, i) => new PartySlot(i + 1, null, 1, "", NoPortrait,
            Operator: new OperatorMatch(new OperatorInfo(id, id, id, id + ".png"), 1, true))).ToList(), PanelMarks: 8)
    {
        Counters = counters,
    };

    private static readonly string[] Squad = ["Zhuang_Fangyi", "Typhoeus", "Arcane", "Perlica"];

    private static RememberedCounter Find(IReadOnlyList<RememberedCounter> counters, string id) => counters.Single(c => c.Counter.Id == id);

    [Fact]
    public void Every_counter_of_the_squad_is_listed_in_slot_order_unseen_until_read()
    {
        var counters = new CounterMemory().Update(Reading(Squad));

        Assert.Equal(["sunderblade", "hunting-arrow", "sign"], counters.Select(c => c.Counter.Id));
        Assert.All(counters, c => Assert.Null(c.Value));
        Assert.All(counters, c => Assert.False(c.Live));
    }

    [Fact]
    public void A_counter_read_while_controlled_is_kept_after_switching_away_but_not_live()
    {
        var memory = new CounterMemory();
        var seen = memory.Update(Reading(Squad, new CounterReading("Zhuang_Fangyi", "sunderblade", 7)));
        var afterSwitch = memory.Update(Reading(Squad, new CounterReading("Typhoeus", "hunting-arrow", 4), new CounterReading("Typhoeus", "sign", 2)));

        Assert.Equal((7, true), (Find(seen, "sunderblade").Value, Find(seen, "sunderblade").Live));
        Assert.Equal((7, false), (Find(afterSwitch, "sunderblade").Value, Find(afterSwitch, "sunderblade").Live));
        Assert.Equal((4, true), (Find(afterSwitch, "hunting-arrow").Value, Find(afterSwitch, "hunting-arrow").Live));
    }

    [Fact]
    public void An_unreadable_moment_keeps_the_last_value_not_live()
    {
        var memory = new CounterMemory();
        memory.Update(Reading(Squad, new CounterReading("Zhuang_Fangyi", "sunderblade", 5)));
        var covered = memory.Update(Reading(Squad, new CounterReading("Zhuang_Fangyi", "sunderblade", null)));

        Assert.Equal((5, false), (Find(covered, "sunderblade").Value, Find(covered, "sunderblade").Live));
    }

    [Fact]
    public void An_operator_who_leaves_the_squad_is_forgotten_and_clear_forgets_everyone()
    {
        var memory = new CounterMemory();
        memory.Update(Reading(Squad, new CounterReading("Zhuang_Fangyi", "sunderblade", 9)));
        memory.Update(Reading(["Laevatain", "Typhoeus", "Arcane", "Perlica"]));
        var back = memory.Update(Reading(Squad, new CounterReading("Typhoeus", "sign", 3)));

        Assert.Null(Find(back, "sunderblade").Value);

        memory.Clear();
        Assert.All(memory.Update(Reading(Squad)), c => Assert.Null(c.Value));
    }
}
