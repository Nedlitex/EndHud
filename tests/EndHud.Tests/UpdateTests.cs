using System.Net;
using System.Net.Http;
using Xunit;

namespace EndHud.Tests;

/// <summary>Finding a newer EndHud on GitHub (against a stand-in for GitHub's API).</summary>
public class UpdateTests
{
    [Theory]
    [InlineData("v1.2.0", "1.2.0")]
    [InlineData("1.2.0", "1.2.0")]
    [InlineData("1.2.0+3025454ae8dded9f", "1.2.0")]
    [InlineData("v0.0.12", "0.0.12")]
    [InlineData("0.0.0", null)]            // a development build
    [InlineData("0.0.0+3025454", null)]
    [InlineData("v1.2", null)]             // release tags have three numbers
    [InlineData("1.2.3.4", null)]
    [InlineData("latest", null)]
    [InlineData("", null)]
    public void Reads_versions_from_tags_and_builds(string text, string? expected) =>
        Assert.Equal(expected is null ? null : Version.Parse(expected), ReleaseChecker.ParseVersion(text));

    [Fact]
    public async Task Reads_the_latest_release_from_GitHub()
    {
        var github = new FakeGitHub(HttpStatusCode.OK, """{"tag_name":"v1.4.0","html_url":"https://github.com/Nedlitex/EndHud/releases/tag/v1.4.0"}""");

        var latest = await ReleaseChecker.LatestAsync(new HttpClient(github));

        Assert.Equal(new ReleaseInfo(new Version(1, 4, 0), "https://github.com/Nedlitex/EndHud/releases/tag/v1.4.0"), latest);
        var request = Assert.Single(github.Requests);
        Assert.Equal("https://api.github.com/repos/Nedlitex/EndHud/releases/latest", request.Url);
        Assert.Contains("EndHud/", request.UserAgent); // GitHub requires a User-Agent
    }

    [Fact]
    public async Task No_release_yet_or_a_private_repository_is_not_an_error()
    {
        Assert.Null(await ReleaseChecker.LatestAsync(new HttpClient(new FakeGitHub(HttpStatusCode.NotFound, "{}"))));
    }

    [Fact]
    public async Task Finds_a_newer_version_and_announces_it_once()
    {
        var github = Release("v1.4.0");
        var updates = new UpdateService(() => new HttpClient(github, disposeHandler: false), new Version(1, 3, 2));
        var announced = new List<Version>();
        updates.NewVersionFound += r => announced.Add(r.Version);

        var check = await updates.CheckAsync();

        Assert.Equal(UpdateStatus.NewVersion, check.Status);
        Assert.Equal(new Version(1, 4, 0), updates.Available!.Version);
        Assert.Equal([new Version(1, 4, 0)], announced);
    }

    [Theory]
    [InlineData("v1.3.2")] // the same
    [InlineData("v1.1.0")] // older (running a newer build than the latest release)
    public async Task Up_to_date_when_the_latest_release_is_not_newer(string tag)
    {
        var updates = new UpdateService(() => new HttpClient(Release(tag)), new Version(1, 3, 2));

        Assert.Equal(UpdateStatus.UpToDate, (await updates.CheckAsync()).Status);
        Assert.Null(updates.Available);
    }

    [Fact]
    public async Task Unreachable_GitHub_or_no_release_reports_that_it_could_not_check()
    {
        var down = new UpdateService(() => new HttpClient(new FakeGitHub(HttpStatusCode.InternalServerError, "")), new Version(1, 0, 0));
        var none = new UpdateService(() => new HttpClient(new FakeGitHub(HttpStatusCode.NotFound, "{}")), new Version(1, 0, 0));

        Assert.Equal(UpdateStatus.Unavailable, (await down.CheckAsync()).Status);
        Assert.Equal(UpdateStatus.Unavailable, (await none.CheckAsync()).Status);
    }

    [Fact]
    public async Task A_development_build_does_not_ask_GitHub()
    {
        var github = Release("v1.4.0");
        var updates = new UpdateService(() => new HttpClient(github), current: null);

        Assert.Equal(UpdateStatus.DevelopmentBuild, (await updates.CheckAsync()).Status);
        Assert.Empty(github.Requests);
    }

    [Fact]
    public async Task Checking_again_right_away_reuses_the_answer()
    {
        var github = Release("v1.4.0");
        var updates = new UpdateService(() => new HttpClient(github, disposeHandler: false), new Version(1, 3, 2));

        await updates.CheckAsync();
        await updates.CheckAsync();

        Assert.Single(github.Requests); // GitHub allows 60 unauthenticated requests an hour
    }

    private static FakeGitHub Release(string tag) =>
        new(HttpStatusCode.OK, $$"""{"tag_name":"{{tag}}","html_url":"https://github.com/Nedlitex/EndHud/releases/tag/{{tag}}"}""");

    private sealed class FakeGitHub(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public List<(string Url, string UserAgent)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add((request.RequestUri!.ToString(), request.Headers.UserAgent.ToString()));
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }
}
