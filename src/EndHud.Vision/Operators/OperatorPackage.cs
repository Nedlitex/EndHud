using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

using InnData = EndHud.Vision.Inn.InnData;

namespace EndHud.Vision.Operators;

/// <summary>What a package says about itself (<c>package.json</c> inside it).</summary>
/// <param name="ContentAtUtc">When its content last changed: of two packages, the later one is newer.</param>
/// <param name="Operators">Operators in its roster.</param>
public sealed record OperatorPackageInfo(DateTime ContentAtUtc, int Operators);

/// <summary>
/// The operator library as one zip (roster, avatars, skill icons, statuses: the reference folder),
/// kept in the repository's <c>operators/</c> folder and built into EndHud, so a first start needs
/// no network. Newer copies come from the repository like recommended rotations
/// (<see cref="OperatorPackageSync"/>). The maintainer tool builds it from the wiki
/// (<c>endhud-cli build-operator-package</c>), and a weekly workflow keeps it current.
/// <list type="bullet">
/// <item>The same content always gives the same bytes (entries sorted, fixed timestamps), so an
/// unchanged library makes no commit.</item>
/// <item>Installing never touches the player's learned portraits (they live beside the reference
/// folder), and is refused, keeping the current copy, for a zip that isn't a package, has a path
/// outside the folder, or lists far fewer operators than the current copy.</item>
/// <item>It also carries the inn's data (<see cref="InnData"/>, <c>inn/inn-data.json</c>), kept by
/// hand in the repository's <c>inn/</c> folder and added when the package is built. A package whose
/// inn data can't be read, or that drops it or most of its dishes, is refused like a short roster.
/// One in a newer format than this build reads is installed (the operators still update) and its
/// inn data left unread.</item>
/// </list>
/// </summary>
public static class OperatorPackage
{
    public const string FileName = "operator-library.zip";
    public const string InfoName = "package.json";

    private static readonly DateTimeOffset EntryTime = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    /// <summary>
    /// Zip <paramref name="referenceFolder"/> (a downloaded reference library). The content date is
    /// <paramref name="previous"/>'s when the content is the same as in it (so the bytes are the same
    /// too), else <paramref name="nowUtc"/>.
    /// </summary>
    public static byte[] Build(string referenceFolder, DateTime nowUtc, byte[]? previous = null)
    {
        var files = Directory.GetFiles(referenceFolder, "*", SearchOption.AllDirectories)
            .Select(f => (Path: Path.GetRelativePath(referenceFolder, f).Replace('\\', '/'), Full: f))
            .Where(f => f.Path != InfoName)
            .OrderBy(f => f.Path, StringComparer.Ordinal)
            .ToList();
        var content = files.ToDictionary(f => f.Path, f => File.ReadAllBytes(f.Full));
        int operators = JsonSerializer.Deserialize<List<OperatorInfo>>(content[OperatorLibrary.ManifestName])?.Count ?? 0;

        DateTime at = nowUtc;
        if (previous is not null && SameContent(previous, content) && ReadInfo(previous) is { } before) at = before.ContentAtUtc;
        var info = JsonSerializer.SerializeToUtf8Bytes(new OperatorPackageInfo(at, operators), Json);

        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, InfoName, info);
            foreach (var (path, bytes) in content) Add(zip, path, bytes);
        }
        return buffer.ToArray();
    }

    private static void Add(ZipArchive zip, string path, byte[] bytes)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        entry.LastWriteTime = EntryTime;
        using var stream = entry.Open();
        stream.Write(bytes);
    }

    private static bool SameContent(byte[] zip, Dictionary<string, byte[]> content)
    {
        try
        {
            using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
            var entries = archive.Entries.Where(e => e.FullName != InfoName && !e.FullName.EndsWith('/')).ToList();
            if (entries.Count != content.Count) return false;
            foreach (var entry in entries)
            {
                if (!content.TryGetValue(entry.FullName, out var bytes)) return false;
                using var stream = entry.Open();
                using var copy = new MemoryStream();
                stream.CopyTo(copy);
                if (!copy.ToArray().AsSpan().SequenceEqual(bytes)) return false;
            }
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    /// <summary>The package's own description, or null if <paramref name="zip"/> isn't a package.</summary>
    public static OperatorPackageInfo? ReadInfo(byte[] zip)
    {
        try
        {
            using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
            if (archive.GetEntry(InfoName) is not { } entry) return null;
            using var stream = entry.Open();
            return JsonSerializer.Deserialize<OperatorPackageInfo>(stream, Json);
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException)
        {
            return null;
        }
    }

    /// <summary>The package installed in <paramref name="referenceFolder"/>, or null (none, or a wiki download from before packages).</summary>
    public static OperatorPackageInfo? Installed(string referenceFolder)
    {
        string path = Path.Combine(referenceFolder, InfoName);
        if (!OperatorReference.Exists(referenceFolder) || !File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<OperatorPackageInfo>(File.ReadAllBytes(path), Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The package built into EndHud, or null in a build without one.</summary>
    public static byte[]? BuiltIn()
    {
        using var stream = typeof(OperatorPackage).Assembly.GetManifestResourceStream(FileName);
        if (stream is null) return null;
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    /// <summary>
    /// Install <paramref name="zip"/> into <paramref name="referenceFolder"/> if it's newer than what's
    /// there (or nothing is). Returns the operators it added, or null when nothing changed (not
    /// newer). Throws <see cref="InvalidDataException"/>, keeping the current copy, when it can't be
    /// installed.
    /// </summary>
    public static IReadOnlyList<OperatorInfo>? InstallIfNewer(string referenceFolder, byte[] zip)
    {
        var info = ReadInfo(zip) ?? throw new InvalidDataException("Not an operator package (no package.json).");
        if (Installed(referenceFolder) is { } installed && installed.ContentAtUtc >= info.ContentAtUtc) return null;
        return Install(referenceFolder, zip);
    }

    /// <summary>Refuse a package whose inn data is broken, missing or much shorter than the current copy's.</summary>
    private static void CheckInnData(string current, string staging)
    {
        bool had = File.Exists(InnData.PathIn(current)), has = File.Exists(InnData.PathIn(staging));
        if (!has)
        {
            if (had) throw new InvalidDataException("The package has no inn data (the current copy has); keeping the current copy.");
            return;
        }
        if (InnData.Load(staging) is not { } incoming) return; // a newer format: left unread
        if (incoming.Problems() is { Count: > 0 } problems)
            throw new InvalidDataException($"The package's inn data has problems ({problems[0]}); keeping the current copy.");
        InnData? before = null;
        try { before = had ? InnData.Load(current) : null; } catch (InvalidDataException) { }
        if (before is not null && incoming.Dishes.Count < before.Dishes.Count * OperatorReference.MinShareOfCurrentRoster)
            throw new InvalidDataException($"The package lists {incoming.Dishes.Count} dishes (the current copy has {before.Dishes.Count}); keeping the current copy.");
    }

    /// <summary>The most files a package may hold (today's has about 230).</summary>
    public const int MaxFiles = 5000;

    /// <summary>The most a package may unpack to, all files together (today's: about 1.3 MB).</summary>
    public const long MaxUnpackedBytes = 256L * 1024 * 1024;

    /// <summary>Install <paramref name="zip"/>, replacing the reference folder; the operators it added.</summary>
    public static IReadOnlyList<OperatorInfo> Install(string referenceFolder, byte[] zip)
    {
        string folder = Path.GetFullPath(referenceFolder.TrimEnd(Path.DirectorySeparatorChar, '/'));
        string staging = folder + ".new";
        var current = OperatorReference.Exists(folder) ? OperatorReference.ReadRoster(folder) : [];
        if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        try
        {
            Directory.CreateDirectory(staging);
            using (var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read))
            {
                // Checked before anything is written (the sizes the package declares), and again while unpacking
                // (the bytes that really come out), so a package can't fill the disk.
                if (archive.Entries.Count > MaxFiles)
                    throw new InvalidDataException($"The package has {archive.Entries.Count} files, more than the {MaxFiles} accepted.");
                if (archive.Entries.Sum(e => e.Length) > MaxUnpackedBytes)
                    throw new InvalidDataException($"The package unpacks larger than {MaxUnpackedBytes} bytes, the most accepted.");
                string root = staging + Path.DirectorySeparatorChar;
                long left = MaxUnpackedBytes;
                foreach (var entry in archive.Entries)
                {
                    if (entry.FullName.EndsWith('/')) continue;
                    string target = Path.GetFullPath(Path.Combine(staging, entry.FullName));
                    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"The package has a file outside its folder: {entry.FullName}");
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    using (var input = new TrustedHttp.CappedStream(entry.Open(), left))
                    using (var output = File.Create(target))
                        input.CopyTo(output);
                    File.SetLastWriteTime(target, entry.LastWriteTime.DateTime); // as ExtractToFile does
                    left -= new FileInfo(target).Length;
                }
            }
            var roster = OperatorReference.ReadRoster(staging);
            if (roster.Count == 0 || roster.Count < current.Count * OperatorReference.MinShareOfCurrentRoster)
                throw new InvalidDataException($"The package lists {roster.Count} operators (the current copy has {current.Count}); keeping the current copy.");
            CheckInnData(folder, staging);
            OperatorReference.Swap(folder, staging);
            var known = current.Select(o => o.Id).ToHashSet();
            return roster.Where(o => !known.Contains(o.Id)).ToList();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            throw new InvalidDataException($"The operator package couldn't be installed: {ex.Message}", ex);
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                try { Directory.Delete(staging, recursive: true); } catch (IOException) { }
            }
        }
    }
}
