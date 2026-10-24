using System.IO;
using System.IO.Compression;
using EndHud.Vision;
using EndHud.Vision.Tests;
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

    /// <summary>Battle-skill key reading in the summary: whether it reads, and per slot how many presses were judged how; nothing else.</summary>
    [Fact]
    public void The_summary_says_only_slot_counts_about_presses()
    {
        var tally = new int[4, Enum.GetValues<EndHud.Vision.Rotations.PressVerdict>().Length];
        for (int slot = 0; slot < 4; slot++)
            for (int verdict = 0; verdict < tally.GetLength(1); verdict++)
                tally[slot, verdict] = slot * 10 + verdict + 1;

        var lines = OverlayWindow.PressLines("listening: keyboard (4 of 4 keys held), PlayStation pads 0 (not opened 0), XInput pads 1, other pads 0; PlayStation reports without buttons 0", tally).ToList();

        Assert.Equal(5, lines.Count);
        var allowed = new System.Text.RegularExpressions.Regex(
            @"^(battle-skill key reading: .*|  slot [1-4] battle skill: counted \d+, cooling \d+, no skill points \d+, repeat \d+, not in battle \d+, empty slot \d+)$");
        Assert.All(lines, line => Assert.Matches(allowed, line));
        Assert.Equal("battle-skill key reading: listening: keyboard (4 of 4 keys held), PlayStation pads 0 (not opened 0), XInput pads 1, other pads 0; PlayStation reports without buttons 0", lines[0]);
        var counted = (int)EndHud.Vision.Rotations.PressVerdict.Counted;
        var repeat = (int)EndHud.Vision.Rotations.PressVerdict.Repeat;
        Assert.Contains($"counted {tally[1, counted]},", lines[2]);
        Assert.Contains($"repeat {tally[3, repeat]},", lines[4]);
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

    /// <summary>
    /// A 16:10 screen: the game's 16:9 picture between black bars. The UID is blacked out where it is
    /// in that picture, 80 px above the frame's bottom bar (a 0.6.4 export showed a player's UID).
    /// </summary>
    [Fact]
    public void Between_black_bars_the_uid_in_the_picture_is_blacked_out()
    {
        var bgra = new byte[2560 * 1600 * 4];
        for (int y = 0; y < 1600; y++)
        for (int x = 0; x < 2560; x++)
        {
            int i = (y * 2560 + x) * 4;
            byte v = y is >= 80 and < 1520 ? (byte)255 : (byte)0;
            bgra[i] = bgra[i + 1] = bgra[i + 2] = v;
            bgra[i + 3] = 255;
        }

        var masked = DiagnosticExport.WithoutUid(new Frame(2560, 1600, bgra));

        var uid = DiagnosticExport.UidArea(2560, 1440);
        Assert.Equal((byte)0, masked.GetPixel(uid.X + uid.Width / 2, 80 + uid.Y + uid.Height / 2).R);
        Assert.Equal((byte)255, masked.GetPixel(1280, 800).R);
    }

    /// <summary>
    /// A 16:10 screen with MSI Afterburner's on-screen display (RivaTuner) in the bottom bar: the bars are still found, so
    /// the UID is blacked out where it is in the picture, in the exported game picture too (2026-10-24 investigation: the
    /// display kept the bars and the mask fell in the bar, the UID showing).
    /// </summary>
    [Fact]
    public async Task With_an_on_screen_display_in_the_bottom_bar_the_uid_is_still_blacked_out()
    {
        var (padded, picture) = SyntheticOsd.Letterboxed(White(2560, 1440), 2560, 1600);
        var frame = SyntheticOsd.Draw(padded, OsdPosition.BottomRight, background: true);
        var uid = DiagnosticExport.UidArea(picture.Width, picture.Height);
        int x = picture.X + uid.X + uid.Width / 2, y = picture.Y + uid.Y + uid.Height / 2;

        var masked = DiagnosticExport.WithoutUid(frame);
        Assert.Equal((byte)0, masked.GetPixel(x, y).R);
        Assert.Equal((byte)255, masked.GetPixel(x, picture.Y + uid.Y - 4).R);

        string config = Path.Combine(Directory.CreateDirectory(_folder).FullName, "config.json");
        File.WriteAllText(config, "{}");
        string zip = Path.Combine(_folder, "out", "osd.zip");
        await OnSta(() => DiagnosticExport.WriteAsync(DiagnosticSize.Full, zip, "game window: 2560x1600", frame, config, []));
        using var archive = ZipFile.OpenRead(zip);
        string extracted = Path.Combine(_folder, "osd-game.png");
        archive.GetEntry("game.png")!.ExtractToFile(extracted);
        Assert.Equal((byte)0, (await ImageFile.LoadAsync(extracted)).GetPixel(x, y).R);
    }

    /// <summary>
    /// A frame that isn't 16:9 whose edges aren't black enough to be bars (something lit across the top one): the whole
    /// frame is read, but the UID is also blacked out where it would be in a letterboxed picture, in case they were bars.
    /// </summary>
    [Fact]
    public void When_bars_are_expected_but_rejected_the_uid_is_blacked_out_where_they_would_put_it()
    {
        var (frame, picture) = SyntheticOsd.Letterboxed(White(2560, 1440), 2560, 1600);
        for (int x = 0; x < frame.Width; x++) frame.Bgra[(10 * frame.Width + x) * 4 + 1] = 200;
        Assert.Equal(new PixelRect(0, 0, 2560, 1600), Letterbox.Content(frame));

        var masked = DiagnosticExport.WithoutUid(frame);

        var inPicture = DiagnosticExport.UidArea(picture.Width, picture.Height);
        Assert.Equal((byte)0, masked.GetPixel(picture.X + inPicture.X + inPicture.Width / 2, picture.Y + inPicture.Y + inPicture.Height / 2).R);
        var inFrame = DiagnosticExport.UidArea(frame.Width, frame.Height);
        Assert.Equal((byte)0, masked.GetPixel(inFrame.X + inFrame.Width / 2, inFrame.Y + inFrame.Height / 2).R);
        Assert.Equal((byte)255, masked.GetPixel(1280, 800).R);
    }

    /// <summary>
    /// MSI Afterburner's on-screen display is drawn by RivaTuner Statistics Server (RTSS) into the game's frames, and a
    /// player reported EndHud working only with it off: the summary says whether RTSS runs and whether its hooks are loaded
    /// into EndHud itself (a process name and EndHud's own modules; nothing of another process is read).
    /// </summary>
    [Fact]
    public void The_summary_says_whether_rivatuner_runs_and_is_hooked_into_endhud()
    {
        string summary = DiagnosticExport.DescribeComputer();

        Assert.Matches(@"(?m)^RTSS \(RivaTuner, Afterburner's on-screen display\): (running|not running); RivaTuner hooks loaded in EndHud: (yes|no)\r?$", summary);
        Assert.Equal("RTSS (RivaTuner, Afterburner's on-screen display): running; RivaTuner hooks loaded in EndHud: yes (RTSSHooks64.dll)",
            DiagnosticExport.DescribeRivaTuner(running: true, ["ntdll.dll", "RTSSHooks64.dll", "EndHud.dll"]));
        Assert.Equal("RTSS (RivaTuner, Afterburner's on-screen display): not running; RivaTuner hooks loaded in EndHud: no",
            DiagnosticExport.DescribeRivaTuner(running: false, ["ntdll.dll"]));
        Assert.Equal("RTSS (RivaTuner, Afterburner's on-screen display): unknown; RivaTuner hooks loaded in EndHud: unknown",
            DiagnosticExport.DescribeRivaTuner(running: null, null));
    }

    private static Frame White(int width, int height)
    {
        var bgra = new byte[width * height * 4];
        Array.Fill(bgra, (byte)255);
        return new Frame(width, height, bgra);
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

        await OnSta(() => DiagnosticExport.WriteAsync(DiagnosticSize.Mini, zip, "game window: 1600x900", new Frame(1600, 900, white), config,
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

        await OnSta(() => DiagnosticExport.WriteAsync(DiagnosticSize.Mini, zip, "game window: 3840x2160", game, config, []));

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

    /// <summary>The full export: every log file whole, the game picture full size and the recent
    /// pictures, each with the UID blacked out; no size limit.</summary>
    [Fact]
    public async Task The_full_export_holds_every_log_whole_and_the_pictures_with_the_uid_blacked_out()
    {
        Directory.CreateDirectory(DiagnosticLog.Folder);
        string older = string.Concat(Enumerable.Range(0, 20000).Select(i => $"older line {i}{Environment.NewLine}"));
        File.WriteAllText(Path.Combine(DiagnosticLog.Folder, "endhud.1.log"), older);
        DiagnosticLog.Info("reading on");
        string config = Path.Combine(_folder, "config.json");
        File.WriteAllText(config, "{ \"PartyOcrEnabled\": true }");
        var white = new byte[1600 * 900 * 4];
        Array.Fill(white, (byte)255);
        string zip = Path.Combine(_folder, "out", "diagnostics.zip");

        await OnSta(() => DiagnosticExport.WriteAsync(DiagnosticSize.Full, zip, "game window: 1600x900", new Frame(1600, 900, white), config,
            [new RecentFrame(new DateTime(2026, 10, 16, 21, 5, 7), new Frame(1600, 900, white), "panel not seen, 0/8 marks, from window capture")]));

        using var archive = ZipFile.OpenRead(zip);
        Assert.Equal(["config.json", "game.png", "hud.jpg", "logs/endhud.1.log", "logs/endhud.log", "recent/01-210507.png", "recent/readings.txt", "summary.txt"],
            archive.Entries.Select(e => e.FullName.Replace('\\', '/')).Order());
        using (var log = new StreamReader(archive.GetEntry("logs/endhud.1.log")!.Open()))
            Assert.Equal(older, log.ReadToEnd());
        using (var readings = new StreamReader(archive.GetEntry("recent/readings.txt")!.Open()))
            Assert.Equal("01-210507.png: 21:05:07.000, panel not seen, 0/8 marks, from window capture", readings.ReadToEnd().Trim());
        foreach (string entry in new[] { "game.png", "recent/01-210507.png" })
        {
            string extracted = Path.Combine(_folder, Path.GetFileName(entry));
            archive.GetEntry(entry)!.ExtractToFile(extracted);
            var picture = await ImageFile.LoadAsync(extracted);
            var uid = DiagnosticExport.UidArea(picture.Width, picture.Height);
            Assert.Equal((byte)0, picture.GetPixel(uid.X + uid.Width / 2, uid.Y + uid.Height / 2).R);
            Assert.Equal((byte)255, picture.GetPixel(picture.Width / 2, picture.Height / 2).R);
        }
    }

    [Fact]
    public void The_two_sizes_have_their_own_file_names() =>
        Assert.NotEqual(Path.GetFileName(DiagnosticExport.NewPath(DiagnosticSize.Mini))[..23], Path.GetFileName(DiagnosticExport.NewPath(DiagnosticSize.Full))[..23]);

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
