using System.Text.Json;

namespace EndHud.Vision.Operators;

/// <summary>One operator's in-game squad portrait, as the character model describes it.</summary>
/// <param name="PortraitWidth">How wide the portrait was, in pixels, when described: the squad
/// portrait's width on the screen size this reference stands for (see <see cref="PortraitReferences.Sizes"/>).</param>
public sealed record PortraitReference(string OperatorId, int PortraitWidth, float[] Features);

/// <summary>
/// EndHud's built-in portrait references (Assets/operator-portraits.json): each operator's squad
/// portrait as the game draws it, described by the character model at the common portrait sizes.
/// The portrait itself isn't shipped, only those numbers. Comparing a portrait with the same art
/// is far more reliable than with the wiki's drawing; an operator without a reference (a new
/// one) is recognised by the wiki avatar alone. Built from the test fixtures' squad screenshots
/// (tests/data/Fixtures/squads) by <see cref="Build"/>.
/// </summary>
public static class PortraitReferences
{
    public const string ResourceName = "operator-portraits.json";

    /// <summary>
    /// The screen sizes references are made for, and the squad portrait's width in pixels on each
    /// (128 px at 4K, scaled with the screen). A smaller screen draws a blurrier portrait, which is
    /// compared best with a reference just as small, so a player's portrait is compared with the
    /// references of the size closest to its own (<see cref="NearestWidth"/>).
    /// </summary>
    public static readonly (string Screen, int PortraitWidth)[] Sizes =
    [
        ("3840x2160", 128), ("2880x1800", 96), ("2560x1440", 85), ("2256x1504", 75),
        ("1920x1080", 64), ("1600x900", 53), ("1366x768", 46), ("1280x720", 43),
    ];

    private const string About =
        "Built-in operator portrait references for EndHud (src/EndHud.Vision/Operators/PortraitReferences.cs). " +
        "For each screen size, each operator's squad portrait as the game draws it, described by the character " +
        "model named in Model: base64 of 768 little-endian float32 values per portrait (some operators have two). " +
        "No image is stored. Made from tests/data/Fixtures/squads by `endhud-cli build-portrait-references`.";

    // File layout: { About, Model, Sizes: [ { Screen, PortraitWidth, Operators: { id: [features, ...] } } ] }
    private sealed record Stored(string About, string Model, List<StoredSize> Sizes);
    private sealed record StoredSize(string Screen, int PortraitWidth, SortedDictionary<string, List<string>> Operators);

    /// <summary>The built-in references; empty if they were made with another model.</summary>
    public static IReadOnlyList<PortraitReference> BuiltIn => Loaded.Value;

    private static readonly Lazy<IReadOnlyList<PortraitReference>> Loaded = new(() =>
    {
        using var stream = typeof(PortraitReferences).Assembly.GetManifestResourceStream(ResourceName);
        return stream is null ? [] : Read(stream);
    });

    internal static IReadOnlyList<PortraitReference> Read(Stream json)
    {
        // Unreadable, another layout or another model: no built-in references (the wiki avatars
        // still recognise everyone), never an error that stops recognition.
        try
        {
            var stored = JsonSerializer.Deserialize<Stored>(json);
            if (stored?.Sizes is null || stored.Model != CharacterModel.Name) return [];
            return stored.Sizes
                .Where(size => size.Operators is not null)
                .SelectMany(size => size.Operators.SelectMany(op =>
                    (op.Value ?? []).Select(features => new PortraitReference(op.Key, size.PortraitWidth, FromBase64(features)))))
                .ToList();
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            return [];
        }
    }

    /// <summary>
    /// Describe a labelled 4K squad portrait (<see cref="PartySlot.ScreenPortrait"/> of a 4K frame)
    /// at each of <paramref name="widths"/> (default: every one of <see cref="Sizes"/>), shrunk as a
    /// smaller screen would draw it.
    /// </summary>
    public static IEnumerable<PortraitReference> Describe(string operatorId, Frame portrait4K, IEnumerable<int>? widths = null)
    {
        var model = CharacterModel.Shared;
        foreach (int width in widths ?? Sizes.Select(s => s.PortraitWidth))
        {
            int height = (int)Math.Round(portrait4K.Height * width / (double)portrait4K.Width);
            var shrunk = width == portrait4K.Width ? portrait4K : portrait4K.Resize(width, height);
            yield return new PortraitReference(operatorId, width, model.Features(shrunk));
        }
    }

    /// <summary>Describe labelled 4K squad portraits (see <see cref="Describe"/>) and write them to <paramref name="file"/> in the built-in format.</summary>
    public static int Build(IEnumerable<(string OperatorId, Frame Portrait4K)> portraits, string file)
    {
        var references = portraits.SelectMany(p => Describe(p.OperatorId, p.Portrait4K)).ToList();
        var sizes = Sizes.Select(size => new StoredSize(size.Screen, size.PortraitWidth, new SortedDictionary<string, List<string>>(
            references.Where(r => r.PortraitWidth == size.PortraitWidth)
                .GroupBy(r => r.OperatorId)
                .ToDictionary(g => g.Key, g => g.Select(r => ToBase64(r.Features)).ToList()),
            StringComparer.Ordinal))).ToList();
        File.WriteAllText(file, JsonSerializer.Serialize(new Stored(About, CharacterModel.Name, sizes),
            new JsonSerializerOptions { WriteIndented = true }));
        return references.Count;
    }

    /// <summary>The portrait width in <see cref="Sizes"/> closest to <paramref name="width"/>.</summary>
    public static int NearestWidth(int width) => Sizes.Select(s => s.PortraitWidth).MinBy(w => Math.Abs(w - width));

    private static string ToBase64(float[] values)
    {
        var bytes = new byte[values.Length * 4];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return Convert.ToBase64String(bytes);
    }

    private static float[] FromBase64(string text)
    {
        var bytes = Convert.FromBase64String(text);
        var values = new float[bytes.Length / 4];
        Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
        return values;
    }
}
