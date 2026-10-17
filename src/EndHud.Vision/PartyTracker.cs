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
/// One tracker follows one game window; use a new one for another window.
/// </summary>
public sealed class PartyTracker
{
    public const double RevivedHp = 0.1;

    public const int ChargeMedianWindow = 3;

    private readonly bool[] _dead = new bool[PartyLayout.SlotCount];
    private readonly Queue<double>[] _charge = Enumerable.Range(0, PartyLayout.SlotCount).Select(_ => new Queue<double>()).ToArray();
    private readonly PartySlot?[] _lastSlots = new PartySlot?[PartyLayout.SlotCount];
    private PartyState? _lastVisible;

    public PartyState Track(PartyState state)
    {
        if (!state.PanelVisible)
        {
            if (_lastVisible is null) return state;
            _lastVisible = _lastVisible with { ReadingsSinceSeen = _lastVisible.ReadingsSinceSeen + 1 };
            return _lastVisible;
        }

        var slots = state.Slots.Select(Track).ToList();
        _lastVisible = state with { Slots = slots, ReadingsSinceSeen = 0 };
        return _lastVisible;
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
