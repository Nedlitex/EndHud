namespace EndHud.Vision;

/// <summary>
/// The game draws a 16:9 picture. On a screen of another shape it fills the rest with black bars:
/// a player's 2560×1600 (16:10) screen showed the game's 2560×1440 picture between bars 80 px high
/// above and below it (10 % of the picture black). Every HUD position is measured from the 16:9
/// picture's edges (<see cref="UiScaler"/>), so the bars are cut off before reading; read with
/// them, the squad panel was looked for 80 px too low and never found, and the UID mask of the
/// diagnostics export fell on the bar instead of the UID.
/// <para>
/// A bar need not be entirely black: an on-screen display drawn into the game's frames (MSI
/// Afterburner's, through RivaTuner) lands in a bar wherever the player puts it, and one glyph of it
/// used to make the bars "not bars", so nothing was read on any such screen (2026-10-24
/// investigation). A bar is taken when most of its lines across (columns of a top or bottom bar,
/// rows of a side bar) are entirely black and nearly all its pixels are: a display covers a corner
/// of a bar, while a picture reaching the frame's edge lights its lines from end to end.
/// </para>
/// </summary>
public static class Letterbox
{
    // A bar pixel's brightest channel is at most this (the bars are pure black; JPEG noise allowed).
    private const int BarMax = 12;

    // Bars thinner than this (rounding of a near-16:9 window) aren't worth cutting.
    private const int MinBar = 4;

    // Of a bar's lines across, at least this share entirely black. A display's widest at 1080p is about
    // 270 of a bar's 1920 columns (86 % left black); a row of picture across the bar leaves none.
    internal const double MinBlackLines = 0.6;

    // Of a bar's pixels, at least this share black. Measured with the synthetic display in a bar
    // (LetterboxTests, every position, with and without its background, 1280x800 to 5120x1440): 97.4 %
    // at the lowest, its lines 84.8 %. Real fonts are outlined and denser: room is left below.
    internal const double MinBlackPixels = 0.9;

    /// <summary>
    /// The 16:9 picture inside <paramref name="frame"/>: the whole frame when it is already 16:9,
    /// or when what would be the bars isn't black enough on both sides (then it isn't a letterbox).
    /// </summary>
    public static PixelRect Content(Frame frame) => Read(frame).Content;

    /// <summary>
    /// What <see cref="Content"/> decided and why: where the bars would be on a frame that isn't 16:9,
    /// and how black each one is.
    /// </summary>
    public static LetterboxReading Read(Frame frame)
    {
        int w = frame.Width, h = frame.Height;
        var whole = new PixelRect(0, 0, w, h);
        int contentH = (int)Math.Round(w * 9 / 16.0), contentW = (int)Math.Round(h * 16 / 9.0);
        if (h - contentH >= 2 * MinBar)
        {
            int bar = (h - contentH) / 2;
            var first = Measure(frame, new PixelRect(0, 0, w, bar), acrossColumns: true);
            var second = Measure(frame, new PixelRect(0, h - bar, w, bar), acrossColumns: true);
            var picture = new PixelRect(0, bar, w, h - 2 * bar);
            return new LetterboxReading(first.IsBar && second.IsBar ? picture : whole, picture, first, second);
        }
        if (w - contentW >= 2 * MinBar)
        {
            int bar = (w - contentW) / 2;
            var first = Measure(frame, new PixelRect(0, 0, bar, h), acrossColumns: false);
            var second = Measure(frame, new PixelRect(w - bar, 0, bar, h), acrossColumns: false);
            var picture = new PixelRect(bar, 0, w - 2 * bar, h);
            return new LetterboxReading(first.IsBar && second.IsBar ? picture : whole, picture, first, second);
        }
        return new LetterboxReading(whole, null, null, null);
    }

    /// <summary>
    /// How black <paramref name="area"/> is (sampled every other row and column): its share of black pixels,
    /// and of its lines across (columns when <paramref name="acrossColumns"/>, else rows) entirely black.
    /// </summary>
    private static BarBlackness Measure(Frame frame, PixelRect area, bool acrossColumns)
    {
        int lines = 0, blackLines = 0;
        long pixels = 0, blackPixels = 0;
        int outerFrom = acrossColumns ? area.X : area.Y, outerTo = acrossColumns ? area.Right : area.Bottom;
        int innerFrom = acrossColumns ? area.Y : area.X, innerTo = acrossColumns ? area.Bottom : area.Right;
        for (int o = outerFrom; o < outerTo; o += 2)
        {
            bool allBlack = true;
            for (int i = innerFrom; i < innerTo; i += 2)
            {
                var (r, g, b) = acrossColumns ? frame.GetPixel(o, i) : frame.GetPixel(i, o);
                pixels++;
                if (Math.Max(r, Math.Max(g, b)) <= BarMax) blackPixels++;
                else allBlack = false;
            }
            lines++;
            if (allBlack) blackLines++;
        }
        return new BarBlackness(pixels == 0 ? 0 : blackPixels / (double)pixels, lines == 0 ? 0 : blackLines / (double)lines);
    }
}

/// <summary>How black a would-be bar is: the share of its pixels, and of its lines across, that are black.</summary>
public readonly record struct BarBlackness(double Pixels, double Lines)
{
    /// <summary>Black enough to be a bar (an overlay in a corner of it allowed).</summary>
    public bool IsBar => Pixels >= Letterbox.MinBlackPixels && Lines >= Letterbox.MinBlackLines;

    public override string ToString() =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{Math.Floor(Pixels * 100)}% of pixels, {Math.Floor(Lines * 100)}% of lines black");
}

/// <summary>
/// <see cref="Letterbox.Read"/>'s decision. <paramref name="Expected"/>: the 16:9 picture if the frame had bars (null on a 16:9
/// frame); <paramref name="First"/> and <paramref name="Second"/>: how black the top and bottom (or left and right) bars are.
/// </summary>
public sealed record LetterboxReading(PixelRect Content, PixelRect? Expected, BarBlackness? First, BarBlackness? Second)
{
    /// <summary>The frame isn't 16:9, yet its edges weren't black enough to be bars (the whole frame is read).</summary>
    public bool BarsRejected => Expected is { } picture && Content != picture;

    /// <summary>A line for the log when <see cref="BarsRejected"/>: each bar's blackness.</summary>
    public string Describe() => Expected is null
        ? "16:9, no bars"
        : $"bars expected (picture {Expected.Value.Width}x{Expected.Value.Height} at {Expected.Value.X},{Expected.Value.Y}) {(BarsRejected ? "but not black" : "and cut off")}: {(Expected.Value.X > 0 ? "left" : "top")} {First}, " +
          $"{(Expected.Value.X > 0 ? "right" : "bottom")} {Second}{(BarsRejected ? " (an overlay drawn in the bar, or not a letterbox)" : "")}";
}
