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
/// <param name="Condition">A condition step's own look; null for a skill or an attack.</param>
/// <param name="Badges">Its expected conditions as badges under the circle (<see cref="StripBadge"/>); none for a step without any.</param>
internal sealed record StripStep(BitmapSource? Icon, BitmapSource? Avatar, string Action, string? Note, StripStepState State, bool WithPrevious, StripCondition? Condition = null,
    IReadOnlyList<StripBadge>? Badges = null)
{
    public IReadOnlyList<StripBadge> Badges { get; init; } = Badges ?? [];
}

/// <summary>
/// One condition as the strip shows it: a small badge of the condition's picture and a compact sign, never words
/// (owner, 2026-10-25: "show some condition hint in the rotation? like 连击 icon >= 1, fire icon >= 1, etc? not text,
/// this will make user confuse"). Built by <see cref="StripBadges"/>.
/// </summary>
/// <param name="Picture">The condition's picture (<see cref="ConditionIcon"/>): a status's or buff's icon, a skill's icon, an
/// operator's avatar, a step's icon; null while not loaded or for none: <paramref name="Glyph"/> is drawn instead.</param>
/// <param name="Glyph">What is drawn without a picture.</param>
/// <param name="Value">The comparison and the number, e.g. "≥1", "=4", "&lt;2", "≥90%"; null for a yes/no condition.</param>
/// <param name="Sign">A small mark for conditions that share a skill's icon: running, empowered.</param>
/// <param name="Not">The opposite is asked for (not ready, absent, not running): a slash across the picture.</param>
/// <param name="Holds">It holds in the current reading: bright. Not holding, or unknown: dim.</param>
/// <param name="Or">One of several where any one is enough, after the first: a "/" before it.</param>
internal sealed record StripBadge(BitmapSource? Picture, BadgeGlyph Glyph, string? Value, BadgeSign Sign, bool Not, bool Holds, bool Or = false);

/// <summary>What a badge draws when it has no picture.</summary>
internal enum BadgeGlyph
{
    /// <summary>Its picture (none loaded yet: "!").</summary>
    Picture,

    /// <summary>The skill points: three slanted yellow segments.</summary>
    SkillPoints,

    /// <summary>The phase's steps lit: an amber ring, a lit step's.</summary>
    Steps,

    /// <summary>Nothing to picture (a phase's showings or plays): "!".</summary>
    Other,
}

/// <summary>A mark beside a skill's icon, telling apart conditions that share it.</summary>
internal enum BadgeSign
{
    None,

    /// <summary>The skill is running (a lasting ultimate or battle skill; its time left): a small triangle.</summary>
    Running,

    /// <summary>The battle skill is empowered (战技已强化): a small four-pointed star.</summary>
    Empowered,
}

/// <summary>A condition step on the strip (lit like any step once met: <see cref="StripStepState.Lit"/>).</summary>
/// <param name="SkillPoints">Its first condition is about the skill points: their glyph in the circle (when <see cref="StripStep.Icon"/> is null).</param>
internal sealed record StripCondition(bool SkillPoints);

internal enum StripStepState
{
    /// <summary>Not followed (the editor's preview, the menu's sample, the next phase's preview): every step alike.</summary>
    Plain,

    /// <summary>Seen used this round (a condition step: met).</summary>
    Lit,

    /// <summary>The current step, ready to use: cyan chevrons and ring.</summary>
    Ready,

    /// <summary>The current step, not ready yet: white chevrons and ring, with the reasons under the strip.</summary>
    Waiting,

    /// <summary>Not seen used yet this round.</summary>
    Unlit,

    /// <summary>A basic, heavy or plunge attack: never seen used, so never lit nor waited on.</summary>
    Neutral,
}

/// <summary>
/// What the strip shows: the phase ("充能轴 II"), its steps (with their conditions as badges), a look at the phase
/// played after it, and, with every step lit and the phase not finished yet, its finish condition as badges at the end.
/// </summary>
/// <param name="Finish">The finish condition's badges, shown after the last step while the phase waits for it alone; empty otherwise.</param>
internal sealed record StripView(string Phase, IReadOnlyList<StripStep> Steps, StripNext? Next, IReadOnlyList<StripBadge> Finish);

/// <summary>The first steps of the phase played next, drawn after a divider that names it, dimmed.</summary>
internal sealed record StripNext(string Phase, IReadOnlyList<StripStep> Steps);

/// <summary>
/// The rotation strip (排轴), drawn like the community's rotation charts: a see-through grey band that
/// fades out at both ends, a line through the skill icons, the phase's name large on the left. The
/// current step has stacked chevrons above it: cyan when it's time to use it, white when it isn't
/// yet (the reasons, in words, under the band). Steps seen used are lit (an amber ring on a filled disc),
/// the others dim, attacks (never seen) half dim with a dashed ring. Text has a dark outline so it reads
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

    /// <summary>A lit step's ring and glow: amber, apart from the cyan of "use it now" and the red of the reasons.</summary>
    internal static readonly Color LitColor = Color.FromRgb(0xFF, 0xC2, 0x4A);
    private static readonly Brush Lit = Frozen(LitColor);

    /// <summary>A lit step's disc: filled, a dark warm tone under its white icon.</summary>
    private static readonly Brush LitFill = Frozen(Color.FromArgb(0xE0, 0x4A, 0x36, 0x14));

    /// <summary>A step not seen used yet this round: dimmed, so the lit ones stand out.</summary>
    internal const double UnlitOpacity = 0.45;

    /// <summary>A badge whose condition doesn't hold now, or can't be told: dim, so the ones holding stand out.</summary>
    internal const double DimBadgeOpacity = 0.4;

    /// <summary>A basic, heavy or plunge attack (never seen used): between unlit and lit, its ring dashed.</summary>
    internal const double NeutralOpacity = 0.75;

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

        return new Border { Background = Band, Padding = new Thickness(FadeWidth, 6, FadeWidth, 6), Child = grid };
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
        bool lit = step.State == StripStepState.Lit;
        var accent = step.State == StripStepState.Ready ? Cyan : Text;
        var panel = new StackPanel
        {
            Width = StepWidth,
            Margin = new Thickness(step.WithPrevious ? -Overlap : 0, 0, 0, 0),
            // Lit and current bright; not seen yet dim; an attack (never seen) in between (owner, 2026-10-24).
            Opacity = step.State switch { StripStepState.Unlit => UnlitOpacity, StripStepState.Neutral => NeutralOpacity, _ => 1 },
            Tag = step.State, // found by it in tests
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
            Fill = current ? CurrentFill : lit ? LitFill : StepFill,
            // Cyan: use it now. White: this is the step, but not yet. Amber: seen used (met). Grey: not yet seen; dashed: never seen.
            Stroke = current ? accent : lit ? Lit : Ring,
            StrokeThickness = current || lit ? 3.5 : 2,
            StrokeDashArray = step.State == StripStepState.Neutral ? [3, 2] : null,
            Effect = step.State == StripStepState.Ready ? new DropShadowEffect { Color = CyanColor, BlurRadius = 12, ShadowDepth = 0, Opacity = 0.9 }
                : lit ? new DropShadowEffect { Color = LitColor, BlurRadius = 10, ShadowDepth = 0, Opacity = 0.8 }
                : null,
        });
        if (step.Condition is { } about)
        {
            // The alert pop-up's circle content: the subject's picture, the skill-points glyph, or "!".
            var glyph = AlertPopUp.Icon(step.Icon, about.SkillPoints);
            if (glyph is FrameworkElement element)
            {
                element.HorizontalAlignment = HorizontalAlignment.Center;
                element.VerticalAlignment = VerticalAlignment.Center;
            }
            icon.Children.Add(glyph);
        }
        else if (step.Icon is not null)
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
        var note = new TextBlock
        {
            Text = step.Note ?? " ",
            Foreground = Text,
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Effect = Outline(),
        };
        if (step.Condition is not null)
        {
            // The conditions in words can be long: smaller, two lines at most under the circle, then "…".
            note.FontSize = 15;
            note.TextWrapping = TextWrapping.Wrap;
            note.TextTrimming = TextTrimming.CharacterEllipsis;
            note.TextAlignment = TextAlignment.Center;
            note.MaxWidth = StepWidth;
            note.MaxHeight = 42;
        }
        panel.Children.Add(note);
        return panel;
    }
}
