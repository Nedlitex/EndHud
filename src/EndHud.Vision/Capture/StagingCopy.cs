using System.Runtime.InteropServices;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace EndHud.Vision.Capture;

/// <summary>
/// Copies a captured frame's GPU texture into memory the usual way capture tools do it (and
/// Microsoft's capture sample): CopyResource into a CPU-readable staging texture on the capture's
/// own D3D11 device, then Map and read it, all synchronously on the thread that received the frame.
/// It replaces <c>SoftwareBitmap.CreateCopyFromSurfaceAsync</c> waited on inside the frame
/// callback, which on Windows 10 22H2 PCs never returned: players' logs show one frame arrived and
/// its copy still running minutes later, and no frame after it.
/// </summary>
/// <remarks>
/// Not thread-safe: the device's immediate context is used without a lock, so one instance serves
/// one capture, whose frames arrive one at a time.
/// </remarks>
internal sealed class StagingCopy : IDisposable
{
    private readonly IntPtr _device;   // ID3D11Device*, owned (one reference)
    private readonly IntPtr _context;  // ID3D11DeviceContext*, owned
    private IntPtr _staging;           // ID3D11Texture2D*, recreated when the size changes
    private uint _width, _height;
    private IntPtr _cornerStaging;     // ID3D11Texture2D*, the small one for CopyRegion
    private uint _cornerWidth, _cornerHeight;
    private volatile string _step = "idle";
    private long _stepSince = System.Diagnostics.Stopwatch.GetTimestamp();

    public StagingCopy(IntPtr device, IntPtr context)
    {
        _device = device;
        _context = context;
    }

    /// <summary>The graphics adapter this copy's device is on (for the diagnostic log).</summary>
    public string Adapter => GraphicsInfo.AdapterOf(_device);

    /// <summary>What the copy is doing now and for how long, e.g. "map for 2300 ms": names the step that hangs.</summary>
    public string Step => $"{_step} for {System.Diagnostics.Stopwatch.GetElapsedTime(Interlocked.Read(ref _stepSince)).TotalMilliseconds:F0} ms";

    /// <summary>The last frame copied: size, DXGI pixel format and how long the copy took (null before the first).</summary>
    public string? LastCopy { get; private set; }

    private void Enter(string step)
    {
        _step = step;
        Interlocked.Exchange(ref _stepSince, System.Diagnostics.Stopwatch.GetTimestamp());
    }

    /// <summary>The surface's pixels (BGRA, top-down rows, alpha forced opaque).</summary>
    public Frame Copy(IDirect3DSurface surface)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Enter("get texture");
        IntPtr texture = TextureOf(surface);
        try
        {
            var desc = new D3D11_TEXTURE2D_DESC();
            Vtbl<GetDescFn>(texture, 10)(texture, ref desc);
            Enter("create staging texture");
            EnsureStaging(desc);

            Enter("copy on the GPU");
            Vtbl<CopyResourceFn>(_context, 47)(_context, _staging, texture);
            Enter("map");
            Check(Vtbl<MapFn>(_context, 14)(_context, _staging, 0, D3D11_MAP_READ, 0, out var mapped), "Map");
            Enter("read");
            try
            {
                int width = (int)desc.Width, height = (int)desc.Height, row = width * 4;
                var bgra = new byte[row * height];
                for (int y = 0; y < height; y++)
                    Marshal.Copy(mapped.pData + (int)(y * mapped.RowPitch), bgra, y * row, row);
                for (int i = 3; i < bgra.Length; i += 4) bgra[i] = 255;
                LastCopy = $"{width}x{height}, format {desc.Format}, row pitch {mapped.RowPitch}, copied in {clock.ElapsedMilliseconds} ms";
                return new Frame(width, height, bgra);
            }
            finally
            {
                Vtbl<UnmapFn>(_context, 15)(_context, _staging, 0);
            }
        }
        finally
        {
            Marshal.Release(texture);
            Enter("idle");
        }
    }

    /// <summary>
    /// Only <paramref name="region"/> of the surface (clamped to it): copied on the GPU into a small staging texture of
    /// its own, so reading a corner many times a second costs that corner's pixels, not the whole window's. Null when
    /// the region lies outside the surface.
    /// </summary>
    public Frame? CopyRegion(IDirect3DSurface surface, PixelRect region)
    {
        IntPtr texture = TextureOf(surface);
        try
        {
            var desc = new D3D11_TEXTURE2D_DESC();
            Vtbl<GetDescFn>(texture, 10)(texture, ref desc);
            var r = region.ClampTo((int)desc.Width, (int)desc.Height);
            if (r.IsEmpty) return null;
            if (_cornerStaging == IntPtr.Zero || _cornerWidth != r.Width || _cornerHeight != r.Height)
            {
                if (_cornerStaging != IntPtr.Zero) Marshal.Release(_cornerStaging);
                _cornerStaging = IntPtr.Zero;
                var small = desc with
                {
                    Width = (uint)r.Width,
                    Height = (uint)r.Height,
                    MipLevels = 1,
                    ArraySize = 1,
                    SampleCount = 1,
                    SampleQuality = 0,
                    Usage = D3D11_USAGE_STAGING,
                    BindFlags = 0,
                    CPUAccessFlags = D3D11_CPU_ACCESS_READ,
                    MiscFlags = 0,
                };
                Check(Vtbl<CreateTexture2DFn>(_device, 5)(_device, ref small, IntPtr.Zero, out _cornerStaging), "CreateTexture2D");
                (_cornerWidth, _cornerHeight) = ((uint)r.Width, (uint)r.Height);
            }
            var box = new D3D11_BOX { Left = (uint)r.X, Top = (uint)r.Y, Front = 0, Right = (uint)r.Right, Bottom = (uint)r.Bottom, Back = 1 };
            Vtbl<CopySubresourceRegionFn>(_context, 46)(_context, _cornerStaging, 0, 0, 0, 0, texture, 0, ref box);
            Check(Vtbl<MapFn>(_context, 14)(_context, _cornerStaging, 0, D3D11_MAP_READ, 0, out var mapped), "Map");
            try
            {
                int row = r.Width * 4;
                var bgra = new byte[row * r.Height];
                for (int y = 0; y < r.Height; y++)
                    Marshal.Copy(mapped.pData + (int)(y * mapped.RowPitch), bgra, y * row, row);
                for (int i = 3; i < bgra.Length; i += 4) bgra[i] = 255;
                return new Frame(r.Width, r.Height, bgra);
            }
            finally
            {
                Vtbl<UnmapFn>(_context, 15)(_context, _cornerStaging, 0);
            }
        }
        finally
        {
            Marshal.Release(texture);
        }
    }

    private void EnsureStaging(D3D11_TEXTURE2D_DESC source)
    {
        if (_staging != IntPtr.Zero && source.Width == _width && source.Height == _height) return;
        if (_staging != IntPtr.Zero) Marshal.Release(_staging);
        _staging = IntPtr.Zero;
        var desc = source with
        {
            MipLevels = 1,
            ArraySize = 1,
            SampleCount = 1,
            SampleQuality = 0,
            Usage = D3D11_USAGE_STAGING,
            BindFlags = 0,
            CPUAccessFlags = D3D11_CPU_ACCESS_READ,
            MiscFlags = 0,
        };
        Check(Vtbl<CreateTexture2DFn>(_device, 5)(_device, ref desc, IntPtr.Zero, out _staging), "CreateTexture2D");
        (_width, _height) = (source.Width, source.Height);
    }

    /// <summary>The ID3D11Texture2D behind a WinRT Direct3D surface (one reference, released by the caller).</summary>
    private static IntPtr TextureOf(IDirect3DSurface surface)
    {
        var access = surface.As<IDirect3DDxgiInterfaceAccess>();
        var iid = IID_ID3D11Texture2D;
        return access.GetInterface(ref iid);
    }

    public void Dispose()
    {
        if (_staging != IntPtr.Zero) Marshal.Release(_staging);
        _staging = IntPtr.Zero;
        if (_cornerStaging != IntPtr.Zero) Marshal.Release(_cornerStaging);
        _cornerStaging = IntPtr.Zero;
        Marshal.Release(_context);
        Marshal.Release(_device);
    }

    private static void Check(int hr, string what)
    {
        if (hr < 0) throw new InvalidOperationException($"D3D11 {what} failed (0x{hr:X8}).");
    }

    // ---- COM by hand: the few D3D11 calls used, by vtable slot ----

    private static T Vtbl<T>(IntPtr comObject, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(comObject), slot * IntPtr.Size));

    // ID3D11Texture2D::GetDesc (slot 10: IUnknown 3, ID3D11DeviceChild 4, ID3D11Resource 3).
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void GetDescFn(IntPtr self, ref D3D11_TEXTURE2D_DESC desc);

    // ID3D11Device::CreateTexture2D (slot 5).
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateTexture2DFn(IntPtr self, ref D3D11_TEXTURE2D_DESC desc, IntPtr initialData, out IntPtr texture);

    // ID3D11DeviceContext::Map (14), Unmap (15), CopyResource (47); CopySubresourceRegion (46) below.
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int MapFn(IntPtr self, IntPtr resource, uint subresource, int mapType, uint flags, out D3D11_MAPPED_SUBRESOURCE mapped);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void UnmapFn(IntPtr self, IntPtr resource, uint subresource);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void CopyResourceFn(IntPtr self, IntPtr destination, IntPtr source);

    // ID3D11DeviceContext::CopySubresourceRegion (46).
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void CopySubresourceRegionFn(IntPtr self, IntPtr destination, uint destinationSubresource, uint x, uint y, uint z,
        IntPtr source, uint sourceSubresource, ref D3D11_BOX box);

    [StructLayout(LayoutKind.Sequential)]
    private struct D3D11_BOX
    {
        public uint Left;
        public uint Top;
        public uint Front;
        public uint Right;
        public uint Bottom;
        public uint Back;
    }

    private const int D3D11_USAGE_STAGING = 3;
    private const uint D3D11_CPU_ACCESS_READ = 0x20000;
    private const int D3D11_MAP_READ = 1;
    private static readonly Guid IID_ID3D11Texture2D = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

    [StructLayout(LayoutKind.Sequential)]
    private record struct D3D11_TEXTURE2D_DESC
    {
        public uint Width;
        public uint Height;
        public uint MipLevels;
        public uint ArraySize;
        public int Format;
        public uint SampleCount;
        public uint SampleQuality;
        public int Usage;
        public uint BindFlags;
        public uint CPUAccessFlags;
        public uint MiscFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3D11_MAPPED_SUBRESOURCE
    {
        public IntPtr pData;
        public uint RowPitch;
        public uint DepthPitch;
    }

    [ComImport]
    [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDirect3DDxgiInterfaceAccess
    {
        IntPtr GetInterface([In] ref Guid iid);
    }
}
