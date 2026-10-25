using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Xunit;

namespace EndHud.Tests;

/// <summary>
/// Between the update's SHA-256 check and the installer running it, nothing may swap the file (<see cref="Updater"/>):
/// each update downloads into a new folder with a random name; the checked file is then held open so that no
/// other program can write, replace or delete it, and checked again through that handle; the script that runs
/// the installer inherits the handle. An installer must say it is EndHud's (<see cref="MsiPackage"/>). Nothing
/// is installed while EndHud runs as administrator. No network; nothing is installed (the start is recorded).
/// </summary>
[Collection(ProcessStartCollection.Name)]
public sealed class UpdateSwapTests : IDisposable
{
    private static readonly byte[] NewExe = "new EndHud 1.4.0"u8.ToArray();

    private readonly string _root = Path.Combine(Path.GetTempPath(), "endhud-tests", Guid.NewGuid().ToString("N"));
    private readonly List<ProcessStartInfo> _started = [];
    private readonly List<Updater> _updaters = [];
    private string Exe => Path.Combine(_root, "app", "EndHud.exe");
    private string Work => Path.Combine(_root, "work");

    public UpdateSwapTests()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Exe)!);
        File.WriteAllBytes(Exe, "old"u8.ToArray());
    }

    public void Dispose()
    {
        foreach (var updater in _updaters) updater.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private Updater NewUpdater(InstallKind kind, byte[] file, bool elevated = false, Action<string>? beforeLock = null)
    {
        var updater = new Updater(() => new HttpClient(new Serve(file)), Exe, Work, kind, info =>
        {
            _started.Add(info);
            return true;
        }, UpdateFeed.GitHub, elevated) { BeforeLock = beforeLock };
        _updaters.Add(updater);
        return updater;
    }

    private static ReleaseInfo Release(byte[] file) => new(new Version(1, 4, 0), "https://github.com/Nedlitex/EndHud/releases/tag/v1.4.0",
    [
        new ReleaseAsset("EndHud-1.4.0.msi", "https://github.com/Nedlitex/EndHud/releases/download/v1.4.0/EndHud-1.4.0.msi", Sha256(file)),
        new ReleaseAsset("EndHud-1.4.0-portable.exe", "https://github.com/Nedlitex/EndHud/releases/download/v1.4.0/EndHud-1.4.0-portable.exe", Sha256(file)),
    ]);

    private static string Sha256(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    [Fact]
    public async Task Each_update_downloads_into_its_own_folder_with_a_name_nobody_can_guess()
    {
        byte[] msi = TestMsi.Create(_root);

        Assert.True((await NewUpdater(InstallKind.Msi, msi).InstallAsync(Release(msi), restart: true)).Started);
        Assert.True((await NewUpdater(InstallKind.Msi, msi).InstallAsync(Release(msi), restart: true)).Started);

        var folders = _started.Select(s => Path.GetDirectoryName(s.Environment["ENDHUD_MSI"])!).ToList();
        Assert.Equal(2, folders.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(folders, f => Assert.Equal(Path.GetFullPath(Work), Path.GetDirectoryName(f)));
        Assert.All(folders, f => Assert.True(Path.GetFileName(f).Length >= 12, f));
        Assert.All(_started, s => Assert.Equal(Path.GetDirectoryName(s.Environment["ENDHUD_MSI"]), s.WorkingDirectory)); // the script beside it
    }

    [Fact]
    public async Task The_checked_installer_and_its_script_cannot_be_changed_or_deleted_until_the_script_has_them()
    {
        byte[] msi = TestMsi.Create(_root);
        Assert.True((await NewUpdater(InstallKind.Msi, msi).InstallAsync(Release(msi), restart: true)).Started);
        var start = Assert.Single(_started);
        string installer = start.Environment["ENDHUD_MSI"]!, script = Path.Combine(start.WorkingDirectory, "install.cmd");

        foreach (string file in new[] { installer, script })
        {
            Assert.Throws<IOException>(() => File.WriteAllBytes(file, "swapped"u8.ToArray()));
            Assert.Throws<IOException>(() => File.Delete(file));
            Assert.Throws<IOException>(() => File.Move(file, file + ".moved"));
            Assert.NotEmpty(File.ReadAllBytes(file)); // reading stays allowed: msiexec and cmd.exe read them
        }
        Assert.Equal(msi, File.ReadAllBytes(installer));
    }

    [Fact]
    public async Task The_held_files_pass_to_the_script_Windows_starts()
    {
        byte[] msi = TestMsi.Create(_root);
        Assert.True((await NewUpdater(InstallKind.Msi, msi).InstallAsync(Release(msi), restart: true)).Started);
        string installer = Assert.Single(_started).Environment["ENDHUD_MSI"]!;

        // A real child started the way the updater starts its script (Process.Start, no shell): it inherits the lock,
        // which then outlives this side letting go of it, as it outlives EndHud's exit.
        using var child = Process.Start(new ProcessStartInfo("cmd.exe", "/d /c ping -n 30 127.0.0.1 >nul") { UseShellExecute = false, CreateNoWindow = true })!;
        try
        {
            foreach (var updater in _updaters) updater.Dispose();
            Assert.Throws<IOException>(() => File.WriteAllBytes(installer, "swapped"u8.ToArray()));
        }
        finally
        {
            child.Kill(entireProcessTree: true);
            child.WaitForExit();
        }
        File.WriteAllBytes(installer, "free again"u8.ToArray()); // nobody holds it once the child is gone
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_file_changed_after_its_download_is_refused_not_run(bool msi)
    {
        var kind = msi ? InstallKind.Msi : InstallKind.Portable;
        byte[] file = msi ? TestMsi.Create(_root) : NewExe;
        var updater = NewUpdater(kind, file, beforeLock: path => File.WriteAllBytes(path, "swapped by another program"u8.ToArray()));

        var outcome = await updater.InstallAsync(Release(file), restart: true);

        Assert.Equal(UpdateFailure.Checksum, outcome.Failure);
        Assert.Contains("changed after it was downloaded", outcome.Detail);
        Assert.Empty(_started);
        Assert.Equal("old"u8.ToArray(), File.ReadAllBytes(Exe));
    }

    [Theory]
    [InlineData("{00000000-0000-0000-0000-000000000000}", "EndHud", "1.4.0")]
    [InlineData(null, "EndHud", "1.4.0")]
    [InlineData(MsiPackage.EndHudUpgradeCode, "Something Else", "1.4.0")]
    [InlineData(MsiPackage.EndHudUpgradeCode, "EndHud", "9.9.9")]
    public async Task An_installer_that_is_not_EndHuds_for_this_version_is_not_run(string? upgradeCode, string name, string version)
    {
        byte[] msi = TestMsi.Create(_root, upgradeCode, name, version);

        var outcome = await NewUpdater(InstallKind.Msi, msi).InstallAsync(Release(msi), restart: true);

        Assert.Equal(UpdateFailure.Checksum, outcome.Failure);
        Assert.Contains("not EndHud's installer", outcome.Detail);
        Assert.Empty(_started);
    }

    [Fact]
    public async Task A_file_that_is_not_an_installer_at_all_is_not_run()
    {
        var outcome = await NewUpdater(InstallKind.Msi, NewExe).InstallAsync(Release(NewExe), restart: true);

        Assert.Equal(UpdateFailure.Checksum, outcome.Failure);
        Assert.Empty(_started);
        Assert.Empty(Directory.GetFiles(Work, "*", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Nothing_is_downloaded_or_installed_while_EndHud_runs_as_administrator(bool msi)
    {
        int requests = 0;
        var updater = new Updater(() => new HttpClient(new Serve(NewExe, () => requests++)), Exe, Work, msi ? InstallKind.Msi : InstallKind.Portable, info =>
        {
            _started.Add(info);
            return true;
        }, UpdateFeed.GitHub, elevated: true);
        _updaters.Add(updater);

        var outcome = await updater.InstallAsync(Release(NewExe), restart: true);

        Assert.Equal(UpdateFailure.Elevated, outcome.Failure);
        Assert.Equal(0, requests);
        Assert.Empty(_started);
    }

    [Fact]
    public async Task Release_yml_s_update_test_on_a_runner_that_is_administrator_still_updates_from_its_stand_in()
    {
        const string local = "http://127.0.0.1:8765/EndHud-1.4.0-portable.exe";
        var updater = new Updater(() => new HttpClient(new Serve(NewExe)), Exe, Work, InstallKind.Portable, info =>
        {
            _started.Add(info);
            return true;
        }, UpdateFeed.Local("http://127.0.0.1:8765/latest.json", out _)!, elevated: true);
        _updaters.Add(updater);
        var release = new ReleaseInfo(new Version(1, 4, 0), "https://github.com/Nedlitex/EndHud/releases",
            [new ReleaseAsset("EndHud-1.4.0-portable.exe", local, Sha256(NewExe))]);

        Assert.True((await updater.InstallAsync(release, restart: false)).Started);
        Assert.Equal(NewExe, File.ReadAllBytes(Exe));
    }

    [Fact]
    public async Task A_process_started_while_a_portable_exe_is_locked_does_not_keep_it_from_moving_into_place()
    {
        Process? child = null;
        var updater = new Updater(() => new HttpClient(new Serve(NewExe)), Exe, Work, InstallKind.Portable, info =>
        {
            _started.Add(info);
            return true;
        }, UpdateFeed.GitHub, elevated: false)
        {
            // Anything EndHud starts meanwhile (a browser, Explorer) inherits whatever is inheritable.
            AfterLock = _ => child = Process.Start(new ProcessStartInfo("cmd.exe", "/d /c ping -n 30 127.0.0.1 >nul") { UseShellExecute = false, CreateNoWindow = true }),
        };
        _updaters.Add(updater);
        try
        {
            var outcome = await updater.InstallAsync(Release(NewExe), restart: false);

            Assert.True(outcome.Started, $"{outcome.Failure} {outcome.Detail}");
            Assert.Equal(NewExe, File.ReadAllBytes(Exe));
        }
        finally
        {
            child?.Kill(entireProcessTree: true);
            child?.WaitForExit();
            child?.Dispose();
        }
    }

    [Fact]
    public void The_refusal_while_administrator_has_its_own_sentence()
    {
        string strings = File.ReadAllText(Path.Combine(InnTestKit.RepositoryRoot(), "src", "EndHud", "i18n", "zh-CN.xaml"));
        Assert.Matches("x:Key=\"Update.Failed.Elevated\">[^<]*管理员[^<]*\\{0\\}", strings);
    }

    private sealed class Serve(byte[] file, Action? asked = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            asked?.Invoke();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(file) });
        }
    }
}
