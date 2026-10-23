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
    /// (<see cref="PartyState.ReadingsSinceSeen"/>), or in the controller layouts (not recorded yet).
    /// </summary>
    public static IReadOnlyList<CounterReading> Read(Frame frame, PartyState state)
    {
        if (state.ReadingsSinceSeen > 0 || state.ActiveSlot is not { } active || state.Layout != HudLayout.KeyboardMouse) return [];
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
