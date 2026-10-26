using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
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
internal sealed record StripStep(ImageSource? Icon, BitmapSource? Avatar, string Action, string? Note, StripStepState State, bool WithPrevious, StripCondition? Condition = null,
    IReadOnlyList<StripBadge>? Badges = null)
{
    public IReadOnlyList<StripBadge> Badges { get; init; } = Badges ?? [];

    /// <summary>A battle skill's key (<see cref="StripKey"/>), in a chip above the circle; null for any other step, or an operator not in the recognised squad.</summary>
    public StripKey? Key { get; init; }
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
internal sealed record StripBadge(ImageSource? Picture, BadgeGlyph Glyph, string? Value, BadgeSign Sign, bool Not, bool Holds, bool Or = false);

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

    /// <summary>The battle skill is empowered: a small four-pointed star.</summary>
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

    /// <summary>The current step, not ready yet: white chevrons and ring (its badges say which condition is missing).</summary>
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
/// yet. A step's conditions are small badges right under its circle (a picture and "≥1"-style signs, never words;
/// glowing gold while they hold, pulsing the moment they come true, dim while not), kept inside the step's own column so they never reach a
/// neighbour's; the finish condition's, once every step is lit, follow the last step. Steps seen used are lit (an amber ring on a filled disc),
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

    /// <summary>A lit step's ring and glow: amber, apart from the cyan of "use it now" and the red of a badge's "not" slash.</summary>
    internal static readonly Color LitColor = Color.FromRgb(0xFF, 0xC2, 0x4A);
    private static readonly Brush Lit = Frozen(LitColor);

    /// <summary>A lit step's disc: filled, a dark warm tone under its white icon.</summary>
    private static readonly Brush LitFill = Frozen(Color.FromArgb(0xE0, 0x4A, 0x36, 0x14));

    /// <summary>
    /// A condition met (a badge holding, a condition step lit) glows round itself in this warm gold, so it reads at a glance
    /// in a fight (owner, 2026-10-25: "when condition is met, glow more obviously to notify user, right now it is hard to
    /// tell"): bright over a dark scene, and over a light one its colour (and the dark pill or disc it surrounds) still
    /// stands apart. Drawn as an effect, outside the layout: nothing moves when a condition comes true.
    /// </summary>
    internal static readonly Color MetGlowColor = Color.FromRgb(0xFF, 0xC4, 0x1A);

    /// <summary>The glow at the top of each pulse: nearly white gold, a flash.</summary>
    internal static readonly Color MetFlashColor = Color.FromRgb(0xFF, 0xF2, 0xB8);

    /// <summary>A met badge's or condition step's edge: brighter than a lit step's amber.</summary>
    internal static readonly Color MetEdgeColor = Color.FromRgb(0xFF, 0xE0, 0x78);
    private static readonly Brush MetEdge = Frozen(MetEdgeColor);

    /// <summary>A holding badge's pill: a warm fill, brighter than the dark one of a badge not holding.</summary>
    private static readonly Brush MetBadgeFill = Frozen(Color.FromArgb(0xF0, 0x6A, 0x4A, 0x0C));

    /// <summary>A met condition step's disc: a warmer, brighter fill than a lit skill step's.</summary>
    private static readonly Brush MetStepFill = Frozen(Color.FromArgb(0xF0, 0x74, 0x52, 0x10));

    /// <summary>The glow's blur round a holding badge, steady and at the top of a pulse (units at 1080p).</summary>
    internal const double BadgeGlow = 14, BadgeGlowPeak = 30;

    /// <summary>The glow's blur round a met condition step's circle, steady and at the top of a pulse.</summary>
    internal const double StepGlow = 24, StepGlowPeak = 46;

    /// <summary>The wider glow round those (<see cref="Halo"/>), steady and at the top of a pulse.</summary>
    internal const double BadgeHalo = 26, BadgeHaloPeak = 46, StepHalo = 36, StepHaloPeak = 64;

    /// <summary>
    /// The moment a condition becomes met, its glow pulses: <see cref="PulseCount"/> flashes, each up and back in twice
    /// this, about a second in all, then the steady glow. Only on the change: a redraw with it still met doesn't pulse
    /// again, and nothing moves while it isn't met.
    /// </summary>
    internal static readonly TimeSpan PulseHalf = TimeSpan.FromMilliseconds(170);

    internal const int PulseCount = 3;

    /// <summary>The pulse's frame rate: a glow's blur and colour need no more, and the overlay's window is redrawn by the CPU.</summary>
    private const int PulseFrameRate = 30;

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

    /// <param name="before">What the strip showed until now (null: nothing): a condition met in <paramref name="view"/> but not
    /// in it pulses (<see cref="PulseHalf"/>), one met in both glows steadily.</param>
    public static UIElement Build(StripView view, StripView? before = null)
    {
        var fresh = Met(view);
        fresh.ExceptWith(Met(before));
        var steps = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        // With any badge in the phase every step keeps a row for them, so the notes stay in line.
        bool badgeRow = view.Steps.Any(s => s.Badges.Count > 0);
        for (int i = 0; i < view.Steps.Count; i++)
        {
            var step = view.Steps[i];
            var element = Step(step, Column(view.Steps, i), badgeRow, StepId(view, i), fresh);
            // The current step on top of a step overlapping it.
            if (step.State is StripStepState.Ready or StripStepState.Waiting) Panel.SetZIndex(element, 1);
            steps.Children.Add(element);
        }
        if (view.Finish.Count > 0)
        {
            // What the round still waits for, after the last step, on the line: badges only.
            var finish = new WrapPanel { MaxWidth = StepWidth, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(10, LineY - BadgeHeight / 2, 0, 0) };
            var ids = BadgeIds(FinishId(view), view.Finish);
            for (int j = 0; j < view.Finish.Count; j++) finish.Children.Add(Badge(view.Finish[j], StepWidth, fresh.Contains(ids[j])));
            steps.Children.Add(finish);
        }
        if (view.Next is { Steps.Count: > 0 } next)
        {
            steps.Children.Add(NextDivider(next.Phase));
            bool nextRow = next.Steps.Any(s => s.Badges.Count > 0);
            for (int i = 0; i < next.Steps.Count; i++)
            {
                // A preview: it glows where met, but never pulses.
                var element = Step(next.Steps[i], Column(next.Steps, i), nextRow, null, fresh);
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
            // Centred on the line through the icons, wherever the badges and notes under them end.
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, LineY - 27, 28, 0),
            Effect = Outline(),
        });
        row.Children.Add(steps);

        // The line runs through the icons' centres, behind them.
        var grid = new Grid();
        grid.Children.Add(new Rectangle { Height = 1, Fill = Line, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, LineY, 0, 0) });
        grid.Children.Add(row);

        return new Border { Background = Band, Padding = new Thickness(FadeWidth, 6, FadeWidth, 6), Child = grid };
    }

    /// <summary>
    /// What is met in a view, by where it is: each condition step lit, and each badge holding, by its step (or the finish),
    /// its look and its place among the step's badges alike. A badge put before the others (the step's own ability) doesn't
    /// shift the rest, and a picture loading doesn't make it another badge.
    /// </summary>
    internal static HashSet<string> Met(StripView? view)
    {
        var met = new HashSet<string>(StringComparer.Ordinal);
        if (view is null) return met;
        for (int i = 0; i < view.Steps.Count; i++)
        {
            var step = view.Steps[i];
            if (step.Condition is not null && step.State == StripStepState.Lit) met.Add(StepId(view, i));
            var ids = BadgeIds(StepId(view, i), step.Badges);
            for (int j = 0; j < ids.Count; j++) if (step.Badges[j].Holds) met.Add(ids[j]);
        }
        var finish = BadgeIds(FinishId(view), view.Finish);
        for (int j = 0; j < finish.Count; j++) if (view.Finish[j].Holds) met.Add(finish[j]);
        return met;
    }

    private static string StepId(StripView view, int i) => $"{view.Phase}|{i}";

    private static string FinishId(StripView view) => $"{view.Phase}|finish";

    private static List<string> BadgeIds(string owner, IReadOnlyList<StripBadge> badges)
    {
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var ids = new List<string>(badges.Count);
        foreach (var b in badges)
        {
            string look = $"{b.Glyph}{b.Value}{b.Sign}{b.Not}{b.Or}";
            int n = seen.GetValueOrDefault(look);
            seen[look] = n + 1;
            ids.Add($"{owner}|{look}#{n}");
        }
        return ids;
    }

    /// <summary>
    /// A second, wider glow round something met that already glows: one shadow round a pill 24 units high is faint, the
    /// glow of the glow carries it out to where it reads over a busy scene. A wrapper of the same size, so the layout is
    /// the same.
    /// </summary>
    private static Grid Halo(FrameworkElement met, double steady, double peak, bool pulse)
    {
        var halo = new Grid { Effect = MetGlow(steady, peak, pulse), HorizontalAlignment = met.HorizontalAlignment, VerticalAlignment = met.VerticalAlignment };
        halo.Children.Add(met);
        return halo;
    }

    /// <summary>The glow round something met, steady, or pulsing first when it has just become met.</summary>
    private static DropShadowEffect MetGlow(double steady, double peak, bool pulse)
    {
        var glow = new DropShadowEffect { Color = MetGlowColor, BlurRadius = steady, ShadowDepth = 0, Opacity = 1 };
        if (!pulse) return glow;
        // Up and back PulseCount times, then the steady glow: the effect's own properties, no redraw of the strip.
        var blur = new DoubleAnimation(steady, peak, PulseHalf) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(PulseCount), FillBehavior = FillBehavior.Stop };
        var colour = new ColorAnimation(MetGlowColor, MetFlashColor, PulseHalf) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(PulseCount), FillBehavior = FillBehavior.Stop };
        Timeline.SetDesiredFrameRate(blur, PulseFrameRate);
        Timeline.SetDesiredFrameRate(colour, PulseFrameRate);
        glow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, blur);
        glow.BeginAnimation(DropShadowEffect.ColorProperty, colour);
        return glow;
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

    /// <summary>
    /// The width under a step its badges may use: the whole step, or, beside a step drawn overlapping it (used together),
    /// only up to halfway to that step's circle, so a step's badges never reach its neighbour's.
    /// </summary>
    private static double Column(IReadOnlyList<StripStep> steps, int i) =>
        steps[i].WithPrevious || (i + 1 < steps.Count && steps[i + 1].WithPrevious) ? StepWidth - Overlap : StepWidth;

    /// <param name="id">Where the step is (<see cref="Met"/>); null for the next phase's preview, which never pulses.</param>
    /// <param name="fresh">What has just become met: it pulses.</param>
    private static FrameworkElement Step(StripStep step, double column, bool badgeRow, string? id, HashSet<string> fresh)
    {
        bool current = step.State is StripStepState.Ready or StripStepState.Waiting;
        bool lit = step.State == StripStepState.Lit;
        // A condition step lit is a condition met: it glows, like a badge holding.
        bool met = lit && step.Condition is not null;
        var accent = step.State == StripStepState.Ready ? Cyan : Text;
        // Lit and current bright; not seen yet dim; an attack (never seen) in between (owner, 2026-10-24). The badges
        // aren't dimmed with the step: theirs is their condition's own (a later step's condition is live too).
        double opacity = step.State switch { StripStepState.Unlit => UnlitOpacity, StripStepState.Neutral => NeutralOpacity, _ => 1 };
        var outer = new StackPanel
        {
            Width = StepWidth,
            Margin = new Thickness(step.WithPrevious ? -Overlap : 0, 0, 0, 0),
            Tag = step, // found by it in tests
        };
        var panel = new StackPanel
        {
            Opacity = opacity,
            Tag = step.State, // found by it in tests
        };
        outer.Children.Add(panel);
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
        bool pulse = met && id is not null && fresh.Contains(id);
        var circle = new Ellipse
        {
            Fill = current ? CurrentFill : met ? MetStepFill : lit ? LitFill : StepFill,
            // Cyan: use it now. White: this is the step, but not yet. Gold: a condition met. Amber: seen used. Grey: not yet seen; dashed: never seen.
            Stroke = current ? accent : met ? MetEdge : lit ? Lit : Ring,
            StrokeThickness = current || lit ? 3.5 : 2,
            StrokeDashArray = step.State == StripStepState.Neutral ? [3, 2] : null,
            Effect = step.State == StripStepState.Ready ? new DropShadowEffect { Color = CyanColor, BlurRadius = 12, ShadowDepth = 0, Opacity = 0.9 }
                : met ? MetGlow(StepGlow, StepGlowPeak, pulse)
                : lit ? new DropShadowEffect { Color = LitColor, BlurRadius = 10, ShadowDepth = 0, Opacity = 0.8 }
                : null,
        };
        icon.Children.Add(met ? Halo(circle, StepHalo, StepHaloPeak, pulse) : circle);
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
        // A battle skill's key, on top of the circle, under the label (never over it): drawn last, over the ring and the icon.
        if (step.Key is { } key) icon.Children.Add(KeyChip(key));
        panel.Children.Add(icon);

        if (badgeRow)
        {
            // Right under the circle, inside the step's column: they wrap to another row, and one too wide shrinks.
            var badges = new WrapPanel { MaxWidth = column, MinHeight = BadgeHeight + 2 * BadgeMargin, HorizontalAlignment = HorizontalAlignment.Center };
            var ids = BadgeIds(id ?? "", step.Badges);
            for (int j = 0; j < step.Badges.Count; j++) badges.Children.Add(Badge(step.Badges[j], column, id is not null && fresh.Contains(ids[j])));
            outer.Children.Add(badges);
        }
        outer.Children.Add(new TextBlock
        {
            Text = step.Note ?? " ",
            Foreground = Text,
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Opacity = opacity,
            Effect = Outline(),
        });
        return outer;
    }

    /// <summary>The key chip's size at 1080p: a small square, as the game's key hint boxes under its skill buttons.</summary>
    internal const double ChipSize = 18;

    private static readonly Brush ChipFill = Frozen(Color.FromArgb(0xE6, 0x1C, 0x1E, 0x22));
    private static readonly Brush ChipEdge = Frozen(Color.FromArgb(0xB0, 0xC8, 0xC8, 0xC8));
    private static readonly Brush ChipInk = Frozen(Color.FromRgb(0xF2, 0xF2, 0xF2));

    /// <summary>
    /// A battle skill's key, styled after the game's key hint boxes: a small dark rounded square with a light digit (1-4,
    /// the operator's squad slot), or with a controller the face button in the slot's direction of the skill diamond, drawn
    /// as a shape (□ △ ○ ×). Centred on the circle's top, just under the label: the label's row ends where the circle begins
    /// (the icon's top margin), so the chip never reaches it, and a step drawn overlapping its neighbour keeps its chip over
    /// its own circle, clear of the neighbour's.
    /// </summary>
    private static FrameworkElement KeyChip(StripKey key)
    {
        UIElement mark = key.Controller ? FaceGlyph(key.Button) : new TextBlock
        {
            Text = key.Slot.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Foreground = ChipInk,
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, -1, 0, 0),
        };
        return new Border
        {
            Width = ChipSize,
            Height = ChipSize,
            CornerRadius = new CornerRadius(3),
            Background = ChipFill,
            BorderBrush = ChipEdge,
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            // Its top 1 under the label's row (the icon sits 2 under it).
            Margin = new Thickness(0, -1, 0, 0),
            Child = mark,
            Tag = key, // found by it in tests
        };
    }

    /// <summary>A face button's symbol in a 10-unit box, light strokes: □ left, △ top, ○ right, × bottom.</summary>
    private static UIElement FaceGlyph(FaceButton button)
    {
        const double size = 10, stroke = 1.6;
        Shape shape = button switch
        {
            FaceButton.Square => new Rectangle { Width = size - 1, Height = size - 1 },
            FaceButton.Triangle => new Polygon { Points = [new Point(size / 2, 0.5), new Point(size - 0.3, size - 1), new Point(0.3, size - 1)], Width = size, Height = size },
            FaceButton.Circle => new Ellipse { Width = size, Height = size },
            _ => new Path { Data = Geometry.Parse("M 1,1 L 9,9 M 9,1 L 1,9"), Width = size, Height = size, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round },
        };
        shape.Stroke = ChipInk;
        shape.StrokeThickness = stroke;
        shape.HorizontalAlignment = HorizontalAlignment.Center;
        shape.VerticalAlignment = VerticalAlignment.Center;
        return shape;
    }

    /// <summary>A badge's height at 1080p, and its picture's.</summary>
    private const double BadgeHeight = 24, BadgePicture = 20, BadgeMargin = 1;

    private static readonly Brush BadgeFill = Frozen(Color.FromArgb(0xD0, 0x18, 0x1A, 0x1E));

    /// <summary>
    /// One condition's badge: a dark pill with its picture (the shared icon rule: <see cref="AlertPopUp.Icon"/>'s picture,
    /// skill-point glyph or "!"; an amber ring for steps lit), a slash across it for "not", a small triangle (running) or
    /// star (empowered), and the comparison with its number. Gold-edged, warm and glowing while it holds, grey-edged and dim
    /// while not. After a "/" when it is one of several where any is enough.
    /// </summary>
    /// <param name="pulse">It has just come true: its glow pulses first.</param>
    private static FrameworkElement Badge(StripBadge badge, double column, bool pulse)
    {
        var picture = new Grid { Width = BadgePicture, Height = BadgePicture, VerticalAlignment = VerticalAlignment.Center };
        picture.Children.Add(badge.Glyph switch
        {
            BadgeGlyph.Steps when badge.Picture is null => new Ellipse { Width = 16, Height = 16, Stroke = Lit, StrokeThickness = 3 },
            _ => new Viewbox
            {
                Child = AlertPopUp.Icon(badge.Picture, badge.Glyph == BadgeGlyph.SkillPoints),
                Width = BadgePicture,
                Height = BadgePicture,
            },
        });
        if (badge.Not)
        {
            picture.Children.Add(new Line
            {
                X1 = BadgePicture - 2, Y1 = 2, X2 = 2, Y2 = BadgePicture - 2,
                Stroke = Warning,
                StrokeThickness = 3,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            });
        }
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(picture);
        if (badge.Sign != BadgeSign.None) content.Children.Add(Sign(badge.Sign));
        if (badge.Value is { } value)
        {
            content.Children.Add(new TextBlock
            {
                Text = value,
                Foreground = Text,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(1, 0, 0, 1),
                Effect = Outline(),
            });
        }
        var pill = new Border
        {
            Height = BadgeHeight,
            CornerRadius = new CornerRadius(BadgeHeight / 2),
            // Holding: a warmer fill, a bright gold edge and a glow round it (outside the layout).
            Background = badge.Holds ? MetBadgeFill : BadgeFill,
            BorderBrush = badge.Holds ? MetEdge : Ring,
            BorderThickness = new Thickness(1.5),
            Padding = new Thickness(1, 0, badge.Value is null && badge.Sign == BadgeSign.None ? 1 : 5, 0),
            Child = content,
            Effect = badge.Holds ? MetGlow(BadgeGlow, BadgeGlowPeak, pulse) : null,
        };
        var item = new StackPanel { Orientation = Orientation.Horizontal };
        if (badge.Or)
            item.Children.Add(new TextBlock { Text = "/", Foreground = Text, FontSize = 17, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(1, 0, 2, 0), Effect = Outline() });
        item.Children.Add(badge.Holds ? Halo(pill, BadgeHalo, BadgeHaloPeak, pulse) : pill);
        return new Viewbox
        {
            // Too wide for the step's column (a long number, an "or"): smaller, never past it.
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
            MaxWidth = column - 2 * BadgeMargin,
            Margin = new Thickness(BadgeMargin),
            Child = item,
            Opacity = badge.Holds ? 1 : DimBadgeOpacity,
            Tag = badge, // found by it in tests
        };
    }

    /// <summary>Running: a small triangle, as a play sign. Empowered: a small four-pointed star.</summary>
    private static UIElement Sign(BadgeSign sign)
    {
        var shape = new Polygon
        {
            Fill = sign == BadgeSign.Empowered ? Lit : Text,
            Points = sign == BadgeSign.Empowered
                ? [new Point(6, 0), new Point(7.6, 4.4), new Point(12, 6), new Point(7.6, 7.6), new Point(6, 12), new Point(4.4, 7.6), new Point(0, 6), new Point(4.4, 4.4)]
                : [new Point(1, 1), new Point(10, 6), new Point(1, 11)],
            Width = 12,
            Height = 12,
            Margin = new Thickness(2, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        return shape;
    }
}
