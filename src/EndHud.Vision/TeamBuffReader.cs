using EndHud.Vision.Operators;

namespace EndHud.Vision;

/// <summary>
/// Team buffs the game draws left of the controlled operator's HP bar, left of their own counter:
/// so far Link (the game's combo buff: a white sword with grey feathers), seen in the owner's Laevatain
/// fight (Fixtures/buffs). Drawn without a frame or a stack number, so it's either there or not. The
/// icon is the wiki's (<see cref="OperatorLibrary.StatusIcon"/>); one is reported only when the icon
/// matches and its white sword is really there (dark, flat scenery can correlate with the
/// icon's shape alone).
/// </summary>
public sealed class TeamBuffReader(OperatorLibrary library)
{
    /// <summary>The buffs looked for, by the wiki's names.</summary>
    public static readonly IReadOnlyList<string> Buffs = ["Link"];

    // Where the icon can be, at 4K, bottom-centre anchored: seen at x 1395–1475, y 1945–2025 beside
    // Laevatain's flower; wider counters (Typhoeus's bars, Zhuang Fangyi's grid) start further left,
    // so the band reaches left of that.
    internal static readonly PixelRect Band = new(1240, 1925, 280, 105);
    // The icon's size at 4K: about 55 px.
    private static readonly int[] Sizes = [50, 56, 62];
    // Normalised correlation (grey, over the icon's opaque part). Measured on the fixtures (Fixtures/
    // buffs, counters): the icon 0.77–0.79; everything else at most 0.67 (scenery beside Laevatain's
    // flower, Zhuang Fangyi's grid).
    internal const double MatchAt = 0.72;
    // Of the matched square, how much must be whitish (the sword: every channel 170 or more, near
    // grey). Measured: the icon 0.20–0.39 (dimmest when the HUD fades); everything else at most 0.12.
    // Dark, flat scenery can correlate with the icon's shape alone: this is what rules it out.
    internal const double MinWhiteShare = 0.15;
    private const int WhiteFrom = 170;

    private sealed record Template(int Size, float[] Grey, bool[] Opaque, int Count, float Mean, float Spread);

    private readonly Dictionary<(string Id, int Size), Template> _templates = new();

    /// <summary>The buffs shown now (empty when none, or when the band can't be read).</summary>
    public IReadOnlyList<string> Read(Frame frame)
    {
        var ui = new UiScaler(frame.Width, frame.Height);
        var band = ui.BottomCenter(Band).ClampTo(frame.Width, frame.Height);
        if (band.IsEmpty) return [];

        // Most of the time there's nothing white here at all: no need to look further.
        var grey = new float[band.Width * band.Height];
        int white = 0;
        for (int y = 0; y < band.Height; y++)
        for (int x = 0; x < band.Width; x++)
        {
            var (r, g, b) = frame.GetPixel(band.X + x, band.Y + y);
            grey[y * band.Width + x] = (r + g + b) / 3f;
            if (IsWhite(r, g, b)) white++;
        }
        int smallest = Math.Max(8, (int)Math.Round(Sizes[0] * ui.Scale));
        if (white < smallest * smallest * MinWhiteShare) return [];

        var found = new List<string>();
        foreach (string id in Buffs)
        {
            if (library.StatusIcon(id) is not { } icon) continue;
            if (Find(frame, band, grey, icon, id, ui.Scale) is { } at && at.Score >= MatchAt && WhiteShare(frame, at.Square) >= MinWhiteShare)
                found.Add(id);
        }
        return found;
    }

    /// <summary>The best match in the band: a coarse pass, then pixel by pixel around the best spot.</summary>
    internal (double Score, PixelRect Square)? Find(Frame frame, PixelRect band, float[] grey, Frame icon, string id, double scale)
    {
        (double Score, PixelRect Square)? best = null;
        foreach (int size4K in Sizes)
        {
            var t = TemplateFor(id, icon, Math.Max(8, (int)Math.Round(size4K * scale)));
            int step = Math.Max(1, (int)Math.Round(3 * scale));
            (double Score, int X, int Y) coarse = (-1, 0, 0);
            for (int y = 0; y + t.Size <= band.Height; y += step)
            for (int x = 0; x + t.Size <= band.Width; x += step)
            {
                double s = Correlate(grey, band.Width, t, x, y);
                if (s > coarse.Score) coarse = (s, x, y);
            }
            for (int y = Math.Max(0, coarse.Y - step); y <= Math.Min(band.Height - t.Size, coarse.Y + step); y++)
            for (int x = Math.Max(0, coarse.X - step); x <= Math.Min(band.Width - t.Size, coarse.X + step); x++)
            {
                double s = Correlate(grey, band.Width, t, x, y);
                if (best is null || s > best.Value.Score) best = (s, new PixelRect(band.X + x, band.Y + y, t.Size, t.Size));
            }
        }
        return best;
    }

    private static bool IsWhite(byte r, byte g, byte b) => Math.Min(r, Math.Min(g, b)) >= WhiteFrom && Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)) <= 40;

    private static double WhiteShare(Frame frame, PixelRect square)
    {
        int white = 0;
        for (int y = square.Y; y < square.Bottom; y++)
        for (int x = square.X; x < square.Right; x++)
        {
            var (r, g, b) = frame.GetPixel(x, y);
            if (IsWhite(r, g, b)) white++;
        }
        return white / (double)(square.Width * square.Height);
    }

    private Template TemplateFor(string id, Frame icon, int size)
    {
        lock (_templates)
        {
            if (_templates.TryGetValue((id, size), out var cached)) return cached;
            var resized = icon.ResizeSmooth(size, size);
            var grey = new float[size * size];
            var opaque = new bool[size * size];
            int n = 0;
            double sum = 0, sumSq = 0;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int i = y * size + x;
                if (resized.GetAlpha(x, y) < 100) continue;
                var (r, g, b) = resized.GetPixel(x, y);
                grey[i] = (r + g + b) / 3f;
                opaque[i] = true;
                n++;
                sum += grey[i];
                sumSq += grey[i] * grey[i];
            }
            float mean = n == 0 ? 0 : (float)(sum / n);
            float spread = n == 0 ? 0 : (float)Math.Sqrt(Math.Max(0, sumSq / n - mean * mean));
            return _templates[(id, size)] = new Template(size, grey, opaque, n, mean, spread);
        }
    }

    /// <summary>Normalised correlation of the band at (<paramref name="ox"/>, <paramref name="oy"/>) with the icon's opaque part.</summary>
    private static double Correlate(float[] band, int bandWidth, Template t, int ox, int oy)
    {
        if (t.Count == 0 || t.Spread < 1e-3) return -1;
        double sumI = 0, sumII = 0, sumTI = 0;
        for (int y = 0; y < t.Size; y++)
        {
            int row = (oy + y) * bandWidth + ox;
            for (int x = 0; x < t.Size; x++)
            {
                int i = y * t.Size + x;
                if (!t.Opaque[i]) continue;
                float v = band[row + x];
                sumI += v;
                sumII += v * v;
                sumTI += v * (t.Grey[i] - t.Mean);
            }
        }
        double meanI = sumI / t.Count, varI = sumII / t.Count - meanI * meanI;
        return varI <= 1e-6 ? -1 : sumTI / t.Count / (Math.Sqrt(varI) * t.Spread);
    }
}
