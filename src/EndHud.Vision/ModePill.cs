namespace EndHud.Vision;

/// <summary>What the mode switch at the top left of the screen says about the battle.</summary>
public enum ModePillReading
{
    /// <summary>No mode switch there: a battle in an instance (the evacuate bar, 撤离, is drawn in its
    /// place), a menu, a loading screen, or the switch covered.</summary>
    NotShown,

    /// <summary>The mode switch (探索 / 工业, exploring or building) with its plain icon on the left: the
    /// player is out of battle.</summary>
    OutOfBattle,

    /// <summary>The mode switch with its left icon turned red: the game marks a fight in the open world.</summary>
    Fighting,
}

/// <summary>
/// The mode switch at the top left (探索 exploring, 工业 building): a toggle drawn as a dark capsule
/// with a round icon at each end (left: a lightning bolt with keyboard and mouse, a D-pad arrow with
/// a controller; right: a compass) and a white slider over the chosen side with the mode's name,
/// "TAB ⇌" above it with keyboard and mouse. The game draws it in the open world only; an
/// instance's battle draws the evacuate bar (撤离, a white rounded rectangle from x 36 to x 356 at 4K)
/// there instead. While a fight is on in the open world the left icon turns red (the owner's
/// controller recording battle_controller_test4.mp4).
/// <para>Found by its shape, never by reading the word: the slider's free round end, 67 px high at 4K,
/// with the dark capsule around it. Exploring, the slider covers the compass and its free end is on
/// the left (x 112, <see cref="ExploringEnd"/>); building, it covers the lightning bolt and its free
/// end is on the right (x 390, <see cref="BuildingEnd"/>). Both areas lie beside the name, so the
/// word never enters the comparison. The evacuate bar is white past the exploring end's left edge and
/// stops short of the building end, overlapping each about two thirds.</para>
/// </summary>
public static class ModePill
{
    /// <summary>Around the exploring slider's left end, at 3840×2160, anchored top-left.</summary>
    internal static readonly PixelRect ExploringEnd = new(98, 60, 102, 96);

    /// <summary>Around the building slider's right end (left of the compass), anchored top-left.</summary>
    internal static readonly PixelRect BuildingEnd = new(294, 60, 100, 96);

    /// <summary>The left icon (lightning, D-pad arrow, or the red fight mark), anchored top-left.</summary>
    internal static readonly PixelRect Icon = new(50, 80, 54, 54);

    // The slider at 4K: top 74, bottom 141 (round ends of radius 33.5). Exploring it starts at x 112,
    // building it ends at x 390.
    private const double Top = 74, Bottom = 141, ExploringLeft = 112, BuildingRight = 390;

    /// <summary>Overlap (intersection over union) either end needs; see <see cref="Measure"/>.</summary>
    public const double MatchAt = 0.8;

    /// <summary>Share of the left icon's pixels that are red when the game marks a fight.</summary>
    public const double RedAt = 0.15;

    private const int WhiteMin = 200;

    /// <summary>Shift allowed, in 4K pixels (rounding at smaller sizes).</summary>
    private const int SearchRadius = 6;

    private static readonly string[] ExploringShape = BuildShape(ExploringEnd, ExploringLeft, double.MaxValue);
    private static readonly string[] BuildingShape = BuildShape(BuildingEnd, double.MinValue, BuildingRight);

    /// <summary>What the mode switch says on this frame (the 16:9 picture, as every HUD reader gets it).</summary>
    public static ModePillReading Read(Frame frame, UiScaler ui)
    {
        var (shape, red) = Measure(frame, ui);
        if (shape < MatchAt) return ModePillReading.NotShown;
        return red >= RedAt ? ModePillReading.Fighting : ModePillReading.OutOfBattle;
    }

    /// <summary>The slider's overlap with its shape (0–1, the better of the two modes) and the red share
    /// of the left icon (0–1), for tuning and diagnostics.</summary>
    public static (double Shape, double Red) Measure(Frame frame, UiScaler ui)
    {
        int radius = Math.Max(1, (int)Math.Round(SearchRadius * ui.Scale));
        double shape = Math.Max(
            WhiteShape.Score(frame, ui.TopLeft(ExploringEnd), ExploringShape, WhiteMin, radius),
            WhiteShape.Score(frame, ui.TopLeft(BuildingEnd), BuildingShape, WhiteMin, radius));
        return (shape, RedShare(frame, ui.TopLeft(Icon)));
    }

    /// <summary>Share of strongly red pixels (the fight mark's crimson) in <paramref name="rect"/>.</summary>
    private static double RedShare(Frame frame, PixelRect rect)
    {
        rect = rect.ClampTo(frame.Width, frame.Height);
        if (rect.IsEmpty) return 0;
        int red = 0;
        for (int y = rect.Y; y < rect.Bottom; y++)
        for (int x = rect.X; x < rect.Right; x++)
        {
            var (r, g, b) = frame.GetPixel(x, y);
            if (r >= 150 && r - Math.Max(g, b) >= 80) red++;
        }
        return red / (double)(rect.Width * rect.Height);
    }

    /// <summary>The slider (from <paramref name="left"/> to <paramref name="right"/>, round ends) over
    /// <paramref name="area"/>, one character per 2 px at 4K.</summary>
    private static string[] BuildShape(PixelRect area, double left, double right)
    {
        const int step = 2;
        double radius = (Bottom - Top) / 2, centreY = (Top + Bottom) / 2;
        var rows = new string[area.Height / step];
        for (int row = 0; row < rows.Length; row++)
        {
            double y = area.Y + row * step + step / 2.0;
            var chars = new char[area.Width / step];
            for (int col = 0; col < chars.Length; col++)
            {
                double x = area.X + col * step + step / 2.0;
                // Inside when within the radius of the slider's centre line.
                double cx = Math.Clamp(x, left + radius, right - radius);
                chars[col] = Math.Pow(x - cx, 2) + Math.Pow(y - centreY, 2) <= radius * radius ? '#' : '.';
            }
            rows[row] = new string(chars);
        }
        return rows;
    }
}
