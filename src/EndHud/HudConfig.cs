using System.IO;
using System.Text.Json;

namespace EndHud;

/// <summary>User settings, stored as JSON in %APPDATA%\EndHud\config.json.</summary>
internal sealed class HudConfig
{
    /// <summary>
    /// Show the HUD automatically whenever the game window has focus (and hide it when it loses
    /// focus). When off, the HUD only appears after Ctrl+Shift+H or the tray menu.
    /// </summary>
    public bool AutoShowOnGameFocus { get; set; } = true;

    /// <summary>Read operator levels, HP and ping from the squad panel (bottom-left).</summary>
    public bool PartyOcrEnabled { get; set; } = true;

    /// <summary>How often the squad panel is read, in milliseconds (minimum 250).</summary>
    public int OcrIntervalMs { get; set; } = 1000;

    /// <summary>UI language; picks the file i18n/&lt;Language&gt;.xaml.</summary>
    public string Language { get; set; } = "zh-CN";
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
            return JsonSerializer.Deserialize<HudConfig>(File.ReadAllText(FilePath), Json) ?? new HudConfig();
        }
        catch (JsonException)
        {
            // Keep the broken file for the user to fix, and run on defaults.
            File.Copy(FilePath, FilePath + ".invalid", overwrite: true);
            return new HudConfig();
        }
    }

    public static void Save(HudConfig config)
    {
        Directory.CreateDirectory(Folder);
        string temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(config, Json));
        File.Move(temp, FilePath, overwrite: true);
    }
}
