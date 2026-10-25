using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using Xunit;

namespace EndHud.Tests;

/// <summary>
/// Where an update may come from (<see cref="UpdateTrust"/>, <see cref="UpdateFeed"/>): the release information
/// only from GitHub's API for this repository, the files only from this repository's release downloads and the
/// hosts GitHub redirects them to, every redirect checked. A forged answer (a proxy that holds a trusted
/// certificate) can no longer send EndHud to a file anywhere else. No network: a stand-in handler answers by
/// address and records what was asked.
/// </summary>
[Collection(ProcessStartCollection.Name)]
public sealed class UpdateTrustTests : IDisposable
{
    private static readonly byte[] NewExe = "new EndHud 1.4.0"u8.ToArray();
    private const string Download = "https://github.com/Nedlitex/EndHud/releases/download/v1.4.0/EndHud-1.4.0-portable.exe";
    private const string AssetHost = "https://release-assets.githubusercontent.com/github-production-release-asset/1/abc?sp=r";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "endhud-tests", Guid.NewGuid().ToString("N"));
    private readonly Routes _web = new();

    public UpdateTrustTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "app"));
        File.WriteAllBytes(Exe, "old"u8.ToArray());
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private string Exe => Path.Combine(_root, "app", "EndHud.exe");

    private Updater NewUpdater(UpdateFeed feed) =>
        new(() => new HttpClient(_web, disposeHandler: false), Exe, Path.Combine(_root, "work"), InstallKind.Portable, _ => true, feed, elevated: false);

    private static ReleaseInfo Release(string url) => new(new Version(1, 4, 0), "https://github.com/Nedlitex/EndHud/releases/tag/v1.4.0",
        [new ReleaseAsset("EndHud-1.4.0-portable.exe", url, Convert.ToHexString(SHA256.HashData(NewExe)).ToLowerInvariant())]);

    [Theory]
    [InlineData("https://evil.example/EndHud-1.4.0-portable.exe")]
    [InlineData("https://github.com/someone-else/EndHud/releases/download/v1.4.0/EndHud-1.4.0-portable.exe")]
    [InlineData("https://github.com/Nedlitex/EndHud/raw/main/EndHud-1.4.0-portable.exe")] // not a release file
    [InlineData("https://github.com.evil.example/Nedlitex/EndHud/releases/download/v1.4.0/EndHud-1.4.0-portable.exe")]
    [InlineData("https://github.com@evil.example/Nedlitex/EndHud/releases/download/v1.4.0/EndHud-1.4.0-portable.exe")]
    [InlineData("https://github.com:8443/Nedlitex/EndHud/releases/download/v1.4.0/EndHud-1.4.0-portable.exe")]
    [InlineData("http://github.com/Nedlitex/EndHud/releases/download/v1.4.0/EndHud-1.4.0-portable.exe")]
    [InlineData("https://release-assets.githubusercontent.com/x/EndHud-1.4.0-portable.exe")] // only as GitHub's redirect
    [InlineData("http://127.0.0.1:8765/EndHud-1.4.0-portable.exe")] // the stand-in only with --from
    [InlineData("file:///C:/EndHud-1.4.0-portable.exe")]
    public async Task A_file_anywhere_but_this_repositorys_releases_is_not_downloaded(string url)
    {
        var outcome = await NewUpdater(UpdateFeed.GitHub).InstallAsync(Release(url), restart: true);

        Assert.Equal(UpdateFailure.NoPackage, outcome.Failure);
        Assert.Empty(_web.Asked);
        Assert.Equal("old"u8.ToArray(), File.ReadAllBytes(Exe));
    }

    [Theory]
    [InlineData("release-assets.githubusercontent.com")]
    [InlineData("objects.githubusercontent.com")]
    public async Task GitHubs_redirect_to_its_asset_host_is_followed(string host)
    {
        string asset = AssetHost.Replace("release-assets.githubusercontent.com", host);
        _web.Redirect(Download, asset);
        _web.Serve(asset, NewExe);

        var outcome = await NewUpdater(UpdateFeed.GitHub).InstallAsync(Release(Download), restart: true);

        Assert.True(outcome.Started, $"{outcome.Failure} {outcome.Detail}");
        Assert.Equal([Download, asset], _web.Asked);
        Assert.Equal(NewExe, File.ReadAllBytes(Exe));
    }

    [Theory]
    [InlineData("https://evil.example/EndHud.exe")]
    [InlineData("http://release-assets.githubusercontent.com/x")] // https only
    [InlineData("https://github.com/someone-else/x/releases/download/v1/EndHud.exe")]
    [InlineData("http://127.0.0.1:8765/EndHud.exe")]
    public async Task A_redirect_anywhere_else_is_refused_before_it_is_asked(string elsewhere)
    {
        _web.Redirect(Download, elsewhere);
        _web.Serve(elsewhere, NewExe);

        var outcome = await NewUpdater(UpdateFeed.GitHub).InstallAsync(Release(Download), restart: true);

        Assert.Equal(UpdateFailure.Download, outcome.Failure);
        Assert.Equal([Download], _web.Asked);
        Assert.Contains("refused", outcome.Detail);
        Assert.Equal("old"u8.ToArray(), File.ReadAllBytes(Exe));
    }

    [Fact]
    public async Task Redirects_that_never_end_are_given_up()
    {
        _web.Redirect(Download, Download);

        var outcome = await NewUpdater(UpdateFeed.GitHub).InstallAsync(Release(Download), restart: true);

        Assert.Equal(UpdateFailure.Download, outcome.Failure);
        Assert.True(_web.Asked.Count <= 6, $"{_web.Asked.Count} requests");
    }

    [Fact]
    public async Task With_the_stand_in_on_this_computer_its_files_are_downloaded()
    {
        const string local = "http://127.0.0.1:8765/EndHud-1.4.0-portable.exe";
        _web.Serve(local, NewExe);
        var feed = UpdateFeed.Local("http://127.0.0.1:8765/latest.json", out _)!;

        var outcome = await NewUpdater(feed).InstallAsync(Release(local), restart: true);

        Assert.True(outcome.Started, $"{outcome.Failure} {outcome.Detail}");
        Assert.Equal(NewExe, File.ReadAllBytes(Exe));
    }

    [Fact]
    public async Task The_release_information_is_not_taken_from_a_redirect_off_GitHubs_API()
    {
        _web.Redirect(ReleaseChecker.LatestReleaseApi, "https://evil.example/latest.json");
        _web.Serve("https://evil.example/latest.json", """{"tag_name":"v9.9.9","html_url":"https://evil.example","assets":[]}"""u8.ToArray());

        var check = await new UpdateService(() => new HttpClient(_web, disposeHandler: false), new Version(1, 0, 0)).CheckAsync();

        Assert.Equal(UpdateStatus.Unavailable, check.Status);
        Assert.Equal([ReleaseChecker.LatestReleaseApi], _web.Asked);
    }

    [Fact]
    public async Task A_redirect_within_GitHubs_API_for_this_repository_is_followed()
    {
        const string moved = "https://api.github.com/repos/Nedlitex/EndHud/releases/latest?moved=1";
        _web.Redirect(ReleaseChecker.LatestReleaseApi, moved);
        _web.Serve(moved, """{"tag_name":"v1.4.0","html_url":"https://github.com/Nedlitex/EndHud/releases/tag/v1.4.0","assets":[]}"""u8.ToArray());

        var check = await new UpdateService(() => new HttpClient(_web, disposeHandler: false), new Version(1, 0, 0)).CheckAsync();

        Assert.Equal(UpdateStatus.NewVersion, check.Status);
    }

    [Fact]
    public async Task An_oversized_release_answer_is_not_read()
    {
        _web.Serve(ReleaseChecker.LatestReleaseApi, new byte[ReleaseChecker.MaxAnswerBytes + 1]);

        var check = await new UpdateService(() => new HttpClient(_web, disposeHandler: false), new Version(1, 0, 0)).CheckAsync();

        Assert.Equal(UpdateStatus.Unavailable, check.Status);
    }

    [Theory]
    [InlineData("https://github.com/Nedlitex/EndHud/releases/tag/v1.4.0", true)]
    [InlineData("https://github.com/nedlitex/endhud/releases/latest", true)]
    [InlineData("https://github.com/Nedlitex/EndHud", true)]
    [InlineData("https://github.com/Nedlitex/EndHudEvil/releases", false)]
    [InlineData("http://github.com/Nedlitex/EndHud/releases", false)]
    [InlineData("https://evil.example/Nedlitex/EndHud/", false)]
    [InlineData("https://github.com@evil.example/Nedlitex/EndHud/", false)]
    [InlineData("file:///C:/Users/x/Downloads/evil.exe", false)]
    [InlineData(@"\\host\share\evil.exe", false)]
    [InlineData("C:\\Windows\\System32\\calc.exe", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_this_repositorys_pages_on_GitHub_are_opened(string? url, bool opened) =>
        Assert.Equal(opened, UpdateTrust.IsRepositoryPage(url));

    [Fact]
    public void A_ShellExecute_link_opener_refuses_anything_else_and_says_why()
    {
        var started = new List<ProcessStartInfo>();

        Assert.False(TrayIcon.OpenLink("file:///C:/evil.exe", info => started.Add(info)));
        Assert.True(TrayIcon.OpenLink("https://github.com/Nedlitex/EndHud/releases/tag/v1.4.0", info => started.Add(info)));

        var only = Assert.Single(started);
        Assert.Equal("https://github.com/Nedlitex/EndHud/releases/tag/v1.4.0", only.FileName);
    }

    /// <summary>Answers by address: a file, a redirect, or 404; records every address asked.</summary>
    private sealed class Routes : HttpMessageHandler
    {
        private readonly Dictionary<string, Func<HttpResponseMessage>> _answers = [];

        public List<string> Asked { get; } = [];

        public void Serve(string url, byte[] body) =>
            _answers[url] = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };

        public void Redirect(string url, string to) =>
            _answers[url] = () => new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri(to) } };

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string url = request.RequestUri!.OriginalString;
            Asked.Add(url);
            return Task.FromResult(_answers.TryGetValue(url, out var answer) ? answer() : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
