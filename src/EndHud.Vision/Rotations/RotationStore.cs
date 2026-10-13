using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace EndHud.Vision.Rotations;

/// <summary>Why a rotation file or share code couldn't be read.</summary>
public enum RotationReadError
{
    /// <summary>Not an EndHud rotation (not JSON, or JSON of something else).</summary>
    NotARotation,

    /// <summary>A share code that's cut short or altered.</summary>
    DamagedShareCode,

    /// <summary>Made by a newer EndHud (a format this build doesn't know).</summary>
    NewerFormat,
}

public sealed class RotationReadException(RotationReadError error, string? detail = null)
    : Exception($"{error}{(detail is null ? "" : ": " + detail)}")
{
    public RotationReadError Error { get; } = error;
}

/// <summary>Rotations as JSON, and as a share code to paste in a chat.</summary>
public static class RotationFormat
{
    /// <summary>Share codes start with this; the number is the code's own version.</summary>
    public const string ShareCodePrefix = "EHR1:";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // Keep Chinese names readable in the file.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All),
    };

    public static string ToJson(Rotation rotation) => JsonSerializer.Serialize(rotation, Json);

    private static readonly JsonSerializerOptions Compact = new(Json) { WriteIndented = false };

    public static Rotation FromJson(string json)
    {
        Rotation? rotation;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("format", out var format) || format.ValueKind != JsonValueKind.Number)
                throw new RotationReadException(RotationReadError.NotARotation);
            if (format.GetInt32() > Rotation.CurrentFormat)
                throw new RotationReadException(RotationReadError.NewerFormat, $"format {format.GetInt32()}");
            rotation = document.RootElement.Deserialize<Rotation>(Json);
        }
        catch (JsonException ex)
        {
            throw new RotationReadException(RotationReadError.NotARotation, ex.Message);
        }
        if (rotation is null || rotation.Id is null || rotation.Phases is null || rotation.Sequence is null || rotation.Squad is null)
            throw new RotationReadException(RotationReadError.NotARotation);
        return rotation;
    }

    /// <summary>"EHR1:" + the JSON, compressed, in URL-safe base64: short enough for a chat message.</summary>
    public static string ToShareCode(Rotation rotation)
    {
        using var buffer = new MemoryStream();
        using (var deflate = new DeflateStream(buffer, CompressionLevel.SmallestSize))
            deflate.Write(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(rotation, Compact)));
        return ShareCodePrefix + Convert.ToBase64String(buffer.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static Rotation FromShareCode(string code)
    {
        code = string.Concat(code.Where(c => !char.IsWhiteSpace(c))); // chats wrap long lines
        if (!code.StartsWith(ShareCodePrefix, StringComparison.Ordinal))
        {
            // A newer code ("EHR2:") is from a newer EndHud; anything else isn't a code at all.
            throw new RotationReadException(code.StartsWith("EHR", StringComparison.Ordinal) && code.Contains(':')
                ? RotationReadError.NewerFormat
                : RotationReadError.NotARotation);
        }

        string body = code[ShareCodePrefix.Length..].Replace('-', '+').Replace('_', '/');
        body = body.PadRight(body.Length + (4 - body.Length % 4) % 4, '=');
        string json;
        try
        {
            using var deflate = new DeflateStream(new MemoryStream(Convert.FromBase64String(body)), CompressionMode.Decompress);
            using var reader = new StreamReader(deflate, Encoding.UTF8);
            json = reader.ReadToEnd();
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException)
        {
            throw new RotationReadException(RotationReadError.DamagedShareCode, ex.Message);
        }

        try
        {
            return FromJson(json);
        }
        catch (RotationReadException ex) when (ex.Error == RotationReadError.NotARotation)
        {
            throw new RotationReadException(RotationReadError.DamagedShareCode, ex.Message);
        }
    }
}

/// <summary>A rotation file that couldn't be read, kept so the editor can say so (never dropped silently).</summary>
public sealed record UnreadableRotation(string File, RotationReadError Error);

/// <summary>
/// The player's rotations: one JSON file each in a folder (%APPDATA%\EndHud\rotations in the app).
/// <list type="bullet">
/// <item>Create / correct: <see cref="Save"/> writes the file whole (temp file, then rename).</item>
/// <item>Remove: <see cref="Delete"/>, only when the player asks; nothing is removed automatically.</item>
/// <item>Import never overwrites: <see cref="Import"/> reports a clash and the player chooses
/// (<see cref="Save"/> to replace, <see cref="AsCopy"/> to keep both).</item>
/// <item>Survives updates (the installer and updater never touch it); moves between computers by
/// export and import.</item>
/// </list>
/// </summary>
public sealed class RotationStore(string folder)
{
    public string Folder => folder;

    private string FileOf(string id) => Path.Combine(folder, id + ".json");

    /// <summary>Every rotation, and every file that couldn't be read (with why).</summary>
    public (IReadOnlyList<Rotation> Rotations, IReadOnlyList<UnreadableRotation> Unreadable) Load()
    {
        var rotations = new List<Rotation>();
        var unreadable = new List<UnreadableRotation>();
        if (!Directory.Exists(folder)) return (rotations, unreadable);
        foreach (string file in Directory.GetFiles(folder, "*.json").Order(StringComparer.Ordinal))
        {
            try
            {
                rotations.Add(RotationFormat.FromJson(File.ReadAllText(file)));
            }
            catch (RotationReadException ex)
            {
                unreadable.Add(new UnreadableRotation(file, ex.Error));
            }
            catch (IOException)
            {
                unreadable.Add(new UnreadableRotation(file, RotationReadError.NotARotation));
            }
        }
        return (rotations.OrderBy(r => r.Name, StringComparer.Ordinal).ToList(), unreadable);
    }

    /// <summary>Write (or rewrite) a rotation's file.</summary>
    public void Save(Rotation rotation)
    {
        Directory.CreateDirectory(folder);
        string temp = FileOf(rotation.Id) + ".tmp";
        File.WriteAllText(temp, RotationFormat.ToJson(rotation));
        File.Move(temp, FileOf(rotation.Id), overwrite: true);
    }

    public bool Delete(string id)
    {
        if (!File.Exists(FileOf(id))) return false;
        File.Delete(FileOf(id));
        return true;
    }

    public bool Exists(string id) => File.Exists(FileOf(id));

    /// <summary>Save an imported rotation, unless one with its id exists: then returns false and saves nothing.</summary>
    public bool Import(Rotation rotation)
    {
        if (Exists(rotation.Id)) return false;
        Save(rotation);
        return true;
    }

    /// <summary>The same rotation under a new id (and the given name), to keep both.</summary>
    public static Rotation AsCopy(Rotation rotation, string name) =>
        rotation with { Id = NewId(), Name = name };

    public static string NewId() => Guid.NewGuid().ToString("N");
}
