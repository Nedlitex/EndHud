using Xunit;
using Xunit.Abstractions;

namespace EndHud.Vision.Tests;

/// <summary>
/// The on-screen display gate (<see cref="OnScreenDisplayCombatGateTests"/>) for "战技已强化": the empowered glow told
/// from the battle-skill corner sampled 30 times a second (<see cref="FlashSampler.Empowered"/>), in the keyboard
/// layout and the controller's (whichever diamond the corner shows), with the synthetic display (<see cref="SyntheticOsd"/>)
/// at each of its five positions, on its backdrop or not, redrawn every 0.5 s: every slot's verdict must be the clean
/// frame's or unknown, never the other. Over the combat fixtures the display gate samples, and the cast-flash corners
/// (the empowered glow among them) placed at the bottom right of a frame of their recording's size.
/// </summary>
[Trait("Category", "Slow")] // a display sweep over every combat fixture: left out of the fast lane (scripts/test.ps1 -Fast)
public class OnScreenDisplayEmpoweredGateTests(ITestOutputHelper output)
{
    public static TheoryData<string> Frames()
    {
        var data = new TheoryData<string>();
        foreach (string frame in OnScreenDisplayCombatGateTests.Frames()) data.Add(frame);
        foreach (string file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures", "cast-flash"), "*.png").Order(StringComparer.Ordinal))
            data.Add("cast-flash/" + Path.GetFileName(file));
        return data;
    }

    /// <summary>A fixture as a whole frame: a cast-flash corner is put at the bottom right of a black 3838×2158 frame.</summary>
    private static async Task<Frame> LoadAsync(string fixture)
    {
        var image = await ImageFile.LoadAsync(CombatFrameTests.Fixture(fixture));
        if (!fixture.StartsWith("cast-flash/", StringComparison.Ordinal)) return image;
        const int w = 3838, h = 2158;
        var bgra = new byte[w * h * 4];
        for (int i = 3; i < bgra.Length; i += 4) bgra[i] = 255;
        for (int y = 0; y < image.Height; y++)
            Array.Copy(image.Bgra, y * image.Width * 4, bgra, ((h - image.Height + y) * w + w - image.Width) * 4, image.Width * 4);
        return new Frame(w, h, bgra);
    }

    [Theory]
    [MemberData(nameof(Frames))]
    public async Task Empowered_reads_the_same_or_unknown_under_a_display(string fixture)
    {
        var source = await LoadAsync(fixture);
        var wrong = new List<string>();
        foreach (var (size, frame) in new[] { ("4K", source), ("1080p", SimulatedScreen.FullHd.Render(source)) })
        foreach (var layout in new[] { HudLayout.KeyboardMouse, HudLayout.Controller })
        {
            var region = BattleSkillFlash.Region(frame.Width, frame.Height, layout);
            var corner = frame.Crop(region);
            double scale = new UiScaler(frame.Width, frame.Height).Scale;
            var clean = Verdict(_ => corner, scale, layout);
            int px = SyntheticOsd.PixelFor(frame.Height);
            foreach (var position in SyntheticOsd.Positions)
            foreach (bool background in new[] { true, false })
            {
                var (x, y) = SyntheticOsd.Place(position, frame.Width, frame.Height, px);
                var drawn = Enumerable.Range(0, 2).Select(refresh => SyntheticOsd.Draw(corner, x - region.X, y - region.Y, px, background, refresh: refresh)).ToList();
                var covered = Verdict(t => drawn[(int)(t / 0.5) % 2], scale, layout);
                for (int slot = 0; slot < 4; slot++)
                    if (covered[slot] is { } read && read != clean[slot])
                        wrong.Add($"{size} {layout} {position}{(background ? " bg" : "")} slot {slot + 1}: {clean[slot]?.ToString() ?? "unknown"} read {read}");
                output.WriteLine($"GATE\t{fixture}\t{size}\t{layout}\t{position}\t{(background ? "bg" : "nobg")}\tclean={string.Join(",", clean)}\tcovered={string.Join(",", covered)}");
            }
        }
        Assert.True(wrong.Count == 0, $"{fixture}: {string.Join("; ", wrong)}");
    }

    /// <summary>The verdict by slot (a squad of four) after one second of the corner sampled 30 times a second.</summary>
    private static IReadOnlyList<bool?> Verdict(Func<double, Frame> cornerAt, double scale, HudLayout layout)
    {
        var sampler = new FlashSampler();
        for (int n = 0; n < 30; n++)
        {
            double t = n / 30.0;
            sampler.Sample(cornerAt(t), scale, TimeSpan.FromSeconds(t), layout);
        }
        return sampler.Empowered(4);
    }
}
