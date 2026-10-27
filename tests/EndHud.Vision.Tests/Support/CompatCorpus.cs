using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EndHud.Vision.Tests;

/// <summary>
/// The frozen backward-compatibility corpus (tests/EndHud.Vision.Tests/Compat, see its README.md): rotation and alert
/// files of every format, as old EndHud versions wrote them, each with what loading it today writes back. Read from the
/// source folder, never from a copy beside the binaries: a file deleted from the folder must be missing here too.
/// Shared by both test assemblies (EndHud.Tests links this file, for the editors' export-then-import round trips).
/// </summary>
internal static partial class CompatCorpus
{
    public const string RotationPrefix = "rotation-format-";
    public const string AlertPrefix = "alerts-format-";
    public const string ExpectedSuffix = ".expected.json";
    public const string ManifestName = "MANIFEST.sha256";

    /// <summary>The corpus folder in the repository.</summary>
    public static string Folder { get; } = Path.Combine(RepositoryRoot(), "tests", "EndHud.Vision.Tests", "Compat");

    /// <summary>The folder as the repository names it, for messages.</summary>
    public const string FolderInRepository = "tests/EndHud.Vision.Tests/Compat";

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "EndHud.sln"))) return dir.FullName;
        throw new DirectoryNotFoundException("EndHud.sln not found above " + AppContext.BaseDirectory);
    }

    /// <summary>"rotation-format-07.json", "rotation-format-02-laevatain.json": the format, then an optional name.</summary>
    [GeneratedRegex(@"^(rotation|alerts)-format-(\d{2})(-[a-z0-9-]+)?\.json$")]
    private static partial Regex InputName();

    /// <summary>The rotation files (inputs, not their expected outputs), by name.</summary>
    public static IReadOnlyList<string> RotationFiles() => Inputs(RotationPrefix);

    /// <summary>The alert files (inputs, not their expected outputs), by name.</summary>
    public static IReadOnlyList<string> AlertFiles() => Inputs(AlertPrefix);

    private static IReadOnlyList<string> Inputs(string prefix) =>
        Directory.GetFiles(Folder, prefix + "*.json").Select(Path.GetFileName).OfType<string>()
            .Where(name => !name.EndsWith(ExpectedSuffix, StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToList();

    /// <summary>The format its name says ("rotation-format-07.json": 7); null for a name not in the corpus's pattern.</summary>
    public static int? FormatInName(string name) =>
        InputName().Match(name) is { Success: true } match ? int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture) : null;

    /// <summary>The format the file itself is stamped with (its "format" field).</summary>
    public static int FormatInFile(string name)
    {
        using var document = JsonDocument.Parse(Text(name));
        return document.RootElement.GetProperty("format").GetInt32();
    }

    /// <summary>A corpus file's text, line ends as the repository keeps them (LF), whatever the checkout made of them.</summary>
    public static string Text(string name) => File.ReadAllText(Path.Combine(Folder, name)).ReplaceLineEndings("\n");

    /// <summary>The expected output's file name for an input.</summary>
    public static string ExpectedOf(string name) => name[..^".json".Length] + ExpectedSuffix;

    /// <summary>
    /// SHA-256 of a file's bytes with CRLF taken as LF: git keeps them LF and a Windows checkout may make them CRLF, which
    /// is the same file. Any other change, a BOM included, is a different hash.
    /// </summary>
    public static string Hash(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        var lf = new List<byte>(bytes.Length);
        for (int i = 0; i < bytes.Length; i++)
            if (!(bytes[i] == (byte)'\r' && i + 1 < bytes.Length && bytes[i + 1] == (byte)'\n')) lf.Add(bytes[i]);
        return Convert.ToHexString(SHA256.HashData(lf.ToArray())).ToLowerInvariant();
    }

    /// <summary>The frozen files' hashes as committed (MANIFEST.sha256: "hash  name" per line), by name.</summary>
    public static IReadOnlyDictionary<string, string> Manifest() =>
        File.ReadAllLines(Path.Combine(Folder, ManifestName)).Select(line => line.Trim()).Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Select(line => line.Split("  ", 2)).ToDictionary(parts => parts[1], parts => parts[0], StringComparer.Ordinal);

    /// <summary>Every frozen file: the JSON files (inputs and expected outputs).</summary>
    public static IReadOnlyList<string> FrozenFiles() =>
        Directory.GetFiles(Folder, "*.json").Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal).ToList();
}
