using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace EndHud.Vision;

/// <summary>
/// Reads short white HUD text (operator levels) with the OCR engine built into Windows.
/// The region is cleaned of background, turned into smooth dark-on-white text and upscaled
/// first, which is what the engine reads best.
/// </summary>
public sealed class TextRecognizer
{
    // Windows OCR is most reliable when glyphs are a few dozen pixels tall. Its results on tiny
    // crops vary with scale, so when the caller rejects a reading we retry at another size.
    private static readonly int[] TargetRegionHeights = [120, 90, 160];
    private const int Padding = 16;
    private const double MaxSourceUpscale = 4;
    // Below this HUD scale the region is enlarged first. On the simulated screens it decides
    // whether small screens read levels at all: 8 instead of 2 of 15 at 1600x900, 6 instead of 4
    // in a 1700x956 window (and one fewer at 1080p, so it isn't applied above this).
    private const double EnlargeBelowScale = 0.5;

    private readonly OcrEngine _engine;

    public TextRecognizer()
    {
        // HUD text is Latin letters and digits; any installed OCR language reads it, English best.
        _engine = OcrEngine.TryCreateFromLanguage(new Language("en-US"))
                  ?? OcrEngine.TryCreateFromLanguage(new Language("zh-Hans-CN"))
                  ?? OcrEngine.TryCreateFromUserProfileLanguages()
                  ?? throw new InvalidOperationException(
                      "No Windows OCR language is installed (Settings → Time & language → Language → add a language with OCR).");
    }

    public string LanguageTag => _engine.RecognizerLanguage.LanguageTag;

    /// <summary>
    /// The text of each attempt in turn (the region at several scales, then
    /// <paramref name="glyphRows"/> if given), read only as far as the caller enumerates, for a
    /// caller that wants attempts to agree. <paramref name="hudScale"/> is how large the game's HUD
    /// is relative to 4K (<see cref="UiScaler.Scale"/>). The cleanup is tuned on 4K, where glyphs
    /// are a couple of dozen pixels tall; picking out text pixels from glyphs a few pixels tall
    /// loses their thin strokes, so in a small window the region is first enlarged smoothly to its
    /// 4K size.
    /// </summary>
    public async IAsyncEnumerable<string> ReadAttemptsAsync(Frame frame, PixelRect region, PixelRect? glyphRows, double hudScale)
    {
        var attempts = TargetRegionHeights.Select(h => (region, h, edgeBlobsAreBackground: true));
        if (glyphRows is { } tight)
            attempts = attempts.Concat(TargetRegionHeights.Select(h => (tight, h, edgeBlobsAreBackground: false)));

        double enlarge = hudScale < EnlargeBelowScale ? Math.Clamp(1 / hudScale, 1, MaxSourceUpscale) : 1;
        foreach (var (rect, targetHeight, edgeBlobsAreBackground) in attempts)
        {
            var (source, sourceRect) = enlarge > 1.05 ? Enlarge(frame, rect, enlarge) : (frame, rect);
            if (source is null) yield break;
            var prepared = Prepare(source, sourceRect, targetHeight, edgeBlobsAreBackground);
            if (prepared is null) yield break;
            using var bitmap = prepared.ToSoftwareBitmap();
            yield return (await _engine.RecognizeAsync(bitmap)).Text;
        }
    }

    private static (Frame? Frame, PixelRect Rect) Enlarge(Frame frame, PixelRect region, double factor)
    {
        var r = region.ClampTo(frame.Width, frame.Height);
        if (r.IsEmpty) return (null, r);
        var enlarged = frame.Crop(r).ResizeSmooth((int)Math.Round(r.Width * factor), (int)Math.Round(r.Height * factor));
        return (enlarged, new PixelRect(0, 0, enlarged.Width, enlarged.Height));
    }

    /// <summary>The image actually handed to the OCR engine (exposed for debugging layouts).</summary>
    /// <param name="edgeBlobsAreBackground">
    /// Discard blobs touching the region's top/bottom edge. Right when the region has margin around
    /// the text; wrong for a crop of exactly the text rows, where every glyph touches both edges.
    /// </param>
    public static Frame? Prepare(Frame frame, PixelRect region, int targetHeight = 120, bool edgeBlobsAreBackground = true)
    {
        var r = region.ClampTo(frame.Width, frame.Height);
        if (r.IsEmpty) return null;

        // 1. Decide which pixels are text: bright, unsaturated, in glyph-shaped blobs.
        var ink = new bool[r.Width * r.Height];
        for (int y = 0; y < r.Height; y++)
        for (int x = 0; x < r.Width; x++)
        {
            var (cr, cg, cb) = frame.GetPixel(r.X + x, r.Y + y);
            ink[y * r.Width + x] = IsTextPixel(cr, cg, cb);
        }
        RemoveNonGlyphBlobs(ink, r.Width, r.Height, edgeBlobsAreBackground);

        // 2. Keep the anti-aliased edge around each glyph (1 px), using its real brightness.
        var darkness = new float[r.Width * r.Height];
        for (int y = 0; y < r.Height; y++)
        for (int x = 0; x < r.Width; x++)
        {
            if (!NearInk(ink, r.Width, r.Height, x, y)) continue;
            var (cr, cg, cb) = frame.GetPixel(r.X + x, r.Y + y);
            darkness[y * r.Width + x] = Whiteness(cr, cg, cb);
        }

        // 3. Upscale bilinearly and draw dark text on white.
        double scale = Math.Max(1.0, targetHeight / (double)r.Height);
        int innerW = (int)Math.Round(r.Width * scale), innerH = (int)Math.Round(r.Height * scale);
        int width = innerW + Padding * 2, height = innerH + Padding * 2;
        var buffer = new byte[width * height * 4];
        Array.Fill(buffer, (byte)255);

        for (int y = 0; y < innerH; y++)
        {
            double sy = Math.Clamp((y + 0.5) / scale - 0.5, 0, r.Height - 1);
            int y0 = (int)sy, y1 = Math.Min(y0 + 1, r.Height - 1);
            double fy = sy - y0;
            int row = ((y + Padding) * width + Padding) * 4;
            for (int x = 0; x < innerW; x++)
            {
                double sx = Math.Clamp((x + 0.5) / scale - 0.5, 0, r.Width - 1);
                int x0 = (int)sx, x1 = Math.Min(x0 + 1, r.Width - 1);
                double fx = sx - x0;
                double d = (darkness[y0 * r.Width + x0] * (1 - fx) + darkness[y0 * r.Width + x1] * fx) * (1 - fy)
                         + (darkness[y1 * r.Width + x0] * (1 - fx) + darkness[y1 * r.Width + x1] * fx) * fy;
                if (d <= 0) continue;
                byte v = (byte)Math.Round(255 * (1 - Math.Min(1, d)));
                int i = row + x * 4;
                buffer[i] = buffer[i + 1] = buffer[i + 2] = v;
            }
        }
        return new Frame(width, height, buffer);
    }

    private static bool NearInk(bool[] ink, int width, int height, int x, int y)
    {
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            int nx = x + dx, ny = y + dy;
            if (nx >= 0 && ny >= 0 && nx < width && ny < height && ink[ny * width + nx]) return true;
        }
        return false;
    }

    /// <summary>0 for background-ish pixels, rising to 1 for pure white.</summary>
    private static float Whiteness(byte r, byte g, byte b)
    {
        int min = Math.Min(r, Math.Min(g, b));
        int max = Math.Max(r, Math.Max(g, b));
        if (max - min > 70) return 0;
        return Math.Clamp((min - 110) / 110f, 0, 1);
    }

    /// <summary>
    /// Text sits fully inside its region; bright bits of the portrait behind it (hair, collars)
    /// enter from the top or bottom edge, or are specks. Drop those connected blobs.
    /// </summary>
    private static void RemoveNonGlyphBlobs(bool[] ink, int width, int height, bool edgeBlobsAreBackground)
    {
        int minArea = Math.Max(2, width * height / 400);
        var seen = new bool[ink.Length];
        var kept = new List<(List<int> Pixels, int Bottom)>();
        var stack = new Stack<int>();

        for (int start = 0; start < ink.Length; start++)
        {
            if (!ink[start] || seen[start]) continue;

            var blob = new List<int>();
            bool touchesEdge = false;
            int bottom = 0;
            seen[start] = true;
            stack.Push(start);
            while (stack.Count > 0)
            {
                int p = stack.Pop();
                blob.Add(p);
                int x = p % width, y = p / width;
                bottom = Math.Max(bottom, y);
                if (y == 0 || y == height - 1) touchesEdge = true;
                if (x > 0) Visit(p - 1);
                if (x < width - 1) Visit(p + 1);
                if (y > 0) Visit(p - width);
                if (y < height - 1) Visit(p + width);
            }

            if ((touchesEdge && edgeBlobsAreBackground) || blob.Count < minArea)
                Erase(blob);
            else
                kept.Add((blob, bottom));
        }

        // HUD text has no descenders: anything reaching well below the shared baseline is background.
        if (kept.Count >= 2)
        {
            int baseline = kept.Select(b => b.Bottom).Order().ElementAt(kept.Count / 2);
            int tolerance = Math.Max(2, height / 8);
            foreach (var (pixels, bottom) in kept)
                if (bottom > baseline + tolerance) Erase(pixels);
        }

        void Erase(List<int> pixels)
        {
            foreach (int p in pixels) ink[p] = false;
        }

        void Visit(int q)
        {
            if (!ink[q] || seen[q]) return;
            seen[q] = true;
            stack.Push(q);
        }
    }

    // HUD text is near-white; portrait art behind it is coloured or darker.
    private static bool IsTextPixel(byte r, byte g, byte b)
    {
        int min = Math.Min(r, Math.Min(g, b));
        int max = Math.Max(r, Math.Max(g, b));
        return min >= 175 && max - min <= 50;
    }
}
