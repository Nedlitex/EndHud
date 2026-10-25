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

    /// <summary>Alert pop-ups ship on: an older config.json without the setting has them on; the rules list decides what shows.</summary>
    [Fact]
    public void Alerts_are_on_by_default_and_an_old_config_has_them_on()
    {
        Assert.True(new HudConfig().AlertsEnabled);
        Assert.True(ConfigStore.Parse("""{ "SettingsVersion": 6, "ComboHintX": 0.3 }""").AlertsEnabled);
        Assert.False(ConfigStore.Parse("""{ "AlertsEnabled": false }""").AlertsEnabled);
        Assert.Contains("\"AlertsEnabled\": true", ConfigStore.Serialize(new HudConfig()));
    }

    [Theory]
    [InlineData("{}", 1.0, 0.85)]
    [InlineData("{\"InnStripScale\": 1.4, \"InnStripOpacity\": 0.5}", 1.4, 0.5)]
    [InlineData("{\"InnStripScale\": 9, \"InnStripOpacity\": 4}", 2.0, 1.0)]
    [InlineData("{\"InnStripScale\": 0, \"InnStripOpacity\": 0}", 0.5, 0.2)]
    public void The_inn_strips_size_defaults_to_100_percent_its_opacity_to_85_and_hand_edited_ones_stay_in_range(string json, double scale, double opacity)
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
    [InlineData("""{ "InnCallX": 0.2, "InnCallY": 0.36, "SettingsVersion": 6 }""", 0.2, 0.36)]    // chosen in version 6: kept
    [InlineData("""{ }""", HudConfig.DefaultInnCallX, HudConfig.DefaultInnCallY)]
    public void The_inn_banners_old_default_places_move_to_the_new_default(string json, double x, double y)
    {
        var config = ConfigStore.Parse(json);
        Assert.Equal((x, y), (config.InnCallX, config.InnCallY));
        Assert.Equal(HudConfig.CurrentSettingsVersion, config.SettingsVersion);
    }

    /// <summary>
    /// The inn helper is 85% opaque by default (owner, 2026-10-22: "make sure the transparency is not 100%"). The first start
    /// writes the whole config, so every file from before version 6 says 100% (the old default), never the player's choice:
    /// it becomes 85%. Until 2026-10-22 it was kept, so no player upgrading ever saw the new default (v0.6.2 also wrote it back
    /// as version 5). Another value, or 100% chosen in version 6, is kept.
    /// </summary>
    [Theory]
    [InlineData("""{ }""", 0.85)]
    [InlineData("""{ "InnStripOpacity": 1 }""", 0.85)]
    [InlineData("""{ "InnStripOpacity": 1, "SettingsVersion": 4 }""", 0.85)]
    [InlineData("""{ "InnStripOpacity": 1, "SettingsVersion": 5 }""", 0.85)]
    [InlineData("""{ "InnStripOpacity": 0.6, "SettingsVersion": 4 }""", 0.6)]
    [InlineData("""{ "InnStripOpacity": 1, "SettingsVersion": 6 }""", 1.0)]
    [InlineData("""{ "InnStripOpacity": 0.85, "SettingsVersion": 6 }""", 0.85)]
    public void The_inn_helper_is_see_through_by_default_and_an_old_files_100_percent_becomes_85(string json, double opacity)
    {
        Assert.Equal(0.85, HudConfig.DefaultInnStripOpacity);
        Assert.Equal(0.85, new HudConfig().InnStripOpacity);
        var config = ConfigStore.Parse(json);
        Assert.Equal(opacity, config.InnStripOpacity);
        Assert.Equal(HudConfig.CurrentSettingsVersion, config.SettingsVersion);
    }

    /// <summary>A config written now and read back keeps the inn helper's opacity, the default and 100% alike.</summary>
    [Theory]
    [InlineData(0.85)]
    [InlineData(1.0)]
    [InlineData(0.4)]
    public void The_inn_helpers_opacity_survives_a_save_and_load(double opacity)
    {
        var config = ConfigStore.Parse("{}");
        config.InnStripOpacity = opacity;
        Assert.Equal(opacity, ConfigStore.Parse(ConfigStore.Serialize(config)).InnStripOpacity);
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

    /// <summary>Reading battle-skill keys is the player's consent: off until they turn it on, and an older config.json has it off.</summary>
    [Fact]
    public void Reading_skill_presses_is_off_until_the_player_turns_it_on()
    {
        Assert.False(new HudConfig().ReadSkillPresses);
        Assert.False(ConfigStore.Parse("""{ "SettingsVersion": 6, "RotationEnabled": true }""").ReadSkillPresses);
        Assert.True(ConfigStore.Parse("""{ "ReadSkillPresses": true }""").ReadSkillPresses);
    }

    [Fact]
    public void Skill_keys_default_to_1_to_4()
    {
        // Set-1 scan codes of the top-row 1, 2, 3, 4.
        Assert.Equal([0x02, 0x03, 0x04, 0x05], new HudConfig().SkillKeys);
        Assert.Equal([0x02, 0x03, 0x04, 0x05], ConfigStore.Parse("{}").SkillKeys);
        Assert.Equal([0x10, 0x11, 0x12, 0x13], ConfigStore.Parse("""{ "SkillKeys": [16, 17, 18, 19] }""").SkillKeys);
    }

    [Theory]
    [InlineData("""{ "SkillKeys": [2, 2, 4, 5] }""")]      // two slots on one key
    [InlineData("""{ "SkillKeys": [2, 3, 4] }""")]         // a slot missing
    [InlineData("""{ "SkillKeys": [2, 3, 4, 5, 6] }""")]   // one too many
    [InlineData("""{ "SkillKeys": [0, 3, 4, 5] }""")]      // not a key
    [InlineData("""{ "SkillKeys": [2, 3, 4, 512] }""")]    // beyond any key
    [InlineData("""{ "SkillKeys": null }""")]
    public void A_skill_key_list_with_a_duplicate_goes_back_to_the_defaults(string json) =>
        Assert.Equal([0x02, 0x03, 0x04, 0x05], ConfigStore.Parse(json).SkillKeys);

    /// <summary>A rebinding survives a save and load, and the default list is not shared between configs.</summary>
    [Fact]
    public void Chosen_skill_keys_survive_a_save_and_load()
    {
        var config = ConfigStore.Parse("{}");
        config.SkillKeys[0] = 0x10;

        Assert.Equal([0x10, 0x03, 0x04, 0x05], ConfigStore.Parse(ConfigStore.Serialize(config)).SkillKeys);
        Assert.Equal(0x02, new HudConfig().SkillKeys[0]);
    }

    [Theory]
    [InlineData("""{ "OcrIntervalMs": 1000 }""", 250)]                        // the old default, never chosen
    [InlineData("""{ "OcrIntervalMs": 500 }""", 500)]                           // chosen by the player: kept
    [InlineData("""{ "OcrIntervalMs": 1000, "SettingsVersion": 2 }""", 1000)]   // chosen after the change: kept
    [InlineData("""{ }""", 250)]
    public void The_reading_interval_defaults_to_250ms_and_old_defaults_are_updated(string json, int expected)
    {
        var config = ConfigStore.Parse(json);

        Assert.Equal(expected, config.OcrIntervalMs);
        Assert.Equal(HudConfig.CurrentSettingsVersion, config.SettingsVersion);
    }

    /// <summary>A hand-edited config.json with null where a list or a name belongs runs on the defaults for those, never crashes the HUD later.</summary>
    [Theory]
    [InlineData("""{ "ActiveRotations": null, "Language": null, "SkillKeys": null }""")]
    [InlineData("""{ "Language": "zh-CN?x=1" }""")]
    [InlineData("""{ "Language": "../../evil" }""")]
    public void Nulls_and_odd_names_in_a_hand_edited_config_fall_back_to_the_defaults(string json)
    {
        var config = ConfigStore.Parse(json);

        Assert.NotNull(config.ActiveRotations);
        Assert.Equal(Loc.DefaultLanguage, config.Language);
        Assert.Equal(new HudConfig().SkillKeys, config.SkillKeys);
    }

    [Theory]
    [InlineData("""{ "OcrIntervalMs": 1.5 }""")]
    [InlineData("""{ "OcrIntervalMs": 1e40 }""")]
    [InlineData("""{ "ComboHintMode": "Nonsense" }""")]
    [InlineData("""{ "ActiveRotations": [1, 2] }""")]
    [InlineData("[]")]
    public void A_config_that_cant_be_read_is_a_JSON_error_which_the_store_keeps_aside(string json) =>
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => ConfigStore.Parse(json));
}
