using EndHud.Vision;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// The top-left evacuate bar (撤离, <see cref="EvacuateBar"/>), which only an instance draws: read in an instance's battle in
/// both layouts and over a white sky, never in the open world (the mode switch, an open-world event's own buttons), on
/// menus, cutscenes or the inn; the same on every screen size; and an on-screen display over the corner never makes it.
/// </summary>
public class EvacuateBarTests
{
    private static async Task<Frame> Load(string file) => await ImageFile.LoadAsync(CombatFrameTests.Fixture(file));

    private static bool Read(Frame frame) => EvacuateBar.Read(frame, new UiScaler(frame.Width, frame.Height));

    public static TheoryData<string> Shown => new()
    {
        "combat-all-ready.jpg",                                   // keyboard and mouse: the door icon, "ESC" above
        "battle-low-hp.jpg",
        "battle-squad-wiped.jpg",
        Path.Combine("controller", "combat.jpg"),                 // controller: the round menu button
        Path.Combine("controller", "fullbattle-lb-held.jpg"),
        Path.Combine("mode-switch", "controller-evacuate-bar.jpg"), // a bright sky right around it
        Path.Combine("panel-or-not", "white-sky-after-a-failed-fight.jpg"), // no icon drawn, a white sky
        Path.Combine("skill-points", "seven-segments-full.jpg"),  // the owner's screenshot
    };

    public static TheoryData<string> NotShown => new()
    {
        // The open world: the mode switch, exploring, building or fighting.
        "exploration-levels-visible.png",
        Path.Combine("mode-switch", "controller-exploring.jpg"),
        Path.Combine("mode-switch", "controller-exploring-bright-sky.jpg"),
        Path.Combine("mode-switch", "keyboard-building.jpg"),
        Path.Combine("mode-switch", "controller-fight-bright-sky.jpg"),
        Path.Combine("controller", "ultimates-charging.jpg"),
        // Fights in the open world without the switch: an event's own buttons and the minimap there.
        Path.Combine("mode-switch", "keyboard-open-world-event-fight.jpg"),
        "ultimate-charge-just-started.jpg",
        "ultimate-charging-two-hurt.jpg",
        // No HUD at the top left: a cutscene, photo mode, the target wheel, the exit dialog, the squad select screen.
        Path.Combine("controller", "fullbattle3-si-ultimate-cutscene.jpg"),
        Path.Combine("controller", "photo-mode.jpg"),
        Path.Combine("controller", "target-wheel.jpg"),
        Path.Combine("controller", "exit-dialog.jpg"),
        Path.Combine("panel-or-not", "squad-select-screen.jpg"),
        // The inn's screens: white panels and black text, but not the bar.
        Path.Combine("inn", "menu-dishes.jpg"),
        Path.Combine("inn", "setup-theme.jpg"),
        Path.Combine("inn", "session-four-orders.jpg"),
        // An instance's battle starting, the screen greyed: not read (the battle kind keeps the instance seen before).
        Path.Combine("mode-switch", "controller-evacuate-bar-battle-start.jpg"),
    };

    [Theory]
    [MemberData(nameof(Shown))]
    public async Task Read_in_an_instance(string file) => Assert.True(Read(await Load(file)));

    [Theory]
    [MemberData(nameof(NotShown))]
    public async Task Not_read_in_the_open_world_or_without_it(string file) => Assert.False(Read(await Load(file)));

    /// <summary>A clear margin both ways on the white share and the characters' ink, so neither is a knife edge.</summary>
    [Fact]
    public async Task The_measures_sit_well_clear_of_the_thresholds()
    {
        var bar = await Load("combat-all-ready.jpg");
        var pill = await Load(Path.Combine("mode-switch", "controller-exploring.jpg"));
        var (white, _, first, second) = EvacuateBar.Measure(bar, new UiScaler(bar.Width, bar.Height));
        Assert.True(white >= EvacuateBar.WhiteAt + 0.05, $"white {white:F2}");
        Assert.InRange(first, EvacuateBar.CharacterInkMin + 0.15, EvacuateBar.CharacterInkMax - 0.1);
        Assert.InRange(second, EvacuateBar.CharacterInkMin + 0.15, EvacuateBar.CharacterInkMax - 0.1);

        // The mode switch's capsule there: white under 0.75, and 撤 has no ink.
        var (pillWhite, _, pillFirst, _) = EvacuateBar.Measure(pill, new UiScaler(pill.Width, pill.Height));
        Assert.True(pillWhite <= EvacuateBar.WhiteAt - 0.15, $"white {pillWhite:F2}");
        Assert.True(pillFirst < EvacuateBar.CharacterInkMin, $"first {pillFirst:F2}");
    }

    /// <summary>
    /// White where the bar would be but no word in it is not the bar: a flash or a white sky filling the corner, a plain white
    /// box. The word's two characters (ink in each, not a dark patch) are what make it the bar.
    /// </summary>
    [Theory]
    [InlineData(255, false)] // plain white over the bar's place and around it
    [InlineData(255, true)]  // white, with a dark block where the word would be (a dark patch is not two characters)
    [InlineData(230, false)] // a bright grey-white sky
    public async Task A_white_corner_without_the_word_is_not_the_bar(byte white, bool darkWordPlace)
    {
        var frame = await Load(Path.Combine("mode-switch", "keyboard-open-world-event-fight.jpg"));
        var ui = new UiScaler(frame.Width, frame.Height);
        Paint(frame, ui.TopLeft(new PixelRect(0, 40, 420, 140)), white);
        if (darkWordPlace) Paint(frame, ui.TopLeft(new PixelRect(180, 80, 96, 46)), 20);

        Assert.False(Read(frame));
    }

    private static void Paint(Frame frame, PixelRect rect, byte value)
    {
        for (int y = rect.Y; y < rect.Bottom; y++)
        for (int x = rect.X; x < rect.Right; x++)
        {
            int i = (y * frame.Width + x) * 4;
            frame.Bgra[i] = frame.Bgra[i + 1] = frame.Bgra[i + 2] = value;
        }
    }

    public static TheoryData<string, string> Screens()
    {
        var data = new TheoryData<string, string>();
        foreach (var screen in SimulatedScreen.All)
        {
            data.Add(screen.Name, "combat-all-ready.jpg");
            data.Add(screen.Name, Path.Combine("controller", "combat.jpg"));
            data.Add(screen.Name, Path.Combine("mode-switch", "controller-evacuate-bar.jpg"));
            data.Add(screen.Name, Path.Combine("mode-switch", "keyboard-open-world-event-fight.jpg"));
            data.Add(screen.Name, "exploration-levels-visible.png");
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Screens))]
    [Trait("Category", "Slow")] // sweeps screen sizes: left out of the fast lane
    public async Task Read_the_same_on_every_screen_size(string screen, string file)
    {
        var source = await Load(file);
        Assert.Equal(Read(source), Read(SimulatedScreen.Named(screen).Render(source)));
    }

    /// <summary>An on-screen display (RivaTuner's, at the top left by default) over the corner can hide the bar, never make
    /// one: over the open world the reading stays not seen at every position, with its backdrop or without, at 4K and 1080p.</summary>
    [Theory]
    [InlineData("exploration-levels-visible.png")]
    [InlineData("mode-switch/keyboard-open-world-event-fight.jpg")]
    [InlineData("mode-switch/controller-fight-bright-sky.jpg")]
    [InlineData("mode-switch/keyboard-building.jpg")]
    [Trait("Category", "Slow")] // sweeps synthetic on-screen displays: left out of the fast lane
    public async Task A_display_over_the_corner_never_makes_the_bar(string file)
    {
        var source = await Load(file);
        foreach (var frame in new[] { source, SimulatedScreen.FullHd.Render(source) })
        foreach (var position in SyntheticOsd.Positions)
        foreach (bool background in new[] { true, false })
        foreach (bool minimal in new[] { true, false })
            Assert.False(Read(SyntheticOsd.Draw(frame, position, background, minimal)), $"{file} {frame.Width}x{frame.Height} {position} bg={background} minimal={minimal}");
    }
}
