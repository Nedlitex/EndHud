namespace EndHud;

/// <summary>
/// Size and position of the combo-ready hints over the game window. Plain arithmetic, so it can
/// be checked for any window size and display scaling without a screen.
/// <para>
/// WPF lays the overlay out in device-independent units: one unit is one physical pixel at 100%
/// display scaling, 1.5 pixels at 150%, and so on (<c>dpiScale</c>). The banner should be the same
/// share of the game window whatever the scaling, so its size is worked out in physical pixels
/// and only then converted to units.
/// </para>
/// </summary>
internal static class ComboHintLayout
{
    /// <summary>The banner is drawn for a game window this many physical pixels tall, and scaled with it.</summary>
    public const double ReferenceHeight = 1080;

    /// <summary>
    /// Limits on that scaling, in physical pixels: below 0.6 the name would be under about 12
    /// pixels tall; above 2.5 (a game window over 2700 pixels tall) it would only get bigger.
    /// </summary>
    public const double MinScale = 0.6, MaxScale = 2.5;

    /// <summary>How much of the hints, in units, stays inside the window when placed or dragged.</summary>
    public const double MinVisible = 80;

    /// <summary>
    /// The scale to apply to the banner (in units) for a game window <paramref name="windowHeight"/>
    /// units tall at display scaling <paramref name="dpiScale"/>.
    /// </summary>
    public static double Scale(double windowHeight, double dpiScale)
    {
        double physical = Math.Clamp(windowHeight * dpiScale / ReferenceHeight, MinScale, MaxScale);
        return physical / dpiScale;
    }

    /// <summary>The hints' top-left corner for a wanted position, kept at least partly inside the window.</summary>
    public static (double Left, double Top) Place(double left, double top, double windowWidth, double windowHeight) => (
        Math.Clamp(left, 0, Math.Max(0, windowWidth - MinVisible)),
        Math.Clamp(top, 0, Math.Max(0, windowHeight - MinVisible)));
}
