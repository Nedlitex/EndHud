using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using EndHud.Vision.Rotations;

namespace EndHud.Vision.Operators;

/// <param name="Installed">A newer package was installed.</param>
/// <param name="Added">Operators that are new with it (empty when nothing was installed).</param>
public sealed record OperatorPackageSyncResult(bool Installed, IReadOnlyList<OperatorInfo> Added);

/// <summary>
/// Keeps the operator library up to date from the repository's <c>operators/</c> folder on GitHub,
/// as recommended rotations are kept (<see cref="RotationSync"/>): one request lists the folder with
/// each file's git blob SHA-1; the package is downloaded only when that changed since the last
/// check, must match it, and is installed only if it's newer than the library in use
/// (<see cref="OperatorPackage.InstallIfNewer"/>). An unreachable GitHub, a 404 or an unexpected
/// answer changes nothing.
/// </summary>
public static class OperatorPackageSync
{
    /// <summary>The last listed SHA, beside the reference folder (the folder itself is replaced on install).</summary>
    public const string SeenShaName = "package-seen.sha";

    public static string ContentsApi(string repository) => $"https://api.github.com/repos/{repository}/contents/operators?ref=main";

    /// <summary>Null when GitHub couldn't be asked or answered oddly (nothing changed).</summary>
    public static async Task<OperatorPackageSyncResult?> SyncAsync(string referenceFolder, HttpClient http, string api, string userAgent,
        CancellationToken ct = default)
    {
        if (await ListAsync(http, api, userAgent, ct) is not { } listed) return null;
        if (listed.Sha is not { } sha || listed.DownloadUrl is not { } url) return new OperatorPackageSyncResult(false, []); // nothing published

        string seenPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(referenceFolder.TrimEnd('\\', '/')))!, SeenShaName);
        string? seen = File.Exists(seenPath) ? File.ReadAllText(seenPath).Trim() : null;
        if (seen == sha && OperatorReference.Exists(referenceFolder)) return new OperatorPackageSyncResult(false, []);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd(userAgent);
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        byte[] zip = await response.Content.ReadAsByteArrayAsync(ct);
        if (RotationSync.GitBlobSha(zip) != sha)
            throw new InvalidDataException("The downloaded operator package doesn't match its listing.");

        var added = OperatorPackage.InstallIfNewer(referenceFolder, zip);
        Directory.CreateDirectory(Path.GetDirectoryName(seenPath)!);
        File.WriteAllText(seenPath, sha);
        return new OperatorPackageSyncResult(added is not null, added ?? []);
    }

    /// <summary>The package as the folder lists it (both null: not published), or null when GitHub couldn't be asked or answered oddly.</summary>
    private sealed record Listing(string? Sha, string? DownloadUrl);

    private static async Task<Listing?> ListAsync(HttpClient http, string api, string userAgent, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, api);
        request.Headers.UserAgent.ParseAdd(userAgent);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        try
        {
            if (JsonNode.Parse(await response.Content.ReadAsStringAsync(ct)) is not JsonArray entries) return null;
            foreach (var entry in entries)
            {
                if (entry?["name"]?.GetValue<string>() != OperatorPackage.FileName || entry["type"]?.GetValue<string>() != "file") continue;
                string? sha = entry["sha"]?.GetValue<string>(), url = entry["download_url"]?.GetValue<string>();
                return sha is null || url is null ? null : new Listing(sha, url);
            }
            return new Listing(null, null);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }
}
