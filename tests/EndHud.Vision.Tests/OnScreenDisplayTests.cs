using EndHud.Vision;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// On-screen displays injected into the game (RivaTuner / MSI Afterburner temperatures and frame
/// rates, Steam, Discord, NVIDIA) are part of the captured picture, and RivaTuner's default place is
/// the top left, over the mode switch (<see cref="ModePill"/>). Covering the switch must never take
/// the battle HUD down in a battle: an on-screen display over a battle's top left is never read as
/// the switch, and over the switch it reads as not seen (or still the switch, where it misses the
/// parts compared), never as a fight. The tracker keeps the switch last seen through a short cover.
/// The displays here are drawn: lines of text in a 5x7 pixel font, in the colours such displays use,
/// with or without their dark backdrop, and a frame-time graph under them.
/// </summary>
public class OnScreenDisplayTests
{
    private static async Task<Frame> Load(string file) => await ImageFile.LoadAsync(CombatFrameTests.Fixture(file));

    private static ModePillReading Read(Frame frame) => ModePill.Read(frame, new UiScaler(frame.Width, frame.Height));

    private static readonly (byte R, byte G, byte B) White = (255, 255, 255), Orange = (255, 160, 20), Red = (255, 40, 50), Green = (90, 255, 90);

    /// <summary>The display variants: text colour, font pixel size at 4K, a dark backdrop, where it starts.</summary>
    public static TheoryData<string, int> Displays()
    {
        var data = new TheoryData<string, int>();
        foreach (string frame in new[] { "battle", "exploring", "building", "controller-exploring" })
            for (int variant = 0; variant < VariantCount; variant++)
                data.Add(frame, variant);
        return data;
    }

    private const int VariantCount = 8;

    private static Frame WithDisplay(Frame source, int variant)
    {
        var colour = (variant % 4) switch { 0 => White, 1 => Orange, 2 => Red, _ => Green };
        int pixel = variant < 4 ? 5 : 8;            // font pixel at 4K: text 35 or 56 px high
        bool backdrop = variant % 2 == 0;
        int left = variant % 3 * 12, top = variant % 2 * 20;
        return OnScreenDisplay.Draw(source, colour, pixel, backdrop, left, top);
    }

    private static async Task<Frame> Fixture(string frame) => frame switch
    {
        "battle" => await Load("combat-all-ready.jpg"),
        "exploring" => await Load("exploration-levels-visible.png"),
        "building" => await Load(Path.Combine("mode-switch", "keyboard-building.jpg")),
        _ => await Load(Path.Combine("mode-switch", "controller-exploring.jpg")),
    };

    [Theory]
    [MemberData(nameof(Displays))]
    public async Task A_display_over_the_top_left_is_never_read_as_a_fight_nor_as_the_switch_in_a_battle(string frame, int variant)
    {
        var reading = Read(WithDisplay(await Fixture(frame), variant));

        if (frame == "battle") Assert.Equal(ModePillReading.NotSeen, reading);
        else Assert.Contains(reading, new[] { ModePillReading.NotSeen, ModePillReading.OutOfBattle });
    }

    /// <summary>The battle with a display over the corner is still a battle, read through the whole reader.</summary>
    [Fact]
    public async Task A_battle_under_a_display_is_still_in_combat()
    {
        var reader = new PartyReader(new TextRecognizer());
        for (int variant = 0; variant < VariantCount; variant++)
        {
            var state = await reader.ReadAsync(WithDisplay(await Fixture("battle"), variant));
            Assert.True(state.InCombat, $"variant {variant}: {state.ModePill}");
        }
    }

    /// <summary>
    /// Exploring, then a display covers the switch: the switch last seen is kept for a short while (a
    /// notification or a flash over it), then the squad panel's battle signs decide alone, so a
    /// display that stays can never keep the battle HUD down in a battle.
    /// </summary>
    [Fact]
    public void The_switch_last_seen_is_kept_through_a_short_cover_only()
    {
        var tracker = new PartyTracker();
        var noPortrait = new Frame(1, 1, new byte[4]);
        PartyState Panel(ModePillReading pill) => new(
            Enumerable.Range(1, 4).Select(n => new PartySlot(n, null, 1, "", noPortrait, new ComboReading(1, true))).ToList(), PanelMarks: 8)
        { SkillPoints = 3, ModePill = pill };

        tracker.Track(Panel(ModePillReading.OutOfBattle));
        for (int n = 1; n < PartyTracker.ModePillCoveredReadings; n++)
            Assert.Equal(ModePillReading.OutOfBattle, tracker.Track(Panel(ModePillReading.NotSeen)).ModePill);
        var after = tracker.Track(Panel(ModePillReading.NotSeen));
        Assert.Equal(ModePillReading.NotSeen, after.ModePill);
        Assert.True(after.InCombat);
    }
}

/// <summary>Draws an on-screen display like RivaTuner's into a copy of a frame, at its top left.</summary>
internal static class OnScreenDisplay
{
    private static readonly string[] Lines = ["GPU  65C  98%", "CPU  71C  42%", "FPS  144", "16.7 MS"];

    public static Frame Draw(Frame source, (byte R, byte G, byte B) colour, int pixel4K, bool backdrop, int left4K, int top4K)
    {
        var frame = new Frame(source.Width, source.Height, (byte[])source.Bgra.Clone());
        double s = source.Width / 3840.0;
        int px = Math.Max(1, (int)Math.Round(pixel4K * s));
        int x0 = (int)Math.Round((left4K + 10) * s), y0 = (int)Math.Round((top4K + 10) * s);
        int lineHeight = 9 * px, width = 14 * 6 * px, graphHeight = (int)Math.Round(90 * s);
        if (backdrop) Shade(frame, x0 - px, y0 - px, width + 2 * px, Lines.Length * lineHeight + graphHeight + 3 * px);
        for (int line = 0; line < Lines.Length; line++)
            for (int c = 0; c < Lines[line].Length; c++)
                Glyph(frame, Lines[line][c], x0 + c * 6 * px, y0 + line * lineHeight, px, colour);
        // A frame-time graph under the text: a jagged line two pixels thick.
        int gy = y0 + Lines.Length * lineHeight + px, gw = width;
        var random = new Random(7);
        int last = graphHeight / 2;
        for (int x = 0; x < gw; x++)
        {
            int y = Math.Clamp(last + random.Next(-6, 7), 0, graphHeight - 2);
            for (int yy = Math.Min(last, y); yy <= Math.Max(last, y) + 1; yy++) Set(frame, x0 + x, gy + yy, colour);
            last = y;
        }
        return frame;
    }

    private static void Shade(Frame frame, int x, int y, int w, int h)
    {
        for (int yy = Math.Max(0, y); yy < Math.Min(frame.Height, y + h); yy++)
        for (int xx = Math.Max(0, x); xx < Math.Min(frame.Width, x + w); xx++)
        {
            int i = (yy * frame.Width + xx) * 4;
            for (int k = 0; k < 3; k++) frame.Bgra[i + k] = (byte)(frame.Bgra[i + k] * 0.35);
        }
    }

    private static void Set(Frame frame, int x, int y, (byte R, byte G, byte B) c)
    {
        if ((uint)x >= (uint)frame.Width || (uint)y >= (uint)frame.Height) return;
        int i = (y * frame.Width + x) * 4;
        frame.Bgra[i] = c.B;
        frame.Bgra[i + 1] = c.G;
        frame.Bgra[i + 2] = c.R;
    }

    private static void Glyph(Frame frame, char ch, int x, int y, int px, (byte R, byte G, byte B) colour)
    {
        if (!Font.TryGetValue(ch, out var rows)) return;
        for (int r = 0; r < 7; r++)
        for (int c = 0; c < 5; c++)
        {
            if (rows[r][c] != '#') continue;
            for (int dy = 0; dy < px; dy++)
            for (int dx = 0; dx < px; dx++)
                Set(frame, x + c * px + dx, y + r * px + dy, colour);
        }
    }

    // A 5x7 pixel font, the characters the display's lines use.
    private static readonly Dictionary<char, string[]> Font = new()
    {
        ['0'] = [".###.", "#...#", "#..##", "#.#.#", "##..#", "#...#", ".###."],
        ['1'] = ["..#..", ".##..", "..#..", "..#..", "..#..", "..#..", ".###."],
        ['2'] = [".###.", "#...#", "....#", "...#.", "..#..", ".#...", "#####"],
        ['4'] = ["...#.", "..##.", ".#.#.", "#..#.", "#####", "...#.", "...#."],
        ['5'] = ["#####", "#....", "####.", "....#", "....#", "#...#", ".###."],
        ['6'] = ["..##.", ".#...", "#....", "####.", "#...#", "#...#", ".###."],
        ['7'] = ["#####", "....#", "...#.", "..#..", ".#...", ".#...", ".#..."],
        ['8'] = [".###.", "#...#", "#...#", ".###.", "#...#", "#...#", ".###."],
        ['9'] = [".###.", "#...#", "#...#", ".####", "....#", "...#.", ".##.."],
        ['G'] = [".###.", "#...#", "#....", "#.###", "#...#", "#...#", ".###."],
        ['P'] = ["####.", "#...#", "#...#", "####.", "#....", "#....", "#...."],
        ['U'] = ["#...#", "#...#", "#...#", "#...#", "#...#", "#...#", ".###."],
        ['C'] = [".###.", "#...#", "#....", "#....", "#....", "#...#", ".###."],
        ['F'] = ["#####", "#....", "#....", "####.", "#....", "#....", "#...."],
        ['S'] = [".####", "#....", "#....", ".###.", "....#", "....#", "####."],
        ['M'] = ["#...#", "##.##", "#.#.#", "#.#.#", "#...#", "#...#", "#...#"],
        ['%'] = ["##...", "##..#", "...#.", "..#..", ".#...", "#..##", "...##"],
        ['.'] = [".....", ".....", ".....", ".....", ".....", ".##..", ".##.."],
    };
}
