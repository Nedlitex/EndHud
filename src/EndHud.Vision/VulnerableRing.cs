namespace EndHud.Vision;

/// <summary>
/// Vulnerable on the boss. The game doesn't put it in the status row: it draws it in the
/// infliction ring's place at the right end of the boss's HP bar (<see cref="InflictionRing.Area"/>),
/// as a white shield crossed by a dark diagonal slash (lower left to upper right) on a dark red disc,
/// with the same white arcs around it for the stacks, clockwise from the top (1–4).
/// Measured on the owner's physical-team recording (Fixtures/vulnerable): 1, 2, 3 and 4 stacks.
/// <para>
/// When a stack is added (and in the last half second before the ring goes) a grey circle sweeps
/// outwards through the arcs and an enlarged ghost of the shield fades behind it. The circle lights
/// the gaps between the arcs, which the game otherwise leaves empty, so a frame with a lit gap
/// reads Vulnerable with an unknown count rather than a count the circle made up.
/// </para>
/// </summary>
public static class VulnerableRing
{
    /// <summary>The wiki's id for Vulnerable (<see cref="Operators.StatusInfo"/>).</summary>
    public const string StatusId = "Vulnerable";

    // The shield, sampled within ShieldRadius of the ring's centre (4K px), split by the slash: a line at
    // 45° through SlashOffset px above the centre. Pixels within SlashHalfWidth of it are the slash, pixels
    // at least ClearOfSlash from it the shield. Measured on the recording, every 0.5 s of 0–132.5 s
    // (115 frames showing the shield, 149 without it, 2 transitions):
    // - the shield's share of white (every channel ≥ ShieldWhiteFrom, near grey; the lower of the two
    //   sides of the slash) was 1.00 on every frame with it, the HUD fading back in at 81.5 s included;
    //   MinShieldWhite 0.8;
    // - the slash stood 106–229 below the shield's brightness; without the shield the same measure was
    //   at most 55 (a dark scene, its "shield" not white at all) and at most 32 where the place was
    //   white (≥ 0.5) on both sides; MinSlashContrast 80, about halfway;
    // - four recordings of arts teams (Si, Laevatain, Typhoeus, Zhuang; 585 frames, one a second) never
    //   read as Vulnerable. Their closest frame: sky over a dark roof edge, slash measure 79, but white
    //   on one side of the line only (0.00 on the other);
    // - the top arc's whiteness (InflictionRing.Measure) was 169 on that fading-in frame and 232–248
    //   otherwise. MinTopArc 150 lets the fading frame say Vulnerable is there; its stacks still need the
    //   arcs' full white (InflictionRing.MinArcWhiteness), so its count reads as unknown.
    private const double ShieldRadius = 14;
    private const double SlashOffset = 4;
    private const double SlashHalfWidth = 1.5;
    private const double ClearOfSlash = 5;
    private const int ShieldWhiteFrom = 130;
    internal const double MinShieldWhite = 0.8;
    internal const int MinSlashContrast = 80;
    internal const int MinTopArc = 150;

    /// <summary>Vulnerable and its stacks, or null when the ring doesn't show it.</summary>
    public static InflictionRing.Infliction? Read(Frame frame)
    {
        var (whiteness, _, _, element) = InflictionRing.Measure(frame);
        if (element is not null || whiteness < MinTopArc) return null;
        var (white, slash) = Shield(frame);
        if (white < MinShieldWhite || slash < MinSlashContrast) return null;
        return new InflictionRing.Infliction(StatusId, Stacks(frame));
    }

    /// <summary>The ring's stacks, or null when an effect lights a gap between the arcs.</summary>
    internal static int? Stacks(Frame frame) =>
        InflictionRing.GapShares(frame).Any(g => g > InflictionRing.UnlitShare) ? null : InflictionRing.Stacks(frame);

    /// <summary>The shield's share of white pixels and how much darker the slash is than the shield, for measuring thresholds.</summary>
    internal static (double White, int SlashContrast) Shield(Frame frame)
    {
        var ui = new UiScaler(frame.Width, frame.Height);
        var area = ui.TopCenter(InflictionRing.Area);
        double k = area.Width / (double)InflictionRing.Area.Width;
        double cx = area.X + InflictionRing.CentreX * k, cy = area.Y + InflictionRing.CentreY * k;
        var shield = new List<int>();
        var slash = new List<int>();
        // Each side of the slash (upper left, lower right) counted apart: the shield is white on both,
        // where a bright-over-dark edge in the scenery is white on one side only.
        int[] white = new int[2], count = new int[2];
        for (double dy = -ShieldRadius; dy <= ShieldRadius; dy++)
        for (double dx = -ShieldRadius; dx <= ShieldRadius; dx++)
        {
            if (dx * dx + dy * dy > ShieldRadius * ShieldRadius) continue;
            int px = Math.Clamp((int)Math.Round(cx + dx * k), 0, frame.Width - 1), py = Math.Clamp((int)Math.Round(cy + dy * k), 0, frame.Height - 1);
            var (r, g, b) = frame.GetPixel(px, py);
            int min = Math.Min(r, Math.Min(g, b)), max = Math.Max(r, Math.Max(g, b));
            double side = (dx + dy + SlashOffset) / Math.Sqrt(2);
            if (Math.Abs(side) <= SlashHalfWidth) slash.Add(min);
            else if (Math.Abs(side) >= ClearOfSlash)
            {
                int s = side < 0 ? 0 : 1;
                shield.Add(min);
                count[s]++;
                if (min >= ShieldWhiteFrom && max - min < 40) white[s]++;
            }
        }
        shield.Sort();
        slash.Sort();
        return (Math.Min(white[0] / (double)count[0], white[1] / (double)count[1]), shield[shield.Count / 2] - slash[slash.Count / 2]);
    }
}
