using EndHud.Vision;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>When the combo-ready hint shows, from tracked readings.</summary>
public class ComboHintsTests
{
    private static readonly Frame NoPortrait = new(1, 1, new byte[4]);

    private static PartyState Combat(params ComboReading?[] combos) => new(
        combos.Select((c, i) => new PartySlot(i + 1, null, 1, "", NoPortrait, c)).ToList(), 100, "", PanelMarks: 8);

    private static readonly ComboReading Ready = new(1, true);
    private static ComboReading Cooling(double progress) => new(progress, false);
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    [Fact]
    public void Shows_for_a_few_seconds_when_the_cooldown_finishes()
    {
        var hints = new ComboHints { ShowFor = S(3) };
        Assert.Empty(hints.Update(Combat(Ready, Ready, Ready, Ready), S(0))); // ready from the start: no hint
        Assert.Empty(hints.Update(Combat(Cooling(0.2), Ready, Ready, Ready), S(1)));
        Assert.Empty(hints.Update(Combat(Cooling(0.7), Ready, Ready, Ready), S(2)));
        Assert.Equal([1], hints.Update(Combat(Ready, Ready, Ready, Ready), S(3)));
        Assert.Equal([1], hints.Update(Combat(Ready, Ready, Ready, Ready), S(5.9)));
        Assert.Empty(hints.Update(Combat(Ready, Ready, Ready, Ready), S(6)));
    }

    [Fact]
    public void A_cooldown_refreshed_by_a_skill_jumps_straight_to_ready_and_still_counts()
    {
        var hints = new ComboHints();
        hints.Update(Combat(Cooling(0.3), Ready, Ready, Ready), S(0));
        hints.Update(Combat(Cooling(0.4), Ready, Ready, Ready), S(1));

        Assert.Equal([1], hints.Update(Combat(Ready, Ready, Ready, Ready), S(2)));
    }

    [Fact]
    public void A_single_cooling_reading_between_ready_ones_is_not_trusted()
    {
        // As the HUD fades back in, a ready bar is briefly drawn as an empty track.
        var hints = new ComboHints();
        hints.Update(Combat(Ready, Ready, Ready, Ready), S(0));
        hints.Update(Combat(Ready, Ready, Cooling(0), Ready), S(1));

        Assert.Empty(hints.Update(Combat(Ready, Ready, Ready, Ready), S(2)));
    }

    [Fact]
    public void Re_checking_the_same_reading_on_a_timer_ends_the_hint_on_time_and_counts_nothing_twice()
    {
        var hints = new ComboHints { ShowFor = S(3) };
        var cooling = Combat(Cooling(0.5), Ready, Ready, Ready);
        hints.Update(cooling, S(0));
        hints.Update(cooling, S(0.5)); // the same reading again: still only one cooling reading
        var ready = Combat(Ready, Ready, Ready, Ready);
        Assert.Empty(hints.Update(ready, S(1)));

        hints.Update(Combat(Cooling(0.1), Ready, Ready, Ready), S(10));
        hints.Update(Combat(Cooling(0.2), Ready, Ready, Ready), S(11));
        ready = Combat(Ready, Ready, Ready, Ready);
        Assert.Equal([1], hints.Update(ready, S(12)));
        Assert.Equal([1], hints.Update(ready, S(14.5)));
        Assert.Empty(hints.Update(ready, S(15)));
    }

    [Fact]
    public void Always_mode_shows_every_ready_combo_in_combat()
    {
        var hints = new ComboHints { Mode = ComboHintMode.WhileReady };

        Assert.Equal([2, 3, 4], hints.Update(Combat(Cooling(0.5), Ready, Ready, Ready), S(0)));
        Assert.Equal([1, 2, 3, 4], hints.Update(Combat(Ready, Ready, Ready, Ready), S(100)));
    }

    [Fact]
    public void No_hint_out_of_combat_for_the_fallen_or_once_the_panel_is_gone()
    {
        var hints = new ComboHints { Mode = ComboHintMode.WhileReady };
        var combat = Combat(Ready, Ready, Ready, Ready);

        // Out of combat: no combo bar, the level shows instead.
        var exploring = combat with { Slots = combat.Slots.Select(s => s with { Combo = null, Level = 90 }).ToList() };
        Assert.Empty(hints.Update(exploring, S(0)));
        // Fallen.
        Assert.Equal([2, 3, 4], hints.Update(combat with { Slots = [combat.Slots[0] with { Dead = true }, .. combat.Slots.Skip(1)] }, S(1)));
        // The panel kept through one missed reading (a cutscene), but not through more (a menu).
        Assert.Equal([2, 3, 4], hints.Update(combat with { Slots = [combat.Slots[0] with { Dead = true }, .. combat.Slots.Skip(1)], ReadingsSinceSeen = 1 }, S(2)));
        Assert.Empty(hints.Update(combat with { ReadingsSinceSeen = 2 }, S(3)));
    }

    [Fact]
    public void A_cooldown_that_finishes_during_a_cutscene_is_hinted_when_the_panel_is_back()
    {
        var hints = new ComboHints();
        hints.Update(Combat(Cooling(0.5), Ready, Ready, Ready), S(-1));
        var cooling = Combat(Cooling(0.6), Ready, Ready, Ready);
        hints.Update(cooling, S(0));
        for (int missed = 1; missed <= 3; missed++)
            Assert.Empty(hints.Update(cooling with { ReadingsSinceSeen = missed }, S(missed)));

        Assert.Equal([1], hints.Update(Combat(Ready, Ready, Ready, Ready), S(4)));
    }

    [Fact]
    public void A_new_fight_starts_over()
    {
        var hints = new ComboHints();
        hints.Update(Combat(Cooling(0.4), Ready, Ready, Ready), S(-1));
        hints.Update(Combat(Cooling(0.5), Ready, Ready, Ready), S(0));
        var exploring = Combat(null, null, null, null);
        exploring = exploring with { Slots = exploring.Slots.Select(s => s with { Level = 90 }).ToList() };
        hints.Update(exploring, S(10));

        // Ready when the next fight starts: it didn't come off cooldown in this fight.
        Assert.Empty(hints.Update(Combat(Ready, Ready, Ready, Ready), S(20)));
    }

    // ---- Recordings, read once a second the way the HUD reads them ----

    private static async Task<List<(int Time, int Slot)>> HintsStartingIn(string recording)
    {
        var video = await VideoFile.OpenAsync(Recordings.PathOf(recording));
        var reader = new PartyReader(new TextRecognizer());
        var tracker = new PartyTracker();
        var hints = new ComboHints();
        var shown = new HashSet<int>();
        var starts = new List<(int, int)>();
        for (int t = 0; t < (int)video.Duration.TotalSeconds; t++)
        {
            var now = hints.Update(tracker.Track(await reader.ReadAsync(await video.GetFrameAsync(S(t)))), S(t));
            starts.AddRange(now.Where(slot => !shown.Contains(slot)).Select(slot => (t, slot)));
            shown = [.. now];
        }
        return starts;
    }

    [Fact]
    public async Task Combat_recording_hints_exactly_when_each_cooldown_finishes()
    {
        // Slot 1's combo refills over 13–21 s and is ready at 22 s; slot 3's over 11–24 s, ready at 25 s.
        Assert.Equal([(22, 1), (25, 3)], await HintsStartingIn(Recordings.Combat));
    }

    [Fact]
    public async Task Battle_hints_every_finished_cooldown_including_during_cutscenes()
    {
        // Read off the recording: each (time, slot) is the first reading with that slot's combo ready
        // after it was seen cooling. 81 s and 87 s follow ultimate cutscenes (78–80 s, 85–86 s)
        // during which the cooldowns finished.
        Assert.Equal(
            [(32, 1), (34, 2), (34, 3), (37, 4), (43, 1), (56, 1), (63, 4), (81, 1), (81, 3), (83, 2), (87, 4), (97, 1)],
            await HintsStartingIn(Recordings.BattleFight1));
    }

    [Fact]
    public async Task Battle_no_hint_for_operators_who_fall_while_cooling()
    {
        // Slots 3 and 4 fall mid-cooldown (55–57 s); only finished cooldowns are hinted.
        Assert.Equal([(27, 3), (56, 1)], await HintsStartingIn(Recordings.BattleFight2));
    }
}
