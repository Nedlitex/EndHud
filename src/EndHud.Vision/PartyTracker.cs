namespace EndHud.Vision;

/// <summary>
/// Carries state across readings where one frame can't be trusted on its own.
/// Panel: see <see cref="HeldMissedReadings"/>.
/// Ultimate charge: skill effects of the arc's own colour (sparkles next to its end) can add a
/// few percent in a single frame. The charge shown is the median of the last
/// <see cref="ChargeMedianWindow"/> readings, so a one-frame spike never reaches the HUD.
/// Death: an effect passing over the death mark can hide it for a frame, which would make a
/// fallen operator flash back to life with a sliver of "HP" (seen in the battle recording). So
/// a fallen operator stays fallen until a reading shows them clearly alive again — HP above
/// <see cref="RevivedHp"/>, as after a revive or at the start of the next battle.
/// </summary>
public sealed class PartyTracker
{
    public const double RevivedHp = 0.1;

    /// <summary>
    /// A heavy skill effect can hide enough of the panel for one reading to miss it; keep showing
    /// the last reading through this many misses rather than flashing "not visible".
    /// </summary>
    public const int HeldMissedReadings = 1;

    public const int ChargeMedianWindow = 3;

    private readonly bool[] _dead = new bool[PartyLayout.SlotCount];
    private readonly Queue<double>[] _charge = Enumerable.Range(0, PartyLayout.SlotCount).Select(_ => new Queue<double>()).ToArray();
    private PartyState? _lastVisible;
    private int _missed;

    public PartyState Track(PartyState state)
    {
        if (!state.PanelVisible)
        {
            if (_lastVisible is not null && _missed < HeldMissedReadings)
            {
                _missed++;
                return _lastVisible;
            }
            return state;
        }
        _missed = 0;
        _lastVisible = TrackDeaths(state);
        return _lastVisible;
    }

    private PartyState TrackDeaths(PartyState state)
    {
        var slots = state.Slots.Select(slot =>
        {
            int i = slot.Number - 1;
            if (slot.Dead)
            {
                _dead[i] = true;
                _charge[i].Clear();
                return slot;
            }
            if (_dead[i] && (slot.Hp ?? 0) <= RevivedHp)
                return slot with { Dead = true, Hp = 0, Level = null, Combo = null, Ultimate = null };
            _dead[i] = false;
            return slot with { Ultimate = SmoothCharge(i, slot.Ultimate) };
        }).ToList();
        return state with { Slots = slots };
    }

    private UltimateReading? SmoothCharge(int slot, UltimateReading? reading)
    {
        var recent = _charge[slot];
        if (reading is null) return null;
        if (reading.Ready)
        {
            recent.Clear(); // charging starts again from empty after it's used
            return reading;
        }
        recent.Enqueue(reading.Progress);
        while (recent.Count > ChargeMedianWindow) recent.Dequeue();
        var sorted = recent.Order().ToList();
        return reading with { Progress = sorted[sorted.Count / 2] };
    }
}
