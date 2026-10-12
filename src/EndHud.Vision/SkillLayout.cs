namespace EndHud.Vision;

/// <summary>
/// The 终结技 (ultimate) indicators in the bottom-right corner: one small circle per squad slot,
/// in slot order, above that operator's large 战技 (battle skill) button. Measured on a
/// 3840×2160 recording and anchored to the bottom-right corner.
/// <list type="bullet">
/// <item>Ready: the circle is filled with the operator's element colour behind a white icon,
/// has a white outer ring, and the slot-number tag shows above it.</item>
/// <item>Charging: a dark disc inside a thin grey ring, no tag; the charge is an arc of element
/// colour growing clockwise from 12 o'clock just inside the ring.</item>
/// </list>
/// </summary>
internal static class SkillLayout
{
    /// <summary>Centre of slot 1's ultimate circle.</summary>
    public const int FirstCentreX = 3155;
    public const int CentreY = 1783;

    /// <summary>Horizontal distance between neighbouring circles.</summary>
    public const double Pitch = 191.7;

    /// <summary>The disc inside the circle: element colour when ready, dark when charging.</summary>
    public const int DiscRadius = 28;

    /// <summary>The thin grey ring drawn around the empty disc while charging.</summary>
    public const int ChargingRingRadius = 34;

    /// <summary>The charge arc's band (measured: radius 16–26, solid 18–24).</summary>
    public const int ArcInnerRadius = 18;
    public const int ArcOuterRadius = 24;

    /// <summary>Inside the arc: empty and dark while charging, the white icon when ready.</summary>
    public const int EmptyCentreRadius = 13;

    /// <summary>The white icon of a ready ultimate.</summary>
    public const int IconRadius = 18;
}
