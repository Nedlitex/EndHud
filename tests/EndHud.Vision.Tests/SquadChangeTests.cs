using EndHud.Vision;
using EndHud.Vision.Operators;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// A squad changed in the team menu (Fixtures/squad-change): when the panel comes back out of
/// combat, every slot is named again, even where the new operator's portrait looks like the old
/// one's in pixels.
/// </summary>
public sealed class SquadChangeTests : IDisposable
{
    private readonly string _userFolder = Path.Combine(Path.GetTempPath(), "endhud-tests", Guid.NewGuid().ToString("N"));
    private DateTime _clock = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public void Dispose()
    {
        try { Directory.Delete(_userFolder, recursive: true); } catch (IOException) { }
    }

    private static async Task<PartyState> ReadAsync(string fixture) =>
        await new PartyReader(new TextRecognizer()).ReadAsync(await ImageFile.LoadAsync(CombatFrameTests.Fixture(fixture)));

    private async Task<PartyState> ReadingAsync(SquadIdentifier identifier, PartyState state)
    {
        _clock += TimeSpan.FromSeconds(0.25);
        return await identifier.IdentifyAsync(state);
    }

    [Fact]
    public async Task A_new_squad_is_named_after_the_team_menu_even_when_a_portrait_looks_like_the_old_one()
    {
        var before = await ReadAsync(Path.Combine("controller", "another-squad.jpg"));
        var after = await ReadAsync(Path.Combine("squad-change", "exploration-new-squad.jpg"));
        Assert.True(before.InCombat);
        Assert.True(after.PanelVisible);
        Assert.False(after.InCombat);
        var identifier = new SquadIdentifier(await OperatorCache.LoadAsync(_userFolder), () => _clock, null);
        await identifier.PrepareAsync(before);

        // The first squad, settled in battle.
        PartyState named = before;
        for (int n = 0; n < SquadIdentifier.Window * 3; n++) named = await ReadingAsync(identifier, before);
        Assert.Equal(["Zhuang_Fangyi", "Perlica", "Arcane", "Liino"], named.Slots.Select(s => s.Operator?.Operator.Id));

        // The team menu: no panel for a few seconds.
        for (int n = 0; n < PresenceMemory.ReadingsToLeave + 4; n++) await ReadingAsync(identifier, new PartyState([]));

        // The new squad, out of combat. The old names may show while it's compared again, never a mix
        // once it's named.
        for (int n = 0; n < SquadIdentifier.MinReadings * 3; n++) named = await ReadingAsync(identifier, after);
        Assert.Equal(["Camille", "Laevatain", "Si", "Arcane"], named.Slots.Select(s => s.Operator?.Operator.Id));
    }

    /// <summary>
    /// The first time a portrait size is seen (the controller's smaller portraits, a new screen),
    /// the wiki avatars take a while to describe, and until then the newest operators have nothing
    /// to match: Si came closest to Arcane, was named Arcane, and kept it. She stays unnamed until
    /// the avatars are in, then is named; the others are named from their in-game references at once.
    /// </summary>
    [Theory]
    [InlineData("controller/combat.jpg")]
    [InlineData("squad-change/exploration-new-squad.jpg")]
    public async Task An_operator_without_a_built_in_portrait_is_not_named_as_someone_else_while_the_avatars_load(string fixture)
    {
        var state = await ReadAsync(fixture);
        var gate = new TaskCompletionSource();
        var identifier = new SquadIdentifier(await OperatorCache.LoadAsync(_userFolder), () => _clock, null, gate.Task);

        PartyState named = state;
        for (int n = 0; n < SquadIdentifier.Window * 2; n++)
        {
            named = await ReadingAsync(identifier, state);
            Assert.Null(named.Slots[2].Operator); // Si: never Arcane or anyone else
        }
        Assert.Equal(["Camille", "Laevatain", null, "Arcane"], named.Slots.Select(s => s.Operator?.Operator.Id));

        gate.SetResult();
        await identifier.PrepareAsync(state);
        for (int n = 0; n < SquadIdentifier.MinReadings * 2; n++) named = await ReadingAsync(identifier, state);
        Assert.Equal(["Camille", "Laevatain", "Si", "Arcane"], named.Slots.Select(s => s.Operator?.Operator.Id));
    }
}
