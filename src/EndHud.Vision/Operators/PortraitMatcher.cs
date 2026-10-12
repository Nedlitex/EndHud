namespace EndHud.Vision.Operators;

/// <summary>
/// Identifies the operator in a squad portrait (see <see cref="PartyLayout.Portrait"/>), first
/// against portraits learned in game, then against wiki icons.
/// </summary>
/// <remarks>
/// Similarity is normalised cross-correlation over RGB, so brightness/contrast differences
/// between the wiki art and the game's rendering don't matter. Only the middle of each image is
/// compared: the portrait's edges show the game world behind the operator, which changes.
/// </remarks>
public sealed class PortraitMatcher(OperatorLibrary library)
{
    /// <summary>Size every squad portrait is resampled to before matching or learning.</summary>
    public const int PortraitWidth = 64;
    public const int PortraitHeight = 80;

    // Learned portraits are the same rendering as what we're matching, so they score high.
    internal const double LearnedThreshold = 0.80;
    private const double LearnedMargin = 0.04;
    // Wiki icons are different art of the same face (sometimes a different pose altogether): a
    // guess only, shown when it clearly beats the runner-up and never learned from automatically.
    // Measured on the 4K fixture: right answers score 0.77–0.84 and lead by 0.02–0.04.
    private const double IconThreshold = 0.72;
    private const double IconMargin = 0.03;

    private const int LearnedSearchRadius = 3;
    private const int CalibratedSizeRadius = 3;
    private const int CalibratedOffsetRadius = 3;
    // In the squad panel the head is drawn at about the wiki icon's framing: icon width ≈ portrait
    // width (64 px here). Much smaller sizes only find chance matches in hair texture.
    private static readonly int[] IconSizes = Enumerable.Range(0, 11).Select(i => 52 + i * 2).ToArray(); // 52…72
    // Icons may overhang the portrait by this fraction of their size (the head fills the slot).
    private const double MaxOverhang = 0.2;

    private readonly Dictionary<(string Id, int Size), Template> _iconTemplates = new();
    private static readonly Lazy<bool[]> PortraitMask = new(BuildPortraitMask);

    public OperatorMatch? Match(Frame portrait) => MatchLearned(portrait) ?? MatchIcon(portrait).Match;

    /// <summary>Best match among learned in-game portraits, if clearly confident.</summary>
    public OperatorMatch? MatchLearned(Frame portrait)
    {
        var image = Patch.From(portrait);
        var best = new Dictionary<string, (OperatorInfo Op, double Score)>();
        foreach (var (op, learned) in library.LearnedPortraits())
        {
            double score = SimilarityToLearned(image, learned);
            if (!best.TryGetValue(op.Id, out var b) || score > b.Score) best[op.Id] = (op, score);
        }

        var ranked = best.Values.OrderByDescending(b => b.Score).ToList();
        if (ranked.Count == 0 || ranked[0].Score < LearnedThreshold) return null;
        if (ranked.Count > 1 && ranked[0].Score - ranked[1].Score < LearnedMargin) return null;
        return new OperatorMatch(ranked[0].Op, ranked[0].Score, FromLearned: true);
    }

    /// <summary>Similarity of two squad portraits, allowing a few pixels of misalignment.</summary>
    public double SimilarityToLearned(Frame a, Frame b) => SimilarityToLearned(Patch.From(a), b);

    private static double SimilarityToLearned(Patch image, Frame learned)
    {
        var template = Template.FromPortrait(learned, PortraitMask.Value);
        double best = -1;
        for (int dy = -LearnedSearchRadius; dy <= LearnedSearchRadius; dy++)
        for (int dx = -LearnedSearchRadius; dx <= LearnedSearchRadius; dx++)
            best = Math.Max(best, template.Correlate(image, dx, dy));
        return best;
    }

    /// <summary>Ranks every wiki icon against the portrait; Match is set only for a confident, clear winner.</summary>
    public (OperatorMatch? Match, IReadOnlyList<IconScore> Ranking) MatchIcon(Frame portrait)
    {
        var image = Patch.From(portrait);
        var calibration = library.Calibration;
        var ranking = new List<IconScore>();

        foreach (var op in library.Operators)
        {
            var (shape, size, x, y) = BestPlacement(op.Id, image, calibration);
            double colour = OverlapColourSimilarity(portrait, library.Icons[op.Id].Resize(size, size), x, y);
            ranking.Add(new IconScore(op, (shape + colour) / 2, shape, colour, size, x, y));
        }

        ranking.Sort((a, b) => b.Score.CompareTo(a.Score));
        if (ranking.Count == 0 || ranking[0].Score < IconThreshold) return (null, ranking);
        if (ranking.Count > 1 && ranking[0].Score - ranking[1].Score < IconMargin) return (null, ranking);
        return (new OperatorMatch(ranking[0].Operator, ranking[0].Score, FromLearned: false), ranking);
    }

    /// <summary>
    /// The user confirmed <paramref name="portrait"/> is <paramref name="op"/>: record where that
    /// operator's icon sits in the portrait. Every operator is framed the same way in the squad
    /// panel, so this narrows (and speeds up) all later icon searches. Only taken from a
    /// confirmed answer — a calibration from a wrong guess would skew every search after it.
    /// </summary>
    public void Calibrate(Frame portrait, OperatorInfo op)
    {
        var (_, size, x, y) = BestPlacement(op.Id, Patch.From(portrait), calibration: null);
        library.SaveCalibration(new IconCalibration(size, x, y));
    }

    /// <summary>
    /// Where (size and offset) the icon correlates best with the portrait. Uncalibrated: a coarse
    /// pass over the whole range, then a fine pass around the winner. Calibrated: a fine pass
    /// around the calibrated placement only.
    /// </summary>
    private (double Score, int Size, int X, int Y) BestPlacement(string id, Patch image, IconCalibration? calibration)
    {
        var best = (Score: -1.0, Size: 0, X: 0, Y: 0);
        if (calibration is null)
        {
            for (int s = 0; s < IconSizes.Length; s += 2)
                Search(IconSizes[s], step: 2, centre: null);
            calibration = new IconCalibration(best.Size, best.X, best.Y);
        }

        for (int size = calibration.Size - CalibratedSizeRadius; size <= calibration.Size + CalibratedSizeRadius; size++)
            Search(size, step: 1, centre: (calibration.X, calibration.Y));
        return best;

        void Search(int size, int step, (int X, int Y)? centre)
        {
            if (size < 16) return;
            var template = IconTemplate(id, size);
            int overhang = (int)(size * MaxOverhang);
            int minX = -overhang, minY = -overhang;
            int maxX = PortraitWidth - size + overhang, maxY = PortraitHeight - size + overhang;
            if (centre is { } c)
            {
                minX = Math.Max(minX, c.X - CalibratedOffsetRadius);
                maxX = Math.Min(maxX, c.X + CalibratedOffsetRadius);
                minY = Math.Max(minY, c.Y - CalibratedOffsetRadius);
                maxY = Math.Min(maxY, c.Y + CalibratedOffsetRadius);
            }
            for (int y = minY; y <= maxY; y += step)
            for (int x = minX; x <= maxX; x += step)
            {
                double score = template.Correlate(image, x, y);
                if (score > best.Score) best = (score, size, x, y);
            }
        }
    }

    /// <summary>
    /// Colour similarity over only the pixels where the placed icon and the portrait's face area
    /// overlap, so parts of the icon the game doesn't show (weapons, clothes) don't count.
    /// </summary>
    private static double OverlapColourSimilarity(Frame portrait, Frame icon, int ox, int oy)
    {
        var mask = PortraitMask.Value;
        bool Overlaps(int ix, int iy)
        {
            int px = ix + ox, py = iy + oy;
            return (uint)px < (uint)portrait.Width && (uint)py < (uint)portrait.Height
                   && mask[py * portrait.Width + px] && icon.GetAlpha(ix, iy) > 200;
        }

        var iconColours = ColourHistogram.Of(icon, Overlaps);
        var portraitColours = ColourHistogram.Of(portrait, (px, py) =>
        {
            int ix = px - ox, iy = py - oy;
            return (uint)ix < (uint)icon.Width && (uint)iy < (uint)icon.Height && Overlaps(ix, iy);
        });
        return portraitColours.Similarity(iconColours);
    }

    private Template IconTemplate(string id, int size)
    {
        lock (_iconTemplates)
        {
            if (!_iconTemplates.TryGetValue((id, size), out var template))
                _iconTemplates[(id, size)] = template = Template.FromIcon(library.Icons[id].Resize(size, size));
            return template;
        }
    }

    /// <summary>
    /// The face area of a squad portrait: an ellipse around the head, leaving out the corners
    /// (game world) and the HUD drawn on top of the portrait.
    /// </summary>
    private static bool[] BuildPortraitMask()
    {
        var mask = new bool[PortraitWidth * PortraitHeight];
        // Centred on the eyes, which sit about 60% of the way down.
        double cx = PortraitWidth * 0.48, cy = PortraitHeight * 0.55;
        double rx = PortraitWidth * 0.40, ry = PortraitHeight * 0.38;
        for (int y = 0; y < PortraitHeight; y++)
        for (int x = 0; x < PortraitWidth; x++)
        {
            double nx = (x - cx) / rx, ny = (y - cy) / ry;
            mask[y * PortraitWidth + x] = nx * nx + ny * ny <= 1 && !IsHudOverlay(x, y);
        }
        return mask;
    }

    /// <summary>
    /// Portrait pixels covered by other HUD (positions from <see cref="PartyLayout.Portrait"/> at 4K):
    /// the food-buff icon, x ≥ 188 and y ≥ 1880, and the level text, y ≥ 1946.
    /// </summary>
    private static bool IsHudOverlay(int x, int y) =>
        (x >= (188 - 81) / 2 && y >= (1880 - 1826) / 2) || y >= (1946 - 1826) / 2;

    /// <summary>An image as float RGB, for correlation. HUD-covered pixels are flagged and never compared.</summary>
    private sealed class Patch(int width, int height, float[] rgb, bool[] excluded)
    {
        public int Width { get; } = width;
        public int Height { get; } = height;
        public float[] Rgb { get; } = rgb;
        public bool[] Excluded { get; } = excluded;

        public static Patch From(Frame frame)
        {
            var rgb = new float[frame.Width * frame.Height * 3];
            var excluded = new bool[frame.Width * frame.Height];
            for (int y = 0; y < frame.Height; y++)
            for (int x = 0; x < frame.Width; x++)
            {
                var (r, g, b) = frame.GetPixel(x, y);
                int i = (y * frame.Width + x) * 3;
                rgb[i] = r;
                rgb[i + 1] = g;
                rgb[i + 2] = b;
                excluded[y * frame.Width + x] = IsHudOverlay(x, y);
            }
            return new Patch(frame.Width, frame.Height, rgb, excluded);
        }
    }

    /// <summary>A reference image reduced to the masked pixels, zero-mean per channel, for fast correlation.</summary>
    private sealed class Template
    {
        private readonly int[] _xs, _ys;
        private readonly float[] _values; // (pixel, channel) interleaved, mean removed
        private readonly double _norm;

        private Template(int[] xs, int[] ys, float[] values, double norm)
        {
            _xs = xs;
            _ys = ys;
            _values = values;
            _norm = norm;
        }

        public static Template FromIcon(Frame icon)
        {
            // Compare a centred disc: the face. Skip transparent pixels.
            double c = (icon.Width - 1) / 2.0, radius = icon.Width * 0.45;
            return Build(icon, (x, y) =>
                (x - c) * (x - c) + (y - c) * (y - c) <= radius * radius && icon.GetAlpha(x, y) > 200);
        }

        public static Template FromPortrait(Frame portrait, bool[] mask) =>
            Build(portrait, (x, y) => mask[y * portrait.Width + x]);

        private static Template Build(Frame frame, Func<int, int, bool> include)
        {
            var xs = new List<int>();
            var ys = new List<int>();
            var values = new List<float>();
            double[] sum = new double[3];
            for (int y = 0; y < frame.Height; y++)
            for (int x = 0; x < frame.Width; x++)
            {
                if (!include(x, y)) continue;
                var (r, g, b) = frame.GetPixel(x, y);
                xs.Add(x);
                ys.Add(y);
                values.Add(r); values.Add(g); values.Add(b);
                sum[0] += r; sum[1] += g; sum[2] += b;
            }

            int n = xs.Count;
            var arr = values.ToArray();
            double norm = 0;
            for (int i = 0; i < arr.Length; i++)
            {
                arr[i] -= (float)(sum[i % 3] / n);
                norm += arr[i] * arr[i];
            }
            return new Template(xs.ToArray(), ys.ToArray(), arr, Math.Sqrt(norm));
        }

        /// <summary>Normalised cross-correlation with the image placed so template (0,0) lands on image (ox, oy).</summary>
        public double Correlate(Patch image, int ox, int oy)
        {
            double dot = 0, s0 = 0, s1 = 0, s2 = 0, q = 0;
            int n = 0;
            for (int k = 0; k < _xs.Length; k++)
            {
                int x = _xs[k] + ox, y = _ys[k] + oy;
                if ((uint)x >= (uint)image.Width || (uint)y >= (uint)image.Height) continue;
                if (image.Excluded[y * image.Width + x]) continue;
                int i = (y * image.Width + x) * 3;
                float r = image.Rgb[i], g = image.Rgb[i + 1], b = image.Rgb[i + 2];
                int t = k * 3;
                dot += _values[t] * r + _values[t + 1] * g + _values[t + 2] * b;
                s0 += r; s1 += g; s2 += b;
                q += r * r + g * g + b * b;
                n++;
            }
            if (n < _xs.Length * 0.6 || _norm == 0) return -1; // mostly off the image or under HUD

            // dot/_norm assumed every template pixel was compared; when some were skipped, the
            // template's mean over the compared subset isn't zero any more, so recompute exactly.
            if (n < _xs.Length) return CorrelateSubset(image, ox, oy);

            double variance = q - (s0 * s0 + s1 * s1 + s2 * s2) / n;
            return variance <= 1e-6 ? 0 : dot / (_norm * Math.Sqrt(variance));
        }

        private double CorrelateSubset(Patch image, int ox, int oy)
        {
            // Two passes over the pixels both images have: means, then centred dot product.
            double[] tSum = new double[3], iSum = new double[3];
            int n = 0;
            for (int k = 0; k < _xs.Length; k++)
            {
                if (!Usable(image, k, ox, oy, out int i)) continue;
                for (int c = 0; c < 3; c++)
                {
                    tSum[c] += _values[k * 3 + c];
                    iSum[c] += image.Rgb[i + c];
                }
                n++;
            }

            double dot = 0, tt = 0, ii = 0;
            for (int k = 0; k < _xs.Length; k++)
            {
                if (!Usable(image, k, ox, oy, out int i)) continue;
                for (int c = 0; c < 3; c++)
                {
                    double t = _values[k * 3 + c] - tSum[c] / n;
                    double v = image.Rgb[i + c] - iSum[c] / n;
                    dot += t * v;
                    tt += t * t;
                    ii += v * v;
                }
            }
            return tt <= 1e-6 || ii <= 1e-6 ? 0 : dot / Math.Sqrt(tt * ii);
        }

        private bool Usable(Patch image, int k, int ox, int oy, out int index)
        {
            int x = _xs[k] + ox, y = _ys[k] + oy;
            index = (y * image.Width + x) * 3;
            return (uint)x < (uint)image.Width && (uint)y < (uint)image.Height && !image.Excluded[y * image.Width + x];
        }
    }
}

/// <summary>How well one wiki icon matched, and where (for diagnostics and calibration).</summary>
/// <param name="Score">Combined: mean of <paramref name="Shape"/> and <paramref name="Colour"/>.</param>
/// <param name="Shape">Best normalised cross-correlation (layout of the face and hair).</param>
/// <param name="Colour">Similarity of the colour distributions (hair, eyes, skin), independent of pose.</param>
public sealed record IconScore(OperatorInfo Operator, double Score, double Shape, double Colour, int Size, int X, int Y);

/// <summary>
/// Hue histogram of the saturated pixels plus brightness bins for the grey ones. Compared with
/// the Bhattacharyya coefficient: 1 = identical distributions, 0 = no overlap.
/// </summary>
internal sealed class ColourHistogram
{
    private const int HueBins = 18;
    private const int GreyBins = 4;
    private readonly double[] _bins;

    private ColourHistogram(double[] bins) => _bins = bins;

    public static ColourHistogram Of(Frame frame, Func<int, int, bool> include)
    {
        // Saturated colours split into dark/bright halves: dark red hair vs bright red eyes.
        var bins = new double[HueBins * 2 + GreyBins];
        double total = 0;
        for (int y = 0; y < frame.Height; y++)
        for (int x = 0; x < frame.Width; x++)
        {
            if (!include(x, y)) continue;
            var (r, g, b) = frame.GetPixel(x, y);
            int max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            int bin;
            if (max < 40 || max - min < max * 0.25)
            {
                bin = HueBins * 2 + Math.Min(GreyBins - 1, max * GreyBins / 256);
            }
            else
            {
                double hue = Hue(r, g, b, max, min);
                int h = Math.Min(HueBins - 1, (int)(hue / 360 * HueBins));
                bin = h * 2 + (max >= 160 ? 1 : 0);
            }
            bins[bin]++;
            total++;
        }
        if (total > 0)
            for (int i = 0; i < bins.Length; i++) bins[i] /= total;
        return new ColourHistogram(bins);
    }

    public double Similarity(ColourHistogram other)
    {
        double bc = 0;
        for (int i = 0; i < _bins.Length; i++) bc += Math.Sqrt(_bins[i] * other._bins[i]);
        return bc;
    }

    private static double Hue(int r, int g, int b, int max, int min)
    {
        double d = max - min;
        double h = max == r ? (g - b) / d % 6 : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        h *= 60;
        return h < 0 ? h + 360 : h;
    }
}
