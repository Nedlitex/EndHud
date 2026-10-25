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
        // With a controller both diamonds, the held one reaching further up and left.
        Assert.Equal(new PixelRect(3130, 1480, 710, 680), BattleSkillFlash.Region(3840, 2160, HudLayout.ControllerSkillsHeld));
        Assert.Equal(new PixelRect(1565, 740, 355, 340), BattleSkillFlash.Region(1920, 1080, HudLayout.Controller));
    }

    // The controller (Fixtures/cast-flash/controller-*: the bottom-right 710×680 px of full battle 3, 4K at 0.999).

    private static async Task<(SkillButtonPlaces? Places, FlashSample[] Buttons)> ControllerAsync(string file, double scale = Scale)
    {
        var frame = await ImageFile.LoadAsync(CombatFrameTests.Fixture(Path.Combine("cast-flash", file)));
        var places = BattleSkillFlash.PlacesOf(frame, scale);
        return (places, places is { } p ? BattleSkillFlash.MeasureAll(frame, scale, p) : []);
    }

    [Theory]
    [InlineData("controller-rest.png", SkillButtonPlaces.Diamond)]
    [InlineData("controller-slot1-empowered.png", SkillButtonPlaces.Diamond)]
    [InlineData("controller-slot2-empowered.png", SkillButtonPlaces.Diamond)]
    [InlineData("controller-scene-light.png", SkillButtonPlaces.Diamond)]
    [InlineData("controller-held-slot3-pressed.png", SkillButtonPlaces.DiamondHeld)]
    [InlineData("controller-held-slot3-fill.png", SkillButtonPlaces.DiamondHeld)]
    [InlineData("controller-held-slot1-fill.png", SkillButtonPlaces.DiamondHeld)]
    [InlineData("controller-held-slot3-fill-slot2-empowered.png", SkillButtonPlaces.DiamondHeld)]
    [InlineData("controller-held-slot1-empowered.png", SkillButtonPlaces.DiamondHeld)]
    [InlineData("controller-held-slot1-ultimate-press.png", SkillButtonPlaces.DiamondHeld)]
    public async Task The_controller_corner_says_which_diamond_it_shows(string file, SkillButtonPlaces expected)
    {
        var (places, _) = await ControllerAsync(file);
        Assert.Equal(expected, places);
    }

    [Fact]
    public async Task Keyboard_corners_show_no_controller_diamond()
    {
        foreach (string file in new[] { "bug1-rest.png", "bug1-slot3-fill.png", "fullbattletest-slot2-empowered-rim.png", "bug1-scene-light.png" })
        {
            var frame = await ImageFile.LoadAsync(CombatFrameTests.Fixture(Path.Combine("cast-flash", file)));
            Assert.Null(BattleSkillFlash.PlacesOf(frame, Scale));
        }
    }

    [Fact]
    public async Task At_rest_no_controller_button_is_filled_or_lit()
    {
        var (_, buttons) = await ControllerAsync("controller-rest.png");
        Assert.All(buttons, b =>
        {
            Assert.False(BattleSkillFlash.Filled(b), $"{b}");
            Assert.Equal(false, BattleSkillFlash.Glow(b));
        });
    }

    [Theory]
    [InlineData("controller-held-slot3-fill.png", 2)]
    [InlineData("controller-held-slot1-fill.png", 0)]
    [InlineData("controller-held-slot3-fill-slot2-empowered.png", 2)]
    public async Task A_cast_with_lb_held_fills_its_own_button_only_and_stands_out(string file, int cast)
    {
        var (_, buttons) = await ControllerAsync(file);
        Assert.True(BattleSkillFlash.Filled(buttons[cast]), $"{buttons[cast]}");
        Assert.True(BattleSkillFlash.StandsOut(buttons[cast], baseline: 150), $"{buttons[cast]}");
        for (int other = 0; other < 4; other++)
            if (other != cast) Assert.False(BattleSkillFlash.Filled(buttons[other]), $"{other}: {buttons[other]}");
    }

    [Fact]
    public async Task The_pressed_button_before_the_flash_is_not_yet_filled()
    {
        // LB held, the button pressed: light grey (about 150), lighter than at rest but not the flash's white.
        var (_, buttons) = await ControllerAsync("controller-held-slot3-pressed.png");
        Assert.All(buttons, b => Assert.False(BattleSkillFlash.Filled(b), $"{b}"));
        Assert.InRange(buttons[2].Fill, 140, 170);
    }

    [Fact]
    public async Task A_long_press_for_the_ultimate_fills_like_a_cast()
    {
        // Slot 1's ultimate held down: the same white disc (the judge tells it apart: the ultimate is spent).
        var (_, buttons) = await ControllerAsync("controller-held-slot1-ultimate-press.png");
        Assert.True(BattleSkillFlash.Filled(buttons[0]), $"{buttons[0]}");
        Assert.All(buttons.Skip(1), b => Assert.False(BattleSkillFlash.Filled(b), $"{b}"));
    }

    [Theory]
    [InlineData("controller-slot1-empowered.png", 0)]
    [InlineData("controller-slot2-empowered.png", 1)]
    [InlineData("controller-held-slot1-empowered.png", 0)]
    [InlineData("controller-held-slot3-fill-slot2-empowered.png", 1)]
    public async Task The_empowered_glow_lights_its_own_rim_in_either_diamond(string file, int empowered)
    {
        var (_, buttons) = await ControllerAsync(file);
        Assert.Equal(true, BattleSkillFlash.Glow(buttons[empowered]));
        for (int other = 0; other < 4; other++)
            if (other != empowered) Assert.NotEqual(true, BattleSkillFlash.Glow(buttons[other]));
    }

    [Fact]
    public async Task Scene_light_behind_the_controller_buttons_lights_no_rim()
    {
        // A white beam behind the see-through buttons lights their rims (0.29-0.56), but the scene round them is as bright.
        var (_, buttons) = await ControllerAsync("controller-scene-light.png");
        Assert.All(buttons, b => Assert.Null(BattleSkillFlash.Glow(b)));
        Assert.All(buttons, b => Assert.False(BattleSkillFlash.Filled(b), $"{b}"));
    }

    [Fact]
    public async Task The_keyboard_empowered_glow_is_told_and_its_rest_is_not()
    {
        var glow = await MeasureAsync("fullbattletest-slot2-empowered-rim.png");
        Assert.Equal(true, BattleSkillFlash.Glow(glow[1]));
        Assert.All((await MeasureAsync("bug1-rest.png")), b => Assert.Equal(false, BattleSkillFlash.Glow(b)));
    }

    [Fact]
    public async Task Under_a_dark_backdrop_a_button_is_not_seen()
    {
        // An on-screen display's 55 % black backdrop over the buttons: the glow can't be told, not "not lit".
        var frame = await ImageFile.LoadAsync(CombatFrameTests.Fixture(Path.Combine("cast-flash", "controller-slot2-empowered.png")));
        var dimmed = new byte[frame.Bgra.Length];
        for (int i = 0; i < dimmed.Length; i++) dimmed[i] = (i & 3) == 3 ? frame.Bgra[i] : (byte)(frame.Bgra[i] * 0.45);
        var buttons = BattleSkillFlash.MeasureAll(new Frame(frame.Width, frame.Height, dimmed), Scale, SkillButtonPlaces.Diamond);
        Assert.All(buttons, b => Assert.Null(BattleSkillFlash.Glow(b)));
    }

    [Fact]
    public async Task On_a_1080p_screen_the_controller_fill_and_glow_are_found_the_same()
    {
        var frame = await ImageFile.LoadAsync(CombatFrameTests.Fixture(Path.Combine("cast-flash", "controller-held-slot3-fill-slot2-empowered.png")));
        var half = frame.Resize(frame.Width / 2, frame.Height / 2);
        Assert.Equal(SkillButtonPlaces.DiamondHeld, BattleSkillFlash.PlacesOf(half, Scale / 2));
        var buttons = BattleSkillFlash.MeasureAll(half, Scale / 2, SkillButtonPlaces.DiamondHeld);
        Assert.True(BattleSkillFlash.Filled(buttons[2]), $"{buttons[2]}");
        Assert.Equal(true, BattleSkillFlash.Glow(buttons[1]));
        Assert.False(BattleSkillFlash.Filled(buttons[0]), $"{buttons[0]}");
    }
}
