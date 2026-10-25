using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
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

    /// <summary>The downloaded file isn't what GitHub says it should be; it was deleted.</summary>
    Checksum,

    /// <summary>The program file couldn't be replaced (portable exe in a read-only place, or in use).</summary>
    Replace,
}

/// <param name="Started">The update is under way: EndHud must now exit (the installer, or the new exe, takes over).</param>
internal sealed record UpdateOutcome(bool Started, UpdateFailure? Failure = null, string? Detail = null);

/// <summary>
/// Installs a newer release in place, after the player clicks "update now" (or for
/// <c>EndHud.exe --update-now</c>):
/// <list type="number">
/// <item>Downloads the release's file for this install (<see cref="InstallKind"/>) and checks its
/// SHA-256 against the one GitHub reports for it; a mismatch is deleted, never run.</item>
/// <item><b>MSI install:</b> starts a small hidden script that waits for this EndHud to exit, runs
/// the new MSI quietly (per-user: no administrator prompt; Windows' installed-apps list stays
/// right), and starts EndHud again with <see cref="UpdatedFlag"/> (or <see cref="UpdateFailedFlag"/>
/// if the installer failed, when the old version is still in place).</item>
/// <item><b>Portable exe:</b> renames the running exe to <c>EndHud.exe.old</c> (Windows allows
/// renaming a running program, not overwriting it), moves the new one into its place, and starts
/// it. The next start deletes the .old file (<see cref="CleanUp"/>).</item>
/// </list>
/// If anything fails before the hand-over, nothing has changed and this version keeps running.
/// </summary>
internal sealed class Updater
{
    public const string UpdatedFlag = "--updated";
    public const string UpdateFailedFlag = "--update-failed";

    /// <summary>Where downloads and the install script go; deleted by <see cref="CleanUp"/>.</summary>
    public static string WorkFolder { get; } = Path.Combine(Path.GetTempPath(), "EndHud-update");

    private readonly Func<HttpClient> _http;
    private readonly string _exe;
    private readonly string _work;
    private readonly Func<ProcessStartInfo, bool> _start;
    private readonly UpdateFeed _feed;

    public InstallKind Kind { get; }

    /// <summary>The most an installer or portable exe may be (a release's are about 100 MB).</summary>
    public const long MaxDownloadBytes = 512L * 1024 * 1024;

    /// <summary>Updates the running EndHud from <paramref name="feed"/>'s files (GitHub's, or release.yml's stand-in's).</summary>
    public Updater(UpdateFeed feed) : this(
        () => GitHubHttp.Create(TimeSpan.FromMinutes(10)),
        Environment.ProcessPath!, WorkFolder, DetectInstall(Environment.ProcessPath!),
        info => Process.Start(info) is not null, feed)
    {
    }

    /// <param name="start">Starts a process (tests record it instead).</param>
    /// <param name="feed">Which addresses a release's files may come from (<see cref="UpdateFeed.TrustsDownload"/>).</param>
    internal Updater(Func<HttpClient> http, string exe, string work, InstallKind kind, Func<ProcessStartInfo, bool> start, UpdateFeed feed) =>
        (_http, _exe, _work, Kind, _start, _feed) = (http, exe, work, kind, start, feed);

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
        var asset = Kind == InstallKind.Msi ? release.Msi : release.Portable;
        if (asset?.Sha256 is null || !Uri.TryCreate(asset.Url, UriKind.Absolute, out var url))
            return new UpdateOutcome(false, UpdateFailure.NoPackage);
        if (!_feed.TrustsDownloadStart(url))
        {
            DiagnosticLog.Warn($"EndHud update: refused to download {asset.Name} from {url.Scheme}://{url.Host}: not one of this repository's release downloads");
            return new UpdateOutcome(false, UpdateFailure.NoPackage, url.Host);
        }

        string file;
        try
        {
            file = await DownloadAsync(url, asset, ct);
        }
        catch (InvalidDataException ex)
        {
            return new UpdateOutcome(false, UpdateFailure.Checksum, ex.Message);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or UnauthorizedAccessException)
        {
            return new UpdateOutcome(false, UpdateFailure.Download, ex.Message);
        }

        return Kind == InstallKind.Msi ? StartMsi(file, release.Version, restart) : SwapPortable(file, release.Version, restart);
    }

    private async Task<string> DownloadAsync(Uri url, ReleaseAsset asset, CancellationToken ct)
    {
        Directory.CreateDirectory(_work);
        string target = Path.Combine(_work, Path.GetFileName(asset.Name));
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

    private UpdateOutcome StartMsi(string msi, Version version, bool restart)
    {
        string script = Path.Combine(_work, "install.cmd");
        File.WriteAllText(script, InstallScript.ReplaceLineEndings("\r\n") + "\r\n");
        File.Delete(ResultFile);

        var info = new ProcessStartInfo("cmd.exe", $"/d /c \"\"{script}\"\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = _work,
        };
        info.Environment["ENDHUD_PID"] = Environment.ProcessId.ToString();
        info.Environment["ENDHUD_MSI"] = msi;
        info.Environment["ENDHUD_MSI_LOG"] = Path.Combine(_work, "install.log");
        info.Environment["ENDHUD_RESULT"] = ResultFile;
        info.Environment["ENDHUD_EXE"] = _exe;
        info.Environment["ENDHUD_VERSION"] = version.ToString(3);
        info.Environment["ENDHUD_RESTART"] = restart ? "1" : "0";
        return _start(info) ? new UpdateOutcome(true) : new UpdateOutcome(false, UpdateFailure.Replace, "cmd.exe");
    }

    private UpdateOutcome SwapPortable(string newExe, Version version, bool restart)
    {
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
