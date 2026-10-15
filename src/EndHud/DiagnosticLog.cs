using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text;

namespace EndHud;

/// <summary>
/// EndHud's log, for finding out what went wrong on a player's PC without guessing: what the PC is,
/// which window EndHud follows, and every step of reading the game that failed, hung or was slow.
/// Players send it with the rest of <see cref="DiagnosticExport"/>.
/// <list type="bullet">
/// <item>One file, <c>logs/endhud.log</c> in the config folder; past <see cref="MaxBytes"/> it becomes
/// <c>endhud.1.log</c> (and that <c>endhud.2.log</c>), so the logs never take more than about 3 MB.</item>
/// <item>The user's own folder is written as <c>%USERPROFILE%</c>, so their Windows account name
/// doesn't travel with the log.</item>
/// <item>A message repeated every reading (a window that sends no frames) is written once per
/// <see cref="Throttled"/> period, with how many times it happened meanwhile.</item>
/// <item>Never throws: a log that can't be written is skipped.</item>
/// </list>
/// </summary>
internal static class DiagnosticLog
{
    public const long MaxBytes = 1024 * 1024;
    public const int KeptFiles = 3;

    private static readonly object Lock = new();
    private static readonly ConcurrentDictionary<string, (DateTime At, int Skipped)> Recent = new();
    private static string _folder = Path.Combine(ConfigStore.Folder, "logs");

    public static string Folder => _folder;
    public static string FilePath => Path.Combine(_folder, "endhud.log");

    /// <summary>Log into another folder (tests).</summary>
    internal static void UseFolder(string folder)
    {
        lock (Lock) _folder = folder;
        Recent.Clear();
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}: {ex}");

    /// <summary>
    /// Write <paramref name="message"/> unless the same <paramref name="key"/> was written in the
    /// last <paramref name="every"/>; the next one written says how many were skipped.
    /// </summary>
    public static void Throttled(string key, TimeSpan every, string message, string level = "WARN")
    {
        var now = DateTime.UtcNow;
        if (Recent.TryGetValue(key, out var last) && now - last.At < every)
        {
            Recent[key] = (last.At, last.Skipped + 1);
            return;
        }
        Recent[key] = (now, 0);
        Write(level, last.Skipped > 0 ? $"{message} (and {last.Skipped} more like it since the last one)" : message);
    }

    /// <summary>Hide the user's own folder (and so their account name) in a message.</summary>
    internal static string Redact(string text)
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrEmpty(profile) ? text : text.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
    }

    private static void Write(string level, string message)
    {
        string line = $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)} {level,-5} [{Environment.CurrentManagedThreadId}] {Redact(message)}{Environment.NewLine}";
        lock (Lock)
        {
            try
            {
                Directory.CreateDirectory(_folder);
                var file = new FileInfo(FilePath);
                if (file.Exists && file.Length + line.Length > MaxBytes) Roll();
                File.AppendAllText(FilePath, line, Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Not being able to log must never stop the HUD.
            }
        }
    }

    private static void Roll()
    {
        for (int i = KeptFiles - 1; i >= 1; i--)
        {
            string from = i == 1 ? FilePath : Path.Combine(_folder, $"endhud.{i - 1}.log");
            string to = Path.Combine(_folder, $"endhud.{i}.log");
            if (File.Exists(from)) File.Move(from, to, overwrite: true);
        }
    }
}
