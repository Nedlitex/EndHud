using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using EndHud.Vision;

namespace EndHud;

/// <summary>User settings, stored as JSON in %APPDATA%\EndHud\config.json.</summary>
internal sealed class HudConfig
{
    /// <summary>
    /// Show the HUD automatically whenever the game window has focus (and hide it when it loses
    /// focus). When off, the HUD only appears after Ctrl+Shift+H or the tray menu.
    /// </summary>
    public bool AutoShowOnGameFocus { get; set; } = true;

    /// <summary>Read the squad panel (bottom-left): HP, combo cooldowns, ultimates, levels.</summary>
    public bool PartyOcrEnabled { get; set; } = true;

    /// <summary>How often the squad panel is read, in milliseconds (minimum 250).</summary>
    public int OcrIntervalMs { get; set; } = 1000;

    /// <summary>UI language; picks the file i18n/&lt;Language&gt;.xaml.</summary>
    public string Language { get; set; } = "zh-CN";

    /// <summary>Look for a newer EndHud on GitHub soon after start and then daily; the player installs it in one click.</summary>
    public bool CheckForUpdates { get; set; } = true;

    // The debug panel isn't a setting: it's off at every start (OverlayWindow). A "DebugPanel" left
    // in an older config.json is ignored.

    /// <summary>Show a hint when an operator's combo comes off cooldown in combat.</summary>
    public bool ComboHintEnabled { get; set; } = true;

    /// <summary>WhenReady: for <see cref="ComboHintSeconds"/> after the cooldown finishes. WhileReady: until it's used.</summary>
    public ComboHintMode ComboHintMode { get; set; } = ComboHintMode.WhenReady;

    public double ComboHintSeconds { get; set; } = DefaultComboHintSeconds;

    /// <summary>Top-left corner of the hints, as a fraction of the game window's width and height.</summary>
    public double ComboHintX { get; set; } = DefaultComboHintX;

    public double ComboHintY { get; set; } = DefaultComboHintY;

    public const double DefaultComboHintSeconds = 3;
    // Left of the screen's centre, where the game shows its own combo banner.
    public const double DefaultComboHintX = 0.2;
    public const double DefaultComboHintY = 0.36;
}

/// <summary>
/// Loads and saves <see cref="HudConfig"/>. The file is created with defaults on first run.
/// Users can edit it by hand (restart to apply) or delete it to reset everything.
/// </summary>
internal static class ConfigStore
{
    public static string Folder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EndHud");

    public static string FilePath { get; } = Path.Combine(Folder, "config.json");

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static HudConfig Load()
    {
        if (!File.Exists(FilePath))
        {
            var defaults = new HudConfig();
            Save(defaults);
            return defaults;
        }

        try
        {
            return Parse(File.ReadAllText(FilePath));
        }
        catch (JsonException)
        {
            // Keep the broken file for the user to fix, and run on defaults.
            File.Copy(FilePath, FilePath + ".invalid", overwrite: true);
            return new HudConfig();
        }
    }

    /// <summary>A config file's text; settings it doesn't mention keep their defaults, and ones EndHud no longer has are ignored.</summary>
    internal static HudConfig Parse(string json) => JsonSerializer.Deserialize<HudConfig>(json, Json) ?? new HudConfig();

    internal static string Serialize(HudConfig config) => JsonSerializer.Serialize(config, Json);

    public static void Save(HudConfig config)
    {
        Directory.CreateDirectory(Folder);
        string temp = FilePath + ".tmp";
        File.WriteAllText(temp, Serialize(config));
        File.Move(temp, FilePath, overwrite: true);
    }
}
