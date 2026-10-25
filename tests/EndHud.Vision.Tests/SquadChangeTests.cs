using EndHud.Vision;
using EndHud.Vision.Operators;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// A squad changed in the team menu (Fixtures/squad-change): when the panel comes back out of
/// combat, every slot is named again, even where the new operator's portrait looks like the old
/// one's in pixels.
/// </summary>
[Trait("Category", "Slow")] // recognises operators with the model: left out of the fast lane (scripts/test.ps1 -Fast)
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

        // The new squad, out of combat. At no reading is an old name shown where the operator changed
        // (slot 3 was Arcane and is Si now; slot 4 is Arcane, as before).
        string[] truth = ["Camille", "Laevatain", "Si", "Arcane"];
        for (int n = 0; n < SquadIdentifier.MinReadings * 3; n++)
        {
            named = await ReadingAsync(identifier, after);
            foreach (var slot in named.Slots.Where(s => s.Operator is not null))
                Assert.True(slot.Operator!.Operator.Id == truth[slot.Number - 1], $"reading {n + 1}: slot {slot.Number} shown as {slot.Operator.Operator.Id}");
        }
        Assert.Equal(truth, named.Slots.Select(s => s.Operator?.Operator.Id));
    }

    /// <summary>
    /// With a controller the squad panel isn't drawn out of combat, so after the team menu it
    /// comes back straight into a fight (the owner's third controller recording: about 30 s in the
    /// menu). Slot 1 kept Camille's name for Zhuang Fangyi, whose portrait scores 0.81 against hers.
    /// </summary>
    [Fact]
    public async Task A_new_squad_is_named_when_the_panel_comes_back_into_a_fight_after_the_team_menu()
    {
        var before = await ReadAsync(Path.Combine("controller", "combat.jpg"));
        var after = await ReadAsync(Path.Combine("controller", "another-squad.jpg"));
        Assert.True(before.InCombat);
        Assert.True(after.InCombat);
        var identifier = new SquadIdentifier(await OperatorCache.LoadAsync(_userFolder), () => _clock, null);
        await identifier.PrepareAsync(before);

        PartyState named = before;
        for (int n = 0; n < SquadIdentifier.Window * 3; n++) named = await ReadingAsync(identifier, before);
        Assert.Equal(["Camille", "Laevatain", "Si", "Arcane"], named.Slots.Select(s => s.Operator?.Operator.Id));

        // 30 s in the team menu: no panel.
        for (int n = 0; n < 120; n++) await ReadingAsync(identifier, new PartyState([]));

        // At no reading is the old squad shown: a slot is the new operator, or nobody yet.
        string[] truth = ["Zhuang_Fangyi", "Perlica", "Arcane", "Liino"];
        for (int n = 0; n < SquadIdentifier.MinReadings * 3; n++)
        {
            named = await ReadingAsync(identifier, after);
            foreach (var slot in named.Slots.Where(s => s.Operator is not null))
                Assert.True(slot.Operator!.Operator.Id == truth[slot.Number - 1], $"reading {n + 1}: slot {slot.Number} shown as {slot.Operator.Operator.Id}");
        }
        Assert.Equal(truth, named.Slots.Select(s => s.Operator?.Operator.Id));
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
