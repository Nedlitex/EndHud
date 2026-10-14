using EndHud.Vision.Operators;

namespace EndHud.Vision.Tests;

/// <summary>
/// The operator reference data the tests use: the repository's operator package (roster, avatars,
/// skill icons, statuses: what EndHud ships), unpacked into the git-ignored .cache/operator-package.
/// </summary>
internal static class OperatorCache
{
    // The repository's operator package (what EndHud ships), unpacked once: no network needed.
    private static readonly Lazy<Task<string>> Folder = new(() =>
    {
        string folder = Path.Combine(RepositoryRoot(), ".cache", "operator-package");
        byte[] package = File.ReadAllBytes(Path.Combine(RepositoryRoot(), "operators", OperatorPackage.FileName));
        if (OperatorPackage.Installed(folder) != OperatorPackage.ReadInfo(package)) OperatorPackage.Install(folder, package);
        return Task.FromResult(folder);
    });

    /// <summary>The folder, downloaded first if missing.</summary>
    public static Task<string> FolderAsync() => Folder.Value;

    /// <summary>A library over the cached reference data, with its own (empty) user folder.</summary>
    public static async Task<OperatorLibrary> LoadAsync(string userFolder) =>
        await OperatorLibrary.LoadAsync(userFolder, await FolderAsync());

    internal static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "EndHud.sln"))) return dir.FullName;
        throw new DirectoryNotFoundException("EndHud.sln not found above " + AppContext.BaseDirectory);
    }
}
