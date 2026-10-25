namespace EndHud.Vision;

/// <summary>
/// The evacuate bar (撤离) at the top left: the game draws it in an instance (a dungeon, a boss
/// battle), never in the open world, where the mode switch (<see cref="ModePill"/>) or an open-world
/// event's own buttons sit there instead. So it tells an instance's battle from a fight in the open
/// world (owner, 2026-10-25: "look at the left top for difference, if there is 撤离 that is battle").
/// <para>
/// A white rounded box, x 36-356 and y 68-146 at 4K, anchored top-left: an icon on its left (a door
/// with an arrow with keyboard and mouse, "ESC" above the box; a round dark menu button with a
/// controller) and the word 撤离 in black in its middle. Found by its parts, never by OCR: the box's
/// white where nothing is drawn on it (above and below the word, the gap between the icon and the word,
/// right of the word), dark ink in the icon's place, and ink in each of the word's two characters.
/// The glyphs are not compared pixel by pixel: the same word renders a pixel or two differently from
/// capture to capture (the keyboard and the controller fixtures differ), and the ink shares are as
/// telling. The scenery around the box is never compared, as a bright sky sits right against it
/// (Fixtures/mode-switch/controller-evacuate-bar.jpg).
/// </para>
/// <para>
/// Overlays drawn into the game (RivaTuner's on-screen display sits at the top left by default) can
/// only hide it: text and graphs don't make a white box, and a display's dark backdrop greys it. So a
/// covered bar reads not seen, which says nothing either way, and <see cref="PartyTracker"/> keeps the
/// battle kind last seen.
/// </para>
/// </summary>
public static class EvacuateBar
{
    // Where nothing is drawn on the box (4K, anchored top-left): must be white.
    private static readonly PixelRect[] Blank =
    [
        new(44, 71, 304, 7),    // above the icon and the word
        new(44, 140, 304, 4),   // below them (the small grey marks under the word end at y 137)
        new(118, 82, 56, 54),   // between the icon and the word
        new(292, 82, 54, 54),   // right of the word
    ];

    /// <summary>The icon on the box's left: a door, or the controller's round menu button. Dark either way.</summary>
    internal static readonly PixelRect Icon = new(56, 84, 50, 48);

    /// <summary>The word's two characters, 撤 and 离.</summary>
    internal static readonly PixelRect FirstCharacter = new(184, 82, 42, 42);
    internal static readonly PixelRect SecondCharacter = new(232, 84, 40, 40);

    /// <summary>Share of the blank areas that must be white.</summary>
    public const double WhiteAt = 0.9;

    /// <summary>Share of the icon's place that must be dark.</summary>
    public const double IconInkAt = 0.25;

    /// <summary>Ink share each character must have, and must not exceed (a dark patch isn't a character).</summary>
    public const double CharacterInkMin = 0.15, CharacterInkMax = 0.6;

    private const int WhiteMin = 200;
    private const int InkMax = 110;

    /// <summary>Is the evacuate bar on this frame (the 16:9 picture, as every HUD reader gets it)?</summary>
    public static bool Read(Frame frame, UiScaler ui)
    {
        var m = Measure(frame, ui);
        return m.White >= WhiteAt
            && m.First is >= CharacterInkMin and <= CharacterInkMax
            && m.Second is >= CharacterInkMin and <= CharacterInkMax;
    }

    /// <summary>The blank areas' white share, and the ink shares of the icon and of each character (0-1),
    /// for tuning and diagnostics.</summary>
    public static (double White, double Icon, double First, double Second) Measure(Frame frame, UiScaler ui)
    {
        int white = 0, all = 0;
        foreach (var area in Blank)
        {
            var (w, n) = Count(frame, ui.TopLeft(area), (r, g, b) => Math.Min(r, Math.Min(g, b)) > WhiteMin);
            white += w;
            all += n;
        }
        return (all == 0 ? 0 : white / (double)all, Ink(frame, ui.TopLeft(Icon)),
            Ink(frame, ui.TopLeft(FirstCharacter)), Ink(frame, ui.TopLeft(SecondCharacter)));
    }

    private static double Ink(Frame frame, PixelRect rect)
    {
        var (ink, n) = Count(frame, rect, (r, g, b) => Math.Max(r, Math.Max(g, b)) < InkMax);
        return n == 0 ? 0 : ink / (double)n;
    }

    private static (int Hits, int Pixels) Count(Frame frame, PixelRect rect, Func<byte, byte, byte, bool> hit)
    {
        rect = rect.ClampTo(frame.Width, frame.Height);
        if (rect.IsEmpty) return (0, 0);
        int hits = 0;
        for (int y = rect.Y; y < rect.Bottom; y++)
        for (int x = rect.X; x < rect.Right; x++)
        {
            var (r, g, b) = frame.GetPixel(x, y);
            if (hit(r, g, b)) hits++;
        }
        return (hits, rect.Width * rect.Height);
    }
}
