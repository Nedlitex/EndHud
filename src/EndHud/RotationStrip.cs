using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace EndHud;

/// <summary>A step as the strip draws it.</summary>
/// <param name="Icon">The operator's skill icon (wiki); null while loading or if the wiki has none.</param>
/// <param name="Avatar">The operator's avatar (wiki), drawn small at the icon's corner.</param>
/// <param name="Action">Above the icon: 战技 / 连携技 / 终结技, or the step's own name.</param>
/// <param name="Note">Below the icon, e.g. "飞天".</param>
/// <param name="WithPrevious">Drawn overlapping the step before: used right after it.</param>
internal sealed record StripStep(BitmapSource? Icon, BitmapSource? Avatar, string Action, string? Note, StripStepState State, bool WithPrevious);

internal enum StripStepState
{
    Done,
    /// <summary>The current step, ready to use: cyan chevrons and ring.</summary>
    Ready,
    /// <summary>The current step, not ready yet: white chevrons and ring, with the reasons under the strip.</summary>
    Waiting,
    Upcoming,
}

/// <summary>
/// What the strip shows: the phase ("充能轴 II"), its steps, a look at the phase played after it,
/// and why the current step isn't ready.
/// </summary>
internal sealed record StripView(string Phase, IReadOnlyList<StripStep> Steps, StripNext? Next, IReadOnlyList<string> Reasons);

/// <summary>The first steps of the phase played next, drawn after a divider that names it, dimmed.</summary>
internal sealed record StripNext(string Phase, IReadOnlyList<StripStep> Steps);

/// <summary>
/// The rotation strip (排轴), drawn like the community's rotation charts: a see-through grey band that
/// fades out at both ends, a line through the skill icons, the phase's name large on the left. The
/// current step has stacked chevrons above it: cyan when it's time to use it, white when it isn't
/// yet (the reasons, in words, under the band). Done steps fade. Text has a dark outline so it reads
/// over any scene. Drawn at the size for a 1080p game window and scaled with the window (see
/// ComboHintLayout).
/// </summary>
internal static class RotationStrip
{
    private const double IconSize = 80;
    private const double AvatarSize = 30;
    private const double StepWidth = 104;
    private const double Overlap = 42;
    private const double FadeWidth = 90;

    // Frozen: shared by every strip drawn, from any thread.
    internal static readonly Color CyanColor = Color.FromRgb(0x3C, 0xD2, 0xF0);
    private static readonly Brush Cyan = Frozen(CyanColor);
    private static readonly Brush Ring = Frozen(Color.FromArgb(0x99, 0xD8, 0xD8, 0xD8));
    private static readonly Brush Text = Frozen(Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF));
    private static readonly Brush Label = Frozen(Color.FromArgb(0xCC, 0xD0, 0xD0, 0xD0));
    private static readonly Brush Line = Frozen(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
    internal static readonly Color WarningColor = Color.FromRgb(0xFF, 0x8A, 0x7A);
    private static readonly Brush Warning = Frozen(WarningColor);
    private static readonly Brush StepFill = Frozen(Color.FromArgb(0x8C, 0x26, 0x28, 0x2C));
    private static readonly Brush CurrentFill = Frozen(Color.FromArgb(0xE0, 0x18, 0x1A, 0x1E));
    private static readonly Brush Band = FrozenBand();

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    /// <summary>See-through grey, fading out at the left and right ends.</summary>
    private static Brush FrozenBand()
    {
        var grey = Color.FromArgb(0x9C, 0x50, 0x54, 0x5A);
        var clear = Color.FromArgb(0x00, 0x50, 0x54, 0x5A);
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0), MappingMode = BrushMappingMode.RelativeToBoundingBox };
        brush.GradientStops.Add(new GradientStop(clear, 0));
        brush.GradientStops.Add(new GradientStop(grey, 0.08));
        brush.GradientStops.Add(new GradientStop(grey, 0.92));
        brush.GradientStops.Add(new GradientStop(clear, 1));
        brush.Freeze();
        return brush;
    }

    /// <summary>A dark outline around text, so it reads over bright scenes through the see-through band.</summary>
    private static Effect Outline() => new DropShadowEffect { Color = Colors.Black, BlurRadius = 4, ShadowDepth = 0, Opacity = 0.85 };

    public static UIElement Build(StripView view)
    {
        var steps = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var step in view.Steps)
        {
            var element = Step(step);
            // The current step on top of a step overlapping it.
            if (step.State is StripStepState.Ready or StripStepState.Waiting) Panel.SetZIndex(element, 1);
            steps.Children.Add(element);
        }
        if (view.Next is { Steps.Count: > 0 } next)
        {
            steps.Children.Add(NextDivider(next.Phase));
            foreach (var step in next.Steps)
            {
                var element = Step(step);
                element.Opacity = NextOpacity;
                steps.Children.Add(element);
            }
        }

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock
        {
            Text = view.Phase,
            Foreground = Text,
            FontSize = 40,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 28, 0),
            Effect = Outline(),
        });
        row.Children.Add(steps);

        // The line runs through the icons' centres, behind them.
        var grid = new Grid();
        grid.Children.Add(new Rectangle { Height = 1, Fill = Line, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, LineY, 0, 0) });
        grid.Children.Add(row);

        var body = new StackPanel { Children = { grid } };
        if (view.Reasons.Count > 0)
        {
            body.Children.Add(new TextBlock
            {
                Text = string.Join("　", view.Reasons),
                Foreground = Warning,
                FontSize = 19,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 0),
                Effect = Outline(),
            });
        }
        return new Border { Background = Band, Padding = new Thickness(FadeWidth, 6, FadeWidth, 6), Child = body };
    }

    // Chevrons (26) + label (24) + the icon's top margin (2) + half the icon.
    private const double LineY = 26 + 24 + 2 + IconSize / 2;

    /// <summary>The next phase's steps are a preview, not part of this phase: dimmed.</summary>
    internal const double NextOpacity = 0.5;

    /// <summary>
    /// Where this phase ends: a vertical bar, and "接下来" over the next phase's name, so its steps
    /// aren't taken for this phase's (two phases may share a name; the numeral tells them apart).
    /// </summary>
    private static UIElement NextDivider(string phase)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 6, 0), VerticalAlignment = VerticalAlignment.Top };
        panel.Children.Add(new Rectangle { Width = 2, Height = IconSize, Fill = Ring, Margin = new Thickness(0, LineY - IconSize / 2, 8, 0), VerticalAlignment = VerticalAlignment.Top });
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, LineY - 26, 0, 0) };
        words.Children.Add(new TextBlock { Text = Loc.Get("Rotation.Next"), Foreground = Label, FontSize = 16, Effect = Outline() });
        words.Children.Add(new TextBlock { Text = phase, Foreground = Text, FontSize = 18, Effect = Outline() });
        panel.Children.Add(words);
        return panel;
    }

    /// <summary>Two stacked chevrons pointing down at the current step, as the guides draw it; empty space otherwise.</summary>
    private static UIElement Chevrons(Brush? brush)
    {
        var area = new Canvas { Width = 28, Height = 26, HorizontalAlignment = HorizontalAlignment.Center };
        if (brush is null) return area;
        foreach (double top in new[] { 3.0, 12.0 })
        {
            area.Children.Add(new Polyline
            {
                Points = [new Point(4, top), new Point(14, top + 8), new Point(24, top)],
                Stroke = brush,
                StrokeThickness = 3.5,
                StrokeLineJoin = PenLineJoin.Miter,
                Effect = Outline(),
            });
        }
        return area;
    }

    private static UIElement Step(StripStep step)
    {
        bool current = step.State is StripStepState.Ready or StripStepState.Waiting;
        var accent = step.State == StripStepState.Ready ? Cyan : Text;
        var panel = new StackPanel
        {
            Width = StepWidth,
            Margin = new Thickness(step.WithPrevious ? -Overlap : 0, 0, 0, 0),
            Opacity = step.State == StripStepState.Done ? 0.45 : 1,
        };
        panel.Children.Add(Chevrons(current ? accent : null));
        panel.Children.Add(new TextBlock
        {
            Text = step.Action,
            Foreground = current ? Text : Label,
            FontSize = 18,
            FontWeight = current ? FontWeights.Bold : FontWeights.Normal,
            Height = 24,
            HorizontalAlignment = HorizontalAlignment.Center,
            Effect = Outline(),
        });

        var icon = new Grid { Width = IconSize, Height = IconSize, Margin = new Thickness(0, 2, 0, 2) };
        icon.Children.Add(new Ellipse
        {
            Fill = current ? CurrentFill : StepFill,
            // Cyan: use it now. White: this is the step, but not yet. Grey: later.
            Stroke = current ? accent : Ring,
            StrokeThickness = current ? 3.5 : 2,
            Effect = step.State == StripStepState.Ready ? new DropShadowEffect { Color = CyanColor, BlurRadius = 12, ShadowDepth = 0, Opacity = 0.9 } : null,
        });
        if (step.Icon is not null)
        {
            icon.Children.Add(new Image
            {
                Source = step.Icon,
                Width = IconSize * 0.7,
                Height = IconSize * 0.7,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
        if (step.Avatar is not null)
        {
            icon.Children.Add(new Ellipse
            {
                Width = AvatarSize,
                Height = AvatarSize,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Fill = new ImageBrush(step.Avatar) { Stretch = Stretch.UniformToFill },
                Stroke = Text,
                StrokeThickness = 1,
            });
        }
        panel.Children.Add(icon);
        panel.Children.Add(new TextBlock
        {
            Text = step.Note ?? " ",
            Foreground = Text,
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Effect = Outline(),
        });
        return panel;
    }
}
