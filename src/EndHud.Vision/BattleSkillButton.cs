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

    /// <summary>
    /// Bands of white rows across the disc this many or more: lines of text drawn over the button (an on-screen
    /// display's, such as RivaTuner's), not its icon or countdown. The icon and the countdown's digits are one band on
    /// every button of the fixtures (two where an effect's white streak crosses it); the display's lines, three.
    /// </summary>
    internal const int TextLines = 3;

    /// <summary>
    /// Inside this radius (4K) the white rows are counted: the dark disc (to 58), the outline left out. At 44 (the icon's
    /// reach) the text's lines over a dimmed button joined through its light parts into two bands.
    /// </summary>
    private const double LinesRadius = 56;

    /// <summary>
    /// How many bands of rows with white (at least 2 px at 4K) the disc has, told apart by at least 3 rows (4K) with
    /// none.
    /// </summary>
    internal static int WhiteLines(Frame frame, double cx, double cy, double scale)
    {
        double radius = LinesRadius * scale;
        double minWhite = Math.Max(1, 2 * scale), minGap = Math.Max(1, 3 * scale);
        int bands = 0, gap = 0;
        bool inBand = false;
        for (int y = (int)Math.Round(cy - radius); y <= (int)Math.Round(cy + radius); y++)
        {
            if ((uint)y >= (uint)frame.Height) continue;
            int white = 0;
            for (int x = (int)Math.Round(cx - radius); x <= (int)Math.Round(cx + radius); x++)
            {
                double dx = x - cx, dy = y - cy;
                if (dx * dx + dy * dy > radius * radius || (uint)x >= (uint)frame.Width) continue;
                var (r, g, b) = frame.GetPixel(x, y);
                if (Math.Min(r, Math.Min(g, b)) > 200) white++;
            }
            if (white >= minWhite)
            {
                if (!inBand) bands++;
                inBand = true;
                gap = 0;
            }
            else if (++gap >= minGap)
            {
                inBand = false;
            }
        }
        return bands;
    }

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
        if (WhiteLines(frame, cx, cy, ui.Scale) >= TextLines) return null;

        double darkShare = dark / (double)n, whiteShare = white / (double)n;
        if (darkShare >= CoolingDarkAt && whiteShare is >= CoolingWhiteMin and <= CoolingWhiteMax) return BattleSkillState.Cooling;
        if (whiteShare >= IconWhiteMin && darkShare <= IconDarkMax) return BattleSkillState.Available;
        return null;
    }
}
