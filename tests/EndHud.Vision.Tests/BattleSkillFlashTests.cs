using EndHud.Vision;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// The battle-skill buttons measured for the cast flash (Fixtures/cast-flash: the bottom-right
/// corner of 4K frames, scale 0.999). A cast fills the top half of the button flat light grey; the
/// empowered glow lights its rim instead; scene light shows through every button alike.
/// </summary>
public sealed class BattleSkillFlashTests
{
    private const double Scale = 2158 / 2160.0;

    private static async Task<FlashSample[]> MeasureAsync(string file, double scale = Scale) =>
        BattleSkillFlash.MeasureAll(await ImageFile.LoadAsync(CombatFrameTests.Fixture(Path.Combine("cast-flash", file))), scale);

    [Fact]
    public async Task At_rest_no_button_is_filled_or_lit()
    {
        var buttons = await MeasureAsync("bug1-rest.png");
        Assert.All(buttons, b =>
        {
            Assert.False(BattleSkillFlash.Filled(b), $"{b}");
            Assert.False(BattleSkillFlash.RimLit(b), $"{b}");
            Assert.False(BattleSkillFlash.Countdown(b), $"{b}");
        });
    }

    [Fact]
    public async Task A_cast_fills_its_own_button_only_brighter_than_the_scene_round_it()
    {
        var buttons = await MeasureAsync("bug1-slot3-fill.png");
        Assert.True(BattleSkillFlash.Filled(buttons[2]), $"{buttons[2]}");
        Assert.True(BattleSkillFlash.StandsOut(buttons[2], baseline: 75), $"{buttons[2]}");
        Assert.InRange(buttons[2].Fill, 160, 185);
        foreach (int other in new[] { 0, 1, 3 }) Assert.False(BattleSkillFlash.Filled(buttons[other]), $"{other}: {buttons[other]}");
    }

    [Fact]
    public async Task The_first_frame_of_a_fill_is_not_yet_filled()
    {
        var buttons = await MeasureAsync("bug1-slot3-fill-starting.png");
        Assert.False(BattleSkillFlash.Filled(buttons[2]), $"{buttons[2]}");
        Assert.InRange(buttons[2].Fill, 95, 125);
    }

    [Fact]
    public async Task Scene_light_behind_the_buttons_is_no_brighter_than_the_scene_round_them()
    {
        var buttons = await MeasureAsync("bug1-scene-light.png");
        // Bright, but the scene round each button is as bright: nothing stands out against it.
        foreach (int lit in new[] { 1, 2, 3 })
            Assert.True(buttons[lit].Fill - buttons[lit].Scene < BattleSkillFlash.SceneContrast, $"{lit}: {buttons[lit]}");
    }

    [Fact]
    public async Task A_cast_then_its_countdown()
    {
        var fill = await MeasureAsync("fullbattletest-slot2-fill.png");
        Assert.True(BattleSkillFlash.Filled(fill[1]), $"{fill[1]}");
        Assert.True(BattleSkillFlash.StandsOut(fill[1], baseline: 80), $"{fill[1]}");

        var countdown = await MeasureAsync("fullbattletest-slot2-countdown.png");
        Assert.True(BattleSkillFlash.Countdown(countdown[1]), $"{countdown[1]}");
        Assert.False(BattleSkillFlash.Filled(countdown[1]));
    }

    [Fact]
    public async Task The_empowered_glow_lights_the_rim_and_does_not_fill_the_disc()
    {
        var start = await MeasureAsync("fullbattletest-slot2-empowered-start-fill.png");
        Assert.True(BattleSkillFlash.Filled(start[1]), $"{start[1]}");
        Assert.False(BattleSkillFlash.RimLit(start[1]), $"{start[1]}");

        var glow = await MeasureAsync("fullbattletest-slot2-empowered-rim.png");
        Assert.True(BattleSkillFlash.RimLit(glow[1]), $"{glow[1]}");
        Assert.False(BattleSkillFlash.Filled(glow[1]), $"{glow[1]}");

        var cast = await MeasureAsync("fullbattletest-slot2-empowered-cast-fill.png");
        Assert.True(BattleSkillFlash.Filled(cast[1]), $"{cast[1]}");
        Assert.True(BattleSkillFlash.RimLit(cast[1]), $"{cast[1]}");
    }

    [Fact]
    public async Task A_fill_tinted_by_a_purple_scene_still_counts()
    {
        var buttons = await MeasureAsync("typho-slot1-purple-fill.png");
        Assert.True(BattleSkillFlash.Filled(buttons[0]), $"{buttons[0]}");
        Assert.True(BattleSkillFlash.StandsOut(buttons[0], baseline: 128), $"{buttons[0]}");
    }

    [Fact]
    public async Task On_a_1080p_screen_the_fill_is_found_the_same()
    {
        var frame = await ImageFile.LoadAsync(CombatFrameTests.Fixture(Path.Combine("cast-flash", "bug1-slot3-fill.png")));
        var half = frame.Resize(frame.Width / 2, frame.Height / 2);
        var buttons = BattleSkillFlash.MeasureAll(half, Scale / 2);
        Assert.True(BattleSkillFlash.Filled(buttons[2]), $"{buttons[2]}");
        Assert.False(BattleSkillFlash.Filled(buttons[1]), $"{buttons[1]}");
    }

    [Fact]
    public void The_corner_read_is_the_bottom_right_of_the_picture_at_any_size()
    {
        Assert.Equal(new PixelRect(3020, 1790, 820, 370), BattleSkillFlash.Region(3840, 2160));
        Assert.Equal(new PixelRect(1510, 895, 410, 185), BattleSkillFlash.Region(1920, 1080));
    }
}
