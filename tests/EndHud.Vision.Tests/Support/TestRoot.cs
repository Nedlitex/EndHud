using System.IO;

namespace EndHud.Vision.Tests;

/// <summary>
/// The one folder a test run may write the app's own data into (settings, logs, rotations, alerts, the operator
/// library, the update's downloads): <c>%TEMP%\endhud-tests\run-…</c>, new for every run of a test assembly.
/// Never the player's real <c>%APPDATA%\EndHud</c>: a test's log lines there end up in the owner's diagnostics zips.
/// Shared by both test assemblies (EndHud.Tests links this file).
/// </summary>
internal static class TestRoot
{
    /// <summary>How long a past run's folder is kept (for looking into a failure) before a later run deletes it.</summary>
    public static readonly TimeSpan KeptFor = TimeSpan.FromDays(1);

    /// <summary>Where every run's folder goes.</summary>
    public static string Parent { get; } = Path.Combine(Path.GetTempPath(), "endhud-tests");

    private static readonly Lazy<string> Run = new(Create, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>This run's folder (created on first use, when the runs older than <see cref="KeptFor"/> are deleted).</summary>
    public static string Folder => Run.Value;

    /// <summary>The player's real data folder, which no test may write to (only ever read, to prove that).</summary>
    public static string RealAppFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EndHud");

    /// <summary>The player's real update work folder, which no test may write to or delete.</summary>
    public static string RealUpdateFolder { get; } = Path.Combine(Path.GetTempPath(), "EndHud-update");

    /// <summary>Whether <paramref name="path"/> is <paramref name="folder"/> or inside it.</summary>
    public static bool IsUnder(string path, string folder)
    {
        string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>What picks a folder of the player's own (their data, their temp) rather than taking one it is handed.</summary>
    public static readonly System.Text.RegularExpressions.Regex PlayerFolder = new(
        @"SpecialFolder\.(ApplicationData|LocalApplicationData|CommonApplicationData)|GetTempPath\s*\(|GetTempFileName\s*\(|""%?(APPDATA|LOCALAPPDATA|TEMP|TMP)%?""");

    /// <summary>
    /// The lines of the C# sources under <c>src/<paramref name="project"/></c> that pick one of the player's own
    /// folders (<see cref="PlayerFolder"/>), as "file:line: text"; comments are not looked at.
    /// </summary>
    public static IReadOnlyList<string> PlayerFolderLines(string project)
    {
        string folder = Path.Combine(RepoRoot(), "src", project);
        var found = new List<string>();
        foreach (string file in Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(folder, file);
            if (relative.StartsWith("bin" + Path.DirectorySeparatorChar) || relative.StartsWith("obj" + Path.DirectorySeparatorChar)) continue;
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                string code = lines[i].TrimStart();
                if (code.StartsWith("//")) continue;
                if (PlayerFolder.IsMatch(code)) found.Add($"{relative.Replace('\\', '/')}:{i + 1}: {code}");
            }
        }
        return found;
    }

    public static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "EndHud.sln"))) return dir.FullName;
        throw new DirectoryNotFoundException("EndHud.sln not found above " + AppContext.BaseDirectory);
    }

    private static string Create()
    {
        DeleteOldRuns(Parent, DateTime.UtcNow - KeptFor);
        string folder = Path.Combine(Parent, $"run-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Environment.ProcessId}-{Guid.NewGuid().ToString("N")[..8]}");
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>Delete the run folders under <paramref name="parent"/> last written before <paramref name="before"/>; one in use is skipped.</summary>
    internal static void DeleteOldRuns(string parent, DateTime before)
    {
        if (!Directory.Exists(parent)) return;
        foreach (string run in Directory.GetDirectories(parent, "run-*"))
        {
            try
            {
                if (Directory.GetLastWriteTimeUtc(run) < before) Directory.Delete(run, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
