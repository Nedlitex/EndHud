using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace EndHud.Vision.Alerts;

/// <summary>Why an alerts file or share code couldn't be read.</summary>
public enum AlertReadError
{
    /// <summary>Not EndHud alert rules (not JSON, or JSON of something else, e.g. a rotation).</summary>
    NotAlerts,

    /// <summary>A share code that's cut short or altered.</summary>
    DamagedShareCode,

    /// <summary>Made by a newer EndHud (a format this build doesn't know).</summary>
    NewerFormat,
}

public sealed class AlertReadException(AlertReadError error, string? detail = null)
    : Exception($"{error}{(detail is null ? "" : ": " + detail)}")
{
    public AlertReadError Error { get; } = error;
}

/// <summary>Alert rules as JSON (<c>{ "format": 1, "rules": [ … ] }</c>), and as a share code to paste in a chat.</summary>
public static class AlertFormat
{
    /// <summary>Share codes start with this; the number is the code's own version (rotations' codes start "EHR1:").</summary>
    public const string ShareCodePrefix = "EHA1:";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // Keep Chinese text readable in the file.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All),
    };

    private static readonly JsonSerializerOptions Compact = new(Json) { WriteIndented = false };

    public static string ToJson(IEnumerable<AlertRule> rules)
    {
        var list = rules.ToList();
        return JsonSerializer.Serialize(new AlertFile(AlertFile.FormatNeeded(list), list), Json);
    }

    public static IReadOnlyList<AlertRule> FromJson(string json)
    {
        AlertFile? file;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("format", out var format) || format.ValueKind != JsonValueKind.Number
                || !root.TryGetProperty("rules", out var rules) || rules.ValueKind != JsonValueKind.Array)
                throw new AlertReadException(AlertReadError.NotAlerts);
            if (format.GetInt32() > AlertFile.CurrentFormat)
                throw new AlertReadException(AlertReadError.NewerFormat, $"format {format.GetInt32()}");
            file = root.Deserialize<AlertFile>(Json);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
        {
            throw new AlertReadException(AlertReadError.NotAlerts, ex.Message);
        }
        if (file is null || !file.Readable) throw new AlertReadException(AlertReadError.NotAlerts);
        return file.Rules;
    }

    /// <summary>"EHA1:" + the JSON, compressed, in URL-safe base64 (the rotations' scheme).</summary>
    public static string ToShareCode(IEnumerable<AlertRule> rules)
    {
        using var buffer = new MemoryStream();
        using (var deflate = new DeflateStream(buffer, CompressionLevel.SmallestSize))
        {
            var list = rules.ToList();
            deflate.Write(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new AlertFile(AlertFile.FormatNeeded(list), list), Compact)));
        }
        return ShareCodePrefix + Convert.ToBase64String(buffer.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static IReadOnlyList<AlertRule> FromShareCode(string code)
    {
        code = string.Concat(code.Where(c => !char.IsWhiteSpace(c))); // chats wrap long lines
        if (!code.StartsWith(ShareCodePrefix, StringComparison.Ordinal))
        {
            // A newer code ("EHA2:") is from a newer EndHud; anything else (a rotation's "EHR1:") isn't alert rules.
            throw new AlertReadException(code.StartsWith("EHA", StringComparison.Ordinal) && code.Contains(':')
                ? AlertReadError.NewerFormat
                : AlertReadError.NotAlerts);
        }

        string body = code[ShareCodePrefix.Length..].Replace('-', '+').Replace('_', '/');
        body = body.PadRight(body.Length + (4 - body.Length % 4) % 4, '=');
        string json;
        try
        {
            using var deflate = new DeflateStream(new MemoryStream(Convert.FromBase64String(body)), CompressionMode.Decompress);
            using var reader = new StreamReader(deflate, Encoding.UTF8);
            json = reader.ReadToEnd();
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException)
        {
            throw new AlertReadException(AlertReadError.DamagedShareCode, ex.Message);
        }

        try
        {
            return FromJson(json);
        }
        catch (AlertReadException ex) when (ex.Error == AlertReadError.NotAlerts)
        {
            throw new AlertReadException(AlertReadError.DamagedShareCode, ex.Message);
        }
    }
}

/// <summary>What <see cref="AlertStore.Load"/> found.</summary>
/// <param name="Rules">The rules (none when the file is missing, unreadable, newer or couldn't be opened).</param>
/// <param name="RenamedUnreadable">An unreadable file was renamed to this, to start afresh without losing it.</param>
/// <param name="Newer">The file is from a newer EndHud: left as it is, and saving is refused.</param>
/// <param name="Failed">The file couldn't be opened (locked, no access): why. Saving is refused too, so it isn't overwritten.</param>
public sealed record AlertLoad(IReadOnlyList<AlertRule> Rules, string? RenamedUnreadable, bool Newer, string? Failed = null);

/// <summary>Rules after an import: how many were added, skipped as duplicates, and left out over the 100 cap.</summary>
public sealed record AlertImport(IReadOnlyList<AlertRule> Rules, int Added, int Duplicates, int OverLimit);

/// <summary>
/// The player's alert rules: one file, <c>%APPDATA%\EndHud\alerts.json</c> in the app, global (every fight, every squad).
/// <list type="bullet">
/// <item>Create: the editor's "new" or "copy", or an import; written by <see cref="Save"/> whole (temp file, then rename).</item>
/// <item>Read: <see cref="Load"/>; a missing file is no rules.</item>
/// <item>Correct: <see cref="Corrected"/> rewrites a rule in its place under its own id.</item>
/// <item>Remove: <see cref="Removed"/>, only when the player asks; nothing is removed automatically (no eviction, no age limit).</item>
/// <item>Protect: an unreadable file is renamed, never overwritten or lost; a file from a newer EndHud is
/// never overwritten; imports never replace a rule (<see cref="Import"/>); at most <see cref="AlertFile.MaxRules"/>.</item>
/// <item>Carry: survives updates (the installer and updater never touch %APPDATA%); moves between
/// computers and players by share code or file (<see cref="AlertFormat"/>).</item>
/// </list>
/// </summary>
public sealed class AlertStore(string file)
{
    private bool _refuseSave;

    public string File => file;

    public AlertLoad Load()
    {
        _refuseSave = false;
        if (!System.IO.File.Exists(file)) return new AlertLoad([], null, false);
        string text;
        try
        {
            text = System.IO.File.ReadAllText(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _refuseSave = true;
            return new AlertLoad([], null, false, ex.Message);
        }
        try
        {
            return new AlertLoad(AlertFormat.FromJson(text), null, false);
        }
        catch (AlertReadException ex) when (ex.Error == AlertReadError.NewerFormat)
        {
            _refuseSave = true;
            return new AlertLoad([], null, true);
        }
        catch (AlertReadException)
        {
            // Not lost: kept beside it under a dated name, and the player starts afresh.
            string renamed = UnreadableName();
            try
            {
                System.IO.File.Move(file, renamed);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _refuseSave = true;
                return new AlertLoad([], null, false, ex.Message);
            }
            RenamedEarlier = renamed;
            return new AlertLoad([], renamed, false);
        }
    }

    /// <summary>
    /// An unreadable file this store renamed during this run (null if none): a later load (the editor
    /// opened after the overlay's) can still say where the player's old rules went.
    /// </summary>
    public string? RenamedEarlier { get; private set; }

    private string UnreadableName()
    {
        string folder = Path.GetDirectoryName(file) ?? ".";
        string stem = $"{Path.GetFileNameWithoutExtension(file)}.unreadable-{DateTime.Now:yyyyMMdd-HHmmss}";
        string name = Path.Combine(folder, stem + ".json");
        for (int n = 2; System.IO.File.Exists(name); n++) name = Path.Combine(folder, $"{stem}-{n}.json");
        return name;
    }

    /// <summary>
    /// Writes every rule (the whole file, then renamed over the old one). Refused when the file is from a
    /// newer EndHud or couldn't be opened, and over <see cref="AlertFile.MaxRules"/> rules.
    /// </summary>
    public void Save(IReadOnlyList<AlertRule> rules)
    {
        if (_refuseSave || OnDiskIsNewer())
            throw new InvalidOperationException("The alerts file is from a newer EndHud or couldn't be opened; it is not overwritten.");
        if (rules.Count > AlertFile.MaxRules)
            throw new InvalidOperationException($"At most {AlertFile.MaxRules} alert rules.");
        Directory.CreateDirectory(Path.GetDirectoryName(file) ?? ".");
        string temp = file + ".tmp";
        System.IO.File.WriteAllText(temp, AlertFormat.ToJson(rules));
        System.IO.File.Move(temp, file, overwrite: true);
    }

    private bool OnDiskIsNewer()
    {
        try
        {
            if (!System.IO.File.Exists(file)) return false;
            AlertFormat.FromJson(System.IO.File.ReadAllText(file));
            return false;
        }
        catch (AlertReadException ex)
        {
            return ex.Error == AlertReadError.NewerFormat;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true; // can't tell: don't overwrite
        }
    }

    /// <summary>The rules with <paramref name="rule"/> rewritten in its place (found by its id).</summary>
    public static IReadOnlyList<AlertRule> Corrected(IReadOnlyList<AlertRule> rules, AlertRule rule)
    {
        int at = rules.ToList().FindIndex(r => r.Id == rule.Id);
        if (at < 0) throw new KeyNotFoundException($"No alert rule {rule.Id} to correct.");
        var list = rules.ToList();
        list[at] = rule;
        return list;
    }

    /// <summary>The rules without the one with this id.</summary>
    public static IReadOnlyList<AlertRule> Removed(IReadOnlyList<AlertRule> rules, string id) => rules.Where(r => r.Id != id).ToList();

    /// <summary>
    /// Adds <paramref name="incoming"/> after <paramref name="current"/>, never replacing: every imported rule
    /// gets a new id; one with the same conditions and words as a rule already there is skipped; at
    /// <see cref="AlertFile.MaxRules"/> the rest is left out.
    /// </summary>
    public static AlertImport Import(IReadOnlyList<AlertRule> current, IEnumerable<AlertRule> incoming)
    {
        var merged = current.ToList();
        int added = 0, duplicates = 0, overLimit = 0;
        foreach (var rule in incoming)
        {
            if (merged.Any(r => r.SameAs(rule))) duplicates++;
            else if (merged.Count >= AlertFile.MaxRules) overLimit++;
            else
            {
                merged.Add(rule with { Id = AlertRule.NewId() });
                added++;
            }
        }
        return new AlertImport(merged, added, duplicates, overLimit);
    }
}
