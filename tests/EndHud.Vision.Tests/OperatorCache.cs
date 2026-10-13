using EndHud.Vision.Operators;

namespace EndHud.Vision.Tests;

/// <summary>
/// The operator reference data the tests use: the roster, avatars and skill icons from the wiki,
/// kept in the repository's .cache/operator-library (git-ignored: it's game art). A fresh clone
/// downloads it the first time the tests run; CI keeps it in its cache.
/// </summary>
internal static class OperatorCache
{
    private static readonly Lazy<Task<string>> Folder = new(async () =>
    {
        string folder = Path.Combine(RepositoryRoot(), ".cache", "operator-library");
        // Also refresh a copy from before status icons were downloaded (no statuses.json).
        if (!OperatorReference.Exists(folder) || !File.Exists(Path.Combine(folder, "statuses.json")))
        {
            using var http = WikiOperatorSource.CreateHttpClient();
            await OperatorReference.UpdateAsync(folder, http);
        }
        return folder;
    });

    /// <summary>The folder, downloaded first if missing.</summary>
    public static Task<string> FolderAsync() => Folder.Value;

    /// <summary>A library over the cached reference data, with its own (empty) user folder.</summary>
    public static async Task<OperatorLibrary> LoadAsync(string userFolder) =>
        await OperatorLibrary.LoadAsync(userFolder, await FolderAsync());

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "EndHud.sln"))) return dir.FullName;
        throw new DirectoryNotFoundException("EndHud.sln not found above " + AppContext.BaseDirectory);
    }
}
