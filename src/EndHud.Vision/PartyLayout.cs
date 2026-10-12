namespace EndHud.Vision;

/// <summary>
/// Where the squad panel (bottom-left: four operator portraits with level and HP bar) sits,
/// measured on a 3840×2160 screenshot and anchored to the bottom-left corner.
/// </summary>
internal static class PartyLayout
{
    public const int SlotCount = 4;

    /// <summary>Horizontal distance between neighbouring slots.</summary>
    public const double SlotPitch = 233.67;

    /// <summary>"LV.90" under the first portrait (stops short of the food-buff icon and the HP bar).</summary>
    public static readonly PixelRect LevelText = new(78, 1940, 128, 45);

    /// <summary>
    /// Just the rows the "LV.90" glyphs occupy (1949–1973) plus a pixel each side. Fallback for low
    /// resolutions, where the glyphs touch bright portrait pixels above/below and would otherwise
    /// be discarded with them.
    /// </summary>
    public static readonly PixelRect LevelGlyphRows = new(78, 1948, 128, 27);

    /// <summary>The coloured part of the first HP bar.</summary>
    public static readonly PixelRect HpBar = new(85, 1986, 166, 12);

    /// <summary>
    /// The combo skill cooldown bar directly above the HP bar, shown in combat in place of
    /// the level text: grey fill growing from the left while cooling down, solid white when ready.
    /// Only the bar's inner rows (1978–1981): at low HP a pink frame line runs along row 1977.
    /// </summary>
    public static readonly PixelRect ComboBar = new(85, 1978, 166, 4);

    /// <summary>
    /// The small white "-" marks either side of each HP bar. Drawn in every state — exploration,
    /// combat, low HP, dead — so finding them is the sign that the squad panel is on screen.
    /// </summary>
    public static readonly PixelRect PanelMarkLeft = new(68, 1988, 11, 9);
    public static readonly PixelRect PanelMarkRight = new(257, 1988, 11, 9);

    /// <summary>
    /// Centre of the ⊘ symbol that replaces a fallen operator's portrait (on a hatched square),
    /// and the radii of its light-grey ring.
    /// </summary>
    public const int DeathMarkCentreX = 166;
    public const int DeathMarkCentreY = 1905;
    public const int DeathMarkRingInner = 24;
    public const int DeathMarkRingOuter = 30;

    /// <summary>
    /// The first operator's head portrait, from the top of the hair to the HP bar. The face sits
    /// low: the level text covers the mouth and chin, and the food-buff icon the right cheek
    /// (PortraitMatcher ignores both areas).
    /// </summary>
    public static readonly PixelRect Portrait = new(81, 1826, 128, 160);

    public static PixelRect ForSlot(PixelRect first, int slot) =>
        first.Offset((int)Math.Round(slot * SlotPitch), 0);
}
