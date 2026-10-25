using EndHud.Vision;
using EndHud.Vision.Input;
using EndHud.Vision.Rotations;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// Battle skills cast, from the screen: skill points down by about 1 is a cast, the flash on a button says whose;
/// presses only assist. Readings every 0.25 s.
/// </summary>
public sealed class ScreenCastJudgeTests
{
    /// <summary>A reading in battle at <paramref name="Seconds"/>.</summary>
    private sealed record Reading(double Seconds, double? SkillPoints = 2.0)
    {
        public BattleSkillState?[] Skill { get; init; } = [BattleSkillState.Available, BattleSkillState.Available, BattleSkillState.Available, BattleSkillState.Available];
        public bool[] UltimateReady { get; init; } = [false, false, false, false];
        public FlashEvent[] Flashes { get; init; } = [];
        public int Squad { get; init; } = 4;
        public ModePillReading ModePill { get; init; } = ModePillReading.NotSeen;

        public PartyState ToState()
        {
            var slots = Enumerable.Range(0, 4).Select(i => i < Squad
                ? new PartySlot(i + 1, null, 1.0, "", new Frame(1, 1, new byte[4]),
                    Combo: new ComboReading(0.5, false), Ultimate: new UltimateReading(UltimateReady[i], UltimateReady[i] ? 1 : 0.2)) { BattleSkill = Skill[i] }
                : new PartySlot(i + 1, null, null, "", new Frame(1, 1, new byte[4])) { Empty = true }).ToList();
            return new PartyState(slots, PanelMarks: 8) { SkillPoints = SkillPoints, ActiveSlot = 1, ReadAt = TimeSpan.FromSeconds(Seconds), Flashes = Flashes, ModePill = ModePill };
        }
    }

    private static FlashEvent Flash(int slot, double seconds, FlashKind kind = FlashKind.Fill) => new(slot - 1, TimeSpan.FromSeconds(seconds), kind, SkillButtonPlaces.Row);

    private static SkillPress Press(int slot, double seconds) => new(slot, TimeSpan.FromSeconds(seconds));

    /// <summary>Every cast decided over the readings, with the presses made up to each reading.</summary>
    private static List<SkillCast> Judge(IEnumerable<Reading> readings, params SkillPress[] presses)
    {
        var judge = new ScreenCastJudge();
        var casts = new List<SkillCast>();
        PartyState? before = null;
        var queue = new FileSkillPresses(presses);
        foreach (var reading in readings)
        {
            var now = reading.ToState();
            casts.AddRange(judge.Update(before, now, queue.TakeUntil(now.ReadAt!.Value)).Casts);
            before = now;
        }
        return casts;
    }

    /// <summary>Readings from <paramref name="from"/> every 0.25 s for <paramref name="count"/>, points 2.4 then (from <paramref name="dropAt"/>) 1.4.</summary>
    private static IEnumerable<Reading> Fight(double dropAt = 1.0, double from = 0, int count = 12) =>
        Enumerable.Range(0, count).Select(k => from + k * 0.25).Select(t => new Reading(t, t >= dropAt ? 1.4 : 2.4));

    private static IEnumerable<Reading> With(IEnumerable<Reading> readings, double at, Func<Reading, Reading> change) =>
        readings.Select(r => Math.Abs(r.Seconds - at) < 1e-6 ? change(r) : r);

    [Fact]
    public void Points_spent_and_a_flash_on_a_button_is_that_slots_cast()
    {
        var casts = Judge(With(Fight(dropAt: 1.0), 1.0, r => r with { Flashes = [Flash(3, 0.9)] }));
        var cast = Assert.Single(casts);
        Assert.Equal((3, CastSource.Flash, true, true), (cast.Slot, cast.Source, cast.Paid, cast.Certain));
        Assert.Equal(TimeSpan.FromSeconds(0.9), cast.At);
    }

    [Fact]
    public void A_flash_told_after_the_reading_that_saw_the_points_spent_still_names_it()
    {
        // The fill is told 0.17 s after it ends: here a reading after the drop.
        var casts = Judge(With(Fight(dropAt: 1.0), 1.5, r => r with { Flashes = [Flash(2, 0.95)] }));
        var cast = Assert.Single(casts);
        Assert.Equal((2, CastSource.Flash), (cast.Slot, cast.Source));
    }

    [Fact]
    public void A_flash_without_points_spent_is_no_cast()
    {
        var readings = Enumerable.Range(0, 12).Select(k => new Reading(k * 0.25, 2.4 + k * 0.02)).ToList();
        Assert.Empty(Judge(With(readings, 1.0, r => r with { Flashes = [Flash(3, 0.9)] })));
    }

    [Fact]
    public void An_empowered_cast_that_spends_nothing_counts_but_is_not_certain()
    {
        var readings = Enumerable.Range(0, 12).Select(k => new Reading(k * 0.25, 1.98 + k * 0.01)).ToList();
        var flashes = With(With(readings, 1.0, r => r with { Flashes = [Flash(2, 0.8, FlashKind.EmpoweredFill)] }),
            1.5, r => r with { Flashes = [Flash(2, 0.8, FlashKind.EmpoweredCast)] });
        var cast = Assert.Single(Judge(flashes));
        Assert.Equal((2, CastSource.Empowered, false, false), (cast.Slot, cast.Source, cast.Paid, cast.Certain));
    }

    [Fact]
    public void An_empowered_cast_that_spends_a_point_is_one_cast()
    {
        var flashes = With(With(Fight(dropAt: 1.0), 1.0, r => r with { Flashes = [Flash(2, 0.8, FlashKind.EmpoweredFill)] }),
            1.5, r => r with { Flashes = [Flash(2, 0.8, FlashKind.EmpoweredCast)] });
        var cast = Assert.Single(Judge(flashes));
        Assert.Equal((2, CastSource.Flash, true), (cast.Slot, cast.Source, cast.Paid));
    }

    [Fact]
    public void The_glow_starting_is_no_cast()
    {
        var readings = Enumerable.Range(0, 12).Select(k => new Reading(k * 0.25, 2.4)).ToList();
        var flashes = With(With(readings, 1.0, r => r with { Flashes = [Flash(2, 0.8)] }),
            1.5, r => r with { Flashes = [Flash(2, 0.8, FlashKind.EmpoweredStart)] });
        Assert.Empty(Judge(flashes));
    }

    [Fact]
    public void A_press_and_the_flash_of_the_same_slot_are_one_cast()
    {
        var casts = Judge(With(Fight(dropAt: 1.0), 1.25, r => r with { Flashes = [Flash(4, 0.92)] }), Press(4, 0.9));
        var cast = Assert.Single(casts);
        Assert.Equal(4, cast.Slot);
    }

    [Fact]
    public void Points_spent_and_only_a_press_is_the_pressed_slots_cast()
    {
        var cast = Assert.Single(Judge(Fight(dropAt: 1.0), Press(1, 0.9)));
        Assert.Equal((1, CastSource.Press, true, true), (cast.Slot, cast.Source, cast.Paid, cast.Certain));
    }

    [Fact]
    public void A_press_without_points_spent_is_refused_while_the_points_are_read()
    {
        var readings = Enumerable.Range(0, 12).Select(k => new Reading(k * 0.25, 2.4)).ToList();
        Assert.Empty(Judge(readings, Press(1, 0.9)));
    }

    [Fact]
    public void A_press_while_the_points_cant_be_read_is_a_cast_but_not_certain()
    {
        var readings = Enumerable.Range(0, 12).Select(k => new Reading(k * 0.25, k is >= 3 and <= 6 ? null : 2.4)).ToList();
        // The press is judged against the reading at 0.5 s (points read); the readings from 0.75 s to 1.5 s cannot read them.
        var cast = Assert.Single(Judge(readings, Press(2, 0.6)));
        Assert.Equal((2, CastSource.Press, false, false), (cast.Slot, cast.Source, cast.Paid, cast.Certain));
    }

    [Fact]
    public void Points_spent_and_a_button_turned_to_a_countdown_is_that_slots_cast()
    {
        var cooling = new BattleSkillState?[] { BattleSkillState.Available, BattleSkillState.Cooling, BattleSkillState.Available, BattleSkillState.Available };
        var readings = Fight(dropAt: 1.0).Select(r => r.Seconds >= 1.0 ? r with { Skill = cooling } : r);
        var cast = Assert.Single(Judge(readings));
        Assert.Equal((2, CastSource.Cooldown, true), (cast.Slot, cast.Source, cast.Paid));
    }

    [Fact]
    public void Points_spent_and_nothing_saying_whose_is_an_unattributed_cast_after_a_wait()
    {
        var judge = new ScreenCastJudge();
        PartyState? before = null;
        var told = new List<(double At, SkillCast Cast)>();
        foreach (var reading in Fight(dropAt: 1.0))
        {
            var now = reading.ToState();
            foreach (var cast in judge.Update(before, now, []).Casts) told.Add((reading.Seconds, cast));
            before = now;
        }
        var (at, only) = Assert.Single(told);
        Assert.Equal((null, CastSource.Unattributed, true, false), (only.Slot, only.Source, only.Paid, only.Certain));
        Assert.Equal(1.75, at); // waited 0.6 s for a flash: the reading at 1.75 s decides
    }

    [Fact]
    public void A_flash_on_a_slot_spending_its_ultimate_is_not_its_battle_skill()
    {
        var ready = new[] { false, false, true, false };
        var readings = Fight(dropAt: 1.0).Select(r => r.Seconds < 1.0 ? r with { UltimateReady = ready } : r);
        var cast = Assert.Single(Judge(With(readings, 1.0, r => r with { Flashes = [Flash(3, 0.9)] })));
        Assert.Equal(CastSource.Unattributed, cast.Source);
    }

    [Fact]
    public void A_squad_of_three_is_drawn_from_the_right()
    {
        // Buttons 2-4 (from the left) are slots 1-3.
        var readings = Fight(dropAt: 1.0).Select(r => r with { Squad = 3 });
        var cast = Assert.Single(Judge(With(readings, 1.0, r => r with { Flashes = [new FlashEvent(1, TimeSpan.FromSeconds(0.9), FlashKind.Fill, SkillButtonPlaces.Row)] })));
        Assert.Equal(1, cast.Slot);
    }

    /// <summary>The top-left mode switch says out of battle (exploring, building): nothing there is a battle skill cast.</summary>
    [Fact]
    public void Out_of_battle_by_the_mode_switch_nothing_is_a_cast()
    {
        var outside = Fight(dropAt: 1.0).Select(r => r with { ModePill = ModePillReading.OutOfBattle });
        var flashes = With(With(outside, 1.0, r => r with { Flashes = [Flash(2, 0.8, FlashKind.EmpoweredFill), Flash(3, 0.9)] }),
            1.5, r => r with { Flashes = [Flash(2, 0.8, FlashKind.EmpoweredCast)] });

        Assert.Empty(Judge(flashes, Press(1, 0.9)));
    }

    [Fact]
    public void Two_points_spent_at_once_are_two_casts()
    {
        var readings = Enumerable.Range(0, 12).Select(k => k * 0.25).Select(t => new Reading(t, t >= 1.0 ? 0.4 : 2.4));
        var casts = Judge(With(readings, 1.0, r => r with { Flashes = [Flash(1, 0.85), Flash(3, 0.95)] }));
        Assert.Equal([1, 3], casts.Select(c => c.Slot!.Value).Order());
    }

    [Fact]
    public void With_a_controller_the_diamond_keeps_its_places_for_a_squad_of_two()
    {
        // The controller's diamond draws an empty place as a ring with a slash: its second button is slot 2, not slot 4's.
        var readings = Fight(dropAt: 1.0).Select(r => r with { Squad = 2 });
        var cast = Assert.Single(Judge(With(readings, 1.0, r => r with { Flashes = [new FlashEvent(1, TimeSpan.FromSeconds(0.9), FlashKind.Fill, SkillButtonPlaces.DiamondHeld)] })));
        Assert.Equal((2, CastSource.Flash), (cast.Slot, cast.Source));
    }

    [Fact]
    public void With_a_controller_a_flash_counts_though_the_buttons_are_not_read()
    {
        // The controller's buttons aren't read for available or cooling: the "button read before" check is keyboard-only.
        var readings = Fight(dropAt: 1.0).Select(r => r with { Skill = [null, null, null, null] });
        var cast = Assert.Single(Judge(With(readings, 1.0, r => r with { Flashes = [new FlashEvent(2, TimeSpan.FromSeconds(0.9), FlashKind.Fill, SkillButtonPlaces.DiamondHeld)] })));
        Assert.Equal((3, CastSource.Flash), (cast.Slot, cast.Source));
        // With keyboard and mouse an unread button still names nobody (a dimmed one shows the scene through it).
        Assert.Equal(CastSource.Unattributed, Assert.Single(Judge(With(readings, 1.0, r => r with { Flashes = [Flash(3, 0.9)] }))).Source);
    }
}
