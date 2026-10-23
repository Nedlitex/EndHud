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
    public async Task Without_a_download_the_library_is_empty()
    {
        var library = await OperatorLibrary.LoadAsync(_userFolder);

        Assert.True(library.IsEmpty);
        Assert.Empty(library.Operators);
    }

    [Fact]
    public void The_portrait_references_cover_the_roster()
    {
        // The in-game portrait references are the model's numbers, not images (the operator package
        // with the art itself is OperatorPackageTests').
        Assert.All(PortraitReferences.BuiltIn, r => Assert.Equal(768, r.Features.Length));
        Assert.True(PortraitReferences.BuiltIn.Select(r => r.OperatorId).Distinct().Count() >= 32);
    }

    [Theory]
    [InlineData("""{ "Model": "ccip-caformer-24-randaug-pruned-int8", "References": [] }""")] // an older layout
    [InlineData("""{ "Model": "another-model", "Sizes": [] }""")]
    [InlineData("""{ "Model": "ccip-caformer-24-randaug-pruned-int8", "Sizes": [ { "Screen": "1920x1080", "PortraitWidth": 64, "Operators": { "Akekuri": ["not base64!"] } } ] }""")]
    [InlineData("not json")]
    public void Unusable_portrait_references_mean_none_rather_than_an_error(string json)
    {
        // Recognition then falls back to the wiki avatars instead of failing altogether.
        Assert.Empty(PortraitReferences.Read(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json))));
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

    /// <summary>
    /// Every skill icon comes out square with its drawing in the middle (within a pixel), so it sits
    /// centred in its circle: some wiki files aren't (Gilberta's combo was 8 % left of centre, seen in
    /// the rotation editor's preview).
    /// </summary>
    [Fact]
    public async Task Every_skill_icon_is_drawn_centred()
    {
        var library = await OperatorCache.LoadAsync(_userFolder);
        var off = new List<string>();
        foreach (var icon in library.Operators.SelectMany(o => o.Skills))
        {
            var frame = await library.LoadSkillIconAsync(icon);
            int left = frame.Width, top = frame.Height, right = -1, bottom = -1;
            for (int y = 0; y < frame.Height; y++)
            for (int x = 0; x < frame.Width; x++)
                if (frame.GetAlpha(x, y) > 40) { left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y); }
            double cx = (left + right + 1) / 2.0 - frame.Width / 2.0, cy = (top + bottom + 1) / 2.0 - frame.Height / 2.0;
            if (frame.Width != frame.Height || Math.Abs(cx) > 1 || Math.Abs(cy) > 1)
                off.Add($"{icon.File}: {frame.Width}x{frame.Height}, centre off by ({cx:F1}, {cy:F1})");
        }
        Assert.True(off.Count == 0, string.Join("; ", off));
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
