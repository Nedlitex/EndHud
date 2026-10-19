namespace EndHud.Vision.Inn;

/// <summary>
/// Places the inn's screen elements, measured on 4K frames (3838×2158 recordings), in a window of
/// any size. The game scales its UI uniformly with the window (fit to 16:9). The session's table
/// cards and timer hug the top-left corner; the setup screens, the menu and the dispatch panel are
/// laid out on the whole 16:9 area, centred. Only 16:9 windows have been checked (the recordings and
/// their scaled copies); on another shape the corner and centre anchoring is EndHud's assumption.
/// </summary>
internal readonly struct InnLayout
{
    private readonly int _width, _height;

    public InnLayout(int width, int height)
    {
        _width = width;
        _height = height;
        Scale = Math.Min(width / (double)UiScaler.ReferenceWidth, height / (double)UiScaler.ReferenceHeight);
    }

    public InnLayout(Frame frame) : this(frame.Width, frame.Height) { }

    /// <summary>The window's size relative to 4K (<see cref="UiScaler.Scale"/>).</summary>
    public double Scale { get; }

    /// <summary>A rectangle anchored to the top-left corner (the table cards, the timer, the screens' titles).</summary>
    public PixelRect TopLeft(PixelRect r) => new(
        (int)Math.Round(r.X * Scale), (int)Math.Round(r.Y * Scale),
        Math.Max(1, (int)Math.Round(r.Width * Scale)), Math.Max(1, (int)Math.Round(r.Height * Scale)));

    /// <summary>A point anchored to the top-left corner.</summary>
    public (double X, double Y) TopLeft(double x, double y) => (x * Scale, y * Scale);

    /// <summary>A rectangle anchored to the top-right corner (the fire meter).</summary>
    public PixelRect TopRight(PixelRect r) => new(
        _width - (int)Math.Round((UiScaler.ReferenceWidth - r.X) * Scale), (int)Math.Round(r.Y * Scale),
        Math.Max(1, (int)Math.Round(r.Width * Scale)), Math.Max(1, (int)Math.Round(r.Height * Scale)));

    /// <summary>A rectangle on the centred 16:9 area (setup screens, menu, dispatch panel).</summary>
    public PixelRect Centered(PixelRect r)
    {
        double left = (_width - UiScaler.ReferenceWidth * Scale) / 2, top = (_height - UiScaler.ReferenceHeight * Scale) / 2;
        return new PixelRect(
            (int)Math.Round(left + r.X * Scale), (int)Math.Round(top + r.Y * Scale),
            Math.Max(1, (int)Math.Round(r.Width * Scale)), Math.Max(1, (int)Math.Round(r.Height * Scale)));
    }
}
