using EndHud.Vision;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>The gameplay recordings in Recordings/ (HUD areas only; see Recordings/README.md).</summary>
public static class Recordings
{
    /// <summary>33 s of combat: slot 1 uses its ultimate (~9 s), combo cooldowns, slot 1 takes a hit.</summary>
    public const string Combat = "combat.mp4";

    /// <summary>38 s: slot 1's ultimate charging (1% → ~44%), slot 4's combo refilling, two operators hurt.</summary>
    public const string UltimateCharging = "ultimate-charging.mp4";

    /// <summary>13 s out of combat over dark portraits and colourful scenery; slot 1's ultimate at ~44%.</summary>
    public const string OutOfCombat = "out-of-combat.mp4";

    /// <summary>
    /// First fight of a full battle (110 s, from the end of the menus): low HP with pink frames,
    /// ultimate cutscenes, a white-haired operator at 0–5% HP.
    /// </summary>
    public const string BattleFight1 = "battle-fight1.mp4";

    /// <summary>
    /// Second fight (78 s, from the loading screen): the squad dimmed with F1–F4 labels, heals,
    /// operators falling one by one and a full wipe.
    /// </summary>
    public const string BattleFight2 = "battle-fight2.mp4";

    /// <summary>
    /// 30 s at 30 fps (the battle-skill cast flash lasts 2-10 frames): a loading screen, then a fight of Si, Last_Rite,
    /// Tangtang and Arcane; scene light behind slots 2-4 (~14.4 s), Tangtang's battle skill (slot 3, ~16.4 s). Its casts,
    /// labelled by eye, are in <see cref="BattleCastsLabels"/>.
    /// </summary>
    public const string BattleCasts = "battle-casts-30fps.mp4";

    /// <summary>The battle skills cast in <see cref="BattleCasts"/>: lines "seconds,slot".</summary>
    public const string BattleCastsLabels = "battle-casts-30fps.casts.txt";

    /// <summary>
    /// The tests that decode <see cref="BattleCasts"/> frame by frame run on their own, after the others
    /// (<see cref="ThirtyFpsCollection"/>): beside the other recording tests, with the game open on the same PC, the
    /// video decoder ran out of memory (OutOfMemoryException from GetThumbnailAsync, in those tests and the others).
    /// </summary>
    public const string ThirtyFps = "30 fps recording";

    public static string PathOf(string recording) => Path.Combine(AppContext.BaseDirectory, "Recordings", recording);
}

/// <summary>
/// Whole recordings, sampled through time: values must change the way the game showed them
/// changing, not just be right on a few hand-picked frames.
/// </summary>
public class RecordedVideoTests
{
    private sealed record Sample(double Time, PartyState State);

    private static readonly Dictionary<string, Lazy<Task<List<Sample>>>> Cache = new()
    {
        [Recordings.Combat] = new(() => ReadEverySecondAsync(Recordings.Combat, tracked: false)),
        // Read as the HUD shows it (tracker included): the charge is smoothed over readings.
        [Recordings.UltimateCharging] = new(() => ReadEverySecondAsync(Recordings.UltimateCharging, tracked: true)),
        // The battle is read the way the HUD reads it: through the tracker that carries deaths
        // and unreadable values across readings.
        [Recordings.BattleFight1] = new(() => ReadEverySecondAsync(Recordings.BattleFight1, tracked: true)),
        [Recordings.BattleFight2] = new(() => ReadEverySecondAsync(Recordings.BattleFight2, tracked: true)),
    };

    private static async Task<List<Sample>> ReadEverySecondAsync(string recording, bool tracked)
    {
        var video = await VideoFile.OpenAsync(Recordings.PathOf(recording));
        var reader = new PartyReader(new TextRecognizer());
        var tracker = new PartyTracker();
        var samples = new List<Sample>();
        for (int t = 0; t < (int)video.Duration.TotalSeconds; t++)
        {
            var state = await reader.ReadAsync(await video.GetFrameAsync(TimeSpan.FromSeconds(t)));
            samples.Add(new Sample(t, tracked ? tracker.Track(state) : state));
        }
        return samples;
    }

    private static Task<List<Sample>> SamplesOf(string recording) => Cache[recording].Value;

    private static IEnumerable<Sample> Between(List<Sample> samples, double from, double to) =>
        samples.Where(s => s.Time >= from && s.Time <= to && s.State.PanelVisible);

    // ---- Combat ----

    [Fact]
    public async Task Combat_squad_panel_is_found_except_during_transition_and_cutscene()
    {
        var samples = await SamplesOf(Recordings.Combat);
        // 2 s is a scene transition, 9–10 s the full-screen ultimate animation (no HUD).
        var missing = samples.Where(s => !s.State.PanelVisible).Select(s => s.Time).ToList();
        Assert.All(missing, t => Assert.Contains(t, new double[] { 2, 9, 10 }));
    }

    [Fact]
    public async Task Combat_hp_of_slot_1_drops_once_and_stays_down()
    {
        var samples = await SamplesOf(Recordings.Combat);
        // The hit lands between 27 and 28 s.
        Assert.All(Between(samples, 3, 27), s => Assert.InRange(s.State.Slots[0].Hp!.Value, 0.97, 1.0));
        Assert.All(Between(samples, 28, 33), s => Assert.InRange(s.State.Slots[0].Hp!.Value, 0.80, 0.86));
        Assert.All(Between(samples, 3, 33), s => Assert.All(s.State.Slots.Skip(1), slot => Assert.InRange(slot.Hp!.Value, 0.97, 1.0)));
    }

    [Fact]
    public async Task Combat_combo_cooldowns_refill_steadily_then_become_ready()
    {
        var samples = await SamplesOf(Recordings.Combat);
        AssertRefills(samples, slot: 0, cooling: (13, 21), readyFrom: 22);
        AssertRefills(samples, slot: 2, cooling: (11, 24), readyFrom: 25);
    }

    [Fact]
    public async Task Combat_ultimates_all_ready_until_slot_1_uses_its_own()
    {
        var samples = await SamplesOf(Recordings.Combat);
        Assert.All(Between(samples, 3, 8), s => Assert.All(s.State.Slots, slot => Assert.True(slot.Ultimate!.Ready)));
        var after = Between(samples, 12, 33).ToList();
        Assert.All(after, s => Assert.False(s.State.Slots[0].Ultimate?.Ready ?? false));
        Assert.All(after, s => Assert.InRange(s.State.Slots[0].Ultimate?.Progress ?? 0, 0, 0.03));
        Assert.All(after, s => Assert.All(s.State.Slots.Skip(1), slot => Assert.True(slot.Ultimate?.Ready ?? true)));
        Assert.True(after.Count(s => s.State.Slots[0].Ultimate is not null) >= after.Count * 0.85,
            "slot 1's charging ultimate should be readable most of the time");
    }

    // ---- Ultimate charging ----

    [Fact]
    public async Task Charging_ultimate_of_slot_1_only_grows_and_reaches_about_45_percent()
    {
        var samples = await SamplesOf(Recordings.UltimateCharging);
        var charge = Between(samples, 2, 37)
            .Select(s => (s.Time, Reading: s.State.Slots[0].Ultimate))
            .Where(r => r.Reading is not null)
            .ToList();
        Assert.True(charge.Count >= 25, $"slot 1's ultimate readable in only {charge.Count} of 36 samples");
        Assert.All(charge, r => Assert.False(r.Reading!.Ready));
        // Small and slowly growing at first; a jump around 31 s, which the HUD's median of the
        // last 3 readings shows two readings later; never shrinking (beyond noise).
        Assert.All(charge.Where(r => r.Time <= 30), r => Assert.InRange(r.Reading!.Progress, 0, 0.10));
        Assert.All(charge.Where(r => r.Time >= 33), r => Assert.InRange(r.Reading!.Progress, 0.37, 0.50));
        double highest = 0;
        foreach (var (time, reading) in charge)
        {
            Assert.True(reading!.Progress >= highest - 0.04, $"charge fell from {highest:P0} to {reading.Progress:P0} at {time} s");
            highest = Math.Max(highest, reading.Progress);
        }
    }

    [Fact]
    public async Task Charging_other_ultimates_stay_ready()
    {
        var samples = await SamplesOf(Recordings.UltimateCharging);
        Assert.All(Between(samples, 2, 37), s => Assert.All(s.State.Slots.Skip(1), slot => Assert.True(slot.Ultimate?.Ready ?? true)));
    }

    [Fact]
    public async Task Charging_combo_of_slot_4_refills_after_use()
    {
        var samples = await SamplesOf(Recordings.UltimateCharging);
        AssertRefills(samples, slot: 3, cooling: (23, 37), readyFrom: 99);
        var cooling = Between(samples, 23, 37).Select(s => s.State.Slots[3].Combo!.Progress).ToList();
        Assert.InRange(cooling.First(), 0.08, 0.20);
        Assert.InRange(cooling.Last(), 0.88, 0.98);
    }

    [Fact]
    public async Task Charging_two_operators_are_hurt_at_about_20_seconds()
    {
        var samples = await SamplesOf(Recordings.UltimateCharging);
        Assert.All(Between(samples, 3, 18), s => Assert.All(s.State.Slots, slot => Assert.InRange(slot.Hp!.Value, 0.97, 1.0)));
        Assert.All(Between(samples, 22, 37), s =>
        {
            Assert.InRange(s.State.Slots[0].Hp!.Value, 0.85, 0.91);
            Assert.InRange(s.State.Slots[1].Hp!.Value, 0.87, 0.93);
            Assert.InRange(s.State.Slots[2].Hp!.Value, 0.97, 1.0);
            Assert.InRange(s.State.Slots[3].Hp!.Value, 0.97, 1.0);
        });
    }

    // ---- Out of combat (the "combo cooling 0%" bug) ----

    [Fact]
    public async Task Out_of_combat_no_combo_bar_is_ever_reported()
    {
        // Every 0.25 s: the rows above each HP bar show portrait art and shadow, never a bar.
        var video = await VideoFile.OpenAsync(Recordings.PathOf(Recordings.OutOfCombat));
        for (double t = 5; t < 13; t += 0.25)
        {
            var frame = await video.GetFrameAsync(TimeSpan.FromSeconds(t));
            var ui = new UiScaler(frame.Width, frame.Height);
            for (int slot = 0; slot < 4; slot++)
            {
                var reading = ComboBar.Measure(frame, ui.BottomLeft(PartyLayout.ForSlot(PartyLayout.ComboBar, slot)));
                Assert.True(reading is null, $"slot {slot + 1} read a combo bar ({reading?.Progress:P0}) at {t:F2} s, out of combat");
            }
        }
    }

    [Fact]
    public async Task Out_of_combat_ultimate_charge_holds_steady_over_changing_scenery()
    {
        var video = await VideoFile.OpenAsync(Recordings.PathOf(Recordings.OutOfCombat));
        for (double t = 5; t < 12; t += 0.25)
        {
            var frame = await video.GetFrameAsync(TimeSpan.FromSeconds(t));
            if (UltimateGauge.Measure(frame, 0) is not { } reading) continue;
            Assert.False(reading.Ready, $"slot 1 read as ready at {t:F2} s");
            Assert.True(reading.Progress is >= 0.40 and <= 0.48, $"slot 1 charge read as {reading.Progress:P0} at {t:F2} s (it's ~44%)");
        }
    }

    // ---- Full battle (two fights) ----

    [Fact]
    public async Task Battle_panel_stays_on_show_once_seen_and_not_before()
    {
        var fight1 = await SamplesOf(Recordings.BattleFight1);
        var fight2 = await SamplesOf(Recordings.BattleFight2);
        // The end of the menus before the first fight: nothing has been read yet.
        Assert.All(fight1.Where(s => s.Time <= 4), s => Assert.False(s.State.PanelVisible, $"panel reported at {s.Time} s (menus)"));
        // From the first reading on, the last one stays on show through HUD fades and the
        // full-screen ultimate cutscenes (~65, 74, 79, 85 s). Each recording is read with a fresh
        // tracker, and the second opens on a loading screen.
        foreach (var samples in new[] { fight1, fight2 })
        {
            double first = samples.First(s => s.State.PanelVisible).Time;
            Assert.True(first <= 11, $"panel first found at {first} s");
            Assert.All(samples.Where(s => s.Time >= first), s => Assert.True(s.State.PanelVisible, $"panel lost at {s.Time} s"));
        }
        // After the wipe the screen has no panel; the wiped squad stays on show.
        Assert.All(fight2.Where(s => s.Time >= 77), s => Assert.All(s.State.Slots, slot => Assert.True(slot.Dead, $"slot {slot.Number} at {s.Time} s")));
    }

    [Fact]
    public async Task Battle_ultimates_never_go_blank_once_read()
    {
        foreach (var recording in new[] { Recordings.BattleFight1, Recordings.BattleFight2 })
        {
            var samples = await SamplesOf(recording);
            for (int slot = 0; slot < 4; slot++)
            {
                var alive = samples.Where(s => s.State.PanelVisible && !s.State.Slots[slot].Dead).ToList();
                var firstUltimate = alive.FirstOrDefault(s => s.State.Slots[slot].Ultimate is not null);
                if (firstUltimate is null) continue;
                Assert.All(alive.Where(s => s.Time >= firstUltimate.Time),
                    s => Assert.True(s.State.Slots[slot].Ultimate is not null, $"{recording}: slot {slot + 1} ultimate blank at {s.Time} s"));
            }
        }
    }

    [Fact]
    public async Task Battle_operators_fall_one_by_one_and_stay_fallen()
    {
        var fight1 = await SamplesOf(Recordings.BattleFight1);
        var fight2 = await SamplesOf(Recordings.BattleFight2);
        // Nobody falls in the first fight or early in the second (low HP is not death).
        Assert.All(fight1.Concat(Between(fight2, 0, 54)), s => Assert.All(s.State.Slots, slot => Assert.False(slot.Dead, $"slot {slot.Number} dead at {s.Time} s")));
        // Then: slot 4, slot 3, slot 2 and finally slot 1 (the wipe).
        AssertDeadFrom(fight2, slot: 4, from: 56);
        AssertDeadFrom(fight2, slot: 3, from: 57);
        AssertDeadFrom(fight2, slot: 2, from: 68);
        AssertDeadFrom(fight2, slot: 1, from: 73);
        Assert.All(Between(fight2, 55, 72), s => Assert.False(s.State.Slots[0].Dead, $"slot 1 dead at {s.Time} s"));
    }

    [Fact]
    public async Task Battle_low_hp_white_haired_operator_is_not_taken_for_dead()
    {
        // Arcane (slot 3) at 0–5% HP: her white hair sits where the death mark's ring would be.
        var samples = await SamplesOf(Recordings.BattleFight1);
        Assert.All(Between(samples, 104, 108), s => Assert.False(s.State.Slots[2].Dead, $"slot 3 dead at {s.Time} s"));
    }

    [Fact]
    public async Task Battle_combo_bars_are_read_inside_the_low_hp_frame()
    {
        // Slots 2–4 are low on HP (pink frame, tinted bars) and refilling their combo. A reading
        // missed under effects keeps the last value, so the same value may show twice — once.
        // (At 94 s slot 4's bar is unreadable just as it's used, so "ready" is kept for that second.)
        var samples = await SamplesOf(Recordings.BattleFight1);
        foreach (int slot in new[] { 1, 2, 3 })
        {
            var readings = Between(samples, 95, 101).Select(s => s.State.Slots[slot].Combo).ToList();
            Assert.All(readings, c => Assert.True(c is { Ready: false }, $"slot {slot + 1}: combo at low HP was {(c is null ? "missing" : "ready")}"));
            var progress = readings.Select(c => c!.Progress).ToList();
            int repeats = 0;
            for (int i = 1; i < progress.Count; i++)
            {
                Assert.True(progress[i] >= progress[i - 1], $"slot {slot + 1} combo went from {progress[i - 1]:P0} to {progress[i]:P0} at low HP");
                if (progress[i] == progress[i - 1]) repeats++;
            }
            Assert.True(repeats <= 1, $"slot {slot + 1} combo stuck for {repeats} readings at low HP");
        }
    }

    [Fact]
    public async Task Battle_dimmed_squad_and_heals_keep_true_hp()
    {
        // The game dims the squad (F1–F4 labels; darker bars) and a heal sweeps a green glow
        // over slot 1's bar. Neither may read as 0% or as full.
        var samples = await SamplesOf(Recordings.BattleFight2);
        Assert.All(Between(samples, 22, 32), s =>
        {
            Assert.InRange(s.State.Slots[0].Hp!.Value, 0.30, 0.90);
            Assert.InRange(s.State.Slots[1].Hp!.Value, 0.50, 0.80);
            Assert.InRange(s.State.Slots[3].Hp!.Value, 0.55, 0.80);
        });
    }

    private static void AssertDeadFrom(List<Sample> samples, int slot, double from) =>
        Assert.All(Between(samples, from, 75), s => Assert.True(s.State.Slots[slot - 1].Dead, $"slot {slot} not dead at {s.Time} s"));

    // ---- Frame-by-frame: no ultimate flicker ----

    [Fact]
    public Task Combat_ultimate_states_never_flicker() => AssertNoUltimateFlicker(
        Recordings.Combat, allReadyUntil: 8.5, slot1ChargingFrom: 12, end: 33);

    [Fact]
    public Task Charging_ultimate_states_never_flicker() => AssertNoUltimateFlicker(
        Recordings.UltimateCharging, allReadyUntil: null, slot1ChargingFrom: 2.5, end: 37.5);

    /// <summary>
    /// Every 0.25 s — denser than the HUD's once a second: a reading that flips state even for
    /// one frame would show on the HUD. Includes HUD dimming and skill effects sweeping over
    /// the circles. A circle may be unreadable (null), never wrong.
    /// </summary>
    private static async Task AssertNoUltimateFlicker(string recording, double? allReadyUntil, double slot1ChargingFrom, double end)
    {
        var video = await VideoFile.OpenAsync(Recordings.PathOf(recording));
        if (allReadyUntil is { } until)
        {
            for (double t = 3; t < until; t += 0.25)
            {
                var frame = await video.GetFrameAsync(TimeSpan.FromSeconds(t));
                for (int slot = 0; slot < 4; slot++)
                    Assert.True(UltimateGauge.Measure(frame, slot)?.Ready ?? true, $"slot {slot + 1} read as charging at {t:F2} s");
            }
        }
        for (double t = slot1ChargingFrom; t < end; t += 0.25)
        {
            var frame = await video.GetFrameAsync(TimeSpan.FromSeconds(t));
            Assert.False(UltimateGauge.Measure(frame, 0)?.Ready ?? false, $"slot 1 read as ready at {t:F2} s");
            for (int slot = 1; slot < 4; slot++)
                Assert.True(UltimateGauge.Measure(frame, slot)?.Ready ?? true, $"slot {slot + 1} read as charging at {t:F2} s");
        }
    }

    private static void AssertRefills(List<Sample> samples, int slot, (double From, double To) cooling, double readyFrom)
    {
        var progress = Between(samples, cooling.From, cooling.To).Select(s => s.State.Slots[slot].Combo).ToList();
        Assert.NotEmpty(progress);
        Assert.All(progress, c => Assert.True(c is { Ready: false }, $"slot {slot + 1} combo should be cooling, was {(c is null ? "unread" : "ready")}"));
        for (int i = 1; i < progress.Count; i++)
            Assert.True(progress[i]!.Progress > progress[i - 1]!.Progress,
                $"slot {slot + 1} cooldown went from {progress[i - 1]!.Progress:P0} to {progress[i]!.Progress:P0}");
        Assert.All(Between(samples, readyFrom, 99), s => Assert.True(s.State.Slots[slot].Combo!.Ready));
    }
}
