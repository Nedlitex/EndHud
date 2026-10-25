using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace EndHud.Vision;

/// <summary>Reads and writes <see cref="Frame"/>s as image files (PNG, JPEG, BMP…).</summary>
public static class ImageFile
{
    public static async Task<Frame> LoadAsync(string path)
    {
        // A plain file stream, not StorageFile.OpenReadAsync: BitmapDecoder.CreateAsync refused StorageFile's stream
        // now and then ("Value does not fall within the expected range") while other pictures were decoding and the
        // garbage collector ran: measured with 12 loads side by side, 3 to 24 refusals in about 5,700 loads through
        // StorageFile, none in about 15,500 through a file stream.
        using var stream = await FileRandomAccessStream.OpenAsync(Path.GetFullPath(path), FileAccessMode.Read);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        return Frame.FromSoftwareBitmap(bitmap);
    }

    /// <summary>Decode an image held in memory (e.g. an embedded resource).</summary>
    public static async Task<Frame> LoadAsync(byte[] encoded)
    {
        using var memory = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(memory.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(encoded);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
        }
        memory.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(memory);
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        return Frame.FromSoftwareBitmap(bitmap);
    }

    public static async Task SavePngAsync(Frame frame, string path)
    {
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var bitmap = frame.ToSoftwareBitmap();
        using var memory = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, memory);
        encoder.SetSoftwareBitmap(bitmap);
        await encoder.FlushAsync();

        memory.Seek(0);
        await using var output = File.Create(path);
        await memory.AsStreamForRead().CopyToAsync(output);
    }
}
