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

public static class AlertFormat
{
    public const string ShareCodePrefix = "EHA1:";

    public static string ToJson(IEnumerable<AlertRule> rules) => "";

    public static IReadOnlyList<AlertRule> FromJson(string json) => [];

    public static string ToShareCode(IEnumerable<AlertRule> rules) => ShareCodePrefix;

    public static IReadOnlyList<AlertRule> FromShareCode(string code) => [];
}

public sealed record AlertLoad(IReadOnlyList<AlertRule> Rules, string? RenamedUnreadable, bool Newer, string? Failed = null);

public sealed record AlertImport(IReadOnlyList<AlertRule> Rules, int Added, int Duplicates, int OverLimit);

public sealed class AlertStore(string file)
{
    public string File => file;

    public AlertLoad Load() => new([], null, false);

    public void Save(IReadOnlyList<AlertRule> rules) { }

    public static IReadOnlyList<AlertRule> Corrected(IReadOnlyList<AlertRule> rules, AlertRule rule) => [.. rules, rule];

    public static IReadOnlyList<AlertRule> Removed(IReadOnlyList<AlertRule> rules, string id) => rules;

    public static AlertImport Import(IReadOnlyList<AlertRule> current, IEnumerable<AlertRule> incoming) => new(current, 0, 0, 0);
}
