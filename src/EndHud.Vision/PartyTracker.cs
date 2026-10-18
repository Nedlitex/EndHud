namespace EndHud.Vision;

/// <summary>
/// Carries state across readings where one frame can't be trusted on its own.
/// Unreadable values: whenever something can't be read — the whole panel (HUD fade-in, a
/// cutscene, effects hiding the marks), one HP bar, a combo bar or an ultimate circle —
/// the last value that was read stays on show until a new one is read.
/// Ultimate charge: skill effects of the arc's own colour (sparkles next to its end) can add a
/// few percent in a single frame. The charge shown is the median of the last
/// <see cref="ChargeMedianWindow"/> readings, so a one-frame spike never reaches the HUD.
/// Death: an effect passing over the death mark can hide it for a frame, which would make a
/// fallen operator flash back to life with a sliver of "HP" (seen in the battle recording). So
/// a fallen operator stays fallen until a reading shows them clearly alive again — HP above
/// <see cref="RevivedHp"/>, as after a revive or at the start of the next battle.
/// Squad size: with keyboard and mouse an empty place shows the scenery, which can look like an
/// operator (a road stripe passes for a "-" mark, blue water for a full HP bar), and effects can
/// hide a real operator's marks. A wrong size moves every ultimate circle and battle skill button
/// to the wrong operator (and a circle read as just emptied is an ultimate "used"). So a new size
/// is taken only once it has been read enough times in a row, and the readings before that count
/// as not seeing the panel. In the owner's recordings, scenery made a squad of two look larger for
/// up to 10 readings in a row (2.5 s), and hidden marks made a full squad look smaller for under 4:
/// a larger squad takes <see cref="MoreOperatorsReadings"/>, a smaller one
/// <see cref="FewerOperatorsReadings"/>. A squad really changes only in the team menu, so the wait
/// shows the squad from before the menu a little longer, as not seen. (Just after the menu isn't
/// trusted at once: an ultimate's cutscene hides the panel as long.)
/// One tracker follows one game window; use a new one for another window.
/// </summary>
public sealed class PartyTracker
{
    public const double RevivedHp = 0.1;

    public const int ChargeMedianWindow = 3;

    public const int MoreOperatorsReadings = 12;
    public const int FewerOperatorsReadings = 4;

    private readonly bool[] _dead = new bool[PartyLayout.SlotCount];
    private readonly Queue<double>[] _charge = Enumerable.Range(0, PartyLayout.SlotCount).Select(_ => new Queue<double>()).ToArray();
    private readonly PartySlot?[] _lastSlots = new PartySlot?[PartyLayout.SlotCount];
    private PartyState? _lastVisible;
    private int? _size;
    private (int Size, int Readings)? _newSize;

    public PartyState Track(PartyState state)
    {
        if (!state.PanelVisible || !SizeSettled(state.SquadSize))
        {
            if (_lastVisible is null) return state;
            _lastVisible = _lastVisible with { ReadingsSinceSeen = _lastVisible.ReadingsSinceSeen + 1 };
            return _lastVisible;
        }

        var slots = state.Slots.Select(Track).ToList();
        _lastVisible = state with { Slots = slots, ReadingsSinceSeen = 0 };
        return _lastVisible;
    }

    /// <summary>Is <paramref name="size"/> the squad's size (the first one read, or one read enough times in a row)?</summary>
    private bool SizeSettled(int size)
    {
        if (_size is null || size == _size)
        {
            _size = size;
            _newSize = null;
            return true;
        }
        int readings = _newSize is { } pending && pending.Size == size ? pending.Readings + 1 : 1;
        if (readings < (size > _size ? MoreOperatorsReadings : FewerOperatorsReadings))
        {
            _newSize = (size, readings);
            return false;
        }
        _size = size;
        _newSize = null;
        return true;
    }

    private PartySlot Track(PartySlot slot)
    {
        int i = slot.Number - 1;
        if (slot.Empty)
        {
            // No operator here (a squad of fewer than four): nothing carried over, now or later.
            _dead[i] = false;
            _charge[i].Clear();
            _lastSlots[i] = null;
            return slot;
        }
        if (slot.Dead)
        {
            _dead[i] = true;
            _charge[i].Clear();
            return _lastSlots[i] = slot;
        }
        if (_dead[i] && (slot.Hp ?? 0) <= RevivedHp)
            return _lastSlots[i] = slot with { Dead = true, Hp = 0, Level = null, Combo = null, Ultimate = null };
        if (_dead[i])
        {
            _dead[i] = false;
            _lastSlots[i] = null; // revived: nothing from before the fall carries over
        }

        var last = _lastSlots[i];
        // The level text and the combo bar share one spot: a level read means out of combat (no
        // bar), while neither means the bar was there but couldn't be read.
        var combo = slot.Combo ?? (slot.Level is null ? last?.Combo : null);
        return _lastSlots[i] = slot with
        {
            Hp = slot.Hp ?? last?.Hp,
            Combo = combo,
            Ultimate = SmoothCharge(i, slot.Ultimate) ?? last?.Ultimate,
        };
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
