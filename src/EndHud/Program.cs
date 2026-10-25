using System.Runtime.InteropServices;

// The Windows DLLs EndHud calls (hid.dll, xinput1_4.dll, dwmapi.dll, d3d11.dll, dxgi.dll, msi.dll are not
// KnownDLLs) come from System32 only, never from beside the exe: a portable EndHud.exe sits in Downloads.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace EndHud;

/// <summary>
/// EndHud's entry point (EndHud.csproj: StartupObject). Before anything else loads, Windows is told to look for
/// DLLs loaded by name in System32 only, so a DLL planted next to the exe (Downloads, for a portable copy) is
/// never picked up by Windows, WPF or .NET loading something by name. Libraries .NET loads by full path
/// (the ONNX runtime, WPF's native parts, from beside the exe or the single-file's extraction folder) are not
/// affected: release.yml's self-test runs the character model and draws with WPF in the published exe.
/// </summary>
internal static class Program
{
    private const uint LoadLibrarySearchSystem32 = 0x00000800;

    [STAThread]
    public static void Main()
    {
        SetDefaultDllDirectories(LoadLibrarySearchSystem32);
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetDefaultDllDirectories(uint directoryFlags);
}
