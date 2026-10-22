using EndHud.Vision.Inn;
using Xunit;
using Xunit.Abstractions;

namespace EndHud.Vision.Tests;

/// <summary>
/// The tray of dishes the player holds (<see cref="TrayReader"/>) on frames of Fixtures/inn, against what each slot shows
/// (labelled by hand, frame beside the pictures; see the fixtures' README), at 4K, 1440p and 1080p. No slot may be read
/// as a wrong dish at any size.
/// </summary>
public class InnTrayReaderTests(ITestOutputHelper output)
{
    private static readonly Lazy<Task<TrayReader>> Reader = new(async () =>
    {
        string folder = await OperatorCache.FolderAsync();
        var data = InnData.Load(folder) ?? throw new InvalidOperationException("The operator package has no inn data.");
        return new TrayReader(await InnPictures.LoadAsync(folder, data));
    });

    // Each slot left to right: "-" empty, the dish's id, "*" after it for the 珍馐 badge. null: no tray on screen.
    public static TheoryData<string, string?> Frames() => new()
    {
        { "session-tray-two-dishes", "roll-of-fin-supreme, spoon-swimmer*, -, -, -" },
        { "session-tray-three-dishes", "spoon-swimmer*, melon-meatball-soup*, fin-dumpling, -, -" },
        { "session-panel-just-closed", "roll-of-fin-supreme, spoon-swimmer*, -, -, -" },
        { "session-full-blaze-running", "-, -, -, -, -" },
        // The dispatch panel covers the tray.
        { "session-dispatch-panel", null },
        { "dispatch-panel-banners", null },
        // Not a session.
        { "menu-dishes", null },
        { "setup-chefs-placed", null },
    };

    public static TheoryData<string> Sizes() => [SimulatedScreen.Uhd.Name, SimulatedScreen.Qhd.Name, SimulatedScreen.FullHd.Name];

    [Theory]
    [MemberData(nameof(Sizes))]
    public async Task Every_slot_is_read_at_every_size(string screenName)
    {
        var screen = SimulatedScreen.Named(screenName);
        var reader = await Reader.Value;
        var wrong = new List<string>();
        foreach (var row in Frames())
        {
            string fixture = (string)row[0];
            string? expected = (string?)row[1];
            var frame = screen.Render(await InnScreenTests.Load(fixture));
            var read = reader.Read(frame);
            string got = read is null ? "null" : string.Join(", ", read.Select(h => h is null ? "-" : (h.Id ?? "?") + (h.Choice ? "*" : "")));
            output.WriteLine($"{fixture} at {screen.Width}x{screen.Height}: {got}");
            if (read is not null)
            {
                var layout = new InnLayout(frame);
                for (int k = 0; k < TrayReader.Slots; k++)
                    if (read[k] is not null)
                        output.WriteLine($"  slot {k + 1}: {string.Join("  ", reader.Rank(frame, layout, k).Take(3).Select(r => $"{r.Id} {r.Difference:F1}"))}; badge {TrayReader.BadgeShares(frame, layout, k)}");
            }
            if (got != (expected ?? "null")) wrong.Add($"{fixture}: read {got}, expected {expected ?? "null"}");
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }
}
