namespace EndHud.Vision;

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum BattleSkillState
{
    /// <summary>The button shows the skill's icon: it can be cast (if there are skill points).</summary>
    Available,

    /// <summary>The button is a dark disc with a white countdown: just cast, cooling down.</summary>
    Cooling,
}

/// <summary>
/// The four battle skill buttons in the bottom-right corner, one per squad slot in panel order,
/// under the small ultimate circles (<see cref="SkillLayout"/>). Any operator's battle skill is cast
/// with keys 1–4, not only the controlled one's; a cast turns that button into a countdown.
/// </summary>
public static class BattleSkillButton
{
    /// <summary>Centre of slot 1's button at 4K (same columns as the ultimate circles), anchored bottom-right.</summary>
    internal const int CentreY = 1930;

    /// <summary>The disc sampled: inside the button's outline.</summary>
    internal const int SampleRadius = 40;

    // Cooling: a near-black disc (measured 0.74–0.86 of it) with white digits (0.09–0.14).
    // An icon: 0.15–0.48 white, little black. Anything else (effects, cutscenes): unreadable.
    private const double CoolingDarkAt = 0.6;
    private const double CoolingWhiteMin = 0.04;
    private const double CoolingWhiteMax = 0.25;
    private const double IconWhiteMin = 0.12;
    private const double IconDarkMax = 0.45;

    /// <param name="slot">Squad slot, 0-based.</param>
    /// <returns>The button's state, or null when it can't be told (an effect or cutscene over it).</returns>
    public static BattleSkillState? Measure(Frame frame, int slot)
    {
        var ui = new UiScaler(frame.Width, frame.Height);
        var (cx, cy) = ui.BottomRight(SkillLayout.FirstCentreX + slot * SkillLayout.Pitch, CentreY);
        double radius = SampleRadius * ui.Scale;

        int n = 0, dark = 0, white = 0;
        int r = (int)Math.Ceiling(radius);
        for (int dy = -r; dy <= r; dy++)
        for (int dx = -r; dx <= r; dx++)
        {
            if (dx * dx + dy * dy > radius * radius) continue;
            int x = (int)Math.Round(cx + dx), y = (int)Math.Round(cy + dy);
            if ((uint)x >= (uint)frame.Width || (uint)y >= (uint)frame.Height) continue;
            var (cr, cg, cb) = frame.GetPixel(x, y);
            int max = Math.Max(cr, Math.Max(cg, cb)), min = Math.Min(cr, Math.Min(cg, cb));
            n++;
            if (max < 50) dark++;
            else if (min > 200) white++;
        }
        if (n == 0) return null;

        double darkShare = dark / (double)n, whiteShare = white / (double)n;
        if (darkShare >= CoolingDarkAt && whiteShare is >= CoolingWhiteMin and <= CoolingWhiteMax) return BattleSkillState.Cooling;
        if (whiteShare >= IconWhiteMin && darkShare <= IconDarkMax) return BattleSkillState.Available;
        return null;
    }
}
