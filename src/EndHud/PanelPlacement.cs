using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace EndHud;

/// <summary>
/// Where a panel the player drags (the menu, the debug panel) sits: its usual spot plus an offset,
/// kept inside the game window. The offset is saved as a fraction of the window, so it follows the
/// window when it's resized.
/// </summary>
public static class PanelPlacement
{
    /// <summary>
    /// The offset to use for a panel whose spot without any offset is <paramref name="home"/>: the
    /// wanted one, cut so the panel stays inside <paramref name="window"/>. A panel bigger than the
    /// window keeps its top-left corner on screen.
    /// </summary>
    public static Vector Clamp(Rect home, Vector wanted, Size window) => new(
        Fit(wanted.X, home.Left, home.Width, window.Width),
        Fit(wanted.Y, home.Top, home.Height, window.Height));

    private static double Fit(double offset, double start, double length, double room)
    {
        double least = -start, most = room - start - length;
        return Math.Max(least, Math.Min(offset, Math.Max(least, most)));
    }

    public static Vector ToFraction(Vector offset, Size window) =>
        window.Width > 0 && window.Height > 0 ? new(offset.X / window.Width, offset.Y / window.Height) : default;

    public static Vector FromFraction(Vector fraction, Size window) => new(fraction.X * window.Width, fraction.Y * window.Height);

    /// <summary>
    /// Whether a press at <paramref name="source"/> is on something inside <paramref name="panel"/>
    /// that takes clicks itself (a button, check box, box, list, scroll bar): those keep working,
    /// and only the rest of the panel drags it.
    /// </summary>
    public static bool IsOnControl(DependencyObject? source, DependencyObject panel)
    {
        for (var node = source; node is not null && !ReferenceEquals(node, panel); node = Parent(node))
        {
            if (node is ButtonBase or TextBoxBase or ComboBox or Selector or ListBoxItem or ScrollBar or Thumb or Slider or PasswordBox)
                return true;
        }
        return false;
    }

    private static DependencyObject? Parent(DependencyObject node) =>
        (node is Visual ? VisualTreeHelper.GetParent(node) : null) ?? LogicalTreeHelper.GetParent(node);
}
