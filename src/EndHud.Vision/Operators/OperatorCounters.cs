namespace EndHud.Vision.Operators;

/// <summary>One operator's own counter, e.g. Laevatain's 熔火 (0–4).</summary>
/// <param name="Id">Stable key used in rotation files, e.g. "melting-flame".</param>
/// <param name="NameZh">As the game names it, e.g. "熔火".</param>
/// <param name="Max">The most the game draws. For an uncapped counter, only how far the rotation editor's value list goes.</param>
/// <param name="Uncapped">
/// The game sets no maximum (Si's damage stacks): the count is drawn as a plain number, never as
/// segments out of <paramref name="Max"/>, and a reading above it is kept as it is.
/// </param>
public sealed record CounterDefinition(string OperatorId, string Id, string NameZh, string NameEn, int Max, bool Uncapped);

/// <param name="Value">The count; null when it can't be told this reading (covered by an effect).</param>
public sealed record CounterReading(string OperatorId, string CounterId, int? Value);

/// <summary>
/// Operators' own counters, drawn left of the skill-point bar for the controlled operator. Each
/// operator draws theirs differently, so each has its own reader here; an operator without one has
/// no counter reading (conditions on it count as unknown). Confirmed on screen so far: Laevatain's
/// 熔火, Typhoeus's 猎矢 and 启示, and Zhuang Fangyi's 青霆剑.
/// </summary>
public static class OperatorCounters
{
    public static readonly CounterDefinition MeltingFlame = new("Laevatain", "melting-flame", "熔火", "Melting Flame", 4, Uncapped: false);
    public static readonly CounterDefinition HuntingArrow = new("Typhoeus", "hunting-arrow", "猎矢", "Hunting Arrow", 4, Uncapped: false);
    public static readonly CounterDefinition Sign = new("Typhoeus", "sign", "启示", "Sign", 8, Uncapped: false);
    public static readonly CounterDefinition Sunderblade = new("Zhuang_Fangyi", "sunderblade", "青霆剑", "Sunderblade", 9, Uncapped: false);

    // Si's damage stacks, stored by her battle skill. The game sets no maximum (owner,
    // 2026-10-22); 99 only bounds the rotation editor's list of values.
    public static readonly CounterDefinition SiStacks = new("Si", "damage-stacks", "伤害层数", "Damage stacks", 99, Uncapped: true);

    public static IReadOnlyList<CounterDefinition> Definitions { get; } = [MeltingFlame, HuntingArrow, Sign, Sunderblade, SiStacks];

    public static IEnumerable<CounterDefinition> For(string operatorId) => Definitions.Where(d => d.OperatorId == operatorId);

    /// <summary>
    /// The controlled operator's counters, once the squad is identified: none when no slot is
    /// clearly controlled (cutscenes, menus) or the controlled operator isn't recognised. None either
    /// for the squad panel remembered while it's out of sight (<see cref="PartyState.ReadingsSinceSeen"/>):
    /// the frame is something else then, e.g. Si's ultimate cutscene, whose pink read as a full flower.
    /// </summary>
    public static IReadOnlyList<CounterReading> ReadControlled(Frame frame, PartyState state)
    {
        if (state.ReadingsSinceSeen > 0 || state.ActiveSlot is not { } slot || slot > state.Slots.Count) return [];
        return state.Slots[slot - 1].Operator?.Operator.Id is { } id ? Read(frame, id) : [];
    }

    /// <summary>Read the counters of <paramref name="operatorId"/>, which must be the controlled operator.</summary>
    public static IReadOnlyList<CounterReading> Read(Frame frame, string operatorId)
    {
        var readings = new List<CounterReading>();
        foreach (var counter in For(operatorId))
        {
            int? value = counter == MeltingFlame ? ReadPetals(frame)
                : counter == HuntingArrow ? ReadArrows(frame)
                : counter == Sign ? ReadSigns(frame)
                : counter == Sunderblade ? ReadSunderblades(frame)
                : counter == SiStacks ? ReadSiStacks(frame)
                : null;
            readings.Add(new CounterReading(operatorId, counter.Id, value));
        }
        return readings;
    }

    /// <summary>
    /// Laevatain's 熔火: a four-petal flower left of the skill-point bar. 0 stacks: every petal dark;
    /// 1–3: that many petals white (with the numeral I–III beside them); 4: the whole flower pink
    /// (with IV). Petals are judged as a whole, so the thin numeral strokes don't count as a petal.
    /// </summary>
    private static int? ReadPetals(Frame frame)
    {
        var ui = new UiScaler(frame.Width, frame.Height);
        var centre = ui.BottomCenter(new PixelRect(FlowerCentreX, FlowerCentreY, 1, 1));
        double s = ui.Scale;

        int lit = 0, pinkPetals = 0, darkPetals = 0;
        foreach (var (qx, qy) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
        {
            int n = 0, white = 0, pink = 0, dark = 0;
            for (double r = PetalInner * s; r <= PetalOuter * s; r += 1)
            for (double a = 20; a <= 70; a += 5)
            {
                double rad = a * Math.PI / 180;
                int x = (int)Math.Round(centre.X + qx * r * Math.Cos(rad)), y = (int)Math.Round(centre.Y + qy * r * Math.Sin(rad));
                if ((uint)x >= (uint)frame.Width || (uint)y >= (uint)frame.Height) continue;
                var (cr, cg, cb) = frame.GetPixel(x, y);
                int max = Math.Max(cr, Math.Max(cg, cb)), min = Math.Min(cr, Math.Min(cg, cb));
                n++;
                if (min > 170) white++;
                else if (cr > 170 && cr - cg > 70 && cb > cg) pink++;
                else if (max < 110 && max - min < 40) dark++;
            }
            if (n == 0) return null;
            if (white > n * PetalShare) lit++;
            else if (pink > n * PinkPetalShare) pinkPetals++;
            else if (dark > n * PetalShare) darkPetals++;
        }

        if (pinkPetals >= 3) return 4;
        if (pinkPetals > 0 || lit + darkPetals < 4) return null; // not the flower: covered, or not shown
        return lit;
    }

    /// <summary>
    /// Typhoeus's 猎矢: four slanted bars stacked left of the skill-point bar, one lit cyan per
    /// arrow; the others grey. Lit bars drain from the top as she shoots, but the count is what's
    /// read, not which bars. With all four, a white streak sweeps over them: lit bars run from cyan
    /// to near white, but always with green and blue at full. Null when any bar is neither lit nor
    /// grey: the icon dims to a pale teal while she hovers, which can't be told apart reliably.
    /// </summary>
    private static int? ReadArrows(Frame frame)
    {
        var ui = new UiScaler(frame.Width, frame.Height);
        int lit = 0;
        bool litAbove = false; // a lit bar above a grey one: not the icon as drawn (it's moving, or covered)
        foreach (int barY in ArrowBarY)
        {
            var r = ui.BottomCenter(new PixelRect(ArrowBarX, barY - ArrowBarHalfHeight, ArrowBarWidth, 2 * ArrowBarHalfHeight + 1))
                .ClampTo(frame.Width, frame.Height);
            if (r.IsEmpty) return null;
            int n = 0, cyan = 0, grey = 0;
            for (int y = r.Y; y < r.Bottom; y++)
            for (int x = r.X; x < r.Right; x++)
            {
                var (cr, cg, cb) = frame.GetPixel(x, y);
                int max = Math.Max(cr, Math.Max(cg, cb)), min = Math.Min(cr, Math.Min(cg, cb));
                n++;
                if (cg >= 220 && cb >= 190) cyan++;
                else if (max - min <= 20 && max >= 70 && max <= 160) grey++;
            }
            if (cyan > n * ArrowBarShare)
            {
                lit++;
                litAbove = true;
            }
            else if (grey <= n * ArrowBarShare || litAbove) return null;
        }
        return lit;
    }

    /// <summary>
    /// Typhoeus's 启示: eight dots in two columns right of her arrow bars, lit white one per Sign
    /// (filling from the bottom row up, left dot first), grey otherwise. Null when any dot is neither,
    /// or when the lit ones aren't in that order (the HUD moving, or something over it).
    /// </summary>
    private static int? ReadSigns(Frame frame)
    {
        var ui = new UiScaler(frame.Width, frame.Height);
        var lit = new List<bool>(8);
        foreach (int rowY in ArrowBarY.Reverse())
        foreach (int dotX in SignDotX)
        {
            var r = ui.BottomCenter(new PixelRect(dotX - SignDotHalfSize, rowY - SignDotHalfSize, 2 * SignDotHalfSize + 1, 2 * SignDotHalfSize + 1))
                .ClampTo(frame.Width, frame.Height);
            if (r.IsEmpty) return null;
            int n = 0, white = 0, grey = 0;
            for (int y = r.Y; y < r.Bottom; y++)
            for (int x = r.X; x < r.Right; x++)
            {
                var (cr, cg, cb) = frame.GetPixel(x, y);
                int max = Math.Max(cr, Math.Max(cg, cb)), min = Math.Min(cr, Math.Min(cg, cb));
                n++;
                if (min >= 190 && max - min <= 40) white++;
                else if (max - min <= 40 && max >= 80 && max <= 175) grey++;
            }
            if (white > n * ArrowBarShare) lit.Add(true);
            else if (grey > n * ArrowBarShare) lit.Add(false);
            else return null;
        }
        int count = lit.Count(l => l);
        return lit.Take(count).All(l => l) ? count : null; // the first ones, bottom up
    }

    /// <summary>
    /// Zhuang Fangyi's 青霆剑: a diamond-shaped grid of nine cells (rows of 1, 2, 3, 2, 1) left of the
    /// skill-point bar, one lit cyan per sword, dark grey otherwise; the game prints the count beside
    /// it. Null when any cell is neither (the HUD hidden by an animation, a flash over it).
    /// </summary>
    private static int? ReadSunderblades(Frame frame)
    {
        var ui = new UiScaler(frame.Width, frame.Height);
        int lit = 0;
        foreach (var (cx, cy) in SunderbladeCells)
        {
            var r = ui.BottomCenter(new PixelRect(cx - SunderbladeHalfSize, cy - SunderbladeHalfSize, 2 * SunderbladeHalfSize + 1, 2 * SunderbladeHalfSize + 1))
                .ClampTo(frame.Width, frame.Height);
            if (r.IsEmpty) return null;
            int n = 0, cyan = 0, grey = 0;
            for (int y = r.Y; y < r.Bottom; y++)
            for (int x = r.X; x < r.Right; x++)
            {
                var (cr, cg, cb) = frame.GetPixel(x, y);
                int max = Math.Max(cr, Math.Max(cg, cb)), min = Math.Min(cr, Math.Min(cg, cb));
                n++;
                if (cg >= 200 && cb >= 170 && cg - cr >= 40) cyan++;
                else if (max - min <= 25 && max >= 70 && max <= 150) grey++;
            }
            if (cyan > n * ArrowBarShare) lit++;
            else if (grey <= n * ArrowBarShare) return null;
        }
        return lit;
    }

    /// <summary>
    /// Si's damage stacks, left of the HP bar where Laevatain's flower is: 0 is a hollow green
    /// four-lobed flower with no number; from 1 a green cross with a white number on it
    /// (<see cref="BadgeNumber"/>), as many digits as it takes. Null when neither is there (covered,
    /// the cast's flash, or the HUD shaken by a hit), or when the number can't be told.
    /// </summary>
    private static int? ReadSiStacks(Frame frame)
    {
        var ui = new UiScaler(frame.Width, frame.Height);
        var badge = ui.BottomCenter(SiBadgeArea).ClampTo(frame.Width, frame.Height);
        if (badge.IsEmpty) return null;
        int n = 0, green = 0;
        for (int y = badge.Y; y < badge.Bottom; y++)
        for (int x = badge.X; x < badge.Right; x++)
        {
            var (cr, cg, cb) = frame.GetPixel(x, y);
            n++;
            if (cg - Math.Max(cr, cb) >= 60 && cg >= 120) green++;
        }
        double share = green / (double)n;
        if (share < SiGreenMin) return null; // no badge: another operator's HUD, or something over it
        if (BadgeNumber.Read(frame, ui.BottomCenter(SiStacksArea)) is { } stacks) return stacks;
        return share >= SiFlowerGreenMin ? 0 : null; // the hollow flower is nearly half green; a cross with a number on it far less
    }

    // Si's badge, measured at 4K on the owner's Si fight (Fixtures/si), bottom-centre anchored: the
    // whole badge, and its share of bright green. Badges with a number: 0.024–0.079 (the cross is
    // mostly hidden by the digits and its dark outline); the zero flower 0.45–0.46; every other
    // operator's HUD here (Fixtures/battle-hud, Fixtures/counters) at most 0.001.
    private static readonly PixelRect SiBadgeArea = new(1511, 1956, 52, 54);
    private const double SiGreenMin = 0.012;
    private const double SiFlowerGreenMin = 0.3;

    // The nine cells' centres, measured at 4K on the owner's recording (Fixtures/counters, zhuang-*),
    // bottom-centre anchored. Lit: G 220-255, B 190-233, R 145-180; unlit grey 100-125.
    private static readonly (int X, int Y)[] SunderbladeCells =
        [(1523, 1948), (1510, 1961), (1536, 1961), (1496, 1974), (1523, 1974), (1548, 1973), (1510, 1987), (1535, 1987), (1523, 2000)];
    private const int SunderbladeHalfSize = 2;

    // Measured at 4K on a recording of the owner's fight (Fixtures/counters, native-*), bottom-centre
    // anchored: the bars' middle rows (the dots share them) and the straight part of each bar.
    private static readonly int[] ArrowBarY = [1959, 1973, 1987, 2001];
    private const int ArrowBarX = 1512;
    private const int ArrowBarWidth = 28;
    private const int ArrowBarHalfHeight = 2;
    // Lit bars: G and B at or near 255 (R from 50 for cyan up to 250 under the white streak);
    // empty ones neutral grey 85-130; the dimmed teal G about 190.
    private const double ArrowBarShare = 0.6;
    // The two dot columns' centres; lit dots measured 215-255 on every channel, unlit grey 100-150.
    private static readonly int[] SignDotX = [1552, 1567];
    private const int SignDotHalfSize = 2;

    // Si's badge, measured at 4K on the owner's Si fight (Fixtures/si), bottom-centre anchored: where
    // its number is drawn (digits 26 px tall, centred on x 1536, in the dark middle of the cross).
    internal static readonly PixelRect SiStacksArea = new(1503, 1960, 66, 42);

    // Measured on the battle recording at 4K (bottom-centre anchored).
    private const int FlowerCentreX = 1535;
    private const int FlowerCentreY = 1982;
    private const int PetalInner = 10;
    private const int PetalOuter = 30;
    // A lit petal measured 0.65 white, a dark one 0.86 dark; a pink one only 0.39–0.47 bright pink
    // (it shades darker towards its edge).
    private const double PetalShare = 0.5;
    private const double PinkPetalShare = 0.3;
}
