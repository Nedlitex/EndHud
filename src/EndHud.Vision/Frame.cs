using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;

namespace EndHud.Vision;

/// <summary>An image as tightly packed 32-bit BGRA pixels (stride = Width × 4).</summary>
public sealed class Frame
{
    public Frame(int width, int height, byte[] bgra)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), "Frame must be non-empty.");
        if (bgra.Length < width * height * 4) throw new ArgumentException("Pixel buffer too small.", nameof(bgra));
        Width = width;
        Height = height;
        Bgra = bgra;
    }

    public int Width { get; }
    public int Height { get; }
    public byte[] Bgra { get; }

    public (byte R, byte G, byte B) GetPixel(int x, int y)
    {
        int i = (y * Width + x) * 4;
        return (Bgra[i + 2], Bgra[i + 1], Bgra[i]);
    }

    public Frame Crop(PixelRect rect)
    {
        var r = rect.ClampTo(Width, Height);
        if (r.IsEmpty) throw new ArgumentException($"Crop {rect} lies outside the {Width}×{Height} frame.", nameof(rect));
        if (r == new PixelRect(0, 0, Width, Height)) return this;

        var buffer = new byte[r.Width * r.Height * 4];
        for (int y = 0; y < r.Height; y++)
            Buffer.BlockCopy(Bgra, ((r.Y + y) * Width + r.X) * 4, buffer, y * r.Width * 4, r.Width * 4);
        return new Frame(r.Width, r.Height, buffer);
    }

    /// <summary>Resample to <paramref name="width"/>×<paramref name="height"/> by averaging the source pixels each target pixel covers.</summary>
    public Frame Resize(int width, int height)
    {
        var buffer = new byte[width * height * 4];
        double sx = Width / (double)width, sy = Height / (double)height;
        for (int y = 0; y < height; y++)
        {
            int y0 = (int)(y * sy), y1 = Math.Max(y0 + 1, Math.Min(Height, (int)((y + 1) * sy)));
            for (int x = 0; x < width; x++)
            {
                int x0 = (int)(x * sx), x1 = Math.Max(x0 + 1, Math.Min(Width, (int)((x + 1) * sx)));
                int b = 0, g = 0, r = 0, a = 0, n = 0;
                for (int yy = y0; yy < y1; yy++)
                for (int xx = x0; xx < x1; xx++)
                {
                    int i = (yy * Width + xx) * 4;
                    b += Bgra[i]; g += Bgra[i + 1]; r += Bgra[i + 2]; a += Bgra[i + 3];
                    n++;
                }
                int o = (y * width + x) * 4;
                buffer[o] = (byte)(b / n);
                buffer[o + 1] = (byte)(g / n);
                buffer[o + 2] = (byte)(r / n);
                buffer[o + 3] = (byte)(a / n);
            }
        }
        return new Frame(width, height, buffer);
    }

    public byte GetAlpha(int x, int y) => Bgra[(y * Width + x) * 4 + 3];

    public static Frame FromSoftwareBitmap(SoftwareBitmap bitmap)
    {
        using var converted = bitmap.BitmapPixelFormat == BitmapPixelFormat.Bgra8
            ? null
            : SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8);
        var source = converted ?? bitmap;
        var buffer = new byte[source.PixelWidth * source.PixelHeight * 4];
        source.CopyToBuffer(buffer.AsBuffer());
        return new Frame(source.PixelWidth, source.PixelHeight, buffer);
    }

    public SoftwareBitmap ToSoftwareBitmap() =>
        SoftwareBitmap.CreateCopyFromBuffer(Bgra.AsBuffer(), BitmapPixelFormat.Bgra8, Width, Height,
            BitmapAlphaMode.Premultiplied);
}
