using EndHud.Vision;

namespace EndHud;

/// <summary>
/// Which battle overlays show in this battle (owner, 2026-10-25: "we should by default not show the rotation in open world
/// battle, make this a choice that user can flip otherwise", and "the rest of the battle hud should also be able to
/// controlled to define whether to show in open world"). In an instance's battle (<see cref="BattleKind.Instance"/>, the
/// evacuate bar at the top left) and while the kind is unknown every overlay shows, as before the kinds were told apart;
/// in an open-world fight each follows its own setting: the rotation strip off by default, the gauges, the combo-ready
/// hints and the alert pop-ups on.
/// <para>
/// Only what is drawn is held back: the rotation's phase and step following, the gauges' counter memory and the combo
/// cooldowns keep running in an open-world fight (they run on the reading already taken, so it costs next to nothing,
/// and a setting turned on mid-fight then shows the fight as it is, not from its start); held-back alert rules start
/// over as at a fight's start, so turning them on pops what holds then. The menu's placement samples show whatever the kind.
/// </para>
/// </summary>
internal sealed record BattleOverlays(bool Rotation, bool Gauges, bool ComboHints, bool Alerts)
{
    public static BattleOverlays For(HudConfig config, BattleKind kind) => kind != BattleKind.OpenWorld
        ? new(true, true, true, true)
        : new(config.ShowRotationInOpenWorld, config.ShowGaugesInOpenWorld, config.ShowComboHintsInOpenWorld, config.ShowAlertsInOpenWorld);
}

/// <summary>
/// The one-time notice that the rotation strip is now held back in open-world fights, and where to turn it back on
/// (owner, 2026-10-25: "add a notification after flipping the default to false to notify user that they can change this
/// (show only once)"). Given the first time it matters, to a player upgrading and to a new one alike: the first time a
/// strip would have shown in an open-world fight and was held back. Remembered in config.json
/// (<see cref="HudConfig.OpenWorldRotationNoticeShown"/>), so it never comes again.
/// </summary>
internal static class OpenWorldNotice
{
    /// <summary>The strip for this squad would show now, but this is an open-world fight and its setting keeps it hidden.</summary>
    public static bool RotationHeldBack(HudConfig config, BattleKind kind, bool stripWouldShow) =>
        stripWouldShow && !BattleOverlays.For(config, kind).Rotation;

    /// <summary>Is the notice due now? True once ever: it is marked shown on <paramref name="config"/> (the caller saves it).</summary>
    public static bool Due(HudConfig config, bool rotationHeldBack)
    {
        if (!rotationHeldBack || config.OpenWorldRotationNoticeShown) return false;
        config.OpenWorldRotationNoticeShown = true;
        return true;
    }
}
