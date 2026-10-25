using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using EndHud.Vision.Operators;
using EndHud.Vision.Rotations;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// The recommended rotations and the operator package come only from this repository on GitHub
/// (<see cref="TrustedHttp.GitHubRepository"/>): a listing that names another host is not followed, nothing
/// is asked there, and every download is read only up to a size limit. No network: a stand-in handler.
/// </summary>
public sealed class SyncTrustTests : IDisposable
{
    private const string RotationsApi = "https://api.example/repos/x/y/contents/rotations?ref=main";
    private const string OperatorsApi = "https://api.example/repos/x/y/contents/operators?ref=main";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "endhud-tests", Guid.NewGuid().ToString("N"));
    private readonly Routes _web = new();

    /// <summary>The stand-in's hosts, as the app trusts GitHub's.</summary>
    private static bool Trusted(Uri url) => url.Scheme == Uri.UriSchemeHttps && url.Host is "api.example" or "raw.example";

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private RotationLibrary Library => new(Path.Combine(_root, "rotations"));

    private static byte[] RotationFile(string id) =>
        Encoding.UTF8.GetBytes(RotationFormat.ToJson(RotationFormatTests.Guide() with { Id = id, Name = id }));

    private void List(string api, params (string Name, byte[] Bytes, string Url)[] files) =>
        _web.Serve(api, Encoding.UTF8.GetBytes(new JsonArray([.. files.Select(f => (JsonNode)new JsonObject
        {
            ["name"] = f.Name, ["type"] = "file", ["sha"] = RotationSync.GitBlobSha(f.Bytes), ["download_url"] = f.Url,
        })]).ToJsonString()));

    private Task<RotationSyncResult?> SyncRotations() =>
        RotationSync.SyncAsync(Library, new HttpClient(_web, disposeHandler: false), RotationsApi, "EndHud-tests", Trusted);

    [Fact]
    public async Task A_rotation_listed_on_another_host_is_not_downloaded()
    {
        byte[] good = RotationFile("alpha"), evil = RotationFile("beta");
        List(RotationsApi, ("alpha.json", good, "https://raw.example/alpha.json"), ("beta.json", evil, "https://evil.example/beta.json"));
        _web.Serve("https://raw.example/alpha.json", good);
        _web.Serve("https://evil.example/beta.json", evil);

        var result = await SyncRotations();

        Assert.Equal(1, result!.Failed);
        Assert.Equal(["alpha"], result.Added);
        Assert.DoesNotContain(_web.Asked, url => url.Contains("evil.example"));
    }

    [Fact]
    public async Task A_redirect_off_the_trusted_hosts_is_not_followed()
    {
        byte[] file = RotationFile("alpha");
        List(RotationsApi, ("alpha.json", file, "https://raw.example/alpha.json"));
        _web.Redirect("https://raw.example/alpha.json", "https://evil.example/alpha.json");
        _web.Serve("https://evil.example/alpha.json", file);

        var result = await SyncRotations();

        Assert.Equal(1, result!.Failed);
        Assert.Empty(result.Added);
        Assert.DoesNotContain(_web.Asked, url => url.Contains("evil.example"));
    }

    [Fact]
    public async Task The_listing_itself_is_asked_only_where_trusted()
    {
        await Assert.ThrowsAnyAsync<HttpRequestException>(() =>
            RotationSync.SyncAsync(Library, new HttpClient(_web, disposeHandler: false), "https://evil.example/contents/rotations", "EndHud-tests", Trusted));
        Assert.Empty(_web.Asked);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)] // no Content-Length: stopped while reading
    public async Task An_oversized_rotation_file_is_not_kept(bool lengthKnown)
    {
        var json = Encoding.UTF8.GetString(RotationFile("alpha"));
        byte[] big = Encoding.UTF8.GetBytes(json.Insert(1, "\"padding\":\"" + new string('x', RotationSync.MaxFileBytes) + "\","));
        List(RotationsApi, ("alpha.json", big, "https://raw.example/alpha.json"));
        _web.Serve("https://raw.example/alpha.json", big, lengthKnown);

        var result = await SyncRotations();

        Assert.Equal(1, result!.Failed);
        Assert.Empty(Library.Load().Rotations);
    }

    [Fact]
    public async Task An_oversized_listing_changes_nothing()
    {
        _web.Serve(RotationsApi, Encoding.UTF8.GetBytes("[" + new string(' ', RotationSync.MaxFileBytes) + "]"));

        Assert.Null(await SyncRotations());
    }

    private Task<OperatorPackageSyncResult?> SyncPackage() =>
        OperatorPackageSync.SyncAsync(Path.Combine(_root, "operators", "reference"), new HttpClient(_web, disposeHandler: false), OperatorsApi, "EndHud-tests", Trusted);

    [Fact]
    public async Task An_operator_package_listed_on_another_host_is_not_downloaded()
    {
        byte[] package = [1, 2, 3];
        List(OperatorsApi, (OperatorPackage.FileName, package, "https://evil.example/" + OperatorPackage.FileName));
        _web.Serve("https://evil.example/" + OperatorPackage.FileName, package);

        await Assert.ThrowsAnyAsync<HttpRequestException>(SyncPackage);
        Assert.DoesNotContain(_web.Asked, url => url.Contains("evil.example"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_oversized_operator_package_is_refused(bool lengthKnown)
    {
        byte[] package = new byte[OperatorPackageSync.MaxPackageBytes + 1];
        List(OperatorsApi, (OperatorPackage.FileName, package, "https://raw.example/" + OperatorPackage.FileName));
        _web.Serve("https://raw.example/" + OperatorPackage.FileName, package, lengthKnown);

        var ex = await Assert.ThrowsAsync<InvalidDataException>(SyncPackage);
        Assert.Contains("larger than", ex.Message);
    }

    [Theory]
    [InlineData("https://api.github.com/repos/Nedlitex/EndHud/contents/rotations?ref=main", true)]
    [InlineData("https://api.github.com/repos/nedlitex/endhud/contents/operators?ref=main", true)]
    [InlineData("https://raw.githubusercontent.com/Nedlitex/EndHud/main/rotations/a.json", true)]
    [InlineData("https://raw.githubusercontent.com/Nedlitex/EndHud/main/operators/operator-library.zip", true)]
    [InlineData("https://api.github.com/repos/someone/EndHud/contents/rotations", false)]
    [InlineData("https://raw.githubusercontent.com/Nedlitex/EndHudX/main/a.json", false)]
    [InlineData("http://raw.githubusercontent.com/Nedlitex/EndHud/main/a.json", false)]
    [InlineData("https://raw.githubusercontent.com:444/Nedlitex/EndHud/main/a.json", false)]
    [InlineData("https://raw.githubusercontent.com.evil.example/Nedlitex/EndHud/main/a.json", false)]
    [InlineData("https://evil.example/Nedlitex/EndHud/main/a.json", false)]
    public void Only_this_repository_on_GitHub_is_trusted_for_the_synced_files(string url, bool trusted) =>
        Assert.Equal(trusted, TrustedHttp.GitHubRepository("Nedlitex/EndHud")(new Uri(url)));

    /// <summary>Answers by address: a file, a redirect, or 404; records every address asked.</summary>
    private sealed class Routes : HttpMessageHandler
    {
        private readonly Dictionary<string, Func<HttpResponseMessage>> _answers = [];

        public List<string> Asked { get; } = [];

        public void Serve(string url, byte[] body, bool lengthKnown = true) =>
            _answers[url] = () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                // A stream that can't seek: the length isn't known until it is read.
                Content = lengthKnown ? new ByteArrayContent(body) : new StreamContent(new TrustedHttp.CappedStream(new MemoryStream(body), long.MaxValue)),
            };

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
