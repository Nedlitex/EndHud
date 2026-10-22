namespace EndHud.Vision;

/// <summary>
/// A white number drawn on a game badge (Si's damage stacks). Not read yet.
/// </summary>
internal static class BadgeNumber
{
    public static int? Read(Frame frame, PixelRect area) => null;

    internal static readonly (int Digit, bool[] Mask)[] Shapes = [];
}
