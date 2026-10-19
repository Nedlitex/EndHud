namespace EndHud.Vision.Inn;

/// <summary>What a gourmand's speech bubble says.</summary>
/// <param name="Text">The bubble's lines joined (a sentence wraps onto the next line).</param>
/// <param name="Sentences">The text cut at its sentence ends (！。): a gourmand with two requests says one sentence each.</param>
public sealed record GourmandBubble(string Text, IReadOnlyList<string> Sentences)
{
    /// <summary>For each sentence, the request it is (the best of <see cref="InnData.MatchSaid"/>); sentences matching none are left out.</summary>
    public IReadOnlyList<GourmandRequestMatch> Requests(InnData data) =>
        [.. Sentences.Select(s => data.MatchSaid(s).FirstOrDefault()).OfType<GourmandRequestMatch>().DistinctBy(m => m.Request)];
}

/// <summary>
/// A gourmand's (老饕) speech bubble: a large cream bubble with dark text, its key words in orange,
/// wrapping onto several lines. Found as the largest patch of the bubble's cream below the table
/// cards (the yellow cards and the small icon bubbles over the tables are far smaller), then read
/// by OCR line by line. Measured on one recorded bubble (Fixtures/inn/session-gourmand-order).
/// </summary>
public sealed class BubbleReader(InnText text)
{
    // At 4K: the grid the cream is looked for on, and the least a bubble measures.
    private const int Step = 8, Top = 300;
    private const int MinWidth = 500, MinHeight = 150, MinCells = 1200;
    // The text inside the bubble's edge (its left edge carries the requests' category icons).
    private const int InsetLeft = 155, Inset = 16;
    private static readonly char[] SentenceEnds = ['！', '!', '。', '？', '?'];

    /// <summary>The bubble on screen, or null when there's none (or nothing could be read in it).</summary>
    public async Task<GourmandBubble?> ReadAsync(Frame frame)
    {
        var layout = new InnLayout(frame);
        if (FindBubble(frame, layout) is not { } bubble) return null;
        double s = layout.Scale;
        var region = new PixelRect(bubble.X + (int)(InsetLeft * s), bubble.Y + (int)(Inset * s),
            bubble.Width - (int)((InsetLeft + Inset) * s), bubble.Height - (int)(2 * Inset * s));
        var lines = await text.ReadLinesAsync(frame, region, Ink, s);
        string joined = string.Concat(lines).Replace(" ", "");
        if (joined.Count(char.IsLetter) < 3) return null;
        var sentences = joined.Split(SentenceEnds, StringSplitOptions.RemoveEmptyEntries)
            .Where(x => x.Count(char.IsLetter) >= 3).ToList();
        return new GourmandBubble(joined, sentences.Count > 0 ? sentences : [joined]);
    }

    /// <summary>The bubble's text: dark brown, or the orange of its key words.</summary>
    internal static bool Ink(byte r, byte g, byte b) =>
        Math.Max(r, Math.Max(g, b)) <= 120 || (r >= 200 && g >= 70 && g <= 170 && b <= 90);

    /// <summary>The bubble's cream: light, warm (red well over blue), translucent over the scene.</summary>
    private static bool Cream(byte r, byte g, byte b) =>
        r is >= 220 and <= 252 && g is >= 198 and <= 236 && b is >= 135 and <= 178 && r - b is >= 55 and <= 105;

    /// <summary>The largest patch of cream below the cards, in window pixels, or null when none is bubble-sized.</summary>
    internal static PixelRect? FindBubble(Frame frame, InnLayout layout)
    {
        double s = layout.Scale;
        int step = Math.Max(2, (int)Math.Round(Step * s)), top = (int)(Top * s);
        int cols = frame.Width / step, rows = (frame.Height - top) / step;
        if (cols <= 0 || rows <= 0) return null;
        var cream = new bool[cols * rows];
        for (int y = 0; y < rows; y++)
        for (int x = 0; x < cols; x++)
        {
            var (r, g, b) = frame.GetPixel(x * step, top + y * step);
            cream[y * cols + x] = Cream(r, g, b);
        }
        var seen = new bool[cream.Length];
        var stack = new Stack<int>();
        (int Cells, int L, int T, int R, int B) best = (0, 0, 0, 0, 0);
        for (int start = 0; start < cream.Length; start++)
        {
            if (!cream[start] || seen[start]) continue;
            int cells = 0, l = int.MaxValue, t = int.MaxValue, rr = -1, bb = -1;
            seen[start] = true;
            stack.Push(start);
            while (stack.Count > 0)
            {
                int p = stack.Pop();
                int x = p % cols, y = p / cols;
                cells++;
                l = Math.Min(l, x); rr = Math.Max(rr, x); t = Math.Min(t, y); bb = Math.Max(bb, y);
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= cols || ny >= rows) continue;
                    int q = ny * cols + nx;
                    if (!cream[q] || seen[q]) continue;
                    seen[q] = true;
                    stack.Push(q);
                }
            }
            if (cells > best.Cells) best = (cells, l, t, rr, bb);
        }
        // Cells are counted at 4K's spacing whatever the window's size.
        double cellsAt4K = best.Cells * (step / s / Step) * (step / s / Step);
        int width = (best.R - best.L + 1) * step, height = (best.B - best.T + 1) * step;
        if (cellsAt4K < MinCells || width < MinWidth * s || height < MinHeight * s) return null;
        return new PixelRect(best.L * step, top + best.T * step, width, height);
    }
}
