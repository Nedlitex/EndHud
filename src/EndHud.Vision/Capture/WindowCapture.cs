using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

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
    private readonly StagingCopy _reader;
    private readonly object _copyLock = new(); // a frame being copied vs. Dispose releasing the reader
    private bool _disposed;
    private readonly Direct3D11CaptureFramePool _pool;
    private readonly GraphicsCaptureSession _session;
    private readonly object _lock = new();
    private TaskCompletionSource<Frame>? _pending;
    private Frame? _latest;
    private SizeInt32 _poolSize;
    private int _arrived, _resized, _copied, _copyFailed, _copying;
    private string? _copyError;

    /// <summary>
    /// What Windows has sent so far, for the diagnostic log when no picture comes: the callback
    /// firing at all, size changes, pictures copied, copies failed or still running.
    /// </summary>
    public string Describe() =>
        $"capture item {_poolSize.Width}x{_poolSize.Height}: {_arrived} frames arrived, {_resized} resizes, {_copied} copied, " +
        $"{_copyFailed} copies failed{(_copyError is null ? "" : $" (last: {_copyError})")}, {_copying} copying now";

    public WindowCapture(IntPtr hwnd)
    {
        if (!GraphicsCaptureSession.IsSupported())
            throw new PlatformNotSupportedException("Windows.Graphics.Capture is not supported on this system.");

        _device = CaptureInterop.CreateDevice(out _reader);
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
        Interlocked.Increment(ref _arrived);
        using var frame = sender.TryGetNextFrame();
        if (frame is null) return;

        var size = frame.ContentSize;
        if (size.Width != _poolSize.Width || size.Height != _poolSize.Height)
        {
            // Window resized: rebuild buffers at the new size and wait for a frame that fits.
            _poolSize = size;
            Interlocked.Increment(ref _resized);
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

        Interlocked.Increment(ref _copying);
        try
        {
            // Synchronously, on this thread: see StagingCopy for why not CreateCopyFromSurfaceAsync.
            Frame copy;
            lock (_copyLock)
            {
                if (_disposed) return;
                copy = _reader.Copy(frame.Surface);
            }
            lock (_lock) _latest = copy;
            Interlocked.Increment(ref _copied);
            tcs.TrySetResult(copy);
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _copyFailed);
            _copyError = $"{ex.GetType().Name}: {ex.Message}";
            tcs.TrySetException(ex);
        }
        finally
        {
            Interlocked.Decrement(ref _copying);
        }
    }

    public void Dispose()
    {
        lock (_lock) _pending?.TrySetCanceled();
        _pool.FrameArrived -= OnFrameArrived;
        _session.Dispose();
        _pool.Dispose();
        lock (_copyLock)
        {
            _disposed = true;
            _reader.Dispose();
        }
        _device.Dispose();
    }
}
