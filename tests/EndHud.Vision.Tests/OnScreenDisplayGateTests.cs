using EndHud.Vision.Inn;
using Xunit;
using Xunit.Abstractions;

namespace EndHud.Vision.Tests;

/// <summary>
/// The regression gate for overlays drawn into the game's frames, such as MSI Afterburner's on-screen display (drawn by
/// RivaTuner, RTSS), which EndHud captures with the game: with the synthetic display (<see cref="SyntheticOsd"/>) at each
/// position, on its background or not, every value read must be the clean frame's or unknown, never another value. A
/// value lost is a frame missed; a wrong one is shown to the player and, the display being static, kept.
/// <para>
/// Read at 4K, at 1080p, and at 1920×1200 with the 1080p picture between black bars (a 16:10 screen, cut by
/// <see cref="Letterbox"/> as the app does). Covered here: the bars and the inn (which screen, the session's clock, the
/// table cards, the Full Blaze meter, the tray). The combat readers get their own theory over their fixtures.
/// </para>
/// Each variant's verdicts are written to the test output ("GATE" lines) for counting.
/// </summary>
public class OnScreenDisplayGateTests(ITestOutputHelper output)
{
    private static readonly Lazy<Task<(TableCardReader Tables, TrayReader Tray)>> Readers = new(async () =>
    {
        string folder = await OperatorCache.FolderAsync();
        var data = InnData.Load(folder) ?? throw new InvalidOperationException("The operator package has no inn data.");
        var pictures = await InnPictures.LoadAsync(folder, data);
        return (new TableCardReader(InnScreenTests.Text, pictures), new TrayReader(pictures));
    });

    public static TheoryData<string> InnFrames()
    {
        var data = new TheoryData<string>();
        foreach (string file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures", "inn"), "*.jpg").Order())
            data.Add(Path.GetFileNameWithoutExtension(file));
        return data;
    }

    [Theory]
    [MemberData(nameof(InnFrames))]
    public async Task The_inn_reads_the_same_or_unknown_under_a_display(string fixture)
    {
        var source = await InnScreenTests.Load(fixture);
        var fullHd = SimulatedScreen.FullHd.Render(source);
        var sizes = new (string Name, Frame Frame, PixelRect Picture)[]
        {
            ("4K", source, new PixelRect(0, 0, source.Width, source.Height)),
            ("1080p", fullHd, new PixelRect(0, 0, 1920, 1080)),
            ("1920x1200 letterboxed", SyntheticOsd.Letterboxed(fullHd, 1920, 1200).Frame, new PixelRect(0, 60, 1920, 1080)),
        };
        var wrong = new List<string>();
        foreach (var (size, frame, picture) in sizes)
        {
            var clean = await ReadAsync(frame);
            Assert.True(clean["letterbox"] == Describe(picture), $"{fixture} {size}: the clean frame's bars read {clean["letterbox"]}");
            foreach (var position in SyntheticOsd.Positions)
            foreach (bool background in new[] { true, false })
            {
                var covered = await ReadAsync(SyntheticOsd.Draw(frame, position, background));
                int unknown = 0;
                foreach (var (field, value) in clean)
                {
                    string? read = covered.GetValueOrDefault(field);
                    if (read == value) continue;
                    if (read is null && field != "letterbox") { unknown++; continue; }
                    wrong.Add($"{size} {position}{(background ? " bg" : "")} {field}: {value ?? "unknown"} read {read ?? "unknown"}");
                }
                // A value read under the display where the clean frame read none is wrong too.
                foreach (var (field, read) in covered)
                    if (read is not null && !clean.ContainsKey(field))
                        wrong.Add($"{size} {position}{(background ? " bg" : "")} {field}: unknown read {read}");
                output.WriteLine($"GATE\t{fixture}\t{size}\t{position}\t{(background ? "bg" : "nobg")}\tunknown={unknown}\twrong={wrong.Count(w => w.StartsWith($"{size} {position}{(background ? " bg" : "")} "))}");
            }
        }
        Assert.True(wrong.Count == 0, $"{fixture}: {string.Join("; ", wrong)}");
    }

    private static string Describe(PixelRect r) => $"{r.X},{r.Y} {r.Width}x{r.Height}";

    /// <summary>Every value read, by field; a field absent or null is unknown.</summary>
    private static async Task<Dictionary<string, string?>> ReadAsync(Frame frame)
    {
        var (tables, tray) = await Readers.Value;
        var read = new Dictionary<string, string?>();
        var content = Letterbox.Content(frame);
        read["letterbox"] = Describe(content);
        var picture = content == new PixelRect(0, 0, frame.Width, frame.Height) ? frame : frame.Crop(content);

        var kind = await InnScreen.DetectAsync(InnScreenTests.Text, picture);
        read["screen"] = kind?.ToString();
        read["clock"] = InnScreen.ReadClock(picture)?.ToString();
        if (kind is InnScreenKind.Session or InnScreenKind.DispatchPanel)
        {
            var cards = await tables.ReadAsync(picture);
            for (int i = 0; i < cards.Count; i++) read[$"table {i + 1}"] = cards[i] is null ? null : InnUnderOverlayTests.Describe(cards[i]);
            if (await FireMeterReader.ReadAsync(InnScreenTests.Text, picture) is { } fire)
            {
                read["fire"] = fire.Fire?.ToString();
                read["streak"] = fire.Streak?.ToString();
                read["fire ready"] = fire.Ready.ToString();
                read["full blaze left"] = fire.BlazeLeft?.ToString();
            }
            if (tray.Read(picture) is { } held)
                for (int i = 0; i < held.Count; i++) read[$"tray {i + 1}"] = held[i] is { } dish ? $"{dish.Id}{(dish.Choice ? "*" : "")}" : "empty";
        }
        return read;
    }
}
