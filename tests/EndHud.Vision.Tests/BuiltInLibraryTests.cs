using System.Text.Json;
using EndHud.Vision;
using EndHud.Vision.Operators;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// The operator library compiled into EndHud (src/EndHud.Vision/Assets/operators): roster,
/// avatar and skill icons, and portraits learned from gameplay — plus how a user's own data
/// layers on top of it.
/// </summary>
public sealed class BuiltInLibraryTests : IDisposable
{
    private readonly string _userFolder = NewFolder();

    private static string NewFolder() => Path.Combine(Path.GetTempPath(), "endhud-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_userFolder, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task Built_in_library_has_every_operator_with_avatar_and_skill_icons()
    {
        var library = await OperatorLibrary.LoadAsync(_userFolder);

        Assert.False(library.UsesDownloadedReference);
        Assert.True(library.Operators.Count >= 34);
        Assert.Matches(@"^\p{IsCJKUnifiedIdeographs}+$", library.Find("Laevatain")!.NameZh); // the Chinese name, not the English fallback

        // Every operator the wiki has skill icons for: one battle skill, combo and ultimate each (at least).
        // Si and Argent Flow have none on the wiki yet.
        var withSkills = library.Operators.Where(o => o.Skills.Count > 0).ToList();
        Assert.True(withSkills.Count >= 32, $"only {withSkills.Count} operators have skill icons");
        Assert.All(withSkills, o => Assert.Equal(
            [SkillKind.BattleSkill, SkillKind.Combo, SkillKind.Ultimate],
            o.Skills.Select(s => s.Kind).Distinct().Order()));

        // And they decode, straight from the executable.
        foreach (var icon in library.Find("Laevatain")!.Skills)
        {
            var frame = await library.LoadSkillIconAsync(icon);
            Assert.InRange(frame.Width, 60, 160);
        }
    }

    [Fact]
    public async Task Built_in_learned_portraits_recognise_the_squad_in_a_frame_they_never_saw()
    {
        // The built-in portraits were learned from exploration-levels-visible and
        // combat-combos-nearly-refilled; this frame is new to them.
        var library = await OperatorLibrary.LoadAsync(_userFolder);
        var frame = await ImageFile.LoadAsync(CombatFrameTests.Fixture("combat-slot1-hurt.jpg"));
        var state = await new SquadIdentifier(library).IdentifyAsync(await new PartyReader(new TextRecognizer()).ReadAsync(frame));

        Assert.Equal(["Laevatain", "Camille", "Arcane", "Ardelia"], state.Slots.Select(s => s.Operator?.Operator.Id));
        Assert.All(state.Slots, s => Assert.True(s.Operator!.FromLearned));
    }

    [Fact]
    public async Task Unlearning_a_built_in_portrait_sticks_for_that_user_only()
    {
        var library = await OperatorLibrary.LoadAsync(_userFolder);
        Assert.True(library.LearnedCount("Laevatain") > 0);

        library.Unlearn("Laevatain", _ => true);

        // Recognition no longer uses them: slot 1 is no longer matched from a learned portrait.
        var reloaded = await OperatorLibrary.LoadAsync(_userFolder);
        Assert.Equal(0, reloaded.LearnedCount("Laevatain"));
        var frame = await ImageFile.LoadAsync(CombatFrameTests.Fixture("combat-slot1-hurt.jpg"));
        var state = await new SquadIdentifier(reloaded).IdentifyAsync(await new PartyReader(new TextRecognizer()).ReadAsync(frame));
        Assert.False(state.Slots[0].Operator is { FromLearned: true, Operator.Id: "Laevatain" });

        // Another user (another folder) still has them.
        Assert.True((await OperatorLibrary.LoadAsync(NewFolder())).LearnedCount("Laevatain") > 0);
    }

    [Fact]
    public async Task Forgetting_everything_includes_built_in_portraits()
    {
        var library = await OperatorLibrary.LoadAsync(_userFolder);

        library.ForgetAllLearned();

        var reloaded = await OperatorLibrary.LoadAsync(_userFolder);
        Assert.All(new[] { "Laevatain", "Camille", "Arcane", "Ardelia" }, id => Assert.Equal(0, reloaded.LearnedCount(id)));
        Assert.NotNull(reloaded.Calibration); // the built-in framing calibration still applies
    }

    [Fact]
    public async Task A_downloaded_roster_takes_precedence_over_the_built_in_one()
    {
        // As the tray's "update operator library" leaves it: a complete roster in <user>/reference.
        string reference = Path.Combine(_userFolder, OperatorLibrary.ReferenceFolderName);
        Directory.CreateDirectory(Path.Combine(reference, OperatorLibrary.IconFolderName));
        var op = new OperatorInfo("Laevatain", "Laevatain", "Laevatain", "Laevatain.png");
        File.WriteAllText(Path.Combine(reference, "operators.json"), JsonSerializer.Serialize(new[] { op }));
        File.WriteAllBytes(Path.Combine(reference, OperatorLibrary.IconFolderName, "Laevatain.png"),
            EmbeddedAssets.Instance.Read("icons/Laevatain.png"));

        var library = await OperatorLibrary.LoadAsync(_userFolder);

        Assert.True(library.UsesDownloadedReference);
        Assert.Equal(["Laevatain"], library.Operators.Select(o => o.Id));
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
