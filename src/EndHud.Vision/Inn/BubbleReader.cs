namespace EndHud.Vision.Inn;

/// <summary>What a gourmand's speech bubble says.</summary>
/// <param name="Text">The bubble's lines joined, for the log and the "not recognised" line.</param>
/// <param name="Lines">The bubble's lines as read, top to bottom. A request wraps onto the next line,
/// and a gourmand with two requests starts the second on a line of its own.</param>
public sealed record GourmandBubble(string Text, IReadOnlyList<string> Lines)
{
    // Where a bubble's text is cut into pieces: its sentence ends and commas.
    private static readonly char[] Breaks = ['！', '!', '。', '？', '?', '，', ',', '、', '…'];

    /// <summary>The most consecutive pieces one request is looked for in (the longest request has four, and wraps).</summary>
    private const int MostPiecesPerRequest = 6;

    /// <summary>
    /// The requests the bubble makes, in the order it says them. The lines are cut at their
    /// sentence ends and commas into pieces; every run of consecutive pieces is matched on its own
    /// (<see cref="InnData.MatchSaid"/>), so a request wrapped onto the next line is found whole and
    /// two requests are never read as one sentence (owner, 2026-10-19: the bubble 「来点汤，要酸口，
    /// 促消 / 化。/ 上好的肉！做点菜！」 was joined into "…促消化上好的肉" and only the soup was
    /// found). Runs are taken by how much of the bubble they account for (score × length), each
    /// request once, never two runs sharing a piece.
    /// </summary>
    public IReadOnlyList<GourmandRequestMatch> Requests(InnData data)
    {
        var pieces = Lines.SelectMany(l => l.Split(Breaks, StringSplitOptions.RemoveEmptyEntries))
            .Where(p => p.Any(char.IsLetter)).ToList();
        var found = new List<(int First, int Last, GourmandRequestMatch Match, double Weight)>();
        for (int first = 0; first < pieces.Count; first++)
        for (int last = first; last < Math.Min(pieces.Count, first + MostPiecesPerRequest); last++)
        {
            string said = string.Concat(pieces.Skip(first).Take(last - first + 1));
            if (data.MatchSaid(said).FirstOrDefault() is { } match)
                found.Add((first, last, match, match.Score * GourmandRequestMatch.Pieces(said).Count));
        }
        var taken = new List<(int First, int Last, GourmandRequestMatch Match, double Weight)>();
        foreach (var run in found.OrderByDescending(f => f.Weight).ThenBy(f => f.First))
        {
            if (taken.Any(t => t.Match.Request == run.Match.Request || (run.First <= t.Last && t.First <= run.Last))) continue;
            taken.Add(run);
        }
        return [.. taken.OrderBy(t => t.First).Select(t => t.Match)];
    }
}

/// <summary>
/// A gourmand's (老饕) speech bubble: a large cream bubble with dark text, its key words in orange,
/// wrapping onto several lines, a category icon before each request. Found as the largest patch of
/// the bubble's cream below the table cards (the yellow cards and the small icon bubbles over the
/// tables are far smaller), then read by OCR line by line. The bubble hangs over the table in the
/// scene, so its size follows the camera (characters 55 to 75 px tall at 4K in the recordings):
/// where the text starts, right of the icons, is measured in characters, from a first read.
/// Measured on two recorded bubbles (Fixtures/inn/session-gourmand-order, session-gourmand-two-lines).
/// </summary>
public sealed class BubbleReader(InnText text)
{
    // At 4K: the grid the cream is looked for on, and the least a bubble measures.
    private const int Step = 8, Top = 300;
    private const int MinWidth = 500, MinHeight = 150, MinCells = 1200;
    // The text inside the bubble's edge, at 4K, for the first read (icons included).
    private const int Inset = 16;
    // Where the text starts, in character heights from the bubble's left edge: the icons end
    // 2.0–2.15 characters in, the text starts 2.6–2.7 in (both recorded bubbles).
    private const double TextStartsAt = 2.35;
    // A line read shorter than this share of the characters' height is a mark, not text (the
    // icons' edges, the bubble's rim).
    private const double LeastLineHeight = 0.6;

    /// <summary>The bubble on screen, or null when there's none (or nothing could be read in it).</summary>
    public async Task<GourmandBubble?> ReadAsync(Frame frame)
    {
        var layout = new InnLayout(frame);
        if (FindBubble(frame, layout) is not { } bubble) return null;
        double s = layout.Scale;
        int inset = (int)(Inset * s);
        var whole = new PixelRect(bubble.X + inset, bubble.Y + inset, bubble.Width - 2 * inset, bubble.Height - 2 * inset);
        var first = await text.ReadPlacedWordsAsync(frame, whole, Ink, s);
        var letters = first.Where(w => w.Text.Any(char.IsLetter)).Select(w => w.Place.Height).Order().ToList();
        if (letters.Count < 3) return null;
        // The characters' height: the median of the words read (the icons' marks are few and small).
        int glyph = letters[letters.Count / 2];
        int left = bubble.X + (int)(TextStartsAt * glyph);
        var region = new PixelRect(left, whole.Y, whole.Right - left, whole.Height);
        var lines = (await text.ReadPlacedLinesAsync(frame, region, Ink, s))
            .Where(l => l.Place.Height >= LeastLineHeight * glyph)
            .Select(l => l.Text.Replace(" ", ""))
            .Where(l => l.Length > 0)
            .ToList();
        string joined = string.Concat(lines);
        if (joined.Count(char.IsLetter) < 3) return null;
        return new GourmandBubble(joined, lines);
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
