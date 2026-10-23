namespace EndHud.Vision.Operators;

/// <summary>
/// Where a counter's drawing is on this frame. The readers in <see cref="OperatorCounters"/> are
/// measured at 4K beside the controlled operator's HP bar (bottom-centre anchored); the game draws
/// the same counter smaller on the operator's squad portrait (bottom-left anchored), so a place
/// maps the one onto the other: shifted to the portrait and scaled by <see cref="Size"/>.
/// </summary>
internal readonly struct CounterPlace
{
    // The point every portrait placement is measured from: the middle of the drawing beside the HP
    // bar (Laevatain's flower centre; Si's badge and the others are drawn around it).
    private const double AnchorX = 1535, AnchorY = 1982;

    private readonly UiScaler _ui;
    private readonly int _height;
    private readonly bool _atPortrait;
    private readonly double _x, _y;

    private CounterPlace(UiScaler ui, int height, bool atPortrait, double x, double y, double size)
    {
        _ui = ui;
        _height = height;
        _atPortrait = atPortrait;
        _x = x;
        _y = y;
        Size = size;
    }

    /// <summary>On a squad portrait rather than beside the HP bar.</summary>
    public bool OnPortrait => _atPortrait;

    /// <summary>How much smaller than beside the HP bar the drawing is (1 there).</summary>
    public double Size { get; }

    /// <summary>Pixels per 4K pixel of the drawing as it's drawn here.</summary>
    public double Scale => _ui.Scale * Size;

    /// <summary>Beside the controlled operator's HP bar, where the readers were measured.</summary>
    public static CounterPlace BesideHpBar(Frame frame) => new(new UiScaler(frame.Width, frame.Height), frame.Height, false, 0, 0, 1);

    /// <summary>
    /// On a squad portrait: the anchor point beside the HP bar lands on (<paramref name="x"/>,
    /// <paramref name="y"/>), in 4K bottom-left anchored coordinates, and the drawing is
    /// <paramref name="size"/> times as big.
    /// </summary>
    public static CounterPlace AtPortrait(Frame frame, double x, double y, double size) =>
        new(new UiScaler(frame.Width, frame.Height), frame.Height, true, x, y, size);

    /// <summary>A rectangle measured at 4K beside the HP bar, as drawn here on this frame.</summary>
    public PixelRect Map(PixelRect reference)
    {
        if (!_atPortrait) return _ui.BottomCenter(reference);
        double x = _x + (reference.X - AnchorX) * Size, y = _y + (reference.Y - AnchorY) * Size;
        double s = _ui.Scale;
        return new PixelRect(
            (int)Math.Round(x * s),
            _height - (int)Math.Round((UiScaler.ReferenceHeight - y) * s),
            Math.Max(1, (int)Math.Round(reference.Width * Size * s)),
            Math.Max(1, (int)Math.Round(reference.Height * Size * s)));
    }
}

/// <summary>
/// Counters of the operators who aren't controlled. The game draws each operator's counter on
/// their squad portrait, at its lower left, the same drawing as beside the HP bar made smaller, so
/// the same readers (<see cref="OperatorCounters.ReadAt"/>) read it there.
/// </summary>
public static class PortraitCounters
{
    /// <summary>
    /// The counters on the portraits of the recognised operators who aren't controlled. None when no
    /// slot is clearly controlled, when the squad panel is remembered rather than seen
    /// (<see cref="PartyState.ReadingsSinceSeen"/>), out of combat, or in the controller layouts
    /// (not recorded yet). Out of combat the game draws no counter on a portrait: the level text is
    /// there instead of the combo bars (all four recordings), and as a fight ends the dimmed portrait
    /// read as Typhoeus's empty bars and dots. The combo bars are the sign of combat here rather than
    /// <see cref="PartyState.InCombat"/>, which also needs the skill points, unreadable for a moment
    /// when a segment fills.
    /// </summary>
    public static IReadOnlyList<CounterReading> Read(Frame frame, PartyState state)
    {
        if (state.ReadingsSinceSeen > 0 || !state.Slots.Any(s => s.Combo is not null)
            || state.ActiveSlot is not { } active || state.Layout != HudLayout.KeyboardMouse) return [];
        var squad = SquadGeometry.For(state.Layout);
        var first = squad.Portrait(0);
        var readings = new List<CounterReading>();
        foreach (var slot in state.Slots)
        {
            if (slot.Number == active || slot.Empty || slot.Dead || slot.Operator?.Operator.Id is not { } id) continue;
            var portrait = squad.Portrait(slot.Number - 1);
            foreach (var counter in OperatorCounters.For(id))
            {
                if (!Placements.TryGetValue(counter.Id, out var at)) continue; // not seen on a portrait yet
                var place = CounterPlace.AtPortrait(frame, at.X + portrait.X - first.X, at.Y + portrait.Y - first.Y, at.Size);
                readings.Add(new CounterReading(id, counter.Id, OperatorCounters.ReadAt(frame, counter, place)));
            }
        }
        return readings;
    }

    /// <summary>
    /// Whether the drawing's dark outline shows where it should: each pair is a point on the outline
    /// and a point of the drawing beside it (4K, beside the HP bar), and the outline must be clearly
    /// the darker of the two. Beside the HP bar the drawing sits on the HUD's own dark backing; on a
    /// portrait it sits on art and scenery of any colour, and on portraits with no counter grey hair
    /// read as Typhoeus's empty bars and Zhuang Fangyi's empty cells. True for a counter without
    /// pairs: Si's badge has its own checks, and Laevatain's flower has no outline that tells it
    /// from a portrait (see <see cref="OutlinePairs"/>).
    /// </summary>
    internal static bool OutlineShows(Frame frame, CounterDefinition counter, CounterPlace place) =>
        OutlineContrasts(frame, counter, place) is not { } contrasts
        || contrasts.Count(c => c >= OutlineContrast) >= contrasts.Length * OutlineShare;

    /// <summary>Per pair, how much darker the outline point is; null for a counter without pairs.</summary>
    internal static double[]? OutlineContrasts(Frame frame, CounterDefinition counter, CounterPlace place) =>
        OutlinePairs.TryGetValue(counter.Id, out var pairs)
            ? [.. pairs.Select(p => Brightness(frame, place, p.Light, darkest: false) - Brightness(frame, place, p.Dark, darkest: true))]
            : null;

    /// <summary>The brightest channel around a point (3 x 3 pixels): the darkest of them, or their mean.</summary>
    private static double Brightness(Frame frame, CounterPlace place, (double X, double Y) at, bool darkest)
    {
        var p = place.Map(new PixelRect((int)Math.Floor(at.X), (int)Math.Floor(at.Y), 1, 1));
        double sum = 0, min = 255;
        int n = 0;
        for (int y = p.Y - 1; y <= p.Y + 1; y++)
        for (int x = p.X - 1; x <= p.X + 1; x++)
        {
            if ((uint)x >= (uint)frame.Width || (uint)y >= (uint)frame.Height) continue;
            var (r, g, b) = frame.GetPixel(x, y);
            int v = Math.Max(r, Math.Max(g, b));
            sum += v;
            min = Math.Min(min, v);
            n++;
        }
        return n == 0 ? 0 : darkest ? min : sum / n;
    }

    // Measured on the fixtures (Fixtures/portrait-counters), every portrait of an operator who isn't
    // controlled: with the outline at least 25 darker, the drawn icons had all their pairs (100 %;
    // Typhoeus's two arrow-fill flashes 23-25 %, which then read as unknown), the portraits without a
    // counter at most 58 % (Typhoeus's pairs) and 50 % (Zhuang Fangyi's).
    internal const double OutlineContrast = 25;
    internal const double OutlineShare = 0.8;

    // Laevatain's flower has none: the outline round its petals' tips was clear on 67-92 % of the
    // pairs on drawn flowers and on up to 83 % on portraits without one, at every radius (32-38) and
    // contrast (10-25) tried; its seam between the lower petals did no better. It is read only on
    // Laevatain's own portrait, where the flower was drawn in every in-combat frame recorded.
    private static readonly Dictionary<string, ((double X, double Y) Dark, (double X, double Y) Light)[]> OutlinePairs = BuildOutlinePairs();

    private static Dictionary<string, ((double X, double Y) Dark, (double X, double Y) Light)[]> BuildOutlinePairs()
    {
        // Typhoeus's icon: the dark line above and below each bar (bars 14 apart), against the bar.
        var bars = new List<((double, double), (double, double))>();
        foreach (int y in new[] { 1959, 1973, 1987, 2001 })
        for (int x = 1516; x <= 1536; x += 4)
        {
            bars.Add(((x, y - 7), (x, y)));
            bars.Add(((x, y + 7), (x, y)));
        }
        // Zhuang Fangyi's grid: the dark lattice between neighbouring cells, against each of them.
        (int X, int Y)[] cells = [(1523, 1948), (1510, 1961), (1536, 1961), (1496, 1974), (1523, 1974), (1548, 1973), (1510, 1987), (1535, 1987), (1523, 2000)];
        var grid = new List<((double, double), (double, double))>();
        for (int i = 0; i < cells.Length; i++)
        for (int j = i + 1; j < cells.Length; j++)
        {
            var (a, b) = (cells[i], cells[j]);
            if (Math.Abs(a.X - b.X) > 14 || Math.Abs(a.Y - b.Y) > 14) continue;
            var middle = ((a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0);
            grid.Add((middle, (a.X, a.Y)));
            grid.Add((middle, (b.X, b.Y)));
        }
        return new()
        {
            [OperatorCounters.HuntingArrow.Id] = [.. bars],
            [OperatorCounters.Sign.Id] = [.. bars],
            [OperatorCounters.Sunderblade.Id] = [.. grid],
        };
    }

    /// <summary>
    /// Each counter on slot 1's portrait, keyboard and mouse: where the anchor beside the HP bar
    /// lands (4K, bottom-left anchored) and how big the drawing is there; other slots are the same
    /// distance from their own portrait. Measured on the owner's four recordings at 4K (Si in slots
    /// 1 and 3, Laevatain in 2, Typhoeus and Zhuang Fangyi in 1): the drawing is about 0.7 the size
    /// (Si's badge 36 × 38 px against 53 × 52 beside the HP bar). Each value is the middle of the
    /// placements under which every labelled frame reads right (a sweep over x, y and size); the
    /// room either side is in the comment.
    /// </summary>
    internal static readonly Dictionary<string, (double X, double Y, double Size)> Placements = new()
    {
        // x 94.5-100.5, y 1940.5-1943.5 at this size; sizes 0.64-0.68.
        [OperatorCounters.SiStacks.Id] = (97.5, 1942, 0.68),
        // x 96.5-100.5, y 1940.5-1942.5; sizes 0.66-0.94 at this point.
        [OperatorCounters.MeltingFlame.Id] = (98.5, 1941.5, 0.74),
        // Typhoeus's arrows: x 85.5-103.5, y 1940.5-1945.5; his dots: x 94.5-99.5, y 1940.5-1944.5.
        [OperatorCounters.HuntingArrow.Id] = (97, 1942.5, 0.7),
        [OperatorCounters.Sign.Id] = (97, 1942.5, 0.7),
        // x 95.5-99.5, y 1940.5-1943.5; sizes 0.68-0.72.
        [OperatorCounters.Sunderblade.Id] = (97.5, 1942, 0.7),
    };
}
