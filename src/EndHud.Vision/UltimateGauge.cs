namespace EndHud.Vision;

/// <param name="Ready">True: the 终结技 (ultimate) can be used.</param>
/// <param name="Progress">
/// Charge in [0, 1]: 1 when ready; while charging, how far the coloured arc has grown
/// clockwise from 12 o'clock.
/// </param>
public sealed record UltimateReading(bool Ready, double Progress);

/// <summary>Reads the small ultimate circles in the bottom-right corner (see <see cref="SkillLayout"/>).</summary>
public static class UltimateGauge
{
    // A ready disc is mostly element colour (the rest is the white icon). Measured: ~60%+.
    private const double ReadyColourShare = 0.35;
    // While charging, the middle of the disc (inside the arc) is empty: hardly any colour or white.
    private const double EmptyCentreMaxColour = 0.2;
    private const double EmptyCentreMaxWhite = 0.03;
    // Standard deviation of brightness in the centre. Measured on both recordings: 1.6–4.4 for
    // empty discs (25 once, with an effect behind it), 33–77 for dimmed or fading ready icons.
    private const double EmptyCentreMaxSpread = 15;
    private const double RingShare = 0.6;
    private const int ArcSamples = 72;

    /// <param name="slot">Squad slot, 0-based (circle order matches the squad panel).</param>
    /// <returns>The reading, or null when the circle isn't visible (cutscene, effects on top).</returns>
    public static UltimateReading? Measure(Frame frame, int slot)
    {
        var ui = new UiScaler(frame.Width, frame.Height);
        var (cx, cy) = ui.BottomRight(SkillLayout.FirstCentreX + slot * SkillLayout.Pitch, SkillLayout.CentreY);
        double s = ui.Scale;

        // Charging first: near full charge the arc colours much of the disc, but its centre
        // stays empty — no icon, no colour. (The disc is translucent, so "empty" can be any
        // shade of grey depending on what's behind it.)
        // It's also flat: a ready icon the game has dimmed (during HUD fades and combo
        // sequences) loses its colour and bright white, but still has an icon's contrast.
        double centreRadius = SkillLayout.EmptyCentreRadius * s;
        var (centreColoured, _) = SampleDisc(frame, cx, cy, centreRadius);
        bool centreEmpty = centreColoured < EmptyCentreMaxColour
                           && WhiteShare(frame, cx, cy, centreRadius) < EmptyCentreMaxWhite
                           && BrightnessSpread(frame, cx, cy, centreRadius) < EmptyCentreMaxSpread;
        if (centreEmpty && RingVisible(frame, cx, cy, SkillLayout.ChargingRingRadius * s))
            return new UltimateReading(Ready: false, Progress: ArcProgress(frame, cx, cy, s));

        // Ready: element colour around a white icon. Colour alone isn't enough — skill effects
        // sweeping over the corner are colourful too, but have no icon.
        var (coloured, _) = SampleDisc(frame, cx, cy, SkillLayout.DiscRadius * s);
        if (coloured >= ReadyColourShare && WhiteShare(frame, cx, cy, SkillLayout.IconRadius * s) >= IconWhiteShare)
            return new UltimateReading(Ready: true, Progress: 1);
        return null;
    }

    // Share of the icon area that is white. Measured on both recordings: 0.20–0.45 for ready
    // icons (the thinnest, slot 3's, is lowest), 0.00 for skill effects passing over the circle.
    private const double IconWhiteShare = 0.08;

    /// <summary>Standard deviation of per-pixel brightness (darkest channel) over a disc.</summary>
    private static double BrightnessSpread(Frame frame, double cx, double cy, double radius)
    {
        double sum = 0, sumSq = 0;
        int n = 0;
        int r = (int)Math.Ceiling(radius);
        for (int dy = -r; dy <= r; dy++)
        for (int dx = -r; dx <= r; dx++)
        {
            if (dx * dx + dy * dy > radius * radius) continue;
            int x = (int)Math.Round(cx + dx), y = (int)Math.Round(cy + dy);
            if ((uint)x >= (uint)frame.Width || (uint)y >= (uint)frame.Height) continue;
            var (cr, cg, cb) = frame.GetPixel(x, y);
            double v = Math.Min(cr, Math.Min(cg, cb));
            sum += v;
            sumSq += v * v;
            n++;
        }
        if (n == 0) return double.MaxValue;
        double mean = sum / n;
        return Math.Sqrt(Math.Max(0, sumSq / n - mean * mean));
    }

    private static double WhiteShare(Frame frame, double cx, double cy, double radius)
    {
        int white = 0, total = 0;
        int r = (int)Math.Ceiling(radius);
        for (int dy = -r; dy <= r; dy++)
        for (int dx = -r; dx <= r; dx++)
        {
            if (dx * dx + dy * dy > radius * radius) continue;
            int x = (int)Math.Round(cx + dx), y = (int)Math.Round(cy + dy);
            if ((uint)x >= (uint)frame.Width || (uint)y >= (uint)frame.Height) continue;
            var (cr, cg, cb) = frame.GetPixel(x, y);
            total++;
            if (Math.Min(cr, Math.Min(cg, cb)) >= 200) white++;
        }
        return total == 0 ? 0 : white / (double)total;
    }

    /// <summary>
    /// Share of the circle the charge arc covers. Sampled every 5° on the arc band; a direction
    /// counts when most of the band there is element colour.
    /// </summary>
    private static double ArcProgress(Frame frame, double cx, double cy, double scale)
    {
        int covered = 0;
        for (int i = 0; i < ArcSamples; i++)
        {
            double angle = i * Math.PI * 2 / ArcSamples - Math.PI / 2; // from 12 o'clock, clockwise
            int hits = 0, samples = 0;
            for (double r = SkillLayout.ArcInnerRadius; r <= SkillLayout.ArcOuterRadius; r += 2)
            {
                int x = (int)Math.Round(cx + Math.Cos(angle) * r * scale);
                int y = (int)Math.Round(cy + Math.Sin(angle) * r * scale);
                if ((uint)x >= (uint)frame.Width || (uint)y >= (uint)frame.Height) continue;
                var (cr, cg, cb) = frame.GetPixel(x, y);
                samples++;
                if (IsElementColour(cr, cg, cb)) hits++;
            }
            if (samples > 0 && hits * 2 >= samples) covered++;
        }
        return covered / (double)ArcSamples;
    }

    private static bool IsElementColour(byte r, byte g, byte b)
    {
        int max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        return max >= 90 && max - min >= 70;
    }

    /// <summary>Shares of the disc that are saturated element colour, and that are dark.</summary>
    private static (double Coloured, double Dark) SampleDisc(Frame frame, double cx, double cy, double radius)
    {
        int coloured = 0, dark = 0, total = 0;
        int r = (int)Math.Ceiling(radius);
        for (int dy = -r; dy <= r; dy += 2)
        for (int dx = -r; dx <= r; dx += 2)
        {
            if (dx * dx + dy * dy > radius * radius) continue;
            int x = (int)Math.Round(cx + dx), y = (int)Math.Round(cy + dy);
            if ((uint)x >= (uint)frame.Width || (uint)y >= (uint)frame.Height) continue;
            var (cr, cg, cb) = frame.GetPixel(x, y);
            int max = Math.Max(cr, Math.Max(cg, cb));
            total++;
            if (IsElementColour(cr, cg, cb)) coloured++;
            else if (max < 60) dark++;
        }
        return total == 0 ? (0, 0) : (coloured / (double)total, dark / (double)total);
    }

    /// <summary>Is the thin grey ring there (light, unsaturated pixels most of the way round)?</summary>
    private static bool RingVisible(Frame frame, double cx, double cy, double radius)
    {
        int ring = 0, total = 0;
        for (int i = 0; i < 72; i++)
        {
            double angle = i * Math.PI * 2 / 72;
            // The ring is ~3 px wide at 4K: take the brightest of a few radii so small
            // misalignment doesn't matter.
            int best = 0;
            bool unsaturated = false;
            foreach (double dr in new[] { -2.0, -1.0, 0, 1.0, 2.0 })
            {
                int x = (int)Math.Round(cx + Math.Cos(angle) * (radius + dr));
                int y = (int)Math.Round(cy + Math.Sin(angle) * (radius + dr));
                if ((uint)x >= (uint)frame.Width || (uint)y >= (uint)frame.Height) continue;
                var (r, g, b) = frame.GetPixel(x, y);
                int min = Math.Min(r, Math.Min(g, b)), max = Math.Max(r, Math.Max(g, b));
                if (min > best)
                {
                    best = min;
                    unsaturated = max - min <= 60;
                }
            }
            total++;
            // A dim grey (measured: brightest channel-minimum 71–104 around the ring), against a dark disc.
            if (best >= 55 && unsaturated) ring++;
        }
        return total > 0 && ring >= total * RingShare;
    }
}
