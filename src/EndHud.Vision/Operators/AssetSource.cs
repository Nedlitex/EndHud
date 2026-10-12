using System.Reflection;

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

/// <summary>
/// The library compiled into EndHud.Vision.dll from Assets/operators (see the csproj): operator
/// roster, avatar and skill icons, and portraits learned from real gameplay.
/// </summary>
internal sealed class EmbeddedAssets : IAssetSource
{
    private const string Prefix = "operators/";
    public static readonly EmbeddedAssets Instance = new();

    private readonly Assembly _assembly = typeof(EmbeddedAssets).Assembly;
    private readonly Dictionary<string, string> _resources; // relative path → manifest resource name

    private EmbeddedAssets()
    {
        _resources = _assembly.GetManifestResourceNames()
            .Select(name => (Name: name, Path: name.Replace('\\', '/')))
            .Where(r => r.Path.StartsWith(Prefix, StringComparison.Ordinal))
            .ToDictionary(r => r.Path[Prefix.Length..], r => r.Name);
    }

    public bool Exists(string path) => _resources.ContainsKey(path);

    public byte[] Read(string path)
    {
        using var stream = _assembly.GetManifestResourceStream(_resources[path])
                           ?? throw new FileNotFoundException($"Embedded asset {path} is missing.");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    public IEnumerable<string> List(string directory)
    {
        string prefix = directory.TrimEnd('/') + "/";
        return _resources.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal);
    }
}
