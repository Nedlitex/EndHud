namespace EndHud.Vision;

/// <param name="Charge">
/// How high the element-coloured fill reaches in the disc, in [0, 1]; null when something is
/// drawn over the button. NOTE: in the only recording so far this stays at ~25% for every
/// operator, including at the moment an ultimate becomes ready, so it may not be the charge.
/// </param>
/// <param name="Ready">The bright white ring is showing: the 终结技 (ultimate) can be used.</param>
public sealed record UltimateReading(double? Charge, bool Ready);

/// <summary>Reads the ultimate buttons in the bottom-right corner (see <see cref="SkillLayout"/>).</summary>
public static class UltimateGauge
{
    /// <param name="slot">Squad slot, 0-based (button order matches the squad panel).</param>
    /// <returns>The reading, or null when the button isn't on screen.</returns>
    public static UltimateReading? Measure(Frame frame, int slot)
    {
        var ui = new UiScaler(frame.Width, frame.Height);
        var (cx, cy) = ui.BottomRight(SkillLayout.FirstCentreX + slot * SkillLayout.Pitch, SkillLayout.CentreY);
        double s = ui.Scale;

        bool ready = WhiteFraction(frame, cx, cy, SkillLayout.ReadyBandInner * s, SkillLayout.ReadyBandOuter * s) >= ReadyWhiteFraction;
        if (!ready && !RingVisible(frame, cx, cy, SkillLayout.RingRadius * s)) return null;

        double radius = SkillLayout.FillRadius * s;
        double offset = SkillLayout.SampleOffsetX * s;
        double left = WaterLine(frame, cx - offset, cy, radius, offset);
        double right = WaterLine(frame, cx + offset, cy, radius, offset);
        // The fill's top edge is level, so both sides agree; when they don't, something (a skill
        // effect, a damage number) is drawn over the button and the fill can't be read.
        if (Math.Abs(left - right) > 0.12 * 2 * radius) return new UltimateReading(null, ready);

        // Fill height as a fraction of the disc's diameter, measured from the bottom of the disc.
        double charge = Math.Clamp((cy + radius - (left + right) / 2) / (2 * radius), 0, 1);
        return new UltimateReading(charge, ready);
    }

    // Share of the ready band that is white (all channels ≥ ReadyWhiteLevel). Measured on every
    // frame of the recording: at most 0.020 before the ultimate is ready, at least 0.073 while
    // it is (the ring pulses, so the share varies frame to frame). Cut between the two.
    private const double ReadyWhiteFraction = 0.045;
    private const int ReadyWhiteLevel = 180;

    /// <summary>
    /// Walk up the vertical line at <paramref name="x"/> (that far off centre) from the bottom of
    /// the disc while pixels are fill-coloured; returns the y of the fill's top edge, or just
    /// below the disc if there is no fill.
    /// </summary>
    private static double WaterLine(Frame frame, double x, double cy, double radius, double offset)
    {
        int ix = (int)Math.Round(x);
        double halfChord = Math.Sqrt(Math.Max(0, radius * radius - offset * offset));
        int bottom = (int)(cy + halfChord) - 2;
        int top = (int)(cy - halfChord) + 2;

        int waterline = bottom + 1;
        int misses = 0;
        for (int y = bottom; y >= top; y--)
        {
            if ((uint)ix >= (uint)frame.Width || (uint)y >= (uint)frame.Height) break;
            var (r, g, b) = frame.GetPixel(ix, y);
            if (IsFill(r, g, b))
            {
                waterline = y;
                misses = 0;
            }
            else if (++misses > 3)
            {
                break; // past the top of the fill (shorter gaps are video-compression noise)
            }
        }
        return waterline;
    }

    // Element colours (red, green, …) are saturated; the empty disc is dark grey.
    private static bool IsFill(byte r, byte g, byte b)
    {
        int max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        return max >= 90 && max - min >= 60;
    }

    /// <summary>Is the grey ring there (light, unsaturated pixels most of the way round)?</summary>
    private static bool RingVisible(Frame frame, double cx, double cy, double radius)
    {
        int ringish = 0, total = 0;
        foreach (var (r, g, b) in Circle(frame, cx, cy, radius))
        {
            int max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            total++;
            if (max - min <= 60 && max >= 70) ringish++;
        }
        return total > 0 && ringish >= total * 0.6;
    }

    private static double WhiteFraction(Frame frame, double cx, double cy, double inner, double outer)
    {
        int white = 0, total = 0;
        for (double radius = inner; radius <= outer; radius += Math.Max(1, (outer - inner) / 8))
        {
            foreach (var (r, g, b) in Circle(frame, cx, cy, radius))
            {
                total++;
                if (Math.Min(r, Math.Min(g, b)) >= ReadyWhiteLevel) white++;
            }
        }
        return total == 0 ? 0 : white / (double)total;
    }

    private static IEnumerable<(byte R, byte G, byte B)> Circle(Frame frame, double cx, double cy, double radius)
    {
        for (int i = 0; i < 72; i++)
        {
            double angle = i * Math.PI * 2 / 72;
            int x = (int)Math.Round(cx + Math.Cos(angle) * radius);
            int y = (int)Math.Round(cy + Math.Sin(angle) * radius);
            if ((uint)x < (uint)frame.Width && (uint)y < (uint)frame.Height)
                yield return frame.GetPixel(x, y);
        }
    }
}
