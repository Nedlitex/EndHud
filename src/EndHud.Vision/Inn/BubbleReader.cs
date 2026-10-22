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
    /// The requests the bubble makes, in the order it says them. Where the icons were seen, the lines are cut where each
    /// request's icon stands (<see cref="Starts"/>); else, a request starting on a line of its own, the lines are taken as
    /// one request or cut once into two, whichever matches best (test_yuanyuan8: 43 of the data's pairs of a gourmand's
    /// requests on their own lines were lost to the cut below, among them the feast pool's tenth gourmand's dish and drink,
    /// tables 1 and 2). Last, for lines
    /// OCR ran together, the lines are cut at their
    /// sentence ends and commas into pieces; every run of consecutive pieces is matched on its own
    /// (<see cref="InnData.MatchSaid"/>), so a request wrapped onto the next line is found whole and
    /// two requests are never read as one sentence (owner, 2026-10-19: the bubble 「来点汤，要酸口，
    /// 促消 / 化。/ 上好的肉！做点菜！」 was joined into "…促消化上好的肉" and only the soup was
    /// found). Runs are taken by how much of the bubble they account for (score × length), each
    /// request once, never two runs sharing a piece.
    /// </summary>
    public IReadOnlyList<GourmandRequestMatch> Requests(InnData data)
    {
        if (Starts is { Count: > 0 } starts && ByStarts(data, starts) is { } byIcons) return byIcons;
        if (ByLines(data) is { } byLines) return byLines;
        return ByPieces(data);
    }

    /// <summary>
    /// The first line of each request, where the bubble's icons stand (<see cref="BubbleReader"/>); null when the icons
    /// weren't seen (a bubble cut by the screen's edge, or read before 2026-10-21).
    /// </summary>
    public IReadOnlyList<int>? Starts { get; init; }

    /// <summary>
    /// Each request's state as its icon shows it, in the order of <see cref="Starts"/>: true once the icon has turned into a
    /// face (green served, yellow served imperfectly, red failed: nothing more to do for it). Null when not read.
    /// </summary>
    public IReadOnlyList<bool>? Resolved { get; init; }

    /// <summary>Where the bubble is on screen (window pixels); null when not read (readings saved before 2026-10-21).</summary>
    public PixelRect? Place { get; init; }

    /// <summary>The requests the bubble makes (<see cref="Requests"/>), each with whether its icon says it's resolved (false when not read).</summary>
    public IReadOnlyList<(GourmandRequestMatch Match, bool Resolved)> RequestsAndState(InnData data)
    {
        var requests = Requests(data);
        // The icons' states go with the requests only when the requests were cut by the icons.
        bool byIcons = Starts is { Count: > 0 } starts && Resolved is { } done && done.Count == starts.Count && requests.Count == starts.Count;
        return [.. requests.Select((m, i) => (m, byIcons && Resolved![i]))];
    }

    /// <summary>A score this close to whole means the lines are one request (two requests can't both be in one's words).</summary>
    private const double WholeScore = 0.9;

    /// <summary>The lines cut where the icons say each request starts, each part matched on its own; null when a part matches nothing.</summary>
    private IReadOnlyList<GourmandRequestMatch>? ByStarts(InnData data, IReadOnlyList<int> starts)
    {
        var bounds = starts.Where(s => s >= 0 && s < Lines.Count).Distinct().Order().ToList();
        if (bounds.Count == 0 || bounds[0] != 0) return null;
        var found = new List<GourmandRequestMatch>();
        for (int i = 0; i < bounds.Count; i++)
        {
            int end = i + 1 < bounds.Count ? bounds[i + 1] : Lines.Count;
            if (Best(data, bounds[i], end) is not { } match || found.Any(f => f.Request == match.Request)) return null;
            found.Add(match);
        }
        return found;
    }

    /// <summary>
    /// A gourmand starts each request on a line of its own (every bubble of test_yuanyuan8): the lines as one request, or cut
    /// once into two, whichever accounts for them best. Null when no cut gives two requests and the whole isn't clearly one.
    /// </summary>
    private IReadOnlyList<GourmandRequestMatch>? ByLines(InnData data)
    {
        if (Lines.Count == 0) return null;
        var whole = Best(data, 0, Lines.Count);
        if (whole is { Score: >= WholeScore }) return [whole];
        (double Score, GourmandRequestMatch First, GourmandRequestMatch Second)? best = null;
        for (int cut = 1; cut < Lines.Count; cut++)
        {
            if (Best(data, 0, cut) is not { } first || Best(data, cut, Lines.Count) is not { } second || first.Request == second.Request) continue;
            double score = first.Score + second.Score;
            if (best is null || score > best.Value.Score) best = (score, first, second);
        }
        return best is { } b ? [b.First, b.Second] : null;
    }

    private GourmandRequestMatch? Best(InnData data, int from, int to) => data.MatchSaid(string.Concat(Lines.Skip(from).Take(to - from))).FirstOrDefault();

    /// <summary>The lines cut at sentence ends and commas, every run of pieces matched (lines OCR ran together).</summary>
    private IReadOnlyList<GourmandRequestMatch> ByPieces(InnData data)
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
/// wrapping onto several lines, a category icon before each request. Found as the patches of
/// the bubble's cream below the table cards (the yellow cards and the small icon bubbles over the
/// tables are far smaller), each read by OCR line by line. The icons say where each request starts,
/// and turn into a face once it's resolved (green, yellow or red). The bubble hangs over the table in the
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

    // The icons' column, in character heights from the bubble's left edge (they end 2.0–2.15 in), and what makes an icon:
    // rows with ink in it, at most this far apart, at least this tall (both in character heights).
    private const double IconsFrom = 0.3, IconsTo = 2.25, IconGap = 0.3, LeastIconHeight = 0.5;
    // A row of the column holds part of an icon when this share of a character's height of its pixels is icon ink.
    private const double IconRowInk = 0.12;
    // An icon turned into a face (the request resolved) when this share of its ink is a face's colour.
    private const double FaceShare = 0.35;

    /// <summary>The largest bubble on screen, or null when there's none (or nothing could be read in it).</summary>
    public async Task<GourmandBubble?> ReadAsync(Frame frame) => (await ReadAllAsync(frame)).FirstOrDefault();

    /// <summary>
    /// Every bubble on screen with words read in it, the largest first. In the 食有老饕 mode every guest is a gourmand, and
    /// test_yuanyuan8 showed up to three bubbles at once, of three tables, at three sizes: reading only the largest left the
    /// others' requests to be read whenever that one happened to be largest.
    /// </summary>
    public async Task<IReadOnlyList<GourmandBubble>> ReadAllAsync(Frame frame)
    {
        var layout = new InnLayout(frame);
        var read = new List<GourmandBubble>();
        foreach (var bubble in FindBubbles(frame, layout))
            if (await ReadOneAsync(frame, layout, bubble) is { } said) read.Add(said);
        return read;
    }

    private async Task<GourmandBubble?> ReadOneAsync(Frame frame, InnLayout layout, PixelRect bubble)
    {
        double s = layout.Scale;
        int inset = (int)(Inset * s);
        var whole = new PixelRect(bubble.X + inset, bubble.Y + inset, bubble.Width - 2 * inset, bubble.Height - 2 * inset);
        var first = await text.ReadPlacedWordsAsync(frame, whole, Ink, s);
        var letters = first.Where(w => w.Text.Any(char.IsLetter)).Select(w => w.Place.Height).Order().ToList();
        if (letters.Count < 3) return null;
        // The characters' height: the median of the words read (the icons' marks are few and small).
        int glyph = letters[letters.Count / 2];
        // A bubble cut by the screen's left edge has lost its icons (and maybe the start of its lines): read from its edge.
        bool cut = bubble.X <= Math.Max(2, (int)Math.Round(Step * s));
        int left = cut ? whole.X : bubble.X + (int)(TextStartsAt * glyph);
        var region = new PixelRect(left, whole.Y, whole.Right - left, whole.Height);
        var placed = (await text.ReadPlacedLinesAsync(frame, region, Ink, s))
            .Where(l => l.Place.Height >= LeastLineHeight * glyph)
            .Select(l => (Text: l.Text.Replace(" ", ""), l.Place))
            .Where(l => l.Text.Length > 0)
            .OrderBy(l => l.Place.Y)
            .ToList();
        var lines = placed.Select(l => l.Text).ToList();
        string joined = string.Concat(lines);
        if (joined.Count(char.IsLetter) < 3) return null;
        var icons = cut ? [] : Icons(frame, bubble, whole, glyph);
        if (icons.Count == 0) return new GourmandBubble(joined, lines) { Place = bubble };
        // Each line belongs to the icon nearest it: a request's icon stands level with its lines' middle.
        var owner = placed.Select(l => icons.Select((icon, i) => (i, d: Math.Abs(icon.Middle - (l.Place.Y + l.Place.Height / 2.0)))).MinBy(x => x.d).i).ToList();
        var starts = new List<int>();
        var resolved = new List<bool>();
        for (int i = 0; i < icons.Count; i++)
        {
            int start = owner.IndexOf(i);
            if (start < 0) continue;
            starts.Add(start);
            resolved.Add(icons[i].Face);
        }
        return new GourmandBubble(joined, lines) { Place = bubble, Starts = starts, Resolved = resolved };
    }

    /// <summary>An icon before a request: its middle (window pixels, down) and whether it has turned into a face.</summary>
    internal readonly record struct BubbleIcon(double Middle, bool Face);

    /// <summary>The icons in the bubble's icon column, top to bottom (plates for a dish, a bottle for a drink, a face once resolved).</summary>
    internal static List<BubbleIcon> Icons(Frame frame, PixelRect bubble, PixelRect whole, int glyph)
    {
        int x0 = Math.Max(0, bubble.X + (int)(IconsFrom * glyph)), x1 = Math.Min(frame.Width, bubble.X + (int)(IconsTo * glyph));
        int y0 = Math.Max(0, whole.Y), y1 = Math.Min(frame.Height, whole.Bottom);
        var icons = new List<BubbleIcon>();
        if (x1 <= x0 || y1 <= y0) return icons;
        int runFrom = -1, runTo = -1, faces = 0, inks = 0;
        for (int y = y0; y <= y1; y++)
        {
            int ink = 0, face = 0;
            if (y < y1)
                for (int x = x0; x < x1; x++)
                {
                    var (r, g, b) = frame.GetPixel(x, y);
                    if (IsFace(r, g, b)) { face++; ink++; }
                    else if (Math.Max(r, Math.Max(g, b)) <= 120) ink++;
                }
            bool row = ink >= Math.Max(2, IconRowInk * glyph);
            if (row)
            {
                if (runFrom < 0 || y - runTo > IconGap * glyph)
                {
                    Close();
                    runFrom = y;
                    faces = inks = 0;
                }
                runTo = y;
                faces += face;
                inks += ink;
            }
        }
        Close();
        return icons;

        void Close()
        {
            if (runFrom >= 0 && runTo - runFrom + 1 >= LeastIconHeight * glyph)
                icons.Add(new BubbleIcon((runFrom + runTo) / 2.0, faces >= FaceShare * inks));
            runFrom = -1;
        }
    }

    /// <summary>A face's colour: bright and strongly coloured (green served, yellow-green imperfect, red failed), unlike the dark icons and the cream.</summary>
    private static bool IsFace(byte r, byte g, byte b)
    {
        int max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        return max >= 150 && max - min >= 110;
    }

    /// <summary>The bubble's text: dark brown, or the orange of its key words.</summary>
    internal static bool Ink(byte r, byte g, byte b) =>
        Math.Max(r, Math.Max(g, b)) <= 120 || (r >= 200 && g >= 70 && g <= 170 && b <= 90);

    /// <summary>The bubble's cream: light, warm (red well over blue), translucent over the scene.</summary>
    private static bool Cream(byte r, byte g, byte b) =>
        r is >= 220 and <= 252 && g is >= 198 and <= 236 && b is >= 135 and <= 178 && r - b is >= 55 and <= 105;

    /// <summary>Every bubble-sized patch of cream below the cards, in window pixels, the largest first.</summary>
    internal static IReadOnlyList<PixelRect> FindBubbles(Frame frame, InnLayout layout)
    {
        double s = layout.Scale;
        int step = Math.Max(2, (int)Math.Round(Step * s)), top = (int)(Top * s);
        int cols = frame.Width / step, rows = (frame.Height - top) / step;
        if (cols <= 0 || rows <= 0) return [];
        var cream = new bool[cols * rows];
        for (int y = 0; y < rows; y++)
        for (int x = 0; x < cols; x++)
        {
            var (r, g, b) = frame.GetPixel(x * step, top + y * step);
            cream[y * cols + x] = Cream(r, g, b);
        }
        var seen = new bool[cream.Length];
        var stack = new Stack<int>();
        var found = new List<(int Cells, PixelRect Place)>();
        // Cells are counted at 4K's spacing whatever the window's size.
        double cellAt4K = (step / s / Step) * (step / s / Step);
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
            int width = (rr - l + 1) * step, height = (bb - t + 1) * step;
            if (cells * cellAt4K < MinCells || width < MinWidth * s || height < MinHeight * s) continue;
            found.Add((cells, new PixelRect(l * step, top + t * step, width, height)));
        }
        return [.. found.OrderByDescending(f => f.Cells).Select(f => f.Place)];
    }
}
