using EndHud.Vision;
using EndHud.Vision.Operators;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// The controller HUD (Fixtures/controller, see its README): which layout is on screen, and the
/// squad read from the diamond, with LB held or not. Only what the controller recording shows is
/// read there: levels, ultimates and battle skills wait for a recording of them.
/// </summary>
public sealed class ControllerLayoutTests : IDisposable
{
    private static readonly string[] Truth = ["Camille", "Laevatain", "Si", "Arcane"];
    private readonly string _userFolder = Path.Combine(Path.GetTempPath(), "endhud-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_userFolder, recursive: true); } catch (IOException) { }
    }

    private static string Fixture(string name) => CombatFrameTests.Fixture(Path.Combine("controller", name));

    private static async Task<PartyState> ReadAsync(string name, SimulatedScreen? screen = null)
    {
        var frame = await ImageFile.LoadAsync(Fixture(name));
        return await new PartyReader(new TextRecognizer()).ReadAsync(screen?.Render(frame) ?? frame);
    }

    [Theory]
    [InlineData("out-of-combat.jpg", HudLayout.Controller)]
    [InlineData("combat-early.jpg", HudLayout.Controller)]
    [InlineData("combat.jpg", HudLayout.Controller)]
    [InlineData("combat-combos-cooling.jpg", HudLayout.Controller)]
    [InlineData("combat-late.jpg", HudLayout.Controller)]
    [InlineData("lb-held.jpg", HudLayout.ControllerSkillsHeld)]
    [InlineData("lb-held-again.jpg", HudLayout.ControllerSkillsHeld)]
    [InlineData("keyboard-mouse-same-fight.jpg", HudLayout.KeyboardMouse)]
    [InlineData("switch-keyboard-exploration.jpg", HudLayout.KeyboardMouse)]
    [InlineData("fullbattle-ultimates-charging.jpg", HudLayout.Controller)]
    [InlineData("fullbattle-lb-held.jpg", HudLayout.ControllerSkillsHeld)]
    public async Task The_layout_on_screen_is_recognised(string fixture, HudLayout layout) =>
        Assert.Equal(layout, (await ReadAsync(fixture)).Layout);

    /// <summary>
    /// A PlayStation controller labels the badges "L1" instead of "LB" (a player's screenshot; no
    /// full-size picture of it yet): the recording's frames with each badge's "B" replaced by a
    /// "1" are still the controller layout, and the squad is still read. Before, only "LB" was
    /// looked for, so with that pad the squad was found only while L1 was held.
    /// </summary>
    [Theory]
    [InlineData("combat.jpg")]
    [InlineData("combat-late.jpg")]
    [InlineData("out-of-combat.jpg")]
    [InlineData("fullbattle-ultimates-charging.jpg")]
    public async Task A_PlayStation_controllers_L1_badges_are_the_controller_layout(string fixture)
    {
        var frame = WithL1Badges(await ImageFile.LoadAsync(Fixture(fixture)));
        var state = await new PartyReader(new TextRecognizer()).ReadAsync(frame);

        Assert.Equal(HudLayout.Controller, state.Layout);
        if (fixture != "out-of-combat.jpg") Assert.True(state.PanelVisible);
    }

    /// <summary><paramref name="frame"/> with each badge's "B" (4K: x 461–483 / 3552–3574, rows
    /// 1836–1865) painted over dark and a 4-pixel "1" drawn in its place.</summary>
    private static Frame WithL1Badges(Frame frame)
    {
        var ui = new UiScaler(frame.Width, frame.Height);
        var bgra = (byte[])frame.Bgra.Clone();
        void Fill(PixelRect r, byte value)
        {
            for (int y = r.Y; y < r.Bottom; y++)
            for (int x = r.X; x < r.Right; x++)
            {
                int i = (y * frame.Width + x) * 4;
                bgra[i] = bgra[i + 1] = bgra[i + 2] = value;
            }
        }
        Fill(ui.BottomLeft(new PixelRect(461, 1836, 23, 30)), 20);
        Fill(ui.BottomLeft(new PixelRect(466, 1838, 4, 25)), 250);
        Fill(ui.BottomRight(new PixelRect(3552, 1836, 23, 30)), 20);
        Fill(ui.BottomRight(new PixelRect(3557, 1838, 4, 25)), 250);
        return new Frame(frame.Width, frame.Height, bgra);
    }

    /// <summary>Combo: 1 = ready, otherwise the refilled part.</summary>
    public static TheoryData<string, int, double[], double[]> Readings() => new()
    {
        { "combat-early.jpg", 3, [1, 1, 1, 1], [1, 1, 1, 1] },
        { "combat.jpg", 3, [1, 1, 0.99, 1], [1, 1, 1, 1] },
        { "combat-combos-cooling.jpg", 3, [0.73, 1, 0.85, 1], [1, 1, 1, 0.05] },
        { "combat-late.jpg", 2, [0.77, 1, 0.72, 1], [1, 0.39, 1, 0.13] },
        { "lb-held.jpg", 3, [1, 1, 0.99, 1], [1, 1, 1, 1] },
        { "lb-held-again.jpg", 2, [0.78, 1, 0.72, 1], [0.86, 1, 0.82, 1] },
    };

    [Theory]
    [MemberData(nameof(Readings))]
    public async Task The_squad_diamond_is_read(string fixture, int controlled, double[] hp, double[] combo)
    {
        var state = await ReadAsync(fixture);

        Assert.True(state.PanelVisible);
        Assert.True(state.InCombat);
        Assert.Equal(controlled, state.ActiveSlot);
        Assert.NotNull(state.SkillPoints);
        Assert.Equal(0, state.PanelMarks); // not drawn in this layout
        for (int i = 0; i < 4; i++)
        {
            var slot = state.Slots[i];
            Assert.InRange(slot.Hp!.Value, hp[i] - 0.03, hp[i] + 0.03);
            Assert.NotNull(slot.Combo);
            Assert.Equal(combo[i] >= 1, slot.Combo.Ready);
            if (combo[i] < 1) Assert.InRange(slot.Combo.Progress, combo[i] - 0.04, combo[i] + 0.04);
            // Not read in this layout yet: never a value read from the wrong place.
            Assert.Null(slot.Level);
            Assert.Null(slot.BattleSkill);
            // Every ultimate was ready all through the first recording. While LB is held the
            // circles become large rings, which aren't read (the last reading stays).
            if (state.Layout == HudLayout.Controller) Assert.True(slot.Ultimate?.Ready, $"slot {i + 1}: ultimate {slot.Ultimate}");
            else Assert.Null(slot.Ultimate);
            Assert.False(slot.Dead);
        }
    }

    /// <summary>
    /// Three fallen, one left fighting (the second controller recording, 3:41): the fallen are read
    /// as fallen, and the panel stays (one combo bar alone isn't enough to call it the panel; the
    /// fallen operators' marks are). Before, the gauges went away, and the identifier named the
    /// marks as Purrchena.
    /// </summary>
    [Fact]
    public async Task Fallen_operators_are_read_and_keep_the_panel_up()
    {
        var state = await ReadAsync("three-fallen.jpg");

        Assert.Equal(HudLayout.Controller, state.Layout);
        Assert.Equal([true, false, true, true], state.Slots.Select(s => s.Dead));
        Assert.NotNull(state.Slots[1].Combo);
        Assert.True(state.PanelVisible);
        Assert.True(state.InCombat);
    }

    [Theory]
    [InlineData("out-of-combat.jpg")]
    [InlineData("photo-mode.jpg")]
    [InlineData("target-wheel.jpg")]
    public async Task No_squad_panel_when_the_game_draws_none(string fixture) =>
        Assert.False((await ReadAsync(fixture)).PanelVisible);

    /// <summary>Smaller screens: the 4K frames scaled down read the same (or not at all).</summary>
    public static TheoryData<string, string> OnOtherScreens()
    {
        var data = new TheoryData<string, string>();
        foreach (var screen in new[] { SimulatedScreen.Qhd, SimulatedScreen.FullHd, SimulatedScreen.LaptopHdPlus, SimulatedScreen.Wuxga })
            foreach (string fixture in new[] { "combat-late.jpg", "lb-held-again.jpg" })
                data.Add(fixture, screen.Name);
        return data;
    }

    [Theory]
    [MemberData(nameof(OnOtherScreens))]
    public async Task The_diamond_reads_the_same_on_other_screens(string fixture, string screenName)
    {
        var at4K = await ReadAsync(fixture);
        var here = await ReadAsync(fixture, SimulatedScreen.Named(screenName));

        Assert.Equal(at4K.Layout, here.Layout);
        Assert.True(here.PanelVisible);
        Assert.Equal(at4K.ActiveSlot, here.ActiveSlot);
        for (int i = 0; i < 4; i++)
        {
            Assert.InRange(here.Slots[i].Hp!.Value, at4K.Slots[i].Hp!.Value - 0.05, at4K.Slots[i].Hp!.Value + 0.05);
            var (a, b) = (at4K.Slots[i].Combo!, here.Slots[i].Combo);
            if (b is null) continue; // unreadable is allowed, wrong is not
            Assert.Equal(a.Ready, b.Ready);
            Assert.InRange(b.Progress, a.Progress - 0.04, a.Progress + 0.04);
        }
    }

    /// <summary>
    /// The controller's marks must never be found on a keyboard-and-mouse screen: every other
    /// screenshot in the test data, as it is and at 1080p. Controller screenshots are in a
    /// "controller" folder, or elsewhere with "controller" in their name (kept with the other frames
    /// of what they show, e.g. ultimate-time/sky-no-ultimate-controller.jpg).
    /// </summary>
    [Fact]
    public async Task Keyboard_and_mouse_screens_are_never_taken_for_the_controller_layout()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        string controller = Path.DirectorySeparatorChar + "controller" + Path.DirectorySeparatorChar;
        var files = Directory.GetFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            .Where(f => !f.Contains(controller) && !Path.GetFileName(f).Contains("controller", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.True(files.Count > 100, $"only {files.Count} screenshots found");

        var wrong = new List<string>();
        foreach (string file in files)
        {
            var frame = await ImageFile.LoadAsync(file);
            foreach (var shown in new[] { frame, SimulatedScreen.FullHd.Render(frame) })
            {
                var layout = HudLayoutDetector.Detect(shown, new UiScaler(shown.Width, shown.Height));
                if (layout != HudLayout.KeyboardMouse) wrong.Add($"{Path.GetRelativePath(root, file)} at {shown.Width}x{shown.Height}: {layout}");
            }
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }

    [Fact]
    public async Task The_squad_is_named_from_the_diamond_as_from_the_row()
    {
        var clock = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        foreach (string fixture in new[] { "keyboard-mouse-same-fight.jpg", "combat.jpg", "lb-held.jpg" })
        {
            var state = await ReadAsync(fixture);
            var identifier = new SquadIdentifier(await OperatorCache.LoadAsync(_userFolder), () => clock, null);
            await identifier.PrepareAsync(state);
            PartyState named = state;
            for (int n = 0; n < SquadIdentifier.MinReadings * 4; n++)
            {
                clock += TimeSpan.FromSeconds(0.5);
                named = await identifier.IdentifyAsync(state);
                foreach (var slot in named.Slots.Where(s => s.Operator is not null))
                    Assert.True(slot.Operator!.Operator.Id == Truth[slot.Number - 1], $"{fixture}: slot {slot.Number} named {slot.Operator.Operator.Id}");
            }
            Assert.Equal(Truth, named.Slots.Select(s => s.Operator?.Operator.Id));
        }
    }

    /// <summary>
    /// The player switches device whenever they like, and the game switches the HUD with them: a
    /// session as the app reads it (reader, tracker, naming), from the owner's recordings of one
    /// squad: exploring with keyboard and mouse, a fight with a controller (LB held for a moment),
    /// keyboard and mouse mid-fight, the controller again. Every reading has the layout on screen,
    /// the squad's names (never a wrong one), and ultimates read in that layout or, with LB held,
    /// the last ones.
    /// </summary>
    [Fact]
    public async Task Switching_between_keyboard_and_controller_at_any_time_keeps_reading()
    {
        var session = new (string Fixture, HudLayout Layout)[]
        {
            ("switch-keyboard-exploration.jpg", HudLayout.KeyboardMouse),
            ("fullbattle-ultimates-charging.jpg", HudLayout.Controller),
            ("fullbattle-lb-held.jpg", HudLayout.ControllerSkillsHeld),
            ("fullbattle-ultimate-almost-full.jpg", HudLayout.Controller),
            ("keyboard-mouse-same-fight.jpg", HudLayout.KeyboardMouse),
            ("combat-late.jpg", HudLayout.Controller),
        };
        var tracker = new PartyTracker();
        var clock = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var first = await ReadAsync(session[0].Fixture);
        var identifier = new SquadIdentifier(await OperatorCache.LoadAsync(_userFolder), () => clock, null);
        await identifier.PrepareAsync(first);

        foreach (var (fixture, layout) in session)
        {
            var raw = await ReadAsync(fixture);
            Assert.Equal(layout, raw.Layout);
            Assert.True(raw.PanelVisible, fixture);
            PartyState shown = raw;
            // A few readings of each moment, a quarter of a second apart, as the app takes them.
            for (int n = 0; n < SquadIdentifier.Window; n++)
            {
                clock += TimeSpan.FromSeconds(0.25);
                shown = await identifier.IdentifyAsync(tracker.Track(raw));
                foreach (var slot in shown.Slots.Where(s => s.Operator is not null))
                    Assert.True(slot.Operator!.Operator.Id == Truth[slot.Number - 1], $"{fixture}: slot {slot.Number} named {slot.Operator.Operator.Id}");
            }
            Assert.Equal(Truth, shown.Slots.Select(s => s.Operator?.Operator.Id));
            // Ultimates: read in every layout but LB held, where the last reading stays.
            Assert.All(shown.Slots, s => Assert.NotNull(s.Ultimate));
            if (layout == HudLayout.ControllerSkillsHeld) Assert.All(raw.Slots, s => Assert.Null(s.Ultimate));
        }
    }

    [Fact]
    public async Task Ultimates_read_with_a_controller_stay_shown_while_LB_is_held()
    {
        var tracker = new PartyTracker();
        tracker.Track(await ReadAsync("ultimates-charging.jpg"));
        var held = await ReadAsync("lb-held.jpg");
        Assert.All(held.Slots, s => Assert.Null(s.Ultimate));

        var shown = tracker.Track(held);

        Assert.All(shown.Slots, s => Assert.NotNull(s.Ultimate));
    }

    /// <summary>
    /// The ultimate dots in the skill diamond (the owner's fourth controller recording, read off
    /// zoomed frames): charging, a pale element-colour arc from 12 o'clock round a dark dot; ready,
    /// a plain element-colour dot in a light ring. 1 = ready.
    /// </summary>
    public static TheoryData<string, double[]> UltimateCharges() => new()
    {
        { "ultimates-two-ready.jpg", [1, 0.67, 1, 0.47] },
        { "ultimates-charging-one-ready.jpg", [0.33, 0.53, 1, 0.28] },
        { "ultimates-charging.jpg", [0.33, 0.60, 0.48, 0.33] },
        { "fullbattle-ultimates-charging.jpg", [0.40, 0.04, 0.30, 0.45] },
        // Nearly full is still charging: the dot has no light ring yet.
        { "fullbattle-ultimate-almost-full.jpg", [0.65, 0.50, 0.50, 0.97] },
        { "fullbattle-two-ready-two-almost-full.jpg", [1, 0.97, 0.80, 1] },
    };

    [Theory]
    [MemberData(nameof(UltimateCharges))]
    public async Task Ultimate_charge_is_read_from_the_skill_diamond(string fixture, double[] expected)
    {
        var state = await ReadAsync(fixture);

        Assert.Equal(HudLayout.Controller, state.Layout);
        for (int i = 0; i < 4; i++)
        {
            var ultimate = state.Slots[i].Ultimate;
            Assert.True(ultimate is not null, $"slot {i + 1}: unread");
            Assert.Equal(expected[i] >= 1, ultimate.Ready);
            if (expected[i] < 1) Assert.InRange(ultimate.Progress, expected[i] - 0.06, expected[i] + 0.06);
        }
    }
}
