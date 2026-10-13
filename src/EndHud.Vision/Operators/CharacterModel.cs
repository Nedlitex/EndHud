using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace EndHud.Vision.Operators;

/// <summary>
/// The character recognition model, CCIP (see tools/ccip-model): describes the character in an
/// image as 768 numbers, and says how different two descriptions are (0 = the same character),
/// across different art of the same character: the game's 3D portrait and the wiki's drawing.
/// </summary>
/// <remarks>
/// About 0.2 s per image on the CPU, using at most <see cref="Threads"/> threads so the game keeps
/// its CPU. Thread-safe.
/// </remarks>
internal sealed class CharacterModel
{
    public const int InputSize = 384;
    private const int Threads = 2;

    /// <summary>Names this model in caches, so features from another model are never mixed in.</summary>
    public const string Name = "ccip-caformer-24-randaug-pruned-int8";

    public static CharacterModel Shared => Instance.Value;
    private static readonly Lazy<CharacterModel> Instance = new(() => new CharacterModel());

    private readonly InferenceSession _features;
    private readonly InferenceSession _metrics;

    private CharacterModel()
    {
        var options = new SessionOptions
        {
            IntraOpNumThreads = Threads,
            InterOpNumThreads = 1,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
        };
        _features = new InferenceSession(Resource("ccip-features-int8.onnx"), options);
        _metrics = new InferenceSession(Resource("ccip-metrics.onnx"), options);
    }

    private static byte[] Resource(string name)
    {
        using var stream = typeof(CharacterModel).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"The character model ({name}) is missing from EndHud.Vision.");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    /// <summary>Describe the character in <paramref name="image"/> (transparent parts count as white).</summary>
    public float[] Features(Frame image)
    {
        var input = new DenseTensor<float>([1, 3, InputSize, InputSize]);
        var resized = image.Width == InputSize && image.Height == InputSize ? image : image.ResizeSmooth(InputSize, InputSize);
        for (int y = 0; y < InputSize; y++)
        for (int x = 0; x < InputSize; x++)
        {
            var (r, g, b) = resized.GetPixel(x, y);
            int a = resized.GetAlpha(x, y);
            input[0, 0, y, x] = Scale(r, a);
            input[0, 1, y, x] = Scale(g, a);
            input[0, 2, y, x] = Scale(b, a);
        }

        using var output = _features.Run([NamedOnnxValue.CreateFromTensor("input", input)]);
        return output[0].AsEnumerable<float>().ToArray();

        // Over white, then to [-1, 1].
        static float Scale(byte value, int alpha) => (value * alpha + 255 * (255 - alpha)) / 255f / 127.5f - 1;
    }

    /// <summary>How different each of <paramref name="a"/> is from each of <paramref name="b"/>: [i, j], 0 = same character.</summary>
    public float[,] Differences(IReadOnlyList<float[]> a, IReadOnlyList<float[]> b)
    {
        int n = a.Count + b.Count, size = a.Count > 0 ? a[0].Length : b[0].Length;
        var input = new DenseTensor<float>([n, size]);
        for (int i = 0; i < n; i++)
        {
            var row = i < a.Count ? a[i] : b[i - a.Count];
            for (int k = 0; k < size; k++) input[i, k] = row[k];
        }

        using var output = _metrics.Run([NamedOnnxValue.CreateFromTensor("input", input)]);
        var all = output[0].AsTensor<float>();
        var result = new float[a.Count, b.Count];
        for (int i = 0; i < a.Count; i++)
        for (int j = 0; j < b.Count; j++)
            result[i, j] = all[i, a.Count + j];
        return result;
    }
}
