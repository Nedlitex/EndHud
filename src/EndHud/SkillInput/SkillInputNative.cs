using System.Runtime.InteropServices;
using System.Text;

namespace EndHud.SkillInput;

/// <summary>
/// The Win32 calls the battle-skill key reader uses: Raw Input (which only listens; nothing is taken
/// from any program), a message-only window to receive it, HID parsing for PlayStation pads and XInput
/// for Xbox pads. No keyboard hook and no hotkey: see <see cref="SkillInputReader"/>.
/// </summary>
internal static class SkillInputNative
{
    public const int WM_CLOSE = 0x0010;
    public const int WM_TIMER = 0x0113;
    public const int WM_INPUT = 0x00FF;
    public const int WM_HOTKEY = 0x0312;
    public const int WM_INPUT_DEVICE_CHANGE = 0x00FE;
    public const int WM_KEYDOWN = 0x0100;
    public const int WM_SYSKEYDOWN = 0x0104;

    public const int GIDC_ARRIVAL = 1, GIDC_REMOVAL = 2;

    public static readonly IntPtr HWND_MESSAGE = new(-3);

    public const uint RIDEV_REMOVE = 0x00000001;
    public const uint RIDEV_INPUTSINK = 0x00000100;
    public const uint RIDEV_DEVNOTIFY = 0x00002000;

    public const uint RID_INPUT = 0x10000003;
    public const uint RIM_TYPEKEYBOARD = 1, RIM_TYPEHID = 2;

    public const uint RIDI_PREPARSEDDATA = 0x20000005;
    public const uint RIDI_DEVICENAME = 0x20000007;
    public const uint RIDI_DEVICEINFO = 0x2000000b;

    /// <summary>The generic desktop page's usages registered: keyboard, gamepad, joystick.</summary>
    public const ushort UsagePageGeneric = 0x01, UsageJoystick = 0x04, UsageGamepad = 0x05, UsageKeyboard = 0x06;

    public const ushort UsagePageButton = 0x09;
    public const int HidP_Input = 0;
    public const int HIDP_STATUS_SUCCESS = 0x00110000;

    public const uint ERROR_SUCCESS = 0;

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
    public struct RAWINPUTDEVICE
    {
        public ushort usUsagePage;
        public ushort usUsage;
        public uint dwFlags;
        public IntPtr hwndTarget;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUTHEADER
    {
        public uint dwType;
        public uint dwSize;
        public IntPtr hDevice;
        public IntPtr wParam;
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

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern ushort RegisterClassExW(ref WNDCLASSEX wc);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool UnregisterClassW(string className, IntPtr hInstance);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateWindowExW(uint exStyle, string className, string? windowName, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetMessageW(out MSG msg, IntPtr hWnd, uint filterMin, uint filterMax);

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
    public static extern bool RegisterRawInputDevices([In] RAWINPUTDEVICE[] devices, uint count, uint size);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetRegisteredRawInputDevices([Out] RAWINPUTDEVICE[]? devices, ref uint count, uint size);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetRawInputData(IntPtr rawInput, uint command, [Out] byte[]? data, ref uint size, uint headerSize);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern uint GetRawInputDeviceInfoW(IntPtr device, uint command, IntPtr data, ref uint size);

    [DllImport("hid.dll")]
    public static extern int HidP_GetUsages(int reportType, ushort usagePage, ushort linkCollection, [Out] ushort[] usages,
        ref uint usageLength, IntPtr preparsedData, [In] byte[] report, uint reportLength);

    [DllImport("xinput1_4.dll")]
    public static extern uint XInputGetState(uint userIndex, out XINPUT_STATE state);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

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
