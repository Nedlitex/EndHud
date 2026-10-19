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

    [Theory]
    [InlineData("{}", 1.0)]
    [InlineData("{\"RotationScale\": 0.7}", 0.7)]
    [InlineData("{\"RotationScale\": 10}", 2.0)]
    [InlineData("{\"RotationScale\": 0}", 0.5)]
    public void The_rotation_strips_size_defaults_to_100_percent_and_a_hand_edited_one_is_kept_in_range(string json, double expected) =>
        Assert.Equal(expected, ConfigStore.Parse(json).RotationScale);

    [Theory]
    [InlineData("{}", 1.0)]
    [InlineData("{\"RotationOpacity\": 0.6}", 0.6)]
    [InlineData("{\"RotationOpacity\": 3}", 1.0)]
    [InlineData("{\"RotationOpacity\": 0}", 0.2)]
    public void The_rotation_strips_opacity_defaults_to_opaque_and_a_hand_edited_one_is_kept_visible(string json, double expected) =>
        Assert.Equal(expected, ConfigStore.Parse(json).RotationOpacity);

    [Fact]
    public void The_inn_helper_is_on_by_default() => Assert.True(ConfigStore.Parse("{}").InnHelperEnabled);

    [Theory]
    [InlineData("{}", 1.0, 1.0)]
    [InlineData("{\"InnStripScale\": 1.4, \"InnStripOpacity\": 0.5}", 1.4, 0.5)]
    [InlineData("{\"InnStripScale\": 9, \"InnStripOpacity\": 4}", 2.0, 1.0)]
    [InlineData("{\"InnStripScale\": 0, \"InnStripOpacity\": 0}", 0.5, 0.2)]
    public void The_inn_strips_size_and_opacity_default_to_100_percent_and_hand_edited_ones_stay_in_range(string json, double scale, double opacity)
    {
        var config = ConfigStore.Parse(json);

        Assert.Equal(scale, config.InnStripScale);
        Assert.Equal(opacity, config.InnStripOpacity);
    }

    [Theory]
    [InlineData("{}", HudConfig.DefaultInnStripX, HudConfig.DefaultInnStripY)]
    [InlineData("{\"InnStripX\": 0.1, \"InnStripY\": 0.6}", 0.1, 0.6)]
    [InlineData("{\"InnStripX\": -3, \"InnStripY\": 7}", 0.0, 1.0)]
    public void The_inn_strips_place_defaults_to_the_right_and_a_hand_edited_one_stays_on_the_window(string json, double x, double y)
    {
        var config = ConfigStore.Parse(json);

        Assert.Equal(x, config.InnStripX);
        Assert.Equal(y, config.InnStripY);
    }

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
