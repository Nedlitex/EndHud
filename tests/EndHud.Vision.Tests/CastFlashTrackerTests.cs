using System.Globalization;
using EndHud.Vision;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// Runs of samples of the four battle-skill buttons told as flashes (Fixtures/cast-flash/samples: every frame of
/// a few seconds of 30 fps recordings, measured), and made-up runs for the rules the recordings don't show.
/// </summary>
public sealed class CastFlashTrackerTests
{
    /// <summary>A keyboard-and-mouse sample; the recorded samples predate the icon measure, so the button counts as seen (0.3).</summary>
    private static FlashSample Keyboard(double fill, double spread, double scene, double rim) => Measured(new(fill, spread, scene, rim, 0.3, SkillButtonPlaces.Row));

    /// <summary>A made-up or recorded sample whose rim, where lit, is lit all round, and whose scene is bright where it is 200 or more.</summary>
    private static FlashSample Measured(FlashSample s) => s with { RimAllRound = s.Rim, SceneBright = s.Scene >= BattleSkillFlash.BrightScene ? 1 : 0 };

    private static readonly FlashSample Rest = Keyboard(75, 4, 70, 0);

    private static List<(TimeSpan At, FlashSample[] Buttons)> Load(string file)
    {
        var rows = new List<(TimeSpan, FlashSample[])>();
        foreach (string line in File.ReadAllLines(CombatFrameTests.Fixture(Path.Combine("cast-flash", "samples", file))))
        {
            if (line.Trim().Length == 0) continue;
            int space = line.IndexOf(' ');
            double t = double.Parse(line[..space], CultureInfo.InvariantCulture);
            var buttons = line[(space + 1)..].Split('|').Select(cell =>
            {
                var v = cell.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                return Keyboard(v[0], v[1], v[2], v[3]);
            }).ToArray();
            rows.Add((TimeSpan.FromSeconds(t), buttons));
        }
        return rows;
    }

    /// <summary>Every flash told, feeding every <paramref name="every"/>-th sample (2 = 15 Hz from 30 fps).</summary>
    private static List<FlashEvent> Run(IEnumerable<(TimeSpan At, FlashSample[] Buttons)> samples, int every = 1)
    {
        var tracker = new CastFlashTracker();
        var told = new List<FlashEvent>();
        int i = 0;
        TimeSpan last = default;
        foreach (var (at, buttons) in samples)
        {
            last = at;
            if (i++ % every == 0) told.AddRange(tracker.Add(at, buttons));
        }
        // Rest after the recording's samples, so everything in progress is told.
        for (int k = 1; k <= 30; k++) told.AddRange(tracker.Add(last + TimeSpan.FromSeconds(k * every / 30.0), [Rest, Rest, Rest, Rest]));
        return told;
    }

    private static string Describe(IEnumerable<FlashEvent> events) =>
        string.Join(", ", events.Select(e => $"{e.Kind} button {e.Button} at {e.At.TotalSeconds:F3}"));

    [Fact]
    public void A_cast_without_a_cooldown_is_one_fill_on_its_button()
    {
        var told = Run(Load("bug1-slot3-cast.txt"));
        var fill = Assert.Single(told);
        Assert.Equal(2, fill.Button);
        Assert.Equal(FlashKind.Fill, fill.Kind);
        Assert.InRange(fill.At.TotalSeconds, 25.92, 25.94);
    }

    [Fact]
    public void Sampled_at_15_hz_the_cast_is_still_found()
    {
        foreach (int skip in new[] { 0, 1 })
        {
            var told = Run(Load("bug1-slot3-cast.txt").Skip(skip), every: 2);
            var fill = Assert.Single(told);
            Assert.Equal((2, FlashKind.Fill), (fill.Button, fill.Kind));
        }
    }

    [Fact]
    public void A_cast_with_a_cooldown_is_a_fill_then_the_countdown()
    {
        var told = Run(Load("fullbattletest-slot2-cast-countdown.txt"));
        var fill = Assert.Single(told);
        Assert.Equal((1, FlashKind.Fill), (fill.Button, fill.Kind));
        Assert.InRange(fill.At.TotalSeconds, 24.02, 24.04);
        // At 15 Hz too, whichever frames are sampled.
        foreach (int skip in new[] { 0, 1 })
            Assert.Equal([(1, FlashKind.Fill)], Run(Load("fullbattletest-slot2-cast-countdown.txt").Skip(skip), every: 2).Select(e => (e.Button, e.Kind)));
    }

    [Fact]
    public void Scene_light_behind_several_buttons_is_no_flash()
    {
        Assert.Empty(Run(Load("bug1-scene-light.txt")));
        Assert.Empty(Run(Load("bug1-scene-light.txt"), every: 2));
        Assert.Empty(Run(Load("bug1-scene-light.txt").Skip(1), every: 2));
    }

    [Fact]
    public void The_empowered_glow_starting_then_its_cast_ending_it()
    {
        // Slot 4 flashes at 80.0 as well; only slot 2 is followed here.
        var told = Run(Load("fullbattletest-slot2-empowered.txt")).Where(e => e.Button == 1).ToList();
        // The glow starts with a fill (told as a fill, as a cast's would be), and is told as a start once the rim stays lit.
        var start = told.Where(e => e.At.TotalSeconds is > 79.0 and < 79.3).Select(e => e.Kind).ToList();
        Assert.Equal([FlashKind.Fill, FlashKind.EmpoweredStart], start);
        // The empowered cast: a fill inside the lit rim, which then goes off.
        var cast = told.Where(e => e.At.TotalSeconds is > 79.7 and < 79.9).Select(e => e.Kind).ToList();
        Assert.Equal([FlashKind.EmpoweredFill, FlashKind.EmpoweredCast], cast);
        Assert.True(told.Count == 4, Describe(told));
    }

    [Fact]
    public void One_bright_frame_without_a_countdown_is_not_a_flash()
    {
        var samples = Steady(0, 1.0).Concat(At(1.0, Keyboard(200, 5, 70, 0))).Concat(Steady(1.033, 1.0));
        Assert.Empty(Run(samples));
    }

    [Fact]
    public void One_bright_frame_then_a_countdown_is_a_cast()
    {
        var dark = Keyboard(15, 0, 70, 0);
        var samples = Steady(0, 1.0).Concat(At(1.0, Keyboard(150, 5, 70, 0)))
            .Concat(Enumerable.Range(1, 30).SelectMany(k => At(1.0 + k / 30.0, dark)));
        var fill = Assert.Single(Run(samples));
        Assert.Equal(1, fill.Button);
    }

    [Fact]
    public void A_fill_longer_than_any_cast_is_scene_light()
    {
        var bright = Keyboard(200, 5, 70, 0);
        var samples = Steady(0, 1.0).Concat(Enumerable.Range(0, 20).SelectMany(k => At(1.0 + k / 30.0, bright))).Concat(Steady(1.667, 1.0));
        Assert.Empty(Run(samples));
    }

    [Fact]
    public void Two_buttons_filling_together_without_a_countdown_is_scene_light_and_with_one_two_casts()
    {
        var bright = Keyboard(200, 5, 70, 0);
        var dark = Keyboard(15, 0, 70, 0);
        IEnumerable<(TimeSpan, FlashSample[])> Both(FlashSample after) =>
            Steady(0, 1.0)
                .Concat(Enumerable.Range(0, 4).Select(k => (TimeSpan.FromSeconds(1.0 + k / 30.0), new[] { Rest, bright, bright, Rest })))
                .Concat(Enumerable.Range(4, 30).Select(k => (TimeSpan.FromSeconds(1.0 + k / 30.0), new[] { Rest, after, after, Rest })));
        Assert.Empty(Run(Both(Rest)));
        Assert.Equal([1, 2], Run(Both(dark)).Select(e => e.Button).Order());
    }

    [Fact]
    public void A_gap_in_the_samples_forgets_a_fill_in_progress()
    {
        var bright = Keyboard(200, 5, 70, 0);
        var samples = Steady(0, 1.0)
            .Concat(Enumerable.Range(0, 2).SelectMany(k => At(1.0 + k / 30.0, bright)))
            .Concat(Steady(3.0, 1.0)); // nothing sampled for two seconds
        Assert.Empty(Run(samples));
    }

    // The controller (made-up runs at the values measured on full battle 3).

    private static FlashSample Held(double fill, double scene = 110, double rim = 0) => Measured(new(fill, 6, scene, rim, 0.4, SkillButtonPlaces.DiamondHeld));

    private static FlashSample Plain(double fill, double scene = 110, double rim = 0, double icon = 0.3) => Measured(new(fill, 6, scene, rim, icon, SkillButtonPlaces.Diamond));

    /// <summary>Slot 3's button (button 2) as given, the others as <paramref name="rest"/>, 30 times a second from <paramref name="from"/>.</summary>
    private static IEnumerable<(TimeSpan, FlashSample[])> Slot3(double from, IEnumerable<FlashSample> run, FlashSample rest) =>
        run.Select((b, k) => (TimeSpan.FromSeconds(from + k / 30.0), new[] { rest, rest, b, rest }));

    [Fact]
    public void With_lb_held_the_pressed_grey_then_the_white_fill_is_one_cast_told_in_the_held_diamond()
    {
        var pressed = Enumerable.Repeat(Held(155), 6);
        var flash = new[] { Held(201, 130), Held(201, 130), Held(203, 131), Held(199, 115) };
        var tracker = new CastFlashTracker();
        var told = new List<FlashEvent>();
        foreach (var (at, buttons) in Slot3(0, Enumerable.Repeat(Held(120), 30).Concat(pressed).Concat(flash).Concat(Enumerable.Repeat(Held(145), 30)), Held(120)))
            told.AddRange(tracker.Add(at, buttons));
        var fill = Assert.Single(told);
        Assert.Equal((2, FlashKind.Fill, SkillButtonPlaces.DiamondHeld), (fill.Button, fill.Kind, fill.Places));
        Assert.InRange(fill.At.TotalSeconds, 1.19, 1.21);
    }

    [Fact]
    public void A_fill_in_the_plain_diamond_is_no_cast()
    {
        var tracker = new CastFlashTracker();
        var samples = Slot3(0, Enumerable.Repeat(Plain(80), 30).Concat(Enumerable.Repeat(Plain(205, 120), 4)).Concat(Enumerable.Repeat(Plain(80), 30)), Plain(80));
        Assert.Empty(samples.SelectMany(s => tracker.Add(s.Item1, s.Item2)).ToList());
    }

    /// <summary>The verdict per button after feeding these samples.</summary>
    private static IReadOnlyList<bool?> EmpoweredAfter(IEnumerable<(TimeSpan, FlashSample[])> samples)
    {
        var tracker = new CastFlashTracker();
        TimeSpan last = default;
        foreach (var (at, buttons) in samples)
        {
            tracker.Add(at, buttons);
            last = at;
        }
        return tracker.Empowered(last);
    }

    [Fact]
    public void A_rim_lit_for_half_a_second_is_empowered_and_the_others_not()
    {
        var verdict = EmpoweredAfter(Slot3(0, Enumerable.Repeat(Plain(120, rim: 0.4), 20), Plain(80)));
        Assert.Equal([false, false, true, false], verdict);
    }

    [Fact]
    public void A_cast_ring_passing_the_rim_is_not_empowered()
    {
        // The ring grows out of the button in under 0.2 s.
        var run = Enumerable.Repeat(Plain(80), 14).Concat(Enumerable.Repeat(Plain(80, rim: 0.3), 4)).Concat(Enumerable.Repeat(Plain(80), 2));
        Assert.Equal(false, EmpoweredAfter(Slot3(0, run, Plain(80)))[2]);
    }

    [Fact]
    public void Scene_light_or_a_button_not_seen_is_unknown_never_not_empowered()
    {
        Assert.Null(EmpoweredAfter(Slot3(0, Enumerable.Repeat(Plain(200, scene: 240, rim: 0.5), 20), Plain(80)))[2]);
        Assert.Null(EmpoweredAfter(Slot3(0, Enumerable.Repeat(Plain(40, icon: 0), 20), Plain(80)))[2]);
    }

    [Fact]
    public void Too_few_samples_tell_nothing()
    {
        var verdict = EmpoweredAfter(Slot3(0, Enumerable.Repeat(Plain(120, rim: 0.4), 3), Plain(80)));
        Assert.All(verdict, v => Assert.Null(v));
        Assert.All(new CastFlashTracker().Empowered(TimeSpan.FromSeconds(5)), v => Assert.Null(v));
    }

    [Fact]
    public void The_glow_is_followed_across_the_two_diamonds()
    {
        // LB held and let go while slot 3's rim glows: the same slot in either diamond.
        var run = Enumerable.Range(0, 20).Select(k => k % 6 < 3 ? Plain(120, rim: 0.4) : Held(150, rim: 0.45));
        Assert.Equal(true, EmpoweredAfter(Slot3(0, run, Plain(80)))[2]);
    }

    /// <summary>Every button at rest, 30 times a second.</summary>
    private static IEnumerable<(TimeSpan, FlashSample[])> Steady(double from, double seconds) =>
        Enumerable.Range(0, (int)Math.Round(seconds * 30)).Select(k => (TimeSpan.FromSeconds(from + k / 30.0), new[] { Rest, Rest, Rest, Rest }));

    /// <summary>Button 1 (slot 2) as given, the others at rest.</summary>
    private static IEnumerable<(TimeSpan, FlashSample[])> At(double t, FlashSample button) => [(TimeSpan.FromSeconds(t), new[] { Rest, button, Rest, Rest })];
}
