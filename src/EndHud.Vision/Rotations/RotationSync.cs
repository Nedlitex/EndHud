using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace EndHud.Vision.Rotations;

/// <summary>What a sync changed. Names are the rotations' names, for telling the player.</summary>
/// <param name="Failed">Files listed on GitHub that couldn't be downloaded, verified or read; the
/// previous copy (if any) is kept.</param>
public sealed record RotationSyncResult(
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Updated,
    IReadOnlyList<string> Removed,
    int Failed,
    int Total)
{
    public bool Changed => Added.Count + Updated.Count + Removed.Count > 0;
}

/// <summary>
/// Keeps <see cref="RotationLibrary.Published"/> the same as the <c>rotations/</c> folder of the
/// EndHud repository, so recommended rotations are fixed and added by committing a file, without a
/// release. One request lists the folder (GitHub's contents API, which gives each file's git blob
/// SHA-1); only new or changed files are downloaded, and each is checked against that SHA before it
/// replaces the local copy.
/// <list type="bullet">
/// <item>A file is accepted only if it's named <c>&lt;id&gt;.json</c> (lowercase letters, digits,
/// dashes), is a rotation this build can read, and has that id.</item>
/// <item>A file removed from the repository is removed locally, but only after a complete listing:
/// an unreachable GitHub, a 404 or a damaged answer changes nothing.</item>
/// <item>Rotations the player deleted (<see cref="RotationLibrary.Hidden"/>) aren't downloaded.</item>
/// </list>
/// </summary>
public static partial class RotationSync
{
    /// <summary>The contents API for a repository's <c>rotations/</c> folder on main.</summary>
    public static string ContentsApi(string repository) => $"https://api.github.com/repos/{repository}/contents/rotations?ref=main";

    /// <summary>The most a rotation file, or the folder's listing, may be (a real rotation is a few KB).</summary>
    public const int MaxFileBytes = 1024 * 1024;

    /// <summary>Bring the published rotations up to date. Null when the folder couldn't be listed.</summary>
    /// <param name="trusted">Where the listing and the files may come from, every redirect checked too
    /// (the app: <see cref="TrustedHttp.GitHubRepository"/>). A file listed anywhere else is counted as failed.</param>
    /// <exception cref="HttpRequestException">GitHub couldn't be reached, or the listing's address isn't trusted.</exception>
    public static async Task<RotationSyncResult?> SyncAsync(RotationLibrary library, HttpClient http, string api, string userAgent,
        Func<Uri, bool> trusted, CancellationToken ct = default)
    {
        var listing = await ListAsync(http, api, userAgent, trusted, ct);
        if (listing is null) return null;

        var store = library.Published;
        var hidden = library.Hidden();
        var current = store.Load().Rotations.ToDictionary(r => r.Id, StringComparer.Ordinal);
        List<string> added = [], updated = [], removed = [];
        int failed = 0;

        foreach (var file in listing)
        {
            if (hidden.Contains(file.Id)) continue;
            string path = Path.Combine(store.Folder, file.Id + ".json");
            if (File.Exists(path) && GitBlobSha(await File.ReadAllBytesAsync(path, ct)) == file.Sha) continue;

            Rotation rotation;
            byte[] bytes;
            try
            {
                bytes = await GetAsync(http, file.Url, userAgent, trusted, ct);
                if (GitBlobSha(bytes) != file.Sha) throw new InvalidDataException($"{file.Id}.json doesn't match its SHA.");
                rotation = RotationFormat.FromJson(Encoding.UTF8.GetString(bytes));
                if (rotation.Id != file.Id) throw new InvalidDataException($"{file.Id}.json holds the rotation \"{rotation.Id}\".");
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or RotationReadException && !ct.IsCancellationRequested)
            {
                System.Diagnostics.Debug.WriteLine($"EndHud published rotation {file.Id}: {ex.Message}");
                failed++;
                continue;
            }

            // Written as downloaded, so the next listing's SHA matches it.
            Directory.CreateDirectory(store.Folder);
            await File.WriteAllBytesAsync(path + ".tmp", bytes, ct);
            File.Move(path + ".tmp", path, overwrite: true);
            (current.ContainsKey(file.Id) ? updated : added).Add(rotation.Name);
        }

        var listed = listing.Select(f => f.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var (id, rotation) in current)
        {
            if (listed.Contains(id) && !hidden.Contains(id)) continue;
            if (store.Delete(id) && !hidden.Contains(id)) removed.Add(rotation.Name);
        }
        return new RotationSyncResult(added, updated, removed, failed, listing.Count - listing.Count(f => hidden.Contains(f.Id)));
    }

    private sealed record ListedFile(string Id, string Sha, string Url);

    /// <summary>The folder's rotation files, or null if it can't be listed (404, or not a listing).</summary>
    private static async Task<List<ListedFile>?> ListAsync(HttpClient http, string api, string userAgent, Func<Uri, bool> trusted, CancellationToken ct)
    {
        using var response = await TrustedHttp.GetAsync(http, new Uri(api), trusted, request =>
        {
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.UserAgent.ParseAdd(userAgent);
        }, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();

        try
        {
            if (JsonNode.Parse(await TrustedHttp.ReadCappedAsync(response, MaxFileBytes, ct)) is not JsonArray entries) return null;
            var files = new List<ListedFile>();
            foreach (var entry in entries)
            {
                if (entry?["type"]?.GetValue<string>() != "file") continue;
                string? name = entry["name"]?.GetValue<string>(), sha = entry["sha"]?.GetValue<string>(), url = entry["download_url"]?.GetValue<string>();
                if (name is null || sha is null || url is null) return null; // not the listing we know: change nothing
                if (FileName().Match(name) is not { Success: true } match) continue; // e.g. a README
                files.Add(new ListedFile(match.Groups[1].Value, sha.ToLowerInvariant(), url));
            }
            return files;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or InvalidDataException)
        {
            return null; // damaged, oversized, or not the listing we know: change nothing
        }
    }

    private static async Task<byte[]> GetAsync(HttpClient http, string url, string userAgent, Func<Uri, bool> trusted, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) throw new InvalidDataException($"not an address: {url}");
        using var response = await TrustedHttp.GetAsync(http, uri, trusted, request => request.Headers.UserAgent.ParseAdd(userAgent),
            HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        return await TrustedHttp.ReadCappedAsync(response, MaxFileBytes, ct);
    }

    /// <summary>Git's id for a file's content: SHA-1 of "blob &lt;length&gt;\0" + the bytes.</summary>
    public static string GitBlobSha(byte[] content)
    {
        byte[] header = Encoding.ASCII.GetBytes($"blob {content.Length}\0");
        return Convert.ToHexString(SHA1.HashData([.. header, .. content])).ToLowerInvariant();
    }

    [GeneratedRegex(@"^([a-z0-9][a-z0-9-]*)\.json$")]
    private static partial Regex FileName();
}
