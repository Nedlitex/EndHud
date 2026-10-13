using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
    /// <summary>The current step, ready to use: bright, with a gold ring.</summary>
    Ready,
    /// <summary>The current step, not ready yet: dim, with the reasons under the strip.</summary>
    Waiting,
    Upcoming,
}

/// <summary>What the strip shows: the phase ("充能轴 II"), its steps, and why the current step isn't ready.</summary>
internal sealed record StripView(string Phase, IReadOnlyList<StripStep> Steps, IReadOnlyList<string> Reasons);

/// <summary>
/// The rotation strip (排轴), drawn like the guides do: a band with the phase's name and its steps as
/// skill icons. The current step has a chevron above it; it's bright with a gold ring when it's
/// time to use it, dim otherwise, with the reasons (in words) under the band. Done steps fade.
/// Drawn at the size for a 1080p game window and scaled with the window (see ComboHintLayout).
/// </summary>
internal static class RotationStrip
{
    private const double IconSize = 56;
    private const double AvatarSize = 22;
    private const double StepWidth = 76;
    private const double Overlap = 30;

    // Frozen: shared by every strip drawn, from any thread.
    private static readonly Brush Band = Frozen(Color.FromArgb(0xB4, 0x14, 0x18, 0x1C));
    private static readonly Brush Gold = Frozen(Color.FromRgb(0xE6, 0xC2, 0x29));
    private static readonly Brush Dim = Frozen(Color.FromArgb(0xC0, 0x7A, 0x7A, 0x7A));
    private static readonly Brush Text = Frozen(Color.FromArgb(0xEE, 0xFF, 0xFF, 0xFF));
    private static readonly Brush Faint = Frozen(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));
    private static readonly Brush Warning = Frozen(Color.FromRgb(0xFF, 0x7A, 0x6B));
    private static readonly Brush StepFill = Frozen(Color.FromArgb(0xCC, 0x22, 0x26, 0x2B));

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

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

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock
        {
            Text = view.Phase,
            Foreground = Text,
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(18, 0, 18, 0),
            MinWidth = 96,
        });
        row.Children.Add(steps);

        // Why the current step isn't ready goes inside the band, readable over any scene.
        var body = new StackPanel { Children = { row } };
        if (view.Reasons.Count > 0)
        {
            body.Children.Add(new TextBlock
            {
                Text = string.Join("　", view.Reasons),
                Foreground = Warning,
                FontSize = 14,
                Margin = new Thickness(18, 2, 0, 0),
            });
        }
        return new Border { Background = Band, CornerRadius = new CornerRadius(6), Padding = new Thickness(0, 6, 12, 6), Child = body };
    }

    private static UIElement Step(StripStep step)
    {
        bool current = step.State is StripStepState.Ready or StripStepState.Waiting;
        var panel = new StackPanel
        {
            Width = StepWidth,
            Margin = new Thickness(step.WithPrevious ? -Overlap : 0, 0, 0, 0),
            Opacity = step.State == StripStepState.Done ? 0.35 : 1,
        };
        panel.Children.Add(new TextBlock
        {
            Text = current ? "⌄" : " ",
            Foreground = step.State == StripStepState.Ready ? Gold : Faint,
            FontSize = 22,
            LineHeight = 16,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        panel.Children.Add(new TextBlock
        {
            Text = step.Action,
            Foreground = current ? Text : Faint,
            FontSize = 13,
            FontWeight = current ? FontWeights.SemiBold : FontWeights.Normal,
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        var icon = new Grid { Width = IconSize, Height = IconSize, Margin = new Thickness(0, 2, 0, 2) };
        icon.Children.Add(new Ellipse
        {
            Fill = StepFill,
            // Gold: use it now. White: this is the step, but not yet. Grey: later.
            Stroke = step.State switch { StripStepState.Ready => Gold, StripStepState.Waiting => Text, _ => Dim },
            StrokeThickness = current ? 3 : 1.5,
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
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        return panel;
    }
}
