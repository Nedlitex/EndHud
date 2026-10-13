using System.Text.Json;
using EndHud.Vision;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// Skill points and the controlled slot, on frames of a boss fight (Fixtures/battle-hud: HUD areas
/// only, UID blacked out) whose values were read by eye (labels.json).
/// </summary>
public class CombatHudTests
{
    /// <param name="BattleSkills">Each slot's button; null where it's dimmed (under 1 skill point) or covered.</param>
    /// <param name="MeltingFlame">Laevatain's 熔火 (she's controlled throughout), read from the flower by eye.</param>
    private sealed record Label(string File, double? SkillPoints, int? ActiveSlot, BattleSkillState?[] BattleSkills, int? MeltingFlame);

    private static readonly Label[] Labels = JsonSerializer.Deserialize<Label[]>(
        File.ReadAllText(CombatFrameTests.Fixture(Path.Combine("battle-hud", "labels.json"))),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    public static TheoryData<string> Files() => [.. Labels.Select(l => l.File)];

    private static Task<Frame> LoadAsync(string file) => ImageFile.LoadAsync(CombatFrameTests.Fixture(Path.Combine("battle-hud", file)));

    private static async Task<PartyState> ReadAsync(Frame frame) => await new PartyReader(new TextRecognizer()).ReadAsync(frame);

    [Theory]
    [MemberData(nameof(Files))]
    public async Task Reads_the_skill_points_and_the_controlled_slot(string file)
    {
        var label = Labels.Single(l => l.File == file);
        var state = await ReadAsync(await LoadAsync(file));

        if (label.SkillPoints is { } expected)
        {
            Assert.NotNull(state.SkillPoints);
            Assert.InRange(state.SkillPoints!.Value, expected - 0.05, expected + 0.05);
        }
        else
        {
            Assert.Null(state.SkillPoints);
        }
        Assert.Equal(label.ActiveSlot, state.ActiveSlot);
    }

    [Theory]
    [MemberData(nameof(Files))]
    public async Task Reads_whether_each_battle_skill_is_available_or_cooling(string file)
    {
        var label = Labels.Single(l => l.File == file);
        var state = await ReadAsync(await LoadAsync(file));

        Assert.Equal(label.BattleSkills, state.Slots.Select(s => s.BattleSkill));
    }

    [Theory]
    [MemberData(nameof(Files))]
    public async Task Reads_Laevatains_melting_flame(string file)
    {
        var label = Labels.Single(l => l.File == file);
        var frame = await LoadAsync(file);
        var state = await ReadAsync(frame);
        state = state with { Slots = [state.Slots[0] with { Operator = new(new("Laevatain", "Laevatain", "莱万汀", "Laevatain.png"), 1, true) }, .. state.Slots.Skip(1)] };

        var readings = Operators.OperatorCounters.ReadControlled(frame, state);

        if (label.ActiveSlot is null)
        {
            Assert.Empty(readings); // nobody controlled (a cutscene): no counter
            return;
        }
        var reading = Assert.Single(readings);
        Assert.Equal("melting-flame", reading.CounterId);
        Assert.Equal(label.MeltingFlame, reading.Value);
    }

    [Fact]
    public async Task An_operator_without_a_known_counter_has_no_reading()
    {
        Assert.Empty(Operators.OperatorCounters.Read(await LoadAsync("battle-21s.jpg"), "Camille"));
    }

    public static TheoryData<string> Screens() =>
        [SimulatedScreen.Qhd.Name, SimulatedScreen.FullHd.Name, SimulatedScreen.LaptopHdPlus.Name, SimulatedScreen.Hd.Name];

    /// <summary>
    /// Smaller 16:9 screens: each value matches 4K's or is unreadable, never wrong, and most are read.
    /// (Other aspect ratios aren't simulated here: SimulatedScreen pins each quarter of the HUD to its
    /// corner, which would split the centred skill-point bar; the game keeps it centred.)
    /// </summary>
    [Theory]
    [MemberData(nameof(Screens))]
    public async Task Reads_the_same_on_smaller_screens_or_nothing(string screenName)
    {
        var screen = SimulatedScreen.Named(screenName);
        int read = 0, expected = 0;
        foreach (var label in Labels)
        {
            var state = await ReadAsync(screen.Render(await LoadAsync(label.File)));
            Assert.True(state.ActiveSlot is null || state.ActiveSlot == label.ActiveSlot,
                $"{label.File} at {screen.Width}x{screen.Height}: slot {state.ActiveSlot}, expected {label.ActiveSlot}");
            for (int i = 0; i < 4; i++)
            {
                var skill = state.Slots[i].BattleSkill;
                Assert.True(skill is null || skill == label.BattleSkills[i],
                    $"{label.File} at {screen.Width}x{screen.Height}: slot {i + 1} battle skill {skill}, expected {label.BattleSkills[i]}");
            }
            int? flame = state.ActiveSlot is null ? null : Operators.OperatorCounters.Read(screen.Render(await LoadAsync(label.File)), "Laevatain")[0].Value;
            Assert.True(flame is null || flame == label.MeltingFlame,
                $"{label.File} at {screen.Width}x{screen.Height}: 熔火 {flame}, expected {label.MeltingFlame}");
            if (label.SkillPoints is not { } sp) continue;
            expected++;
            if (state.SkillPoints is not { } got) continue;
            read++;
            Assert.True(Math.Abs(got - sp) <= 0.08, $"{label.File} at {screen.Width}x{screen.Height}: {got:F2} skill points, expected {sp:F2}");
        }
        Assert.True(read >= expected * 0.8, $"read {read} of {expected} at {screen.Width}x{screen.Height}");
    }

    [Fact]
    public async Task Out_of_combat_there_are_no_skill_points_but_the_controlled_slot_is_known()
    {
        var state = await ReadAsync(await ImageFile.LoadAsync(CombatFrameTests.Fixture("exploration-levels-visible.png")));

        Assert.Null(state.SkillPoints);
        Assert.Equal(1, state.ActiveSlot);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task The_marker_is_found_above_whichever_slot_has_it(int slot)
    {
        // The recording only ever controls slot 1, so move its marker to another slot.
        var frame = await LoadAsync("battle-21s.jpg");
        var moved = MoveMarker(frame, toSlot: slot);

        Assert.Equal(slot, (await ReadAsync(moved)).ActiveSlot);
    }

    /// <summary>The frame with the area above slot 1 (the marker) swapped with the same area above another slot.</summary>
    private static Frame MoveMarker(Frame frame, int toSlot)
    {
        var bgra = (byte[])frame.Bgra.Clone();
        var ui = new UiScaler(frame.Width, frame.Height);
        var from = ui.BottomLeft(new PixelRect(130, 1790, 72, 46));
        var to = ui.BottomLeft(PartyLayout.ForSlot(new PixelRect(130, 1790, 72, 46), toSlot - 1));
        for (int y = 0; y < from.Height; y++)
        {
            int a = ((from.Y + y) * frame.Width + from.X) * 4, b = ((to.Y + y) * frame.Width + to.X) * 4;
            var row = new byte[from.Width * 4];
            Buffer.BlockCopy(frame.Bgra, a, row, 0, row.Length);
            Buffer.BlockCopy(frame.Bgra, b, bgra, a, row.Length);
            Buffer.BlockCopy(row, 0, bgra, b, row.Length);
        }
        return new Frame(frame.Width, frame.Height, bgra);
    }
}
