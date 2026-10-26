using System.Windows;

namespace EndHud;

/// <summary>
/// The overlay's rotation strip, drawn again only when what it shows changes (<see cref="RotationStripView.Key"/>), and
/// each time against what it drew before, so a condition pulses once, the moment it becomes met, and glows steadily
/// through every redraw after (<see cref="RotationStrip.Build"/>).
/// </summary>
internal sealed class StripRedraw
{
    private readonly DrawnKey _key = new();
    private StripView? _drawn;

    /// <summary>
    /// The strip for <paramref name="view"/> when it must be drawn again (null content: nothing to show); false when what
    /// is on screen already shows it.
    /// </summary>
    public bool Next(StripView? view, out UIElement? content)
    {
        content = null;
        if (!_key.Changed(RotationStripView.Key(view))) return false;
        content = view is null ? null : RotationStrip.Build(view, before: _drawn);
        _drawn = view;
        return true;
    }

    /// <summary>Draw it again on the next <see cref="Next"/>, whatever it shows (the frame's look changed): what is met stays steady.</summary>
    public void Invalidate() => _key.Invalidate();
}
