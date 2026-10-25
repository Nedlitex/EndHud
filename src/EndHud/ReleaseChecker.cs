using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json.Nodes;
using EndHud.Vision;

namespace EndHud;

/// <summary>One file attached to a release.</summary>
/// <param name="Sha256">The file's SHA-256 as GitHub reports it (lowercase hex); null if GitHub gave none.</param>
internal sealed record ReleaseAsset(string Name, string Url, string? Sha256);

/// <param name="Version">The release's version, from its tag (v1.2.0 → 1.2.0).</param>
/// <param name="PageUrl">The release's page on GitHub, where its MSI and portable exe are.</param>
/// <param name="Assets">The files attached to the release.</param>
internal sealed record ReleaseInfo(Version Version, string PageUrl, IReadOnlyList<ReleaseAsset> Assets)
{
    /// <summary>The installer, as release.yml names it: EndHud-1.2.0.msi.</summary>
    public ReleaseAsset? Msi => Assets.FirstOrDefault(a => a.Name.Equals($"EndHud-{Version.ToString(3)}.msi", StringComparison.OrdinalIgnoreCase));

    /// <summary>The portable exe, as release.yml names it: EndHud-1.2.0-portable.exe.</summary>
    public ReleaseAsset? Portable => Assets.FirstOrDefault(a => a.Name.Equals($"EndHud-{Version.ToString(3)}-portable.exe", StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Is there a newer EndHud than this one? Asks GitHub for the repository's latest published release
/// (drafts and pre-releases don't count). Installing it is <see cref="Updater"/>'s job.
/// </summary>
internal static class ReleaseChecker
{
    public const string Repository = "Nedlitex/EndHud";
    public const string LatestReleaseApi = $"https://api.github.com/repos/{Repository}/releases/latest";

    /// <summary>The most of the release information read (a real answer is a few KB).</summary>
    public const int MaxAnswerBytes = 1024 * 1024;

    /// <summary>
    /// This build's version, or null for a development build. Release builds get their version from
    /// the tag (release.yml passes -p:Version); a plain build is 0.0.0 (EndHud.csproj).
    /// </summary>
    public static Version? Current { get; } = ParseVersion(
        typeof(ReleaseChecker).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    /// <summary>This build's version for people: "版本 1.2.0", or "开发版本" for a development build.</summary>
    public static string DisplayVersion => Current is { } version ? Loc.Format("App.Version", version) : Loc.Get("App.Version.Dev");

    /// <summary>"v1.2.0", "1.2.0", "1.2.0+3025454" → 1.2.0. Null for anything else, and for 0.0.0 (a development build).</summary>
    internal static Version? ParseVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string core = text.Trim().TrimStart('v', 'V').Split('+', '-')[0];
        if (!Version.TryParse(core, out var version) || version.Build < 0 || version.Revision >= 0) return null;
        return version == new Version(0, 0, 0) ? null : version;
    }

    /// <summary>
    /// The latest release, or null if there is none yet. GitHub also answers "not found" while the
    /// repository is private. Throws <see cref="HttpRequestException"/> when GitHub can't be reached.
    /// </summary>
    /// <param name="feed">Where to ask: GitHub's latest-release API (null), or release.yml's stand-in. Asked, and
    /// redirects followed, only where the feed trusts (<see cref="UpdateFeed.TrustsApi"/>): else an
    /// <see cref="UntrustedSourceException"/>, which is an <see cref="HttpRequestException"/>.</param>
    public static async Task<ReleaseInfo?> LatestAsync(HttpClient http, UpdateFeed? feed = null, CancellationToken ct = default)
    {
        feed ??= UpdateFeed.GitHub;
        using var response = await TrustedHttp.GetAsync(http, feed.Api, feed.TrustsApi, request =>
        {
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.UserAgent.ParseAdd(UserAgent);
        }, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();

        byte[] body = await TrustedHttp.ReadCappedAsync(response, MaxAnswerBytes, ct);
        var json = JsonNode.Parse(body) ?? throw new InvalidDataException("Empty answer from GitHub.");
        var version = ParseVersion(json["tag_name"]?.GetValue<string>());
        string? page = json["html_url"]?.GetValue<string>();
        if (version is null || page is null) return null;

        var assets = new List<ReleaseAsset>();
        foreach (var asset in json["assets"]?.AsArray() ?? [])
        {
            string? name = asset?["name"]?.GetValue<string>();
            string? url = asset?["browser_download_url"]?.GetValue<string>();
            string? digest = asset?["digest"]?.GetValue<string>();
            if (name is null || url is null) continue;
            string? sha256 = digest is not null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                ? digest["sha256:".Length..].ToLowerInvariant()
                : null;
            assets.Add(new ReleaseAsset(name, url, sha256));
        }
        return new ReleaseInfo(version, page, assets);
    }

    public static string UserAgent => $"EndHud/{Current?.ToString() ?? "dev"} (+https://github.com/{Repository})";
}
