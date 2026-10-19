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
    // Below this many ink pixels (at 4K size) a region holds no text worth reading.
    private const int MinInk = 20;

    private readonly OcrEngine _engine;
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
        using var bitmap = prepared.ToSoftwareBitmap();
        var result = await _engine.RecognizeAsync(bitmap);
        return string.Concat(result.Lines.Select(l => string.Concat(l.Words.Select(w => w.Text))));
    }

    /// <summary>Like <see cref="ReadAsync"/>, for a region holding only digits and their punctuation.</summary>
    public async Task<string?> ReadNumberAsync(Frame frame, PixelRect region, Ink ink, double hudScale)
    {
        var prepared = Prepare(frame, region, ink, hudScale);
        if (prepared is null) return null;
        using var bitmap = prepared.ToSoftwareBitmap();
        var result = await _digits.RecognizeAsync(bitmap);
        return string.Concat(result.Lines.Select(l => string.Concat(l.Words.Select(w => w.Text))));
    }

    /// <summary>The lines read in <paramref name="region"/>, each with its words joined, top to bottom.</summary>
    public async Task<IReadOnlyList<string>> ReadLinesAsync(Frame frame, PixelRect region, Ink ink, double hudScale)
    {
        var prepared = Prepare(frame, region, ink, hudScale);
        if (prepared is null) return [];
        using var bitmap = prepared.ToSoftwareBitmap();
        var result = await _engine.RecognizeAsync(bitmap);
        return [.. result.Lines.Select(l => string.Concat(l.Words.Select(w => w.Text)))];
    }

    /// <summary>The image handed to the engine: black ink on white, enlarged to the region's 4K size.</summary>
    internal static Frame? Prepare(Frame frame, PixelRect region, Ink ink, double hudScale)
    {
        var r = region.ClampTo(frame.Width, frame.Height);
        if (r.Width < 2 || r.Height < 2) return null;

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
        double scale = Math.Clamp(1 / hudScale, 1, 4);
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
