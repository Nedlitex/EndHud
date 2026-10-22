using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace EndHud;

/// <summary>
/// One alert pop-up: the combo-ready banner's mirror image (owner, 2026-10-22). A circle holding the
/// rule's subject (the skill points, a boss status's icon, an operator's avatar, or "!") sits on the
/// right, and a slanted panel with the text slides out of it towards the screen's centre (leftwards).
/// Its edge is orange (<see cref="ComboBanner.AlertEdge"/>), apart from the combo banners' gold.
/// </summary>
internal static class AlertPopUp
{
    /// <summary>The circle's diameter: the combo banner's height.</summary>
    public const double CircleSize = ComboBanner.Height;

    /// <summary>The icon inside the circle.</summary>
    public const double IconSize = 46;

    /// <summary>The panel's widest text before it wraps (two lines at most).</summary>
    public const double TextMaxWidth = 380;

    private const double PanelHeight = 58, RingWidth = 3, Tuck = CircleSize / 2;
    private const double SlideDistance = 120;
    private static readonly TimeSpan PopTime = TimeSpan.FromMilliseconds(220), SlideTime = TimeSpan.FromMilliseconds(320),
        SlideStart = TimeSpan.FromMilliseconds(120), OutTime = TimeSpan.FromMilliseconds(200), GapTime = TimeSpan.FromMilliseconds(220);
    private static readonly IEasingFunction EaseOut = Frozen(new CubicEase { EasingMode = EasingMode.EaseOut });
    private static readonly IEasingFunction EaseIn = Frozen(new CubicEase { EasingMode = EasingMode.EaseIn });
    private static readonly FontFamily Font = new("Microsoft YaHei UI, Segoe UI");
    private static readonly Brush TextBrush = Frozen(new SolidColorBrush(Color.FromArgb(0xEE, 0xFF, 0xFF, 0xFF)));
    private static readonly Brush CircleFill = Frozen(new SolidColorBrush(Color.FromArgb(0xE6, 0x10, 0x14, 0x18)));

    /// <summary>What goes in the circle.</summary>
    public static UIElement Icon(BitmapSource? picture, bool skillPoints)
    {
        if (picture is not null)
            return new Image { Source = picture, Width = IconSize, Height = IconSize, Clip = new EllipseGeometry(new Point(IconSize / 2, IconSize / 2), IconSize / 2, IconSize / 2) };
        if (skillPoints) return SkillPointGlyph();
        return new TextBlock
        {
            Text = "!", FontSize = 38, FontWeight = FontWeights.Bold, FontFamily = Font, Foreground = ComboBanner.AlertEdge,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
    }

    /// <summary>The skill points: three slanted yellow segments, as the game's skill-point bar draws them full.</summary>
    private static UIElement SkillPointGlyph()
    {
        var canvas = new Canvas { Width = IconSize, Height = IconSize };
        var fill = Frozen(new SolidColorBrush(Color.FromRgb(0xF2, 0xD0, 0x3A)));
        for (int i = 0; i < 3; i++)
        {
            double x = 4 + i * 13;
            canvas.Children.Add(new Polygon
            {
                Points = [new Point(x + 4, 14), new Point(x + 13, 14), new Point(x + 9, 32), new Point(x, 32)],
                Fill = fill,
            });
        }
        return canvas;
    }

    /// <summary>The pop-up: the text panel, then the circle on its right, drawn over the panel's tucked-in end.</summary>
    public static Grid Create(UIElement icon, string text)
    {
        var words = new TextBlock
        {
            Text = text, FontSize = 20, FontWeight = FontWeights.Bold, Foreground = TextBrush, FontFamily = Font,
            TextWrapping = TextWrapping.Wrap, MaxWidth = TextMaxWidth, MaxHeight = 56, TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            // The panel is slanted like the game's banners; its words are slanted back upright.
            RenderTransform = new SkewTransform(-12, 0),
        };
        var panel = new Border
        {
            Height = PanelHeight,
            MinWidth = 160,
            VerticalAlignment = VerticalAlignment.Center,
            // Opaque at the circle, fading towards the screen's centre: the combo banner's gradient, mirrored.
            Background = new LinearGradientBrush(Color.FromArgb(0x30, 0x10, 0x14, 0x18), Color.FromArgb(0xE6, 0x10, 0x14, 0x18), 0),
            BorderBrush = ComboBanner.AlertEdge,
            BorderThickness = new Thickness(0, 0, 0, 2),
            Padding = new Thickness(24, 0, Tuck + 14, 0),
            Margin = new Thickness(0, 0, -Tuck, 0),
            RenderTransformOrigin = new Point(1, 0.5),
            RenderTransform = new TransformGroup { Children = { new SkewTransform(12, 0), new TranslateTransform() } },
            Child = words,
        };
        var circle = new Grid
        {
            Width = CircleSize,
            Height = CircleSize,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(1, 1),
            Children =
            {
                new Ellipse { Fill = CircleFill, Stroke = ComboBanner.AlertEdge, StrokeThickness = RingWidth },
                new Border { Child = icon, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            },
        };
        Grid.SetColumn(circle, 1);
        Panel.SetZIndex(circle, 1);
        return new Grid
        {
            Height = CircleSize,
            HorizontalAlignment = HorizontalAlignment.Right,
            ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition { Width = GridLength.Auto } },
            Children = { panel, circle },
        };
    }

    private static Border PanelOf(Grid popUp) => popUp.Children.OfType<Border>().First();

    private static Grid CircleOf(Grid popUp) => popUp.Children.OfType<Grid>().First();

    private static TranslateTransform Slide(Grid popUp) => ((TransformGroup)PanelOf(popUp).RenderTransform).Children.OfType<TranslateTransform>().Single();

    /// <summary>The pop-up's space in the stack (opens and closes with it, as the combo banners' do).</summary>
    public static Grid Holder(Grid popUp, object? tag = null) => new() { Tag = tag, Height = 0, HorizontalAlignment = HorizontalAlignment.Right, Children = { popUp } };

    /// <summary>In: the space opens and the circle pops; then the panel slides out of it, leftwards.</summary>
    public static void SlideIn(Grid holder, Grid popUp)
    {
        holder.BeginAnimation(FrameworkElement.HeightProperty, new DoubleAnimation(0, CircleSize + ComboBanner.Gap, GapTime) { EasingFunction = EaseOut });
        var circle = CircleOf(popUp);
        var pop = new DoubleAnimation(0.6, 1, PopTime) { EasingFunction = EaseOut };
        ((ScaleTransform)circle.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        ((ScaleTransform)circle.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        circle.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, PopTime));
        var panel = PanelOf(popUp);
        panel.Opacity = 0;
        Slide(popUp).BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(SlideDistance, 0, SlideTime) { BeginTime = SlideStart, EasingFunction = EaseOut });
        panel.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, SlideTime) { BeginTime = SlideStart });
    }

    /// <summary>Out: the panel slides back into the circle and both fade, then the space closes.</summary>
    public static void SlideOut(Grid holder, Grid popUp, Action removed)
    {
        Slide(popUp).BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(SlideDistance / 2, OutTime) { EasingFunction = EaseIn });
        PanelOf(popUp).BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, OutTime));
        CircleOf(popUp).BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, OutTime));
        var close = new DoubleAnimation(0, GapTime) { BeginTime = OutTime * 0.75, EasingFunction = EaseIn };
        close.Completed += (_, _) => removed();
        holder.BeginAnimation(FrameworkElement.HeightProperty, close);
    }

    /// <summary>Shown in full without animating (renders, the menu's sample).</summary>
    public static void ShowStill(Grid holder) => holder.Height = CircleSize + ComboBanner.Gap;

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
