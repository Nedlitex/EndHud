using EndHud.Vision;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// The top-left mode switch (<see cref="ModePill"/>), found by its shape: on 探索 or 工业 with its plain
/// icon (out of battle), with its red fight mark (a fight in the open world), and not there in an
/// instance's battle (the evacuate bar, 撤离, in its place), in both layouts and on every screen size.
/// </summary>
public class ModePillTests
{
    private static async Task<Frame> Load(string file) => await ImageFile.LoadAsync(CombatFrameTests.Fixture(file));

    private static ModePillReading Read(Frame frame) => ModePill.Read(frame, new UiScaler(frame.Width, frame.Height));

    public static TheoryData<string> OutOfBattle => new()
    {
        "exploration-levels-visible.png",                       // keyboard and mouse, 探索
        "exploration-dark-portraits.jpg",
        "exploration-scenery-behind-ultimate.jpg",
        Path.Combine("controller", "switch-keyboard-exploration.jpg"),
        Path.Combine("mode-switch", "controller-exploring.jpg"), // controller, 探索, D-pad arrow
        Path.Combine("mode-switch", "keyboard-building.jpg"),    // keyboard and mouse, 工业
    };

    public static TheoryData<string> Fighting => new()
    {
        Path.Combine("controller", "ultimates-charging.jpg"),
        Path.Combine("controller", "ultimates-charging-one-ready.jpg"),
        Path.Combine("controller", "ultimates-two-ready.jpg"),
    };

    public static TheoryData<string> NotShown => new()
    {
        // Battles in an instance: the evacuate bar where the switch would be.
        "combat-all-ready.jpg",
        "battle-low-hp.jpg",
        "battle-dimmed-squad-heal.jpg",
        "battle-squad-wiped.jpg",
        Path.Combine("controller", "combat.jpg"),
        Path.Combine("controller", "fullbattle-lb-held.jpg"),
        Path.Combine("controller", "keyboard-mouse-same-fight.jpg"),
        // The evacuate bar over a white sky; no switch drawn (cutscene, photo mode, a menu).
        Path.Combine("panel-or-not", "white-sky-after-a-failed-fight.jpg"),
        Path.Combine("panel-or-not", "squad-select-screen.jpg"),
        Path.Combine("controller", "fullbattle3-si-ultimate-cutscene.jpg"),
        Path.Combine("controller", "photo-mode.jpg"),
        Path.Combine("controller", "target-wheel.jpg"),
        "ultimate-charge-near-half.jpg",
    };

    [Theory]
    [MemberData(nameof(OutOfBattle))]
    public async Task The_switch_with_its_plain_icon_is_out_of_battle(string file) =>
        Assert.Equal(ModePillReading.OutOfBattle, Read(await Load(file)));

    [Theory]
    [MemberData(nameof(Fighting))]
    public async Task The_switch_with_its_red_mark_is_a_fight(string file) =>
        Assert.Equal(ModePillReading.Fighting, Read(await Load(file)));

    [Theory]
    [MemberData(nameof(NotShown))]
    public async Task No_switch_in_an_instance_battle_or_without_the_hud(string file) =>
        Assert.Equal(ModePillReading.NotShown, Read(await Load(file)));

    /// <summary>The evacuate bar is a white rounded bar in the same place: a clear margin below the switch's match.</summary>
    [Fact]
    public async Task The_evacuate_bar_scores_well_below_the_switch()
    {
        var bar = await Load("combat-all-ready.jpg");
        var exploring = await Load("exploration-levels-visible.png");
        var building = await Load(Path.Combine("mode-switch", "keyboard-building.jpg"));
        double Score(Frame f) => ModePill.Measure(f, new UiScaler(f.Width, f.Height)).Shape;

        Assert.InRange(Score(bar), 0, ModePill.MatchAt - 0.08);
        Assert.InRange(Score(exploring), ModePill.MatchAt + 0.1, 1);
        Assert.InRange(Score(building), ModePill.MatchAt + 0.1, 1);
    }

    public static TheoryData<string, string> Screens()
    {
        var data = new TheoryData<string, string>();
        foreach (var screen in SimulatedScreen.All)
        {
            data.Add(screen.Name, "exploration-levels-visible.png");
            data.Add(screen.Name, Path.Combine("mode-switch", "keyboard-building.jpg"));
            data.Add(screen.Name, Path.Combine("controller", "ultimates-charging.jpg"));
            data.Add(screen.Name, "combat-all-ready.jpg");
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public async Task Read_the_same_on_every_screen_size(string screen, string file)
    {
        var source = await Load(file);
        var expected = Read(source);
        Assert.Equal(expected, Read(SimulatedScreen.Named(screen).Render(source)));
    }

    /// <summary>
    /// Whatever the squad panel shows, the switch with its plain icon is not a battle; read through
    /// the whole reader (<see cref="PartyReader"/>), as the overlay does.
    /// </summary>
    [Theory]
    [MemberData(nameof(OutOfBattle))]
    public async Task The_reader_puts_it_on_the_reading(string file)
    {
        var state = await new PartyReader(new TextRecognizer()).ReadAsync(await Load(file));
        Assert.Equal(ModePillReading.OutOfBattle, state.ModePill);
        Assert.False(state.InCombat);
    }
}
