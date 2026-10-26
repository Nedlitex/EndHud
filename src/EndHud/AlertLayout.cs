namespace EndHud;

/// <summary>
/// Size and place of the alert pop-ups (<see cref="AlertPopUp"/>) over the game window, in the overlay's units. Plain
/// arithmetic, so it can be checked for any window and size without a screen.
/// </summary>
internal static class AlertLayout
{
    /// <summary>The frame around the pop-ups' stack (OverlayWindow.xaml's AlertFrame): its padding and border, both sides.</summary>
    public const double FrameChrome = 2 * (6 + 2);

    /// <summary>The pop-ups keep this share of the window's width clear on the right of its centre line (the character).</summary>
    public const double ClearOfCentre = 0.04;

    /// <summary>The narrowest the text gets before it wraps (four characters a line), however little room there is.</summary>
    public const double MinTextWidth = 80;

    /// <summary>The scale the pop-ups are drawn at: following the game window (<see cref="ComboHintLayout.Scale"/>), times the player's size.</summary>
    public static double Scale(double windowScale, double size) => windowScale;

    /// <summary>The pop-ups' text width before it wraps, for a stack whose right edge is at <paramref name="right"/>.</summary>
    public static double TextMaxWidth(double right, double windowWidth, double scale) => AlertPopUp.TextMaxWidth;

    /// <summary>The stack's top for the combo hint's top <paramref name="comboTop"/>.</summary>
    public static double Top(double comboTop, double windowHeight, double scale) => comboTop;

    /// <summary>The stack's left edge, for its right edge at <paramref name="right"/> and its drawn width.</summary>
    public static double Left(double right, double width, double windowWidth) => right - width;
}
