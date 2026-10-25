using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// The combat readers one by one under an on-screen display drawn into the game's frames (MSI Afterburner's, drawn by
/// RivaTuner; <see cref="SyntheticOsd"/>), each where the 2026-10-24 investigation found a wrong value: what is read
/// under it is the clean frame's value or unknown. <see cref="OnScreenDisplayCombatGateTests"/> runs every combat
/// reading over every combat fixture; these name the case each fix is for.
/// </summary>
public class OnScreenDisplayCombatTests
{
    private static async Task<Frame> Load(string file) => await ImageFile.LoadAsync(CombatFrameTests.Fixture(file));

    /// <summary>The fixture at 4K and at 1080p.</summary>
    private static async Task<IEnumerable<(string Size, Frame Frame)>> Sizes(string file)
    {
        var source = await Load(file);
        return [("4K", source), ("1080p", SimulatedScreen.FullHd.Render(source))];
    }

    private static IEnumerable<(string Name, Frame Frame)> Covered(Frame frame, OsdPosition position) =>
        new[] { true, false }.Select(bg => ($"{position}{(bg ? " with its backdrop" : "")}", SyntheticOsd.Draw(frame, position, bg)));

    /// <summary>
    /// The display's text across the HP bar cut the fill short (1.00 read 0.79 or 0.33, 0.69 read 0.00), and its
    /// backdrop dimmed the fill below the fill colours (read as an empty bar): now the bar is unknown.
    /// </summary>
    [Theory]
    [InlineData("combat-all-ready.jpg")]
    [InlineData("lasting-ultimate-background/zl-46s.jpg")]
    [InlineData("battle-low-hp.jpg")]
    [InlineData("controller/combat.jpg")]
    public async Task An_HP_bar_under_a_display_reads_its_value_or_unknown(string file)
    {
        var reader = new PartyReader(new TextRecognizer());
        foreach (var (size, frame) in await Sizes(file))
        {
            var clean = await reader.ReadAsync(frame);
            foreach (var (name, covered) in Covered(frame, OsdPosition.BottomLeft))
            {
                var read = await reader.ReadAsync(covered);
                for (int i = 0; i < clean.Slots.Count; i++)
                    if (read.Slots[i].Hp is { } hp)
                        Assert.True(clean.Slots[i].Hp is { } was && Math.Abs(hp - was) <= 0.03,
                            $"{file} {size} {name}: slot {i + 1} HP {clean.Slots[i].Hp:F2} read {hp:F2}");
            }
        }
    }

    /// <summary>
    /// A ready combo bar under the display's backdrop is grey at 114, which read as a bar refilling (ready read
    /// 0.92-0.99); the flat dark backdrop over a fallen operator's slot, or over a bar's fill, read as an empty
    /// track (unknown read 0.00, 0.96 read 0.00). A reading the app doesn't take (no squad panel) only must not be a
    /// battle that the clean frame isn't.
    /// </summary>
    [Theory]
    [InlineData("combat-all-ready.jpg", "4K")]
    [InlineData("combat-all-ready.jpg", "1080p")]
    [InlineData("ultimate-charge-near-half.jpg", "4K")]
    [InlineData("battle-three-fallen.jpg", "4K")]
    [InlineData("infliction/ring-42_5s.jpg", "1080p")]
    public async Task A_combo_bar_under_a_display_reads_its_value_or_unknown(string file, string size)
    {
        var reader = new PartyReader(new TextRecognizer());
        var frame = (await Sizes(file)).Single(s => s.Size == size).Frame;
        var clean = await reader.ReadAsync(frame);
        foreach (var (name, covered) in Covered(frame, OsdPosition.BottomLeft))
        {
            var read = await reader.ReadAsync(covered);
            Assert.False(read.InCombat && !clean.InCombat, $"{name}: read as a battle");
            // A reading without the squad panel isn't taken (PartyTracker keeps the last one): its bars are unknown.
            if (!read.PanelVisible) continue;
            for (int i = 0; i < clean.Slots.Count; i++)
                if (read.Slots[i].Combo is { } combo)
                    Assert.True(clean.Slots[i].Combo == combo, $"{name}: slot {i + 1} combo {clean.Slots[i].Combo} read {combo}");
            Assert.False(read.InCombat && !clean.InCombat, $"{name}: read as a battle");
        }
    }

    /// <summary>
    /// The display at the bottom right over a battle-skill button that can't be read (dimmed): its white text gave
    /// enough white for an icon (unknown read Available), and on its dark backdrop for a countdown (unknown read
    /// Cooling); Si's button, read through the same test first, read Cooling too.
    /// </summary>
    [Theory]
    [InlineData("battle-hud/battle-47s.jpg")]
    [InlineData("battle-low-hp.jpg")]
    [InlineData("portrait-counters/laevatain-35s.jpg")]
    public async Task A_battle_skill_button_under_a_display_reads_its_state_or_unknown(string file)
    {
        var reader = new PartyReader(new TextRecognizer());
        foreach (var (size, frame) in await Sizes(file))
        {
            var clean = await reader.ReadAsync(frame);
            foreach (var (name, covered) in Covered(frame, OsdPosition.BottomRight))
            {
                var read = await reader.ReadAsync(covered);
                for (int i = 0; i < clean.Slots.Count; i++)
                    if (read.Slots[i].BattleSkill is { } state)
                        Assert.True(clean.Slots[i].BattleSkill == state, $"{size} {name}: slot {i + 1} battle skill {clean.Slots[i].BattleSkill} read {state}");
                for (int button = 0; button < PartyLayout.SlotCount; button++)
                    if (SkillRing.ReadSi(covered, button).Icon is var icon and not SkillButtonIcon.Unknown)
                        Assert.True(SkillRing.ReadSi(frame, button).Icon == icon, $"{size} {name}: Si's button {button} read {icon}");
            }
        }
    }

    /// <summary>
    /// The display at the top centre over the boss's status row: an icon the wiki doesn't have, seen through the
    /// display's backdrop, matched Amp (position 7, 1080p). Every status read under it is one the clean frame reads.
    /// </summary>
    [Theory]
    [InlineData("battle-hud/battle-96s.jpg")]
    [InlineData("infliction/ring-96_75s.jpg")]
    public async Task The_boss_statuses_under_a_display_are_the_clean_ones_or_unread(string file)
    {
        var reader = new BossStatusReader(await OperatorCache.LoadAsync(Path.Combine(Path.GetTempPath(), "endhud-osd-boss-" + Environment.ProcessId)));
        foreach (var (size, frame) in await Sizes(file))
        {
            var clean = reader.Read(frame).Select(s => (s.StatusId, s.Position)).ToList();
            foreach (var (name, covered) in Covered(frame, OsdPosition.TopCentre))
                foreach (var status in reader.Read(covered))
                    Assert.True(clean.Contains((status.StatusId, status.Position)), $"{size} {name}: {status.StatusId} at position {status.Position}");
        }
    }

    /// <summary>
    /// A squad of three with the display over slot 1: slot 1 showing nothing readable was taken for a screen without
    /// the squad panel, so the squad was read as four.
    /// </summary>
    [Fact]
    public async Task A_squad_of_three_stays_three_with_a_display_over_slot_1()
    {
        var reader = new PartyReader(new TextRecognizer());
        foreach (var (size, frame) in await Sizes("lasting-ultimate-background/zl-46s.jpg"))
        {
            Assert.Equal(3, (await reader.ReadAsync(frame)).SquadSize);
            foreach (var (name, covered) in Covered(frame, OsdPosition.BottomLeft))
                Assert.True((await reader.ReadAsync(covered)).SquadSize == 3, $"{size} {name}");
        }
    }
}
