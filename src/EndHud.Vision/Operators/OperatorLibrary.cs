using System.Text.Json;

namespace EndHud.Vision.Operators;

/// <summary>Where a wiki icon lands inside a squad portrait (shared by all operators: same UI framing).</summary>
/// <param name="Size">Icon edge length in portrait pixels.</param>
public sealed record IconCalibration(int Size, int X, int Y);

/// <summary>
/// The reference portraits used to recognise operators, kept in one folder:
/// <code>
/// operators.json        roster: id, English name, Chinese name, icon file (from the wiki)
/// icons/&lt;id&gt;.png        wiki icon per operator
/// learned/&lt;id&gt;/*.png     in-game squad portraits learned for that operator
/// calibration.json      where wiki icons sit inside a squad portrait
/// </code>
/// Learned portraits are added automatically after a confident match and by the user's
/// corrections, and are what make recognition reliable. They can be corrected (re-assigned to
/// another operator), removed per operator, or cleared entirely by deleting learned/.
/// </summary>
public sealed class OperatorLibrary
{
    public const string IconFolderName = "icons";
    public const string LearnedFolderName = "learned";
    private const string ManifestName = "operators.json";
    private const string CalibrationName = "calibration.json";

    /// <summary>
    /// Learned portraits kept per operator. Beyond this the oldest is dropped (a size cap, not the
    /// way wrong entries are removed — see <see cref="Unlearn"/>).
    /// </summary>
    private const int MaxLearnedPerOperator = 8;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly object _lock = new();
    private readonly Dictionary<string, List<(string Path, Frame Portrait)>> _learned;

    private OperatorLibrary(string folder, IReadOnlyList<OperatorInfo> operators,
        IReadOnlyDictionary<string, Frame> icons, Dictionary<string, List<(string, Frame)>> learned,
        IconCalibration? calibration)
    {
        Folder = folder;
        Operators = operators;
        Icons = icons;
        _learned = learned;
        Calibration = calibration;
    }

    public string Folder { get; }
    public IReadOnlyList<OperatorInfo> Operators { get; }
    public bool IsEmpty => Operators.Count == 0;
    internal IReadOnlyDictionary<string, Frame> Icons { get; }
    public IconCalibration? Calibration { get; private set; }

    public static async Task<OperatorLibrary> LoadAsync(string folder)
    {
        string manifest = Path.Combine(folder, ManifestName);
        var operators = File.Exists(manifest)
            ? JsonSerializer.Deserialize<List<OperatorInfo>>(await File.ReadAllTextAsync(manifest)) ?? []
            : [];

        var icons = new Dictionary<string, Frame>();
        var learned = new Dictionary<string, List<(string, Frame)>>();
        foreach (var op in operators)
        {
            string icon = Path.Combine(folder, IconFolderName, op.IconFile);
            if (File.Exists(icon)) icons[op.Id] = await ImageFile.LoadAsync(icon);

            string dir = Path.Combine(folder, LearnedFolderName, op.Id);
            if (!Directory.Exists(dir)) continue;
            var list = new List<(string, Frame)>();
            foreach (string file in Directory.GetFiles(dir, "*.png").Order())
                list.Add((file, await ImageFile.LoadAsync(file)));
            learned[op.Id] = list;
        }

        string calibrationPath = Path.Combine(folder, CalibrationName);
        var calibration = File.Exists(calibrationPath)
            ? JsonSerializer.Deserialize<IconCalibration>(await File.ReadAllTextAsync(calibrationPath))
            : null;

        return new OperatorLibrary(folder, operators.Where(o => icons.ContainsKey(o.Id)).ToList(), icons, learned,
            calibration);
    }

    /// <summary>
    /// First start: if <paramref name="folder"/> has no roster yet, copy in the library bundled
    /// with EndHud (<paramref name="seedFolder"/>: roster, icons, and portraits learned from
    /// real gameplay). Never touches an existing library — after the first start the user's own
    /// corrections and deletions are what count, and re-seeding would undo them.
    /// </summary>
    /// <returns>True if the seed was copied.</returns>
    public static bool SeedIfMissing(string folder, string seedFolder)
    {
        if (File.Exists(Path.Combine(folder, ManifestName))) return false;
        if (!File.Exists(Path.Combine(seedFolder, ManifestName))) return false;

        foreach (string source in Directory.GetFiles(seedFolder, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(folder, Path.GetRelativePath(seedFolder, source));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (!File.Exists(target)) File.Copy(source, target);
        }
        return true;
    }

    internal static void SaveManifest(string folder, IEnumerable<OperatorInfo> operators)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, ManifestName), JsonSerializer.Serialize(operators, Json));
    }

    public OperatorInfo? Find(string id) => Operators.FirstOrDefault(o => o.Id == id);

    /// <summary>Snapshot of all learned portraits.</summary>
    internal List<(OperatorInfo Operator, Frame Portrait)> LearnedPortraits()
    {
        lock (_lock)
        {
            return _learned
                .SelectMany(kv => kv.Value.Select(p => (Find(kv.Key), p.Portrait)))
                .Where(p => p.Item1 is not null)
                .Select(p => (p.Item1!, p.Portrait))
                .ToList();
        }
    }

    public int LearnedCount(string id)
    {
        lock (_lock) return _learned.TryGetValue(id, out var list) ? list.Count : 0;
    }

    /// <summary>Remember <paramref name="portrait"/> (a squad portrait crop) as <paramref name="op"/>.</summary>
    public async Task LearnAsync(OperatorInfo op, Frame portrait)
    {
        string dir = Path.Combine(Folder, LearnedFolderName, op.Id);
        string path = Path.Combine(dir, $"{DateTime.UtcNow:yyyyMMddHHmmssfff}.png");
        await ImageFile.SavePngAsync(portrait, path);

        lock (_lock)
        {
            if (!_learned.TryGetValue(op.Id, out var list)) _learned[op.Id] = list = [];
            list.Add((path, portrait));
            while (list.Count > MaxLearnedPerOperator)
            {
                TryDelete(list[0].Path);
                list.RemoveAt(0);
            }
        }
    }

    /// <summary>Delete the learned portraits of <paramref name="operatorId"/> that <paramref name="isWrong"/> selects.</summary>
    public int Unlearn(string operatorId, Func<Frame, bool> isWrong)
    {
        lock (_lock)
        {
            if (!_learned.TryGetValue(operatorId, out var list)) return 0;
            var wrong = list.Where(p => isWrong(p.Portrait)).ToList();
            foreach (var p in wrong)
            {
                TryDelete(p.Path);
                list.Remove(p);
            }
            return wrong.Count;
        }
    }

    /// <summary>Forget every learned portrait (wiki icons and roster stay).</summary>
    public void ForgetAllLearned()
    {
        lock (_lock)
        {
            _learned.Clear();
            string dir = Path.Combine(Folder, LearnedFolderName);
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            Calibration = null;
            TryDelete(Path.Combine(Folder, CalibrationName));
        }
    }

    internal void SaveCalibration(IconCalibration calibration)
    {
        lock (_lock)
        {
            Calibration = calibration;
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Path.Combine(Folder, CalibrationName), JsonSerializer.Serialize(calibration, Json));
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { }
    }
}
