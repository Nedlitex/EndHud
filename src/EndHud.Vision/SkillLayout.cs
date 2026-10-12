namespace EndHud.Vision;

/// <summary>
/// The four ultimate (终结技) buttons in the bottom-right corner, one per squad slot in slot
/// order, measured on a 3840×2160 recording and anchored to the bottom-right corner. Each is a
/// dark disc that fills with the operator's element colour from the bottom as the ultimate
/// charges, and gains a bright white ring when it is ready.
/// </summary>
internal static class SkillLayout
{
    /// <summary>Centre of slot 1's ultimate button.</summary>
    public const int FirstCentreX = 3155;
    public const int CentreY = 1932;

    /// <summary>Horizontal distance between neighbouring buttons.</summary>
    public const double Pitch = 192;

    /// <summary>Radius of the inner disc that holds the charge fill.</summary>
    public const int FillRadius = 61;

    /// <summary>The grey ring around the disc (present whenever the button is on screen).</summary>
    public const int RingRadius = 70;

    /// <summary>
    /// When the ultimate is ready a bright white ring plus an expanding ripple cover this band
    /// (measured over the recording: radius 62–96 px at 4K). Nothing white is drawn here otherwise.
    /// </summary>
    public const int ReadyBandInner = 62;
    public const int ReadyBandOuter = 96;

    /// <summary>
    /// The button icon covers the middle of the disc; the fill level is read on vertical lines
    /// this far left and right of centre, where only fill or empty disc shows.
    /// </summary>
    public const int SampleOffsetX = 47;
}
