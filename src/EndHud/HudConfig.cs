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

    /// <summary>How often the screen is read, in milliseconds (minimum 250). Rotations need it fast:
    /// skill points are spent and refilled quickly.</summary>
    public int OcrIntervalMs { get; set; } = DefaultOcrIntervalMs;

    public const int DefaultOcrIntervalMs = 250;

    /// <summary>
    /// The settings file's version; null in files from before versions were kept. Used to update
    /// old defaults the player never chose: files from before version 2 have the old reading
    /// interval, 1000 ms, which becomes 250.
    /// </summary>
    public int? SettingsVersion { get; set; }

    public const int CurrentSettingsVersion = 3;

    /// <summary>UI language; picks the file i18n/&lt;Language&gt;.xaml.</summary>
    public string Language { get; set; } = "zh-CN";

    /// <summary>Look for a newer EndHud on GitHub soon after start and then daily; the player installs it in one click.</summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>Install a newer EndHud by itself when the game isn't running (see AutoInstaller); needs <see cref="CheckForUpdates"/>.</summary>
    public bool AutoInstallUpdates { get; set; } = true;

    // The debug panel isn't a setting: it's off at every start (OverlayWindow). A "DebugPanel" left
    // in an older config.json is ignored.

    /// <summary>Show a hint when an operator's combo comes off cooldown in combat.</summary>
    public bool ComboHintEnabled { get; set; } = true;

    /// <summary>WhenReady: for <see cref="ComboHintSeconds"/> after the cooldown finishes. WhileReady: until it's used.</summary>
    public ComboHintMode ComboHintMode { get; set; } = ComboHintMode.WhenReady;

    public double ComboHintSeconds { get; set; } = DefaultComboHintSeconds;

    /// <summary>How many seconds before a combo comes off cooldown to show its hint already, counting down; 0: only once ready.</summary>
    public double ComboHintLeadSeconds { get; set; } = DefaultComboHintLeadSeconds;

    /// <summary>Top-left corner of the hints, as a fraction of the game window's width and height.</summary>
    public double ComboHintX { get; set; } = DefaultComboHintX;

    public double ComboHintY { get; set; } = DefaultComboHintY;

    /// <summary>Show the rotation strip (排轴) when the recognised squad has a rotation.</summary>
    public bool RotationEnabled { get; set; } = true;

    /// <summary>
    /// Where the rotation strip sits, as a fraction of the game window: the middle of its width
    /// (so it stays centred as steps come and go), and its top.
    /// </summary>
    public double RotationCenterX { get; set; } = DefaultRotationCenterX;

    public double RotationY { get; set; } = DefaultRotationY;

    /// <summary>
    /// Show the gauges in battle: team buffs such as Link, and each recognised operator's
    /// ultimate charge and own counters, as circles.
    /// </summary>
    public bool GaugesEnabled { get; set; } = true;

    /// <summary>
    /// Where the gauges sit, as a fraction of the game window: the middle of their width, and their
    /// bottom edge, so a bigger size grows upwards, away from the game's own bars below them.
    /// </summary>
    public double GaugesCenterX { get; set; } = DefaultGaugesCenterX;

    public double GaugesBottomY { get; set; } = DefaultGaugesBottomY;

    /// <summary>The gauges' size, on top of following the game window: 1 is the usual size (the menu's Size slider, 50%–200%).</summary>
    public double GaugesScale { get; set; } = 1;

    public const double MinGaugesScale = 0.5, MaxGaugesScale = 2;

    /// <summary>
    /// How far the menu and the debug panel have been dragged from their usual spots (the menu
    /// centred, the debug panel at the left), as a fraction of the game window's width and height.
    /// </summary>
    public double MenuOffsetX { get; set; }

    public double MenuOffsetY { get; set; }

    public double DebugOffsetX { get; set; }

    public double DebugOffsetY { get; set; }

    /// <summary>The rotation chosen for each squad (several can be for the same one), by squad key.</summary>
    public Dictionary<string, string> ActiveRotations { get; set; } = [];

    // Centred, under the boss's name, bars and statuses.
    public const double DefaultRotationCenterX = 0.5;
    public const double DefaultRotationY = 0.13;

    // Bottom middle, just above the game's own HP and skill-point bars.
    public const double DefaultGaugesCenterX = 0.5;
    public const double DefaultGaugesBottomY = 0.885;

    public const double DefaultComboHintSeconds = 3;
    public const double DefaultComboHintLeadSeconds = 1;
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
            var defaults = new HudConfig { SettingsVersion = HudConfig.CurrentSettingsVersion };
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
    internal static HudConfig Parse(string json)
    {
        var config = JsonSerializer.Deserialize<HudConfig>(json, Json) ?? new HudConfig();
        if (config.SettingsVersion is null or < 2)
        {
            // 1000 ms was the default before version 2; rotations need the new default.
            if (config.OcrIntervalMs == 1000) config.OcrIntervalMs = HudConfig.DefaultOcrIntervalMs;
        }
        if (config.SettingsVersion is null or < 3)
        {
            // Up to version 3 every start saved the gauges' size as 50% (the slider's minimum; see
            // OverlayWindow.Gauges), so a 50% then was never the player's choice.
            if (config.GaugesScale == HudConfig.MinGaugesScale) config.GaugesScale = 1;
        }
        config.SettingsVersion = HudConfig.CurrentSettingsVersion;
        // A hand-edited size outside what the slider offers would hide the gauges or fill the screen.
        config.GaugesScale = double.IsFinite(config.GaugesScale)
            ? Math.Clamp(config.GaugesScale, HudConfig.MinGaugesScale, HudConfig.MaxGaugesScale)
            : 1;
        return config;
    }

    internal static string Serialize(HudConfig config) => JsonSerializer.Serialize(config, Json);

    public static void Save(HudConfig config)
    {
        Directory.CreateDirectory(Folder);
        string temp = FilePath + ".tmp";
        File.WriteAllText(temp, Serialize(config));
        File.Move(temp, FilePath, overwrite: true);
    }
}
