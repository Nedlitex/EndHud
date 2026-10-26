namespace EndHud;

/// <summary>
/// Size and place of the alert pop-ups (<see cref="AlertPopUp"/>) over the game window, in the overlay's units. Plain
/// arithmetic, so it can be checked for any window and size without a screen.
/// <para>
/// The pop-ups are the combo hint's mirror image: their right edge at the window's width less the combo hint's left
/// edge, their top at its top. They're drawn at the combo hint's scale times the player's size (the alerts card's
/// size slider, <see cref="HudConfig.AlertScale"/>). A bigger size doesn't push them over the game's centre (the
/// character) or onto the combo hint: their text wraps sooner instead, down to <see cref="MinTextWidth"/>. They stay
/// on the window wherever the combo hint is dragged.
/// </para>
/// </summary>
internal static class AlertLayout
{
    /// <summary>The frame around the pop-ups' stack (OverlayWindow.xaml's AlertFrame): its padding and border, both sides.</summary>
    public const double FrameChrome = 2 * (6 + 2);

    /// <summary>The pop-ups keep this share of the window's width clear on the right of its centre line (the character).</summary>
    public const double ClearOfCentre = 0.04;

    /// <summary>The narrowest the text gets before it wraps (four characters a line), however little room there is.</summary>
    public const double MinTextWidth = 80;

    /// <summary>The fullest stack's height, unscaled: the menu's sample and the most pop-ups shown at once, in the frame.</summary>
    public const double StackHeight = FrameChrome + (AlertFollower.MostShown + 1) * (AlertPopUp.CircleSize + ComboBanner.Gap);

    /// <summary>The scale the pop-ups are drawn at: following the game window (<see cref="ComboHintLayout.Scale"/>), times the player's size.</summary>
    public static double Scale(double windowScale, double size) => windowScale * size;

    /// <summary>
    /// The pop-ups' text width before it wraps (in their own units, before <paramref name="scale"/>), for a stack whose
    /// right edge is at <paramref name="right"/>: as wide as <see cref="AlertPopUp.TextMaxWidth"/> where there's room, and
    /// narrower where the stack would reach into the centre's clear band (a stack right of the centre), or past the
    /// window's left edge (a combo hint dragged right of the centre), but never under <see cref="MinTextWidth"/>.
    /// </summary>
    public static double TextMaxWidth(double right, double windowWidth, double scale)
    {
        if (windowWidth <= 0 || scale <= 0) return AlertPopUp.TextMaxWidth;
        double keepClear = windowWidth * (0.5 + ClearOfCentre);
        double room = right >= keepClear ? right - keepClear : right;
        double text = room / scale - AlertPopUp.Chrome - FrameChrome;
        return Math.Clamp(text, MinTextWidth, AlertPopUp.TextMaxWidth);
    }

    /// <summary>The stack's top: the combo hint's <paramref name="comboTop"/>, raised where the fullest stack would run off the window's bottom.</summary>
    public static double Top(double comboTop, double windowHeight, double scale) =>
        Math.Clamp(comboTop, 0, Math.Max(0, windowHeight - StackHeight * scale));

    /// <summary>The stack's left edge, for its right edge at <paramref name="right"/> and its drawn width: moved right where it would start off the window.</summary>
    public static double Left(double right, double width, double windowWidth) =>
        Math.Clamp(right - width, 0, Math.Max(0, windowWidth - width));
}
