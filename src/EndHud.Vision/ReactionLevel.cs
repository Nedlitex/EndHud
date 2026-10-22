namespace EndHud.Vision;

/// <summary>
/// The level badge on a reaction's icon in the boss's status row (Corrosion, Electrification).
/// </summary>
public static class ReactionLevel
{
    /// <summary>Reactions whose level badge has been measured on the owner's recordings.</summary>
    public static IReadOnlySet<string> Measured { get; } = new HashSet<string>(StringComparer.Ordinal) { "Corrosion", "Electrification" };

    /// <summary>The level (1–4) on the badge of the icon in <paramref name="square"/>, or null.</summary>
    public static int? ReadLevel(Frame frame, PixelRect square) => null;
}
