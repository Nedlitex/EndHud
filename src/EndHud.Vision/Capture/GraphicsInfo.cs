using System.Runtime.InteropServices;
using System.Text;

namespace EndHud.Vision.Capture;

/// <summary>
/// The PC's graphics as far as capture depends on it, for the diagnostic log: every graphics
/// adapter (name, vendor, driver version, memory), each screen it drives (position, HDR or not),
/// which screen the game window is on, and which adapter EndHud's capture device uses. Laptops with
/// two GPUs and HDR screens are where window capture is known to fail or to change colours. Only
/// hardware facts; never throws (a part that can't be read says so).
/// </summary>
public static class GraphicsInfo
{
    /// <summary>A few lines describing the adapters and screens, with the game window's screen marked.</summary>
    public static string Describe(IntPtr gameWindow)
    {
        var sb = new StringBuilder();
        try
        {
            IntPtr gameMonitor = gameWindow == IntPtr.Zero ? IntPtr.Zero : MonitorFromWindow(gameWindow, MONITOR_DEFAULTTONEAREST);
            Check(CreateDXGIFactory1(IID_IDXGIFactory1, out IntPtr factory), "CreateDXGIFactory1");
            try
            {
                for (uint a = 0; Vtbl<EnumAdapters1Fn>(factory, 12)(factory, a, out IntPtr adapter) >= 0; a++)
                {
                    try
                    {
                        var desc = new DXGI_ADAPTER_DESC1();
                        Check(Vtbl<GetDesc1Fn>(adapter, 10)(adapter, ref desc), "GetDesc1");
                        sb.Append($"adapter {a}: {desc.Description} (vendor 0x{desc.VendorId:X4}, device 0x{desc.DeviceId:X4}, " +
                                  $"{(ulong)desc.DedicatedVideoMemory / (1024 * 1024)} MB dedicated{((desc.Flags & 2) != 0 ? ", software" : "")}, " +
                                  $"driver {DriverVersion(adapter)}, luid {desc.LuidHigh:X}:{desc.LuidLow:X})");
                        sb.AppendLine();
                        for (uint o = 0; Vtbl<EnumOutputsFn>(adapter, 7)(adapter, o, out IntPtr output) >= 0; o++)
                        {
                            try { sb.AppendLine($"  screen {DescribeOutput(output, gameMonitor)}"); }
                            finally { Marshal.Release(output); }
                        }
                    }
                    finally
                    {
                        Marshal.Release(adapter);
                    }
                }
            }
            finally
            {
                Marshal.Release(factory);
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"graphics adapters: unavailable ({ex.GetType().Name}: {ex.Message})");
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>The adapter behind a D3D11 device (EndHud's capture device), as name and LUID.</summary>
    internal static string AdapterOf(IntPtr d3dDevice)
    {
        try
        {
            Check(Marshal.QueryInterface(d3dDevice, in IID_IDXGIDevice, out IntPtr dxgiDevice), "QueryInterface IDXGIDevice");
            try
            {
                Check(Vtbl<GetAdapterFn>(dxgiDevice, 7)(dxgiDevice, out IntPtr adapter), "GetAdapter");
                try
                {
                    Check(Marshal.QueryInterface(adapter, in IID_IDXGIAdapter1, out IntPtr adapter1), "QueryInterface IDXGIAdapter1");
                    try
                    {
                        var desc = new DXGI_ADAPTER_DESC1();
                        Check(Vtbl<GetDesc1Fn>(adapter1, 10)(adapter1, ref desc), "GetDesc1");
                        return $"{desc.Description} (luid {desc.LuidHigh:X}:{desc.LuidLow:X})";
                    }
                    finally { Marshal.Release(adapter1); }
                }
                finally { Marshal.Release(adapter); }
            }
            finally { Marshal.Release(dxgiDevice); }
        }
        catch (Exception ex)
        {
            return $"unknown ({ex.GetType().Name}: {ex.Message})";
        }
    }

    private static string DescribeOutput(IntPtr output, IntPtr gameMonitor)
    {
        var desc = new DXGI_OUTPUT_DESC();
        Check(Vtbl<GetOutputDescFn>(output, 7)(output, ref desc), "GetDesc");
        var r = desc.DesktopCoordinates;
        string text = $"{desc.DeviceName}: {r.Right - r.Left}x{r.Bottom - r.Top} at ({r.Left},{r.Top})";
        if (Marshal.QueryInterface(output, in IID_IDXGIOutput6, out IntPtr output6) >= 0)
        {
            try
            {
                var desc1 = new DXGI_OUTPUT_DESC1();
                if (Vtbl<GetOutputDesc1Fn>(output6, 27)(output6, ref desc1) >= 0)
                    text += $", {desc1.BitsPerColor}-bit, {(desc1.ColorSpace == DXGI_COLOR_SPACE_RGB_FULL_G2084_NONE_P2020 ? "HDR on" : "HDR off")}";
            }
            finally { Marshal.Release(output6); }
        }
        if (desc.Monitor == gameMonitor && gameMonitor != IntPtr.Zero) text += " <- the game window is here";
        return text;
    }

    /// <summary>The user-mode driver version DXGI reports for the adapter, e.g. "31.0.15.5222".</summary>
    private static string DriverVersion(IntPtr adapter)
    {
        var iid = IID_IDXGIDevice;
        if (Vtbl<CheckInterfaceSupportFn>(adapter, 9)(adapter, ref iid, out long version) < 0) return "unknown";
        return $"{(version >> 48) & 0xFFFF}.{(version >> 32) & 0xFFFF}.{(version >> 16) & 0xFFFF}.{version & 0xFFFF}";
    }

    private static void Check(int hr, string what)
    {
        if (hr < 0) throw new InvalidOperationException($"{what} failed (0x{hr:X8})");
    }

    private static T Vtbl<T>(IntPtr comObject, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(comObject), slot * IntPtr.Size));

    // Vtable slots: IUnknown 0-2, IDXGIObject 3-6; IDXGIFactory EnumAdapters 7 .. IDXGIFactory1 EnumAdapters1 12;
    // IDXGIAdapter EnumOutputs 7, GetDesc 8, CheckInterfaceSupport 9, IDXGIAdapter1 GetDesc1 10;
    // IDXGIOutput GetDesc 7 .. IDXGIOutput6 GetDesc1 27; IDXGIDevice GetAdapter 7.
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int EnumAdapters1Fn(IntPtr self, uint index, out IntPtr adapter);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetDesc1Fn(IntPtr self, ref DXGI_ADAPTER_DESC1 desc);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int EnumOutputsFn(IntPtr self, uint index, out IntPtr output);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CheckInterfaceSupportFn(IntPtr self, ref Guid iid, out long umdVersion);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetOutputDescFn(IntPtr self, ref DXGI_OUTPUT_DESC desc);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetOutputDesc1Fn(IntPtr self, ref DXGI_OUTPUT_DESC1 desc);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetAdapterFn(IntPtr self, out IntPtr adapter);

    private static readonly Guid IID_IDXGIFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");
    private static readonly Guid IID_IDXGIAdapter1 = new("29038f61-3839-4626-91fd-086879011a05");
    private static readonly Guid IID_IDXGIDevice = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
    private static readonly Guid IID_IDXGIOutput6 = new("068346e8-aaec-4b84-add7-137f513f77a1");
    private const int DXGI_COLOR_SPACE_RGB_FULL_G2084_NONE_P2020 = 12;
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("dxgi.dll")] private static extern int CreateDXGIFactory1(in Guid iid, out IntPtr factory);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DXGI_ADAPTER_DESC1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint VendorId, DeviceId, SubSysId, Revision;
        public UIntPtr DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
        public uint LuidLow;
        public int LuidHigh;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DXGI_OUTPUT_DESC
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        public RECT DesktopCoordinates;
        public int AttachedToDesktop;
        public int Rotation;
        public IntPtr Monitor;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DXGI_OUTPUT_DESC1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        public RECT DesktopCoordinates;
        public int AttachedToDesktop;
        public int Rotation;
        public IntPtr Monitor;
        public uint BitsPerColor;
        public int ColorSpace;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 11)] public float[] Chromaticity; // primaries, white point, luminance
    }
}
