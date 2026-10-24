using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace EndHud.SkillInput;

/// <summary>
/// The Win32 calls the battle-skill key reader uses: hotkeys for the four bound keys on a message-only
/// window, HID device reading for PlayStation pads and XInput for Xbox pads. No keyboard hook and no
/// Raw Input: see <see cref="SkillInputReader"/>.
/// </summary>
internal static class SkillInputNative
{
    public const int WM_CLOSE = 0x0010;
    public const int WM_TIMER = 0x0113;
    public const int WM_HOTKEY = 0x0312;
    public const int WM_KEYDOWN = 0x0100;
    public const int WM_SYSKEYDOWN = 0x0104;

    public static readonly IntPtr HWND_MESSAGE = new(-3);

    /// <summary>The generic desktop page's usages a game controller has: gamepad, joystick.</summary>
    public const ushort UsagePageGeneric = 0x01, UsageJoystick = 0x04, UsageGamepad = 0x05;

    public const ushort UsagePageButton = 0x09;
    public const int HidP_Input = 0;
    public const int HIDP_STATUS_SUCCESS = 0x00110000;

    /// <summary>HIDP_CAPS: 5 USHORTs, Reserved[17], 10 USHORTs.</summary>
    public const int HidpCapsSize = 64;

    public const uint ERROR_SUCCESS = 0;

    public const uint GENERIC_READ = 0x80000000;
    public const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2;
    public const uint OPEN_EXISTING = 3;
    public const uint FILE_FLAG_OVERLAPPED = 0x40000000;

    public const uint DIGCF_PRESENT = 0x02, DIGCF_DEVICEINTERFACE = 0x10;

    public const uint INFINITE = 0xFFFFFFFF;
    public const uint WAIT_FAILED = 0xFFFFFFFF;
    public const uint QS_ALLINPUT = 0x04FF;
    public const uint MWMO_INPUTAVAILABLE = 0x0004;
    public const uint PM_REMOVE = 0x0001;

    public delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XINPUT_STATE
    {
        public uint dwPacketNumber;
        public ushort wButtons;
        public byte bLeftTrigger;
        public byte bRightTrigger;
        public short sThumbLX;
        public short sThumbLY;
        public short sThumbRX;
        public short sThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDD_ATTRIBUTES
    {
        public uint Size;
        public ushort VendorID;
        public ushort ProductID;
        public ushort VersionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SP_DEVICE_INTERFACE_DATA
    {
        public uint cbSize;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public UIntPtr Reserved;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern ushort RegisterClassExW(ref WNDCLASSEX wc);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateWindowExW(uint exStyle, string className, string? windowName, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool PeekMessageW(out MSG msg, IntPtr hWnd, uint filterMin, uint filterMax, uint remove);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint MsgWaitForMultipleObjectsEx(uint count, IntPtr[] handles, uint milliseconds, uint wakeMask, uint flags);

    [DllImport("user32.dll")]
    public static extern IntPtr DispatchMessageW(ref MSG msg);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern UIntPtr SetTimer(IntPtr hWnd, UIntPtr id, uint elapseMs, IntPtr timerProc);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool KillTimer(IntPtr hWnd, UIntPtr id);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandleW(string? moduleName);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("hid.dll")]
    public static extern void HidD_GetHidGuid(out Guid hidGuid);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_GetAttributes(SafeFileHandle device, ref HIDD_ATTRIBUTES attributes);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_GetPreparsedData(SafeFileHandle device, out IntPtr preparsedData);

    [DllImport("hid.dll")]
    public static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

    [DllImport("hid.dll")]
    public static extern int HidP_GetCaps(IntPtr preparsedData, IntPtr capabilities);

    [DllImport("hid.dll")]
    public static extern int HidP_GetUsages(int reportType, ushort usagePage, ushort linkCollection, [Out] ushort[] usages,
        ref uint usageLength, IntPtr preparsedData, [In] byte[] report, uint reportLength);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr SetupDiGetClassDevsW(ref Guid classGuid, IntPtr enumerator, IntPtr parent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr deviceInfo, ref Guid classGuid, uint index,
        ref SP_DEVICE_INTERFACE_DATA data);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail,
        uint detailSize, out uint requiredSize, IntPtr deviceInfo);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint disposition,
        uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool ReadFile(SafeFileHandle file, IntPtr buffer, uint toRead, IntPtr read, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetOverlappedResult(SafeFileHandle file, IntPtr overlapped, out uint transferred, bool wait);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CancelIoEx(SafeFileHandle file, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateEventW(IntPtr security, bool manualReset, bool initialState, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr handle);

    [DllImport("xinput1_4.dll")]
    public static extern uint XInputGetState(uint userIndex, out XINPUT_STATE state);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKeyW(uint code, uint mapType);

    private const uint MAPVK_VSC_TO_VK_EX = 3;

    /// <summary>
    /// The virtual key of a stored key (<see cref="SkillKeyCodes"/>) on the player's keyboard layout, which a
    /// hotkey is registered with; 0 when it has none.
    /// </summary>
    public static uint VirtualKey(int code) =>
        MapVirtualKeyW((uint)(code & 0xFF) | ((code & SkillKeyCodes.Extended) != 0 ? 0xE000u : 0), MAPVK_VSC_TO_VK_EX);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetKeyNameTextW(int lParam, StringBuilder name, int size);

    /// <summary>
    /// The name Windows gives a stored key (<see cref="SkillKeyCodes"/>) on the player's keyboard
    /// layout: "1", "Q", "Num 1"; empty when it has none.
    /// </summary>
    public static string KeyName(int code)
    {
        var name = new StringBuilder(64);
        int length = GetKeyNameTextW(((code & 0xFF) << 16) | ((code & SkillKeyCodes.Extended) != 0 ? 1 << 24 : 0), name, name.Capacity);
        return length > 0 ? name.ToString(0, length) : "";
    }
}
