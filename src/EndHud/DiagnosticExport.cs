using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using EndHud.Vision;

namespace EndHud;

/// <summary>
/// "Export diagnostics": one zip a player can send when something doesn't work, so the cause is
/// read rather than guessed. It holds:
/// <list type="bullet">
/// <item><c>summary.txt</c>: EndHud's version, the PC (Windows, CPU, screens, OCR languages), the
/// game window as EndHud sees it, what reading is doing, and the operator library on disk.</item>
/// <item><c>logs/</c>: <see cref="DiagnosticLog"/>'s files.</item>
/// <item><c>config.json</c>: the settings.</item>
/// <item><c>game.png</c>: the last game picture EndHud read, with the player's UID blacked out.</item>
/// </list>
/// Learned portraits and downloaded art stay out (only their counts are listed).
/// </summary>
internal static class DiagnosticExport
{
    /// <summary>Write the zip to <paramref name="zipPath"/>, with the settings in <paramref name="configFile"/>.</summary>
    public static async Task WriteAsync(string zipPath, string summary, Frame? gamePicture, string configFile)
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
            if (gamePicture is not null) await ImageFile.SavePngAsync(WithoutUid(gamePicture), Path.Combine(staging, "game.png"));

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(zipPath))!);
            if (File.Exists(zipPath)) File.Delete(zipPath);
            ZipFile.CreateFromDirectory(staging, zipPath);
        }
        finally
        {
            try { Directory.Delete(staging, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>A file another writer may have open (the log being written).</summary>
    private static void CopyShared(string from, string to)
    {
        using var source = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var target = File.Create(to);
        source.CopyTo(target);
    }

    /// <summary>
    /// Where the game prints the player's UID: bottom left, under the squad panel (at 4K, x 170–560,
    /// 15–95 px above the bottom edge), scaled to the picture like the rest of the HUD.
    /// </summary>
    internal static PixelRect UidArea(int width, int height) =>
        new UiScaler(width, height).BottomLeft(new PixelRect(170, UiScaler.ReferenceHeight - 95, 390, 80)).ClampTo(width, height);

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
    public static string NewPath() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        $"EndHud-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip");

    /// <summary>Show the zip in Explorer, selected.</summary>
    public static void Reveal(string zipPath) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{zipPath}\"") { UseShellExecute = true });
}
