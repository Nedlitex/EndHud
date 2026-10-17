using EndHud.Vision;
using Xunit;

namespace EndHud.Tests;

/// <summary>config.json (<see cref="ConfigStore"/>).</summary>
public class ConfigTests
{
    [Theory]
    [InlineData("{}", 1.0)]
    [InlineData("{\"GaugesScale\": 1.35}", 1.35)]
    [InlineData("{\"GaugesScale\": 10}", 2.0)]
    [InlineData("{\"GaugesScale\": 0}", 0.5)]
    public void The_gauges_size_defaults_to_100_percent_and_a_hand_edited_one_is_kept_in_range(string json, double expected) =>
        Assert.Equal(expected, ConfigStore.Parse(json).GaugesScale);

    /// <summary>Before version 3 every start saved 50% (a slider bug), so 50% from then means the default.</summary>
    [Theory]
    [InlineData("""{ "GaugesScale": 0.5, "SettingsVersion": 2 }""", 1.0)]
    [InlineData("""{ "GaugesScale": 0.5 }""", 1.0)]
    [InlineData("""{ "GaugesScale": 0.75, "SettingsVersion": 2 }""", 0.75)]
    [InlineData("""{ "GaugesScale": 0.5, "SettingsVersion": 3 }""", 0.5)]  // chosen after the fix: kept
    public void A_gauges_size_saved_by_the_slider_bug_goes_back_to_the_default(string json, double expected)
    {
        var config = ConfigStore.Parse(json);
        Assert.Equal(expected, config.GaugesScale);
        Assert.Equal(HudConfig.CurrentSettingsVersion, config.SettingsVersion);
    }

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
