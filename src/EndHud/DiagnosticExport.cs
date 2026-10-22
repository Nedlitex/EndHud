using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using EndHud.Vision;

namespace EndHud;

/// <summary>
/// "Export diagnostics": one zip a player can send when something doesn't work, so the cause is
/// read rather than guessed. Two sizes (<see cref="DiagnosticSize"/>): the mini one, for sites that
/// take small files (a 19 KB zip was too much for one), is kept to <see cref="MaxZipBytes"/>; the
/// full one has every log and the pictures, for GitHub or a file share. The mini one holds:
/// <list type="bullet">
/// <item><c>summary.txt</c>: EndHud's version, the PC (Windows, CPU, screens, OCR languages), the
/// game window as EndHud sees it, what reading is doing, and the operator library on disk.</item>
/// <item><c>logs/endhud.log</c>: the end of <see cref="DiagnosticLog"/>'s files, as much as fits.</item>
/// <item><c>config.json</c>: the settings.</item>
/// <item><c>hud.jpg</c>: the bottom corners of the last game picture EndHud read (the squad and the
/// skill buttons), UID blacked out, as large as fits; left out when even the smallest doesn't.</item>
/// <item><c>recent/readings.txt</c>: what was read from each picture over the last ~12 s.</item>
/// </list>
/// The full one holds the summary, the settings and <c>hud.jpg</c> too, with every log file whole
/// (<c>logs/</c>), <c>game.png</c> (the last game picture read, full size) and the pictures read over
/// the last ~12 s in <c>recent/</c>, all with the UID blacked out.
/// Learned portraits and downloaded art stay out (only their counts are listed).
/// </summary>
internal static class DiagnosticExport
{
    /// <summary>The most the zip may weigh.</summary>
    public const long MaxZipBytes = 20 * 1024;

    /// <summary>What the text (summary, settings, log) may take of it, compressed; the rest is for the picture.</summary>
    private const long TextBytes = 11 * 1024;

    /// <summary>The most log kept before shrinking to fit, in characters (the log compresses about 8 to 1).</summary>
    private const int LogTailMax = 160 * 1024;

    /// <summary>The bottom corners, on a 4K screen: the squad (bottom-left) and the skill buttons (bottom-right).</summary>
    private static readonly PixelRect SquadCorner = new(0, 1640, 960, 520), SkillCorner = new(2880, 1640, 960, 520);

    /// <summary>The corner pictures tried, largest first: scale against 4K, then JPEG quality.</summary>
    private static readonly (double Scale, double Quality)[] PictureSizes = [(0.5, 0.6), (0.4, 0.5), (0.3, 0.45), (0.22, 0.4)];

    /// <summary>Write the zip to <paramref name="zipPath"/>, with the settings in <paramref name="configFile"/>.</summary>
    public static async Task WriteAsync(DiagnosticSize size, string zipPath, string summary, Frame? gamePicture, string configFile,
        IReadOnlyList<RecentFrame> recent)
    {
        if (size == DiagnosticSize.Full)
        {
            await WriteFullAsync(zipPath, summary, gamePicture, configFile, recent);
            return;
        }
        string staging = Path.Combine(Path.GetTempPath(), "endhud-diagnostics-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(staging);
            File.WriteAllText(Path.Combine(staging, "summary.txt"), DiagnosticLog.Redact(summary), Encoding.UTF8);
            if (File.Exists(configFile)) CopyShared(configFile, Path.Combine(staging, "config.json"));
            if (recent.Count > 0)
            {
                string folder = Directory.CreateDirectory(Path.Combine(staging, "recent")).FullName;
                var list = new StringBuilder();
                foreach (var frame in recent) list.AppendLine($"{frame.At:HH:mm:ss.fff}: {frame.Read}");
                File.WriteAllText(Path.Combine(folder, "readings.txt"), list.ToString(), Encoding.UTF8);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(zipPath))!);

            // The log's end, halved until the text fits its share.
            string log = ReadLogs();
            if (log.Length > 0)
            {
                string logFile = Path.Combine(Directory.CreateDirectory(Path.Combine(staging, "logs")).FullName, "endhud.log");
                for (int keep = Math.Min(log.Length, LogTailMax); ; keep /= 2)
                {
                    File.WriteAllText(logFile, Tail(log, keep), Encoding.UTF8);
                    if (Zip(staging, zipPath) <= TextBytes || keep < 1024) break;
                }
            }

            // The largest corner picture that still fits.
            string pictureFile = Path.Combine(staging, "hud.jpg");
            if (gamePicture is not null)
            {
                foreach (var (scale, quality) in PictureSizes)
                {
                    SaveJpeg(Corners(WithoutUid(gamePicture), scale), pictureFile, quality);
                    if (Zip(staging, zipPath) <= MaxZipBytes) return;
                }
                File.Delete(pictureFile);
            }
            Zip(staging, zipPath);
        }
        finally
        {
            try { Directory.Delete(staging, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>Everything: every log whole, the full game picture and the recent ones (UID blacked out).</summary>
    private static async Task WriteFullAsync(string zipPath, string summary, Frame? gamePicture, string configFile,
        IReadOnlyList<RecentFrame> recent)
    {
        string staging = Path.Combine(Path.GetTempPath(), "endhud-diagnostics-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(staging);
            File.WriteAllText(Path.Combine(staging, "summary.txt"), DiagnosticLog.Redact(summary), Encoding.UTF8);
            if (Directory.Exists(DiagnosticLog.Folder))
            {
                string logs = Directory.CreateDirectory(Path.Combine(staging, "logs")).FullName;
                foreach (string log in Directory.GetFiles(DiagnosticLog.Folder, "*.log"))
                    CopyShared(log, Path.Combine(logs, Path.GetFileName(log)));
            }
            if (File.Exists(configFile)) CopyShared(configFile, Path.Combine(staging, "config.json"));
            if (gamePicture is not null)
            {
                var picture = WithoutUid(gamePicture);
                await ImageFile.SavePngAsync(picture, Path.Combine(staging, "game.png"));
                SaveJpeg(Corners(picture, PictureSizes[0].Scale), Path.Combine(staging, "hud.jpg"), PictureSizes[0].Quality);
            }
            if (recent.Count > 0)
            {
                string folder = Directory.CreateDirectory(Path.Combine(staging, "recent")).FullName;
                var list = new StringBuilder();
                for (int i = 0; i < recent.Count; i++)
                {
                    string name = $"{i + 1:D2}-{recent[i].At:HHmmss}.png";
                    await ImageFile.SavePngAsync(WithoutUid(recent[i].Picture), Path.Combine(folder, name));
                    list.AppendLine($"{name}: {recent[i].At:HH:mm:ss.fff}, {recent[i].Read}");
                }
                File.WriteAllText(Path.Combine(folder, "readings.txt"), list.ToString(), Encoding.UTF8);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(zipPath))!);
            Zip(staging, zipPath);
        }
        finally
        {
            try { Directory.Delete(staging, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>Save as JPEG at <paramref name="quality"/> (0–1), with WPF's encoder: WinRT's needs its quality in a
    /// property set whose factory belongs to the thread that first made one, and failed once that thread had ended.</summary>
    private static void SaveJpeg(Frame frame, string path, double quality)
    {
        var source = System.Windows.Media.Imaging.BitmapSource.Create(frame.Width, frame.Height, 96, 96,
            System.Windows.Media.PixelFormats.Bgra32, null, frame.Bgra, frame.Width * 4);
        var encoder = new System.Windows.Media.Imaging.JpegBitmapEncoder { QualityLevel = (int)Math.Round(quality * 100) };
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(source));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    /// <summary>Zip <paramref name="folder"/> to <paramref name="zipPath"/>; the zip's size.</summary>
    private static long Zip(string folder, string zipPath)
    {
        if (File.Exists(zipPath)) File.Delete(zipPath);
        ZipFile.CreateFromDirectory(folder, zipPath, CompressionLevel.SmallestSize, includeBaseDirectory: false);
        return new FileInfo(zipPath).Length;
    }

    /// <summary>Every log file, oldest first.</summary>
    private static string ReadLogs()
    {
        if (!Directory.Exists(DiagnosticLog.Folder)) return "";
        var text = new StringBuilder();
        // endhud.2.log, endhud.1.log, then endhud.log (the one being written).
        static int Age(string file) => int.TryParse(Path.GetExtension(Path.GetFileNameWithoutExtension(file)).TrimStart('.'), out int n) ? n : 0;
        foreach (string file in Directory.GetFiles(DiagnosticLog.Folder, "*.log").OrderByDescending(Age))
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            text.Append(reader.ReadToEnd());
        }
        return text.ToString();
    }

    /// <summary>The last <paramref name="chars"/> characters of <paramref name="log"/>, from a line's start.</summary>
    internal static string Tail(string log, int chars)
    {
        if (chars >= log.Length) return log;
        int start = log.IndexOf('\n', log.Length - chars);
        return "(earlier lines left out to keep the file small)" + Environment.NewLine + (start < 0 ? "" : log[(start + 1)..]);
    }

    /// <summary>The squad corner and the skill corner of <paramref name="picture"/> side by side, at <paramref name="scale"/> of 4K.</summary>
    internal static Frame Corners(Frame picture, double scale)
    {
        var ui = new UiScaler(picture.Width, picture.Height);
        int w = (int)(SquadCorner.Width * scale), h = (int)(SquadCorner.Height * scale);
        var left = picture.Crop(ui.BottomLeft(SquadCorner).ClampTo(picture.Width, picture.Height)).ResizeSmooth(w, h);
        var right = picture.Crop(ui.BottomRight(SkillCorner).ClampTo(picture.Width, picture.Height)).ResizeSmooth(w, h);
        var bgra = new byte[2 * w * h * 4];
        for (int y = 0; y < h; y++)
        {
            Buffer.BlockCopy(left.Bgra, y * w * 4, bgra, y * 2 * w * 4, w * 4);
            Buffer.BlockCopy(right.Bgra, y * w * 4, bgra, (y * 2 + 1) * w * 4, w * 4);
        }
        return new Frame(2 * w, h, bgra);
    }

    /// <summary>A file another writer may have open (the log being written).</summary>
    private static void CopyShared(string from, string to)
    {
        using var source = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var target = File.Create(to);
        source.CopyTo(target);
    }

    /// <summary>
    /// Where the game prints the player's UID: bottom left, under the squad panel, scaled to the
    /// picture like the rest of the HUD. At 4K the text is x 220–454 (a 10-digit UID), rows
    /// 2102–2124, in both layouts; the area leaves room for a longer UID. It stops short of the
    /// rows above: with a controller and LB held, slot 4's HP bar sits on rows 2094–2103, and
    /// a picture of the problem needs it.
    /// </summary>
    internal static PixelRect UidArea(int width, int height) =>
        new UiScaler(width, height).BottomLeft(new PixelRect(200, UiScaler.ReferenceHeight - 60, 300, 32)).ClampTo(width, height);

    /// <summary>A copy of <paramref name="picture"/> with the UID area black.</summary>
    internal static Frame WithoutUid(Frame picture)
    {
        var bgra = (byte[])picture.Bgra.Clone();
        var uid = UidArea(picture.Width, picture.Height);
        for (int y = uid.Y; y < uid.Bottom; y++)
        for (int x = uid.X; x < uid.Right; x++)
        {
            int i = (y * picture.Width + x) * 4;
            bgra[i] = bgra[i + 1] = bgra[i + 2] = 0;
            bgra[i + 3] = 255;
        }
        return new Frame(picture.Width, picture.Height, bgra);
    }

    /// <summary>The PC, as far as a diagnosis needs it.</summary>
    public static string DescribeComputer()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"EndHud {ReleaseChecker.DisplayVersion} ({(Environment.ProcessPath is { } exe ? Path.GetFileName(exe) : "?")}, {(Environment.Is64BitProcess ? "64" : "32")}-bit)");
        sb.AppendLine($"Windows {Environment.OSVersion.Version}, {Environment.ProcessorCount} logical processors, culture {System.Globalization.CultureInfo.CurrentUICulture.Name}");
        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
            sb.AppendLine($"screen {screen.DeviceName}: {screen.Bounds.Width}x{screen.Bounds.Height} at ({screen.Bounds.X},{screen.Bounds.Y}){(screen.Primary ? ", primary" : "")}");
        try
        {
            sb.AppendLine("OCR languages: " + string.Join(", ", Windows.Media.Ocr.OcrEngine.AvailableRecognizerLanguages.Select(l => l.LanguageTag)));
        }
        catch (Exception ex)
        {
            sb.AppendLine($"OCR languages: unavailable ({ex.GetType().Name})");
        }
        return sb.ToString();
    }

    /// <summary>The operator library folder: the package, caches and how many portraits were learned (no images).</summary>
    public static string DescribeLibrary(string folder)
    {
        if (!Directory.Exists(folder)) return $"operator library: {folder} doesn't exist{Environment.NewLine}";
        var sb = new StringBuilder($"operator library: {folder}{Environment.NewLine}");
        string package = Path.Combine(folder, Vision.Operators.OperatorLibrary.ReferenceFolderName, Vision.Operators.OperatorPackage.InfoName);
        sb.AppendLine(File.Exists(package) ? $"  package.json: {File.ReadAllText(package).ReplaceLineEndings(" ")}" : "  no package.json");
        string features = Path.Combine(folder, "features");
        if (Directory.Exists(features))
            foreach (var file in new DirectoryInfo(features).GetFiles())
                sb.AppendLine($"  features/{file.Name}: {file.Length} bytes, {file.LastWriteTime:yyyy-MM-dd HH:mm}");
        string learned = Path.Combine(folder, Vision.Operators.OperatorLibrary.LearnedFolderName);
        if (Directory.Exists(learned))
            foreach (var op in new DirectoryInfo(learned).GetDirectories())
                sb.AppendLine($"  learned/{op.Name}: {op.GetFiles("*.png").Length} portraits");
        return sb.ToString();
    }

    /// <summary>A file name on the desktop for a new export.</summary>
    public static string NewPath(DiagnosticSize size) => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        $"EndHud-diagnostics-{(size == DiagnosticSize.Mini ? "mini" : "full")}-{DateTime.Now:yyyyMMdd-HHmmss}.zip");

    /// <summary>Show the zip in Explorer, selected.</summary>
    public static void Reveal(string zipPath) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{zipPath}\"") { UseShellExecute = true });
}

/// <summary>Which diagnostics zip: see <see cref="DiagnosticExport"/>.</summary>
internal enum DiagnosticSize
{
    /// <summary>Under 20 KB: the log's end, the settings, the summary and the HUD corners.</summary>
    Mini,

    /// <summary>Every log whole and the game pictures too: several MB.</summary>
    Full,
}
