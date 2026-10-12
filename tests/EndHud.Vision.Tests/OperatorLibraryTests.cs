using EndHud.Vision;
using EndHud.Vision.Operators;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// The operator library: EndHud ships no game art, so without a download it's empty; with the
/// wiki's roster (<see cref="OperatorCache"/>) it has every operator's avatar and skill icons, and
/// recognises a squad once the user has taught it.
/// </summary>
public sealed class OperatorLibraryTests : IDisposable
{
    private readonly string _userFolder = NewFolder();

    private static string NewFolder() => Path.Combine(Path.GetTempPath(), "endhud-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_userFolder, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task Without_a_download_the_library_is_empty_but_has_the_built_in_calibration()
    {
        var library = await OperatorLibrary.LoadAsync(_userFolder);

        Assert.True(library.IsEmpty);
        Assert.Empty(library.Operators);
        Assert.NotNull(library.Calibration); // a few numbers, compiled in: not game art
    }

    [Fact]
    public void EndHud_ships_no_game_art()
    {
        // The roster, avatars and skill icons are downloaded, never compiled in (they're the game's
        // art). The only resource is the calibration: a few numbers.
        Assert.Equal(["calibration.json"], typeof(OperatorLibrary).Assembly.GetManifestResourceNames());
    }

    [Fact]
    public async Task Downloaded_library_has_every_operator_with_avatar_and_skill_icons()
    {
        var library = await OperatorCache.LoadAsync(_userFolder);

        Assert.True(library.Operators.Count >= 34);
        Assert.Matches(@"^\p{IsCJKUnifiedIdeographs}+$", library.Find("Laevatain")!.NameZh); // the Chinese name, not the English fallback

        // Every operator the wiki has skill icons for: one battle skill, combo and ultimate each (at least).
        var withSkills = library.Operators.Where(o => o.Skills.Count > 0).ToList();
        Assert.True(withSkills.Count >= 32, $"only {withSkills.Count} operators have skill icons");
        Assert.All(withSkills, o => Assert.Equal(
            [SkillKind.BattleSkill, SkillKind.Combo, SkillKind.Ultimate],
            o.Skills.Select(s => s.Kind).Distinct().Order()));

        Assert.All(library.Operators, o => Assert.NotNull(library.FindAvatar(o.Id)));
        foreach (var icon in library.Find("Laevatain")!.Skills)
            Assert.InRange((await library.LoadSkillIconAsync(icon)).Width, 60, 160);
    }

    [Fact]
    public async Task Once_taught_the_squad_is_recognised_in_a_frame_it_never_saw()
    {
        // Teach it on one screenshot, as a user does in the menu…
        string[] squad = ["Laevatain", "Camille", "Arcane", "Ardelia"];
        var reader = new PartyReader(new TextRecognizer());
        var library = await OperatorCache.LoadAsync(_userFolder);
        var teacher = new SquadIdentifier(library);
        await teacher.IdentifyAsync(await reader.ReadAsync(await ImageFile.LoadAsync(CombatFrameTests.Fixture("exploration-levels-visible.png"))));
        for (int i = 0; i < 4; i++) await teacher.CorrectAsync(i + 1, library.Find(squad[i]));

        // …then a later session recognises it in combat.
        var identifier = new SquadIdentifier(await OperatorCache.LoadAsync(_userFolder));
        var state = await identifier.IdentifyAsync(await reader.ReadAsync(await ImageFile.LoadAsync(CombatFrameTests.Fixture("combat-slot1-hurt.jpg"))));

        Assert.Equal(squad, state.Slots.Select(s => s.Operator?.Operator.Id));
        Assert.All(state.Slots, s => Assert.True(s.Operator!.FromLearned));
    }

    [Theory]
    [InlineData("Skill-Laevatain.png", SkillKind.BattleSkill)]
    [InlineData("Combo-Lastrite.png", SkillKind.Combo)]
    [InlineData("Ult-Arcane2.png", SkillKind.Ultimate)]
    [InlineData("Laevatain_icon.png", null)]
    [InlineData("Skill-Laevatain.jpg", null)]
    public void Classifies_wiki_skill_icon_files(string file, SkillKind? expected) =>
        Assert.Equal(expected, WikiOperatorSource.ClassifySkillFile(file));
}
