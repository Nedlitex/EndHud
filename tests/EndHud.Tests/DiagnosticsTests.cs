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
        // The UID's text at 4K: x 220–454, rows 2102–2124 (keyboard-and-mouse and controller).
        Assert.True(uid.X <= 220 * s && uid.Right >= 454 * s, $"x {uid.X}–{uid.Right}");
        Assert.True(uid.Y <= height - (2160 - 2102) * s && uid.Bottom >= height - (2160 - 2125) * s, $"rows {uid.Y}–{uid.Bottom}");
        // …but not the rows read on slot 4's HP bar with a controller and LB held (4K 2094 + 3 to 2094 + 6).
        Assert.True(uid.Y > height - (2160 - 2099) * s, $"starts at row {uid.Y}");
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

        await OnSta(() => DiagnosticExport.WriteAsync(zip, "game window: 1600x900", new Frame(1600, 900, white), config,
            [new RecentFrame(new DateTime(2026, 10, 16, 21, 5, 7), new Frame(1600, 900, white), "panel not seen, 0/8 marks, from window capture")]));

        using var archive = ZipFile.OpenRead(zip);
        Assert.Equal(["config.json", "game.png", "logs/endhud.log", "recent/01-210507.png", "recent/readings.txt", "summary.txt"],
            archive.Entries.Select(e => e.FullName.Replace('\\', '/')).Order());
        using (var readings = new StreamReader(archive.GetEntry("recent/readings.txt")!.Open()))
            Assert.Equal("01-210507.png: 21:05:07.000, panel not seen, 0/8 marks, from window capture", readings.ReadToEnd().Trim());
        string recent = Path.Combine(_folder, "recent.png");
        archive.GetEntry("recent/01-210507.png")!.ExtractToFile(recent);
        var recentPicture = await ImageFile.LoadAsync(recent);
        var recentUid = DiagnosticExport.UidArea(recentPicture.Width, recentPicture.Height);
        Assert.Equal((byte)0, recentPicture.GetPixel(recentUid.X + recentUid.Width / 2, recentUid.Y + recentUid.Height / 2).R);
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
        // A server that accepts the connection and drops it at once: fails the same way everywhere
        // (a closed port was refused at once here but timed out on a CI runner).
        var server = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        server.Start();
        int port = ((System.Net.IPEndPoint)server.LocalEndpoint).Port;
        var drop = Task.Run(async () =>
        {
            using var client = await server.AcceptTcpClientAsync();
            client.Client.LingerState = new System.Net.Sockets.LingerOption(true, 0); // close with a reset
        });
        try
        {
            using var http = GitHubHttp.Create(TimeSpan.FromSeconds(30));
            await Assert.ThrowsAsync<System.Net.Http.HttpRequestException>(() => http.GetAsync($"http://127.0.0.1:{port}/repos/x/contents/operators"));
            await drop;
        }
        finally
        {
            server.Stop();
        }

        string log = File.ReadAllText(DiagnosticLog.FilePath);
        Assert.Contains("GET 127.0.0.1/repos/x/contents/operators: failed after", log);
        // The whole chain of causes, down to the socket's error code.
        // Reset or aborted, depending on timing; either way the socket's code is there.
        Assert.Matches(@"HttpRequestException: .* <- IOException: .* <- SocketException \(Connection(Reset|Aborted), 1005[34]\): ", log);
    }

    [Fact]
    public void The_graphics_description_lists_the_adapters_and_their_screens()
    {
        string graphics = Vision.Capture.GraphicsInfo.Describe(IntPtr.Zero);

        Assert.Contains("adapter 0: ", graphics);
        Assert.Contains("driver ", graphics);
        Assert.DoesNotContain("unavailable", graphics);
    }

    [Fact]
    public void A_missed_panel_on_a_black_picture_is_described_as_black()
    {
        var black = new Frame(1920, 1080, new byte[1920 * 1080 * 4]);
        var slots = Enumerable.Range(1, 4).Select(n => new PartySlot(n, null, null, "", new Frame(1, 1, new byte[4]))).ToList();

        string miss = PartyMonitor.DescribeMiss(black, new PartyState(slots, PanelMarks: 0));

        Assert.StartsWith("picture 1920x1080, mean brightness 0, 100% black; 0/8 panel marks", miss);
        Assert.Contains("1: hp -, level -, combo -, ultimate -", miss);
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
