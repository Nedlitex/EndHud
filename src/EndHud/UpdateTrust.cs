namespace EndHud;

/// <summary>
/// Where an EndHud update may come from, and which links EndHud opens for it. Every address is checked here
/// before it is asked, and again at every redirect (<see cref="EndHud.Vision.TrustedHttp"/>):
/// <list type="bullet">
/// <item>The release information only from GitHub's API for this repository
/// (<c>https://api.github.com/repos/Nedlitex/EndHud/…</c>).</item>
/// <item>The installer and the portable exe only from this repository's release downloads
/// (<c>https://github.com/Nedlitex/EndHud/releases/download/…</c>), and the hosts GitHub redirects those to
/// (<see cref="AssetRedirectHosts"/>).</item>
/// <item>The release-notes link only when it is an https page of this repository on github.com.</item>
/// <item>For release.yml's update test only (<see cref="UpdateFeed.Local"/>, through <c>--update-now --from</c>):
/// a stand-in on this computer (loopback), and nothing else besides GitHub.</item>
/// </list>
/// HTTPS with the Windows certificate store stays the transport's protection; this keeps a forged answer, or
/// another program on the PC, from pointing EndHud at a file somewhere else.
/// </summary>
internal static class UpdateTrust
{
    /// <summary>Where GitHub sends a release download (seen: release-assets.githubusercontent.com since 2025; objects.githubusercontent.com before).</summary>
    public static readonly IReadOnlySet<string> AssetRedirectHosts =
        new HashSet<string>(["release-assets.githubusercontent.com", "objects.githubusercontent.com"], StringComparer.OrdinalIgnoreCase);

    private static readonly string RepositoryPath = "/" + ReleaseChecker.Repository + "/";

    /// <summary>A plain https address on <paramref name="host"/>: no user name in it, the usual port.</summary>
    private static bool PlainHttps(Uri url, string host) =>
        url.IsAbsoluteUri && url.Scheme == Uri.UriSchemeHttps && url.IsDefaultPort && url.UserInfo.Length == 0
        && string.Equals(url.IdnHost, host, StringComparison.OrdinalIgnoreCase);

    /// <summary>On this computer: release.yml's stand-in for GitHub (http or https on loopback).</summary>
    public static bool IsLocal(Uri url) =>
        url.IsAbsoluteUri && (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps) && url.UserInfo.Length == 0 && url.IsLoopback;

    /// <summary>GitHub's API for this repository's releases.</summary>
    public static bool IsReleaseApi(Uri url) =>
        PlainHttps(url, "api.github.com") && url.AbsolutePath.StartsWith("/repos" + RepositoryPath + "releases", StringComparison.OrdinalIgnoreCase);

    /// <summary>A file attached to one of this repository's releases, where the release information points.</summary>
    public static bool IsReleaseDownload(Uri url) =>
        PlainHttps(url, "github.com") && url.AbsolutePath.StartsWith(RepositoryPath + "releases/download/", StringComparison.OrdinalIgnoreCase);

    /// <summary>Where GitHub redirects a release download to.</summary>
    public static bool IsAssetRedirect(Uri url) =>
        url.IsAbsoluteUri && url.Scheme == Uri.UriSchemeHttps && url.IsDefaultPort && url.UserInfo.Length == 0
        && AssetRedirectHosts.Contains(url.IdnHost);

    /// <summary>A page of this repository on github.com (the release notes): the only kind of link the updater's balloons and menu open.</summary>
    public static bool IsRepositoryPage(string? url) =>
        url is not null && Uri.TryCreate(url, UriKind.Absolute, out var uri) && PlainHttps(uri, "github.com")
        && (uri.AbsolutePath.StartsWith(RepositoryPath, StringComparison.OrdinalIgnoreCase)
            || uri.AbsolutePath.Equals(RepositoryPath.TrimEnd('/'), StringComparison.OrdinalIgnoreCase));
}

/// <summary>Where updates are looked for: GitHub (every EndHud), or release.yml's stand-in on this computer.</summary>
/// <param name="Api">The latest-release API to ask.</param>
/// <param name="AllowsLocal">Also trust addresses on this computer (the stand-in serves the files too).</param>
internal sealed record UpdateFeed(Uri Api, bool AllowsLocal)
{
    public static UpdateFeed GitHub { get; } = new(new Uri(ReleaseChecker.LatestReleaseApi), AllowsLocal: false);

    /// <summary>A stand-in on this computer, or null (with why) for anything else.</summary>
    public static UpdateFeed? Local(string? from, out string refusal)
    {
        refusal = "";
        if (string.IsNullOrWhiteSpace(from) || !Uri.TryCreate(from, UriKind.Absolute, out var url) || url.IsUnc || url.IsFile)
        {
            refusal = $"refused --from \"{from}\": not an http(s) address";
            return null;
        }
        if (!UpdateTrust.IsLocal(url))
        {
            refusal = $"refused --from {url.Scheme}://{url.Host}: only a stand-in on this computer (127.0.0.1, localhost) may replace GitHub";
            return null;
        }
        return new UpdateFeed(url, AllowsLocal: true);
    }

    /// <summary>May the release information be asked here?</summary>
    public bool TrustsApi(Uri url) => UpdateTrust.IsReleaseApi(url) || (AllowsLocal && UpdateTrust.IsLocal(url));

    /// <summary>May the release information send EndHud here for a file (the first address)?</summary>
    public bool TrustsDownloadStart(Uri url) => UpdateTrust.IsReleaseDownload(url) || (AllowsLocal && UpdateTrust.IsLocal(url));

    /// <summary>May a release download be redirected here?</summary>
    public bool TrustsDownload(Uri url) => TrustsDownloadStart(url) || UpdateTrust.IsAssetRedirect(url);
}
