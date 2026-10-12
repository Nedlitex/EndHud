using EndHud.Vision;
using EndHud.Vision.Operators;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// Operator recognition on the 4K fixture, whose squad is (by eye) Laevatain, Camille, Arcane,
/// Ardelia. Uses a copy of the wiki icon library in Fixtures/operators (local test data only).
/// </summary>
public sealed class SquadIdentifierTests : IDisposable
{
    private static readonly string Fixture4K = Path.Combine(AppContext.BaseDirectory, "Fixtures", "party-4k.png");
    private static readonly string FixtureLibrary = Path.Combine(AppContext.BaseDirectory, "Fixtures", "operators");
    private static readonly string[] Truth = ["Laevatain", "Camille", "Arcane", "Ardelia"];

    private readonly string _libraryDir = Path.Combine(Path.GetTempPath(), "endhud-tests", Guid.NewGuid().ToString("N"));

    public SquadIdentifierTests()
    {
        // Learning writes into the library, so every test gets its own copy.
        foreach (string file in Directory.GetFiles(FixtureLibrary, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(_libraryDir, Path.GetRelativePath(FixtureLibrary, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    public void Dispose()
    {
        try { Directory.Delete(_libraryDir, recursive: true); } catch (IOException) { }
    }

    private static async Task<PartyState> ReadAsync(Frame frame) => await new PartyReader(new TextRecognizer()).ReadAsync(frame);

    private static async Task<Frame> Fixture1440Async() => (await ImageFile.LoadAsync(Fixture4K)).Resize(2560, 1440);

    [Fact]
    public async Task Wiki_icons_rank_the_right_operator_near_the_top_for_every_slot()
    {
        var state = await ReadAsync(await ImageFile.LoadAsync(Fixture4K));
        var matcher = new PortraitMatcher(await OperatorLibrary.LoadAsync(_libraryDir));

        for (int i = 0; i < 4; i++)
        {
            var top = matcher.MatchIcon(state.Slots[i].Portrait).Ranking.Take(5).Select(r => r.Operator.Id);
            Assert.Contains(Truth[i], top);
        }
    }

    [Fact]
    public async Task Learned_portraits_identify_the_whole_squad_at_another_resolution()
    {
        // Teach: the user picks the right operator for each slot once, at 4K.
        var library = await OperatorLibrary.LoadAsync(_libraryDir);
        var teacher = new SquadIdentifier(library);
        await teacher.IdentifyAsync(await ReadAsync(await ImageFile.LoadAsync(Fixture4K)));
        for (int i = 0; i < 4; i++)
            await teacher.CorrectAsync(i + 1, library.Find(Truth[i]));

        // Later session (library reloaded from disk), different resolution.
        var identifier = new SquadIdentifier(await OperatorLibrary.LoadAsync(_libraryDir));
        var state = await identifier.IdentifyAsync(await ReadAsync(await Fixture1440Async()));

        Assert.Equal(Truth, state.Slots.Select(s => s.Operator?.Operator.Id));
        Assert.All(state.Slots, s => Assert.True(s.Operator!.FromLearned));
    }

    [Fact]
    public async Task Correcting_a_wrong_answer_unlearns_it_and_learns_the_right_one()
    {
        var library = await OperatorLibrary.LoadAsync(_libraryDir);
        var identifier = new SquadIdentifier(library);
        var frame = await ImageFile.LoadAsync(Fixture4K);
        await identifier.IdentifyAsync(await ReadAsync(frame));

        // A wrong pick gets learned…
        await identifier.CorrectAsync(1, library.Find("Akekuri"));
        var wrong = await new SquadIdentifier(await OperatorLibrary.LoadAsync(_libraryDir)).IdentifyAsync(await ReadAsync(frame));
        Assert.Equal("Akekuri", wrong.Slots[0].Operator?.Operator.Id);

        // …and correcting it removes that portrait from Akekuri and gives it to Laevatain.
        var reloaded = await OperatorLibrary.LoadAsync(_libraryDir);
        var corrector = new SquadIdentifier(reloaded);
        await corrector.IdentifyAsync(await ReadAsync(frame));
        await corrector.CorrectAsync(1, reloaded.Find("Laevatain"));

        var fixedLibrary = await OperatorLibrary.LoadAsync(_libraryDir);
        Assert.Equal(0, fixedLibrary.LearnedCount("Akekuri"));
        var right = await new SquadIdentifier(fixedLibrary).IdentifyAsync(await ReadAsync(frame));
        Assert.Equal("Laevatain", right.Slots[0].Operator?.Operator.Id);
    }

    [Fact]
    public async Task Clearing_a_slot_leaves_it_unrecognised()
    {
        var library = await OperatorLibrary.LoadAsync(_libraryDir);
        var identifier = new SquadIdentifier(library);
        await identifier.IdentifyAsync(await ReadAsync(await ImageFile.LoadAsync(Fixture4K)));

        await identifier.CorrectAsync(2, null);

        Assert.Null(identifier.Current[1]!.Match);
    }

    [Fact]
    public async Task Forgetting_all_learned_portraits_returns_to_wiki_guesses()
    {
        var library = await OperatorLibrary.LoadAsync(_libraryDir);
        var identifier = new SquadIdentifier(library);
        await identifier.IdentifyAsync(await ReadAsync(await ImageFile.LoadAsync(Fixture4K)));
        await identifier.CorrectAsync(1, library.Find("Laevatain"));

        library.ForgetAllLearned();

        var reloaded = await OperatorLibrary.LoadAsync(_libraryDir);
        Assert.Equal(0, reloaded.LearnedCount("Laevatain"));
        Assert.Null(reloaded.Calibration);
    }
}
