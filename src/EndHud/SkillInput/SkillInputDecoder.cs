namespace EndHud.SkillInput;

/// <summary>Buttons that matter, on any pad: the left shoulder (L1/LB) and the four face buttons by place.</summary>
[Flags]
internal enum PadButtons { None = 0, Shoulder = 1, West = 2, North = 4, East = 8, South = 16 }

/// <summary>
/// Turns a pad's buttons into "slot N was cast", and nothing else: hold L1/LB and press the face button
/// in the slot's place on the diamond: left 1, top 2, right 3, bottom 4 (owner, 2026-10-23). The only
/// state kept is each pad's five buttons above. (The keyboard needs no decoding: Windows tells the
/// reader only of the four bound keys, as hotkeys; see <see cref="SkillHotkeys"/>.)
/// Not thread-safe: the reader's own thread is its only caller.
/// </summary>
internal sealed class SkillInputDecoder
{
    private readonly Dictionary<nint, PadButtons> _pads = [];

    /// <summary>A pad's buttons now: the slots whose face button went down while the shoulder is held.</summary>
    public IReadOnlyList<int> Pad(nint device, PadButtons now)
    {
        var before = _pads.GetValueOrDefault(device);
        _pads[device] = now;
        if (!now.HasFlag(PadButtons.Shoulder)) return [];
        var risen = now & ~before;
        var slots = new List<int>(1);
        if (risen.HasFlag(PadButtons.West)) slots.Add(1); // left of the diamond: square / X
        if (risen.HasFlag(PadButtons.North)) slots.Add(2);  // top: triangle / Y
        if (risen.HasFlag(PadButtons.East)) slots.Add(3);   // right: circle / B
        if (risen.HasFlag(PadButtons.South)) slots.Add(4);  // bottom: cross / A
        return slots;
    }

    /// <summary>A pad went away (unplugged): forget its buttons.</summary>
    public void Forget(nint device) => _pads.Remove(device);

    /// <summary>
    /// A PlayStation pad's HID button usages (button page, 1-based) to buttons: 1 square, 2 cross,
    /// 3 circle, 4 triangle, 5 L1 (DualShock 4 and DualSense over USB and plain Bluetooth). Others ignored.
    /// </summary>
    public static PadButtons FromSonyUsages(IEnumerable<ushort> usages)
    {
        var buttons = PadButtons.None;
        foreach (ushort usage in usages)
        {
            buttons |= usage switch
            {
                1 => PadButtons.West,
                2 => PadButtons.South,
                3 => PadButtons.East,
                4 => PadButtons.North,
                5 => PadButtons.Shoulder,
                _ => PadButtons.None,
            };
        }
        return buttons;
    }

    /// <summary>XINPUT_GAMEPAD.wButtons to buttons (Microsoft's documented bits).</summary>
    public static PadButtons FromXInput(ushort buttons) =>
        ((buttons & 0x0100) != 0 ? PadButtons.Shoulder : 0)   // LEFT_SHOULDER
        | ((buttons & 0x4000) != 0 ? PadButtons.West : 0)     // X
        | ((buttons & 0x8000) != 0 ? PadButtons.North : 0)    // Y
        | ((buttons & 0x2000) != 0 ? PadButtons.East : 0)     // B
        | ((buttons & 0x1000) != 0 ? PadButtons.South : 0);   // A
}
