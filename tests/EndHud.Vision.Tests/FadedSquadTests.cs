using System.Globalization;
using EndHud.Vision;
using EndHud.Vision.Operators;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// In battle the game draws some squad portraits see-through (Fixtures/faded-squad, readings taken
/// four a second as in the app). The squad can't change in a battle, so once named, the operators
/// stay named through them: no slot goes back to "not recognised" (the combo hint showing "2号位",
/// the rotation strip vanishing) or to another operator.
/// </summary>
[Trait("Category", "Slow")] // recognises operators with the model: left out of the fast lane (scripts/test.ps1 -Fast)
public sealed class FadedSquadTests : IDisposable
{
    private static readonly string[] Truth = ["Typhoeus", "Arcane", "Perlica", "Liino"];

    private readonly string _libraryDir = Path.Combine(Path.GetTempPath(), "endhud-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_libraryDir, recursive: true); } catch (IOException) { }
    }

    /// <summary>
    /// The readings in order, with their time in the recording. The clear ones at the start are
    /// read five times over, as the app reads the squad for several seconds before the effects start
    /// (enough for every slot's answer to settle).
    /// </summary>
    private static IEnumerable<(double Seconds, string File)> Readings()
    {
        var all = Directory.GetFiles(CombatFrameTests.Fixture("faded-squad"), "t*.jpg").Order(StringComparer.Ordinal)
            .Select(f => (Seconds: double.Parse(Path.GetFileNameWithoutExtension(f)[1..].Replace('_', '.'), CultureInfo.InvariantCulture), File: f))
            .ToList();
        var clear = all.Where(r => r.Seconds <= 15).ToList();
        return [.. Enumerable.Repeat(clear, 5).SelectMany(r => r), .. all.Where(r => r.Seconds > 15)];
    }

    [Fact]
    public async Task Named_operators_stay_named_while_their_portraits_are_faded()
    {
        var clock = new DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);
        var identifier = new SquadIdentifier(await OperatorCache.LoadAsync(_libraryDir), () => clock);
        var reader = new PartyReader(new TextRecognizer());
        var tracker = new PartyTracker();
        bool named = false, prepared = false;
        double namedAt = double.MaxValue;
        int checkedReadings = 0;

        foreach (var (seconds, file) in Readings())
        {
            clock += TimeSpan.FromMilliseconds(250); // four readings a second
            var state = tracker.Track(await reader.ReadAsync(await ImageFile.LoadAsync(file)));
            if (!prepared) await identifier.PrepareAsync(state);
            prepared = true;
            var ids = (await identifier.IdentifyAsync(state)).Slots.Select(s => s.Operator?.Operator.Id).ToList();

            if (!named)
            {
                named = Truth.SequenceEqual(ids);
                if (named) namedAt = seconds;
                continue;
            }
            checkedReadings++;
            Assert.True(Truth.SequenceEqual(ids), $"{seconds:F2} s: {string.Join(", ", ids.Select(id => id ?? "?"))}");
        }

        // Named from the first clear stretches, before the long faded one (30.5–37 s) that broke it.
        Assert.True(namedAt < 30.5, $"named only at {namedAt} s");
        Assert.True(checkedReadings >= 27, $"only {checkedReadings} readings after naming");
    }

    /// <summary>
    /// When a battle starts with a squad not named yet, every slot is compared each reading, so the
    /// whole squad is named after MinReadings readings (less than a second), not four times that.
    /// Until then the combo hint waits instead of showing "1 号位".
    /// </summary>
    [Fact]
    public async Task A_new_squad_is_named_within_a_few_readings()
    {
        var clock = new DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);
        var identifier = new SquadIdentifier(await OperatorCache.LoadAsync(_libraryDir), () => clock);
        var reader = new PartyReader(new TextRecognizer());
        var clear = Readings().Take(SquadIdentifier.MinReadings).ToList();
        await identifier.PrepareAsync(await reader.ReadAsync(await ImageFile.LoadAsync(clear[0].File)));

        IReadOnlyList<string?> ids = [];
        foreach (var (_, file) in clear)
        {
            clock += TimeSpan.FromMilliseconds(250);
            ids = (await identifier.IdentifyAsync(await reader.ReadAsync(await ImageFile.LoadAsync(file)))).Slots.Select(s => s.Operator?.Operator.Id).ToList();
        }

        Assert.Equal(Truth, ids);
    }
}
