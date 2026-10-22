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

    /// <summary>
    /// The setup advice follows the game's own layout (InnAdviceLayout) and the gourmands' panel is gone (owner, live
    /// 2026-10-20: "no longer need the older panel for gourmand"): a config saved with either's place still loads, the
    /// rest kept.
    /// </summary>
    [Fact]
    public void A_config_with_the_old_setup_panels_or_gourmand_panels_place_still_loads()
    {
        var config = ConfigStore.Parse("""{ "InnSetupX": 0.62, "InnSetupBottomY": 0.38, "InnStripX": 0.5, "InnStripY": 0.16, "InnCallX": 0.3 }""");

        Assert.Equal(0.3, config.InnCallX);
        Assert.DoesNotContain(typeof(HudConfig).GetProperties(), p => p.Name.StartsWith("InnSetup", StringComparison.Ordinal) || p.Name is "InnStripX" or "InnStripY");
    }

    /// <summary>
    /// The gourmands' tiles stand at fixed spots (owner, test_yuanyuan8: "make the position FIXED"): a config saved with the
    /// dragged columns' place still loads, the rest kept; the swaps setting, absent from it, is on.
    /// </summary>
    [Fact]
    public void A_config_with_the_gourmands_dragged_place_still_loads_and_swaps_are_on()
    {
        var config = ConfigStore.Parse("""{ "InnGourmandRight": 0.7, "InnGourmandTop": 0.4, "InnCallX": 0.3 }""");

        Assert.Equal(0.3, config.InnCallX);
        Assert.True(config.InnSwapsEnabled);
        Assert.DoesNotContain(typeof(HudConfig).GetProperties(), p => p.Name.StartsWith("InnGourmand", StringComparison.Ordinal));
        Assert.False(ConfigStore.Parse("""{ "InnSwapsEnabled": false }""").InnSwapsEnabled);
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

    /// <summary>
    /// Every start saved the inn's banners' place, so each version's default then is not the player's choice: the combo
    /// hint's place (0.2, 0.36) up to version 3, the middle (0.43, 0.52) in version 4. Both move to the default now; a place
    /// the player dragged to is kept.
    /// </summary>
    [Theory]
    [InlineData("""{ "InnCallX": 0.2, "InnCallY": 0.36 }""", HudConfig.DefaultInnCallX, HudConfig.DefaultInnCallY)]
    [InlineData("""{ "InnCallX": 0.2, "InnCallY": 0.36, "SettingsVersion": 3 }""", HudConfig.DefaultInnCallX, HudConfig.DefaultInnCallY)]
    [InlineData("""{ "InnCallX": 0.43, "InnCallY": 0.52, "SettingsVersion": 4 }""", HudConfig.DefaultInnCallX, HudConfig.DefaultInnCallY)]
    [InlineData("""{ "InnCallX": 0.43, "InnCallY": 0.52, "SettingsVersion": 3 }""", 0.43, 0.52)]  // dragged there before 4: kept
    [InlineData("""{ "InnCallX": 0.6, "InnCallY": 0.3, "SettingsVersion": 4 }""", 0.6, 0.3)]      // dragged: kept
    [InlineData("""{ "InnCallX": 0.2, "InnCallY": 0.36, "SettingsVersion": 5 }""", 0.2, 0.36)]    // chosen in version 5: kept
    [InlineData("""{ }""", HudConfig.DefaultInnCallX, HudConfig.DefaultInnCallY)]
    public void The_inn_banners_old_default_places_move_to_the_new_default(string json, double x, double y)
    {
        var config = ConfigStore.Parse(json);
        Assert.Equal((x, y), (config.InnCallX, config.InnCallY));
        Assert.Equal(HudConfig.CurrentSettingsVersion, config.SettingsVersion);
    }

    /// <summary>The inn helper is 85% opaque by default (owner, 2026-10-22: "make sure the transparency is not 100%"); a saved value is kept.</summary>
    [Fact]
    public void The_inn_helper_is_see_through_by_default_and_a_saved_opacity_is_kept()
    {
        Assert.Equal(0.85, ConfigStore.Parse("{}").InnStripOpacity);
        Assert.Equal(1.0, ConfigStore.Parse("""{ "InnStripOpacity": 1 }""").InnStripOpacity);
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
