using EndHud.Vision;
using EndHud.Vision.Operators;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// Squads of fewer than four (Fixtures/small-squads, controller/two-operators.jpg). With one
/// operator nothing showed; with two, the empty places were named as two more operators and,
/// with keyboard and mouse, the ultimates were read from the wrong circles (they're drawn from the
/// right, the squad from the left).
/// </summary>
public sealed class SmallSquadTests : IDisposable
{
    private readonly string _userFolder = Path.Combine(Path.GetTempPath(), "endhud-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_userFolder, recursive: true); } catch (IOException) { }
    }

    private static async Task<PartyState> ReadAsync(string fixture) =>
        await new PartyReader(new TextRecognizer()).ReadAsync(await ImageFile.LoadAsync(CombatFrameTests.Fixture(fixture)));

    /// <summary>Ultimate charge per operator: 1 = ready.</summary>
    public static TheoryData<string, HudLayout, double[]> Squads() => new()
    {
        { Path.Combine("small-squads", "one-operator.jpg"), HudLayout.KeyboardMouse, [0] },
        { Path.Combine("small-squads", "two-operators.jpg"), HudLayout.KeyboardMouse, [0.57, 0.32] },
        { Path.Combine("controller", "two-operators.jpg"), HudLayout.Controller, [1, 0.89] },
    };

    [Theory]
    [MemberData(nameof(Squads))]
    public async Task A_smaller_squad_is_read_as_it_is(string fixture, HudLayout layout, double[] ultimates)
    {
        var state = await ReadAsync(fixture);

        Assert.Equal(layout, state.Layout);
        Assert.Equal(ultimates.Length, state.SquadSize);
        Assert.True(state.PanelVisible);
        Assert.True(state.InCombat);
        for (int i = 0; i < 4; i++)
        {
            var slot = state.Slots[i];
            if (i >= ultimates.Length)
            {
                // An empty place: nothing read there.
                Assert.True(slot.Empty, $"slot {i + 1} should be empty");
                Assert.Null(slot.Hp);
                Assert.Null(slot.Combo);
                Assert.Null(slot.Ultimate);
                Assert.False(slot.Dead);
                continue;
            }
            Assert.False(slot.Empty);
            Assert.True(slot.Hp > 0.5, $"slot {i + 1}: HP {slot.Hp}");
            Assert.True(slot.Ultimate is not null, $"slot {i + 1}: ultimate unread");
            Assert.Equal(ultimates[i] >= 1, slot.Ultimate.Ready);
            if (ultimates[i] < 1) Assert.InRange(slot.Ultimate.Progress, ultimates[i] - 0.06, ultimates[i] + 0.06);
        }
    }

    [Fact]
    public async Task Only_the_operators_there_are_named_and_nothing_is_kept_for_the_empty_places()
    {
        var clock = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var four = await ReadAsync(Path.Combine("controller", "keyboard-mouse-same-fight.jpg"));
        var two = await ReadAsync(Path.Combine("small-squads", "two-operators.jpg"));
        var identifier = new SquadIdentifier(await OperatorCache.LoadAsync(_userFolder), () => clock, null);
        await identifier.PrepareAsync(four);
        var tracker = new PartyTracker();

        // A full squad first, then (after the team menu) two of them.
        for (int n = 0; n < SquadIdentifier.Window * 2; n++) { clock += TimeSpan.FromSeconds(0.25); await identifier.IdentifyAsync(tracker.Track(four)); }
        for (int n = 0; n < 40; n++) { clock += TimeSpan.FromSeconds(0.25); await identifier.IdentifyAsync(tracker.Track(new PartyState([]))); }
        PartyState named = two;
        for (int n = 0; n < SquadIdentifier.MinReadings * 3; n++)
        {
            clock += TimeSpan.FromSeconds(0.25);
            named = await identifier.IdentifyAsync(tracker.Track(two));
            Assert.All(named.Slots.Skip(2), s => Assert.Null(s.Operator));
            Assert.All(named.Slots.Skip(2), s => Assert.Null(s.Ultimate)); // nothing carried from the full squad
        }

        Assert.Equal(["Arcane", "Camille", null, null], named.Slots.Select(s => s.Operator?.Operator.Id));
    }
}
