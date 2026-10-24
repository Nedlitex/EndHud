namespace EndHud.Vision;

/// <summary>
/// Maps HUD positions measured on a 3840×2160 screenshot to any window size. The game's HUD
/// scales uniformly with the window (fit to 16:9) and corner-anchored elements keep their
/// distance from that corner.
/// </summary>
public readonly struct UiScaler
{
    public const int ReferenceWidth = 3840;
    public const int ReferenceHeight = 2160;

    private readonly int _width;
    private readonly int _height;
    private readonly double _scale;

    public UiScaler(int width, int height)
    {
        _width = width;
        _height = height;
        _scale = Math.Min(width / (double)ReferenceWidth, height / (double)ReferenceHeight);
    }

    public double Scale => _scale;

    /// <summary>A point anchored to the bottom-right corner of the window.</summary>
    public (double X, double Y) BottomRight(double referenceX, double referenceY) => (
        _width - (ReferenceWidth - referenceX) * _scale,
        _height - (ReferenceHeight - referenceY) * _scale);

    /// <summary>A rectangle anchored to the bottom-right corner of the window.</summary>
    public PixelRect BottomRight(PixelRect reference) => new(
        _width - (int)Math.Round((ReferenceWidth - reference.X) * _scale),
        _height - (int)Math.Round((ReferenceHeight - reference.Y) * _scale),
        Math.Max(1, (int)Math.Round(reference.Width * _scale)),
        Math.Max(1, (int)Math.Round(reference.Height * _scale)));

    /// <summary>
    /// A rectangle anchored to the bottom centre of the window (the controlled operator's skill-point
    /// and HP bars): it keeps its distance from the window's centre line and from the bottom.
    /// </summary>
    public PixelRect BottomCenter(PixelRect reference) => new(
        (int)Math.Round(_width / 2.0 - (ReferenceWidth / 2.0 - reference.X) * _scale),
        _height - (int)Math.Round((ReferenceHeight - reference.Y) * _scale),
        Math.Max(1, (int)Math.Round(reference.Width * _scale)),
        Math.Max(1, (int)Math.Round(reference.Height * _scale)));

    /// <summary>A rectangle anchored to the top centre of the window (the boss's name, bars and statuses).</summary>
    public PixelRect TopCenter(PixelRect reference) => new(
        (int)Math.Round(_width / 2.0 - (ReferenceWidth / 2.0 - reference.X) * _scale),
        (int)Math.Round(reference.Y * _scale),
        Math.Max(1, (int)Math.Round(reference.Width * _scale)),
        Math.Max(1, (int)Math.Round(reference.Height * _scale)));

    /// <summary>A rectangle anchored to the top-left corner of the window (the mode switch).</summary>
    public PixelRect TopLeft(PixelRect reference) => new(
        (int)Math.Round(reference.X * _scale),
        (int)Math.Round(reference.Y * _scale),
        Math.Max(1, (int)Math.Round(reference.Width * _scale)),
        Math.Max(1, (int)Math.Round(reference.Height * _scale)));

    /// <summary>A rectangle anchored to the bottom-left corner of the window.</summary>
    public PixelRect BottomLeft(PixelRect reference) => new(
        (int)Math.Round(reference.X * _scale),
        _height - (int)Math.Round((ReferenceHeight - reference.Y) * _scale),
        Math.Max(1, (int)Math.Round(reference.Width * _scale)),
        Math.Max(1, (int)Math.Round(reference.Height * _scale)));
}
