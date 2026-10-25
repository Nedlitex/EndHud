using System.Runtime.InteropServices;
using EndHud.SkillInput;
using EndHud.Vision.Input;
using Xunit;

namespace EndHud.Tests;

/// <summary>
/// <see cref="SkillInputReader"/>'s queue of presses: taken up to a reading's time, bounded, emptied
/// when reading stops. No Windows input is involved (<c>readWindows: false</c>): a press is the
/// WM_HOTKEY id Windows would deliver for the slot's key.
/// </summary>
[Collection(WpfCollection.Name)] // logs "listening"/"stopped" into the process's one log
public class SkillInputReaderQueueTests
{
    /// <summary>The id of a slot's key, as WM_HOTKEY carries it.</summary>
    internal static int IdOf(int slot) => SkillHotkeys.For(SkillKeyCodes.Defaults, _ => 0x31).Single(h => h.Slot == slot).Id;

    private static void Press(SkillInputReader reader, int slot) => reader.Hotkey(IdOf(slot));

    [Fact]
    public void Presses_are_taken_up_to_the_reading_time_oldest_first()
    {
        using var reader = new SkillInputReader(readWindows: false);
        reader.SetListening(true, SkillKeyCodes.Defaults);
        Press(reader, 2);
        Press(reader, 4);
        var between = InnClock.Now;
        Thread.Sleep(5);
        Press(reader, 1);

        Assert.Equal([2, 4], reader.TakeUntil(between).Select(p => p.Slot));
        Assert.Empty(reader.TakeUntil(between));
        Assert.Equal([1], reader.TakeUntil(InnClock.Now).Select(p => p.Slot));
    }

    [Fact]
    public void Nothing_is_read_before_listening_starts()
    {
        using var reader = new SkillInputReader(readWindows: false);
        Press(reader, 1);

        Assert.Empty(reader.TakeUntil(TimeSpan.MaxValue));
        Assert.Equal("off", reader.Describe());
    }

    [Fact]
    public void At_most_32_presses_wait_and_the_oldest_go_first()
    {
        using var reader = new SkillInputReader(readWindows: false);
        reader.SetListening(true, SkillKeyCodes.Defaults);
        for (int i = 0; i < 40; i++) Press(reader, i % 4 + 1);

        var taken = reader.TakeUntil(TimeSpan.MaxValue);
        Assert.Equal(32, taken.Count);
        // Presses 9 to 40 kept, in order: press 9 is slot 1, press 40 slot 4.
        Assert.Equal(Enumerable.Range(8, 32).Select(i => i % 4 + 1), taken.Select(p => p.Slot));
        Assert.True(taken.Zip(taken.Skip(1)).All(pair => pair.First.At <= pair.Second.At));
    }

    [Fact]
    public void Stopping_forgets_the_presses()
    {
        using var reader = new SkillInputReader(readWindows: false);
        reader.SetListening(true, SkillKeyCodes.Defaults);
        Press(reader, 1);
        reader.SetListening(false, SkillKeyCodes.Defaults);
        Press(reader, 2); // after the stop: nothing
        reader.SetListening(true, SkillKeyCodes.Defaults);

        Assert.Empty(reader.TakeUntil(TimeSpan.MaxValue));
        Press(reader, 1);
        Assert.Equal([1], reader.TakeUntil(TimeSpan.MaxValue).Select(p => p.Slot));
    }

    [Fact]
    public void A_hotkey_that_is_not_a_slots_is_no_press()
    {
        using var reader = new SkillInputReader(readWindows: false);
        reader.SetListening(true, SkillKeyCodes.Defaults);
        foreach (int id in new[] { 0, 1, 7, SkillHotkeys.FirstId - 1, SkillHotkeys.FirstId + 8, int.MaxValue }) reader.Hotkey(id);

        Assert.Empty(reader.TakeUntil(TimeSpan.MaxValue));
    }

    [Fact]
    public void The_reader_is_a_press_source() => Assert.IsAssignableFrom<ISkillPressSource>(new SkillInputReader(readWindows: false));
}

/// <summary>
/// The four bound keys as hotkeys (<see cref="SkillHotkeys"/>): one per slot, by the key's virtual key
/// on the player's layout; a numpad digit by both of its keys. No Windows registration involved.
/// </summary>
public class SkillHotkeysTests
{
    private const uint NoRepeat = 0x4000; // MOD_NOREPEAT

    [Fact]
    public void Keys_1_to_4_are_held_as_bare_1_to_4()
    {
        var hotkeys = SkillHotkeys.For(SkillKeyCodes.Defaults, SkillInputNative.VirtualKey);

        Assert.Equal([(1, 0x31u), (2, 0x32u), (3, 0x33u), (4, 0x34u)], hotkeys.Select(h => (h.Slot, h.VirtualKey)));
        Assert.Equal([1, 2, 3, 4], hotkeys.Select(h => SkillHotkeys.SlotOf(h.Id)));
    }

    [Fact]
    public void A_held_key_counts_once_and_no_modifier_is_needed() => Assert.Equal(NoRepeat, SkillHotkeys.Modifiers);

    [Fact]
    public void A_rebound_key_is_held_for_its_slot_and_the_old_key_is_not()
    {
        var hotkeys = SkillHotkeys.For([0x10, 0x03, 0x04, 0x05], SkillInputNative.VirtualKey); // slot 1 on Q

        Assert.Equal(0x51u, hotkeys.Single(h => h.Slot == 1).VirtualKey);
        Assert.DoesNotContain(hotkeys, h => h.VirtualKey == 0x31);
    }

    [Fact]
    public void A_numpad_digit_is_held_with_numlock_on_and_off()
    {
        var hotkeys = SkillHotkeys.For([0x4F, 0x03, 0x04, 0x05], SkillInputNative.VirtualKey); // slot 1 on numpad 1

        Assert.Equal([0x23u, 0x61u], hotkeys.Where(h => h.Slot == 1).Select(h => h.VirtualKey).Order()); // VK_END, VK_NUMPAD1
        Assert.All(hotkeys.Where(h => h.Slot == 1), h => Assert.Equal(1, SkillHotkeys.SlotOf(h.Id)));
    }

    [Fact]
    public void An_extended_key_is_held_by_its_own_virtual_key()
    {
        var hotkeys = SkillHotkeys.For([0x02, 0x03, 0x04, 0x48 | SkillKeyCodes.Extended], SkillInputNative.VirtualKey); // slot 4 on the up arrow

        Assert.Equal([0x26u], hotkeys.Where(h => h.Slot == 4).Select(h => h.VirtualKey)); // VK_UP, not numpad 8
    }

    [Fact]
    public void A_key_with_no_virtual_key_is_left_out() =>
        Assert.Equal([2, 3, 4], SkillHotkeys.For(SkillKeyCodes.Defaults, code => code == 0x02 ? 0u : 0x40u).Select(h => h.Slot));

    [Fact]
    public void The_ids_never_meet_the_overlays_own_hotkeys()
    {
        var ids = SkillHotkeys.For([0x4F, 0x50, 0x51, 0x4B], SkillInputNative.VirtualKey).Select(h => h.Id).ToList(); // numpad 1, 2, 3, 4: two each

        Assert.Equal(8, ids.Distinct().Count());
        Assert.All(ids, id => Assert.True(id > 7));
        Assert.Null(SkillHotkeys.SlotOf(7));
    }
}

/// <summary>
/// The real reader on the desktop. The game runs as administrator and EndHud doesn't, so Windows keeps
/// the game's keystrokes from EndHud's Raw Input (UIPI); a hotkey still arrives. So the four bound keys
/// are hotkeys, held only while reading, which is only while the game is in front. The test proves who
/// holds a key by trying to register it itself: that fails while the reader holds it and succeeds once
/// the reader has let it go. No input is injected: nothing is typed into any program.
/// <para>
/// A hotkey belongs to the whole desktop session, not to one process: while a running EndHud reads the
/// player's 1-4 (the game in front), or a second test run's reader holds them, nobody else can take
/// them, and a test on 1-4 sees them held before its own reader starts. So the reader is given keys
/// from F13-F24 instead, which no program here binds, and only ones nobody holds when the test starts;
/// test runs side by side take turns (<see cref="KeyTurn"/>). What is proven is the same for any four
/// keys (the reader registers whatever it is given); that the player's defaults are the bare digits
/// 1-4 is proven by <see cref="SkillHotkeysTests"/>, with no Windows registration. It also keeps the
/// tests from taking the real digits away from the game the owner may be playing.
/// </para>
/// </summary>
[Trait("Category", "Desktop")] // registers with Windows' input: runs locally, not on CI runners
[Collection(WpfCollection.Name)]
public sealed class SkillInputReaderTests : IDisposable
{
    /// <summary>F13-F24 as stored key codes (set-1 scan codes).</summary>
    private static readonly int[] SpareKeyCodes = [0x64, 0x65, 0x66, 0x67, 0x68, 0x69, 0x6A, 0x6B, 0x6C, 0x6D, 0x6E, 0x76];

    private static readonly bool[] AllFree = [true, true, true, true], AllHeld = [false, false, false, false];

    private const int TestId = 0x7A00;
    private const uint NoRepeat = 0x4000; // MOD_NOREPEAT

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    /// <summary>One of these tests at a time across every test run on this desktop (worktrees run side by side).</summary>
    private static readonly Semaphore KeyTurn = new(1, 1, @"Local\EndHud.Tests.SkillInputReader.Keys");

    private readonly int[] _keys;   // the four keys the reader is given (stored codes), slots 1-4
    private readonly uint[] _vks;   // their virtual keys
    private readonly int _spareKey; // a fifth free key, for rebinding slot 1
    private readonly uint _spareVk;

    public SkillInputReaderTests()
    {
        Assert.True(KeyTurn.WaitOne(TimeSpan.FromMinutes(2)), "another test run kept the skill-key tests' turn for 2 minutes");
        try
        {
            var free = SpareKeyCodes.Select(code => (Code: code, Vk: SkillInputNative.VirtualKey(code)))
                .Where(k => k.Vk != 0 && Free([k.Vk])[0]).ToList();
            Assert.True(free.Count >= 5, $"only {free.Count} of F13-F24 are free on this desktop (5 needed): another program holds the rest");
            _keys = [.. free.Take(4).Select(k => k.Code)];
            _vks = [.. free.Take(4).Select(k => k.Vk)];
            (_spareKey, _spareVk) = free[4];
        }
        catch
        {
            KeyTurn.Release();
            throw;
        }
    }

    public void Dispose() => KeyTurn.Release();

    /// <summary>For each key, whether this test could take it as a bare hotkey (given straight back).</summary>
    internal static bool[] Free(IReadOnlyList<uint> keys) => [.. keys.Select((vk, i) =>
    {
        bool free = RegisterHotKey(IntPtr.Zero, TestId + i, 0, vk);
        if (free) UnregisterHotKey(IntPtr.Zero, TestId + i);
        return free;
    })];

    private void Listen(SkillInputReader reader, bool on)
    {
        reader.SetListening(on, _keys);
        if (on) Assert.True(reader.WaitStarted(TimeSpan.FromSeconds(5)), "the reader's thread never said it started");
    }

    [Fact]
    public void Turning_reading_on_holds_the_four_keys_and_off_lets_them_go()
    {
        Assert.Equal(AllFree, Free(_vks)); // nobody holds them before
        using var reader = new SkillInputReader();

        Listen(reader, true);
        Assert.False(reader.Failed, reader.Describe());
        Assert.Equal(AllHeld, Free(_vks));
        Assert.Equal(_vks.Order(), reader.HeldHotkeys.Select(h => h.VirtualKey).Order());
        Assert.All(reader.HeldHotkeys, h => Assert.Equal(NoRepeat, h.Modifiers)); // bare key; held, it counts once
        Assert.Empty(reader.UnreadKeys);
        Assert.StartsWith("listening: keyboard (4 of 4 keys held)", reader.Describe());

        Listen(reader, false);

        Assert.Equal(AllFree, Free(_vks));
        Assert.Empty(reader.HeldHotkeys);
        Assert.True(reader.Ended);
        Assert.Equal("off", reader.Describe());
    }

    [Fact]
    public void The_keys_are_held_only_while_reading_is_on_and_the_game_is_in_front()
    {
        var config = new HudConfig { ReadSkillPresses = true, RotationEnabled = true, PartyOcrEnabled = true };
        using var reader = new SkillInputReader();

        Listen(reader, OverlayWindow.SkillInputListens(config, gameInFront: false));
        Assert.Equal(AllFree, Free(_vks)); // the game isn't in front: the keys are everyone's

        Listen(reader, OverlayWindow.SkillInputListens(config, gameInFront: true));
        Assert.Equal(AllHeld, Free(_vks));

        Listen(reader, OverlayWindow.SkillInputListens(config, gameInFront: false)); // the game left the front
        Assert.Equal(AllFree, Free(_vks));

        config.ReadSkillPresses = false;
        Listen(reader, OverlayWindow.SkillInputListens(config, gameInFront: true)); // the player turned it off
        Assert.Equal(AllFree, Free(_vks));
    }

    [Fact]
    public void A_key_another_program_holds_is_named_by_slot_and_the_others_are_read()
    {
        // The test is the other program: it takes slot 3's key first (free: picked so, in this test's turn).
        Assert.True(RegisterHotKey(IntPtr.Zero, TestId + 9, 0, _vks[2]), $"the test could not take slot 3's key first (error {Marshal.GetLastPInvokeError()})");
        try
        {
            using var reader = new SkillInputReader();
            Listen(reader, true);

            Assert.Equal([new UnreadKey(3, UnreadKey.HeldElsewhere)], reader.UnreadKeys);
            Assert.True(reader.UnreadKeys[0].TakenByAnotherProgram);
            Assert.Equal(new[] { _vks[0], _vks[1], _vks[3] }.Order(), reader.HeldHotkeys.Select(h => h.VirtualKey).Order());
            Assert.False(reader.Failed);
            Assert.StartsWith("listening: keyboard (3 of 4 keys held)", reader.Describe());

            Listen(reader, false);
            Assert.Equal([new UnreadKey(3, UnreadKey.HeldElsewhere)], reader.UnreadKeys); // the menu, which stops reading, still says so
        }
        finally
        {
            UnregisterHotKey(IntPtr.Zero, TestId + 9);
        }
    }

    [Fact]
    public void New_keys_while_reading_let_the_old_key_go_and_hold_the_new_one()
    {
        using var reader = new SkillInputReader();
        Listen(reader, true);

        reader.SetListening(true, [_spareKey, _keys[1], _keys[2], _keys[3]]); // slot 1 on another key
        Assert.True(reader.WaitStarted(TimeSpan.FromSeconds(5)));

        Assert.Equal([true, false, false, false], Free(_vks));
        Assert.Equal([false], Free([_spareVk]));
        Listen(reader, false);
        Assert.Equal([true], Free([_spareVk]));
    }

    [Fact]
    public void Pads_are_looked_for_while_reading_and_let_go_after()
    {
        using var reader = new SkillInputReader();
        Listen(reader, true);

        // Every Windows machine has HID devices (keyboard, mouse...): seeing none would mean the look itself is broken.
        Assert.True(reader.HidDevicesSeen > 0, reader.Describe());
        Assert.True(reader.HidDevicesDescribed > 0, $"no HID device's vendor and usage could be read, of {reader.HidDevicesSeen}");
        Assert.Matches(@"PlayStation pads \d+ \(not opened \d+\), XInput pads \d+, other pads \d+", reader.Describe());

        Listen(reader, false);

        Assert.Equal(0, reader.SonyPadsOpen);
        Assert.Equal(0, reader.HidDevicesSeen);
    }

    [Fact]
    public void Disposing_the_reader_lets_the_keys_go()
    {
        var reader = new SkillInputReader();
        Listen(reader, true);
        Assert.Equal(AllHeld, Free(_vks));

        reader.Dispose();

        Assert.Equal(AllFree, Free(_vks));
        Assert.True(reader.Ended);
    }
}
