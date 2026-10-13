using EndHud.Vision;
using Xunit;

namespace EndHud.Tests;

/// <summary>config.json (<see cref="ConfigStore"/>).</summary>
public class ConfigTests
{
    [Fact]
    public void The_debug_panel_is_not_a_setting_so_an_old_config_cannot_turn_it_on()
    {
        // A config.json from an earlier EndHud, saved with the debug panel on.
        var config = ConfigStore.Parse("""{ "DebugPanel": true, "ComboHintMode": "WhileReady", "CheckForUpdates": false }""");

        Assert.Equal(ComboHintMode.WhileReady, config.ComboHintMode); // the rest still loads
        Assert.False(config.CheckForUpdates);
        Assert.DoesNotContain("DebugPanel", ConfigStore.Serialize(config)); // and it's dropped when saved
    }

    [Theory]
    [InlineData("""{ "OcrIntervalMs": 1000 }""", 250)]                          // the old default, never chosen
    [InlineData("""{ "OcrIntervalMs": 500 }""", 500)]                           // chosen by the player: kept
    [InlineData("""{ "OcrIntervalMs": 1000, "SettingsVersion": 2 }""", 1000)]   // chosen after the change: kept
    [InlineData("""{ }""", 250)]
    public void The_reading_interval_defaults_to_250ms_and_old_defaults_are_updated(string json, int expected)
    {
        var config = ConfigStore.Parse(json);

        Assert.Equal(expected, config.OcrIntervalMs);
        Assert.Equal(HudConfig.CurrentSettingsVersion, config.SettingsVersion);
    }
}
