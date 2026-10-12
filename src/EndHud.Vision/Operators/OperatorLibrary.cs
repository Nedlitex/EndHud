using System.Text.Json;

namespace EndHud.Vision.Operators;

/// <summary>Where a wiki icon lands inside a squad portrait (shared by all operators: same UI framing).</summary>
/// <param name="Size">Icon edge length in portrait pixels.</param>
public sealed record IconCalibration(int Size, int X, int Y);

/// <summary>
/// The references used to recognise operators. Two parts:
/// <list type="bullet">
/// <item><b>Reference data</b> (read-only): the roster, each operator's avatar and skill icons,
/// portraits learned from real gameplay, and the icon calibration. Built into EndHud
/// (<see cref="EmbeddedAssets"/>), or a newer copy downloaded from the wiki into the user
/// folder's <c>reference/</c>.</item>
/// <item><b>User data</b> (the user folder, %APPDATA%\EndHud\operators in the app):
/// <c>learned/&lt;id&gt;/*.png</c> portraits learned from the user's own corrections,
/// <c>calibration.json</c>, and <c>suppressed.json</c> — built-in portraits the user has
/// unlearned (they can't be deleted from the executable, so they're switched off here).</item>
/// </list>
/// Every learned portrait, built-in or not, can be corrected (<see cref="Unlearn"/> + learn the
/// right one) or cleared (<see cref="ForgetAllLearned"/>).
/// </summary>
/// <remarks>
/// Layout of a library root (embedded, downloaded, or a plain folder used by tools/tests):
/// <code>
/// operators.json            roster: id, names, avatar file, skill icon files
/// icons/&lt;id&gt;.png            avatar
/// skills/&lt;id&gt;/*.png          battle skill / combo / ultimate icons (Skill-*, Combo-*, Ult-*)
/// learned/&lt;id&gt;/*.png         learned squad portraits
/// calibration.json          where avatars sit inside a squad portrait
/// </code>
/// </remarks>
public sealed class OperatorLibrary
{
    public const string IconFolderName = "icons";
    public const string SkillFolderName = "skills";
    public const string LearnedFolderName = "learned";
    public const string ReferenceFolderName = "reference";
    private const string ManifestName = "operators.json";
    private const string CalibrationName = "calibration.json";
    private const string SuppressedName = "suppressed.json";

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

    private sealed record Learned(string Key, string OperatorId, Frame Portrait, string? UserFile);

    private readonly object _lock = new();
    private readonly IAssetSource _reference;
    private readonly string _userFolder;
    private readonly List<Learned> _learned;
    private readonly HashSet<string> _suppressed;
    private readonly IconCalibration? _referenceCalibration;
    private IconCalibration? _userCalibration;

    private OperatorLibrary(IAssetSource reference, string userFolder, IReadOnlyList<OperatorInfo> operators,
        IReadOnlyDictionary<string, Frame> icons, List<Learned> learned, HashSet<string> suppressed,
        IconCalibration? referenceCalibration, IconCalibration? userCalibration)
    {
        _reference = reference;
        _userFolder = userFolder;
        Operators = operators;
        Icons = icons;
        _learned = learned;
        _suppressed = suppressed;
        _referenceCalibration = referenceCalibration;
        _userCalibration = userCalibration;
    }

    public IReadOnlyList<OperatorInfo> Operators { get; }
    public bool IsEmpty => Operators.Count == 0;
    internal IReadOnlyDictionary<string, Frame> Icons { get; }

    /// <summary>The user's own calibration if any, else the built-in one.</summary>
    public IconCalibration? Calibration
    {
        get { lock (_lock) return _userCalibration ?? _referenceCalibration; }
    }

    /// <summary>True when the reference data is a downloaded update rather than the built-in copy.</summary>
    public bool UsesDownloadedReference => _reference is FolderAssets;

    /// <summary>
    /// The app's library: built-in reference data (or the user's downloaded update in
    /// <paramref name="userFolder"/>/reference), plus the user's learned data.
    /// </summary>
    public static Task<OperatorLibrary> LoadAsync(string userFolder, bool includeBuiltInLearned = true)
    {
        string downloaded = Path.Combine(userFolder, ReferenceFolderName);
        IAssetSource reference = File.Exists(Path.Combine(downloaded, ManifestName))
            ? new FolderAssets(downloaded)
            : EmbeddedAssets.Instance;
        return LoadAsync(reference, userFolder, includeBuiltInLearned);
    }

    /// <summary>A self-contained library folder (reference and learned data together), as the tools use.</summary>
    public static Task<OperatorLibrary> LoadFolderAsync(string folder) =>
        LoadAsync(new FolderAssets(folder), folder, includeReferenceLearned: false);

    private static async Task<OperatorLibrary> LoadAsync(IAssetSource reference, string userFolder, bool includeReferenceLearned)
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
        if (includeReferenceLearned)
        {
            foreach (string path in reference.List(LearnedFolderName))
            {
                if (OperatorIdOf(path) is { } id && known.Contains(id))
                    learned.Add(new Learned("builtin:" + path, id, await ImageFile.LoadAsync(reference.Read(path)), null));
            }
        }
        var user = new FolderAssets(userFolder);
        foreach (string path in user.List(LearnedFolderName))
        {
            if (OperatorIdOf(path) is { } id && known.Contains(id) && path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                string file = Path.Combine(user.Root, path);
                learned.Add(new Learned("user:" + path, id, await ImageFile.LoadAsync(file), file));
            }
        }

        var suppressed = user.Exists(SuppressedName)
            ? JsonSerializer.Deserialize<HashSet<string>>(user.Read(SuppressedName)) ?? []
            : [];

        var referenceCalibration = reference.Exists(CalibrationName)
            ? JsonSerializer.Deserialize<IconCalibration>(reference.Read(CalibrationName))
            : null;
        // In a self-contained folder the reference and user calibration are the same file.
        var userCalibration = reference is FolderAssets f && f.Root == user.Root
            ? null
            : user.Exists(CalibrationName) ? JsonSerializer.Deserialize<IconCalibration>(user.Read(CalibrationName)) : null;

        return new OperatorLibrary(reference, userFolder, operators, icons, learned, suppressed,
            referenceCalibration, userCalibration);
    }

    // "learned/Laevatain/2026….png" → "Laevatain"
    private static string? OperatorIdOf(string path)
    {
        var parts = path.Split('/');
        return parts.Length == 3 ? parts[1] : null;
    }

    internal static void SaveManifest(string folder, IEnumerable<OperatorInfo> operators)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, ManifestName), JsonSerializer.Serialize(operators, Json));
    }

    public OperatorInfo? Find(string id) => Operators.FirstOrDefault(o => o.Id == id);

    /// <summary>Decode one of an operator's skill icons.</summary>
    public Task<Frame> LoadSkillIconAsync(SkillIcon icon) => ImageFile.LoadAsync(_reference.Read(icon.File));

    /// <summary>Snapshot of all active learned portraits (built-in ones the user hasn't unlearned, plus the user's).</summary>
    internal List<(OperatorInfo Operator, Frame Portrait)> LearnedPortraits()
    {
        lock (_lock)
        {
            return _learned
                .Where(l => !_suppressed.Contains(l.Key))
                .Select(l => (Find(l.OperatorId), l.Portrait))
                .Where(p => p.Item1 is not null)
                .Select(p => (p.Item1!, p.Portrait))
                .ToList();
        }
    }

    public int LearnedCount(string id)
    {
        lock (_lock) return _learned.Count(l => l.OperatorId == id && !_suppressed.Contains(l.Key));
    }

    /// <summary>Remember <paramref name="portrait"/> (a squad portrait crop) as <paramref name="op"/>.</summary>
    public async Task LearnAsync(OperatorInfo op, Frame portrait)
    {
        string relative = $"{LearnedFolderName}/{op.Id}/{DateTime.UtcNow:yyyyMMddHHmmssfff}.png";
        string file = Path.Combine(_userFolder, relative.Replace('/', Path.DirectorySeparatorChar));
        await ImageFile.SavePngAsync(portrait, file);

        lock (_lock)
        {
            _learned.Add(new Learned("user:" + relative, op.Id, portrait, file));
            var mine = _learned.Where(l => l.OperatorId == op.Id && l.UserFile is not null).ToList();
            foreach (var oldest in mine.Take(Math.Max(0, mine.Count - MaxLearnedPerOperator)))
            {
                TryDelete(oldest.UserFile!);
                _learned.Remove(oldest);
            }
        }
    }

    /// <summary>
    /// Remove the learned portraits of <paramref name="operatorId"/> that <paramref name="isWrong"/>
    /// selects: the user's are deleted, built-in ones are switched off for this user.
    /// </summary>
    public int Unlearn(string operatorId, Func<Frame, bool> isWrong)
    {
        lock (_lock)
        {
            var wrong = _learned.Where(l => l.OperatorId == operatorId && !_suppressed.Contains(l.Key) && isWrong(l.Portrait)).ToList();
            foreach (var l in wrong) Remove(l);
            SaveSuppressed();
            return wrong.Count;
        }
    }

    /// <summary>Forget every learned portrait, built-in ones included, and the user's calibration.</summary>
    public void ForgetAllLearned()
    {
        lock (_lock)
        {
            foreach (var l in _learned.ToList()) Remove(l);
            SaveSuppressed();
            string dir = Path.Combine(_userFolder, LearnedFolderName);
            if (Directory.Exists(dir) && !(_reference is FolderAssets f && f.Root == Path.GetFullPath(_userFolder)))
                Directory.Delete(dir, recursive: true);
            _userCalibration = null;
            if (!(_reference is FolderAssets f2 && f2.Root == Path.GetFullPath(_userFolder)))
                TryDelete(Path.Combine(_userFolder, CalibrationName));
        }
    }

    internal void SaveCalibration(IconCalibration calibration)
    {
        lock (_lock)
        {
            _userCalibration = calibration;
            Directory.CreateDirectory(_userFolder);
            File.WriteAllText(Path.Combine(_userFolder, CalibrationName), JsonSerializer.Serialize(calibration, Json));
        }
    }

    // Caller holds _lock.
    private void Remove(Learned learned)
    {
        if (learned.UserFile is { } file)
        {
            TryDelete(file);
            _learned.Remove(learned);
        }
        else
        {
            _suppressed.Add(learned.Key);
        }
    }

    // Caller holds _lock.
    private void SaveSuppressed()
    {
        if (_suppressed.Count == 0 && !File.Exists(Path.Combine(_userFolder, SuppressedName))) return;
        Directory.CreateDirectory(_userFolder);
        File.WriteAllText(Path.Combine(_userFolder, SuppressedName), JsonSerializer.Serialize(_suppressed.Order(), Json));
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { }
    }
}
