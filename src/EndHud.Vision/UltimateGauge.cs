namespace EndHud.Vision;

/// <param name="Ready">True: the ultimate can be used.</param>
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
    /// How far the charge arc reaches. The arc is one unbroken run of a single element colour
    /// starting at 12 o'clock; the disc is translucent, so colourful scenery behind it (a copper
    /// pipe, say) can show elsewhere in the band and must not count. Walk clockwise from
    /// 12 o'clock in 5° steps while the band holds the arc's colour.
    /// </summary>
    private static double ArcProgress(Frame frame, double cx, double cy, double scale)
    {
        (double Hue, double Brightness)? arc = null;
        int reached = 0, gap = 0;
        for (int i = 0; i < ArcSamples; i++)
        {
            var band = BandColour(frame, cx, cy, scale, i);
            bool sameArc = band is { } b && (arc is not { } a
                || (HueDistance(b.Hue, a.Hue) <= MaxArcHueDrift && b.Brightness >= a.Brightness * MinArcBrightnessRatio));
            if (sameArc)
            {
                arc ??= band; // the arc's colour is set by its start at 12 o'clock
                reached = i + 1;
                gap = 0;
            }
            else if (arc is null ? i >= ArcStartTolerance : ++gap > ArcGapTolerance)
            {
                break; // no arc at 12 o'clock, or past its end
            }
        }
        return reached / (double)ArcSamples;
    }

    // The arc may begin a step or two after exactly 12 o'clock, and compression can blur a
    // single step; anything longer ends the arc.
    private const int ArcStartTolerance = 2;
    private const int ArcGapTolerance = 1;
    // The arc is one flat colour; scenery that merely looks colourful drifts further. Measured:
    // copper-orange behind a red arc is ~30° away.
    private const double MaxArcHueDrift = 18;
    // The arc is vivid; a same-hue scene behind the translucent disc (a dark red backdrop next
    // to a red arc) is much dimmer than the arc's own start.
    private const double MinArcBrightnessRatio = 0.7;

    /// <summary>
    /// Hue and brightness of the element colour in the band at step <paramref name="i"/>, if most
    /// of it is element colour.
    /// </summary>
    private static (double Hue, double Brightness)? BandColour(Frame frame, double cx, double cy, double scale, int i)
    {
        double angle = i * Math.PI * 2 / ArcSamples - Math.PI / 2; // from 12 o'clock, clockwise
        int hits = 0, samples = 0;
        double sx = 0, sy = 0, brightness = 0;
        for (double r = SkillLayout.ArcInnerRadius; r <= SkillLayout.ArcOuterRadius; r += 2)
        {
            int x = (int)Math.Round(cx + Math.Cos(angle) * r * scale);
            int y = (int)Math.Round(cy + Math.Sin(angle) * r * scale);
            if ((uint)x >= (uint)frame.Width || (uint)y >= (uint)frame.Height) continue;
            var (cr, cg, cb) = frame.GetPixel(x, y);
            samples++;
            if (!IsElementColour(cr, cg, cb)) continue;
            hits++;
            brightness += Math.Max(cr, Math.Max(cg, cb));
            double h = Hue(cr, cg, cb) * Math.PI / 180;
            sx += Math.Cos(h);
            sy += Math.Sin(h);
        }
        if (samples == 0 || hits * 2 < samples) return null;
        double mean = Math.Atan2(sy, sx) * 180 / Math.PI;
        return (mean < 0 ? mean + 360 : mean, brightness / hits);
    }

    private static double HueDistance(double a, double b)
    {
        double d = Math.Abs(a - b) % 360;
        return d > 180 ? 360 - d : d;
    }

    private static double Hue(byte r, byte g, byte b)
    {
        int max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double d = max - min;
        if (d == 0) return 0;
        double h = max == r ? (g - b) / d % 6 : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        h *= 60;
        return h < 0 ? h + 360 : h;
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
