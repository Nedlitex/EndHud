using System.Text.Json;

namespace EndHud.Vision.Operators;

/// <summary>One operator's in-game squad portrait, as the character model describes it.</summary>
/// <param name="Source">Where the portrait came from, e.g. "squad-5.png slot 4".</param>
/// <param name="Width">The portrait's width in pixels when described (the screen size it stands for).</param>
public sealed record PortraitReference(string OperatorId, string Source, int Width, float[] Features);

/// <summary>
/// EndHud's built-in portrait references (Assets/operator-portraits.json): each operator's squad
/// portrait as the game draws it, described by the character model at the common portrait sizes.
/// The portrait itself isn't shipped, only those numbers. Comparing a portrait with the same art
/// is far more reliable than with the wiki's drawing; an operator without a reference (a new
/// one) is recognised by the wiki avatar alone. Built from the test fixtures' squad screenshots
/// (tests/EndHud.Vision.Tests/Fixtures/squads) by <see cref="Build"/>.
/// </summary>
public static class PortraitReferences
{
    public const string ResourceName = "operator-portraits.json";

    /// <summary>
    /// Portrait widths references are described at: the squad portrait on 4K, 2880x1800,
    /// 1440p, 2256x1504, 1080p, 1600x900, 1366x768 and 720p screens (128 px at 4K, scaled).
    /// </summary>
    public static readonly int[] Widths = [128, 96, 85, 75, 64, 53, 46, 43];

    private sealed record Stored(string Model, List<StoredReference> References);
    private sealed record StoredReference(string Operator, string Source, int Width, string Features);

    /// <summary>The built-in references; empty if they were made with another model.</summary>
    public static IReadOnlyList<PortraitReference> BuiltIn => Loaded.Value;

    private static readonly Lazy<IReadOnlyList<PortraitReference>> Loaded = new(() =>
    {
        using var stream = typeof(PortraitReferences).Assembly.GetManifestResourceStream(ResourceName);
        return stream is null ? [] : Read(stream);
    });

    internal static IReadOnlyList<PortraitReference> Read(Stream json)
    {
        var stored = JsonSerializer.Deserialize<Stored>(json);
        if (stored is null || stored.Model != CharacterModel.Name) return [];
        return stored.References
            .Select(r => new PortraitReference(r.Operator, r.Source, r.Width, FromBase64(r.Features)))
            .ToList();
    }

    /// <summary>
    /// Describe labelled 4K squad portraits (<see cref="PartySlot.ScreenPortrait"/> of a 4K frame)
    /// at every one of <see cref="Widths"/>, shrunk as a smaller screen would draw them, and write
    /// them to <paramref name="file"/> in the built-in format.
    /// </summary>
    public static int Build(IEnumerable<(string OperatorId, string Source, Frame Portrait4K)> portraits, string file)
    {
        var model = CharacterModel.Shared;
        var references = new List<StoredReference>();
        foreach (var (id, source, portrait) in portraits)
        {
            foreach (int width in Widths)
            {
                int height = (int)Math.Round(portrait.Height * width / (double)portrait.Width);
                var shrunk = width == portrait.Width ? portrait : portrait.Resize(width, height);
                references.Add(new StoredReference(id, source, width, ToBase64(model.Features(shrunk))));
            }
        }
        File.WriteAllText(file, JsonSerializer.Serialize(new Stored(CharacterModel.Name, references),
            new JsonSerializerOptions { WriteIndented = true }));
        return references.Count;
    }

    /// <summary>The width in <see cref="Widths"/> closest to <paramref name="width"/>.</summary>
    public static int NearestWidth(int width) => Widths.MinBy(w => Math.Abs(w - width));

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
