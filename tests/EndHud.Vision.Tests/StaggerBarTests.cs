using System.Text.Json;
using EndHud.Vision;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// The boss's stagger bar (失衡条, <see cref="StaggerBar"/>) under its HP bar, on the owner's two recordings of
/// 蚀影应龙队长 (Fixtures/stagger, labelled by eye): how far it has filled, the node boxes (失衡节点) and how many the
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
            var reading = StaggerBar.Read(screen.Render(await Load(label.File)));
            try
            {
                AssertAsLabelled(label, reading, 0.03, nodes: screen.Height >= 1080);
            }
            catch (Exception e)
            {
                throw new InvalidOperationException($"{label.File} on {screenName}: {reading}", e);
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
        var before = memory.Track(StaggerBar.Read(await Load("fb-41s.jpg")));
        var swollen = memory.Track(StaggerBar.Read(await Load("fb-44_2s.jpg")));
        var after = memory.Track(StaggerBar.Read(await Load("fb-50s.jpg")));

        Assert.Equal(0, before!.NodesReached);
        Assert.Equal(1, swollen!.NodesReached);
        Assert.InRange(Assert.Single(swollen.Nodes), 0.49, 0.51);
        Assert.Equal(1, after!.NodesReached);
    }

    /// <summary>The memory forgets a boss's nodes once no bar has been seen for a while (another boss may have others).</summary>
    [Fact]
    public async Task The_memory_forgets_without_a_bar()
    {
        var memory = new StaggerMemory();
        memory.Track(StaggerBar.Read(await Load("fb-41s.jpg")));
        for (int i = 0; i < StaggerMemory.ForgetAfterReadings; i++) Assert.Null(memory.Track(null));
        var reading = StaggerBar.Read(await Load("fb-41s.jpg"))! with { Nodes = [] };

        Assert.Empty(memory.Track(reading)!.Nodes);
    }
}
