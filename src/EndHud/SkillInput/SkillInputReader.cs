using System.Runtime.InteropServices;
using EndHud.Vision.Input;
using static EndHud.SkillInput.SkillInputNative;

namespace EndHud.SkillInput;

/// <summary>
/// Reads the player's battle-skill presses while they have turned it on (<see cref="HudConfig.ReadSkillPresses"/>),
/// and only then: "slot N, at time T" (<see cref="SkillPress"/>), nothing else.
/// <list type="bullet">
/// <item>Raw Input with RIDEV_INPUTSINK on a message-only window, on a thread of its own. Raw Input only
/// listens: no key is taken from the game or any other program. A hotkey (RegisterHotKey) on a bare 1-4
/// would grab those digits system-wide; a low-level keyboard hook (WH_KEYBOARD_LL) sits in every
/// keystroke's path and is never used.</item>
/// <item>Keyboards and PlayStation pads through Raw Input (the pad's HID report, parsed by Windows);
/// Xbox pads through XInput (polled every 10 ms), whose button bits are fixed and documented. An Xbox
/// pad's own Raw Input reports are ignored. Other pads are counted, never read.</item>
/// <item>Every key or button goes straight into <see cref="SkillInputDecoder"/>, which drops anything
/// that isn't one of the four battle-skill keys or chords; the buffers Windows filled are cleared after
/// each event.</item>
/// <item>Off: every registration removed (RIDEV_REMOVE), the window destroyed, the thread ended, the
/// queue and the decoder dropped.</item>
/// <item>Logged: "listening", "stopped", and Windows refusing. Never a key, a button, a device's name
/// or a press's time.</item>
/// </list>
/// </summary>
internal sealed class SkillInputReader : ISkillPressSource, IDisposable
{
    private const int MaxQueued = 32;
    private const uint XInputPollMs = 10;
    /// <summary>An XInput slot without a pad is asked again only every second: asking an empty slot is slow.</summary>
    private const int XInputEmptySlotEvery = 100;
    private const string ClassName = "EndHud.SkillInput";

    private static readonly WndProc Proc = (hwnd, msg, wParam, lParam) => DefWindowProcW(hwnd, msg, wParam, lParam);
    private static readonly object ClassLock = new();
    private static bool _classRegistered;

    private readonly bool _readWindows;
    private readonly object _queueLock = new();
    private readonly Queue<SkillPress> _queue = new();
    private readonly ManualResetEventSlim _started = new();

    private volatile SkillInputDecoder? _decoder;
    private int[]? _keys;
    private Thread? _thread;
    private volatile bool _stopRequested;
    private IntPtr _window;
    private volatile int _error;

    // The reader thread's own, reset at each start.
    private readonly Dictionary<IntPtr, Device> _devices = [];
    private readonly bool[] _xinputConnected = new bool[4];
    private bool _xinputMissing;
    private long _ticks;
    private byte[] _buffer = new byte[256];
    private byte[] _report = [];
    private readonly ushort[] _usages = new ushort[64];

    // For Describe: counts only.
    private volatile int _sonyPads, _xinputPads, _otherPads, _sonyReportsWithoutButtons;

    private enum PadKind { Sony, XInput, Other }

    private sealed record Device(PadKind Kind, IntPtr Preparsed);

    /// <summary>The reader for the app: reads Windows' input while listening.</summary>
    public SkillInputReader() : this(readWindows: true) { }

    /// <param name="readWindows">False for tests: nothing is registered with Windows; presses come only through <see cref="Feed"/>.</param>
    internal SkillInputReader(bool readWindows) => _readWindows = readWindows;

    /// <summary>Windows refused to deliver input (Raw Input registration failed); see <see cref="Describe"/>.</summary>
    public bool Failed => _error != 0;

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

    /// <summary>For the diagnostics: whether it reads, and how many pads of each kind; counts only.</summary>
    public string Describe() =>
        _keys is null ? "off"
        : Failed ? $"failed (Windows refused, error {_error})"
        : $"listening: keyboard, PlayStation pads {_sonyPads}, XInput pads {_xinputPads}, other pads {_otherPads}; " +
          $"PlayStation reports without buttons {_sonyReportsWithoutButtons}";

    /// <summary>A keyboard event, as WM_INPUT delivers it: into the decoder, and a press if it was a battle-skill key.</summary>
    internal void Feed(RawKeyboard key)
    {
        if (_decoder?.Key(key.MakeCode, key.Flags) is int slot) Enqueue(slot);
    }

    public void Dispose() => SetListening(false, []);

    private void Start(IReadOnlyList<int> keys)
    {
        _keys = [.. keys];
        _decoder = new SkillInputDecoder(_keys);
        _error = 0;
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
        // A refusal stays known (Failed) until the next start: the menu, which stops reading, still says so.
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

    private static RAWINPUTDEVICE[] Devices(uint flags, IntPtr target) =>
    [
        new() { usUsagePage = UsagePageGeneric, usUsage = UsageKeyboard, dwFlags = flags, hwndTarget = target },
        new() { usUsagePage = UsagePageGeneric, usUsage = UsageGamepad, dwFlags = flags, hwndTarget = target },
        new() { usUsagePage = UsagePageGeneric, usUsage = UsageJoystick, dwFlags = flags, hwndTarget = target },
    ];

    /// <summary>This process's Raw Input registrations (tests: none are left when reading is off).</summary>
    internal static RAWINPUTDEVICE[] Registered()
    {
        uint size = (uint)Marshal.SizeOf<RAWINPUTDEVICE>(), count = 0;
        GetRegisteredRawInputDevices(null, ref count, size);
        if (count == 0) return [];
        var devices = new RAWINPUTDEVICE[count];
        uint got = GetRegisteredRawInputDevices(devices, ref count, size);
        return got == uint.MaxValue ? [] : devices[..(int)got];
    }

    private void Run()
    {
        IntPtr window = IntPtr.Zero;
        bool registered = false;
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
            var devices = Devices(RIDEV_INPUTSINK | RIDEV_DEVNOTIFY, window);
            if (!RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RAWINPUTDEVICE>()))
            {
                Refused(Marshal.GetLastPInvokeError());
                return;
            }
            registered = true;
            SetTimer(window, 1, XInputPollMs, IntPtr.Zero);
            _window = window;
            _started.Set();
            if (_stopRequested) return;

            while (GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                if (msg.message == WM_CLOSE) break;
                Handle(msg);
                DispatchMessageW(ref msg);
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Error("skill input: the reader stopped on an error", ex);
        }
        finally
        {
            // The registrations also go with the window when this thread ends (measured: the desktop test
            // stays green without this call); removing them by name is the documented way, kept explicit.
            if (registered)
            {
                var remove = Devices(RIDEV_REMOVE, IntPtr.Zero);
                RegisterRawInputDevices(remove, (uint)remove.Length, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
            }
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
            case WM_INPUT:
                ReadInput(msg.lParam);
                break;
            case WM_TIMER:
                PollXInput();
                break;
            case WM_INPUT_DEVICE_CHANGE when (int)msg.wParam == GIDC_REMOVAL:
                _decoder?.Forget(msg.lParam);
                if (_devices.Remove(msg.lParam, out var gone) && gone.Preparsed != IntPtr.Zero) Marshal.FreeHGlobal(gone.Preparsed);
                CountDevices();
                break;
        }
    }

    private void ReadInput(IntPtr rawInput)
    {
        uint headerSize = (uint)(8 + 2 * IntPtr.Size), size = 0;
        if (GetRawInputData(rawInput, RID_INPUT, null, ref size, headerSize) != 0 || size == 0) return;
        if (_buffer.Length < size) _buffer = new byte[size];
        try
        {
            if (GetRawInputData(rawInput, RID_INPUT, _buffer, ref size, headerSize) == uint.MaxValue) return;
            uint type = BitConverter.ToUInt32(_buffer, 0);
            if (type == RIM_TYPEKEYBOARD)
            {
                Feed(new RawKeyboard(BitConverter.ToUInt16(_buffer, (int)headerSize), BitConverter.ToUInt16(_buffer, (int)headerSize + 2)));
            }
            else if (type == RIM_TYPEHID)
            {
                IntPtr device = IntPtr.Size == 8 ? (IntPtr)BitConverter.ToInt64(_buffer, 8) : BitConverter.ToInt32(_buffer, 8);
                ReadHid(device, (int)headerSize, (int)size);
            }
        }
        finally
        {
            Array.Clear(_buffer, 0, (int)Math.Min(size, (uint)_buffer.Length));
        }
    }

    private void ReadHid(IntPtr handle, int at, int end)
    {
        var device = DeviceFor(handle);
        if (device is not { Kind: PadKind.Sony, Preparsed: var preparsed } || preparsed == IntPtr.Zero) return;
        int reportSize = (int)BitConverter.ToUInt32(_buffer, at), count = (int)BitConverter.ToUInt32(_buffer, at + 4);
        if (_report.Length != reportSize) _report = new byte[reportSize];
        for (int i = 0, from = at + 8; i < count && from + reportSize <= end; i++, from += reportSize)
        {
            Buffer.BlockCopy(_buffer, from, _report, 0, reportSize);
            uint length = (uint)_usages.Length;
            int status = HidP_GetUsages(HidP_Input, UsagePageButton, 0, _usages, ref length, preparsed, _report, (uint)reportSize);
            Array.Clear(_report);
            if (status != HIDP_STATUS_SUCCESS)
            {
                _sonyReportsWithoutButtons++;
                continue;
            }
            var buttons = SkillInputDecoder.FromSonyUsages(_usages.Take((int)length));
            Array.Clear(_usages);
            foreach (int slot in _decoder?.Pad(handle, buttons) ?? []) Enqueue(slot);
        }
    }

    /// <summary>What kind of pad a HID device is, asked of Windows once per device.</summary>
    private Device? DeviceFor(IntPtr handle)
    {
        if (_devices.TryGetValue(handle, out var known)) return known;
        string name = DeviceName(handle);
        int vendor = VendorId(handle);
        var kind = name.Contains("IG_", StringComparison.OrdinalIgnoreCase) ? PadKind.XInput
            : vendor == 0x054C ? PadKind.Sony
            : PadKind.Other;
        var device = new Device(kind, kind == PadKind.Sony ? Preparsed(handle) : IntPtr.Zero);
        _devices[handle] = device;
        CountDevices();
        return device;
    }

    private static string DeviceName(IntPtr handle)
    {
        uint chars = 0;
        GetRawInputDeviceInfoW(handle, RIDI_DEVICENAME, IntPtr.Zero, ref chars);
        if (chars == 0) return "";
        IntPtr buffer = Marshal.AllocHGlobal((int)chars * 2);
        try
        {
            return GetRawInputDeviceInfoW(handle, RIDI_DEVICENAME, buffer, ref chars) == uint.MaxValue ? "" : Marshal.PtrToStringUni(buffer) ?? "";
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    /// <summary>RID_DEVICE_INFO's HID vendor id (cbSize 32; dwType at 4, dwVendorId at 8); 0 when unknown.</summary>
    private static int VendorId(IntPtr handle)
    {
        const int size = 32;
        IntPtr info = Marshal.AllocHGlobal(size);
        try
        {
            for (int i = 0; i < size; i += 4) Marshal.WriteInt32(info, i, 0);
            Marshal.WriteInt32(info, 0, size);
            uint got = size;
            if (GetRawInputDeviceInfoW(handle, RIDI_DEVICEINFO, info, ref got) == uint.MaxValue) return 0;
            return Marshal.ReadInt32(info, 4) == (int)RIM_TYPEHID ? Marshal.ReadInt32(info, 8) : 0;
        }
        finally { Marshal.FreeHGlobal(info); }
    }

    private static IntPtr Preparsed(IntPtr handle)
    {
        uint size = 0;
        GetRawInputDeviceInfoW(handle, RIDI_PREPARSEDDATA, IntPtr.Zero, ref size);
        if (size == 0) return IntPtr.Zero;
        IntPtr data = Marshal.AllocHGlobal((int)size);
        if (GetRawInputDeviceInfoW(handle, RIDI_PREPARSEDDATA, data, ref size) != uint.MaxValue) return data;
        Marshal.FreeHGlobal(data);
        return IntPtr.Zero;
    }

    private void CountDevices()
    {
        _sonyPads = _devices.Values.Count(d => d.Kind == PadKind.Sony);
        _otherPads = _devices.Values.Count(d => d.Kind == PadKind.Other);
    }

    private void ForgetDevices()
    {
        foreach (var device in _devices.Values)
            if (device.Preparsed != IntPtr.Zero) Marshal.FreeHGlobal(device.Preparsed);
        _devices.Clear();
        Array.Clear(_xinputConnected);
        _xinputMissing = false;
        _ticks = 0;
        _sonyPads = _xinputPads = _otherPads = _sonyReportsWithoutButtons = 0;
    }

    private void PollXInput()
    {
        if (_xinputMissing) return;
        bool everyone = _ticks++ % XInputEmptySlotEvery == 0;
        try
        {
            for (uint user = 0; user < 4; user++)
            {
                if (!_xinputConnected[user] && !everyone) continue;
                bool connected = XInputGetState(user, out var state) == ERROR_SUCCESS;
                IntPtr device = -1 - (int)user; // never a Raw Input handle
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
