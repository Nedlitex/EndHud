using System.Text.RegularExpressions;

namespace EndHud.Vision.Inn;

/// <summary>
/// The Full Blaze meter (满堂彩) at the right of the session: the fire ("92/100") under the round
/// button, and the streak (成功连续上餐 18) under that. At full the number goes, the button turns red
/// and a yellow "开启满堂彩 ▸▸" with the R key takes its place. Hidden while the dispatch panel is
/// open. The numbers are read by their shapes (<see cref="InnDigits"/>), the ready label by OCR.
/// </summary>
public static partial class FireMeterReader
{
    // At 4K, anchored to the right. The "N/100" (the flame icon left of it moves with the number's
    // length, and is left out as an unknown first glyph).
    internal static readonly PixelRect Fire = new(3420, 1245, 185, 60);
    // The streak's number, right of its label: white, red from about 15 on.
    internal static readonly PixelRect Streak = new(3565, 1300, 170, 95);
    // The streak's digits are taller than its label's characters (52 px against 40 at 4K).
    private const double StreakHeightShare = 0.88;
    // The round button's middle, and the ready label left of it.
    internal static readonly PixelRect Button = new(3680, 1175, 80, 80);
    internal static readonly PixelRect ReadyLabel = new(3290, 1235, 240, 60);
    // The button is red when ready: at least this share of its middle (the white flame drawn on it is the rest).
    private const double ReadyRedShare = 0.15;

    /// <summary>The meter, or null when neither the fire nor the ready label can be read (hidden, fading in).</summary>
    public static async Task<FireMeter?> ReadAsync(InnText text, Frame frame)
    {
        var layout = new InnLayout(frame);
        // The number is one colour; read in that colour alone, so white things behind a red number don't join it.
        var streakRegion = layout.TopRight(Streak);
        int? streak = ParseStreak(InnDigits.Read(frame, streakRegion, StreakRed, layout.Scale, minHeightShare: StreakHeightShare))
                      ?? ParseStreak(InnDigits.Read(frame, streakRegion, StreakWhite, layout.Scale, minHeightShare: StreakHeightShare));
        if (ParseFire(InnDigits.Read(frame, layout.TopRight(Fire), InnText.White, layout.Scale, dropUnknownFirst: true)) is { } fire)
            return new FireMeter(fire, streak, Ready: false);
        if (RedShare(frame, layout.TopRight(Button)) >= ReadyRedShare
            && await text.ReadAsync(frame, layout.TopRight(ReadyLabel), Yellow, layout.Scale) is { } label
            && (label.Contains("满堂") || label.Contains("堂彩") || label.Contains("开启")))
            return new FireMeter(100, streak, Ready: true);
        return null;
    }

    private static int? ParseFire(string? read) =>
        read is not null && FireText().Match(read) is { Success: true } m && int.Parse(m.Groups[1].Value) is var n and <= 100 ? n : null;

    private static int? ParseStreak(string? read) =>
        read is { Length: >= 1 and <= 3 } && read.All(char.IsAsciiDigit) ? int.Parse(read) : null;

    private static double RedShare(Frame frame, PixelRect region)
    {
        var r = region.ClampTo(frame.Width, frame.Height);
        if (r.IsEmpty) return 0;
        int red = 0;
        for (int y = r.Y; y < r.Bottom; y++)
        for (int x = r.X; x < r.Right; x++)
        {
            var (cr, cg, cb) = frame.GetPixel(x, y);
            if (cr >= 170 && cr - cg >= 70 && cr - cb >= 60) red++;
        }
        return red / (double)(r.Width * r.Height);
    }

    /// <summary>The streak's digits below about 15: pure white (the floor behind is beige, the label greyer).</summary>
    internal static bool StreakWhite(byte r, byte g, byte b)
    {
        int max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        return min >= 215 && max - min <= 25;
    }

    /// <summary>The streak's digits from about 15 on: the game's red-orange (not the dark red brush stroke behind them).</summary>
    internal static bool StreakRed(byte r, byte g, byte b) => r >= 200 && g <= 120 && b <= 100;

    private static bool Yellow(byte r, byte g, byte b) => r >= 200 && g >= 170 && b <= 120;

    [GeneratedRegex(@"^(\d{1,3})/100$")]
    private static partial Regex FireText();
}
