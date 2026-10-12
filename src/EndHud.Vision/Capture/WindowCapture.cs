using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;

namespace EndHud.Vision.Capture;

/// <summary>
/// Captures one window's content with Windows.Graphics.Capture — the same API OBS's window capture
/// uses. It reads what the compositor already drew; nothing touches the target process. Windows
/// on top of the target (like our overlay) are not included.
/// </summary>
public sealed class WindowCapture : IDisposable
{
    private const DirectXPixelFormat PixelFormat = DirectXPixelFormat.B8G8R8A8UIntNormalized;
    private static readonly TimeSpan FrameTimeout = TimeSpan.FromSeconds(2);

    private readonly IDirect3DDevice _device;
    private readonly Direct3D11CaptureFramePool _pool;
    private readonly GraphicsCaptureSession _session;
    private readonly object _lock = new();
    private TaskCompletionSource<Frame>? _pending;
    private Frame? _latest;
    private SizeInt32 _poolSize;

    public WindowCapture(IntPtr hwnd)
    {
        if (!GraphicsCaptureSession.IsSupported())
            throw new PlatformNotSupportedException("Windows.Graphics.Capture is not supported on this system.");

        _device = CaptureInterop.CreateDevice();
        var item = CaptureInterop.CreateItemForWindow(hwnd);
        _poolSize = item.Size;
        _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(_device, PixelFormat, 2, _poolSize);
        _pool.FrameArrived += OnFrameArrived;
        _session = _pool.CreateCaptureSession(item);
        _session.IsCursorCaptureEnabled = false;
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            // Hide the yellow "being captured" border; not allowed on every build, so best-effort.
            try { _session.IsBorderRequired = false; } catch (Exception) { }
        }
        // Keep the first frame: Windows sends one on start, then only when the content changes.
        _pending = new TaskCompletionSource<Frame>(TaskCreationOptions.RunContinuationsAsynchronously);
        _session.StartCapture();
    }

    /// <summary>
    /// The next frame the window presents. Windows only delivers frames when the content changes,
    /// so if none arrives within 2 s the last frame is returned (the window still shows it);
    /// <see cref="TimeoutException"/> only if no frame has arrived at all.
    /// </summary>
    public async Task<Frame> GetFrameAsync(CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource<Frame>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_lock)
        {
            _pending?.TrySetCanceled();
            _pending = tcs;
        }
        try
        {
            return await tcs.Task.WaitAsync(FrameTimeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            lock (_lock)
            {
                if (_pending == tcs) _pending = null;
                return _latest ?? throw new TimeoutException("The window has not presented a frame yet.");
            }
        }
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        using var frame = sender.TryGetNextFrame();
        if (frame is null) return;

        var size = frame.ContentSize;
        if (size.Width != _poolSize.Width || size.Height != _poolSize.Height)
        {
            // Window resized: rebuild buffers at the new size and wait for a frame that fits.
            _poolSize = size;
            sender.Recreate(_device, PixelFormat, 2, size);
            return;
        }

        TaskCompletionSource<Frame>? tcs;
        lock (_lock)
        {
            tcs = _pending;
            _pending = null;
        }
        if (tcs is null) return; // nobody asked; drop the frame

        try
        {
            using var bitmap = SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface, BitmapAlphaMode.Premultiplied)
                .AsTask().GetAwaiter().GetResult();
            var copy = Frame.FromSoftwareBitmap(bitmap);
            lock (_lock) _latest = copy;
            tcs.TrySetResult(copy);
        }
        catch (Exception ex)
        {
            tcs.TrySetException(ex);
        }
    }

    public void Dispose()
    {
        lock (_lock) _pending?.TrySetCanceled();
        _pool.FrameArrived -= OnFrameArrived;
        _session.Dispose();
        _pool.Dispose();
        _device.Dispose();
    }
}
