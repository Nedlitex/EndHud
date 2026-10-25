using EndHud.Vision;

namespace EndHud;

/// <summary>Which battle overlays show in this battle.</summary>
internal sealed record BattleOverlays(bool Rotation, bool Gauges, bool ComboHints, bool Alerts)
{
    public static BattleOverlays For(HudConfig config, BattleKind kind) => new(true, true, true, true);
}

/// <summary>The one-time notice that the rotation strip is held back in open-world fights.</summary>
internal static class OpenWorldNotice
{
    public static bool RotationHeldBack(HudConfig config, BattleKind kind, bool stripWouldShow) => false;

    public static bool Due(HudConfig config, bool rotationHeldBack) => false;
}
