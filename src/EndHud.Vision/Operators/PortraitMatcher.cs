using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;

namespace EndHud.Vision.Operators;

/// <summary>
/// Compares a squad portrait (see <see cref="PartyLayout.Portrait"/>) with portraits the user
/// taught (<see cref="MatchLearned"/>) and with every operator's references (<see cref="Differences"/>):
/// the wiki avatar, and EndHud's built-in in-game portrait (<see cref="PortraitReferences"/>) when
/// there is one.
/// </summary>
/// <remarks>
/// Learned portraits are the same rendering as the squad panel, so pixels are compared:
/// normalised cross-correlation over RGB of the middle of the image (the edges show the game
/// world behind the operator, which changes). The references are compared by
/// <see cref="CharacterModel"/>: the wiki avatar is different art (a drawing, not the game's 3D
/// model), which pixels can't match.
/// </remarks>
public sealed class PortraitMatcher(OperatorLibrary library)
{
    /// <summary>Size every squad portrait is resampled to before matching or learning.</summary>
    public const int PortraitWidth = 64;
    public const int PortraitHeight = 80;

    // Learned portraits are the same rendering as what we're matching, so they score high.
    internal const double LearnedThreshold = 0.80;
    private const double LearnedMargin = 0.04;
    private const int LearnedSearchRadius = 3;

    private static readonly Lazy<bool[]> PortraitMask = new(BuildPortraitMask);
    private readonly Dictionary<int, Task<float[][]>> _wikiFeatures = new();
    // Avatars already described in this process (by avatar content and width): a library reload
    // after a roster refresh only describes what changed.
    private static readonly ConcurrentDictionary<(string Avatar, int Width), float[]> Described = new();

    private readonly IReadOnlyList<PortraitReference> _references = PortraitReferences.BuiltIn;

    /// <summary>With other in-game portrait references than the built-in ones (tests: leave one out, or none).</summary>
    internal PortraitMatcher(OperatorLibrary library, IReadOnlyList<PortraitReference> references) : this(library) =>
        _references = references;

    public OperatorLibrary Library => library;

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

    /// <summary>
    /// How different <paramref name="screenPortrait"/> (the portrait at the screen's own size,
    /// <see cref="PartySlot.ScreenPortrait"/>) is from each operator, in
    /// <see cref="OperatorLibrary.Operators"/> order; 0 = the same character. Per operator, the
    /// closer of its wiki avatar and its in-game portrait references (at the nearest size). While
    /// the avatars for this portrait size are still being prepared (<see cref="PrepareWikiAvatars"/>,
    /// up to a minute on a first start), the in-game references alone, so the squad is named right
    /// away; an operator without one counts as <see cref="Unknown"/> until then. Null when neither
    /// is there yet.
    /// </summary>
    public float[]? Differences(Frame screenPortrait)
    {
        var prepared = PrepareWikiAvatars(screenPortrait.Width);
        float[][] avatarFeatures = prepared.IsCompletedSuccessfully ? prepared.Result : [];

        int width = PortraitReferences.NearestWidth(screenPortrait.Width);
        var index = new Dictionary<string, int>();
        for (int j = 0; j < library.Operators.Count; j++) index[library.Operators[j].Id] = j;
        var references = _references.Where(r => r.PortraitWidth == width && index.ContainsKey(r.OperatorId)).ToList();
        if (avatarFeatures.Length == 0 && references.Count == 0) return null;

        var model = CharacterModel.Shared;
        var differences = model.Differences([model.Features(screenPortrait)],
            [.. avatarFeatures, .. references.Select(r => r.Features)]);
        var row = new float[library.Operators.Count];
        for (int j = 0; j < row.Length; j++) row[j] = j < avatarFeatures.Length ? differences[0, j] : Unknown;
        for (int k = 0; k < references.Count; k++)
        {
            int j = index[references[k].OperatorId];
            row[j] = Math.Min(row[j], differences[0, avatarFeatures.Length + k]);
        }
        return row;
    }

    /// <summary>The difference given to an operator with nothing to compare yet: far from any match.</summary>
    public const float Unknown = 2f;

    /// <summary>
    /// Describe every wiki avatar for portraits <paramref name="width"/> pixels wide, in the
    /// background (about 0.2 s per operator; then cached in the user folder, so only once per
    /// screen size and roster). Each avatar is first shrunk to that width: a small portrait is
    /// blurry, and comparing it with an equally blurry avatar is fairer than with a sharp one
    /// (at 1600x900, 47 of 53 portraits right instead of 36).
    /// </summary>
    public Task<float[][]> PrepareWikiAvatars(int width)
    {
        lock (_wikiFeatures)
        {
            if (!_wikiFeatures.TryGetValue(width, out var task) || task.IsFaulted)
                _wikiFeatures[width] = task = Task.Run(() => DescribeWikiAvatars(width));
            return task;
        }
    }

    private float[][] DescribeWikiAvatars(int width)
    {
        var cacheFile = Path.Combine(library.UserFolder, "features", $"{CharacterModel.Name}-{width}.json");
        var cache = ReadFeatureCache(cacheFile);
        var model = CharacterModel.Shared;
        bool changed = false;
        var result = new float[library.Operators.Count][];
        for (int i = 0; i < result.Length; i++)
        {
            var op = library.Operators[i];
            var avatar = OnWhite(library.Icons[op.Id]);
            string key = Convert.ToHexString(SHA1.HashData(avatar.Bgra));
            if (cache.TryGetValue(op.Id, out var cached) && cached.Avatar == key && cached.Features.Length > 0)
            {
                result[i] = Described[(key, width)] = cached.Features;
                continue;
            }
            result[i] = Described.GetOrAdd((key, width), _ => model.Features(avatar.Resize(width, width)));
            cache[op.Id] = new CachedFeatures(key, result[i]);
            changed = true;
        }
        if (changed) WriteFeatureCache(cacheFile, cache);
        return result;
    }

    private sealed record CachedFeatures(string Avatar, float[] Features);

    private static Dictionary<string, CachedFeatures> ReadFeatureCache(string file)
    {
        try
        {
            return File.Exists(file)
                ? JsonSerializer.Deserialize<Dictionary<string, CachedFeatures>>(File.ReadAllText(file)) ?? []
                : [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return []; // a damaged cache is just recomputed
        }
    }

    private static void WriteFeatureCache(string file, Dictionary<string, CachedFeatures> cache)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            string temp = file + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(cache));
            File.Move(temp, file, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not fatal: it's recomputed next time.
        }
    }

    /// <summary>The avatar over white (the model's convention for transparency), fully opaque.</summary>
    private static Frame OnWhite(Frame image)
    {
        var bgra = new byte[image.Bgra.Length];
        for (int i = 0; i < bgra.Length; i += 4)
        {
            int a = image.Bgra[i + 3];
            for (int c = 0; c < 3; c++) bgra[i + c] = (byte)((image.Bgra[i + c] * a + 255 * (255 - a) + 127) / 255);
            bgra[i + 3] = 255;
        }
        return new Frame(image.Width, image.Height, bgra);
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

    /// <summary>A learned portrait reduced to the masked pixels, zero-mean per channel, for fast correlation.</summary>
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
