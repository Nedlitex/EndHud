using System.Windows;
using System.Windows.Media;
using EndHud.Vision.Input;
using EndHud.Vision.Rotations;
using Xunit;

namespace EndHud.Tests;

/// <summary>
/// What the strip shows: the current phase's steps, then the first two of the phase played next,
/// kept apart from them. Without step marks (the player hasn't turned on reading their
/// battle-skill keys) every step looks the same; with them done steps fade, and the current one is
/// ready or waiting, with why.
/// </summary>
[Collection(WpfCollection.Name)]
public class RotationStripViewTests
{
    private static readonly Rotation Rotation = new(Rotation.CurrentFormat, "r", "r", ["Laevatain", "Camille", "Arcane", "Ardelia"],
    [
        new RotationPhase("charge", "充能轴", [new RotationStep("Ardelia", StepAction.BattleSkill), new RotationStep("Camille", StepAction.Combo), new RotationStep("Arcane", StepAction.Combo)]),
        new RotationPhase("burst", "爆发轴", [new RotationStep("Laevatain", StepAction.Ultimate)], [new UltimateCondition("Laevatain", true)]),
    ]);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Without_step_marks_every_step_is_drawn_alike(bool ready)
    {
        var played = Rotation.Played();
        var position = new RotationPosition(played[1], ready,
            [new ConditionCheck(CheckKind.OwnAbility, null, ready)]);

        var view = RotationStripView.Build(Rotation, position, null, null, (_, _) => null, _ => null, steps: null);

        Assert.Equal("充能轴 I", view.Phase);
        Assert.Equal(3, view.Steps.Count);
        // Then the first two of the next one in the flow (itself again), apart.
        Assert.Equal("充能轴 I", view.Next!.Phase);
        Assert.Equal(2, view.Next.Steps.Count);
        Assert.All(view.Steps.Concat(view.Next.Steps), s => Assert.Equal(StripStepState.Upcoming, s.State));
        Assert.Empty(view.Reasons);
    }

    [Fact]
    public void With_step_marks_done_steps_fade_and_the_current_has_chevrons()
    {
        var played = Rotation.Played();
        var steps = new StepPosition(0, [StepMark.Done, StepMark.Current, StepMark.Upcoming], true, [new ConditionCheck(CheckKind.OwnAbility, null, true)]);

        var view = RotationStripView.Build(Rotation, new RotationPosition(played[0], true, []), null, null, (_, _) => null, _ => null, steps);

        Assert.Equal([StripStepState.Done, StripStepState.Ready, StripStepState.Upcoming], view.Steps.Select(s => s.State));
        Assert.All(view.Next!.Steps, s => Assert.Equal(StripStepState.Upcoming, s.State)); // the next phase's preview is never marked
        Assert.Empty(view.Reasons);
        OnSta(() => RotationStrip.Build(view)); // draws
    }

    [Fact]
    public void A_waiting_step_shows_why()
    {
        var played = Rotation.Played();
        var state = new Vision.PartyState([]) { SkillPoints = 0.4 };
        var steps = new StepPosition(0, [StepMark.Current, StepMark.Upcoming, StepMark.Upcoming], false, [new ConditionCheck(CheckKind.OwnAbility, null, false)]);

        var view = RotationStripView.Build(Rotation, new RotationPosition(played[0], true, []), state, null, (_, _) => null, _ => null, steps);

        Assert.Equal([StripStepState.Waiting, StripStepState.Upcoming, StripStepState.Upcoming], view.Steps.Select(s => s.State));
        // Ardelia's battle skill, the current step: 0.4 of the 1 skill point it needs.
        Assert.Equal([Loc.Format("Rotation.Reason.SkillPoints", "0.4", 1)], view.Reasons);
    }

    [Fact]
    public void A_phase_entered_by_its_condition_is_the_one_shown()
    {
        var played = Rotation.Played();
        var view = RotationStripView.Build(Rotation, new RotationPosition(played[3], true, []), null, null, (_, _) => null, _ => null, steps: null);

        Assert.Equal("爆发轴 II", view.Phase);
        Assert.All(view.Steps, s => Assert.Equal(StripStepState.Upcoming, s.State));
    }

    /// <summary>
    /// A player's report: a phase with a condition and an ordinary phase, both named 充能轴. The strip
    /// showed the first phase's six steps and the second's first two as one row of eight, while the
    /// editor showed six, so the strip looked unlike the rotation they made.
    /// </summary>
    [Fact]
    public void The_next_phases_steps_are_kept_apart_and_named_even_when_the_phases_share_a_name()
    {
        var rotation = new Rotation(Rotation.CurrentFormat, "r", "r", ["Laevatain", "Camille", "Arcane", "Ardelia"],
        [
            new RotationPhase("a", "充能轴", [new RotationStep("Camille", StepAction.BattleSkill), new RotationStep("Arcane", StepAction.Combo)],
                [new UltimateCondition("Laevatain", false)]),
            new RotationPhase("b", "充能轴", [new RotationStep("Laevatain", StepAction.BattleSkill), new RotationStep("Laevatain", StepAction.Combo, WithPrevious: true), new RotationStep("Camille", StepAction.Combo)]),
        ]);

        var view = RotationStripView.ForPhase(rotation, 0, null, (_, _) => null, _ => null);

        Assert.Equal("充能轴 I", view.Phase);
        Assert.Equal(["Rotation.Action.BattleSkill", "Rotation.Action.Combo"], view.Steps.Select(s => s.Action));
        Assert.Equal("充能轴 II", view.Next!.Phase);
        Assert.Equal(["Rotation.Action.BattleSkill", "Rotation.Action.Combo"], view.Next.Steps.Select(s => s.Action));
        Assert.False(view.Next.Steps[0].WithPrevious); // starts the next phase: never overlaps this one's last step
        Assert.True(view.Next.Steps[1].WithPrevious);

        // Drawn after a divider naming it, and dimmed.
        var dimmed = OnSta(() =>
        {
            var strip = RotationStrip.Build(view);
            var texts = Descendants(strip).OfType<System.Windows.Controls.TextBlock>().Select(t => t.Text).ToList();
            Assert.Contains("Rotation.Next", texts);
            Assert.Contains("充能轴 II", texts);
            return Descendants(strip).OfType<UIElement>().Count(e => e.Opacity == RotationStrip.NextOpacity);
        });
        Assert.Equal(2, dimmed);
    }

    /// <summary>
    /// A player couldn't move or size the strip before a fight: it only showed in battle. With the
    /// menu open it shows the squad's rotation, first phase, so it's placed at its real size.
    /// </summary>
    [Fact]
    public void Out_of_battle_the_sample_is_the_squads_rotation_from_its_first_phase()
    {
        var view = RotationStripView.Sample(Rotation, null, (_, _) => null, _ => null);

        Assert.Equal("充能轴 I", view.Phase);
        Assert.Equal(3, view.Steps.Count);
    }

    [Fact]
    public void Without_a_rotation_for_the_squad_the_sample_is_a_generic_strip()
    {
        var view = RotationStripView.Sample(null, null, (_, _) => null, _ => null);

        Assert.Equal("Rotation.Sample", view.Phase);
        Assert.Equal(["Rotation.Action.BattleSkill", "Rotation.Action.Combo", "Rotation.Action.Ultimate"], view.Steps.Select(s => s.Action));
        Assert.Null(view.Next);
        OnSta(() => RotationStrip.Build(view)); // draws
    }

    /// <summary>
    /// A basic attack (format 4) is only drawn: it has no skill icon, so the operator's face fills
    /// the circle (no small face in the corner), under the words "basic attack". It never borrows the battle
    /// skill's icon.
    /// </summary>
    [Fact]
    public void A_basic_attack_step_shows_the_operators_face_and_the_word_basic_attack()
    {
        var library = Operators(["Laevatain", "Camille", "Arcane", "Ardelia"]);
        var face = OnSta(() =>
        {
            var bitmap = new System.Windows.Media.Imaging.WriteableBitmap(2, 2, 96, 96, PixelFormats.Bgra32, null);
            bitmap.Freeze();
            return (System.Windows.Media.Imaging.BitmapSource)bitmap;
        });
        var skill = OnSta(() =>
        {
            var bitmap = new System.Windows.Media.Imaging.WriteableBitmap(3, 3, 96, 96, PixelFormats.Bgra32, null);
            bitmap.Freeze();
            return (System.Windows.Media.Imaging.BitmapSource)bitmap;
        });
        var rotation = new Rotation(Rotation.CurrentFormat, "r", "r", ["Laevatain", "Camille", "Arcane", "Ardelia"],
        [
            new RotationPhase("basic", "Basic attacks", [new RotationStep("Laevatain", StepAction.BasicAttack), new RotationStep("Camille", StepAction.BattleSkill)],
                [new SkillPointsCondition(Comparison.Below, 2)]),
        ]);

        var view = RotationStripView.ForPhase(rotation, 0, library, (_, _) => skill, _ => face);

        var basic = view.Steps[0];
        Assert.Same(face, basic.Icon);
        Assert.Null(basic.Avatar);
        Assert.Equal(Loc.Get("Rotation.Action.BasicAttack"), basic.Action);
        // The battle skill next to it is drawn as before: its icon, the face in the corner.
        Assert.Same(skill, view.Steps[1].Icon);
        Assert.Same(face, view.Steps[1].Avatar);
        OnSta(() => RotationStrip.Build(view)); // draws
    }

    /// <summary>A library of these operators, without any art: enough to find them by id.</summary>
    internal static Vision.Operators.OperatorLibrary Operators(string[] ids)
    {
        string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "endhud-tests", Guid.NewGuid().ToString("N"));
        string reference = System.IO.Path.Combine(folder, "reference");
        System.IO.Directory.CreateDirectory(System.IO.Path.Combine(reference, "icons"));
        System.IO.File.WriteAllText(System.IO.Path.Combine(reference, "operators.json"), System.Text.Json.JsonSerializer.Serialize(
            ids.Select(id => new Vision.Operators.OperatorInfo(id, id, id, id + ".png"))));
        byte[] pixel = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
        foreach (string id in ids) System.IO.File.WriteAllBytes(System.IO.Path.Combine(reference, "icons", id + ".png"), pixel);
        return Vision.Operators.OperatorLibrary.LoadAsync(System.IO.Path.Combine(folder, "user"), reference).GetAwaiter().GetResult();
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var d in Descendants(child)) yield return d;
        }
    }

    private static T OnSta<T>(Func<T> work)
    {
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { result = work(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new Xunit.Sdk.XunitException(error.ToString());
        return result;
    }
}

/// <summary>
/// What the overlay does with each reading (<see cref="RotationFollower"/>, driven as
/// <c>OverlayWindow.TrackRotation</c> drives it): which phase the strip shows, when the counts of
/// phases shown are forgotten, and whether the boss's statuses are read.
/// </summary>
public class RotationFollowerTests
{
    private static readonly string[] Squad = ["Laevatain", "Camille", "Arcane", "Ardelia"];

    /// <summary>A reading of the squad: in battle (skill points and combo bars read) or out of it.</summary>
    private static Vision.PartyState Reading(bool inBattle, bool laevatainUltimateReady = false)
    {
        var slots = Squad.Select((id, i) => new Vision.PartySlot(i + 1, null, 1.0, "", new Vision.Frame(1, 1, new byte[4]),
            Combo: inBattle ? new Vision.ComboReading(1, true) : null,
            Ultimate: new Vision.UltimateReading(i == 0 && laevatainUltimateReady, i == 0 && laevatainUltimateReady ? 1 : 0.3),
            Operator: new Vision.Operators.OperatorMatch(new Vision.Operators.OperatorInfo(id, id, id, id + ".png"), 1, true))).ToList();
        return new Vision.PartyState(slots, PanelMarks: 8) { SkillPoints = inBattle ? 2 : null, ActiveSlot = 1 };
    }

    private static Func<IReadOnlyList<string?>, Rotation?> For(Rotation rotation) => squad => rotation.IsFor(squad) ? rotation : null;

    /// <summary>The owner's ask (2026-10-22): the counts of phases shown are cleared when the fight ends.</summary>
    [Fact]
    public void Leaving_battle_forgets_how_often_each_phase_was_shown()
    {
        var rotation = new Rotation(Rotation.CurrentFormat, "r", "r", Squad,
        [
            new RotationPhase("charge", "Charge", [new RotationStep("Ardelia", StepAction.BattleSkill)]),
            new RotationPhase("burst", "Burst", [new RotationStep("Laevatain", StepAction.Ultimate)], [new UltimateCondition("Laevatain", true)]),
        ]);
        var follower = new RotationFollower();

        Assert.Equal("Charge I", follower.Update(Reading(inBattle: true), true, For(rotation), presses: null).Position!.Value.Position.Step.PhaseLabel);
        Assert.Equal("Burst II", follower.Update(Reading(inBattle: true, laevatainUltimateReady: true), true, For(rotation), presses: null).Position!.Value.Position.Step.PhaseLabel);
        RotationFollowing? outside = null;
        for (int i = 0; i < 20; i++) outside = follower.Update(Reading(inBattle: false), true, For(rotation), presses: null);
        Assert.False(outside!.InBattle); // the short-gap allowance (StripVisibility) is long over
        Assert.Equal("Charge I", follower.Update(Reading(inBattle: true), true, For(rotation), presses: null).Position!.Value.Position.Step.PhaseLabel);

        // The second fight's showing only (not 2), and Burst not yet in this fight.
        Assert.Equal(1, follower.Selectors["r"].TimesShown("charge"));
        Assert.Equal(0, follower.Selectors["r"].TimesShown("burst"));
    }

    [Fact]
    public void A_short_gap_in_battle_is_not_the_end_of_the_fight()
    {
        var rotation = new Rotation(Rotation.CurrentFormat, "r", "r", Squad,
            [new RotationPhase("charge", "Charge", [new RotationStep("Ardelia", StepAction.BattleSkill)])]);
        var follower = new RotationFollower();

        follower.Update(Reading(inBattle: true), true, For(rotation), presses: null);
        follower.Update(Reading(inBattle: false), true, For(rotation), presses: null); // one reading without the skill points
        follower.Update(Reading(inBattle: true), true, For(rotation), presses: null);

        Assert.Equal(1, follower.Selectors["r"].TimesShown("charge"));
    }

    /// <summary>BUG2: a phase gate on a boss status (here inside an "any") never opened: the statuses were read only for steps.</summary>
    [Fact]
    public void A_status_in_a_phase_gate_turns_status_reading_on()
    {
        var rotation = new Rotation(Rotation.CurrentFormat, "r", "r", Squad,
        [
            new RotationPhase("charge", "Charge", [new RotationStep("Ardelia", StepAction.BattleSkill)]),
            new RotationPhase("burst", "Burst", [new RotationStep("Laevatain", StepAction.Ultimate)],
                [new AnyCondition([new BossStatusCondition("Cryo Infliction", true), new UltimateCondition("Arcane", true)])]),
        ]);
        var follower = new RotationFollower();

        Assert.True(follower.Update(Reading(inBattle: true), true, For(rotation), presses: null).ReadBossStatuses);
        // Out of battle there's no rotation to follow: nothing to read.
        RotationFollowing? outside = null;
        for (int i = 0; i < 20; i++) outside = follower.Update(Reading(inBattle: false), true, For(rotation), presses: null);
        Assert.False(outside!.ReadBossStatuses);
    }

    /// <summary>Ardelia's battle skill (slot 4), then Laevatain's (slot 1).</summary>
    private static readonly Rotation TwoSkills = new(Rotation.CurrentFormat, "r", "r", Squad,
        [new RotationPhase("charge", "Charge", [new RotationStep("Ardelia", StepAction.BattleSkill), new RotationStep("Laevatain", StepAction.BattleSkill)])]);

    private static Vision.PartyState At(double seconds, bool inBattle = true) => Reading(inBattle) with { ReadAt = TimeSpan.FromSeconds(seconds) };

    /// <summary>Presses from a list, as the key reader gives them.</summary>
    private sealed class Presses(params SkillPress[] presses) : ISkillPressSource
    {
        private readonly List<SkillPress> _left = [.. presses];
        public int Left => _left.Count;

        public IReadOnlyList<SkillPress> TakeUntil(TimeSpan readAt)
        {
            var taken = _left.Where(p => p.At <= readAt).OrderBy(p => p.At).ToList();
            _left.RemoveAll(p => p.At <= readAt);
            return taken;
        }
    }

    private static SkillPress Press(int slot, double seconds) => new(slot, TimeSpan.FromSeconds(seconds));

    /// <summary>The player hasn't turned on reading their keys: the strip is today's, with no step marked.</summary>
    [Fact]
    public void Without_a_press_source_there_are_no_step_marks()
    {
        var follower = new RotationFollower();

        var follow = follower.Update(At(0.75), true, For(TwoSkills), presses: null);
        follow = follower.Update(At(1.0), true, For(TwoSkills), presses: null);

        Assert.NotNull(follow.Position);
        Assert.Null(follow.Steps);
    }

    [Fact]
    public void A_press_before_the_reading_moves_the_marker()
    {
        var follower = new RotationFollower();
        var presses = new Presses(Press(4, 1.0));

        Assert.Equal([StepMark.Current, StepMark.Upcoming], follower.Update(At(0.75), true, For(TwoSkills), presses).Steps!.Marks);
        Assert.Equal([StepMark.Done, StepMark.Current], follower.Update(At(1.0), true, For(TwoSkills), presses).Steps!.Marks);
    }

    [Fact]
    public void A_press_after_the_reading_waits_for_the_next_one()
    {
        var follower = new RotationFollower();
        var presses = new Presses(Press(4, 1.1));
        follower.Update(At(0.75), true, For(TwoSkills), presses);

        Assert.Equal([StepMark.Current, StepMark.Upcoming], follower.Update(At(1.0), true, For(TwoSkills), presses).Steps!.Marks);
        Assert.Equal([StepMark.Done, StepMark.Current], follower.Update(At(1.25), true, For(TwoSkills), presses).Steps!.Marks);
    }

    [Fact]
    public void A_reading_without_a_time_takes_no_presses()
    {
        var follower = new RotationFollower();
        var presses = new Presses(Press(4, 1.0));
        follower.Update(Reading(inBattle: true), true, For(TwoSkills), presses);

        var follow = follower.Update(Reading(inBattle: true), true, For(TwoSkills), presses);

        Assert.Equal([StepMark.Current, StepMark.Upcoming], follow.Steps!.Marks);
        Assert.Equal(1, presses.Left);
    }

    [Fact]
    public void Leaving_battle_starts_the_steps_over()
    {
        var follower = new RotationFollower();
        var presses = new Presses(Press(4, 1.0));
        follower.Update(At(0.75), true, For(TwoSkills), presses);
        Assert.Equal([StepMark.Done, StepMark.Current], follower.Update(At(1.0), true, For(TwoSkills), presses).Steps!.Marks);

        RotationFollowing? outside = null;
        for (int i = 0; i < 20; i++) outside = follower.Update(At(2 + i * 0.25, inBattle: false), true, For(TwoSkills), presses);
        Assert.False(outside!.InBattle); // the short-gap allowance (StripVisibility) is long over

        Assert.Equal([StepMark.Current, StepMark.Upcoming], follower.Update(At(10), true, For(TwoSkills), presses).Steps!.Marks);
    }

    [Fact]
    public void Presses_are_tallied_by_slot_and_verdict()
    {
        var follower = new RotationFollower();
        // Slot 2 typed out of battle; then slot 4 mashed twice in one reading in battle.
        var presses = new Presses(Press(2, 0.5), Press(4, 1.4), Press(4, 1.45));
        follower.Update(At(0.75, inBattle: false), true, For(TwoSkills), presses);
        follower.Update(At(1.25), true, For(TwoSkills), presses);
        follower.Update(At(1.5), true, For(TwoSkills), presses);

        var tally = follower.PressTally;
        Assert.Equal(1, tally[1, (int)PressVerdict.NotInBattle]);
        Assert.Equal(1, tally[3, (int)PressVerdict.Counted]);
        Assert.Equal(1, tally[3, (int)PressVerdict.Repeat]);
        Assert.Equal(3, tally.Cast<int>().Sum());
    }

    /// <summary>Camille's battle skill (slot 2), then Ardelia's (slot 4).</summary>
    private static readonly Rotation CamilleFirst = new(Rotation.CurrentFormat, "r", "r", Squad,
        [new RotationPhase("charge", "Charge", [new RotationStep("Camille", StepAction.BattleSkill), new RotationStep("Ardelia", StepAction.BattleSkill)])]);

    /// <summary>
    /// The app's wiring (<c>OverlayWindow.TrackRotation</c>): with reading turned on, the presses the
    /// strip follows are the reader's, so key 2 in battle marks slot 2's battle skill done.
    /// </summary>
    [Fact]
    public void Key_2_in_battle_marks_slot_2s_battle_skill_done()
    {
        var reader = new Presses(Press(2, 1.0));
        var presses = OverlayWindow.PressesFor(new HudConfig { ReadSkillPresses = true }, reader);
        var follower = new RotationFollower();
        static Vision.PartyState Ready(double seconds) => At(seconds) with
        {
            Slots = [.. At(seconds).Slots.Select(s => s with { BattleSkill = Vision.BattleSkillState.Available })],
        };

        follower.Update(Ready(0.75), true, For(CamilleFirst), presses);
        var follow = follower.Update(Ready(1.0), true, For(CamilleFirst), presses);

        Assert.NotNull(follow.Steps); // null: the strip was handed no presses, so it marks no step
        Assert.Equal(StepMark.Done, follow.Steps.Marks[0]);
        Assert.Equal(0, reader.Left);
    }

    /// <summary>
    /// The owner's ask (2026-10-23): with the keys read, the phases played through reach the alerts
    /// (an alert "Charge played at least once" fires after Ardelia's and Laevatain's battle skills),
    /// and leaving battle sets the count back to 0.
    /// </summary>
    [Fact]
    public void Phases_played_through_reach_the_alerts_and_reset_when_the_fight_ends()
    {
        var follower = new RotationFollower();
        var alerts = new AlertFollower();
        var rule = Vision.Alerts.AlertRule.New([new PhasePlayedCondition("charge", Comparison.AtLeast, 1)], "");
        var presses = new Presses(Press(4, 1.0), Press(1, 1.2));
        IReadOnlyList<Vision.Alerts.AlertRule> Shown(RotationFollowing follow, double seconds) =>
            alerts.Update(At(seconds), follow.InBattle, true, [rule], follow.History, TimeSpan.FromSeconds(seconds)).Show;

        Assert.Empty(Shown(follower.Update(At(0.75), true, For(TwoSkills), presses), 0.75));
        Assert.Empty(Shown(follower.Update(At(1.0), true, For(TwoSkills), presses), 1.0));
        Assert.Equal([rule], Shown(follower.Update(At(1.25), true, For(TwoSkills), presses), 1.25));

        RotationFollowing? outside = null;
        for (int i = 0; i < 20; i++) outside = follower.Update(At(2 + i * 0.25, inBattle: false), true, For(TwoSkills), presses);
        var again = follower.Update(At(10), true, For(TwoSkills), presses);
        Assert.Equal(0, again.History.TimesPlayed("charge"));
    }

    /// <summary>Without the keys the alerts are told "phase played" is ignored: a rule of nothing else never fires.</summary>
    [Fact]
    public void Without_the_keys_alerts_ignore_phase_played()
    {
        var follow = new RotationFollower().Update(At(1.0), true, For(TwoSkills), presses: null);

        Assert.True(follow.History.PlayedIgnored);
        var rule = Vision.Alerts.AlertRule.New([new PhasePlayedCondition("charge", Comparison.AtLeast, 0)], "");
        Assert.Empty(new AlertFollower().Update(At(1.0), true, true, [rule], follow.History, TimeSpan.Zero).Show);
    }

    /// <summary>The player hasn't turned reading on: the strip gets no presses at all, not the reader's.</summary>
    [Fact]
    public void With_reading_off_the_strip_gets_no_presses()
    {
        var reader = new Presses(Press(2, 1.0));

        Assert.Null(OverlayWindow.PressesFor(new HudConfig { ReadSkillPresses = false }, reader));
        Assert.Same(reader, OverlayWindow.PressesFor(new HudConfig { ReadSkillPresses = true }, reader));
    }

    [Fact]
    public void A_rotation_without_status_conditions_leaves_status_reading_off()
    {
        var rotation = new Rotation(Rotation.CurrentFormat, "r", "r", Squad,
            [new RotationPhase("charge", "Charge", [new RotationStep("Ardelia", StepAction.BattleSkill, [new ComboCondition("Camille", true)])])]);

        Assert.False(new RotationFollower().Update(Reading(inBattle: true), true, For(rotation), presses: null).ReadBossStatuses);
    }
}
