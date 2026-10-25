using System.Runtime.InteropServices;

// The Windows DLLs the capture and input code call (d3d11.dll, dxgi.dll, hid.dll, ...) come from System32 only,
// never from beside the exe (see EndHud's Program.cs). The ONNX runtime loads itself; this doesn't change that.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
