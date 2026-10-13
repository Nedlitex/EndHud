using System.Text.Json;

namespace EndHud.Vision.Operators;

/// <summary>
/// The references used to recognise operators. Two parts:
/// <list type="bullet">
/// <item><b>Reference data</b> (read-only): the roster and each operator's avatar and skill icons,
/// downloaded from the wiki (<see cref="OperatorReference"/>); EndHud ships none of it. In the
/// app it's the user folder's <c>reference/</c>. Without it the library is empty and no operator
/// is recognised.</item>
/// <item><b>User data</b> (the user folder, %APPDATA%\EndHud\operators in the app):
/// <c>learned/&lt;id&gt;/*.png</c> portraits learned from the user's own corrections, and
/// <c>features/</c>, the avatars as described by the character model (a cache, see
/// <see cref="PortraitMatcher.PrepareWikiAvatars"/>).</item>
/// </list>
/// Learned portraits can be corrected (<see cref="Unlearn"/> + learn the right one) or cleared
/// (<see cref="ForgetAllLearned"/>).
/// </summary>
/// <remarks>
/// Layout of a library root (embedded, downloaded, or a plain folder used by tools/tests):
/// <code>
/// operators.json            roster: id, names, avatar file, skill icon files
/// icons/&lt;id&gt;.png            avatar
/// skills/&lt;id&gt;/*.png          battle skill / combo / ultimate icons (Skill-*, Combo-*, Ult-*)
/// learned/&lt;id&gt;/*.png         learned squad portraits (user data)
/// features/*.json           avatars described by the character model (user data, a cache)
/// </code>
/// </remarks>
public sealed class OperatorLibrary
{
    public const string IconFolderName = "icons";
    public const string SkillFolderName = "skills";
    public const string StatusFolderName = "statuses";
    internal const string StatusManifestName = "statuses.json";
    public const string LearnedFolderName = "learned";
    public const string ReferenceFolderName = "reference";
    private const string ManifestName = "operators.json";

    /// <summary>
    /// User-learned portraits kept per operator. Beyond this the oldest is dropped (a size cap,
    /// not the way wrong entries are removed — see <see cref="Unlearn"/>).
    /// </summary>
    private const int MaxLearnedPerOperator = 8;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        // Keep Chinese names readable in operators.json, not escaped as \uXXXX sequences.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All),
    };

    private sealed record Learned(string OperatorId, Frame Portrait, string File);

    private readonly object _lock = new();
    private readonly FolderAssets _reference;
    private readonly string _userFolder;
    private readonly List<Learned> _learned;

    private readonly IReadOnlyList<StatusInfo> _statuses;
    private readonly IReadOnlyDictionary<string, Frame> _statusIcons;

    private OperatorLibrary(FolderAssets reference, string userFolder, IReadOnlyList<OperatorInfo> operators,
        IReadOnlyDictionary<string, Frame> icons, List<Learned> learned,
        IReadOnlyList<StatusInfo> statuses, IReadOnlyDictionary<string, Frame> statusIcons)
    {
        _reference = reference;
        _userFolder = userFolder;
        Operators = operators;
        Icons = icons;
        _learned = learned;
        _statuses = statuses;
        _statusIcons = statusIcons;
    }

    public IReadOnlyList<OperatorInfo> Operators { get; }
    public bool IsEmpty => Operators.Count == 0;
    internal IReadOnlyDictionary<string, Frame> Icons { get; }

    /// <summary>Where the user's data (learned portraits, caches) is kept.</summary>
    public string UserFolder => _userFolder;

    /// <summary>
    /// The app's library: the reference data downloaded into <paramref name="userFolder"/>/reference
    /// (empty if there is none yet), plus the user's learned data.
    /// </summary>
    public static Task<OperatorLibrary> LoadAsync(string userFolder) =>
        LoadAsync(userFolder, Path.Combine(userFolder, ReferenceFolderName));

    /// <summary>A library with its reference data in <paramref name="referenceFolder"/> (tests and tools keep a shared copy).</summary>
    public static Task<OperatorLibrary> LoadAsync(string userFolder, string referenceFolder) =>
        LoadAsync(new FolderAssets(referenceFolder), userFolder);

    private static async Task<OperatorLibrary> LoadAsync(FolderAssets reference, string userFolder)
    {
        var operators = reference.Exists(ManifestName)
            ? JsonSerializer.Deserialize<List<OperatorInfo>>(reference.Read(ManifestName)) ?? []
            : [];

        var icons = new Dictionary<string, Frame>();
        foreach (var op in operators)
        {
            string icon = $"{IconFolderName}/{op.IconFile}";
            if (reference.Exists(icon)) icons[op.Id] = await ImageFile.LoadAsync(reference.Read(icon));
        }
        operators = operators.Where(o => icons.ContainsKey(o.Id)).ToList();
        var known = operators.Select(o => o.Id).ToHashSet();

        var learned = new List<Learned>();
        var user = new FolderAssets(userFolder);
        foreach (string path in user.List(LearnedFolderName))
        {
            if (OperatorIdOf(path) is { } id && known.Contains(id) && path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                string file = Path.Combine(user.Root, path);
                learned.Add(new Learned(id, await ImageFile.LoadAsync(file), file));
            }
        }

        var statusIcons = new Dictionary<string, Frame>();
        var statuses = new List<StatusInfo>();
        foreach (var status in ReadStatuses(reference.Root))
        {
            string icon = $"{StatusFolderName}/{status.IconFile}";
            if (!reference.Exists(icon)) continue;
            statusIcons[status.Id] = await ImageFile.LoadAsync(reference.Read(icon));
            statuses.Add(status);
        }

        return new OperatorLibrary(reference, userFolder, operators, icons, learned, statuses, statusIcons);
    }

    // "learned/Laevatain/2026….png" → "Laevatain"
    private static string? OperatorIdOf(string path)
    {
        var parts = path.Split('/');
        return parts.Length == 3 ? parts[1] : null;
    }

    /// <summary>Status effects with their icons (enemy debuffs, team buffs), from the downloaded library.</summary>
    public IReadOnlyList<StatusInfo> Statuses => _statuses;

    /// <summary>A status's icon, or null if the library has none.</summary>
    public Frame? StatusIcon(string id) => _statusIcons.GetValueOrDefault(id);

    internal static void SaveStatuses(string folder, IEnumerable<StatusInfo> statuses)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, StatusManifestName), JsonSerializer.Serialize(statuses, Json));
    }

    /// <summary>The statuses listed in a library folder (none if it has no list or it can't be read).</summary>
    internal static IReadOnlyList<StatusInfo> ReadStatuses(string folder)
    {
        string path = Path.Combine(folder, StatusManifestName);
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<List<StatusInfo>>(File.ReadAllText(path)) ?? [] : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    internal static void SaveManifest(string folder, IEnumerable<OperatorInfo> operators)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, ManifestName), JsonSerializer.Serialize(operators, Json));
    }

    public OperatorInfo? Find(string id) => Operators.FirstOrDefault(o => o.Id == id);

    /// <summary>The operator's avatar from the roster (the wiki icon), or null if it has none.</summary>
    public Frame? FindAvatar(string id) => Icons.GetValueOrDefault(id);

    /// <summary>Decode one of an operator's skill icons.</summary>
    public Task<Frame> LoadSkillIconAsync(SkillIcon icon) => ImageFile.LoadAsync(_reference.Read(icon.File));

    /// <summary>Snapshot of all learned portraits.</summary>
    internal List<(OperatorInfo Operator, Frame Portrait)> LearnedPortraits()
    {
        lock (_lock)
        {
            return _learned
                .Select(l => (Find(l.OperatorId), l.Portrait))
                .Where(p => p.Item1 is not null)
                .Select(p => (p.Item1!, p.Portrait))
                .ToList();
        }
    }

    public int LearnedCount(string id)
    {
        lock (_lock) return _learned.Count(l => l.OperatorId == id);
    }

    /// <summary>Remember <paramref name="portrait"/> (a squad portrait crop) as <paramref name="op"/>.</summary>
    public async Task LearnAsync(OperatorInfo op, Frame portrait)
    {
        string relative = $"{LearnedFolderName}/{op.Id}/{DateTime.UtcNow:yyyyMMddHHmmssfff}.png";
        string file = Path.Combine(_userFolder, relative.Replace('/', Path.DirectorySeparatorChar));
        await ImageFile.SavePngAsync(portrait, file);

        lock (_lock)
        {
            _learned.Add(new Learned(op.Id, portrait, file));
            var mine = _learned.Where(l => l.OperatorId == op.Id).ToList();
            foreach (var oldest in mine.Take(Math.Max(0, mine.Count - MaxLearnedPerOperator)))
            {
                TryDelete(oldest.File);
                _learned.Remove(oldest);
            }
        }
    }

    /// <summary>Delete the learned portraits of <paramref name="operatorId"/> that <paramref name="isWrong"/> selects.</summary>
    public int Unlearn(string operatorId, Func<Frame, bool> isWrong)
    {
        lock (_lock)
        {
            var wrong = _learned.Where(l => l.OperatorId == operatorId && isWrong(l.Portrait)).ToList();
            foreach (var l in wrong)
            {
                TryDelete(l.File);
                _learned.Remove(l);
            }
            return wrong.Count;
        }
    }

    /// <summary>Forget every learned portrait.</summary>
    public void ForgetAllLearned()
    {
        lock (_lock)
        {
            foreach (var l in _learned) TryDelete(l.File);
            _learned.Clear();
            string dir = Path.Combine(_userFolder, LearnedFolderName);
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { }
    }
}
