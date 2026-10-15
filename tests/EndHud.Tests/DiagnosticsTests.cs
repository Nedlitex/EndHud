using System.IO;
using System.IO.Compression;
using EndHud.Vision;
using Xunit;

namespace EndHud.Tests;

/// <summary>
/// The diagnostic log and export: the log stays small and leaves out the account name, repeats are
/// collapsed, and the exported zip carries the logs, settings, summary and a game picture without
/// the UID.
/// </summary>
public class DiagnosticsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "endhud-diagnostics-tests-" + Guid.NewGuid().ToString("N")[..8]);

    public DiagnosticsTests() => DiagnosticLog.UseFolder(Path.Combine(_folder, "logs"));

    public void Dispose()
    {
        DiagnosticLog.UseFolder(Path.Combine(ConfigStore.Folder, "logs"));
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void The_log_rolls_over_and_keeps_at_most_three_files()
    {
        string line = new('x', 1000);
        for (int i = 0; i < 3500; i++) DiagnosticLog.Info(line); // about 3.5 MB written

        var files = Directory.GetFiles(DiagnosticLog.Folder).Select(Path.GetFileName).Order().ToList();
        Assert.Equal(["endhud.1.log", "endhud.2.log", "endhud.log"], files);
        Assert.All(Directory.GetFiles(DiagnosticLog.Folder), f => Assert.True(new FileInfo(f).Length <= DiagnosticLog.MaxBytes));
    }

    [Fact]
    public void The_users_folder_is_written_without_their_account_name()
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        DiagnosticLog.Error("couldn't read", new IOException($"{profile}\\AppData\\Roaming\\EndHud\\config.json is locked"));

        string log = File.ReadAllText(DiagnosticLog.FilePath);
        Assert.Contains("%USERPROFILE%\\AppData\\Roaming\\EndHud\\config.json is locked", log);
        Assert.DoesNotContain(profile, log, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_message_repeated_every_reading_is_written_once_per_period_with_a_count()
    {
        for (int i = 0; i < 5; i++) DiagnosticLog.Throttled("no-frames", TimeSpan.FromHours(1), "no picture");
        DiagnosticLog.Throttled("no-frames", TimeSpan.Zero, "no picture");

        var lines = File.ReadAllLines(DiagnosticLog.FilePath);
        Assert.Equal(2, lines.Length);
        Assert.EndsWith("no picture", lines[0]);
        Assert.EndsWith("no picture (and 4 more like it since the last one)", lines[1]);
    }

    [Theory]
    [InlineData(3840, 2160)]
    [InlineData(1600, 900)]
    [InlineData(1280, 720)]
    public void The_uid_area_is_bottom_left_and_scales_with_the_screen(int width, int height)
    {
        var uid = DiagnosticExport.UidArea(width, height);
        double s = width / 3840.0;
        Assert.Equal(170 * s, uid.X, 1.0);
        Assert.Equal(390 * s, uid.Width, 1.0);
        Assert.Equal(height - 15 * s, uid.Bottom, 1.0);
    }

    [Fact]
    public async Task The_export_holds_summary_logs_settings_and_the_picture_with_the_uid_blacked_out()
    {
        DiagnosticLog.Info("reading on");
        string config = Path.Combine(_folder, "config.json");
        File.WriteAllText(config, "{ \"PartyOcrEnabled\": true }");
        var white = new byte[1600 * 900 * 4];
        Array.Fill(white, (byte)255);
        string zip = Path.Combine(_folder, "out", "diagnostics.zip");

        await OnSta(() => DiagnosticExport.WriteAsync(zip, "game window: 1600x900", new Frame(1600, 900, white), config));

        using var archive = ZipFile.OpenRead(zip);
        Assert.Equal(["config.json", "game.png", "logs/endhud.log", "summary.txt"],
            archive.Entries.Select(e => e.FullName.Replace('\\', '/')).Order());
        string extracted = Path.Combine(_folder, "game.png");
        archive.GetEntry("game.png")!.ExtractToFile(extracted);
        var picture = await ImageFile.LoadAsync(extracted);
        var uid = DiagnosticExport.UidArea(1600, 900);
        Assert.Equal((byte)0, picture.GetPixel(uid.X + uid.Width / 2, uid.Y + uid.Height / 2).R);
        Assert.Equal((byte)255, picture.GetPixel(800, 450).R);
        Assert.Equal((byte)255, picture.GetPixel(uid.X + uid.Width / 2, uid.Y - 5).R);
    }

    [Fact]
    public void A_network_failure_is_explained_down_to_its_socket_error()
    {
        var ex = new System.Net.Http.HttpRequestException("The SSL connection could not be established, see inner exception.",
            new IOException("Unable to read data from the transport connection.",
                new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.ConnectionReset)));

        Assert.Equal("HttpRequestException: The SSL connection could not be established, see inner exception. <- " +
                     "IOException: Unable to read data from the transport connection. <- " +
                     $"SocketException (ConnectionReset, 10054): {new System.Net.Sockets.SocketException(10054).Message}",
            GitHubHttp.Explain(ex));
    }

    [Fact]
    public async Task Every_github_request_is_logged_with_its_host_and_why_it_failed()
    {
        using var http = GitHubHttp.Create(TimeSpan.FromSeconds(10));
        // Nothing listens on port 1: refused at once.
        await Assert.ThrowsAsync<System.Net.Http.HttpRequestException>(() => http.GetAsync("http://127.0.0.1:1/repos/x/contents/operators"));

        string log = File.ReadAllText(DiagnosticLog.FilePath);
        Assert.Contains("GET 127.0.0.1/repos/x/contents/operators: failed after", log);
        Assert.Contains("SocketException (ConnectionRefused, 10061)", log);
    }

    private static Task OnSta(Func<Task> work)
    {
        var done = new TaskCompletionSource();
        var thread = new Thread(() =>
        {
            try { work().GetAwaiter().GetResult(); done.SetResult(); }
            catch (Exception ex) { done.SetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }
}
