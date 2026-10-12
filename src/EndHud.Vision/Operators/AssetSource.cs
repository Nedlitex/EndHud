namespace EndHud.Vision.Operators;

/// <summary>Read-only files addressed by '/'-separated paths relative to a library root.</summary>
internal interface IAssetSource
{
    bool Exists(string path);
    byte[] Read(string path);

    /// <summary>Every file under <paramref name="directory"/> (recursively), as root-relative paths.</summary>
    IEnumerable<string> List(string directory);
}

/// <summary>A library folder on disk.</summary>
internal sealed class FolderAssets(string root) : IAssetSource
{
    public string Root { get; } = Path.GetFullPath(root);

    public bool Exists(string path) => File.Exists(Resolve(path));

    public byte[] Read(string path) => File.ReadAllBytes(Resolve(path));

    public IEnumerable<string> List(string directory)
    {
        string dir = Resolve(directory);
        return Directory.Exists(dir)
            ? Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(Root, f).Replace('\\', '/'))
                .Order(StringComparer.Ordinal)
            : [];
    }

    private string Resolve(string path) => Path.Combine(Root, path.Replace('/', Path.DirectorySeparatorChar));
}
