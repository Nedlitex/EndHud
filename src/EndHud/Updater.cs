using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using EndHud.Vision;
using Microsoft.Win32;

namespace EndHud;

/// <summary>How this copy of EndHud was installed, which decides how it updates.</summary>
internal enum InstallKind
{
    /// <summary>By the MSI, into %LocalAppData%\Programs\EndHud: updated by running the new MSI quietly.</summary>
    Msi,

    /// <summary>The portable exe, wherever the player put it: updated by swapping the exe.</summary>
    Portable,
}

internal enum UpdateFailure
{
    /// <summary>The release has no file for this kind of install, or no checksum to verify it with.</summary>
    NoPackage,

    /// <summary>The download failed (network).</summary>
    Download,

    /// <summary>The downloaded file isn't what GitHub says it should be (or not EndHud's installer); it was deleted.</summary>
    Checksum,

    /// <summary>The program file couldn't be replaced (portable exe in a read-only place, or in use).</summary>
    Replace,

    /// <summary>EndHud runs as administrator: it doesn't install updates then (see <see cref="Updater"/>).</summary>
    Elevated,
}

/// <param name="Started">The update is under way: EndHud must now exit (the installer, or the new exe, takes over).</param>
internal sealed record UpdateOutcome(bool Started, UpdateFailure? Failure = null, string? Detail = null);

/// <summary>
/// Installs a newer release in place, after the player clicks "update now" (or for
/// <c>EndHud.exe --update-now</c>):
/// <list type="number">
/// <item>Downloads the release's file for this install (<see cref="InstallKind"/>) only from where
/// <see cref="UpdateFeed"/> trusts, into a folder with a random name made for this update, and checks its
/// SHA-256 against the one GitHub reports for it; a mismatch is deleted, never run.</item>
/// <item>Then opens the file so that nothing else can change, replace or delete it, and checks it again
/// through that same handle: what is checked is what runs. An installer must also say it is EndHud's
/// (<see cref="MsiPackage"/>: its upgrade code, name and version).</item>
/// <item><b>MSI install:</b> starts a small hidden script that waits for this EndHud to exit, runs
/// the new MSI quietly (per-user: no administrator prompt; Windows' installed-apps list stays
/// right), and starts EndHud again with <see cref="UpdatedFlag"/> (or <see cref="UpdateFailedFlag"/>
/// if the installer failed, when the old version is still in place). The script and the installer stay
/// locked the same way until the script ends: the script inherits this EndHud's handles on them.</item>
/// <item><b>Portable exe:</b> renames the running exe to <c>EndHud.exe.old</c> (Windows allows
/// renaming a running program, not overwriting it), moves the new one into its place right after its
/// second check, and starts it. The next start deletes the .old file (<see cref="CleanUp"/>).</item>
/// </list>
/// Never while EndHud runs as administrator: the files sit in the player's own temporary folder, where any
/// program of theirs can write, and an elevated EndHud would run what it finds there with full rights.
/// If anything fails before the hand-over, nothing has changed and this version keeps running.
/// </summary>
internal sealed class Updater : IDisposable
{
    public const string UpdatedFlag = "--updated";
    public const string UpdateFailedFlag = "--update-failed";

    /// <summary>Where downloads and the install script go (each update in its own random subfolder); deleted by <see cref="CleanUp"/>.</summary>
    public static string WorkFolder { get; } = Path.Combine(Path.GetTempPath(), "EndHud-update");

    private readonly Func<HttpClient> _http;
    private readonly string _exe;
    private readonly string _work;
    private readonly Func<ProcessStartInfo, bool> _start;
    private readonly UpdateFeed _feed;
    private readonly bool _elevated;

    /// <summary>The verified files, held open (others may read, not write or delete) until disposed or EndHud exits.</summary>
    private readonly List<FileStream> _held = [];

    public InstallKind Kind { get; }

    /// <summary>The most an installer or portable exe may be (a release's are about 100 MB).</summary>
    public const long MaxDownloadBytes = 512L * 1024 * 1024;

    /// <summary>Tests: called with the downloaded file before it is locked and checked again, to change it as another program could.</summary>
    internal Action<string>? BeforeLock { get; init; }

    /// <summary>Updates the running EndHud from <paramref name="feed"/>'s files (GitHub's, or release.yml's stand-in's).</summary>
    public Updater(UpdateFeed feed) : this(
        () => GitHubHttp.Create(TimeSpan.FromMinutes(10)),
        Environment.ProcessPath!, WorkFolder, DetectInstall(Environment.ProcessPath!),
        info => Process.Start(info) is not null, feed, Environment.IsPrivilegedProcess)
    {
    }

    /// <param name="start">Starts a process (tests record it instead).</param>
    /// <param name="feed">Which addresses a release's files may come from (<see cref="UpdateFeed.TrustsDownload"/>).</param>
    /// <param name="elevated">EndHud runs as administrator: nothing is downloaded or installed.</param>
    internal Updater(Func<HttpClient> http, string exe, string work, InstallKind kind, Func<ProcessStartInfo, bool> start, UpdateFeed feed, bool elevated) =>
        (_http, _exe, _work, Kind, _start, _feed, _elevated) = (http, exe, work, kind, start, feed, elevated);

    /// <summary>An MSI install: in the MSI's folder, with the MSI's registry key present.</summary>
    public static InstallKind DetectInstall(string exe)
    {
        string installFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "EndHud");
        bool inInstallFolder = string.Equals(Path.GetDirectoryName(Path.GetFullPath(exe)), installFolder, StringComparison.OrdinalIgnoreCase);
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\EndHud\Installer");
        return inInstallFolder && key?.GetValue("Program") is not null ? InstallKind.Msi : InstallKind.Portable;
    }

    /// <param name="restart">Start EndHud again afterwards (the tray app); false for <c>--update-now</c>.</param>
    public async Task<UpdateOutcome> InstallAsync(ReleaseInfo release, bool restart, CancellationToken ct = default)
    {
        if (_elevated)
        {
            DiagnosticLog.Warn("EndHud update: not installing, EndHud runs as administrator (the update's files sit where any program of the player's can change them)");
            return new UpdateOutcome(false, UpdateFailure.Elevated);
        }
        var asset = Kind == InstallKind.Msi ? release.Msi : release.Portable;
        if (asset?.Sha256 is null || !Uri.TryCreate(asset.Url, UriKind.Absolute, out var url))
            return new UpdateOutcome(false, UpdateFailure.NoPackage);
        if (!_feed.TrustsDownloadStart(url))
        {
            DiagnosticLog.Warn($"EndHud update: refused to download {asset.Name} from {url.Scheme}://{url.Host}: not one of this repository's release downloads");
            return new UpdateOutcome(false, UpdateFailure.NoPackage, url.Host);
        }

        // A new folder with a random name for each update: nothing can be put in place beforehand.
        string run = Path.Combine(_work, "run-" + Path.GetRandomFileName().Replace(".", ""));
        FileStream verified;
        try
        {
            string file = await DownloadAsync(url, asset, run, ct);
            BeforeLock?.Invoke(file);
            verified = Lock(file, asset.Sha256);
            if (Kind == InstallKind.Msi && MsiPackage.Read(file) is var identity && !identity.IsEndHud(release.Version))
            {
                Release(verified);
                File.Delete(file);
                throw new InvalidDataException($"{asset.Name} is not EndHud's installer for {release.Version.ToString(3)} ({identity})");
            }
        }
        catch (InvalidDataException ex)
        {
            DiagnosticLog.Warn($"EndHud update: {ex.Message}");
            DeleteQuietly(run);
            return new UpdateOutcome(false, UpdateFailure.Checksum, ex.Message);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or UnauthorizedAccessException)
        {
            DiagnosticLog.Warn($"EndHud update: download failed: {GitHubHttp.Explain(ex)}");
            DeleteQuietly(run);
            return new UpdateOutcome(false, UpdateFailure.Download, ex.Message);
        }

        return Kind == InstallKind.Msi ? StartMsi(verified, run, release.Version, restart) : SwapPortable(verified, release.Version, restart);
    }

    private async Task<string> DownloadAsync(Uri url, ReleaseAsset asset, string run, CancellationToken ct)
    {
        Directory.CreateDirectory(run);
        string target = Path.Combine(run, Path.GetFileName(asset.Name));
        string partial = target + ".part";

        using (var http = _http())
        {
            // Each redirect is checked too: GitHub sends a release download on to its asset host.
            using var response = await TrustedHttp.GetAsync(http, url, _feed.TrustsDownloadStart,
                request => request.Headers.UserAgent.ParseAdd(ReleaseChecker.UserAgent),
                HttpCompletionOption.ResponseHeadersRead, ct, _feed.TrustsDownload);
            response.EnsureSuccessStatusCode();
            await using var body = await TrustedHttp.OpenCappedAsync(response, MaxDownloadBytes, ct);
            await using var output = File.Create(partial);
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            int read;
            while ((read = await body.ReadAsync(buffer, ct)) > 0)
            {
                sha.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
            }
            string actual = Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
            if (actual != asset.Sha256)
            {
                output.Close();
                File.Delete(partial);
                throw new InvalidDataException($"{asset.Name}: SHA-256 {actual}, expected {asset.Sha256}");
            }
        }

        File.Move(partial, target, overwrite: true);
        return target;
    }

    /// <summary>
    /// Open <paramref name="file"/> so that nothing can write, replace or delete it while it is open (reading is
    /// allowed: the installer must read it), inheritable by the script that runs it, and check its SHA-256 through
    /// this handle. Throws <see cref="InvalidDataException"/> (the file deleted) when it isn't
    /// <paramref name="sha256"/> any more.
    /// </summary>
    private FileStream Lock(string file, string sha256)
    {
        var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Inheritable);
        string actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        stream.Position = 0;
        if (actual == sha256)
        {
            _held.Add(stream);
            return stream;
        }
        stream.Dispose();
        File.Delete(file);
        throw new InvalidDataException($"{Path.GetFileName(file)} changed after it was downloaded: SHA-256 {actual}, expected {sha256}");
    }

    private void Release(FileStream stream)
    {
        _held.Remove(stream);
        stream.Dispose();
    }

    private static void DeleteQuietly(string folder)
    {
        try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>
    /// The script that installs the MSI once EndHud has exited. Paths come in through environment
    /// variables, never through the script text: a batch file can't hold every character a Windows
    /// user name can (Chinese ones included), the environment can.
    /// </summary>
    internal const string InstallScript = """
        @echo off
        rem EndHud update: wait for EndHud to exit, install the new MSI quietly, start EndHud again.
        :wait
        tasklist /fi "PID eq %ENDHUD_PID%" /nh | find " %ENDHUD_PID% " >nul && (ping -n 2 127.0.0.1 >nul & goto wait)
        msiexec /i "%ENDHUD_MSI%" /qn /norestart /l*v "%ENDHUD_MSI_LOG%"
        set result=%errorlevel%
        rem 3010: installed; Windows wants a restart for some file, EndHud's own file is replaced.
        if "%result%"=="3010" set result=0
        >"%ENDHUD_RESULT%" echo %result%
        if not "%ENDHUD_RESTART%"=="1" goto :eof
        if "%result%"=="0" (start "" "%ENDHUD_EXE%" --updated %ENDHUD_VERSION%) else (start "" "%ENDHUD_EXE%" --update-failed %ENDHUD_VERSION%)
        """;

    /// <summary>The installer's exit code is written here (0 = installed), for <c>--update-now</c> and tests.</summary>
    public string ResultFile => Path.Combine(_work, "install-result.txt");

    private UpdateOutcome StartMsi(FileStream msi, string run, Version version, bool restart)
    {
        string script = Path.Combine(run, "install.cmd");
        string text = InstallScript.ReplaceLineEndings("\r\n") + "\r\n";
        try
        {
            File.WriteAllText(script, text);
            File.Delete(ResultFile);
            // Locked like the installer and checked through the lock: cmd.exe reads a script line by line as it runs it.
            Lock(script, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new UpdateOutcome(false, UpdateFailure.Replace, ex.Message);
        }

        var info = new ProcessStartInfo("cmd.exe", $"/d /c \"\"{script}\"\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = run,
        };
        info.Environment["ENDHUD_PID"] = Environment.ProcessId.ToString();
        info.Environment["ENDHUD_MSI"] = msi.Name;
        info.Environment["ENDHUD_MSI_LOG"] = Path.Combine(_work, "install.log");
        info.Environment["ENDHUD_RESULT"] = ResultFile;
        info.Environment["ENDHUD_EXE"] = _exe;
        info.Environment["ENDHUD_VERSION"] = version.ToString(3);
        info.Environment["ENDHUD_RESTART"] = restart ? "1" : "0";
        // Process.Start passes inheritable handles on: the script holds the locks until it ends.
        return _start(info) ? new UpdateOutcome(true) : new UpdateOutcome(false, UpdateFailure.Replace, "cmd.exe");
    }

    private UpdateOutcome SwapPortable(FileStream verified, Version version, bool restart)
    {
        // Windows can't move a file that is open without delete sharing: the lock ends here, and the move follows at once.
        string newExe = verified.Name;
        Release(verified);
        string old = _exe + ".old";
        try
        {
            if (File.Exists(old)) File.Delete(old);
            File.Move(_exe, old); // allowed while it runs
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new UpdateOutcome(false, UpdateFailure.Replace, Path.GetDirectoryName(_exe));
        }

        try
        {
            File.Move(newExe, _exe);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            File.Move(old, _exe); // put this version back
            return new UpdateOutcome(false, UpdateFailure.Replace, Path.GetDirectoryName(_exe));
        }

        if (restart) _start(new ProcessStartInfo(_exe, $"{UpdatedFlag} {version.ToString(3)}") { UseShellExecute = false });
        return new UpdateOutcome(true);
    }

    /// <summary>Let go of the locked files (a started script holds its own handles on them).</summary>
    public void Dispose()
    {
        foreach (var stream in _held) stream.Dispose();
        _held.Clear();
    }

    /// <summary>After an update: delete the old portable exe and the downloads. Never fails.</summary>
    public static void CleanUp(string? exe = null, string? work = null)
    {
        exe ??= Environment.ProcessPath;
        foreach (var action in new Action[]
                 {
                     () => { if (exe is not null && File.Exists(exe + ".old")) File.Delete(exe + ".old"); },
                     () => { if (Directory.Exists(work ?? WorkFolder)) Directory.Delete(work ?? WorkFolder, recursive: true); },
                 })
        {
            try { action(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
