using System.Runtime.InteropServices;
using EndHud.Vision.Input;
using static EndHud.SkillInput.SkillInputNative;

namespace EndHud.SkillInput;

/// <summary>
/// Reads the player's battle-skill presses while they have turned it on (<see cref="HudConfig.ReadSkillPresses"/>),
/// and only then: "slot N, at time T" (<see cref="SkillPress"/>), nothing else. The game runs as
/// administrator and EndHud doesn't, and Windows' privilege isolation (UIPI) keeps an elevated window's
/// input from a lower process's Raw Input: so nothing here is Raw Input.
/// <list type="bullet">
/// <item>The keyboard: the four bound keys as hotkeys (<see cref="SkillHotkeys"/>: bare, MOD_NOREPEAT, so a
/// held key casts once) on a message-only window, on a thread of its own. Windows delivers a hotkey
/// whatever window is in front, and tells EndHud only of these four keys, never of any other. They are
/// held only while reading, which is only while the game is in front, so the digits type as usual in
/// every other program. A low-level keyboard hook (WH_KEYBOARD_LL) sits in every keystroke's path and is
/// never used.</item>
/// <item>PlayStation pads read straight from the device (<see cref="SonyPads"/>: shared, overlapped reads);
/// Xbox pads through XInput (polled every 10 ms), whose button bits are fixed and documented. Other pads
/// are counted, never read.</item>
/// <item>Every button goes straight into <see cref="SkillInputDecoder"/>, which drops anything that isn't
/// one of the four chords; each report is cleared once parsed.</item>
/// <item>Off: every hotkey unregistered, every pad closed, the window destroyed, the thread ended, the
/// queue and the decoder dropped.</item>
/// <item>Logged: "listening", "stopped", and Windows refusing (a slot whose key another program holds, by
/// its slot number). Never a key, a button, a device's name or a press's time.</item>
/// </list>
/// </summary>
internal sealed class SkillInputReader : ISkillPressSource, IDisposable
{
    private const int MaxQueued = 32;
    private const uint XInputPollMs = 10;
    /// <summary>An XInput slot without a pad, and the HID devices, are looked at again only every second: both are slow to ask.</summary>
    private const int EverySecond = 100;
    private const string ClassName = "EndHud.SkillInput";

    private static readonly WndProc Proc = (hwnd, msg, wParam, lParam) => DefWindowProcW(hwnd, msg, wParam, lParam);
    private static readonly object ClassLock = new();
    private static bool _classRegistered;

    private readonly bool _readWindows;
    private readonly object _queueLock = new();
    private readonly Queue<SkillPress> _queue = new();
    private readonly ManualResetEventSlim _started = new();

    private volatile SkillInputDecoder? _decoder;
    private volatile int[]? _keys;
    private volatile IReadOnlyList<HeldHotkey> _held = [];
    private volatile IReadOnlyList<UnreadKey> _unread = [];
    private Thread? _thread;
    private volatile bool _stopRequested;
    private IntPtr _window;
    private volatile int _error;

    // The reader thread's own, reset at each start.
    private SonyPads? _sony;
    private readonly bool[] _xinputConnected = new bool[4];
    private bool _xinputMissing;
    private long _ticks;

    // For Describe and tests: counts only.
    private volatile int _hidSeen, _hidDescribed, _sonyPads, _sonyUnopened, _xinputPads, _otherPads, _sonyReportsWithoutButtons;

    /// <summary>A hotkey as it was registered with Windows: the id, the modifiers passed and the virtual key.</summary>
    internal readonly record struct HeldHotkey(int Id, uint Modifiers, uint VirtualKey);

    /// <summary>The reader for the app: reads Windows' input while listening.</summary>
    public SkillInputReader() : this(readWindows: true) { }

    /// <param name="readWindows">False for tests: nothing is registered with Windows; presses come only through <see cref="Hotkey"/>.</param>
    internal SkillInputReader(bool readWindows) => _readWindows = readWindows;

    /// <summary>Windows refused to deliver input (the reader's window could not be made); see <see cref="Describe"/>.</summary>
    public bool Failed => _error != 0;

    /// <summary>The bound keys the last start could not hold (another program has them): kept until the next start, so the menu can say so.</summary>
    public IReadOnlyList<UnreadKey> UnreadKeys => _unread;

    /// <summary>The hotkeys held now, as registered (tests).</summary>
    internal IReadOnlyList<HeldHotkey> HeldHotkeys => _held;

    /// <summary>HID devices of any kind seen at the last look for pads, while listening (tests).</summary>
    internal int HidDevicesSeen => _hidSeen;

    /// <summary>HID devices whose vendor and usage Windows told, at the last look (tests: the asking works).</summary>
    internal int HidDevicesDescribed => _hidDescribed;

    /// <summary>PlayStation pads open for reading now (tests: none after a stop).</summary>
    internal int SonyPadsOpen => _sonyPads;

    /// <summary>The message-only window input is delivered to, while listening (tests).</summary>
    internal IntPtr Window => _window;

    /// <summary>The reading thread of the last start has ended (or there never was one).</summary>
    internal bool Ended => _thread is not { IsAlive: true };

    /// <summary>Wait for the reading thread to have registered (or failed to).</summary>
    internal bool WaitStarted(TimeSpan timeout) => _started.Wait(timeout);

    /// <summary>
    /// Listen (with these four keys) or stop. Called on every refresh: does nothing unless something
    /// changed. Keys changed while listening restart with the new ones.
    /// </summary>
    public void SetListening(bool on, IReadOnlyList<int> keys)
    {
        if (on)
        {
            if (_keys is not null && _keys.SequenceEqual(keys)) return;
            if (_keys is not null) Stop();
            Start(keys);
        }
        else if (_keys is not null)
        {
            Stop();
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<SkillPress> TakeUntil(TimeSpan readAt)
    {
        var taken = new List<SkillPress>();
        lock (_queueLock)
        {
            while (_queue.TryPeek(out var press) && press.At <= readAt) taken.Add(_queue.Dequeue());
        }
        return taken;
    }

    /// <summary>For the diagnostics: whether it reads, how many keys it holds and how many pads of each kind; counts only.</summary>
    public string Describe() =>
        _keys is null ? "off"
        : Failed ? $"failed (Windows refused, error {_error})"
        : $"listening: keyboard ({_held.Select(h => SkillHotkeys.SlotOf(h.Id)).Distinct().Count()} of 4 keys held), " +
          $"PlayStation pads {_sonyPads} (not opened {_sonyUnopened}), XInput pads {_xinputPads}, other pads {_otherPads}; " +
          $"PlayStation reports without buttons {_sonyReportsWithoutButtons}";

    /// <summary>A WM_HOTKEY, by its id: a press of the slot it stands for; nothing for any other id or while not listening.</summary>
    internal void Hotkey(int id)
    {
        if (_keys is not null && SkillHotkeys.SlotOf(id) is int slot) Enqueue(slot);
    }

    public void Dispose() => SetListening(false, []);

    private void Start(IReadOnlyList<int> keys)
    {
        _keys = [.. keys];
        _decoder = new SkillInputDecoder();
        _error = 0;
        _unread = [];
        _stopRequested = false;
        _started.Reset();
        DiagnosticLog.Info("skill input: listening");
        if (!_readWindows)
        {
            _started.Set();
            return;
        }
        _thread = new Thread(Run) { IsBackground = true, Name = "EndHud skill input" };
        _thread.Start();
    }

    private void Stop()
    {
        if (_thread is { } thread)
        {
            _stopRequested = true;
            _started.Wait(TimeSpan.FromSeconds(1));
            if (_window != IntPtr.Zero) PostMessageW(_window, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            if (!thread.Join(TimeSpan.FromSeconds(1))) DiagnosticLog.Warn("skill input: the reader did not stop within a second");
        }
        _decoder = null;
        _keys = null;
        // A refusal stays known (Failed, UnreadKeys) until the next start: the menu, which stops reading, still says so.
        lock (_queueLock) _queue.Clear();
        DiagnosticLog.Info("skill input: stopped");
    }

    private void Enqueue(int slot)
    {
        var press = new SkillPress(slot, InnClock.Now);
        lock (_queueLock)
        {
            _queue.Enqueue(press);
            while (_queue.Count > MaxQueued) _queue.Dequeue();
        }
    }

    private void Run()
    {
        IntPtr window = IntPtr.Zero;
        try
        {
            IntPtr instance = GetModuleHandleW(null);
            RegisterClass(instance);
            window = CreateWindowExW(0, ClassName, null, 0, 0, 0, 0, 0, HWND_MESSAGE, IntPtr.Zero, instance, IntPtr.Zero);
            if (window == IntPtr.Zero)
            {
                Refused(Marshal.GetLastPInvokeError());
                return;
            }
            HoldKeys(window);
            _sony = new SonyPads(PadButtonsNow, device => _decoder?.Forget(device));
            LookForPads();
            SetTimer(window, 1, XInputPollMs, IntPtr.Zero);
            _window = window;
            _started.Set();
            if (_stopRequested) return;
            Loop();
        }
        catch (Exception ex)
        {
            DiagnosticLog.Error("skill input: the reader stopped on an error", ex);
        }
        finally
        {
            // The hotkeys also go with the window when this thread ends (measured: without LetKeysGo another
            // program can still take the keys at once after a stop); unregistering them by name is the
            // documented way, kept explicit. The pads' reads are cancelled and waited for before their memory goes.
            LetKeysGo(window);
            _sony?.Dispose();
            _sony = null;
            if (window != IntPtr.Zero)
            {
                KillTimer(window, 1);
                DestroyWindow(window);
            }
            _window = IntPtr.Zero;
            ForgetDevices();
            _started.Set();
        }
    }

    /// <summary>Waits for a message (a hotkey, the poll timer, the stop) or a pad's report, whichever comes first.</summary>
    private void Loop()
    {
        while (true)
        {
            var events = _sony!.Events;
            uint woke = MsgWaitForMultipleObjectsEx((uint)events.Length, events, INFINITE, QS_ALLINPUT, MWMO_INPUTAVAILABLE);
            if (woke == WAIT_FAILED)
            {
                DiagnosticLog.Error($"skill input: waiting for input failed (error {Marshal.GetLastPInvokeError()})");
                return;
            }
            if (woke < events.Length)
            {
                _sony.Complete((int)woke);
                CountPads();
                continue;
            }
            while (PeekMessageW(out var msg, IntPtr.Zero, 0, 0, PM_REMOVE))
            {
                if (msg.message == WM_CLOSE) return;
                Handle(msg);
                DispatchMessageW(ref msg);
            }
        }
    }

    /// <summary>
    /// Registers the bound keys' hotkeys on the reader's window (only the window's own thread may). A key
    /// Windows refuses (another program holds it) is logged by its slot, never by the key, and left out:
    /// the other slots are still read.
    /// </summary>
    private void HoldKeys(IntPtr window)
    {
        var held = new List<HeldHotkey>(8);
        var unread = new List<UnreadKey>();
        var hotkeys = SkillHotkeys.For(_keys!, VirtualKey);
        for (int slot = 1; slot <= 4; slot++)
        {
            int error = -1; // no virtual key on this layout, unless Windows says otherwise
            bool any = false;
            foreach (var hotkey in hotkeys.Where(h => h.Slot == slot))
            {
                if (RegisterHotKey(window, hotkey.Id, SkillHotkeys.Modifiers, hotkey.VirtualKey))
                {
                    held.Add(new HeldHotkey(hotkey.Id, SkillHotkeys.Modifiers, hotkey.VirtualKey));
                    any = true;
                }
                else
                {
                    error = Marshal.GetLastPInvokeError();
                }
            }
            if (any) continue;
            var key = new UnreadKey(slot, error == 0 ? -1 : error);
            unread.Add(key);
            DiagnosticLog.Warn(key.TakenByAnotherProgram
                ? $"skill input: another program holds slot {slot}'s key, so slot {slot} is not read"
                : $"skill input: Windows refused slot {slot}'s key (error {key.Error}), so slot {slot} is not read");
        }
        _held = held;
        _unread = unread;
    }

    private void LetKeysGo(IntPtr window)
    {
        foreach (var hotkey in _held) UnregisterHotKey(window, hotkey.Id);
        _held = [];
    }

    private void Refused(int error)
    {
        _error = error == 0 ? -1 : error;
        DiagnosticLog.Error($"skill input: Windows refused to read input (error {error})");
    }

    private static void RegisterClass(IntPtr instance)
    {
        lock (ClassLock)
        {
            if (_classRegistered) return;
            var wc = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(Proc),
                hInstance = instance,
                lpszClassName = ClassName,
            };
            if (RegisterClassExW(ref wc) == 0 && Marshal.GetLastPInvokeError() != 1410) // ERROR_CLASS_ALREADY_EXISTS
                throw new InvalidOperationException($"the input window's class could not be registered (error {Marshal.GetLastPInvokeError()})");
            _classRegistered = true;
        }
    }

    private void Handle(MSG msg)
    {
        switch (msg.message)
        {
            case WM_HOTKEY:
                Hotkey((int)msg.wParam);
                break;
            case WM_TIMER:
                bool everySecond = _ticks++ % EverySecond == 0;
                PollXInput(everySecond);
                if (everySecond && _ticks > 1) LookForPads();
                break;
        }
    }

    private void PadButtonsNow(nint device, PadButtons buttons)
    {
        foreach (int slot in _decoder?.Pad(device, buttons) ?? []) Enqueue(slot);
    }

    private void LookForPads()
    {
        _sony!.Rescan();
        CountPads();
    }

    private void CountPads()
    {
        _hidSeen = _sony!.Seen;
        _hidDescribed = _sony.Described;
        _sonyPads = _sony.Open;
        _sonyUnopened = _sony.Unopened;
        _otherPads = _sony.Other;
        _sonyReportsWithoutButtons = _sony.ReportsWithoutButtons;
    }

    private void ForgetDevices()
    {
        Array.Clear(_xinputConnected);
        _xinputMissing = false;
        _ticks = 0;
        _hidSeen = _hidDescribed = _sonyPads = _sonyUnopened = _xinputPads = _otherPads = _sonyReportsWithoutButtons = 0;
    }

    private void PollXInput(bool everyone)
    {
        if (_xinputMissing) return;
        try
        {
            for (uint user = 0; user < 4; user++)
            {
                if (!_xinputConnected[user] && !everyone) continue;
                bool connected = XInputGetState(user, out var state) == ERROR_SUCCESS;
                IntPtr device = -1 - (int)user; // never a PlayStation pad's id (those count up from 1)
                if (!connected && _xinputConnected[user]) _decoder?.Forget(device);
                _xinputConnected[user] = connected;
                if (!connected) continue;
                foreach (int slot in _decoder?.Pad(device, SkillInputDecoder.FromXInput(state.wButtons)) ?? []) Enqueue(slot);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            _xinputMissing = true;
            DiagnosticLog.Warn("skill input: XInput is not available, Xbox pads are not read");
        }
        _xinputPads = _xinputConnected.Count(c => c);
    }
}
