using EndHud.Vision.Operators;

namespace EndHud.Vision;

/// <summary>A status the enemy has, recognised by its icon under the boss's bars.</summary>
/// <param name="Position">Its place in the row, from 0 (the row reflows as statuses come and go);
/// <see cref="RingPosition"/> for the ring at the end of the HP bar: an elemental infliction (<see cref="InflictionRing"/>) or Vulnerable (<see cref="VulnerableRing"/>);
/// <see cref="SecondRingPosition"/> for the other of the two beside it when the boss has both.</param>
/// <param name="Score">How well the icon matched (normalised correlation, 1 = identical).</param>
/// <param name="Stacks">Stacks (inflictions and Vulnerable, 1–4) or level (reactions, I–IV as 1–4; a reaction with no badge
/// is I); null when not read (covered, a dark scene that could hide a badge, or a status whose count
/// EndHud doesn't read yet: see <see cref="BossStatusReader.CountsRead"/>).</param>
public sealed record BossStatusReading(string StatusId, int Position, double Score, int? Stacks)
{
    public const int RingPosition = -1;

    /// <summary>The icon right of the ring's place: the second of Vulnerable and an infliction shown together (the later applied).</summary>
    public const int SecondRingPosition = -2;
}

/// <summary>
/// The boss's statuses: a row of icons in square frames under its HP bars, top centre, 70 px apart
/// at 4K. Each icon is the wiki's (downloaded with the operator library, <see cref="OperatorLibrary.Statuses"/>),
/// drawn on the frame's dark background, with an optional stack badge in its bottom-right corner.
/// An icon is reported only when one wiki icon clearly matches it; an icon the wiki doesn't have
/// (some are an operator's own debuff), or one covered by an effect, isn't reported at all, so a
/// status not listed may still be there but unrecognised.
/// </summary>
public sealed class BossStatusReader(OperatorLibrary library)
{
    /// <summary>Slot 0's inner square (the dark background holding the icon) at 4K, top-centre anchored.</summary>
    internal static readonly PixelRect FirstSquare = new(1448, 184, 40, 40);
    internal const int Pitch = 70;
    internal const int MaxPositions = 10;

    // Normalised correlation needed, and the lead over the next-best icon. Measured on the boss fight
    // (Fixtures/battle-hud): right icons 0.60–0.80 (a fading row: just under 0.60) with a lead of
    // 0.13–0.25; other icons, flashes and scenery reached 0.60 too (once 0.88, orange scenery against
    // the flame icons) but never led by more than 0.03. The lead is what tells them apart.
    internal double MatchAt { get; init; } = 0.55;
    internal double Margin { get; init; } = 0.1;

    /// <summary>How much of the inner square the icon fills, as tried.</summary>
    private static readonly double[] IconFills = [0.72, 0.8, 0.88];

    private sealed record IconTemplate(int Size, float[] Premultiplied, float[] Transparency, bool[] Compared);

    private readonly Dictionary<(string Id, int Size), IconTemplate> _templates = new();

    /// <summary>
    /// The statuses recognised in the row (empty when there's no row or none is recognised), then the
    /// icons at the end of the HP bar: an elemental infliction, Vulnerable, or both side by side (<see cref="RingIcons"/>). Each carries its count where
    /// EndHud reads one: the infliction's stacks, a measured reaction's level (<see cref="ReactionLevel"/>).
    /// </summary>
    public IReadOnlyList<BossStatusReading> Read(Frame frame)
    {
        var found = new List<BossStatusReading>();
        var ui = new UiScaler(frame.Width, frame.Height);
        foreach (var (position, best, _) in Scores(frame))
        {
            if (best is not { } b) continue;
            int? level = ReactionLevel.Measured.Contains(b.Id) ? ReactionLevel.ReadLevel(frame, ui.TopCenter(FirstSquare.Offset(position * Pitch, 0))) : null;
            found.Add(new BossStatusReading(b.Id, position, b.Score, level));
        }
        var icons = RingIcons(frame);
        for (int i = 0; i < icons.Count; i++)
            found.Add(new BossStatusReading(icons[i].Element, i == 0 ? BossStatusReading.RingPosition : BossStatusReading.SecondRingPosition, 1, icons[i].Stacks));
        return found;
    }

    /// <summary>
    /// The icons at the right end of the HP bar, each with its stack arcs: an elemental infliction or
    /// Vulnerable (a shield with a slash) in the ring's place, and when the boss has both, the other one
    /// beside it (<see cref="InflictionRing.SecondOffset"/>), in the order they were applied (test_rossi:
    /// heat then Vulnerable, Vulnerable then heat, Vulnerable then cryo).
    /// <para>
    /// The second place is read only when the first is read and holds the other kind. The game packs the
    /// icons to the left, so a second icon with no first is a moment of change: the first fading where it
    /// stood (test_rossi 25.5 s) or one arriving. And two inflictions side by side are an infliction
    /// being replaced: in the Si recording at 20.1-20.6 s the new nature icon took the first place while
    /// the old cryo slid right and faded out (gone at 20.7 s), so the one on the right is already gone
    /// from the boss.
    /// </para>
    /// <para>
    /// The second icon must also have its top stack arc lit (white with its dark outline,
    /// <see cref="InflictionRing.SegmentShares"/>), which every second icon in test_rossi had (1.00): in the
    /// Typhoeus recording at 105.5 s white sky crossed by a dark branch stood in the second place, white on
    /// both sides of a dark diagonal (0.99, slash 88) like Vulnerable's shield, but with no arc at all (0.00).
    /// </para>
    /// </summary>
    internal static IReadOnlyList<InflictionRing.Infliction> RingIcons(Frame frame)
    {
        if (RingIcon(frame, 0) is not { } first) return [];
        if (InflictionRing.SegmentShares(frame, InflictionRing.SecondOffset)[0] < InflictionRing.LitShare
            || RingIcon(frame, InflictionRing.SecondOffset) is not { } second) return [first];
        bool firstVulnerable = first.Element == VulnerableRing.StatusId, secondVulnerable = second.Element == VulnerableRing.StatusId;
        return firstVulnerable == secondVulnerable ? [first] : [first, second];
    }

    private static InflictionRing.Infliction? RingIcon(Frame frame, double offset) =>
        InflictionRing.ReadWithStacks(frame, offset) ?? VulnerableRing.Read(frame, offset);

    /// <summary>Statuses whose stack count or level EndHud reads (the editor offers a count only for these).</summary>
    public static IReadOnlySet<string> CountsRead { get; } =
        new HashSet<string>(["Heat Infliction", "Cryo Infliction", "Electric Infliction", "Nature Infliction", VulnerableRing.StatusId, .. ReactionLevel.Measured], StringComparer.Ordinal);

    /// <summary>For each position: the accepted status (if any) and the two best scores (for measuring thresholds).</summary>
    internal IEnumerable<(int Position, (string Id, double Score)? Accepted, ((string Id, double Score) First, (string Id, double Score) Second) Top)> Scores(Frame frame)
    {
        var ui = new UiScaler(frame.Width, frame.Height);
        for (int position = 0; position < MaxPositions; position++)
        {
            var square = ui.TopCenter(FirstSquare.Offset(position * Pitch, 0));
            if (square.Width < 12 || square.X < 0 || square.Y < 0 || square.Right > frame.Width || square.Bottom > frame.Height) yield break;

            var background = Background(frame, square);
            (string Id, double Score) first = ("", -1), second = ("", -1);
            foreach (var status in library.Statuses)
            {
                if (library.StatusIcon(status.Id) is not { } icon) continue;
                double score = -1;
                foreach (double fill in IconFills)
                {
                    var template = Template(status.Id, icon, Math.Max(8, (int)Math.Round(square.Width * fill)));
                    int offset = (square.Width - template.Size) / 2;
                    for (int dy = -2; dy <= 2; dy++)
                    for (int dx = -2; dx <= 2; dx++)
                        score = Math.Max(score, Correlate(frame, square, template, offset + dx, offset + dy, background));
                }
                if (score > first.Score) (second, first) = (first, (status.Id, score));
                else if (score > second.Score) second = (status.Id, score);
            }
            bool accepted = first.Score >= MatchAt && first.Score - second.Score >= Margin;
            yield return (position, accepted ? first : null, (first, second));
        }
    }

    /// <summary>The frame's background colour: the median of the square's top-left corner, which icons leave empty.</summary>
    internal static (int R, int G, int B) Background(Frame frame, PixelRect square)
    {
        var rs = new List<int>();
        var gs = new List<int>();
        var bs = new List<int>();
        int corner = Math.Max(2, square.Width / 6);
        for (int y = square.Y; y < square.Y + corner; y++)
        for (int x = square.X; x < square.X + corner; x++)
        {
            var (r, g, b) = frame.GetPixel(x, y);
            rs.Add(r); gs.Add(g); bs.Add(b);
        }
        rs.Sort(); gs.Sort(); bs.Sort();
        return (rs[rs.Count / 2], gs[gs.Count / 2], bs[bs.Count / 2]);
    }

    /// <summary>The wiki icon at <paramref name="size"/>; the badge corner (bottom right) isn't compared.</summary>
    private IconTemplate Template(string id, Frame icon, int size)
    {
        lock (_templates)
        {
            if (_templates.TryGetValue((id, size), out var cached)) return cached;
            var resized = icon.ResizeSmooth(size, size);
            var premultiplied = new float[size * size * 3];
            var transparency = new float[size * size];
            var compared = new bool[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int i = y * size + x;
                var (r, g, b) = resized.GetPixel(x, y);
                float a = resized.GetAlpha(x, y) / 255f;
                premultiplied[i * 3] = r * a;
                premultiplied[i * 3 + 1] = g * a;
                premultiplied[i * 3 + 2] = b * a;
                transparency[i] = 1 - a;
                compared[i] = !(x >= size * 0.55 && y >= size * 0.55);
            }
            return _templates[(id, size)] = new IconTemplate(size, premultiplied, transparency, compared);
        }
    }

    /// <summary>Normalised correlation (over RGB) of the frame with the icon blended over the frame's background.</summary>
    private static double Correlate(Frame frame, PixelRect square, IconTemplate t, int ox, int oy, (int R, int G, int B) background)
    {
        double sumT = 0, sumI = 0, sumTT = 0, sumII = 0, sumTI = 0;
        int n = 0;
        for (int y = 0; y < t.Size; y++)
        for (int x = 0; x < t.Size; x++)
        {
            int i = y * t.Size + x;
            if (!t.Compared[i]) continue;
            int px = square.X + ox + x, py = square.Y + oy + y;
            if ((uint)px >= (uint)frame.Width || (uint)py >= (uint)frame.Height) continue;
            var (r, g, b) = frame.GetPixel(px, py);
            float clear = t.Transparency[i];
            Add(t.Premultiplied[i * 3] + clear * background.R, r);
            Add(t.Premultiplied[i * 3 + 1] + clear * background.G, g);
            Add(t.Premultiplied[i * 3 + 2] + clear * background.B, b);
        }
        if (n == 0) return -1;
        double cov = sumTI - sumT * sumI / n, varT = sumTT - sumT * sumT / n, varI = sumII - sumI * sumI / n;
        return varT <= 1e-6 || varI <= 1e-6 ? -1 : cov / Math.Sqrt(varT * varI);

        void Add(double tv, double iv)
        {
            sumT += tv; sumI += iv; sumTT += tv * tv; sumII += iv * iv; sumTI += tv * iv;
            n++;
        }
    }
}
