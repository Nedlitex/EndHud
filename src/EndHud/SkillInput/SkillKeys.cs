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
