using System.Text.Json;
using EndHud.Vision;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// The boss's stagger bar (<see cref="StaggerBar"/>) under its HP bar, on the owner's two recordings of
/// one boss (Fixtures/stagger, labelled by eye): how far it has filled, the node boxes and how many the
/// fill has passed, the large stagger's yellow countdown and how much of it is left; unknown with no boss bar. At 4K
/// and on smaller screens; and a node box drawn swollen (the game's cue as the fill reaches it) is carried by
/// <see cref="StaggerMemory"/>, so the count of nodes reached doesn't drop for that second.
/// </summary>
public class StaggerBarTests
{
    private sealed record Label(string File, string? State, double? Progress, double? Countdown, double[]? Nodes, int? Reached, bool Swollen);

    private static readonly Label[] Labels = JsonSerializer.Deserialize<Label[]>(
        File.ReadAllText(CombatFrameTests.Fixture(Path.Combine("stagger", "labels.json"))),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    public static TheoryData<string> Files() => [.. Labels.Select(l => l.File)];

    private static Task<Frame> Load(string file) => ImageFile.LoadAsync(CombatFrameTests.Fixture(Path.Combine("stagger", file)));

    private static void AssertAsLabelled(Label label, StaggerReading? reading, double tolerance, bool nodes = true)
    {
        if (label.State is null)
        {
            Assert.Null(reading);
            return;
        }
        Assert.NotNull(reading);
        Assert.Equal(label.State == "staggered" ? StaggerState.Staggered : StaggerState.Building, reading.State);
        if (label.Progress is { } progress) Assert.InRange(reading.Progress, progress - tolerance, progress + tolerance);
        if (label.Countdown is { } countdown)
        {
            Assert.NotNull(reading.CountdownLeft);
            Assert.InRange(reading.CountdownLeft.Value, countdown - tolerance, countdown + tolerance);
            Assert.Equal(1, reading.Progress);
        }
        else Assert.Null(reading.CountdownLeft);
        if (nodes && label.Nodes is { } expected)
        {
            Assert.Equal(expected.Length, reading.Nodes.Count);
            for (int i = 0; i < expected.Length; i++) Assert.InRange(reading.Nodes[i], expected[i] - 0.01, expected[i] + 0.01);
            Assert.Equal(label.Reached, reading.NodesReached);
        }
    }

    [Theory]
    [MemberData(nameof(Files))]
    public async Task Reads_as_labelled_at_4K(string file) =>
        AssertAsLabelled(Labels.Single(l => l.File == file), StaggerBar.Read(await Load(file)), 0.02);

    public static TheoryData<string> Screens() => [SimulatedScreen.Qhd.Name, SimulatedScreen.FullHd.Name, SimulatedScreen.Hd.Name];

    /// <summary>On smaller screens: the state, fill and countdown everywhere; the node boxes (16 px wide at 4K) from 1080p up.</summary>
    [Theory]
    [MemberData(nameof(Screens))]
    public async Task Reads_as_labelled_on_smaller_screens(string screenName)
    {
        var screen = SimulatedScreen.Named(screenName);
        foreach (var label in Labels)
        {
            var (reading, why, _) = StaggerBar.Measure(screen.Render(await Load(label.File)));
            try
            {
                AssertAsLabelled(label, reading, 0.03, nodes: screen.Height >= 1080);
            }
            catch (Exception e)
            {
                throw new InvalidOperationException($"{label.File} on {screenName}: {reading} {why}", e);
            }
        }
    }

    public static TheoryData<string> NoBoss => new()
    {
        "exploration-levels-visible.png",
        Path.Combine("mode-switch", "keyboard-building.jpg"),
        Path.Combine("mode-switch", "controller-exploring-bright-sky.jpg"),
        Path.Combine("inn", "menu-dishes.jpg"),
        Path.Combine("panel-or-not", "squad-select-screen.jpg"),
        Path.Combine("controller", "photo-mode.jpg"),
    };

    [Theory]
    [MemberData(nameof(NoBoss))]
    public async Task Unknown_without_a_boss_bar(string file) =>
        Assert.Null(StaggerBar.Read(await ImageFile.LoadAsync(CombatFrameTests.Fixture(file))));

    /// <summary>
    /// The node box swells for about a second as the fill reaches it (fb 43.9-44.8 s, f2 67.1-68.1 s), and isn't found as a box
    /// then: the box seen a moment before is remembered, so the count reached goes 0 then 1 and stays 1.
    /// </summary>
    [Fact]
    public async Task A_swollen_node_box_is_remembered()
    {
        var memory = new StaggerMemory();
        memory.Track(StaggerBar.Read(await Load("fb-30s.jpg")), bossBar: true);
        var before = memory.Track(StaggerBar.Read(await Load("fb-41s.jpg")), bossBar: true);
        var swollen = memory.Track(StaggerBar.Read(await Load("fb-44_2s.jpg")), bossBar: true);
        var after = memory.Track(StaggerBar.Read(await Load("fb-50s.jpg")), bossBar: true);

        Assert.Equal(0, before!.NodesReached);
        Assert.Equal(1, swollen!.NodesReached);
        Assert.InRange(Assert.Single(swollen.Nodes), 0.49, 0.51);
        Assert.Equal(1, after!.NodesReached);
    }

    /// <summary>
    /// An on-screen display drawn into the game (<see cref="SyntheticOsd"/>; its top-centre position covers the bar's middle
    /// 22-78 %, a line of text crossing the bar's rows) at each position, with and without its backdrop, at 4K and 1080p:
    /// the bar reads as on the clean frame or unknown, never another state, fill, countdown or node. The same fields are
    /// in the slow combat gate (<see cref="OnScreenDisplayCombatGateTests"/>).
    /// </summary>
    [Theory]
    [MemberData(nameof(Files))]
    public async Task Under_a_display_reads_the_same_or_unknown(string file)
    {
        var source = await Load(file);
        var wrong = new List<string>();
        foreach (var (size, frame) in new[] { ("4K", source), ("1080p", SimulatedScreen.FullHd.Render(source)) })
        {
            var cleanReading = StaggerBar.Read(frame);
            var clean = Fields(cleanReading);
            foreach (var position in SyntheticOsd.Positions)
            foreach (bool background in new[] { true, false })
            {
                var (covered, _, bossBar) = StaggerBar.Measure(SyntheticOsd.Draw(frame, position, background));
                var (_, found) = OnScreenDisplayCombatGateTests.Compare(clean, Fields(covered));
                wrong.AddRange(found.Select(w => $"{size} {position}{(background ? " bg" : "")} {w}"));

                // What the app keeps (StaggerMemory): the clean frame read twice, then the covered one. Unread under the
                // boss's bar it holds the clean reading, so it must be that or unknown (no boss bar seen), never another.
                var memory = new StaggerMemory();
                memory.Track(cleanReading, bossBar: true);
                var known = Fields(memory.Track(cleanReading, bossBar: true));
                var kept = memory.Track(covered, bossBar);
                if (bossBar && kept is null) wrong.Add($"{size} {position}{(background ? " bg" : "")} kept nothing under the boss's bar");
                var (_, keptWrong) = OnScreenDisplayCombatGateTests.Compare(known, Fields(kept));
                wrong.AddRange(keptWrong.Select(w => $"{size} {position}{(background ? " bg" : "")} kept {w}"));
            }
        }
        Assert.True(wrong.Count == 0, string.Join("; ", wrong));

        static Dictionary<string, string?> Fields(StaggerReading? reading) =>
            OnScreenDisplayCombatGateTests.StaggerFields(reading).ToDictionary(f => f.Field, f => f.Value);
    }

    /// <summary>
    /// fb-41s.jpg with a yellow block over the bar's right half (rows 150-175, 4K x 1945-2145): the boss's HP bar is still
    /// there, the stagger bar no longer has its shape (pink from the left, yellow further on), so it can't be read.
    /// </summary>
    private static async Task<Frame> Covered41s()
    {
        var source = await Load("fb-41s.jpg");
        var bgra = (byte[])source.Bgra.Clone();
        var ui = new UiScaler(source.Width, source.Height);
        int x0 = (int)(source.Width / 2.0 + 25 * ui.Scale), x1 = (int)(source.Width / 2.0 + 225 * ui.Scale);
        for (int y = (int)(150 * ui.Scale); y <= (int)(175 * ui.Scale); y++)
        for (int x = x0; x <= x1; x++)
        {
            int i = (y * source.Width + x) * 4;
            (bgra[i], bgra[i + 1], bgra[i + 2]) = ((byte)85, (byte)200, (byte)230);
        }
        return new Frame(source.Width, source.Height, bgra);
    }

    private static readonly StaggerReading Zero = new(StaggerState.Building, 0, [], null);

    /// <summary>The same state, fill, countdown and node boxes (a record compares its node list by reference).</summary>
    private static void AssertSame(StaggerReading expected, StaggerReading? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal((expected.State, expected.Progress, expected.CountdownLeft), (actual.State, actual.Progress, actual.CountdownLeft));
        Assert.Equal(expected.Nodes, actual.Nodes);
    }

    /// <summary>
    /// Owner, 2026-10-25: "when stagger bar cannot be read, just use the last known value instead of hardcode that back to 0.
    /// only start from 0". Read at 0.45 with its node box, then covered under the boss's HP bar: still 0.45, the box kept.
    /// </summary>
    [Fact]
    public async Task Unread_under_a_boss_holds_the_last_reading()
    {
        var memory = new StaggerMemory();
        var read = StaggerBar.Read(await Load("fb-41s.jpg"));
        memory.Track(read, bossBar: true);
        var seen = memory.Track(read, bossBar: true)!;
        Assert.Single(seen.Nodes);

        var (covered, why, bossBar) = StaggerBar.Measure(await Covered41s());
        Assert.True(covered is null && bossBar, $"the covered frame should be unread under a boss bar: {covered} {why} {bossBar}");

        for (int i = 0; i < 3 * StaggerMemory.ForgetAfterReadings; i++)
        {
            var held = memory.Track(covered, bossBar);
            AssertSame(seen, held);
            Assert.Equal(0, held!.NodesReached);
        }
    }

    /// <summary>The boss's bar there but never read yet: building, 0, no node boxes (none made up).</summary>
    [Fact]
    public void Unread_from_the_start_under_a_boss_is_0()
    {
        var memory = new StaggerMemory();
        AssertSame(Zero, memory.Track(null, bossBar: true));
        AssertSame(Zero, memory.Track(null, bossBar: true));
    }

    /// <summary>A reading of 40 % then the bar unread: 40 %; a countdown held stays at its last length (not run down).</summary>
    [Fact]
    public void Unread_after_a_reading_holds_it_unchanged()
    {
        var memory = new StaggerMemory();
        var forty = new StaggerReading(StaggerState.Building, 0.40, [], null);
        memory.Track(forty, bossBar: true);
        for (int i = 0; i < 5; i++) AssertSame(forty, memory.Track(null, bossBar: true));

        var countdown = new StaggerReading(StaggerState.Staggered, 1, [], 0.56);
        memory.Track(countdown, bossBar: true);
        for (int i = 0; i < 5; i++) AssertSame(countdown, memory.Track(null, bossBar: true));

        // A new reading replaces what was held.
        var later = new StaggerReading(StaggerState.Building, 0.10, [], null);
        AssertSame(later, memory.Track(later, bossBar: true));
        AssertSame(later, memory.Track(null, bossBar: true));
    }

    /// <summary>
    /// No boss bar: nothing (null). Gone for <see cref="StaggerMemory.ForgetAfterReadings"/> readings (the fight over, the
    /// boss gone), the next boss starts from 0; a shorter gap (an effect over the HP bar's end for a moment) keeps it.
    /// </summary>
    [Fact]
    public void Boss_gone_then_back_starts_from_0()
    {
        var memory = new StaggerMemory();
        var forty = new StaggerReading(StaggerState.Building, 0.40, [0.5], null);
        memory.Track(forty, bossBar: true);
        memory.Track(forty, bossBar: true);

        for (int i = 0; i < StaggerMemory.ForgetAfterReadings - 1; i++) Assert.Null(memory.Track(null, bossBar: false));
        AssertSame(forty, memory.Track(null, bossBar: true));

        for (int i = 0; i < StaggerMemory.ForgetAfterReadings; i++) Assert.Null(memory.Track(null, bossBar: false));
        AssertSame(Zero, memory.Track(null, bossBar: true));
    }

    /// <summary>
    /// A boss with no node boxes (owner: "a boss can have 0 nodes", only the large stagger): Typhoeus (reactions/
    /// level-I-typhoeus-*). Building reads its fill with no nodes and none reached; the large stagger still reads, with
    /// none reached; held while unread, none are made up.
    /// </summary>
    [Fact]
    public async Task A_boss_without_node_boxes()
    {
        static Task<Frame> Typhoeus(string s) => ImageFile.LoadAsync(CombatFrameTests.Fixture(Path.Combine("reactions", $"level-I-typhoeus-{s}.jpg")));

        var building = StaggerBar.Read(await Typhoeus("24s"));
        Assert.NotNull(building);
        Assert.Equal(StaggerState.Building, building.State);
        Assert.InRange(building.Progress, 0.08, 0.15);
        Assert.Empty(building.Nodes);
        Assert.Equal(0, building.NodesReached);

        var staggered = StaggerBar.Read(await Typhoeus("98s"));
        Assert.NotNull(staggered);
        Assert.Equal(StaggerState.Staggered, staggered.State);
        Assert.NotNull(staggered.CountdownLeft);
        Assert.Empty(staggered.Nodes);
        Assert.Equal(0, staggered.NodesReached);

        var memory = new StaggerMemory();
        memory.Track(building, bossBar: true);
        memory.Track(building, bossBar: true);
        var held = memory.Track(null, bossBar: true);
        AssertSame(building, held);
        Assert.Equal(0, held!.NodesReached);
    }

    /// <summary>The memory forgets a boss's nodes once no bar has been seen for a while (another boss may have others).</summary>
    [Fact]
    public async Task The_memory_forgets_without_a_bar()
    {
        var memory = new StaggerMemory();
        var seen = StaggerBar.Read(await Load("fb-41s.jpg"))!;
        memory.Track(seen, bossBar: true);
        memory.Track(seen, bossBar: true);
        for (int i = 0; i < StaggerMemory.ForgetAfterReadings - 1; i++) Assert.Null(memory.Track(null, bossBar: false));
        Assert.Single(memory.Track(seen with { Nodes = [] }, bossBar: true)!.Nodes);
        for (int i = 0; i < StaggerMemory.ForgetAfterReadings; i++) Assert.Null(memory.Track(null, bossBar: false));

        Assert.Empty(memory.Track(seen with { Nodes = [] }, bossBar: true)!.Nodes);
    }

    /// <summary>
    /// A box seen in one reading only isn't counted: a false one at the fill's end (before the clearance rule, seven over the
    /// two recordings, each in one reading) was kept for the rest of the fight and counted as a second node reached.
    /// </summary>
    [Fact]
    public async Task A_box_seen_once_is_not_counted()
    {
        var memory = new StaggerMemory();
        var seen = StaggerBar.Read(await Load("fb-50s.jpg"))!;
        memory.Track(seen, bossBar: true);
        memory.Track(seen, bossBar: true);

        var once = memory.Track(seen with { Nodes = [.. seen.Nodes, 0.58] }, bossBar: true);
        var next = memory.Track(seen, bossBar: true);

        Assert.Equal(1, once!.NodesReached);
        Assert.Equal(1, next!.NodesReached);
    }
}
