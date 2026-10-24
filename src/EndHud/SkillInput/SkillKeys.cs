namespace EndHud.SkillInput;

/// <summary>
/// How a battle-skill key is stored (<see cref="HudConfig.SkillKeys"/>): the physical key, as its set-1
/// scan code, plus <see cref="Extended"/> for an extended (E0) key. A physical key, not a character, so
/// the keyboard layout or the input language never changes which key casts which slot.
/// </summary>
internal static class SkillKeyCodes
{
    /// <summary>The top-row 1, 2, 3, 4: slots 1 to 4.</summary>
    public static readonly int[] Defaults = [0x02, 0x03, 0x04, 0x05];

    /// <summary>Added to the scan code of an extended (E0) key, such as the arrows or the numpad's Enter.</summary>
    public const int Extended = 0x100;

    /// <summary>The highest stored code: any scan code, extended.</summary>
    public const int Max = 0x1FF;

    /// <summary>The key of a WM_KEYDOWN/WM_SYSKEYDOWN message: bits 16-23 of lParam the scan code, bit 24 extended.</summary>
    public static int FromKeyMessage(nint lParam) => (int)((lParam >> 16) & 0xFF) | (((lParam >> 24) & 1) != 0 ? Extended : 0);

    /// <summary>Whether <paramref name="keys"/> binds four distinct real keys, one per slot.</summary>
    public static bool AreValid(int[]? keys) =>
        keys is { Length: 4 } && keys.Distinct().Count() == 4 && keys.All(k => k is > 0 and <= Max);
}

/// <summary>One hotkey the reader holds while reading: its id, the slot it casts, and the virtual key Windows matches.</summary>
internal readonly record struct SkillHotkey(int Id, int Slot, uint VirtualKey);

/// <summary>
/// The hotkeys that stand for the four bound keys while reading (<see cref="SkillInputReader"/>). The game
/// runs as administrator; EndHud doesn't, and Windows keeps an elevated window's keystrokes from a
/// lower process's Raw Input, but still delivers a hotkey. Bare keys (no Ctrl/Shift/Alt) with
/// <see cref="Modifiers"/> = MOD_NOREPEAT: holding a key sends one WM_HOTKEY, so a held key casts once.
/// </summary>
internal static class SkillHotkeys
{
    /// <summary>MOD_NOREPEAT and no modifier key.</summary>
    public const uint Modifiers = 0x4000;

    /// <summary>Ids from here on: two per slot (a numpad key also by its NumLock-on virtual key), far from the overlay's 1-7.</summary>
    public const int FirstId = 0x5E00;

    private const int IdsPerSlot = 2;

    /// <summary>
    /// The hotkeys for <paramref name="keys"/> (stored codes, slots 1-4 in order), each code turned into its virtual
    /// key by <paramref name="virtualKey"/> (0: none). A numpad digit's scan code maps to its NumLock-off key (End for
    /// numpad 1), so its NumLock-on key (VK_NUMPAD1) is held too: both cast the slot.
    /// </summary>
    public static IReadOnlyList<SkillHotkey> For(IReadOnlyList<int> keys, Func<int, uint> virtualKey)
    {
        var hotkeys = new List<SkillHotkey>(4);
        for (int slot = 1; slot <= keys.Count && slot <= 4; slot++)
        {
            int code = keys[slot - 1], id = FirstId + (slot - 1) * IdsPerSlot;
            uint vk = virtualKey(code);
            if (vk != 0) hotkeys.Add(new SkillHotkey(id, slot, vk));
            if (NumLockOnKey(code) is uint numpad && numpad != vk) hotkeys.Add(new SkillHotkey(id + 1, slot, numpad));
        }
        return hotkeys;
    }

    /// <summary>The slot a WM_HOTKEY id casts; null for an id that isn't one of these.</summary>
    public static int? SlotOf(int id) =>
        id >= FirstId && id < FirstId + 4 * IdsPerSlot ? (id - FirstId) / IdsPerSlot + 1 : null;

    /// <summary>A numpad key's virtual key with NumLock on (VK_NUMPAD0-9, VK_DECIMAL); null for any other key.</summary>
    public static uint? NumLockOnKey(int code) => code switch
    {
        0x47 => 0x67, 0x48 => 0x68, 0x49 => 0x69,
        0x4B => 0x64, 0x4C => 0x65, 0x4D => 0x66,
        0x4F => 0x61, 0x50 => 0x62, 0x51 => 0x63,
        0x52 => 0x60, 0x53 => 0x6E,
        _ => null,
    };
}

/// <summary>A bound key the reader could not hold while reading: the slot, and Windows' error (1409: another program has it).</summary>
internal readonly record struct UnreadKey(int Slot, int Error)
{
    public const int HeldElsewhere = 1409; // ERROR_HOTKEY_ALREADY_REGISTERED

    /// <summary>Another program already holds the key.</summary>
    public bool TakenByAnotherProgram => Error == HeldElsewhere;
}

/// <summary>What pressing a key did while the player was choosing a slot's key.</summary>
internal enum CaptureResult
{
    /// <summary>The key is now the slot's.</summary>
    Saved,

    /// <summary>Esc: the slot keeps its key.</summary>
    Cancelled,

    /// <summary>Another slot has that key: refused; still choosing.</summary>
    Taken,

    /// <summary>A message with no physical key (scan code 0, e.g. an injected one): still choosing.</summary>
    Ignored,
}

/// <summary>The menu's "press the key for slot N" (OverlayWindow.SkillInput), as a pure rule.</summary>
internal static class SkillKeyCapture
{
    private const int Esc = 0x01;

    /// <summary>The player pressed <paramref name="code"/> while choosing slot <paramref name="slot"/>'s key; <paramref name="keys"/> is changed when saved.</summary>
    public static (CaptureResult Result, int TakenBy) Capture(int[] keys, int slot, int code)
    {
        if (code == Esc) return (CaptureResult.Cancelled, 0);  // Esc keeps the old key
        if ((code & 0xFF) == 0) return (CaptureResult.Ignored, 0);
        int other = Array.IndexOf(keys, code);
        if (other >= 0 && other != slot - 1) return (CaptureResult.Taken, other + 1);
        keys[slot - 1] = code;
        return (CaptureResult.Saved, 0);
    }
}
