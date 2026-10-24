namespace EndHud.Vision;

/// <summary>
/// Finds a white HUD shape (a marker, a button's letters) where it should be: the shape, drawn as
/// text rows with '#' for white, is resampled to the screen's size and compared with the white
/// pixels there, allowing a few pixels' shift. A '?' is a pixel left out of the comparison (the
/// scenery around a HUD element, white or not).
/// </summary>
internal static class WhiteShape
{
    /// <summary>
    /// Overlap of white pixels with <paramref name="shape"/> laid over <paramref name="rect"/>
    /// (intersection over union, 0–1), at the best shift within <paramref name="searchRadius"/>.
    /// </summary>
    /// <param name="whiteMin">A pixel is white when all three channels are above this.</param>
    public static double Score(Frame frame, PixelRect rect, string[] shape, int whiteMin, int searchRadius)
    {
        int w = rect.Width, h = rect.Height;
        if (w < 10 || h < 6) return 0;
        var expected = new bool[w * h];
        var ignored = new bool[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            char c = shape[y * shape.Length / h][x * shape[0].Length / w];
            expected[y * w + x] = c == '#';
            ignored[y * w + x] = c == '?';
        }

        // Which pixels are white, over the rectangle and the search margin around it.
        int m = searchRadius, ww = w + 2 * m, wh = h + 2 * m;
        var white = new bool[ww * wh];
        for (int y = 0; y < wh; y++)
        for (int x = 0; x < ww; x++)
        {
            int px = rect.X - m + x, py = rect.Y - m + y;
            if ((uint)px >= (uint)frame.Width || (uint)py >= (uint)frame.Height) continue;
            var (r, g, b) = frame.GetPixel(px, py);
            white[y * ww + x] = Math.Min(r, Math.Min(g, b)) > whiteMin;
        }

        double best = 0;
        for (int dy = -m; dy <= m; dy++)
        for (int dx = -m; dx <= m; dx++)
        {
            int both = 0, either = 0;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (ignored[y * w + x]) continue;
                bool isWhite = white[(y + m + dy) * ww + x + m + dx];
                bool e = expected[y * w + x];
                if (isWhite && e) both++;
                if (isWhite || e) either++;
            }
            if (either > 0) best = Math.Max(best, both / (double)either);
        }
        return best;
    }
}
