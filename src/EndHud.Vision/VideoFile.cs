using System.Runtime.InteropServices;
using Windows.Graphics.Imaging;
using WinRT;
using Windows.Media.Editing;
using Windows.Storage;

namespace EndHud.Vision;

/// <summary>
/// Decodes frames from a video file (MP4/H.264 etc.) with Windows' built-in media stack. Used to
/// test the readers against recorded gameplay.
/// </summary>
public sealed class VideoFile
{
    private readonly MediaComposition _composition;

    private VideoFile(MediaComposition composition, TimeSpan duration, int width, int height)
    {
        _composition = composition;
        Duration = duration;
        Width = width;
        Height = height;
    }

    public TimeSpan Duration { get; }
    public int Width { get; }
    public int Height { get; }

    public static async Task<VideoFile> OpenAsync(string path)
    {
        var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path));
        var clip = await MediaClip.CreateFromFileAsync(file);
        var composition = new MediaComposition();
        composition.Clips.Add(clip);
        var video = clip.GetVideoEncodingProperties();
        return new VideoFile(composition, clip.OriginalDuration, (int)video.Width, (int)video.Height);
    }

    /// <summary>The frame shown at <paramref name="time"/>, at full resolution.</summary>
    public async Task<Frame> GetFrameAsync(TimeSpan time)
    {
        using var stream = await _composition.GetThumbnailAsync(time, Width, Height, VideoFramePrecision.NearestFrame);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        return Frame.FromSoftwareBitmap(bitmap);
    }

    /// <summary>
    /// Only <paramref name=region/> of the frame shown at <paramref name=time/> (clamped to it), copied straight out
    /// of the decoded picture: sampling a corner many times a second (the battle-skill flash) this way doesn't make a
    /// 33 MB copy of each 4K frame. (Measured: 150 whole 4K frames left 4 GB for the garbage collector, and a test run
    /// beside the other recording tests ran out of memory.)
    /// </summary>
    public async Task<Frame> GetRegionAsync(TimeSpan time, PixelRect region)
    {
        using var stream = await _composition.GetThumbnailAsync(time, Width, Height, VideoFramePrecision.NearestFrame);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        var r = region.ClampTo(bitmap.PixelWidth, bitmap.PixelHeight);
        if (r.IsEmpty) throw new ArgumentException($"Region {region} lies outside the {bitmap.PixelWidth}x{bitmap.PixelHeight} frame.", nameof(region));
        using var buffer = bitmap.LockBuffer(BitmapBufferAccessMode.Read);
        var plane = buffer.GetPlaneDescription(0);
        using var reference = buffer.CreateReference();
        reference.As<IMemoryBufferByteAccess>().GetBuffer(out IntPtr data, out _);
        var bgra = new byte[r.Width * r.Height * 4];
        for (int y = 0; y < r.Height; y++)
            Marshal.Copy(data + plane.StartIndex + (r.Y + y) * plane.Stride + r.X * 4, bgra, y * r.Width * 4, r.Width * 4);
        return new Frame(r.Width, r.Height, bgra);
    }

    /// <summary>The bytes behind a locked bitmap buffer (Windows' documented COM interface for it).</summary>
    [ComImport]
    [Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMemoryBufferByteAccess
    {
        void GetBuffer(out IntPtr buffer, out uint capacity);
    }
}
