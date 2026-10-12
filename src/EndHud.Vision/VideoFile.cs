using Windows.Graphics.Imaging;
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
}
