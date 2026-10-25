using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using Xunit;

namespace EndHud.Tests;

/// <summary>
/// Installing a newer release in place (<see cref="Updater"/>), against a stand-in for GitHub's
/// download server, in a temporary folder playing the install folder. Nothing is really started:
/// the processes the updater would start are recorded.
/// </summary>
[Collection(ProcessStartCollection.Name)]
public sealed class UpdaterTests : IDisposable
{
    private static readonly byte[] OldExe = "old EndHud"u8.ToArray();
    private static readonly byte[] NewExe = "new EndHud 1.4.0"u8.ToArray();

    private readonly string _root = Path.Combine(Path.GetTempPath(), "endhud-tests", Guid.NewGuid().ToString("N"));
    private readonly List<ProcessStartInfo> _started = [];
    private string Exe => Path.Combine(_root, "app", "EndHud.exe");
    private string Work => Path.Combine(_root, "work");

    public UpdaterTests()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Exe)!);
        File.WriteAllBytes(Exe, OldExe);
    }

    private readonly List<Updater> _updaters = [];

    public void Dispose()
    {
        foreach (var updater in _updaters) updater.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private Updater NewUpdater(InstallKind kind, HttpMessageHandler downloads, bool elevated = false, Action<string>? beforeLock = null)
    {
        var updater = new Updater(() => new HttpClient(downloads, disposeHandler: false), Exe, Work, kind, info =>
        {
            _started.Add(info);
            return true;
        }, UpdateFeed.GitHub, elevated) { BeforeLock = beforeLock };
        _updaters.Add(updater);
        return updater;
    }

    private static string Sha256(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private static ReleaseInfo Release(byte[] file, string? sha256) => new(new Version(1, 4, 0), "https://github.com/Nedlitex/EndHud/releases/tag/v1.4.0",
    [
        new ReleaseAsset("EndHud-1.4.0.msi", "https://github.com/Nedlitex/EndHud/releases/download/v1.4.0/EndHud-1.4.0.msi", sha256),
        new ReleaseAsset("EndHud-1.4.0-portable.exe", "https://github.com/Nedlitex/EndHud/releases/download/v1.4.0/EndHud-1.4.0-portable.exe", sha256),
    ]);

    [Fact]
    public async Task A_portable_exe_is_swapped_for_the_new_one_and_started()
    {
        var updater = NewUpdater(InstallKind.Portable, new FakeDownloads(NewExe));

        var outcome = await updater.InstallAsync(Release(NewExe, Sha256(NewExe)), restart: true);

        Assert.True(outcome.Started);
        Assert.Equal(NewExe, File.ReadAllBytes(Exe));
        Assert.Equal(OldExe, File.ReadAllBytes(Exe + ".old")); // the running exe, renamed; deleted on the next start
        var start = Assert.Single(_started);
        Assert.Equal(Exe, start.FileName);
        Assert.Equal("--updated 1.4.0", start.Arguments);
    }

    [Fact]
    public async Task A_download_that_fails_its_checksum_is_deleted_and_nothing_changes()
    {
        var updater = NewUpdater(InstallKind.Portable, new FakeDownloads("tampered"u8.ToArray()));

        var outcome = await updater.InstallAsync(Release(NewExe, Sha256(NewExe)), restart: true);

        Assert.Equal(UpdateFailure.Checksum, outcome.Failure);
        Assert.Equal(OldExe, File.ReadAllBytes(Exe));
        Assert.False(File.Exists(Exe + ".old"));
        Assert.Empty(Directory.GetFiles(Work)); // neither the download nor its partial file is kept
        Assert.Empty(_started);
    }

    [Fact]
    public async Task Without_a_checksum_from_GitHub_nothing_is_downloaded()
    {
        var downloads = new FakeDownloads(NewExe);
        var updater = NewUpdater(InstallKind.Portable, downloads);

        var outcome = await updater.InstallAsync(Release(NewExe, sha256: null), restart: true);

        Assert.Equal(UpdateFailure.NoPackage, outcome.Failure);
        Assert.Equal(0, downloads.Requests);
        Assert.Equal(OldExe, File.ReadAllBytes(Exe));
    }

    [Fact]
    public async Task A_failed_download_changes_nothing()
    {
        var updater = NewUpdater(InstallKind.Portable, new FakeDownloads(NewExe) { Status = HttpStatusCode.BadGateway });

        var outcome = await updater.InstallAsync(Release(NewExe, Sha256(NewExe)), restart: true);

        Assert.Equal(UpdateFailure.Download, outcome.Failure);
        Assert.Equal(OldExe, File.ReadAllBytes(Exe));
        Assert.Empty(_started);
    }

    [Fact]
    public async Task An_exe_that_cant_be_replaced_is_left_as_it_was()
    {
        var updater = NewUpdater(InstallKind.Portable, new FakeDownloads(NewExe));
        ReleaseInfo release = Release(NewExe, Sha256(NewExe));

        UpdateOutcome outcome;
        using (new FileStream(Exe, FileMode.Open, FileAccess.Read, FileShare.Read)) // held open: can't be renamed
            outcome = await updater.InstallAsync(release, restart: true);

        Assert.Equal(UpdateFailure.Replace, outcome.Failure);
        Assert.Equal(OldExe, File.ReadAllBytes(Exe));
        Assert.Empty(_started);
    }

    [Theory]
    [InlineData("http://example.com/EndHud-1.4.0-portable.exe")]  // not HTTPS
    [InlineData("file:///C:/EndHud-1.4.0-portable.exe")]
    public async Task Only_HTTPS_downloads_are_accepted(string url)
    {
        var downloads = new FakeDownloads(NewExe);
        var updater = NewUpdater(InstallKind.Portable, downloads);
        var release = new ReleaseInfo(new Version(1, 4, 0), "https://github.com/x", [new ReleaseAsset("EndHud-1.4.0-portable.exe", url, Sha256(NewExe))]);

        Assert.Equal(UpdateFailure.NoPackage, (await updater.InstallAsync(release, restart: true)).Failure);
        Assert.Equal(0, downloads.Requests);
    }

    [Fact]
    public async Task An_MSI_install_hands_over_to_a_hidden_script_that_installs_quietly_and_restarts()
    {
        byte[] msi = TestMsi.Create(_root);
        var updater = NewUpdater(InstallKind.Msi, new FakeDownloads(msi));

        var outcome = await updater.InstallAsync(Release(msi, Sha256(msi)), restart: true);

        Assert.True(outcome.Started);
        Assert.Equal(OldExe, File.ReadAllBytes(Exe)); // the installer replaces it once EndHud has exited
        var start = Assert.Single(_started);
        Assert.Equal("cmd.exe", start.FileName);
        Assert.True(start.CreateNoWindow);
        Assert.Equal(msi, File.ReadAllBytes(start.Environment["ENDHUD_MSI"]!)); // the verified download
        Assert.Equal(Environment.ProcessId.ToString(), start.Environment["ENDHUD_PID"]);
        Assert.Equal(Exe, start.Environment["ENDHUD_EXE"]);
        Assert.Equal("1.4.0", start.Environment["ENDHUD_VERSION"]);
        Assert.Equal("1", start.Environment["ENDHUD_RESTART"]);

        string script = File.ReadAllText(Path.Combine(start.WorkingDirectory, "install.cmd"));
        Assert.Contains("msiexec /i \"%ENDHUD_MSI%\" /qn /norestart", script);
        Assert.Contains("--updated %ENDHUD_VERSION%", script);
        Assert.Contains("--update-failed %ENDHUD_VERSION%", script);
        Assert.DoesNotContain(_root, script); // paths only through the environment (any characters)
    }

    [Fact]
    public void After_an_update_the_old_exe_and_the_downloads_are_cleaned_up()
    {
        File.WriteAllBytes(Exe + ".old", OldExe);
        Directory.CreateDirectory(Work);
        File.WriteAllBytes(Path.Combine(Work, "EndHud-1.4.0.msi"), NewExe);

        Updater.CleanUp(Exe, Work);

        Assert.False(File.Exists(Exe + ".old"));
        Assert.False(Directory.Exists(Work));
        Assert.True(File.Exists(Exe));
    }

    private sealed class FakeDownloads(byte[] file) : HttpMessageHandler
    {
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(Status) { Content = new ByteArrayContent(file) });
        }
    }
}
