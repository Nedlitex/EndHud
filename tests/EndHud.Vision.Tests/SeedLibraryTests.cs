using EndHud.Vision;
using EndHud.Vision.Operators;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>The operator library bundled with EndHud and installed on first start.</summary>
public sealed class SeedLibraryTests : IDisposable
{
    private static readonly string Seed = Path.Combine(AppContext.BaseDirectory, "Seed", "operators");
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "endhud-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task First_start_installs_the_seed_and_recognises_the_squad_in_a_frame_it_never_saw()
    {
        Assert.True(OperatorLibrary.SeedIfMissing(_folder, Seed));

        var library = await OperatorLibrary.LoadAsync(_folder);
        Assert.True(library.Operators.Count >= 34);
        Assert.Equal("莱万汀", library.Find("Laevatain")!.NameZh);

        // The seed learned this squad from the 5 s exploration screenshot and the 20 s combat frame; 30 s is new.
        var frame = await ImageFile.LoadAsync(CombatFrameTests.Fixture("combat-30s.jpg"));
        var state = await new SquadIdentifier(library).IdentifyAsync(await new PartyReader(new TextRecognizer()).ReadAsync(frame));
        Assert.Equal(["Laevatain", "Camille", "Arcane", "Ardelia"], state.Slots.Select(s => s.Operator?.Operator.Id));
        Assert.All(state.Slots, s => Assert.True(s.Operator!.FromLearned));
    }

    [Fact]
    public async Task An_existing_library_is_never_reseeded()
    {
        // The user has their own library, in which they deleted everything learned about Laevatain.
        OperatorLibrary.SeedIfMissing(_folder, Seed);
        var library = await OperatorLibrary.LoadAsync(_folder);
        library.Unlearn("Laevatain", _ => true);

        Assert.False(OperatorLibrary.SeedIfMissing(_folder, Seed));
        Assert.Equal(0, (await OperatorLibrary.LoadAsync(_folder)).LearnedCount("Laevatain"));
    }
}
