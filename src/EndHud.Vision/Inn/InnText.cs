using Windows.Globalization;
using Windows.Media.Ocr;

namespace EndHud.Vision.Inn;

/// <summary>
/// Reads the inn's text (Chinese labels and numbers) with the Windows OCR engine for Simplified
/// Chinese. A region is turned into black ink on white first: the caller says which pixels are
/// ink (white text on a dark pill, dark text on a light card, red text when time is up), and the
/// region is enlarged to its 4K size so a small window reads like a large one.
/// </summary>
public sealed class InnText
{
    private const int Padding = 12;
    private const int MinHeight = 100;
    // Below this many ink pixels (at 4K size) a region holds no text worth reading.
    private const int MinInk = 20;

    private readonly OcrEngine _engine;
    // One recognition at a time: the engine read garbage when two callers shared it at once.
    private readonly SemaphoreSlim _busy = new(1, 1);
    // Numbers alone ("04:46", "92/100") read more reliably with the English engine, when there is one.
    private readonly OcrEngine _digits;

    private InnText(OcrEngine engine, OcrEngine digits)
    {
        _engine = engine;
        _digits = digits;
    }

    /// <summary>The reader, or null when the Simplified Chinese OCR language isn't installed (Windows Settings → Language).</summary>
    public static InnText? TryCreate() =>
        OcrEngine.TryCreateFromLanguage(new Language("zh-Hans-CN")) is { } engine
            ? new InnText(engine, OcrEngine.TryCreateFromLanguage(new Language("en-US")) ?? engine)
            : null;

    /// <summary>Which pixels are text.</summary>
    public delegate bool Ink(byte r, byte g, byte b);

    /// <summary>Near-white text (on a dark or coloured background).</summary>
    public static bool White(byte r, byte g, byte b) => Math.Min(r, Math.Min(g, b)) >= 175 && Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)) <= 60;

    /// <summary>Dark text (on a light card).</summary>
    public static bool Dark(byte r, byte g, byte b) => Math.Max(r, Math.Max(g, b)) <= 110;

    /// <summary>
    /// The text in <paramref name="region"/> (window pixels), lines joined without spaces, or null
    /// when the region holds no ink. <paramref name="hudScale"/> is <see cref="UiScaler.Scale"/>.
    /// </summary>
    public async Task<string?> ReadAsync(Frame frame, PixelRect region, Ink ink, double hudScale)
    {
        var prepared = Prepare(frame, region, ink, hudScale);
        if (prepared is null) return null;
        return string.Concat((await RecognizeAsync(_engine, prepared)).Select(l => string.Concat(l.Words.Select(w => w.Text))));
    }

    private async Task<IReadOnlyList<OcrLine>> RecognizeAsync(OcrEngine engine, Frame prepared)
    {
        using var bitmap = prepared.ToSoftwareBitmap();
        await _busy.WaitAsync();
        try
        {
            return (await engine.RecognizeAsync(bitmap)).Lines;
        }
        finally
        {
            _busy.Release();
        }
    }

    /// <summary>
    /// The text read at a few sizes in turn, for a caller looking for one of a few known words: the
    /// engine reads a short line at one size and returns nothing (or a slip) at another.
    /// </summary>
    public async IAsyncEnumerable<string> ReadAttemptsAsync(Frame frame, PixelRect region, Ink ink, double hudScale)
    {
        foreach (int height in AttemptHeights)
        {
            var prepared = Prepare(frame, region, ink, hudScale, height);
            if (prepared is null) yield break;
            yield return string.Concat((await RecognizeAsync(_engine, prepared)).Select(l => string.Concat(l.Words.Select(w => w.Text))));
        }
        // Last, the region's own shades of grey (dark text kept dark, light text turned dark): in a
        // small window picking ink pixel by pixel can break a character the engine reads whole.
        if (PrepareGrey(frame, region, ink, hudScale) is { } grey)
            yield return string.Concat((await RecognizeAsync(_engine, grey)).Select(l => string.Concat(l.Words.Select(w => w.Text))));
    }

    /// <summary>The region in grey, enlarged to 4K size (at least <see cref="MinHeight"/> tall), the text made the dark side.</summary>
    internal static Frame? PrepareGrey(Frame frame, PixelRect region, Ink ink, double hudScale)
    {
        var r = region.ClampTo(frame.Width, frame.Height);
        if (r.Width < 2 || r.Height < 2) return null;
        double scale = Math.Clamp(Math.Max(1 / hudScale, MinHeight / (double)r.Height), 1, 6);
        var big = frame.Crop(r).ResizeSmooth((int)Math.Round(r.Width * scale), (int)Math.Round(r.Height * scale));
        // Which side the text is on: the ink's mean brightness against the rest's.
        double inkSum = 0, restSum = 0;
        int inkCount = 0, restCount = 0;
        for (int y = r.Y; y < r.Bottom; y++)
        for (int x = r.X; x < r.Right; x++)
        {
            var (cr, cg, cb) = frame.GetPixel(x, y);
            double v = (cr + cg + cb) / 3.0;
            if (ink(cr, cg, cb)) { inkSum += v; inkCount++; }
            else { restSum += v; restCount++; }
        }
        if (inkCount == 0 || restCount == 0) return null;
        bool invert = inkSum / inkCount > restSum / restCount;
        int width = big.Width + Padding * 2, height = big.Height + Padding * 2;
        var buffer = new byte[width * height * 4];
        Array.Fill(buffer, (byte)255);
        for (int y = 0; y < big.Height; y++)
        for (int x = 0; x < big.Width; x++)
        {
            int i = (y * big.Width + x) * 4;
            int v = (big.Bgra[i] + big.Bgra[i + 1] + big.Bgra[i + 2]) / 3;
            byte g = (byte)(invert ? 255 - v : v);
            int o = ((y + Padding) * width + x + Padding) * 4;
            buffer[o] = buffer[o + 1] = buffer[o + 2] = g;
        }
        return new Frame(width, height, buffer);
    }

    private static readonly int[] AttemptHeights = [MinHeight, 140, 70];

    /// <summary>Like <see cref="ReadAsync"/>, for a region holding only digits and their punctuation.</summary>
    public async Task<string?> ReadNumberAsync(Frame frame, PixelRect region, Ink ink, double hudScale)
    {
        var prepared = Prepare(frame, region, ink, hudScale);
        if (prepared is null) return null;
        return string.Concat((await RecognizeAsync(_digits, prepared)).Select(l => string.Concat(l.Words.Select(w => w.Text))));
    }

    /// <summary>The lines read in <paramref name="region"/>, each with its words joined, top to bottom.</summary>
    public async Task<IReadOnlyList<string>> ReadLinesAsync(Frame frame, PixelRect region, Ink ink, double hudScale)
    {
        var prepared = Prepare(frame, region, ink, hudScale);
        if (prepared is null) return [];
        return [.. (await RecognizeAsync(_engine, prepared)).Select(l => string.Concat(l.Words.Select(w => w.Text)))];
    }

    /// <summary>
    /// Each line read in <paramref name="region"/> with where it is in the frame, top to bottom, for a
    /// caller that finds a label first and then reads what stands beside it.
    /// </summary>
    public Task<IReadOnlyList<(string Text, PixelRect Place)>> ReadPlacedLinesAsync(Frame frame, PixelRect region, Ink ink, double hudScale) =>
        ReadPlacedLinesAsync(frame, region, hudScale, Prepare(frame, region.ClampTo(frame.Width, frame.Height), ink, hudScale));

    /// <summary>
    /// Like <see cref="ReadPlacedLinesAsync(Frame, PixelRect, Ink, double)"/> at a few preparations
    /// in turn (ink picked out at two sizes, then the region's own greys), for a caller that keeps
    /// the attempt that makes the most sense: in a small window one reads what another misses.
    /// </summary>
    public async IAsyncEnumerable<IReadOnlyList<(string Text, PixelRect Place)>> ReadPlacedLinesAttemptsAsync(Frame frame, PixelRect region, Ink ink, double hudScale)
    {
        var r = region.ClampTo(frame.Width, frame.Height);
        yield return await ReadPlacedLinesAsync(frame, r, hudScale, Prepare(frame, r, ink, hudScale));
        yield return await ReadPlacedLinesAsync(frame, r, hudScale, Prepare(frame, r, ink, Math.Min(hudScale, 0.5)));
        yield return await ReadPlacedLinesAsync(frame, r, hudScale, PrepareGrey(frame, r, ink, hudScale));
    }

    private async Task<IReadOnlyList<(string Text, PixelRect Place)>> ReadPlacedLinesAsync(Frame frame, PixelRect region, double hudScale, Frame? prepared, bool words = false)
    {
        var r = region.ClampTo(frame.Width, frame.Height);
        if (prepared is null) return [];
        // The prepared image is the region enlarged by one factor, inside a padding.
        double fx = (prepared.Width - 2 * Padding) / (double)r.Width, fy = (prepared.Height - 2 * Padding) / (double)r.Height;
        PixelRect Place(double left, double top, double right, double bottom) => new(
            r.X + (int)Math.Round((left - Padding) / fx), r.Y + (int)Math.Round((top - Padding) / fy),
            (int)Math.Round((right - left) / fx), (int)Math.Round((bottom - top) / fy));
        var lines = await RecognizeAsync(_engine, prepared);
        if (words)
            return [.. lines.SelectMany(l => l.Words).Select(w =>
                (w.Text, Place(w.BoundingRect.X, w.BoundingRect.Y, w.BoundingRect.X + w.BoundingRect.Width, w.BoundingRect.Y + w.BoundingRect.Height)))];
        return [.. lines.Where(l => l.Words.Count > 0).Select(l => (string.Concat(l.Words.Select(w => w.Text)), Place(
            l.Words.Min(w => w.BoundingRect.X), l.Words.Min(w => w.BoundingRect.Y),
            l.Words.Max(w => w.BoundingRect.X + w.BoundingRect.Width), l.Words.Max(w => w.BoundingRect.Y + w.BoundingRect.Height))))];
    }

    /// <summary>Each word read in <paramref name="region"/> with where it is in the frame (a line may join things standing apart).</summary>
    public Task<IReadOnlyList<(string Text, PixelRect Place)>> ReadPlacedWordsAsync(Frame frame, PixelRect region, Ink ink, double hudScale) =>
        ReadPlacedLinesAsync(frame, region, hudScale, Prepare(frame, region.ClampTo(frame.Width, frame.Height), ink, hudScale), words: true);

    /// <summary>The image handed to the engine: black ink on white, enlarged to the region's 4K size.</summary>
    internal static Frame? Prepare(Frame frame, PixelRect region, Ink ink, double hudScale, int minHeight = MinHeight)
    {
        var r = region.ClampTo(frame.Width, frame.Height);
        if (r.Width < 2 || r.Height < 2) return null;
        if (hudScale < 0.9)
        {
            // A small window: enlarge smoothly to the 4K size before telling ink from background,
            // which loses the thin strokes of characters a dozen pixels tall.
            double enlarge = Math.Min(4, 1 / hudScale);
            frame = frame.Crop(r).ResizeSmooth((int)Math.Round(r.Width * enlarge), (int)Math.Round(r.Height * enlarge));
            r = new PixelRect(0, 0, frame.Width, frame.Height);
            hudScale = 1;
        }

        var mask = new float[r.Width * r.Height];
        int count = 0;
        for (int y = 0; y < r.Height; y++)
        for (int x = 0; x < r.Width; x++)
        {
            var (cr, cg, cb) = frame.GetPixel(r.X + x, r.Y + y);
            if (!ink(cr, cg, cb)) continue;
            mask[y * r.Width + x] = 1;
            count++;
        }
        // Enlarged to the region's 4K size at least, and to a height the engine reads reliably: it
        // returned nothing for a clean 50 px line ("点餐中") that it read at twice the size.
        double scale = Math.Clamp(Math.Max(1 / hudScale, minHeight / (double)r.Height), 1, 6);
        if (count * scale * scale < MinInk) return null;

        int innerW = (int)Math.Round(r.Width * scale), innerH = (int)Math.Round(r.Height * scale);
        int width = innerW + Padding * 2, height = innerH + Padding * 2;
        var buffer = new byte[width * height * 4];
        Array.Fill(buffer, (byte)255);
        for (int y = 0; y < innerH; y++)
        {
            double sy = Math.Clamp((y + 0.5) / scale - 0.5, 0, r.Height - 1);
            int y0 = (int)sy, y1 = Math.Min(y0 + 1, r.Height - 1);
            double fy = sy - y0;
            for (int x = 0; x < innerW; x++)
            {
                double sx = Math.Clamp((x + 0.5) / scale - 0.5, 0, r.Width - 1);
                int x0 = (int)sx, x1 = Math.Min(x0 + 1, r.Width - 1);
                double fx = sx - x0;
                double d = (mask[y0 * r.Width + x0] * (1 - fx) + mask[y0 * r.Width + x1] * fx) * (1 - fy)
                         + (mask[y1 * r.Width + x0] * (1 - fx) + mask[y1 * r.Width + x1] * fx) * fy;
                if (d <= 0) continue;
                int i = ((y + Padding) * width + x + Padding) * 4;
                buffer[i] = buffer[i + 1] = buffer[i + 2] = (byte)Math.Round(255 * (1 - d));
            }
        }
        return new Frame(width, height, buffer);
    }
}
