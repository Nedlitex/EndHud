using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace EndHud;

/// <summary>
/// The inn strip as drawn: a dark see-through card, one line under another, each dish's or drink's
/// picture beside its name (its name alone when the package has no picture for it). Text only:
/// nothing is drawn over the game's own screens. Drawn at the size for a 1080p game window and
/// scaled with the window (see ComboHintLayout), like the rotation strip.
/// </summary>
internal static class InnStrip
{
    /// <summary>A picture's size beside its name, at 1080p (<see cref="InnStripLook.Normal"/>).</summary>
    internal const double PictureSize = 28;

    /// <summary>
    /// The lines' width at 1080p: a longer line wraps between dishes. Measured so the gourmand panel, drawn larger
    /// (<see cref="InnStripLook.Gourmands"/>), fits from its default place to a 1080p window's edge (see InnStripLayoutTests).
    /// </summary>
    internal const double LineWidth = 520;

    /// <summary>How far in a line starts per step of its indent (a row under its heading), at 1080p.</summary>
    internal const double IndentStep = 16;

    /// <summary>The accent bar down the card's left edge, at 1080p.</summary>
    internal const double AccentBar = 3;

    // Frozen: shared by every strip drawn.
    private static readonly Brush Background = Frozen(Color.FromArgb(0xC0, 0x10, 0x14, 0x18));
    private static readonly Brush TextBrush = Frozen(Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF));
    private static readonly Brush AccentBrush = Frozen(Color.FromRgb(0xE6, 0xC2, 0x29));
    private static readonly Brush NoteBrush = Frozen(Color.FromRgb(0xFF, 0xB8, 0x6B));
    private static readonly Brush DimBrush = Frozen(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF));
    private static readonly Brush RuleBrush = Frozen(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
    private static readonly FontFamily Font = new("Microsoft YaHei UI, Segoe UI");

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    /// <param name="picture">A dish's or drink's picture by id; null when there's none.</param>
    /// <param name="lineWidth">How wide the lines may be, at 1080p: <see cref="LineWidth"/> for the session's strip, a spot's width for setup advice (<see cref="InnAdviceLayout.LineWidth"/>).</param>
    /// <param name="look">Its text and pictures' size: <see cref="InnStripLook.Normal"/>, or <see cref="InnStripLook.Compact"/> where a full panel must fit a small blank of the game's screen.</param>
    public static FrameworkElement Build(InnStripModel model, Func<string, BitmapSource?> picture, double lineWidth, InnStripLook? look = null)
    {
        look ??= InnStripLook.Normal;
        var lines = new StackPanel();
        foreach (var line in model.Lines)
        {
            if (line.Style == InnLineStyle.Heading && lines.Children.Count > 0)
                lines.Children.Add(new Rectangle { Height = 1, Fill = RuleBrush, Margin = new Thickness(0, 8, 0, 4) });
            lines.Children.Add(Line(line, picture, lineWidth, look));
        }
        return new Border
        {
            Background = Background,
            CornerRadius = new CornerRadius(4),
            BorderBrush = AccentBrush,
            BorderThickness = new Thickness(AccentBar, 0, 0, 0),
            Padding = look.Padding,
            Child = lines,
        };
    }

    private static UIElement Line(InnLine line, Func<string, BitmapSource?> picture, double lineWidth, InnStripLook look)
    {
        double FontSize = look.FontSize, PictureSize = look.PictureSize;
        var (brush, size, weight) = line.Style switch
        {
            InnLineStyle.Accent => (AccentBrush, FontSize + 1, FontWeights.Bold),
            InnLineStyle.Heading => (AccentBrush, FontSize, FontWeights.SemiBold),
            InnLineStyle.Subheading => (AccentBrush, FontSize, FontWeights.SemiBold),
            InnLineStyle.Note => (NoteBrush, FontSize - 2, FontWeights.Normal),
            InnLineStyle.Dim => (DimBrush, FontSize - 3, FontWeights.Normal),
            _ => (TextBrush, FontSize, FontWeights.Normal),
        };
        double indent = line.Indent * IndentStep, width = lineWidth - indent;
        TextBlock Text(InnSegment segment) => new()
        {
            Text = segment.Text,
            // A single long sentence (what's missing) wraps within the line.
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = width,
            Foreground = brush,
            FontFamily = Font,
            FontSize = size,
            FontWeight = weight,
            VerticalAlignment = VerticalAlignment.Center,
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 3, ShadowDepth = 0, Opacity = 0.8 },
        };

        // Pieces kept together (a marker and its dish, a name and the space after it) are one unit; the line wraps between units.
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        var segments = line.Segments;
        FrameworkElement? label = null;
        if (line.Hanging && segments.Count > 1)
        {
            // A label ("干员："): the names wrap beside it, never under it.
            // As tall as the first row of names, its words centred like theirs; at the top when the names wrap.
            label = new Border { Height = PictureSize, VerticalAlignment = VerticalAlignment.Top, Child = Text(segments[0]) };
            segments = [.. segments.Skip(1)];
        }
        StackPanel? unit = null;
        foreach (var segment in segments)
        {
            if (unit is null)
            {
                unit = new StackPanel { Orientation = Orientation.Horizontal, MinHeight = PictureSize };
                row.Children.Add(unit);
            }
            if (segment.PictureId is { } id && picture(id) is { } image)
            {
                unit.Children.Add(new Image
                {
                    Source = image,
                    Width = PictureSize,
                    Height = PictureSize,
                    Margin = new Thickness(0, 0, 3, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                });
            }
            unit.Children.Add(Text(segment));
            if (!segment.KeepWithNext) unit = null;
        }
        var margin = new Thickness(indent, look.LineGap, 0, look.LineGap);
        if (label is null)
        {
            row.Margin = margin;
            row.MaxWidth = width;
            return row;
        }
        var labelled = new DockPanel { Margin = margin, MaxWidth = width, LastChildFill = true };
        DockPanel.SetDock(label, Dock.Left);
        labelled.Children.Add(label);
        labelled.Children.Add(row);
        return labelled;
    }
}

/// <summary>How large an inn panel's text and pictures are drawn, at 1080p (scaled with the window like the rest).</summary>
/// <param name="LineGap">Space above and below each line.</param>
internal sealed record InnStripLook(double FontSize, double PictureSize, double LineGap, Thickness Padding)
{
    /// <summary>The session's strip and the chefs' advice.</summary>
    public static readonly InnStripLook Normal = new(16, InnStrip.PictureSize, 2, new Thickness(12, 8, 14, 8));

    /// <summary>
    /// The session's gourmand panel, larger (owner, live 2026-10-20: "老饕's panel's picture is also TOO small for human
    /// to read, make that panel bigger"): 20 px words, 48 px pictures at 1080p.
    /// </summary>
    public static readonly InnStripLook Gourmands = new(20, 48, 3, new Thickness(14, 10, 16, 10));

    /// <summary>The menu's advice: a full menu (eight dishes, eight drinks, their substitutes) in the menu screen's blank column.</summary>
    public static readonly InnStripLook Compact = new(13, 20, 1, new Thickness(10, 6, 10, 6));

    /// <summary>The card's own width around its lines: the accent bar and the padding.</summary>
    public double ChromeWidth => InnStrip.AccentBar + Padding.Left + Padding.Right;
}
