namespace EndHud.Vision.Operators;

/// <summary>One operator's own counter, e.g. Laevatain's 熔火 (0–4).</summary>
/// <param name="Id">Stable key used in rotation files, e.g. "melting-flame".</param>
/// <param name="NameZh">As the game names it, e.g. "熔火".</param>
public sealed record CounterDefinition(string OperatorId, string Id, string NameZh, string NameEn, int Max);

/// <param name="Value">The count; null when it can't be told this reading (covered by an effect).</param>
public sealed record CounterReading(string OperatorId, string CounterId, int? Value);

/// <summary>
/// Operators' own counters, drawn left of the skill-point bar for the controlled operator. Each
/// operator draws theirs differently, so each has its own reader here; an operator without one has
/// no counter reading (conditions on it count as unknown). Confirmed on screen so far: Laevatain.
/// Typhoeus (猎矢, 启示) and Zhuang Fangyi (青霆剑) are known but need combat screenshots with
/// their counters partly filled before their readers can be written and checked.
/// </summary>
public static class OperatorCounters
{
    public static readonly CounterDefinition MeltingFlame = new("Laevatain", "melting-flame", "熔火", "Melting Flame", 4);

    public static IReadOnlyList<CounterDefinition> Definitions { get; } = [MeltingFlame];

    public static IEnumerable<CounterDefinition> For(string operatorId) => Definitions.Where(d => d.OperatorId == operatorId);

    /// <summary>
    /// The controlled operator's counters, once the squad is identified: none when no slot is
    /// clearly controlled (cutscenes, menus) or the controlled operator isn't recognised.
    /// </summary>
    public static IReadOnlyList<CounterReading> ReadControlled(Frame frame, PartyState state)
    {
        if (state.ActiveSlot is not { } slot || slot > state.Slots.Count) return [];
        return state.Slots[slot - 1].Operator?.Operator.Id is { } id ? Read(frame, id) : [];
    }

    /// <summary>Read the counters of <paramref name="operatorId"/>, which must be the controlled operator.</summary>
    public static IReadOnlyList<CounterReading> Read(Frame frame, string operatorId)
    {
        var readings = new List<CounterReading>();
        foreach (var counter in For(operatorId))
        {
            int? value = counter == MeltingFlame ? ReadPetals(frame) : null;
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
