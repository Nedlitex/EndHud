using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json.Nodes;

namespace EndHud;

/// <param name="Version">The release's version, from its tag (v1.2.0 → 1.2.0).</param>
/// <param name="PageUrl">The release's page on GitHub, where its MSI and portable exe are.</param>
internal sealed record ReleaseInfo(Version Version, string PageUrl);

/// <summary>
/// Is there a newer EndHud than this one? Asks GitHub for the repository's latest published release
/// (drafts and pre-releases don't count). Nothing is downloaded or installed: the user gets the
/// release page.
/// </summary>
internal static class ReleaseChecker
{
    public const string Repository = "Nedlitex/EndHud";
    private const string LatestReleaseApi = $"https://api.github.com/repos/{Repository}/releases/latest";

    /// <summary>
    /// This build's version, or null for a development build. Release builds get their version from
    /// the tag (release.yml passes -p:Version); a plain build is 0.0.0 (EndHud.csproj).
    /// </summary>
    public static Version? Current { get; } = ParseVersion(
        typeof(ReleaseChecker).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

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
    public static async Task<ReleaseInfo?> LatestAsync(HttpClient http, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApi);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.UserAgent.ParseAdd($"EndHud/{Current?.ToString() ?? "dev"} (+https://github.com/{Repository})");
        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();

        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct)) ?? throw new InvalidDataException("Empty answer from GitHub.");
        var version = ParseVersion(json["tag_name"]?.GetValue<string>());
        string? page = json["html_url"]?.GetValue<string>();
        return version is null || page is null ? null : new ReleaseInfo(version, page);
    }
}
