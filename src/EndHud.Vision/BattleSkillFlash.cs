namespace EndHud.Vision;

/// <summary>
/// One battle-skill button, measured for the cast flash (<see cref="BattleSkillFlash"/>).
/// </summary>
/// <param name="Fill">Median brightness (brightest channel) of the top half of the disc, the white icon left out:
/// 65-90 at rest, 140-233 while a cast fills it; under 30 for a countdown's dark disc. -1 when nothing of it is left
/// to measure (the icon covers it all).</param>
/// <param name="Spread">Median spread of the channels (brightest minus darkest) over the same pixels: a fill is nearly
/// grey (25 or less on a neutral scene, up to 48 when a purple scene tints the see-through disc).</param>
/// <param name="Scene">Median brightness of the scene round the button (a ring just outside it).</param>
/// <param name="Rim">Share of near-white pixels on the button's rim: 0.25-0.47 under the empowered glow, 0-0.02 at rest.</param>
public readonly record struct FlashSample(double Fill, double Spread, double Scene, double Rim);

/// <summary>
/// The battle-skill buttons (bottom right, keyboard-and-mouse layout) measured for the cast flash, one frame at a
/// time. When a slot casts its battle skill, by any of keys 1-4 and whoever is controlled, the dark top half of that
/// slot's button turns flat light grey for 2-10 frames at 30 fps (67-330 ms), then a white ring grows out of it. An
/// empowered battle skill draws a thick white ring on the button's rim instead, which stays for seconds; it starts
/// with a fill too. What a run of samples means is <see cref="CastFlashTracker"/>'s job; this only measures.
/// <para>The flash is too short for the readings (every 250 ms or slower), so the buttons are measured on their own
/// fast path from a small crop of the corner (<see cref="Region"/>). Measured on 4K recordings (2026-10-24).</para>
/// </summary>
public static class BattleSkillFlash
{
    /// <summary>The corner read, at 4K, anchored bottom right: the four buttons and the scene round them.</summary>
    public static readonly PixelRect Corner = new(3020, 1790, 820, 370);

    /// <summary>Slot 1's button centre at 4K (the fill was measured round this point); the others are <see cref="SkillLayout.Pitch"/> apart.</summary>
    private const double CentreX = SkillLayout.FirstCentreX - 2;
    private const double CentreY = 1928;

    // Radii at 4K: the disc's top half (the fill), the rim (the empowered ring), the scene round the button.
    private const double FillInner = 20, FillOuter = 50, FillBelowCentre = 5;
    private const double RimInner = 66, RimOuter = 84;
    private const double SceneInner = 90, SceneOuter = 125;

    /// <summary>Brighter than this the icon's white: left out of the fill.</summary>
    private const int IconFrom = 235;

    /// <summary>A fill: at least this bright...</summary>
    public const double FilledAt = 140;

    /// <summary>...and nearly grey (the channels this close).</summary>
    public const double SpreadMax = 60;

    /// <summary>A fill stands out when it is this much brighter than the scene round the button...</summary>
    public const double SceneContrast = 60;

    /// <summary>...or jumps this much above the button's own brightness just before, and reaches <see cref="JumpReach"/>.</summary>
    public const double OwnJump = 70;

    public const double JumpReach = 200;

    /// <summary>The rim counts as lit (the empowered glow) from this share of near-white pixels.</summary>
    public const double RimLitAt = 0.2;

    /// <summary>A countdown's dark disc: the fill measure under this.</summary>
    public const double CountdownBelow = 30;

    /// <summary>The corner read on a picture of this size (the game's 16:9 picture).</summary>
    public static PixelRect Region(int width, int height) => new UiScaler(width, height).BottomRight(Corner).ClampTo(width, height);

    /// <summary>The disc looks filled: bright and grey.</summary>
    public static bool Filled(FlashSample s) => s.Fill >= FilledAt && s.Spread <= SpreadMax;

    /// <summary>
    /// A fill that can't be scene light: brighter than the scene round the button, or a jump above the button's
    /// own brightness just before (<paramref name="baseline"/>) to near white (a tinted scene makes the first fail).
    /// </summary>
    public static bool StandsOut(FlashSample s, double baseline) =>
        Filled(s) && (s.Fill - s.Scene >= SceneContrast || (s.Fill >= JumpReach && s.Fill - baseline >= OwnJump));

    public static bool RimLit(FlashSample s) => s.Rim >= RimLitAt;

    /// <summary>The dark disc of a countdown (the button cooling down after a cast).</summary>
    public static bool Countdown(FlashSample s) => s.Fill is >= 0 and < CountdownBelow;

    /// <summary>The four buttons, slot order from the left (a smaller squad's are drawn from the right).</summary>
    /// <param name="image">A picture whose bottom-right corner is the game picture's: the whole picture, or the corner
    /// cut from it (<see cref="Region"/>).</param>
    /// <param name="scale">The game picture's HUD scale (<see cref="UiScaler.Scale"/>).</param>
    public static FlashSample[] MeasureAll(Frame image, double scale)
    {
        var samples = new FlashSample[4];
        for (int button = 0; button < 4; button++) samples[button] = Measure(image, scale, button);
        return samples;
    }

    /// <param name="button">0-3, from the left.</param>
    public static FlashSample Measure(Frame image, double scale, int button)
    {
        double cx = image.Width - (UiScaler.ReferenceWidth - (CentreX + button * SkillLayout.Pitch)) * scale;
        double cy = image.Height - (UiScaler.ReferenceHeight - CentreY) * scale;
        Span<int> fill = stackalloc int[256], spread = stackalloc int[256], scene = stackalloc int[256];
        int fills = 0, scenes = 0, rim = 0, rimWhite = 0;
        double fillIn = Sq(FillInner * scale), fillOut = Sq(FillOuter * scale), rimIn = Sq(RimInner * scale), rimOut = Sq(RimOuter * scale);
        double sceneIn = Sq(SceneInner * scale), sceneOut = Sq(SceneOuter * scale), below = FillBelowCentre * scale;
        // The scene ring is the big one: every other pixel of it is plenty for a median.
        int r = (int)Math.Ceiling(SceneOuter * scale);
        int x0 = Math.Max(0, (int)Math.Floor(cx - r)), x1 = Math.Min(image.Width - 1, (int)Math.Ceiling(cx + r));
        int y0 = Math.Max(0, (int)Math.Floor(cy - r)), y1 = Math.Min(image.Height - 1, (int)Math.Ceiling(cy + r));
        var bgra = image.Bgra;
        for (int y = y0; y <= y1; y++)
        {
            double dy = y - cy, dy2 = dy * dy;
            int row = y * image.Width;
            for (int x = x0; x <= x1; x++)
            {
                double dx = x - cx, d2 = dx * dx + dy2;
                if (d2 > sceneOut) continue;
                int i = (row + x) * 4;
                int b = bgra[i], g = bgra[i + 1], red = bgra[i + 2];
                int max = Math.Max(red, Math.Max(g, b)), min = Math.Min(red, Math.Min(g, b));
                if (d2 >= sceneIn)
                {
                    if (((x ^ y) & 1) == 0)
                    {
                        scene[max]++;
                        scenes++;
                    }
                }
                else if (d2 >= rimIn)
                {
                    if (d2 <= rimOut)
                    {
                        rim++;
                        if (min > 200) rimWhite++;
                    }
                }
                else if (d2 >= fillIn && d2 <= fillOut && dy < -below && max < IconFrom)
                {
                    fill[max]++;
                    spread[max - min]++;
                    fills++;
                }
            }
        }
        return new FlashSample(
            fills == 0 ? -1 : Median(fill, fills),
            fills == 0 ? -1 : Median(spread, fills),
            scenes == 0 ? -1 : Median(scene, scenes),
            rim == 0 ? 0 : rimWhite / (double)rim);
    }

    private static double Sq(double v) => v * v;

    /// <summary>The median of a histogram of <paramref name="count"/> values (the lower middle one for an even count, as numpy's average of two is within a level).</summary>
    private static double Median(Span<int> histogram, int count)
    {
        int lower = (count - 1) / 2, upper = count / 2, seen = 0;
        int? low = null;
        for (int v = 0; v < histogram.Length; v++)
        {
            seen += histogram[v];
            if (low is null && seen > lower) low = v;
            if (seen > upper) return (low!.Value + v) / 2.0;
        }
        return histogram.Length - 1;
    }
}
