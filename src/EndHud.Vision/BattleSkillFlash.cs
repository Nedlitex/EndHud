namespace EndHud.Vision;

/// <summary>Where the four battle-skill buttons are drawn: the layouts each put them somewhere else.</summary>
public enum SkillButtonPlaces
{
    /// <summary>Keyboard and mouse: a row, bottom right; a squad of fewer than four is drawn from the right.</summary>
    Row,

    /// <summary>Controller: a diamond round the "LB" badge (slot 1 left, 2 top, 3 right, 4 bottom); every place kept,
    /// an empty one drawn as a ring with a slash.</summary>
    Diamond,

    /// <summary>Controller with LB held (how a battle skill is cast there): a larger diamond up and to the left, each
    /// button with its face-button badge (X, Y, B, A) towards the middle.</summary>
    DiamondHeld,
}

/// <summary>
/// One battle-skill button, measured for the cast flash and the empowered glow (<see cref="BattleSkillFlash"/>).
/// </summary>
/// <param name="Fill">Median brightness (brightest channel) of the top half of the disc, the white icon left out:
/// 65-90 at rest, 140-233 while a cast fills it (keyboard and mouse); under 30 for a countdown's dark disc. -1 when
/// nothing of it is left to measure (the icon covers it all).</param>
/// <param name="Spread">Median spread of the channels (brightest minus darkest) over the same pixels: a fill is nearly
/// grey (25 or less on a neutral scene, up to 48 when a purple scene tints the see-through disc).</param>
/// <param name="Scene">Median brightness of the scene round the button (a ring just outside it).</param>
/// <param name="Rim">Share of near-white pixels on the button's rim: 0.25-0.73 under the empowered glow, 0-0.1 at rest.</param>
/// <param name="Icon">Share of the disc's middle that is light and grey (the icon, a countdown's digits, a fill): 0.16-0.8
/// on every button seen on screen, 0 under a dark backdrop or with the HUD gone; <see cref="BattleSkillFlash.CoveredByText"/> (-1)
/// with three or more lines of text over the button (an on-screen display's, <see cref="BattleSkillButton.TextLines"/>).</param>
/// <param name="Places">Which layout's places it was measured at.</param>
public readonly record struct FlashSample(double Fill, double Spread, double Scene, double Rim, double Icon, SkillButtonPlaces Places)
{
    /// <summary>
    /// The rim's near-white share all round: in twelve sectors round the button, the third least lit (two may be dark: a
    /// face-button badge beside it, an effect over one side). The empowered glow is a whole ring; the white text of an
    /// on-screen display over part of the rim lights only the sectors it covers (it raised <see cref="Rim"/> from 0.15 to
    /// 0.29 over a filled button in the display gate).
    /// </summary>
    public double RimAllRound { get; init; }

    /// <summary>Share of the scene round the button at least <see cref="BattleSkillFlash.BrightScene"/> bright: a white flash
    /// of the scene there lights the see-through rim too. Its median (<see cref="Scene"/>) alone misses it once part of the
    /// ring is darkened (an on-screen display's backdrop over it).</summary>
    public double SceneBright { get; init; }
}

/// <summary>
/// The battle-skill buttons measured for the cast flash, one frame at a time. When a slot casts its battle skill,
/// whoever is controlled, the dark top half of that slot's button turns flat light grey (keyboard and mouse) or white
/// (controller) for 2-10 frames at 30 fps (67-330 ms), then a white ring grows out of it. An empowered battle skill draws
/// a thick white ring on the button's rim instead, which stays for seconds; it starts with a fill too. What a run of
/// samples means is <see cref="CastFlashTracker"/>'s job; this only measures.
/// <para>The flash is too short for the readings (every 250 ms or slower), so the buttons are measured on their own
/// fast path from a small crop of the corner (<see cref="Region(int, int, HudLayout)"/>). Measured on 4K recordings
/// (2026-10-24): keyboard and mouse on six, the controller on battle_controller_test_fullbattle3.</para>
/// <para><b>Controller.</b> A battle skill is cast with LB held, so its flash is in the held diamond
/// (<see cref="SkillButtonPlaces.DiamondHeld"/>), which sits up and to the left of the plain one. The held buttons are
/// see-through and lighter (their top half reads 105-190 at rest over a light floor), and the pressed one turns light
/// grey (145-180) before the flash turns it white (195-230): so a fill there is 195 or brighter. A fill in the plain
/// diamond is never a cast (the empowered glow starting, a long press for an ultimate let go), so it isn't one here.
/// Which diamond a frame shows is told from the corner alone (<see cref="PlacesOf"/>), as LB is held and let go many
/// times a second faster than the readings follow.</para>
/// </summary>
public static class BattleSkillFlash
{
    /// <summary>The corner read with keyboard and mouse, at 4K, anchored bottom right: the four buttons and the scene round them.</summary>
    public static readonly PixelRect Corner = new(3020, 1790, 820, 370);

    /// <summary>The corner read with a controller, at 4K, anchored bottom right: both diamonds and the scene round them.</summary>
    public static readonly PixelRect ControllerCorner = new(3130, 1480, 710, 680);

    /// <summary>Slot 1's button centre at 4K with keyboard and mouse (the fill was measured round this point); the others are <see cref="SkillLayout.Pitch"/> apart.</summary>
    private const double CentreX = SkillLayout.FirstCentreX - 2;
    private const double CentreY = 1928;

    /// <summary>Button centres at 4K in the controller's diamonds, slot 1 first (measured on full battle 3).</summary>
    private static readonly (double X, double Y)[] Diamond = [(3422, 1854), (3553, 1723), (3684, 1854), (3553, 1982)];

    private static readonly (double X, double Y)[] DiamondHeld = [(3264, 1776), (3414, 1610), (3560, 1776), (3414, 1904)];

    /// <summary>The held diamond's four face-button badges (Y top, X left, B right, A bottom) at 4K: a dark disc with a white letter.</summary>
    private static readonly (double X, double Y)[] HeldBadges = [(3414, 1700), (3342, 1776), (3490, 1776), (3414, 1852)];

    // Radii at 4K: the disc's top half (the fill), the rim (the empowered ring), the scene round the button, the icon.
    private const double FillInner = 20, FillOuter = 50, FillBelowCentre = 5;
    private const double RimInner = 66, RimOuter = 84;
    private const double SceneInner = 90, SceneOuter = 125;
    private const double IconOuter = 40;

    /// <summary>Brighter than this the icon's white: left out of the fill.</summary>
    private const int IconFrom = 235;

    /// <summary>A fill: at least this bright (keyboard and mouse)...</summary>
    public const double FilledAt = 140;

    /// <summary>...or this, in the controller's held diamond, whose buttons are lighter at rest and pressed (105-190).</summary>
    public const double HeldFilledAt = 195;

    /// <summary>...and nearly grey (the channels this close).</summary>
    public const double SpreadMax = 60;

    /// <summary>A fill stands out when it is this much brighter than the scene round the button (keyboard and mouse)...</summary>
    public const double SceneContrast = 60;

    /// <summary>...this much in the held diamond (79.3 s of full battle 3: a white fill of 200 in front of a light floor at 146)...</summary>
    public const double HeldSceneContrast = 45;

    /// <summary>...or jumps this much above the button's own brightness just before, and reaches <see cref="JumpReach"/>.</summary>
    public const double OwnJump = 70;

    public const double JumpReach = 200;

    /// <summary>The rim counts as lit (the empowered glow) from this share of near-white pixels.</summary>
    public const double RimLitAt = 0.2;

    /// <summary>
    /// The scene round the button this bright or brighter: a white flash of the scene behind the see-through buttons
    /// lights every rim alike (full battle 3, 80.5 s and 104.3 s: 208-255 round them; under the empowered glow 120-194),
    /// so the rim can't be told.
    /// </summary>
    public const double BrightScene = 200;

    /// <summary>The button is on screen: at least this share of its middle light and grey (0.16 or more on every button
    /// seen; 0 under a 55 % black backdrop).</summary>
    public const double IconSeenAt = 0.1;

    /// <summary>A countdown's dark disc: the fill measure under this.</summary>
    public const double CountdownBelow = 30;

    // The held diamond is told by its badges: a dark ring round a white letter.
    private const double BadgeRingInner = 16, BadgeRingOuter = 21, BadgeCore = 12;

    /// <summary>The corner read with keyboard and mouse on a picture of this size (the game's 16:9 picture).</summary>
    public static PixelRect Region(int width, int height) => Region(width, height, HudLayout.KeyboardMouse);

    /// <summary>The corner read in this layout on a picture of this size (the game's 16:9 picture).</summary>
    public static PixelRect Region(int width, int height, HudLayout layout) =>
        new UiScaler(width, height).BottomRight(layout == HudLayout.KeyboardMouse ? Corner : ControllerCorner).ClampTo(width, height);

    /// <summary>Both corners at once (the recorded check, which doesn't know the layout before reading).</summary>
    public static PixelRect AnyRegion(int width, int height)
    {
        var ui = new UiScaler(width, height);
        var a = ui.BottomRight(Corner);
        var b = ui.BottomRight(ControllerCorner);
        int x = Math.Min(a.X, b.X), y = Math.Min(a.Y, b.Y);
        return new PixelRect(x, y, Math.Max(a.X + a.Width, b.X + b.Width) - x, Math.Max(a.Y + a.Height, b.Y + b.Height) - y).ClampTo(width, height);
    }

    /// <summary>The disc looks filled: bright and grey. Never in the controller's plain diamond (no cast is made there).</summary>
    public static bool Filled(FlashSample s) => s.Places switch
    {
        SkillButtonPlaces.Row => s.Fill >= FilledAt && s.Spread <= SpreadMax,
        SkillButtonPlaces.DiamondHeld => s.Fill >= HeldFilledAt && s.Spread <= SpreadMax,
        _ => false,
    };

    /// <summary>
    /// A fill that can't be scene light: brighter than the scene round the button, or a jump above the button's
    /// own brightness just before (<paramref name="baseline"/>) to near white (a tinted scene makes the first fail).
    /// </summary>
    public static bool StandsOut(FlashSample s, double baseline) =>
        Filled(s) && (s.Fill - s.Scene >= (s.Places == SkillButtonPlaces.DiamondHeld ? HeldSceneContrast : SceneContrast)
                      || (s.Fill >= JumpReach && s.Fill - baseline >= OwnJump));

    public static bool RimLit(FlashSample s) => s.Rim >= RimLitAt;

    /// <summary>
    /// The empowered glow on this button in this sample: lit (true), not lit on a button seen on screen (false), or
    /// can't be told (null): the scene behind as bright as the glow, lines of text over the button, or the button not
    /// seen (a dark backdrop, the HUD gone).
    /// </summary>
    public static bool? Glow(FlashSample s)
    {
        if (s.Scene >= BrightScene || s.SceneBright >= BrightSceneShare || s.Icon < 0) return null;
        if (s.RimAllRound >= RimAllRoundAt) return true;
        return s.Rim < RimUnlitBelow && s.Icon >= IconSeenAt ? false : null;
    }

    /// <summary>The glow is lit all round from this (<see cref="FlashSample.RimAllRound"/>: 0.29-0.54 under the glow in both
    /// layouts, 0-0.02 on every other button of the fixtures).</summary>
    public const double RimAllRoundAt = 0.15;

    /// <summary>
    /// The rim unlit: under this near-white share overall (0-0.1 at rest). Between it and lit all round (the white text
    /// of a display over part of the rim) the glow can't be told.
    /// </summary>
    public const double RimUnlitBelow = 0.12;

    /// <summary>A white flash of the scene round the button: this share of it bright (0.72-0.93 on the scene-light
    /// fixtures; at most 0.39 round a button with the scene behind it lit, 0.2 under the glow).</summary>
    public const double BrightSceneShare = 0.5;

    /// <summary>The dark disc of a countdown (the button cooling down after a cast).</summary>
    public static bool Countdown(FlashSample s) => s.Fill is >= 0 and < CountdownBelow;

    /// <summary>The four buttons with keyboard and mouse, slot order from the left (a smaller squad's are drawn from the right).</summary>
    /// <param name="image">A picture whose bottom-right corner is the game picture's: the whole picture, or the corner
    /// cut from it (<see cref="Region(int, int)"/>).</param>
    /// <param name="scale">The game picture's HUD scale (<see cref="UiScaler.Scale"/>).</param>
    public static FlashSample[] MeasureAll(Frame image, double scale) => MeasureAll(image, scale, SkillButtonPlaces.Row);

    /// <summary>The four buttons at these places: from the left in the row, slot 1 to 4 in the diamonds.</summary>
    public static FlashSample[] MeasureAll(Frame image, double scale, SkillButtonPlaces places)
    {
        var samples = new FlashSample[4];
        for (int button = 0; button < 4; button++) samples[button] = Measure(image, scale, places, button);
        return samples;
    }

    /// <summary>
    /// Which controller diamond the corner shows: the plain one by the "LB" (or "L1") badge in its middle, the held one
    /// by at least three of its four face-button badges; null when neither shows (a cutscene, the HUD fading between
    /// them). Measured on full battle 3 at 30 fps: the badge on 3,423 of the 3,485 frames read as the plain diamond and
    /// never with three held badges; three or four badges on 259 of the 358 read as held.
    /// </summary>
    /// <param name="image">A picture whose bottom-right corner is the game picture's.</param>
    public static SkillButtonPlaces? PlacesOf(Frame image, double scale)
    {
        if (HudLayoutDetector.SkillsBadgeShown(image, scale)) return SkillButtonPlaces.Diamond;
        int badges = HeldBadges.Count(b => HeldBadge(image, scale, b));
        return badges >= 3 ? SkillButtonPlaces.DiamondHeld : null;
    }

    /// <summary>A dark disc round a white letter: the ring mostly dark (brightest channel under 90), the middle partly white.</summary>
    private static bool HeldBadge(Frame image, double scale, (double X, double Y) at)
    {
        double cx = image.Width - (UiScaler.ReferenceWidth - at.X) * scale, cy = image.Height - (UiScaler.ReferenceHeight - at.Y) * scale;
        double ringIn = Sq(BadgeRingInner * scale), ringOut = Sq(BadgeRingOuter * scale), core = Sq(BadgeCore * scale);
        int r = (int)Math.Ceiling(BadgeRingOuter * scale), ring = 0, dark = 0, middle = 0, white = 0;
        var bgra = image.Bgra;
        for (int y = (int)Math.Floor(cy - r); y <= (int)Math.Ceiling(cy + r); y++)
        for (int x = (int)Math.Floor(cx - r); x <= (int)Math.Ceiling(cx + r); x++)
        {
            if ((uint)x >= (uint)image.Width || (uint)y >= (uint)image.Height) continue;
            double d2 = Sq(x - cx) + Sq(y - cy);
            int i = (y * image.Width + x) * 4;
            int b = bgra[i], g = bgra[i + 1], red = bgra[i + 2];
            if (d2 >= ringIn && d2 <= ringOut)
            {
                ring++;
                if (Math.Max(red, Math.Max(g, b)) < 90) dark++;
            }
            else if (d2 <= core)
            {
                middle++;
                if (Math.Min(red, Math.Min(g, b)) > 170) white++;
            }
        }
        if (ring == 0 || middle == 0) return false;
        double whiteShare = white / (double)middle;
        return dark >= 0.6 * ring && whiteShare is >= 0.15 and <= 0.6;
    }

    /// <param name="button">0-3: from the left in the row; the slot's place (slot 1 to 4) in the diamonds.</param>
    public static FlashSample Measure(Frame image, double scale, SkillButtonPlaces places, int button)
    {
        var (rx, ry) = places switch
        {
            SkillButtonPlaces.Diamond => Diamond[button],
            SkillButtonPlaces.DiamondHeld => DiamondHeld[button],
            _ => (CentreX + button * SkillLayout.Pitch, CentreY),
        };
        double cx = image.Width - (UiScaler.ReferenceWidth - rx) * scale;
        double cy = image.Height - (UiScaler.ReferenceHeight - ry) * scale;
        Span<int> fill = stackalloc int[256], spread = stackalloc int[256], scene = stackalloc int[256];
        Span<int> sector = stackalloc int[RimSectors], sectorWhite = stackalloc int[RimSectors];
        int fills = 0, scenes = 0, sceneBright = 0, rim = 0, rimWhite = 0, icon = 0, iconLight = 0;
        // White pixels per row of the disc, for lines of text over it (BattleSkillButton.WhiteLines, counted in this pass).
        double linesRadius = BattleSkillButton.LinesRadius * scale, linesOut = Sq(linesRadius), beside = BattleSkillButton.BesideIcon * scale;
        int linesTop = (int)Math.Round(cy - linesRadius);
        Span<int> lines = stackalloc int[(int)Math.Round(cy + linesRadius) - linesTop + 1], linesBeside = stackalloc int[lines.Length];
        for (int k = 0; k < lines.Length; k++) lines[k] = linesBeside[k] = (uint)(linesTop + k) < (uint)image.Height ? 0 : -1;
        double fillIn = Sq(FillInner * scale), fillOut = Sq(FillOuter * scale), rimIn = Sq(RimInner * scale), rimOut = Sq(RimOuter * scale);
        double sceneIn = Sq(SceneInner * scale), sceneOut = Sq(SceneOuter * scale), below = FillBelowCentre * scale, iconOut = Sq(IconOuter * scale);
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
                        if (max >= BrightScene) sceneBright++;
                    }
                    continue;
                }
                if (d2 >= rimIn)
                {
                    if (d2 <= rimOut)
                    {
                        int s = Math.Min(RimSectors - 1, (int)((Math.Atan2(dy, dx) + Math.PI) / (2 * Math.PI) * RimSectors));
                        rim++;
                        sector[s]++;
                        if (min > 200)
                        {
                            rimWhite++;
                            sectorWhite[s]++;
                        }
                    }
                    continue;
                }
                if (d2 <= linesOut && min > 200)
                {
                    lines[y - linesTop]++;
                    if (Math.Abs(dx) >= beside) linesBeside[y - linesTop]++;
                }
                if (d2 <= iconOut)
                {
                    icon++;
                    if (min >= 130 && max - min <= 40) iconLight++;
                }
                if (d2 >= fillIn && d2 <= fillOut && dy < -below && max < IconFrom)
                {
                    fill[max]++;
                    spread[max - min]++;
                    fills++;
                }
            }
        }
        // Lines of text over the button (an on-screen display's): its look is the display's, not the button's.
        bool text = Math.Max(BattleSkillButton.Bands(lines, scale), BattleSkillButton.Bands(linesBeside, scale)) >= BattleSkillButton.TextLines;
        // All round the rim: the share in the sector that is the RimSectorsSpared+1-th least lit.
        Span<double> shares = stackalloc double[RimSectors];
        for (int s = 0; s < RimSectors; s++) shares[s] = sector[s] == 0 ? 0 : sectorWhite[s] / (double)sector[s];
        shares.Sort();
        return new FlashSample(
            fills == 0 ? -1 : Median(fill, fills),
            fills == 0 ? -1 : Median(spread, fills),
            scenes == 0 ? -1 : Median(scene, scenes),
            rim == 0 ? 0 : rimWhite / (double)rim,
            text ? CoveredByText : icon == 0 ? 0 : iconLight / (double)icon,
            places)
        {
            RimAllRound = rim == 0 ? 0 : shares[RimSectorsSpared],
            SceneBright = scenes == 0 ? 0 : sceneBright / (double)scenes,
        };
    }

    /// <summary>The rim is looked at in this many sectors round the button...</summary>
    private const int RimSectors = 12;

    /// <summary>...of which this many may be dark (a face-button badge or the ultimate's circle beside the button, an effect over one side).</summary>
    private const int RimSectorsSpared = 2;

    /// <summary>The <see cref="FlashSample.Icon"/> of a button with lines of text over it (an on-screen display's): its glow can't be told.</summary>
    public const double CoveredByText = -1;

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
