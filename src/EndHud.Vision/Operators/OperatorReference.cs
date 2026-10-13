using System.Net.Http;

namespace EndHud.Vision.Operators;

/// <param name="Download">What the download fetched, reused and missed.</param>
/// <param name="Added">Operators that weren't in the previous copy (all of them on the first download).</param>
public sealed record OperatorUpdate(WikiDownload Download, IReadOnlyList<OperatorInfo> Added)
{
    public int Operators => Download.Operators;
}

/// <summary>
/// The operator reference data on this machine: the roster, avatars and skill icons downloaded
/// from the wiki (<see cref="WikiOperatorSource"/>). EndHud ships none of it; the app fetches it on
/// first start and refreshes it when it gets old, and developer tools keep their own copy.
/// <para>
/// An update never leaves a broken library behind: it downloads beside the current copy, keeps
/// the current copy if the new one is much smaller (a wiki or network problem, not operators
/// being removed), and swaps the new one in with two renames. A swap interrupted between the two
/// is undone the next time the folder is used.
/// </para>
/// </summary>
public static class OperatorReference
{
    private const string Manifest = "operators.json";

    /// <summary>
    /// A new download must list at least this share of the current roster to replace it: operators
    /// aren't removed from the game, so far fewer means the wiki or the network failed part-way.
    /// </summary>
    public const double MinShareOfCurrentRoster = 0.8;

    /// <summary>True when <paramref name="folder"/> holds a complete download.</summary>
    public static bool Exists(string folder)
    {
        Recover(folder);
        return File.Exists(Path.Combine(folder, Manifest));
    }

    /// <summary>When the download in <paramref name="folder"/> was made, or null if there is none.</summary>
    public static DateTime? DownloadedAtUtc(string folder) =>
        Exists(folder) ? File.GetLastWriteTimeUtc(Path.Combine(folder, Manifest)) : null;

    /// <summary>
    /// Download the current roster into <paramref name="folder"/>, replacing what's there (new
    /// operators and new icons appear; unchanged files are reused). Throws, leaving the current
    /// copy as it was, when the wiki can't be reached or returns too little.
    /// </summary>
    public static async Task<OperatorUpdate> UpdateAsync(string folder, HttpClient http, WikiPoliteness? politeness = null,
        CancellationToken ct = default)
    {
        folder = Path.GetFullPath(folder.TrimEnd(Path.DirectorySeparatorChar, '/'));
        string staging = folder + ".new", old = folder + ".old";
        bool hasCurrent = Exists(folder);
        var current = hasCurrent ? Roster(folder) : [];
        int currentCount = current.Count;

        if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        try
        {
            var result = await WikiOperatorSource.DownloadAsync(staging, http, hasCurrent ? folder : null, politeness, ct);
            if (result.Operators == 0 || result.Operators < currentCount * MinShareOfCurrentRoster)
                throw new InvalidDataException(
                    $"The wiki listed {result.Operators} operators (the current copy has {currentCount}); keeping the current copy.");

            Directory.CreateDirectory(Path.GetDirectoryName(folder)!);
            if (Directory.Exists(old)) Directory.Delete(old, recursive: true);
            if (hasCurrent) Directory.Move(folder, old);
            Directory.Move(staging, folder);
            TryDelete(old);
            var known = current.Select(o => o.Id).ToHashSet();
            return new OperatorUpdate(result, Roster(folder).Where(o => !known.Contains(o.Id)).ToList());
        }
        finally
        {
            TryDelete(staging);
        }
    }

    /// <summary>
    /// True when <paramref name="folder"/> should be downloaded again: there's no copy, it's older than
    /// <paramref name="maxAge"/>, or it's from before status icons were downloaded (EndHud 0.1.0), which
    /// rotations need.
    /// </summary>
    public static bool IsDue(string folder, TimeSpan maxAge) =>
        DownloadedAtUtc(folder) is not { } at
        || DateTime.UtcNow - at >= maxAge
        || !File.Exists(Path.Combine(folder, OperatorLibrary.StatusManifestName));

    /// <summary>
    /// <see cref="UpdateAsync"/> if <see cref="IsDue"/>. Returns null when the current copy is fresh enough.
    /// </summary>
    public static async Task<OperatorUpdate?> EnsureAsync(string folder, TimeSpan maxAge, HttpClient http,
        WikiPoliteness? politeness = null, CancellationToken ct = default)
    {
        if (!IsDue(folder, maxAge)) return null;
        return await UpdateAsync(folder, http, politeness, ct);
    }

    private static List<OperatorInfo> Roster(string folder)
    {
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<OperatorInfo>>(File.ReadAllBytes(Path.Combine(folder, Manifest))) ?? [];
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }

    // A swap interrupted after moving the current copy aside: put it back.
    private static void Recover(string folder)
    {
        string old = Path.GetFullPath(folder.TrimEnd(Path.DirectorySeparatorChar, '/')) + ".old";
        if (!Directory.Exists(folder) && Directory.Exists(old)) Directory.Move(old, folder);
    }

    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
