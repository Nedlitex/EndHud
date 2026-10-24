using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using static EndHud.SkillInput.SkillInputNative;

namespace EndHud.SkillInput;

/// <summary>What kind of game controller a HID device is, for the battle-skill reader.</summary>
internal enum PadKind
{
    /// <summary>Not a game pad or joystick (a keyboard, a mouse, a headset...): never opened for reading.</summary>
    NotAPad,

    /// <summary>A PlayStation pad (Sony's vendor id): read here.</summary>
    Sony,

    /// <summary>An Xbox pad's HID face: read through XInput instead.</summary>
    XInput,

    /// <summary>Any other pad: counted, never read (its button numbers vary by maker).</summary>
    Other,
}

/// <summary>
/// PlayStation pads, read straight from the device: each one opened for shared reading (the game and
/// every other program keep reading it as before) and its input reports read with overlapped I/O on
/// the reader's own thread, which waits on them together with its messages. Unlike Raw Input, a
/// device read is not filtered by Windows' privilege isolation, so the pad is read while the game,
/// which runs as administrator, is in front. Each report goes through Windows' HID parser
/// (HidP_GetUsages) for its buttons, then straight to <paramref name="buttons"/>; the report is
/// cleared at once. Pads are looked for when reading starts and every second after (one plugged in
/// mid-fight is read within the second). Not thread-safe: the reader's thread is the only caller.
/// </summary>
/// <param name="buttons">A pad's buttons now, by the pad's id for this reading.</param>
/// <param name="gone">A pad went away (unplugged, or reading stopped): forget its buttons.</param>
internal sealed class SonyPads(Action<nint, PadButtons> buttons, Action<nint> gone) : IDisposable
{
    public const int SonyVendor = 0x054C;

    /// <summary>At most this many PlayStation pads are read at once (each is a handle to wait on).</summary>
    private const int MaxPads = 8;

    private const int ERROR_IO_PENDING = 997, ERROR_IO_INCOMPLETE = 996;

    private readonly List<Pad> _pads = [];
    private readonly Dictionary<string, PadKind> _kinds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _described = new(StringComparer.OrdinalIgnoreCase);
    private readonly ushort[] _usages = new ushort[64];
    private byte[] _report = [];
    private IntPtr[] _events = [];
    private nint _nextId = 1;

    /// <summary>The events to wait on, one per open pad, in the order <see cref="Complete"/> takes.</summary>
    public IntPtr[] Events => _events;

    /// <summary>HID devices seen at the last look (any kind; for tests and the diagnostics).</summary>
    public int Seen { get; private set; }

    /// <summary>PlayStation pads being read.</summary>
    public int Open => _pads.Count;

    /// <summary>PlayStation pads present but not opened (another program holds one alone).</summary>
    public int Unopened { get; private set; }

    /// <summary>Pads that are neither PlayStation nor Xbox: counted, never read.</summary>
    public int Other { get; private set; }

    /// <summary>PlayStation reports in which Windows' parser found no buttons (e.g. a DualSense switched to its extended Bluetooth report).</summary>
    public int ReportsWithoutButtons { get; private set; }

    /// <summary>What a HID device is, from its path, its vendor and its top-level usage.</summary>
    public static PadKind Classify(string path, int vendor, ushort usagePage, ushort usage)
    {
        if (usagePage != UsagePageGeneric || usage is not (UsageGamepad or UsageJoystick)) return PadKind.NotAPad;
        if (path.Contains("IG_", StringComparison.OrdinalIgnoreCase)) return PadKind.XInput;
        return vendor == SonyVendor ? PadKind.Sony : PadKind.Other;
    }

    /// <summary>Looks for pads: opens new PlayStation pads, lets go of those no longer there.</summary>
    public void Rescan()
    {
        var present = HidDevicePaths();
        Seen = present.Count;
        foreach (var pad in _pads.Where(p => !present.Contains(p.Path)).ToList()) Close(pad);
        foreach (string path in _kinds.Keys.Where(k => !present.Contains(k)).ToList()) _kinds.Remove(path);

        int unopened = 0;
        foreach (string path in present)
        {
            if (!_kinds.TryGetValue(path, out var kind))
            {
                var asked = KindOf(path);
                if (asked is not null) _described.Add(path);
                _kinds[path] = kind = asked ?? PadKind.NotAPad;
            }
            if (kind != PadKind.Sony || _pads.Any(p => p.Path == path)) continue;
            if (_pads.Count >= MaxPads || !TryOpen(path)) unopened++;
        }
        _described.IntersectWith(present);
        Unopened = unopened;
        Other = _kinds.Values.Count(k => k == PadKind.Other);
        Described = _described.Count;
    }

    /// <summary>HID devices present whose vendor and usage Windows told (tests: the asking itself works).</summary>
    public int Described { get; private set; }

    /// <summary>The read of the pad whose event is <paramref name="index"/> in <see cref="Events"/> finished: its buttons out, the next read started.</summary>
    public void Complete(int index)
    {
        if (index < 0 || index >= _pads.Count) return;
        var pad = _pads[index];
        if (!GetOverlappedResult(pad.File, pad.Overlapped, out uint length, false))
        {
            if (Marshal.GetLastPInvokeError() != ERROR_IO_INCOMPLETE) Close(pad); // unplugged, or the read failed
            return;
        }
        pad.Reading = false;
        Parse(pad, (int)Math.Min(length, (uint)pad.ReportLength));
        if (!Read(pad)) Close(pad);
    }

    /// <summary>Every pad let go: reads cancelled and waited for, handles closed, memory freed.</summary>
    public void Dispose()
    {
        foreach (var pad in _pads.ToList()) Close(pad);
        _kinds.Clear();
        _described.Clear();
        Seen = Unopened = Other = Described = ReportsWithoutButtons = 0;
    }

    private void Parse(Pad pad, int length)
    {
        if (_report.Length != pad.ReportLength) _report = new byte[pad.ReportLength];
        try
        {
            Marshal.Copy(pad.Buffer, _report, 0, length);
            uint count = (uint)_usages.Length;
            int status = HidP_GetUsages(HidP_Input, UsagePageButton, 0, _usages, ref count, pad.Preparsed, _report, (uint)pad.ReportLength);
            if (status != HIDP_STATUS_SUCCESS)
            {
                ReportsWithoutButtons++;
                return;
            }
            buttons(pad.Id, SkillInputDecoder.FromSonyUsages(_usages.Take((int)count)));
        }
        finally
        {
            Array.Clear(_report);
            Array.Clear(_usages);
            ZeroMemory(pad.Buffer, pad.ReportLength);
        }
    }

    /// <summary>Starts the pad's next overlapped read; false when Windows refused it (the pad is gone).</summary>
    private static bool Read(Pad pad)
    {
        Marshal.StructureToPtr(new NativeOverlapped { EventHandle = pad.Event }, pad.Overlapped, false);
        pad.Reading = ReadFile(pad.File, pad.Buffer, (uint)pad.ReportLength, IntPtr.Zero, pad.Overlapped)
            || Marshal.GetLastPInvokeError() == ERROR_IO_PENDING;
        return pad.Reading;
    }

    private bool TryOpen(string path)
    {
        var file = CreateFileW(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
        if (file.IsInvalid) return false;
        if (!HidD_GetPreparsedData(file, out IntPtr preparsed))
        {
            file.Dispose();
            return false;
        }
        int reportLength = ReportLength(preparsed);
        if (reportLength <= 0)
        {
            HidD_FreePreparsedData(preparsed);
            file.Dispose();
            return false;
        }
        var pad = new Pad(path, file, preparsed, reportLength, _nextId++,
            CreateEventW(IntPtr.Zero, true, false, null),
            Marshal.AllocHGlobal(Marshal.SizeOf<NativeOverlapped>()),
            Marshal.AllocHGlobal(reportLength));
        ZeroMemory(pad.Buffer, reportLength);
        _pads.Add(pad);
        _events = [.. _pads.Select(p => p.Event)];
        if (pad.Event == IntPtr.Zero || !Read(pad))
        {
            Close(pad);
            return false;
        }
        return true;
    }

    private void Close(Pad pad)
    {
        if (pad.Reading)
        {
            CancelIoEx(pad.File, pad.Overlapped);
            GetOverlappedResult(pad.File, pad.Overlapped, out _, true); // the read must be over before its memory goes
            pad.Reading = false;
        }
        pad.File.Dispose();
        if (pad.Event != IntPtr.Zero) CloseHandle(pad.Event);
        ZeroMemory(pad.Buffer, pad.ReportLength);
        Marshal.FreeHGlobal(pad.Buffer);
        Marshal.FreeHGlobal(pad.Overlapped);
        HidD_FreePreparsedData(pad.Preparsed);
        _pads.Remove(pad);
        _events = [.. _pads.Select(p => p.Event)];
        gone(pad.Id);
    }

    /// <summary>
    /// A device's kind, asked of it without reading it (opened with no access, which any program may do);
    /// null when Windows wouldn't say.
    /// </summary>
    private static PadKind? KindOf(string path)
    {
        using var file = CreateFileW(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (file.IsInvalid) return null;
        var attributes = new HIDD_ATTRIBUTES { Size = (uint)Marshal.SizeOf<HIDD_ATTRIBUTES>() };
        if (!HidD_GetAttributes(file, ref attributes) || !HidD_GetPreparsedData(file, out IntPtr preparsed)) return null;
        try
        {
            var (usagePage, usage, length) = Caps(preparsed);
            return length == 0 && usagePage == 0 ? null : Classify(path, attributes.VendorID, usagePage, usage);
        }
        finally { HidD_FreePreparsedData(preparsed); }
    }

    private static int ReportLength(IntPtr preparsed) => Caps(preparsed).InputReportLength;

    /// <summary>HIDP_CAPS' first fields: Usage at 0, UsagePage at 2, InputReportByteLength at 4 (the struct is 64 bytes).</summary>
    private static (ushort UsagePage, ushort Usage, int InputReportLength) Caps(IntPtr preparsed)
    {
        IntPtr caps = Marshal.AllocHGlobal(HidpCapsSize);
        try
        {
            ZeroMemory(caps, HidpCapsSize);
            if (HidP_GetCaps(preparsed, caps) != HIDP_STATUS_SUCCESS) return (0, 0, 0);
            return ((ushort)Marshal.ReadInt16(caps, 2), (ushort)Marshal.ReadInt16(caps, 0), (ushort)Marshal.ReadInt16(caps, 4));
        }
        finally { Marshal.FreeHGlobal(caps); }
    }

    /// <summary>The paths of the HID devices present now.</summary>
    private static HashSet<string> HidDevicePaths()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        HidD_GetHidGuid(out Guid hid);
        IntPtr set = SetupDiGetClassDevsW(ref hid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (set == IntPtr.Zero || set == new IntPtr(-1)) return paths;
        try
        {
            var data = new SP_DEVICE_INTERFACE_DATA { cbSize = (uint)Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
            for (uint index = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hid, index, ref data); index++)
            {
                SetupDiGetDeviceInterfaceDetailW(set, ref data, IntPtr.Zero, 0, out uint size, IntPtr.Zero);
                if (size == 0) continue;
                IntPtr detail = Marshal.AllocHGlobal((int)size);
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6); // SP_DEVICE_INTERFACE_DETAIL_DATA_W.cbSize
                    if (SetupDiGetDeviceInterfaceDetailW(set, ref data, detail, size, out _, IntPtr.Zero)
                        && Marshal.PtrToStringUni(detail + 4) is { Length: > 0 } path)
                        paths.Add(path);
                }
                finally { Marshal.FreeHGlobal(detail); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return paths;
    }

    private static void ZeroMemory(IntPtr memory, int length)
    {
        for (int i = 0; i < length; i++) Marshal.WriteByte(memory, i, 0);
    }

    private sealed class Pad(string path, SafeFileHandle file, IntPtr preparsed, int reportLength, nint id, IntPtr @event, IntPtr overlapped, IntPtr buffer)
    {
        public string Path { get; } = path;
        public SafeFileHandle File { get; } = file;
        public IntPtr Preparsed { get; } = preparsed;
        public int ReportLength { get; } = reportLength;
        public nint Id { get; } = id;
        public IntPtr Event { get; } = @event;
        public IntPtr Overlapped { get; } = overlapped;
        public IntPtr Buffer { get; } = buffer;

        /// <summary>A read is under way (its memory must stay until it is over).</summary>
        public bool Reading { get; set; }
    }
}
