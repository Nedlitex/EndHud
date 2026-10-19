namespace EndHud.Vision.Inn;

/// <summary>
/// The dish and drink pictures (<c>inn/pictures/&lt;id&gt;.png</c> in the operator package, 128×128
/// with transparency), which the table cards draw at a smaller size to show what a guest ordered.
/// For matching, each is drawn as the card would draw it: shrunk to the art's size on screen and
/// laid on the card's grey disc (<see cref="Rendered"/>, kept per size).
/// </summary>
public sealed class InnPictures
{
    private readonly IReadOnlyDictionary<string, Frame> _pictures;
    private readonly Dictionary<(int Size, int Background), Rendering[]> _rendered = [];
    private readonly object _lock = new();

    /// <param name="pictures">Each dish's or drink's picture by its id, as decoded by <see cref="ImageFile"/> (premultiplied alpha).</param>
    public InnPictures(IReadOnlyDictionary<string, Frame> pictures) => _pictures = pictures;

    /// <summary>The pictures of every dish and drink in <paramref name="data"/> that has one in the reference folder (package).</summary>
    public static async Task<InnPictures> LoadAsync(string referenceFolder, InnData data)
    {
        var pictures = new Dictionary<string, Frame>();
        foreach (string id in data.Dishes.Select(d => d.Id).Concat(data.Drinks.Select(d => d.Id)))
        {
            string path = InnData.PicturePath(referenceFolder, id);
            if (File.Exists(path)) pictures[id] = await ImageFile.LoadAsync(path);
        }
        return new InnPictures(pictures);
    }

    public int Count => _pictures.Count;

    /// <summary>A picture drawn <paramref name="size"/> pixels square over a background of one colour: RGB, row by row.</summary>
    internal sealed record Rendering(string Id, int Size, byte[] Rgb);

    /// <summary>Every picture at <paramref name="size"/> over <paramref name="background"/> (packed 0xRRGGBB).</summary>
    internal Rendering[] Rendered(int size, int background)
    {
        lock (_lock)
        {
            if (_rendered.TryGetValue((size, background), out var cached)) return cached;
            var rendered = _pictures.Select(p => Render(p.Key, p.Value, size, background)).ToArray();
            // A frame's discs share one size and colour; keep the cache from growing with odd sizes.
            if (_rendered.Count > 16) _rendered.Clear();
            _rendered[(size, background)] = rendered;
            return rendered;
        }
    }

    private static Rendering Render(string id, Frame picture, int size, int background)
    {
        // Bilinear while shrinking little (4K: 128 to 103); averaging when shrinking more (1080p: to 52).
        var small = size * 2 > picture.Width ? picture.ResizeSmooth(size, size) : picture.Resize(size, size);
        int bgR = (background >> 16) & 0xFF, bgG = (background >> 8) & 0xFF, bgB = background & 0xFF;
        var rgb = new byte[size * size * 3];
        for (int i = 0; i < size * size; i++)
        {
            int o = i * 4;
            // Premultiplied: colour + background × (1 − alpha).
            double rest = 1 - small.Bgra[o + 3] / 255.0;
            rgb[i * 3] = (byte)Math.Min(255, Math.Round(small.Bgra[o + 2] + bgR * rest));
            rgb[i * 3 + 1] = (byte)Math.Min(255, Math.Round(small.Bgra[o + 1] + bgG * rest));
            rgb[i * 3 + 2] = (byte)Math.Min(255, Math.Round(small.Bgra[o] + bgB * rest));
        }
        return new Rendering(id, size, rgb);
    }
}
