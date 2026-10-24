using System.Runtime.InteropServices;
using EndHud.SkillInput;
using EndHud.Vision.Input;
using Xunit;
using static EndHud.SkillInput.SkillInputNative;

namespace EndHud.Tests;

/// <summary>
/// <see cref="SkillInputReader"/>'s queue of presses: taken up to a reading's time, bounded, emptied
/// when reading stops. No Windows input is involved (<c>readWindows: false</c>).
/// </summary>
[Collection(WpfCollection.Name)] // logs "listening"/"stopped" into the process's one log
public class SkillInputReaderQueueTests
{
    private const ushort Break = 0x01;

    private static void Press(SkillInputReader reader, int slot)
    {
        ushort code = (ushort)SkillKeyCodes.Defaults[slot - 1];
        reader.Feed(new RawKeyboard(code, 0));
        reader.Feed(new RawKeyboard(code, Break));
    }

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
    public void Stopping_forgets_the_presses_and_the_keys_held()
    {
        using var reader = new SkillInputReader(readWindows: false);
        reader.SetListening(true, SkillKeyCodes.Defaults);
        reader.Feed(new RawKeyboard(0x02, 0)); // held through the stop
        reader.SetListening(false, SkillKeyCodes.Defaults);
        reader.SetListening(true, SkillKeyCodes.Defaults);

        Assert.Empty(reader.TakeUntil(TimeSpan.MaxValue));
        reader.Feed(new RawKeyboard(0x02, 0));
        Assert.Equal([1], reader.TakeUntil(TimeSpan.MaxValue).Select(p => p.Slot));
    }

    [Fact]
    public void New_keys_take_effect_while_listening()
    {
        using var reader = new SkillInputReader(readWindows: false);
        reader.SetListening(true, SkillKeyCodes.Defaults);
        reader.SetListening(true, [0x10, 0x03, 0x04, 0x05]);
        reader.Feed(new RawKeyboard(0x02, 0));
        reader.Feed(new RawKeyboard(0x10, 0));

        Assert.Equal([1], reader.TakeUntil(TimeSpan.MaxValue).Select(p => p.Slot));
    }

    [Fact]
    public void The_reader_is_a_press_source() => Assert.IsAssignableFrom<ISkillPressSource>(new SkillInputReader(readWindows: false));
}

/// <summary>
/// The real reader on the desktop. The game runs as administrator and EndHud doesn't, so Windows keeps
/// the game's keystrokes from EndHud's Raw Input (UIPI); a hotkey still arrives. So the four bound keys
/// are hotkeys, held only while reading. The test proves who holds a key by trying to register it
/// itself: that fails while EndHud holds it and succeeds once EndHud has let it go. No input is
/// injected: nothing is typed into any program.
/// </summary>
[Trait("Category", "Desktop")] // registers with Windows' input: runs locally, not on CI runners
[Collection(WpfCollection.Name)]
public class SkillInputReaderTests
{
    /// <summary>The default keys' virtual keys: the top-row 1 to 4.</summary>
    private static readonly uint[] DigitKeys = [0x31, 0x32, 0x33, 0x34];

    private static readonly bool[] AllFree = [true, true, true, true], AllHeld = [false, false, false, false];

    private const int TestId = 0x7A00;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    /// <summary>For each key, whether this test could take it as a bare hotkey (given straight back).</summary>
    internal static bool[] Free(IReadOnlyList<uint> keys) => [.. keys.Select((vk, i) =>
    {
        bool free = RegisterHotKey(IntPtr.Zero, TestId + i, 0, vk);
        if (free) UnregisterHotKey(IntPtr.Zero, TestId + i);
        return free;
    })];

    [Fact]
    public void Turning_reading_on_holds_the_four_keys_and_off_lets_them_go()
    {
        Assert.Equal(AllFree, Free(DigitKeys)); // nobody holds them before
        using var reader = new SkillInputReader();

        reader.SetListening(true, SkillKeyCodes.Defaults);
        Assert.True(reader.WaitStarted(TimeSpan.FromSeconds(5)), "the reader's thread never said it started");
        Assert.False(reader.Failed, reader.Describe());
        Assert.Equal(AllHeld, Free(DigitKeys));
        Assert.StartsWith("listening: keyboard", reader.Describe());

        reader.SetListening(false, SkillKeyCodes.Defaults);

        Assert.Equal(AllFree, Free(DigitKeys));
        Assert.True(reader.Ended);
        Assert.Equal("off", reader.Describe());
    }

    [Fact]
    public void Disposing_the_reader_lets_the_keys_go()
    {
        var reader = new SkillInputReader();
        reader.SetListening(true, SkillKeyCodes.Defaults);
        Assert.True(reader.WaitStarted(TimeSpan.FromSeconds(5)));
        Assert.Equal(AllHeld, Free(DigitKeys));

        reader.Dispose();

        Assert.Equal(AllFree, Free(DigitKeys));
        Assert.True(reader.Ended);
    }
}
