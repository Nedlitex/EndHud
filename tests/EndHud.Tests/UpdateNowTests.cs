using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using Xunit;

namespace EndHud.Tests;

/// <summary>
/// <c>EndHud.exe --update-now --from &lt;url&gt;</c> (<see cref="UpdateNow"/>): the stand-in for GitHub that
/// release.yml's update test serves on this computer is the only other place an update may come from. Any
/// other program could otherwise make EndHud download and run whatever it names. Nothing reaches the network:
/// the requests the update check would make are counted by a stand-in handler.
/// </summary>
public sealed class UpdateNowTests : IDisposable
{
    private readonly string _work = Path.Combine(Path.GetTempPath(), "endhud-tests", Guid.NewGuid().ToString("N"));
    private readonly Counting _http = new();

    public void Dispose()
    {
        try { Directory.Delete(_work, recursive: true); } catch (IOException) { }
    }

    private Task<int> Run(params string[] args) => UpdateNow.RunAsync(args, _work,
        feed => new UpdateService(() => new HttpClient(_http, disposeHandler: false), new Version(1, 0, 0), feed),
        feed => new Updater(() => new HttpClient(_http, disposeHandler: false), Path.Combine(_work, "EndHud.exe"), _work, InstallKind.Portable, _ => true, feed, elevated: false));

    private string Log => File.ReadAllText(Path.Combine(_work, "update-now.log"));

    [Theory]
    [InlineData("https://evil.example/latest.json")]
    [InlineData("http://evil.example/latest.json")]
    [InlineData("https://api.github.com/repos/someone-else/EndHud/releases/latest")]
    [InlineData("file:///C:/feed/latest.json")]
    [InlineData(@"\\host\share\latest.json")]
    [InlineData("not a url")]
    public async Task A_feed_that_is_not_on_this_computer_is_refused_before_anything_is_asked(string from)
    {
        int exit = await Run(UpdateNow.Flag, "--from", from);

        Assert.NotEqual(0, exit);
        Assert.Equal(0, _http.Requests);
        Assert.Contains("refused --from", Log);
    }

    [Fact]
    public async Task A_from_without_a_value_is_refused_rather_than_ignored()
    {
        int exit = await Run(UpdateNow.Flag, "--from");

        Assert.NotEqual(0, exit);
        Assert.Equal(0, _http.Requests);
    }

    [Theory]
    [InlineData("http://127.0.0.1:8765/latest.json")] // release.yml's stand-in
    [InlineData("https://localhost:8765/latest.json")]
    [InlineData("http://[::1]:8765/latest.json")]
    public async Task The_stand_in_on_this_computer_is_asked(string from)
    {
        int exit = await Run(UpdateNow.Flag, "--from", from);

        Assert.Equal(1, exit); // the stand-in handler has no release: "couldn't check"
        Assert.Equal(1, _http.Requests);
        Assert.Equal(new Uri(from), _http.Last);
    }

    private sealed class Counting : HttpMessageHandler
    {
        public int Requests { get; private set; }
        public Uri? Last { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            Last = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{}") });
        }
    }
}
