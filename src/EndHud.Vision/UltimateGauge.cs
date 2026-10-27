namespace EndHud.Vision;

/// <param name="Ready">True: the ultimate can be used.</param>
/// <param name="Progress">
/// Charge in [0, 1]: 1 when ready; while charging, how far the coloured arc has grown
/// clockwise from 12 o'clock.
/// </param>
public sealed record UltimateReading(bool Ready, double Progress);

/// <summary>
/// Where one ultimate circle is and how big its parts are, at 4K, anchored bottom-right.
/// <list type="bullet">
/// <item>Keyboard and mouse: a row of circles (<see cref="SkillLayout"/>); ready, the disc is
/// element colour behind a white icon.</item>
/// <item>Controller: a small dot at each point of the skill diamond. Charging looks the same,
/// smaller (a dark disc, an element-colour arc from 12 o'clock, a thin grey ring); ready, it's a
/// plain element-colour disc with no icon, in a white ring.</item>
/// </list>
/// </summary>
internal sealed record UltimateCircle(double CentreX, double CentreY, int EmptyCentreRadius, int ArcInnerRadius, int ArcOuterRadius,
    int ChargingRingRadius, int DiscRadius, int? IconRadius, int? ReadyRingRadius, int ArcMinSaturation = 70, double ArcMaxHueDrift = 18, int ArcRadiusStep = 2,
    bool ReadsNeutral = false)
{
    /// <summary>
    /// Keyboard and mouse, slot 0–3. These also read a Physical ultimate, drawn in a neutral cream
    /// instead of an element colour (<see cref="ReadsNeutral"/>).
    /// </summary>
    public static UltimateCircle Row(int slot) => new(SkillLayout.FirstCentreX + slot * SkillLayout.Pitch, SkillLayout.CentreY,
        SkillLayout.EmptyCentreRadius, SkillLayout.ArcInnerRadius, SkillLayout.ArcOuterRadius, SkillLayout.ChargingRingRadius,
        SkillLayout.DiscRadius, IconRadius: SkillLayout.IconRadius, ReadyRingRadius: null, ReadsNeutral: true);

    /// <summary>
    /// Controller, slot 0–3: slot 1 left, 2 top, 3 right, 4 bottom, like the squad diamond.
    /// Measured on the owner's controller recordings: the arc 14–17 px from the centre and paler
    /// than keyboard and mouse's (channel spread 36–104, lowest over a bright floor, against 30 or
    /// less for what's behind it), its hue drifting further round it (a yellow arc from 51° to
    /// 31°); sampled every pixel across it, as it's only 3–4 px wide. The empty middle
    /// within 8, the grey (charging) ring at about 30, the light (ready) ring at 30–34, the ready
    /// colour filling 20.
    /// </summary>
    public static UltimateCircle Diamond(int slot)
    {
        var (x, y) = slot switch { 0 => (3344, 1849), 1 => (3553, 1646), 2 => (3766, 1849), _ => (3553, 2060) };
        return new(x, y, EmptyCentreRadius: 8, ArcInnerRadius: 14, ArcOuterRadius: 17, ChargingRingRadius: 30,
            DiscRadius: 17, IconRadius: null, ReadyRingRadius: 30, ArcMinSaturation: 40, ArcMaxHueDrift: 25, ArcRadiusStep: 1);
    }
}

/// <summary>Reads the small ultimate circles in the bottom-right corner (see <see cref="UltimateCircle"/>).</summary>
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
    public static UltimateReading? Measure(Frame frame, int slot) => Measure(frame, UltimateCircle.Row(slot));

    internal static UltimateReading? Measure(Frame frame, UltimateCircle circle)
    {
        var ui = new UiScaler(frame.Width, frame.Height);
        var (cx, cy) = ui.BottomRight(circle.CentreX, circle.CentreY);
        double s = ui.Scale;

        // Charging first: near full charge the arc colours much of the disc, but its centre
        // stays empty — no icon, no colour. (The disc is translucent, so "empty" can be any
        // shade of grey depending on what's behind it.)
        // It's also flat: a ready icon the game has dimmed (during HUD fades and combo
        // sequences) loses its colour and bright white, but still has an icon's contrast.
        double centreRadius = circle.EmptyCentreRadius * s;
        var (centreColoured, _) = SampleDisc(frame, cx, cy, centreRadius);
        var (centreBrightness, centreSpread) = Brightness(frame, cx, cy, centreRadius);
        bool centreEmpty = centreColoured < EmptyCentreMaxColour
                           && WhiteShare(frame, cx, cy, centreRadius) < EmptyCentreMaxWhite
                           && centreSpread < EmptyCentreMaxSpread;
        if (centreEmpty && RingVisible(frame, cx, cy, circle.ChargingRingRadius * s))
            return new UltimateReading(Ready: false, Progress: ArcProgress(frame, cx, cy, s, circle, centreBrightness));

        // Ready: element colour around a white icon (keyboard and mouse) or inside a white ring
        // (controller). Colour alone isn't enough: skill effects sweeping over the corner are
        // colourful too, but have no icon or ring.
        var (coloured, _) = SampleDisc(frame, cx, cy, circle.DiscRadius * s);
        bool marked = circle.IconRadius is { } icon
            ? WhiteShare(frame, cx, cy, icon * s) >= IconWhiteShare
            : circle.ReadyRingRadius is { } ring && WhiteRingShare(frame, cx, cy, ring * s, s) >= WhiteRingMinShare && coloured >= PlainDiscColourShare;
        if (coloured >= ReadyColourShare && marked)
            return new UltimateReading(Ready: true, Progress: 1);
        // A Physical ultimate is ready in grey: the disc lit a light, unsaturated grey behind a
        // large white icon, in a light ring just outside the circle. Grey scenery and light effects
        // have no icon that big; a coloured effect isn't grey.
        if (circle.ReadsNeutral && circle.IconRadius is { } neutralIcon
            && WhiteShare(frame, cx, cy, neutralIcon * s) >= NeutralReadyIconShare
            && NeutralLitShare(frame, cx, cy, circle.DiscRadius * s) >= NeutralReadyDiscShare
            && LightRingShare(frame, cx, cy, s) >= NeutralReadyRingShare)
            return new UltimateReading(Ready: true, Progress: 1);
        return null;
    }

    // Measured on two recordings with Physical ultimates (test_mifu: four of them; test_rossi_mifu_si:
    // Mi Fu in slot 4, over a light floor). The icon, white share as above: 0.45-0.59 on every ready
    // circle; at most 0.15 on charging ones, 0.12 under a light cyan effect that the disc and ring
    // tests alone took for ready. The disc, share with channel spread <= 20 and darkest channel >= 90:
    // 0.88-0.99 ready; 0.03-0.46 charging over a dark floor, but up to 1.00 over a light one. The
    // light ring, 36-44 px out (lightest unsaturated pixel >= 150): all the way round when ready
    // (median 200-210); up to 0.68 round a charging circle over a light floor, 0.86 under the effect;
    // 0.00-0.01 on the frames where the circle flashes plain white as it fills (those read nothing,
    // like a cutscene).
    private const double NeutralReadyIconShare = 0.3;
    private const double NeutralReadyDiscShare = 0.6;
    private const double NeutralReadyRingShare = 0.9;

    private static double NeutralLitShare(Frame frame, double cx, double cy, double radius)
    {
        int lit = 0, total = 0;
        int r = (int)Math.Ceiling(radius);
        for (int dy = -r; dy <= r; dy += 2)
        for (int dx = -r; dx <= r; dx += 2)
        {
            if (dx * dx + dy * dy > radius * radius) continue;
            int x = (int)Math.Round(cx + dx), y = (int)Math.Round(cy + dy);
            if ((uint)x >= (uint)frame.Width || (uint)y >= (uint)frame.Height) continue;
            var (cr, cg, cb) = frame.GetPixel(x, y);
            int min = Math.Min(cr, Math.Min(cg, cb)), max = Math.Max(cr, Math.Max(cg, cb));
            total++;
            if (min >= 90 && max - min <= 20) lit++;
        }
        return total == 0 ? 0 : lit / (double)total;
    }

    /// <summary>
    /// Share of the way round with a light grey ring 36-44 px from the centre (at 4K, scaled): the
    /// ring a ready keyboard-and-mouse circle has.
    /// </summary>
    private static double LightRingShare(Frame frame, double cx, double cy, double scale)
    {
        int light = 0;
        for (int i = 0; i < 72; i++)
        {
            double angle = i * Math.PI * 2 / 72;
            for (int dr = 36; dr <= 44; dr++)
            {
                int x = (int)Math.Round(cx + Math.Cos(angle) * dr * scale);
                int y = (int)Math.Round(cy + Math.Sin(angle) * dr * scale);
                if ((uint)x >= (uint)frame.Width || (uint)y >= (uint)frame.Height) continue;
                var (r, g, b) = frame.GetPixel(x, y);
                int min = Math.Min(r, Math.Min(g, b)), max = Math.Max(r, Math.Max(g, b));
                if (min >= 150 && max - min <= 40) { light++; break; }
            }
        }
        return light / 72.0;
    }

    // The controller's ready dot is plain colour (no icon) inside a white ring.
    private const double PlainDiscColourShare = 0.7;
    private const double WhiteRingMinShare = 0.6;

    /// <summary>
    /// Share of the way round that has a light grey ring between <paramref name="radius"/> − 3
    /// and + 5 px (at 4K, scaled). Measured on ready dots: the ring is at radius 30–34 and its
    /// darkest channel 155–220, varying between recordings and pulsing when the ultimate first fills.
    /// </summary>
    private static double WhiteRingShare(Frame frame, double cx, double cy, double radius, double scale)
    {
        int white = 0;
        for (int i = 0; i < 72; i++)
        {
            double angle = i * Math.PI * 2 / 72;
            for (int dr = -3; dr <= 5; dr++)
            {
                int x = (int)Math.Round(cx + Math.Cos(angle) * (radius + dr * scale));
                int y = (int)Math.Round(cy + Math.Sin(angle) * (radius + dr * scale));
                if ((uint)x >= (uint)frame.Width || (uint)y >= (uint)frame.Height) continue;
                var (r, g, b) = frame.GetPixel(x, y);
                int min = Math.Min(r, Math.Min(g, b)), max = Math.Max(r, Math.Max(g, b));
                if (min >= 140 && max - min <= 40) { white++; break; }
            }
        }
        return white / 72.0;
    }

    // Share of the icon area that is white. Measured on both recordings: 0.20–0.45 for ready
    // icons (the thinnest, slot 3's, is lowest), 0.00 for skill effects passing over the circle.
    private const double IconWhiteShare = 0.08;

    /// <summary>Mean and standard deviation of per-pixel brightness (darkest channel) over a disc.</summary>
    private static (double Mean, double Spread) Brightness(Frame frame, double cx, double cy, double radius)
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
        if (n == 0) return (double.MaxValue, double.MaxValue);
        double mean = sum / n;
        return (mean, Math.Sqrt(Math.Max(0, sumSq / n - mean * mean)));
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
    /// 12 o'clock in 5° steps while the band holds the arc's colour. A Physical ultimate's arc is
    /// neutral cream instead (see <see cref="IsNeutralArc"/>), and an arc is one or the other.
    /// </summary>
    private static double ArcProgress(Frame frame, double cx, double cy, double scale, UltimateCircle circle, double centreBrightness)
    {
        // The arc's colour so far: the average of its bands, not just its first one, which can take
        // a tint from the scenery behind the translucent disc (a cyan arc's start read 158° over a
        // red backdrop, the rest of it 169-183°).
        double sx = 0, sy = 0, brightness = 0;
        int bands = 0;
        bool neutralArc = false;
        int reached = 0, gap = 0;
        for (int i = 0; i < ArcSamples; i++)
        {
            var band = BandColour(frame, cx, cy, scale, i, circle, centreBrightness);
            bool sameArc = band is { } b && (bands == 0
                || (b.Neutral == neutralArc
                    && (b.Neutral || HueDistance(b.Hue, MeanHue(sx, sy)) <= circle.ArcMaxHueDrift)
                    && b.Brightness >= brightness / bands * MinArcBrightnessRatio));
            if (sameArc)
            {
                double h = band!.Value.Hue * Math.PI / 180;
                (sx, sy, brightness, bands) = (sx + Math.Cos(h), sy + Math.Sin(h), brightness + band.Value.Brightness, bands + 1);
                neutralArc = band.Value.Neutral;
                reached = i + 1;
                gap = 0;
            }
            else if (bands == 0 ? i >= ArcStartTolerance : ++gap > ArcGapTolerance)
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
    // copper-orange behind a red arc is ~30° away (UltimateCircle.ArcMaxHueDrift: 18° for the
    // keyboard-and-mouse circles).
    // The arc is vivid; a same-hue scene behind the translucent disc (a dark red backdrop next
    // to a red arc) is much dimmer than the arc's own start.
    private const double MinArcBrightnessRatio = 0.7;

    /// <summary>
    /// Hue and brightness of the arc colour in the band at step <paramref name="i"/>, if most of it
    /// is element colour or (on circles that read Physical ultimates) neutral cream.
    /// </summary>
    private static (double Hue, double Brightness, bool Neutral)? BandColour(Frame frame, double cx, double cy, double scale, int i,
        UltimateCircle circle, double centreBrightness)
    {
        double angle = i * Math.PI * 2 / ArcSamples - Math.PI / 2; // from 12 o'clock, clockwise
        int hits = 0, neutral = 0, samples = 0;
        double sx = 0, sy = 0, brightness = 0, neutralBrightness = 0;
        for (double r = circle.ArcInnerRadius; r <= circle.ArcOuterRadius; r += circle.ArcRadiusStep)
        {
            int x = (int)Math.Round(cx + Math.Cos(angle) * r * scale);
            int y = (int)Math.Round(cy + Math.Sin(angle) * r * scale);
            if ((uint)x >= (uint)frame.Width || (uint)y >= (uint)frame.Height) continue;
            var (cr, cg, cb) = frame.GetPixel(x, y);
            samples++;
            if (!IsElementColour(cr, cg, cb, circle.ArcMinSaturation))
            {
                if (circle.ReadsNeutral && IsNeutralArc(cr, cg, cb, centreBrightness))
                {
                    neutral++;
                    neutralBrightness += Math.Max(cr, Math.Max(cg, cb));
                }
                continue;
            }
            hits++;
            brightness += Math.Max(cr, Math.Max(cg, cb));
            double h = Hue(cr, cg, cb) * Math.PI / 180;
            sx += Math.Cos(h);
            sy += Math.Sin(h);
        }
        if (samples == 0) return null;
        if (hits * 2 >= samples)
        {
            double mean = Math.Atan2(sy, sx) * 180 / Math.PI;
            return (mean < 0 ? mean + 360 : mean, brightness / hits, false);
        }
        return neutral * 2 >= samples ? (0, neutralBrightness / neutral, true) : null;
    }

    /// <summary>
    /// A pixel of a Physical ultimate's cream arc: light, hardly any colour, and much lighter than
    /// the circle's empty centre. Measured on test_mifu and test_rossi_mifu_si, per 5° band: the arc's
    /// darkest channel 189-202 (10th percentile) and channel spread up to 34 (90th), against the grey
    /// charging ring's 72 and the dark disc's 60 or less. The empty centre's mean darkest channel was
    /// 12-109 (the highest over light floors), and the arc stood at least 90 above it; in a white-out
    /// (test_mifu 66 s, the screen washed out to grey) the centre was 178 and the "arc" 178, nothing
    /// above it, and accepting any light grey read that circle as full.
    /// </summary>
    private static bool IsNeutralArc(byte r, byte g, byte b, double centreBrightness)
    {
        int max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        return min >= 150 && max - min <= 45 && min >= centreBrightness + 50;
    }

    private static double MeanHue(double sx, double sy)
    {
        double mean = Math.Atan2(sy, sx) * 180 / Math.PI;
        return mean < 0 ? mean + 360 : mean;
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

    private static bool IsElementColour(byte r, byte g, byte b, int minSaturation = 70)
    {
        int max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        return max >= 90 && max - min >= minSaturation;
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
