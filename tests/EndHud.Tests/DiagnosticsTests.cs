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
[Collection(WpfCollection.Name)]
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

    /// <remarks>
    /// The log is one per process, and other test classes run alongside this one: what the code they
    /// test logs lands in the same file (on CI, six lines in the middle of this test). So the key and
    /// the message are this test's own, and only its lines are counted.
    /// </remarks>
    [Fact]
    public void A_message_repeated_every_reading_is_written_once_per_period_with_a_count()
    {
        string key = "test-repeat-" + Guid.NewGuid().ToString("N"), message = "no picture " + key;
        for (int i = 0; i < 5; i++) DiagnosticLog.Throttled(key, TimeSpan.FromHours(1), message);
        DiagnosticLog.Throttled(key, TimeSpan.Zero, message);

        var lines = File.ReadAllLines(DiagnosticLog.FilePath).Where(l => l.Contains(key)).ToList();
        Assert.Equal(2, lines.Count);
        Assert.EndsWith(message, lines[0]);
        Assert.EndsWith(message + " (and 4 more like it since the last one)", lines[1]);
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
    public async Task The_export_holds_summary_logs_settings_and_the_hud_corners_with_the_uid_blacked_out()
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
        Assert.Equal(["config.json", "hud.jpg", "logs/endhud.log", "recent/readings.txt", "summary.txt"],
            archive.Entries.Select(e => e.FullName.Replace('\\', '/')).Order());
        using (var readings = new StreamReader(archive.GetEntry("recent/readings.txt")!.Open()))
            Assert.Equal("21:05:07.000: panel not seen, 0/8 marks, from window capture", readings.ReadToEnd().Trim());
        using (var log = new StreamReader(archive.GetEntry("logs/endhud.log")!.Open()))
            Assert.Contains("reading on", log.ReadToEnd());
        string extracted = Path.Combine(_folder, "hud.jpg");
        archive.GetEntry("hud.jpg")!.ExtractToFile(extracted);
        var picture = await ImageFile.LoadAsync(extracted);
        // The squad corner (4K x 0–960, rows 1640–2160) then the skill corner, side by side: the
        // UID's middle (4K 350, 2116) is black, the rest white (JPEG: near enough).
        double s = picture.Width / 1920.0;
        Assert.InRange(picture.GetPixel((int)(350 * s), (int)((2116 - 1640) * s)).R, 0, 40);
        Assert.InRange(picture.GetPixel((int)(350 * s), (int)((2060 - 1640) * s)).R, 215, 255);
        Assert.InRange(picture.GetPixel((int)(1500 * s), (int)(200 * s)).R, 215, 255);
    }

    /// <summary>
    /// A player couldn't upload a 19 KB zip, so the export stays under 20 KB: with the logs at their
    /// largest (three files, about 3 MB) and a real 4K combat picture, the end of the log (its last
    /// line) and the HUD corners still fit, the start of the log doesn't.
    /// </summary>
    [Fact]
    public async Task The_export_stays_under_20_KB_with_the_logs_full_and_keeps_their_end()
    {
        Directory.CreateDirectory(DiagnosticLog.Folder);
        var random = new Random(7);
        foreach (string name in new[] { "endhud.2.log", "endhud.1.log", "endhud.log" })
        {
            var text = new System.Text.StringBuilder();
            for (int i = 0; text.Length < DiagnosticLog.MaxBytes; i++)
                text.Append($"2026-10-22 18:{i % 60:D2}:{random.Next(60):D2}.{random.Next(1000):D3} INFO  [{random.Next(30)}] {name} line {i}: no squad panel for {random.Next(500)} readings: picture 2560x1440, mean brightness {random.Next(200)}, {random.Next(9)}% black; {random.Next(9)}/8 panel marks").AppendLine();
            File.WriteAllText(Path.Combine(DiagnosticLog.Folder, name), (name == "endhud.2.log" ? "THE FIRST LINE" + Environment.NewLine : "") + text + (name == "endhud.log" ? "THE LAST LINE" + Environment.NewLine : ""));
        }
        string config = Path.Combine(_folder, "config.json");
        File.WriteAllText(config, "{ \"PartyOcrEnabled\": true }");
        var game = await ImageFile.LoadAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "combat-combos-nearly-refilled.jpg"));
        string zip = Path.Combine(_folder, "out", "diagnostics.zip");

        await OnSta(() => DiagnosticExport.WriteAsync(zip, "game window: 3840x2160", game, config, []));

        Assert.InRange(new FileInfo(zip).Length, 1, DiagnosticExport.MaxZipBytes);
        using var archive = ZipFile.OpenRead(zip);
        Assert.NotNull(archive.GetEntry("hud.jpg"));
        Assert.NotNull(archive.GetEntry("summary.txt"));
        using var log = new StreamReader(archive.GetEntry("logs/endhud.log")!.Open());
        string kept = log.ReadToEnd();
        Assert.Contains("THE LAST LINE", kept);
        Assert.DoesNotContain("THE FIRST LINE", kept);
        Assert.StartsWith("(earlier lines left out", kept);
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
        // Every connection, not just the first: the HTTP client may try again on a new connection
        // when the first is dropped before any answer, and one the server never accepts waits for
        // the client's timeout instead (a TaskCanceledException after 30 s, seen on a CI runner).
        // On a thread of its own, not the thread pool: on a busy runner a pooled accept loop waited
        // longer than the client's 30 s for its turn (the same TaskCanceledException, at 1 min 20 s).
        var drops = new Thread(() =>
        {
            try
            {
                while (true)
                {
                    using var client = server.AcceptTcpClient();
                    client.Client.LingerState = new System.Net.Sockets.LingerOption(true, 0); // close with a reset
                }
            }
            catch (Exception ex) when (ex is ObjectDisposedException or System.Net.Sockets.SocketException or InvalidOperationException)
            {
                // the server stopped
            }
        }) { IsBackground = true, Name = "drop every connection" };
        drops.Start();
        try
        {
            using var http = GitHubHttp.Create(TimeSpan.FromSeconds(30));
            await Assert.ThrowsAsync<System.Net.Http.HttpRequestException>(() => http.GetAsync($"http://127.0.0.1:{port}/repos/x/contents/operators"));
        }
        finally
        {
            server.Stop();
            drops.Join(TimeSpan.FromSeconds(10));
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
