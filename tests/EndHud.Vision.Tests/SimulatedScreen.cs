using EndHud.Vision;

namespace EndHud.Vision.Tests;

/// <summary>
/// A game window of another size, made from a 4K recording, for testing screens we don't have.
/// <list type="bullet">
/// <item>16:9 sizes are the 4K frame scaled down: the game scales its HUD uniformly, so this is
/// close to the real thing (a game rendering natively at that size draws text a little
/// differently).</item>
/// <item>Other aspect ratios follow the layout EndHud assumes (see <see cref="UiScaler"/>): the
/// HUD is scaled to fit 16:9 and each element keeps its distance from its own corner. The 4K
/// frame is scaled and each quarter pinned to its corner of the wider or taller window; the
/// rest is black. These check that EndHud handles that layout, not that the game uses it;
/// that needs a real screenshot at that size.</item>
/// </list>
/// </summary>
public sealed record SimulatedScreen(string Name, int Width, int Height)
{
    // Native 4K: the fixtures as they are.
    public static readonly SimulatedScreen Uhd = new("3840x2160 (16:9, 4K desktop monitor)", 3840, 2160);

    // Desktop monitors.
    public static readonly SimulatedScreen Qhd = new("2560x1440 (16:9, 27-inch desktop monitor)", 2560, 1440);
    public static readonly SimulatedScreen FullHd = new("1920x1080 (16:9, desktop monitor or laptop)", 1920, 1080);
    public static readonly SimulatedScreen FiveK = new("5120x2880 (16:9, 5K desktop monitor)", 5120, 2880);
    public static readonly SimulatedScreen UltraWide = new("3440x1440 (21:9, 34-inch ultrawide)", 3440, 1440);
    public static readonly SimulatedScreen UltraWideFhd = new("2560x1080 (21:9, 29-inch ultrawide)", 2560, 1080);
    public static readonly SimulatedScreen SuperWide = new("5120x1440 (32:9, 49-inch super-ultrawide)", 5120, 1440);
    public static readonly SimulatedScreen Wuxga = new("1920x1200 (16:10, desktop monitor or laptop)", 1920, 1200);

    // Laptop panels.
    public static readonly SimulatedScreen Hd = new("1280x720 (16:9, small window or old laptop)", 1280, 720);
    public static readonly SimulatedScreen LaptopHd = new("1366x768 (16:9, budget laptop)", 1366, 768);
    public static readonly SimulatedScreen LaptopHdPlus = new("1600x900 (16:9, laptop)", 1600, 900);
    public static readonly SimulatedScreen Wqxga = new("2560x1600 (16:10, 16-inch laptop)", 2560, 1600);
    public static readonly SimulatedScreen LaptopRetina = new("2880x1800 (16:10, 15-inch high-DPI laptop)", 2880, 1800);
    public static readonly SimulatedScreen Surface = new("2256x1504 (3:2, 13-inch laptop)", 2256, 1504);
    public static readonly SimulatedScreen SurfaceLarge = new("3000x2000 (3:2, 13.5-inch high-DPI laptop)", 3000, 2000);

    // The game in a window rather than full screen.
    public static readonly SimulatedScreen OddWindow = new("1700x956 (16:9 window on a 1080p screen)", 1700, 956);

    /// <summary>Every simulated screen except native 4K.</summary>
    public static readonly SimulatedScreen[] All =
    [
        Qhd, FullHd, FiveK, UltraWide, UltraWideFhd, SuperWide, Wuxga,
        Hd, LaptopHd, LaptopHdPlus, Wqxga, LaptopRetina, Surface, SurfaceLarge,
        OddWindow,
    ];

    public static SimulatedScreen Named(string name) => All.Append(Uhd).Single(s => s.Name == name);

    public override string ToString() => Name;

    /// <summary>The 3840×2160 <paramref name="source"/> as this screen would show it.</summary>
    public Frame Render(Frame source)
    {
        if (source.Width == Width && source.Height == Height) return source;
        double scale = Math.Min(Width / (double)UiScaler.ReferenceWidth, Height / (double)UiScaler.ReferenceHeight);
        int w = (int)Math.Round(UiScaler.ReferenceWidth * scale), h = (int)Math.Round(UiScaler.ReferenceHeight * scale);
        // Larger than 4K (5K): enlarge smoothly, as the game would render more pixels, not blocks.
        var scaled = w > source.Width ? source.ResizeSmooth(w, h) : source.Resize(w, h);
        if (w == Width && h == Height) return scaled;

        var pixels = new byte[Width * Height * 4];
        for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        int left = w / 2, top = h / 2;
        // Each quarter of the 16:9 HUD goes to its own corner of the screen.
        Copy(scaled, 0, 0, left, top, pixels, 0, 0);
        Copy(scaled, left, 0, w - left, top, pixels, Width - (w - left), 0);
        Copy(scaled, 0, top, left, h - top, pixels, 0, Height - (h - top));
        Copy(scaled, left, top, w - left, h - top, pixels, Width - (w - left), Height - (h - top));
        return new Frame(Width, Height, pixels);
    }

    /// <summary>
    /// The 3840×2160 <paramref name="source"/> scaled to fit this screen and placed whole in the margin it leaves:
    /// <paramref name="along"/> 0 at the top or left, 0.5 centred, 1 at the bottom or right; the rest black. Another
    /// layout the game may use for its full-screen panels (the inn's), beside <see cref="Render"/>'s corners.
    /// </summary>
    public Frame Placed(Frame source, double along)
    {
        double scale = Math.Min(Width / (double)UiScaler.ReferenceWidth, Height / (double)UiScaler.ReferenceHeight);
        int w = (int)Math.Round(UiScaler.ReferenceWidth * scale), h = (int)Math.Round(UiScaler.ReferenceHeight * scale);
        var scaled = w == source.Width && h == source.Height ? source : w > source.Width ? source.ResizeSmooth(w, h) : source.Resize(w, h);
        var pixels = new byte[Width * Height * 4];
        for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        Copy(scaled, 0, 0, w, h, pixels, (int)Math.Round((Width - w) * along), (int)Math.Round((Height - h) * along));
        return new Frame(Width, Height, pixels);
    }

    private void Copy(Frame from, int x, int y, int width, int height, byte[] to, int toX, int toY)
    {
        for (int row = 0; row < height; row++)
            Buffer.BlockCopy(from.Bgra, ((y + row) * from.Width + x) * 4, to, ((toY + row) * Width + toX) * 4, width * 4);
    }
}
