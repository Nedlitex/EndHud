namespace EndHud.Vision;

/// <summary>An axis-aligned rectangle in pixels.</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public PixelRect Offset(int dx, int dy) => this with { X = X + dx, Y = Y + dy };

    /// <summary>The part of this rectangle that lies inside a <paramref name="width"/>×<paramref name="height"/> image.</summary>
    public PixelRect ClampTo(int width, int height)
    {
        int x0 = Math.Clamp(X, 0, width), y0 = Math.Clamp(Y, 0, height);
        int x1 = Math.Clamp(Right, 0, width), y1 = Math.Clamp(Bottom, 0, height);
        return new PixelRect(x0, y0, x1 - x0, y1 - y0);
    }
}
