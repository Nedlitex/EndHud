using EndHud.Vision.Operators;

namespace EndHud.Vision;

/// <summary>A team buff shown now.</summary>
/// <param name="Stacks">How many are active: 1 when the icon has no number; otherwise the number in its
/// corner (Link stacks up to <see cref="TeamBuffReader.MaxStacks"/>); null when a number is there but
/// can't be told (an effect over it).</param>
public sealed record TeamBuff(string Id, int? Stacks);

/// <summary>
/// Team buffs the game draws left of the controlled operator's HP bar, left of their own counter:
/// so far Link (the game's combo buff: a white sword with grey feathers), seen in the owner's Laevatain
/// fight (Fixtures/buffs). Drawn without a frame. One stack shows no number; more show the count,
/// white on a small dark square at the icon's bottom right. The icon is the wiki's
/// (<see cref="OperatorLibrary.StatusIcon"/>); one is reported only when the icon
/// matches and its white sword is really there (dark, flat scenery can correlate with the
/// icon's shape alone).
/// </summary>
public sealed class TeamBuffReader(OperatorLibrary library)
{
    /// <summary>The buffs looked for, by the wiki's names.</summary>
    public static readonly IReadOnlyList<string> Buffs = ["Link"];

    /// <summary>Link's most stacks (the wiki's Link page lists its bonus for 1 to 4 active buffs).</summary>
    public const int MaxStacks = 4;

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
    // The stack badge: its side as a share of the icon's (26 px by a 62 px match at 4K), and how dark
    // its pixels are (every channel at most this). Of the darkest square of that size at the icon's
    // bottom right: how much must be dark, how much dark or white (the digit; the rest is its
    // anti-aliased edge), and the least white (a digit is there). Measured on the badge with "2": about
    // 0.6 dark, 0.2 white; without one, the darkest square there is mostly scenery.
    private const double BadgeShare = 0.42;
    private const int BadgeDarkMax = 60;
    private const double BadgeMinDark = 0.4;
    private const double BadgeMinDarkOrWhite = 0.7;
    private const double BadgeMinWhite = 0.08;
    // The badge covers the icon's lower right feather: matched with it, the icon scores 0.66–0.74.
    // Below MatchAt, a match this good with a badge in its corner is scored again without the badge.
    private const double MatchWithBadgeFrom = 0.6;

    private sealed record Template(int Size, float[] Grey, bool[] Opaque, int Count, float Mean, float Spread);

    private readonly Dictionary<(string Id, int Size), Template> _templates = new();

    /// <summary>The buffs shown now, with their stacks (empty when none, or when the band can't be read).</summary>
    public IReadOnlyList<TeamBuff> Read(Frame frame) =>
        Locate(frame).Select(b => new TeamBuff(b.Id, StackBadge(frame, b.Square) is { } badge ? Stacks(StackDigits.Read(frame, badge)) : 1)).ToList();

    /// <summary>A digit Link can show (2 to <see cref="MaxStacks"/>); anything else isn't a reading of it.</summary>
    internal static int? Stacks(int? digit) => digit is >= 2 and <= MaxStacks ? digit : null;

    private List<(string Id, PixelRect Square)> Locate(Frame frame)
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

        var found = new List<(string, PixelRect)>();
        foreach (string id in Buffs)
        {
            if (library.StatusIcon(id) is not { } icon) continue;
            if (Find(frame, band, grey, icon, id, ui.Scale) is not { } at || WhiteShare(frame, at.Square) < MinWhiteShare) continue;
            double score = at.Score;
            if (score < MatchAt && score >= MatchWithBadgeFrom && StackBadge(frame, at.Square) is { } badge)
            {
                var t = TemplateFor(id, icon, at.Square.Width);
                var hidden = new PixelRect(badge.X - at.Square.X, badge.Y - at.Square.Y, badge.Width, badge.Height);
                score = Correlate(grey, band.Width, t, at.Square.X - band.X, at.Square.Y - band.Y, hidden);
            }
            if (score >= MatchAt) found.Add((id, at.Square));
        }
        return found;
    }

    /// <summary>
    /// The dark square holding the stack count at the icon's bottom right, or null when there's none
    /// (one stack). Measured at 4K on the controller recordings (Fixtures/buffs, controller-*): about
    /// 30 px, nearly black, the digit white, over the icon's lower right feather.
    /// </summary>
    internal static PixelRect? StackBadge(Frame frame, PixelRect icon)
    {
        int s = icon.Width;
        int size = Math.Max(4, (int)Math.Round(s * BadgeShare));
        var area = new PixelRect(icon.X + s / 2, icon.Y + s / 2, s, s).ClampTo(frame.Width, frame.Height);
        if (area.Width < size || area.Height < size) return null;

        // Dark and white pixels summed over any rectangle of the area (integral images).
        int w = area.Width, h = area.Height, stride = w + 1;
        var dark = new int[stride * (h + 1)];
        var white = new int[stride * (h + 1)];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            var (r, g, b) = frame.GetPixel(area.X + x, area.Y + y);
            int i = (y + 1) * stride + x + 1;
            dark[i] = dark[i - 1] + dark[i - stride] - dark[i - stride - 1] + (Math.Max(r, Math.Max(g, b)) <= BadgeDarkMax ? 1 : 0);
            white[i] = white[i - 1] + white[i - stride] - white[i - stride - 1] + (IsWhite(r, g, b) ? 1 : 0);
        }
        double Share(int[] t, int x, int y) =>
            (t[(y + size) * stride + x + size] - t[y * stride + x + size] - t[(y + size) * stride + x] + t[y * stride + x]) / (double)(size * size);

        (double Dark, int X, int Y) best = (0, 0, 0);
        for (int y = 0; y + size <= h; y++)
        for (int x = 0; x + size <= w; x++)
        {
            double d = Share(dark, x, y);
            if (d > best.Dark) best = (d, x, y);
        }
        double whiteShare = Share(white, best.X, best.Y);
        if (best.Dark < BadgeMinDark || best.Dark + whiteShare < BadgeMinDarkOrWhite || whiteShare < BadgeMinWhite) return null;
        return new PixelRect(area.X + best.X, area.Y + best.Y, size, size);
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
    /// <param name="hidden">A part of the icon left out (the stack badge over it), in the icon's own pixels.</param>
    private static double Correlate(float[] band, int bandWidth, Template t, int ox, int oy, PixelRect? hidden = null)
    {
        if (t.Count == 0 || t.Spread < 1e-3) return -1;
        if (hidden is not null) return CorrelateLeavingOut(band, bandWidth, t, ox, oy, hidden.Value);
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

    /// <summary>As <see cref="Correlate"/>, over the opaque part outside <paramref name="hidden"/> (the template's mean and spread taken over that part too).</summary>
    private static double CorrelateLeavingOut(float[] band, int bandWidth, Template t, int ox, int oy, PixelRect hidden)
    {
        int n = 0;
        double sumT = 0, sumTT = 0, sumI = 0, sumII = 0, sumTI = 0;
        for (int y = 0; y < t.Size; y++)
        {
            int row = (oy + y) * bandWidth + ox;
            for (int x = 0; x < t.Size; x++)
            {
                int i = y * t.Size + x;
                if (!t.Opaque[i] || (x >= hidden.X && x < hidden.Right && y >= hidden.Y && y < hidden.Bottom)) continue;
                float v = band[row + x], g = t.Grey[i];
                n++;
                sumT += g;
                sumTT += g * g;
                sumI += v;
                sumII += v * v;
                sumTI += v * g;
            }
        }
        if (n == 0) return -1;
        double meanT = sumT / n, meanI = sumI / n;
        double varT = sumTT / n - meanT * meanT, varI = sumII / n - meanI * meanI;
        return varT <= 1e-6 || varI <= 1e-6 ? -1 : (sumTI / n - meanT * meanI) / Math.Sqrt(varT * varI);
    }
}
