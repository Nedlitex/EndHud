namespace EndHud.Vision.Inn;

/// <summary>
/// Places the inn's screen elements, measured on 4K frames (3838×2158 recordings), in a window of
/// any size. The game scales its UI uniformly with the window (fit to 16:9). The session's table
/// cards and timer hug the top-left corner; the setup screens, the menu and the dispatch panel are
/// laid out on the whole 16:9 area. Only 16:9 windows have been checked (the recordings and their
/// scaled copies). On another shape the 16:9 area leaves a margin, and where the game puts a panel in
/// it isn't known: a 16:10 player's 营业设定 was read 120 px off with the area centred (2026-10-26
/// diagnostics: no kitchens, a kitchen title taken for the theme). A reader that can tell a good
/// reading from a bad one tries each of <see cref="Placements"/> and keeps the best.
/// </summary>
internal readonly struct InnLayout
{
    private readonly int _width, _height;
    // Where the 16:9 area sits in the margin a screen of another shape leaves: 0 at the top or left, 0.5 centred, 1 at the bottom or right.
    private readonly double _along;

    public InnLayout(int width, int height) : this(width, height, 0.5) { }

    private InnLayout(int width, int height, double along)
    {
        _width = width;
        _height = height;
        _along = along;
        Scale = Math.Min(width / (double)UiScaler.ReferenceWidth, height / (double)UiScaler.ReferenceHeight);
    }

    public InnLayout(Frame frame) : this(frame.Width, frame.Height) { }

    /// <summary>
    /// The places the 16:9 area may take: centred, then against one edge and the other (top and bottom on a taller
    /// screen, left and right on a wider one). Only centred on a 16:9 screen, which leaves no margin to place it in.
    /// </summary>
    public IReadOnlyList<InnLayout> Placements()
    {
        double margin = Math.Max(_width - UiScaler.ReferenceWidth * Scale, _height - UiScaler.ReferenceHeight * Scale);
        return margin < 2 ? [this] : [this, new InnLayout(_width, _height, 0), new InnLayout(_width, _height, 1)];
    }

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

    /// <summary>A rectangle on the 16:9 area (setup screens, menu, dispatch panel): centred, or as placed (<see cref="Placements"/>).</summary>
    public PixelRect Centered(PixelRect r)
    {
        double left = (_width - UiScaler.ReferenceWidth * Scale) * _along, top = (_height - UiScaler.ReferenceHeight * Scale) * _along;
        return new PixelRect(
            (int)Math.Round(left + r.X * Scale), (int)Math.Round(top + r.Y * Scale),
            Math.Max(1, (int)Math.Round(r.Width * Scale)), Math.Max(1, (int)Math.Round(r.Height * Scale)));
    }
}
