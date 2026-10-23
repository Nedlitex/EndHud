namespace EndHud.Vision;

/// <summary>What a lasting battle skill's button shows.</summary>
public enum SkillButtonIcon
{
    /// <summary>The skill's own icon: not running (Si: the butterfly).</summary>
    Original,

    /// <summary>The icon it changes to while the skill lasts (Si: a swirl in a diamond).</summary>
    Changed,

    /// <summary>
    /// The icon after the skill is cast a second time while it still lasts (Si: the butterfly
    /// crossed out inside a ring with four points). The skill goes on: the ring keeps draining.
    /// </summary>
    SecondCast,

    /// <summary>The button is a countdown: just cast, cooling down.</summary>
    Cooling,

    /// <summary>Neither icon can be told (an effect or a cutscene over the button).</summary>
    Unknown,
}

/// <param name="OperatorId">Whose battle skill (Si).</param>
/// <param name="LeftShare">The grey ring's part not yet drained, 0–1; null when it can't be read.</param>
public sealed record SkillRingReading(string OperatorId, SkillButtonIcon Icon, double? LeftShare);

/// <summary>
/// Battle skills whose button changes while they last. Si's: from the first use the button's
/// butterfly becomes a swirl and a grey ring inside the button drains clockwise from 12 o'clock
/// over ≈22 s; the butterfly comes back when it ends. Measured with Si in slot 1, keyboard layout,
/// on the owner's Si fight (Fixtures/si). Cast again while the swirl is up, the icon becomes a
/// crossed-out butterfly and the ring goes on draining from where it was, without filling again;
/// the butterfly comes back when the ring is empty (or, once, earlier). Measured with Si in slot 3
/// and in slot 1 (Fixtures/si-timer-second-cast).
/// </summary>
public static class SkillRing
{
    /// <summary>
    /// How long Si's ring takes to drain: full at 13.5 s, gone at 35.5 s and the butterfly back at
    /// 35.75 s on the owner's fight (±0.25 s). Whether her skill level changes it is not known.
    /// </summary>
    public static readonly TimeSpan SiDuration = TimeSpan.FromSeconds(22);

    /// <summary>Every such operator in the squad, keyboard layout only (the controller layout's buttons aren't read).</summary>
    public static IReadOnlyList<SkillRingReading> Read(Frame frame, PartyState state)
    {
        if (state.ReadingsSinceSeen > 0 || state.Layout != HudLayout.KeyboardMouse) return [];
        var found = new List<SkillRingReading>();
        int size = state.SquadSize;
        for (int i = 0; i < state.Slots.Count && i < size; i++)
            if (state.Slots[i] is { Dead: false, Operator.Operator.Id: "Si" })
                found.Add(ReadSi(frame, button: PartyLayout.SlotCount - size + i)); // numbered as PartyReader numbers buttons
        return found;
    }

    /// <param name="button">The battle-skill button, 0 = leftmost of four (a smaller squad's are drawn from the right).</param>
    internal static SkillRingReading ReadSi(Frame frame, int button)
    {
        if (BattleSkillButton.Measure(frame, button) == BattleSkillState.Cooling) return new("Si", SkillButtonIcon.Cooling, null);
        var icon = Icon(frame, button);
        return new("Si", icon, icon is SkillButtonIcon.Changed or SkillButtonIcon.SecondCast ? Left(frame, button) : null);
    }

    /// <summary>
    /// The butterfly, the swirl or the crossed-out butterfly, by the icon's white shape: the one it
    /// matches best, clearly better than the others; unknown when it's clearly none of them.
    /// </summary>
    internal static SkillButtonIcon Icon(Frame frame, int button)
    {
        if (IconGlyph(frame, button) is not { } glyph) return SkillButtonIcon.Unknown;
        var scores = new (SkillButtonIcon Icon, double Overlap)[]
        {
            (SkillButtonIcon.Original, Overlap(glyph, ButterflyMask)),
            (SkillButtonIcon.Changed, Overlap(glyph, SwirlMask)),
            (SkillButtonIcon.SecondCast, Overlap(glyph, SecondCastMask)),
        }.OrderByDescending(s => s.Overlap).ToArray();
        if (scores[0].Overlap < IconMatchAt || scores[0].Overlap - scores[1].Overlap < IconMinLead) return SkillButtonIcon.Unknown;
        return scores[0].Icon;
    }

    /// <summary>
    /// The icon's white inside the button, scaled to <see cref="IconSize"/>². White is relative to the
    /// icon's own brightest pixels, because the whole button dims at times (a skill not affordable,
    /// a moment's fade) and the icon with it. Null when there's no white icon there at all.
    /// </summary>
    internal static bool[]? IconGlyph(Frame frame, int button)
    {
        var ui = new UiScaler(frame.Width, frame.Height);
        var (cx, cy) = Centre(ui, button);
        double radius = IconRadius * ui.Scale;
        int x0 = (int)Math.Round(cx - radius), y0 = (int)Math.Round(cy - radius), side = (int)Math.Round(2 * radius);
        if (x0 < 0 || y0 < 0 || x0 + side > frame.Width || y0 + side > frame.Height || side < IconSize / 2) return null;

        var mins = new List<int>(side * side);
        for (int y = 0; y < side; y++)
        for (int x = 0; x < side; x++)
        {
            double dx = x + 0.5 - radius, dy = y + 0.5 - radius;
            if (dx * dx + dy * dy > radius * radius) continue;
            var (r, g, b) = frame.GetPixel(x0 + x, y0 + y);
            mins.Add(Math.Min(r, Math.Min(g, b)));
        }
        mins.Sort();
        int top = mins[(int)(mins.Count * 0.95)];
        if (top < IconWhiteFloor) return null;
        int whiteFrom = (int)(top * IconWhiteShare);

        var glyph = new bool[IconSize * IconSize];
        for (int gy = 0; gy < IconSize; gy++)
        for (int gx = 0; gx < IconSize; gx++)
        {
            int px0 = gx * side / IconSize, px1 = Math.Max(px0 + 1, (gx + 1) * side / IconSize);
            int py0 = gy * side / IconSize, py1 = Math.Max(py0 + 1, (gy + 1) * side / IconSize);
            int n = 0, on = 0;
            for (int y = py0; y < py1; y++)
            for (int x = px0; x < px1; x++)
            {
                var (r, g, b) = frame.GetPixel(x0 + x, y0 + y);
                int max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
                n++;
                if (min >= whiteFrom && max - min <= IconMaxTint) on++;
            }
            glyph[gy * IconSize + gx] = on * 2 >= n;
        }
        return glyph;
    }

    /// <summary>
    /// The ring's part not yet drained. Sampled every 5° on the ring's band, each compared with the
    /// dark just inside it (the ring is lighter: grey over the dark disc, pale teal over the teal
    /// part at the bottom). 12 and 6 o'clock are skipped: a tick is drawn there whatever is left.
    /// The drained part runs clockwise from 12 o'clock, so the samples must be one drained run and
    /// then ring; anything else (an effect over it, the button fading) is not read.
    /// </summary>
    internal static double? Left(Frame frame, int button)
    {
        var ui = new UiScaler(frame.Width, frame.Height);
        var (cx, cy) = Centre(ui, button);
        double s = ui.Scale;
        int firstRing = -1, lastDrained = -1, unsure = 0;
        var angles = new List<int>();
        for (int a = AngleStep; a < 360; a += AngleStep)
            if (a != 180) angles.Add(a);

        for (int i = 0; i < angles.Count; i++)
        {
            double rad = angles[i] * Math.PI / 180, sin = Math.Sin(rad), cos = -Math.Cos(rad);
            double? ring = Brightness(frame, cx, cy, sin, cos, RingRadii, s), inside = Brightness(frame, cx, cy, sin, cos, InsideRadii, s);
            if (ring is null || inside is null) return null;
            double contrast = ring.Value - inside.Value;
            if (contrast >= RingFrom)
            {
                if (firstRing < 0) firstRing = i;
            }
            else if (contrast <= DrainedBelow)
            {
                if (firstRing >= 0) return null; // drained after ring: not this ring as drawn
                lastDrained = i;
            }
            else unsure++;
        }
        if (unsure > MaxUnsure) return null;
        if (firstRing < 0) return 0;                  // all drained
        if (lastDrained < 0) return 1;                // all ring
        double edge = (angles[lastDrained] + angles[firstRing]) / 2.0;
        return 1 - edge / 360;
    }

    /// <summary>Mean of R+G+B over <paramref name="radii"/> (4K) along one direction from the centre.</summary>
    private static double? Brightness(Frame frame, double cx, double cy, double sin, double cos, int[] radii, double scale)
    {
        double sum = 0;
        foreach (int r in radii)
        {
            int x = (int)Math.Round(cx + r * scale * sin), y = (int)Math.Round(cy + r * scale * cos);
            if ((uint)x >= (uint)frame.Width || (uint)y >= (uint)frame.Height) return null;
            var (cr, cg, cb) = frame.GetPixel(x, y);
            sum += cr + cg + cb;
        }
        return sum / radii.Length;
    }

    private static (double X, double Y) Centre(UiScaler ui, int button) =>
        ui.BottomRight(RingCentreX + button * SkillLayout.Pitch, RingCentreY);

    private static double Overlap(bool[] a, bool[] b)
    {
        int both = 0, either = 0;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] && b[i]) both++;
            if (a[i] || b[i]) either++;
        }
        return either == 0 ? 0 : both / (double)either;
    }

    private static bool[] Mask(string[] rows) => rows.SelectMany(r => r.Select(c => c == '#')).ToArray();

    // Button 0's centre at 4K, bottom-right anchored: fitted to the full ring at 13.5 s and 37.5 s
    // (frame 3153, 1931 on the 3838 × 2158 recording).
    private const double RingCentreX = 3154.4;
    private const double RingCentreY = 1932.8;
    // The ring's band (4K): grey 120-130 over a disc of 80, 55-61 px out; the dark inside it at 50-51.
    private static readonly int[] RingRadii = [56, 57, 58, 59];
    private static readonly int[] InsideRadii = [50, 51];
    private const int AngleStep = 5;
    // Ring minus inside, as R+G+B: ring 70-170, drained -25 to 30 on every clear frame; the faded
    // button (19.5-20 s) has many samples in between.
    private const double RingFrom = 60;
    private const double DrainedBelow = 35;
    private const int MaxUnsure = 3;

    // The icon: inside this radius (4K), white from this share of its brightest 5 % (bright icons
    // 250, dimmed ones 170; the dark disc around them about 80).
    private const int IconRadius = 40;
    private const int IconSize = 24;
    private const double IconWhiteShare = 0.8;
    private const int IconWhiteFloor = 120;
    private const int IconMaxTint = 40;
    internal const double IconMatchAt = 0.5;
    internal const double IconMinLead = 0.1;

    /// <summary>
    /// Si's butterfly (24 × 24): the majority over the labelled frames (4 frames: 10_5s, 12s, 35_75s, 36s) of
    /// Fixtures/si, rebuilt with a throwaway run of <see cref="IconGlyph"/>.
    /// </summary>
    internal static readonly bool[] ButterflyMask = Mask([
        ".......#####.#####......",
        "......##.........##.....",
        "....##......#......##...",
        "...##...............##..",
        "..##.................#..",
        "..#...................#.",
        ".#........#...#........#",
        ".#........##.##........#",
        "##...#####.....#####...#",
        "#....######...#####.....",
        "#.....######.######.....",
        "#.....######.#####......",
        "#.......####.####.......",
        "#........###.###........",
        "#.......####.####......#",
        ".#.....###.....###.....#",
        ".#....###.......##.....#",
        ".##...###.......###...#.",
        "..#...................#.",
        "...#.................#..",
        "....#.......#.......#...",
        ".....#.............#....",
        "......##.........##.....",
        ".........#######........",
    ]);

    /// <summary>
    /// Si's swirl (24 × 24): the majority over the labelled frames (17 frames: 13_5s, 15s, 16s, 18_75s, 21_5s, 23s, 24_5s, 27s, 29_75s, 30_5s, 30_75s, 33s, 35_25s, 37s, 37_5s, 39_25s, 40_5s) of
    /// Fixtures/si, rebuilt with a throwaway run of <see cref="IconGlyph"/>.
    /// </summary>
    internal static readonly bool[] SwirlMask = Mask([
        "...........#.#..........",
        "..........#..##.........",
        ".........#######........",
        "........#########.......",
        ".......##.......##......",
        "......#...........#.....",
        ".....#....####.....#....",
        "....#......#####...#....",
        "...##.......#####...##..",
        "..##........######..###.",
        ".###.....##.######...##.",
        "#.##....#########....#..",
        "#.##...#####.####....#..",
        ".###...#####.###.....###",
        "..##...#####........###.",
        "...##...#####.......#...",
        "....#....####.......#...",
        ".....#....####.....#....",
        "......#...........#.....",
        ".......#.........#......",
        "........#########.......",
        ".........######.........",
        "..........#..##.........",
        "...........#............",
    ]);

    /// <summary>
    /// Si's crossed-out butterfly after a second cast (24 × 24): the majority over the 8 labelled
    /// frames of Fixtures/si-timer-second-cast taken with Si in slot 3 (lae-24s to lae-37s), rebuilt
    /// with a throwaway run of <see cref="IconGlyph"/>. The 4 frames with Si in slot 1 (si-*) were
    /// left out to check it: their own majority differs from this in a few edge pixels only.
    /// </summary>
    internal static readonly bool[] SecondCastMask = Mask([
        "........................",
        "........................",
        "....#...########........",
        "....##############..#...",
        ".....###.############...",
        "....###.#########..##...",
        "....##.#########....##..",
        "...##.##...####.....##..",
        "...#..##....##..##...##.",
        "..##..##.........##..##.",
        "..##..###........##..##.",
        "..##..###........##..##.",
        "..##..###.......###..##.",
        "..##..##...#....###..##.",
        "..##...#..####..###..#..",
        "...#.....#####...#..##..",
        "...##...#######..#..#...",
        "....##.##########..##...",
        "....############..##....",
        "...##..##############...",
        "........#########.......",
        "........................",
        "........................",
        "........................",
    ]);
}
