namespace EndHud.Vision;

/// <summary>
/// Which HUD the game draws. It follows the device the player last used, so it can change in
/// the middle of a fight (seen in the controller recording: controller, then keyboard and mouse
/// for the last seconds).
/// </summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum HudLayout
{
    /// <summary>Keyboard and mouse: the squad in a row bottom-left, the skill buttons in a row bottom-right.</summary>
    KeyboardMouse,

    /// <summary>Controller: the squad in a diamond bottom-left around an "LB" badge, the skill buttons
    /// in a diamond bottom-right around another.</summary>
    Controller,

    /// <summary>Controller with LB held (to cast skills): the squad diamond spreads 16 px apart around
    /// a D-pad, and the skill diamond grows. Everything read in <see cref="Controller"/> is still drawn.</summary>
    ControllerSkillsHeld,
}

/// <summary>
/// Tells the layouts apart by the controller layout's own marks: the white "LB" letters in the
/// middle of each diamond, and the D-pad that replaces the squad's while LB is held. Keyboard and
/// mouse draw none of them there.
/// </summary>
public static class HudLayoutDetector
{
    // Overlap of white pixels with the shapes (0–1), measured: the letters score about 0.9 in the
    // controller layout (an effect over one of them lowers it; there are two) and 0.11 at most on
    // keyboard-and-mouse screens. In the controller recording, read every 0.5 s, the layout was
    // found in all 114 readings in combat (98 controller, 16 LB held); no screenshot in the test
    // data is taken for a controller layout, at 4K or 1080p (ControllerLayoutTests).
    private const double MatchAt = 0.5;
    private const int LetterWhiteMin = 170;
    private const int SearchRadius = 3;

    /// <summary>The "LB" letters in the middle of the skill diamond, anchored bottom-right.</summary>
    private static readonly PixelRect SkillsLetters = new(3532, 1836, 42, 30);

    /// <summary>The "LB" letters in the middle of the squad diamond, anchored bottom-left.</summary>
    private static readonly PixelRect SquadLetters = new(441, 1836, 42, 30);

    // A PlayStation controller labels the same badges "L1" (a player's screenshot, 0.6.2), and the
    // "LB" letters above don't match it: the HUD read as keyboard and mouse and the squad was never
    // found unless L1 was held (the D-pad). The "L" both labels share, alone, scores 0.89 or more in
    // every controller fixture and 0.43 at most on keyboard-and-mouse ones; the search is wider
    // because "L1" is narrower than "LB", so its "L" sits further right in the badge.
    private const double LetterLMatchAt = 0.7;
    private const int LetterLSearchRadius = 8;

    /// <summary>The "L" of the squad badge's "LB" or "L1", anchored bottom-left.</summary>
    private static readonly PixelRect SquadLetterL = new(441, 1836, 22, 30);

    /// <summary>The "L" of the skill badge's "LB" or "L1", anchored bottom-right.</summary>
    private static readonly PixelRect SkillsLetterL = new(3532, 1836, 22, 30);

    /// <summary>The D-pad in the middle of the squad diamond while LB is held, anchored bottom-left.</summary>
    private static readonly PixelRect SquadDpad = new(414, 1798, 98, 99);

    public static HudLayout Detect(Frame frame, UiScaler ui)
    {
        if (WhiteShape.Score(frame, ui.BottomRight(SkillsLetters), SkillsLbShape, LetterWhiteMin, SearchRadius) >= MatchAt
            || WhiteShape.Score(frame, ui.BottomLeft(SquadLetters), SquadLbShape, LetterWhiteMin, SearchRadius) >= MatchAt
            || WhiteShape.Score(frame, ui.BottomRight(SkillsLetterL), LetterLShape, LetterWhiteMin, LetterLSearchRadius) >= LetterLMatchAt
            || WhiteShape.Score(frame, ui.BottomLeft(SquadLetterL), LetterLShape, LetterWhiteMin, LetterLSearchRadius) >= LetterLMatchAt)
            return HudLayout.Controller;
        if (WhiteShape.Score(frame, ui.BottomLeft(SquadDpad), DpadShape, LetterWhiteMin, SearchRadius) >= MatchAt)
            return HudLayout.ControllerSkillsHeld;
        return HudLayout.KeyboardMouse;
    }

    /// <summary>
    /// The skill diamond's "LB" (or "L1") badge is in its middle: the plain controller diamond, not the one LB held
    /// draws, which has none there. For the battle-skill corner alone (<see cref="BattleSkillFlash.PlacesOf"/>).
    /// </summary>
    /// <param name="image">A picture whose bottom-right corner is the game picture's (the whole picture, or a corner cut from it).</param>
    /// <param name="scale">The game picture's HUD scale.</param>
    internal static bool SkillsBadgeShown(Frame image, double scale) =>
        WhiteShape.Score(image, FromBottomRight(image, scale, SkillsLetters), SkillsLbShape, LetterWhiteMin, SearchRadius) >= MatchAt
        || WhiteShape.Score(image, FromBottomRight(image, scale, SkillsLetterL), LetterLShape, LetterWhiteMin, LetterLSearchRadius) >= LetterLMatchAt;

    /// <summary>A 4K rectangle anchored bottom right, on a picture (or a corner of it) at this scale.</summary>
    private static PixelRect FromBottomRight(Frame image, double scale, PixelRect r) => new(
        image.Width - (int)Math.Round((UiScaler.ReferenceWidth - r.X) * scale),
        image.Height - (int)Math.Round((UiScaler.ReferenceHeight - r.Y) * scale),
        Math.Max(1, (int)Math.Round(r.Width * scale)),
        Math.Max(1, (int)Math.Round(r.Height * scale)));

    private static readonly string[] SkillsLbShape =
    [
        "..........................................",
        "..........................................",
        "..........................................",
        "..........................................",
        ".....##...............############........",
        "....####..............#############.......",
        "....####..............##############......",
        "....####..............####.......####.....",
        "....####..............###........####.....",
        "....####..............###.........###.....",
        "....####..............###.........###.....",
        "....####..............###........####.....",
        "....####..............####......####......",
        "....####..............##############......",
        "....####..............##############......",
        "....####..............###############.....",
        "....####..............###.........####....",
        "....####..............###..........###....",
        "....####..............###..........####...",
        "....####..............###..........####...",
        "....####..............###..........####...",
        "....####..............###..........####...",
        "....####..............###.........####....",
        "....###############...####....#######.....",
        "....###############...##############......",
        "....###############...#############.......",
        "..........................................",
        "..........................................",
        "..........................................",
        "..........................................",
    ];

    private static readonly string[] SquadLbShape =
    [
        "..........................................",
        "..........................................",
        "..........................................",
        "..........................................",
        ".....##................##########.........",
        "....####..............#############.......",
        "....####..............##############......",
        "....####..............####......#####.....",
        "....####..............###.........###.....",
        "....####..............###.........####....",
        "....####..............###.........####....",
        "....####..............###.........###.....",
        "....####..............####.......####.....",
        "....####..............##############......",
        "....####..............##############......",
        "....####..............##############......",
        "....####..............####......######....",
        "....####..............###..........###....",
        "....####..............###..........####...",
        "....####..............###..........####...",
        "....####..............###..........####...",
        "....####..............###..........####...",
        "....####..............###..........####...",
        "....###############...###........#####....",
        "....###############...###############.....",
        "....###############...##############......",
        ".....##############....############.......",
        "..........................................",
        "..........................................",
        "..........................................",
    ];

    /// <summary>At half the 4K size: resampled to the rectangle's size like the others.</summary>
    private static readonly string[] DpadShape =
    [
        ".................................................",
        ".................................................",
        "....................########.....................",
        "..................#.############.................",
        ".................#.............#.................",
        ".................#..............#................",
        "................#...##########..#................",
        "................#..###########..##...............",
        "................#..###########...#...............",
        "................#..###########...#...............",
        "................#..###########..##...............",
        "................#..###########..##...............",
        "................#..###########..##...............",
        "................#..###########..##...............",
        "................#..###########..##...............",
        "................#..###########..##...............",
        ".......##########...##########..############.....",
        "....####.#.#######...########...####.#......##...",
        "...##.............#...######.................##..",
        "...#...................####......................",
        "..##..##########........##.......###########...#.",
        "..##..###########...............############..#..",
        "..##..#############............#############..##.",
        "..##..#############....####...##############..##.",
        "..##..###############..###...###############..##.",
        "..##..###############..####..###############..##.",
        "..##..##############...####..###############...#.",
        "..##..#############............#############...#.",
        "..##..############...#..........############..##.",
        "..##..###########...#...#........###########..##.",
        "...#...#########.......####...#...#########...#..",
        "...#..............#...######.....................",
        "....#............#...########...#................",
        "......###########...#########...#####....##......",
        "................#..###########..#................",
        "................#..###########..##...............",
        "................#..###########..#................",
        "................#..###########..##...............",
        "................#..###########..##...............",
        "................#..###########..##...............",
        "................#..###########..##...............",
        "................#..###########..##...............",
        "................#..###########...#...............",
        "................#...##########..#................",
        "................#................................",
        "................................#................",
        "..................#############..................",
        "....................##########...................",
        ".................................................",
        ".................................................",
    ];

    /// <summary>The "L" of <see cref="SquadLbShape"/> (its left 22 columns). After it: static fields
    /// are set in the order they're written.</summary>
    private static readonly string[] LetterLShape = [.. SquadLbShape.Select(row => row[..22])];
}
