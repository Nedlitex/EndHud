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
