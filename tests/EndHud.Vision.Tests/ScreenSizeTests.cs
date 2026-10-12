using EndHud.Vision;
using Xunit;
using Xunit.Abstractions;

namespace EndHud.Vision.Tests;

/// <summary>
/// Other screen sizes and aspect ratios, simulated from the 4K fixtures and recordings (see
/// <see cref="SimulatedScreen"/> for what that does and doesn't prove). Every value read at
/// another size must match the 4K reading — unreadable is allowed, wrong is not — and enough of
/// them must be read for the screen to be useful.
/// </summary>
public class ScreenSizeTests(ITestOutputHelper output)
{
    public static TheoryData<string> Screens() => [.. SimulatedScreen.All.Select(s => s.Name)];

    private sealed record Fixture(string Name, Frame Frame, PartyState At4K);

    private static readonly Lazy<Task<List<Fixture>>> Fixtures = new(async () =>
    {
        var reader = new PartyReader(new TextRecognizer());
        var list = new List<Fixture>();
        foreach (string path in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures")).Order())
        {
            // Some fixtures are 3838x2158 (a recording's size); they're used as they are, since
            // stretching them by two pixels is itself a change of screen size.
            var frame = await ImageFile.LoadAsync(path);
            list.Add(new Fixture(Path.GetFileName(path), frame, await reader.ReadAsync(frame)));
        }
        // Each screen is compared with these 4K readings, so they must be complete themselves: a
        // level or ping the 4K reading misses is a comparison silently skipped at every size.
        int text = list.Sum(f => (f.At4K.PingMs is null ? 0 : 1) + f.At4K.Slots.Count(s => s.Level is not null));
        if (text < 26) throw new InvalidOperationException($"4K readings found only {text} levels and pings (27 expected)");
        return list;
    });

    /// <summary>Readings and how many of them were readable, for one screen.</summary>
    private sealed class Tally
    {
        public List<string> Wrong { get; } = [];
        public int Measured, MeasuredRead, Text, TextRead;
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public async Task Every_reading_matches_4K_or_is_unreadable(string screenName)
    {
        var screen = SimulatedScreen.Named(screenName);
        double scale = Math.Min(screen.Width / 3840.0, screen.Height / 2160.0);
        // An HP bar is a few pixels tall at 1600x900 and below; its fill edge is less exact.
        double hpTolerance = scale < PartyReader.MinTextScale ? 0.08 : 0.05;
        var reader = new PartyReader(new TextRecognizer());
        var tally = new Tally();

        foreach (var fixture in await Fixtures.Value)
        {
            var here = await reader.ReadAsync(screen.Render(fixture.Frame));
            var at4K = fixture.At4K;
            void Wrong(string what, object? expected, object? actual) =>
                tally.Wrong.Add($"{fixture.Name} {what}: 4K {expected ?? "none"}, here {actual ?? "none"}");

            // Not finding the panel is unreadable (the HUD keeps the last reading); finding one
            // that isn't there is wrong.
            tally.Measured++;
            if (here.PanelVisible && !at4K.PanelVisible) Wrong("panel visible", false, true);
            else if (here.PanelVisible == at4K.PanelVisible) tally.MeasuredRead++;

            CountText(tally, at4K.PingMs, here.PingMs, (a, b) => Math.Abs(a - b) <= 2, v => Wrong("ping", at4K.PingMs, v));
            for (int i = 0; i < PartyLayout.SlotCount; i++)
            {
                var (a, b) = (at4K.Slots[i], here.Slots[i]);
                string slot = $"slot {i + 1}";
                CountText(tally, a.Level, b.Level, (x, y) => x == y, v => Wrong($"{slot} level", a.Level, v));

                tally.Measured++;
                if (b.Dead != a.Dead) Wrong($"{slot} fallen", a.Dead, b.Dead);
                else tally.MeasuredRead++;
                if (a.Hp is { } hpA)
                {
                    tally.Measured++;
                    if (b.Hp is not { } hpB) { }
                    else if (Math.Abs(hpA - hpB) > hpTolerance) Wrong($"{slot} HP", $"{hpA:P0}", $"{hpB:P0}");
                    else tally.MeasuredRead++;
                }
                if (a.Combo is { } comboA)
                {
                    tally.Measured++;
                    if (b.Combo is not { } comboB) { }
                    else if (comboB.Ready != comboA.Ready || Math.Abs(comboB.Progress - comboA.Progress) > 0.04)
                        Wrong($"{slot} combo", Describe(comboA), Describe(comboB));
                    else tally.MeasuredRead++;
                }
                else if (b.Combo is { } extra) Wrong($"{slot} combo", null, Describe(extra));
                if (a.Ultimate is { } ultA)
                {
                    tally.Measured++;
                    if (b.Ultimate is not { } ultB) { }
                    else if (ultB.Ready != ultA.Ready || Math.Abs(ultB.Progress - ultA.Progress) > 0.04)
                        Wrong($"{slot} ultimate", Describe(ultA), Describe(ultB));
                    else tally.MeasuredRead++;
                }
            }
        }

        output.WriteLine($"{screen}: measured {tally.MeasuredRead}/{tally.Measured}, text {tally.TextRead}/{tally.Text}");
        Assert.True(tally.Wrong.Count == 0, $"{screen}: {tally.Wrong.Count} wrong readings:\n" + string.Join("\n", tally.Wrong));
        // Enough read to be useful. Measured on these fixtures (of what 4K reads):
        //   bars, circles, death marks   97-100% everywhere
        //   levels and pings             88-100% at 1440p-class, 3000x2000 and 5K; 58% at 2880x1800;
        //                                62% at 1080p-class and 2256x1504; 46% in a 1700x956 window;
        //                                none at 1600x900 and smaller (by design)
        // A level or ping missed in one reading keeps its last value on the HUD.
        double measured = tally.MeasuredRead / (double)tally.Measured;
        Assert.True(measured >= (scale < PartyReader.MinTextScale ? 0.95 : 0.97), $"{screen}: only {measured:P0} of bars, circles and death marks read");
        if (scale >= PartyReader.MinTextScale)
        {
            double text = tally.TextRead / (double)tally.Text;
            double floor = scale >= 0.6 ? 0.5 : 0.4;
            Assert.True(text >= floor, $"{screen}: only {text:P0} of levels and pings read (at least {floor:P0} expected)");
        }
    }

    private static void CountText(Tally tally, int? at4K, int? here, Func<int, int, bool> same, Action<int?> wrong)
    {
        if (at4K is null)
        {
            // 4K couldn't read it; another size reading something is fine only if it's plausible,
            // which this test can't judge, so it isn't counted either way.
            return;
        }
        tally.Text++;
        if (here is null) return;
        if (same(at4K.Value, here.Value)) tally.TextRead++;
        else wrong(here);
    }

    private static string Describe(ComboReading c) => c.Ready ? "ready" : $"{c.Progress:P0}";
    private static string Describe(UltimateReading u) => u.Ready ? "ready" : $"{u.Progress:P0}";

    // ---- Recordings at other sizes ----

    public static TheoryData<string> HintScreens() =>
    [
        SimulatedScreen.FullHd.Name, SimulatedScreen.Hd.Name, SimulatedScreen.LaptopHd.Name,
        SimulatedScreen.UltraWide.Name, SimulatedScreen.Wuxga.Name, SimulatedScreen.Surface.Name,
    ];

    [Theory]
    [MemberData(nameof(HintScreens))]
    public async Task Combo_ready_hints_come_at_the_same_moments_on_other_screens(string screenName)
    {
        var screen = SimulatedScreen.Named(screenName);
        var video = await VideoFile.OpenAsync(Recordings.PathOf(Recordings.Combat));
        var reader = new PartyReader(new TextRecognizer());
        var tracker = new PartyTracker();
        var hints = new ComboHints();
        var shown = new HashSet<int>();
        var starts = new List<(int, int)>();
        for (int t = 0; t < (int)video.Duration.TotalSeconds; t++)
        {
            var frame = screen.Render(await video.GetFrameAsync(TimeSpan.FromSeconds(t)));
            var now = hints.Update(tracker.Track(await reader.ReadAsync(frame)), TimeSpan.FromSeconds(t));
            starts.AddRange(now.Where(slot => !shown.Contains(slot)).Select(slot => (t, slot)));
            shown = [.. now];
        }

        // As at 4K (ComboHintsTests): slot 1 at 22 s, slot 3 at 25 s, nothing else.
        Assert.Equal([(22, 1), (25, 3)], starts);
    }
}
