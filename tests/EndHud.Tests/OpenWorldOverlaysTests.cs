using EndHud.Vision;
using Xunit;

namespace EndHud.Tests;

/// <summary>
/// Which battle overlays show in a fight in the open world (owner, 2026-10-25: "we should by default not show the rotation
/// in open world battle, make this a choice that user can flip otherwise", and "the rest of the battle hud should also be
/// able to controlled to define whether to show in open world"): one setting per overlay, the rotation strip off by
/// default and the rest on; an instance's battle (the evacuate bar, 撤离) and an unknown kind show everything as before.
/// And the one-time notice the first time the strip is held back in an open-world fight.
/// </summary>
public class OpenWorldOverlaysTests
{
    [Fact]
    public void By_default_only_the_rotation_strip_stays_hidden_in_an_open_world_fight()
    {
        var config = new HudConfig();

        Assert.Equal(new BattleOverlays(Rotation: false, Gauges: true, ComboHints: true, Alerts: true), BattleOverlays.For(config, BattleKind.OpenWorld));
    }

    [Theory]
    [InlineData(BattleKind.Instance)]
    [InlineData(BattleKind.Unknown)]
    public void An_instance_or_an_unknown_battle_shows_every_overlay_whatever_the_settings(BattleKind kind)
    {
        var config = new HudConfig
        {
            ShowRotationInOpenWorld = false, ShowGaugesInOpenWorld = false, ShowComboHintsInOpenWorld = false, ShowAlertsInOpenWorld = false,
        };

        Assert.Equal(new BattleOverlays(true, true, true, true), BattleOverlays.For(config, kind));
    }

    [Fact]
    public void Each_setting_hides_only_its_own_overlay_in_the_open_world()
    {
        var all = new HudConfig { ShowRotationInOpenWorld = true };
        Assert.Equal(new BattleOverlays(true, true, true, true), BattleOverlays.For(all, BattleKind.OpenWorld));

        Assert.Equal(new BattleOverlays(true, false, true, true),
            BattleOverlays.For(new HudConfig { ShowRotationInOpenWorld = true, ShowGaugesInOpenWorld = false }, BattleKind.OpenWorld));
        Assert.Equal(new BattleOverlays(true, true, false, true),
            BattleOverlays.For(new HudConfig { ShowRotationInOpenWorld = true, ShowComboHintsInOpenWorld = false }, BattleKind.OpenWorld));
        Assert.Equal(new BattleOverlays(true, true, true, false),
            BattleOverlays.For(new HudConfig { ShowRotationInOpenWorld = true, ShowAlertsInOpenWorld = false }, BattleKind.OpenWorld));
    }

    [Fact]
    public void The_defaults_are_saved_and_an_older_config_gets_them()
    {
        // A config.json from before the settings (version 6) says nothing of them: the defaults, the notice not shown yet.
        var old = ConfigStore.Parse("""{ "SettingsVersion": 6, "RotationEnabled": true }""");
        Assert.False(old.ShowRotationInOpenWorld);
        Assert.True(old.ShowGaugesInOpenWorld);
        Assert.True(old.ShowComboHintsInOpenWorld);
        Assert.True(old.ShowAlertsInOpenWorld);
        Assert.False(old.OpenWorldRotationNoticeShown);
        Assert.Equal(HudConfig.CurrentSettingsVersion, old.SettingsVersion);

        string saved = ConfigStore.Serialize(new HudConfig());
        Assert.Contains("\"ShowRotationInOpenWorld\": false", saved);
        Assert.Contains("\"ShowGaugesInOpenWorld\": true", saved);

        // The player's choices are kept.
        var chosen = ConfigStore.Parse("""{ "ShowRotationInOpenWorld": true, "ShowAlertsInOpenWorld": false }""");
        Assert.True(chosen.ShowRotationInOpenWorld);
        Assert.False(chosen.ShowAlertsInOpenWorld);
    }

    [Fact]
    public void The_notice_is_due_the_first_time_the_strip_is_held_back_and_never_again()
    {
        var config = new HudConfig();

        Assert.False(OpenWorldNotice.Due(config, rotationHeldBack: false)); // nothing held back yet: not due, not spent
        Assert.False(config.OpenWorldRotationNoticeShown);
        Assert.True(OpenWorldNotice.Due(config, rotationHeldBack: true));
        Assert.True(config.OpenWorldRotationNoticeShown);
        Assert.False(OpenWorldNotice.Due(config, rotationHeldBack: true));

        // Saved and read back (the next start): still never again.
        var reloaded = ConfigStore.Parse(ConfigStore.Serialize(config));
        Assert.False(OpenWorldNotice.Due(reloaded, rotationHeldBack: true));
    }

    [Fact]
    public void Held_back_only_when_a_strip_would_show_in_an_open_world_fight_with_the_setting_off()
    {
        var config = new HudConfig();
        Assert.True(OpenWorldNotice.RotationHeldBack(config, BattleKind.OpenWorld, stripWouldShow: true));
        Assert.False(OpenWorldNotice.RotationHeldBack(config, BattleKind.OpenWorld, stripWouldShow: false)); // no rotation for this squad
        Assert.False(OpenWorldNotice.RotationHeldBack(config, BattleKind.Instance, stripWouldShow: true));
        Assert.False(OpenWorldNotice.RotationHeldBack(new HudConfig { ShowRotationInOpenWorld = true }, BattleKind.OpenWorld, stripWouldShow: true));
    }
}
