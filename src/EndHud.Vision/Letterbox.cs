namespace EndHud.Vision;

/// <summary>
/// The game draws a 16:9 picture. On a screen of another shape it fills the rest with black bars:
/// a player's 2560×1600 (16:10) screen showed the game's 2560×1440 picture between bars 80 px high
/// above and below it (10 % of the picture black). Every HUD position is measured from the 16:9
/// picture's edges (<see cref="UiScaler"/>), so the bars are cut off before reading; read with
/// them, the squad panel was looked for 80 px too low and never found, and the UID mask of the
/// diagnostics export fell on the bar instead of the UID.
/// </summary>
public static class Letterbox
{
    // A bar pixel's brightest channel is at most this (the bars are pure black; JPEG noise allowed).
    private const int BarMax = 12;

    // Bars thinner than this (rounding of a near-16:9 window) aren't worth cutting.
    private const int MinBar = 4;

    /// <summary>
    /// The 16:9 picture inside <paramref name="frame"/>: the whole frame when it is already 16:9,
    /// or when what would be the bars isn't black on both sides (then it isn't a letterbox).
    /// </summary>
    public static PixelRect Content(Frame frame)
    {
        int w = frame.Width, h = frame.Height;
        int contentH = (int)Math.Round(w * 9 / 16.0), contentW = (int)Math.Round(h * 16 / 9.0);
        if (h - contentH >= 2 * MinBar)
        {
            int bar = (h - contentH) / 2;
            if (IsBlack(frame, new PixelRect(0, 0, w, bar)) && IsBlack(frame, new PixelRect(0, h - bar, w, bar)))
                return new PixelRect(0, bar, w, h - 2 * bar);
        }
        else if (w - contentW >= 2 * MinBar)
        {
            int bar = (w - contentW) / 2;
            if (IsBlack(frame, new PixelRect(0, 0, bar, h)) && IsBlack(frame, new PixelRect(w - bar, 0, bar, h)))
                return new PixelRect(bar, 0, w - 2 * bar, h);
        }
        return new PixelRect(0, 0, w, h);
    }

    /// <summary>Every pixel of <paramref name="area"/> black (sampled every other row and column).</summary>
    private static bool IsBlack(Frame frame, PixelRect area)
    {
        for (int y = area.Y; y < area.Bottom; y += 2)
        for (int x = area.X; x < area.Right; x += 2)
        {
            var (r, g, b) = frame.GetPixel(x, y);
            if (Math.Max(r, Math.Max(g, b)) > BarMax) return false;
        }
        return true;
    }
}
