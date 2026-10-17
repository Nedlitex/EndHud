namespace EndHud.Vision;

/// <summary>
/// Signs of squad-panel state that don't depend on the portraits or bars, which change with
/// combat, low HP and death.
/// </summary>
public static class PanelMarkers
{
    /// <summary>
    /// How many of the eight "-" marks beside the HP bars are visible (two per slot). Measured
    /// in exploration, combat, low-HP and dead states: all eight are found. Menus, loading
    /// screens and cutscenes don't draw them.
    /// </summary>
    public static int Count(Frame frame) => CountBySlot(frame).Sum();

    /// <summary>The marks found beside each slot's HP bar (0-2 per slot, slot order).</summary>
    public static int[] CountBySlot(Frame frame)
    {
        var ui = new UiScaler(frame.Width, frame.Height);
        var found = new int[PartyLayout.SlotCount];
        for (int slot = 0; slot < PartyLayout.SlotCount; slot++)
        {
            if (IsMark(frame, ui.BottomLeft(PartyLayout.ForSlot(PartyLayout.PanelMarkLeft, slot)))) found[slot]++;
            if (IsMark(frame, ui.BottomLeft(PartyLayout.ForSlot(PartyLayout.PanelMarkRight, slot)))) found[slot]++;
        }
        return found;
    }

    /// <summary>
    /// The controller layout draws an empty place in a squad of fewer than four as a large faint
    /// ring with a slash, where an operator's portrait would be (no HP bar). Measured on the
    /// owner's recording with two operators: the ring 58-60 px from the centre at 4K, about 25
    /// brighter than the band just inside it and the scenery just outside, all the way round.
    /// </summary>
    internal static bool IsEmptyPlace(Frame frame, PixelRect centre4K)
    {
        var ui = new UiScaler(frame.Width, frame.Height);
        double s = ui.Scale;
        var c = ui.BottomLeft(centre4K);
        double Mean(double from, double to) => MeanBrightness(frame, c.X, c.Y, from * s, to * s, s);
        double ring = Mean(57, 61), inside = Mean(48, 54), outside = Mean(64, 70);
        return ring - inside >= EmptyRingContrast && ring - outside >= EmptyRingContrast * 0.6;
    }

    private const double EmptyRingContrast = 12;

    /// <summary>Mean of the darkest channel on circles from <paramref name="inner"/> to <paramref name="outer"/> radius.</summary>
    private static double MeanBrightness(Frame frame, double cx, double cy, double inner, double outer, double scale)
    {
        double sum = 0;
        int n = 0;
        for (double radius = inner; radius <= outer + 0.01; radius += Math.Max(1, scale))
        {
            for (int i = 0; i < 72; i++)
            {
                double angle = i * Math.PI * 2 / 72;
                int x = (int)Math.Round(cx + Math.Cos(angle) * radius);
                int y = (int)Math.Round(cy + Math.Sin(angle) * radius);
                if ((uint)x >= (uint)frame.Width || (uint)y >= (uint)frame.Height) continue;
                var (r, g, b) = frame.GetPixel(x, y);
                sum += Math.Min(r, Math.Min(g, b));
                n++;
            }
        }
        return n == 0 ? 0 : sum / n;
    }

    /// <summary>
    /// A small light dash inside the window: a few light-grey pixels, not a light area. The
    /// marks are white normally (peak 240–255) and dim grey when the game dims the squad
    /// (peak 136–180), so the threshold sits below both.
    /// </summary>
    private static bool IsMark(Frame frame, PixelRect window)
    {
        var r = window.ClampTo(frame.Width, frame.Height);
        int white = 0;
        for (int y = r.Y; y < r.Bottom; y++)
        for (int x = r.X; x < r.Right; x++)
        {
            var (cr, cg, cb) = frame.GetPixel(x, y);
            int min = Math.Min(cr, Math.Min(cg, cb)), max = Math.Max(cr, Math.Max(cg, cb));
            if (min >= 120 && max - min <= 50) white++;
        }
        // The dash is ~5×2 px at 4K; a bright background would fill the window instead.
        double scale = Math.Max(r.Width, 1) * Math.Max(r.Height, 1) / 99.0;
        return white >= 4 * scale && white <= 30 * scale;
    }

    /// <summary>
    /// Has the operator in <paramref name="slot"/> fallen? Their portrait is replaced by a
    /// hatched square with a thin light-grey ⊘: a light ring with dark (hatched) inside and
    /// outside. Measured share of light-grey pixels:
    /// <code>
    ///                          ring   inside   just outside
    ///   death mark             ~0.61   ~0.08    ~0.02
    ///   Arcane alive (white hair) 0.56  0.69     0.54
    ///   other living portraits ≤ 0.26  ≤ 0.17   ≤ 0.31
    /// </code>
    /// </summary>
    public static bool IsDeathMark(Frame frame, int slot) =>
        IsDeathMark(frame, new PixelRect(PartyLayout.DeathMarkCentreX + (int)Math.Round(slot * PartyLayout.SlotPitch), PartyLayout.DeathMarkCentreY, 1, 1));

    /// <param name="centre4K">The mark's centre at 4K, anchored bottom-left (a 1×1 rectangle).
    /// The controller layout draws the same mark, same size, in its own place (SquadGeometry).</param>
    internal static bool IsDeathMark(Frame frame, PixelRect centre4K)
    {
        var ui = new UiScaler(frame.Width, frame.Height);
        double s = ui.Scale;
        var centre = ui.BottomLeft(centre4K);
        double cx = centre.X, cy = centre.Y;

        return LightGreyShare(frame, cx, cy, PartyLayout.DeathMarkRingInner * s, PartyLayout.DeathMarkRingOuter * s, s) >= DeathRingShare
               && LightGreyShare(frame, cx, cy, 2 * s, DeathMarkInsideRadius * s, s) <= MaxLightInside
               && LightGreyShare(frame, cx, cy, DeathMarkOutsideInner * s, DeathMarkOutsideOuter * s, s) <= MaxLightOutside;
    }

    private const double DeathRingShare = 0.4;
    private const int DeathMarkInsideRadius = 16;
    private const double MaxLightInside = 0.3; // the slash crosses the inside, so not zero
    private const int DeathMarkOutsideInner = 34;
    private const int DeathMarkOutsideOuter = 40;
    private const double MaxLightOutside = 0.12;

    /// <summary>Share of light, unsaturated pixels on circles from <paramref name="inner"/> to <paramref name="outer"/> radius.</summary>
    private static double LightGreyShare(Frame frame, double cx, double cy, double inner, double outer, double scale)
    {
        int grey = 0, total = 0;
        for (double radius = inner; radius <= outer + 0.01; radius += 2 * scale)
        {
            for (int i = 0; i < 72; i++)
            {
                double angle = i * Math.PI * 2 / 72;
                int x = (int)Math.Round(cx + Math.Cos(angle) * radius);
                int y = (int)Math.Round(cy + Math.Sin(angle) * radius);
                if ((uint)x >= (uint)frame.Width || (uint)y >= (uint)frame.Height) continue;
                var (cr, cg, cb) = frame.GetPixel(x, y);
                int min = Math.Min(cr, Math.Min(cg, cb)), max = Math.Max(cr, Math.Max(cg, cb));
                total++;
                if (min >= 140 && max - min <= 40) grey++;
            }
        }
        return total == 0 ? 0 : grey / (double)total;
    }
}
