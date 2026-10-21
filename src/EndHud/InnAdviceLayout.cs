using System.Windows;

namespace EndHud;

/// <summary>
/// Where each piece of setup advice goes: in a blank part of the game's own screen next to the
/// choice it's about, so it follows the game's layout and needs no dragging. The game lays its
/// setup screens out on the window's centred 16:9 area, scaled with it (as the inn's readers
/// assume, see InnLayout); each spot is a fraction of that area, measured on the 4K fixtures:
/// <list type="bullet">
/// <item><b>Under a kitchen's card</b> (the setup screen, setup-chefs-placed.jpg; the owner's 4K screenshot): the
/// cards stand at x 0.418–0.588, 0.594–0.764 and 0.771–0.941; under a card's chef bonus rows (or under
/// the place-a-chef button of an empty card, at y 0.53) the card is blank from y ≈ 0.66 to its bottom edge at
/// 0.865. The spot is y 0.665–0.855, inset from the card's sides.</item>
/// </list>
/// </summary>
internal static class InnAdviceLayout
{
    /// <summary>A spot as fractions of the centred 16:9 area: its left, top, width and height.</summary>
    internal readonly record struct Fractions(double Left, double Top, double Width, double Height);

    /// <summary>The spot of <paramref name="spot"/> on the 16:9 area.</summary>
    public static Fractions Of(InnAdviceSpot spot) => spot switch
    {
        InnAdviceSpot.Kitchen1 => new(0.424, 0.665, 0.158, 0.19),
        InnAdviceSpot.Kitchen2 => new(0.600, 0.665, 0.158, 0.19),
        InnAdviceSpot.Kitchen3 => new(0.777, 0.665, 0.158, 0.19),
        _ => throw new ArgumentOutOfRangeException(nameof(spot), spot, null),
    };

    /// <summary>The spot in a game window of <paramref name="width"/> × <paramref name="height"/> (the overlay's units).</summary>
    public static Rect Place(InnAdviceSpot spot, double width, double height)
    {
        double unit = Math.Min(width / 16, height / 9);
        double areaWidth = unit * 16, areaHeight = unit * 9;
        double left = (width - areaWidth) / 2, top = (height - areaHeight) / 2;
        var f = Of(spot);
        return new Rect(left + f.Left * areaWidth, top + f.Top * areaHeight, f.Width * areaWidth, f.Height * areaHeight);
    }

    /// <summary>
    /// The width of a panel's lines (<see cref="InnStrip"/>, in its 1080p units) so that the panel,
    /// drawn at <paramref name="scale"/>, is as wide as its spot at most.
    /// </summary>
    public static double LineWidth(InnAdviceSpot spot, double width, double height, double scale) =>
        Math.Max(InnStrip.PictureSize * 3, Place(spot, width, height).Width / scale - InnStripLook.Normal.ChromeWidth);
}
