using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace EndHud;

/// <summary>
/// One combo-ready banner: how it looks, and how it slides in and out. Used by the overlay, and by
/// the tool that renders the README's animations (tools/EndHud.ReadmeMedia), which poses the same
/// banner at chosen moments with <see cref="Pose"/> instead of running the animations.
/// </summary>
internal static class ComboBanner
{
    public const double Height = 72, Gap = 6;

    /// <summary>The combo's icon beside the avatar.</summary>
    public const double IconSize = 44;

    /// <summary>The overlay's gold (the Accent resource in OverlayWindow.xaml).</summary>
    public static readonly Brush Accent = Frozen(new SolidColorBrush(Color.FromRgb(0xE6, 0xC2, 0x29)));

    private static readonly Brush TextBrush = Frozen(new SolidColorBrush(Color.FromArgb(0xEE, 0xFF, 0xFF, 0xFF)));

    // A combo about to be ready (counting down) is drawn muted, in a cool grey-blue, so it reads
    // apart from a ready one at a glance; turning ready plays a gold pulse (BecomeReady).
    private static readonly Color AccentColor = Color.FromRgb(0xE6, 0xC2, 0x29);
    private static readonly Color SoonEdgeColor = Color.FromRgb(0x7F, 0x9C, 0xB8);
    private static readonly Brush SoonCaption = Frozen(new SolidColorBrush(Color.FromRgb(0xA8, 0xCB, 0xEA)));
    private const double SoonContentOpacity = 0.6;
    private static readonly TimeSpan PulseTime = TimeSpan.FromMilliseconds(600), PopTime = TimeSpan.FromMilliseconds(350);
    private const double PopScale = 1.08;
    private static readonly FontFamily Font = new("Microsoft YaHei UI, Segoe UI");

    // ---- The animation, in one place ----
    // In: the space opens, the banner slides in from the left and fades in, then the gold edge
    // flares and settles. Out: it slides back and fades, then its space closes.
    private static readonly TimeSpan SlideInTime = TimeSpan.FromMilliseconds(320);
    private static readonly TimeSpan SlideOutTime = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan GapTime = TimeSpan.FromMilliseconds(220);
    private static readonly TimeSpan FlareStart = SlideInTime / 2, FlareTime = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan CloseStart = SlideOutTime * 0.75;
    private const double SlideDistance = 160, FlareWidth = 18, EdgeWidth = 5;
    private static readonly IEasingFunction EaseOut = Frozen(new CubicEase { EasingMode = EasingMode.EaseOut });
    private static readonly IEasingFunction EaseIn = Frozen(new CubicEase { EasingMode = EasingMode.EaseIn });
    private static readonly IEasingFunction EaseInOut = Frozen(new CubicEase { EasingMode = EasingMode.EaseInOut });

    /// <summary>How long a banner takes to slide out and close its space.</summary>
    public static TimeSpan OutDuration => CloseStart + GapTime;

    /// <summary>
    /// The banner's content: the operator's avatar and combo icon (either may be missing), name and
    /// caption. <paramref name="soon"/>: about to be ready (the caption a countdown), drawn muted.
    /// <paramref name="iconSize"/>: the icon's size (the inn's banners draw a dish's picture larger).
    /// </summary>
    public static UIElement Content(BitmapSource? avatar, BitmapSource? icon, string name, string caption, bool soon = false, double iconSize = IconSize)
    {
        // The banner is slanted like the game's; its content is slanted back upright.
        var content = new StackPanel { Orientation = Orientation.Horizontal, RenderTransform = new SkewTransform(12, 0) };
        if (avatar is not null)
            content.Children.Add(new Image { Source = avatar, Width = 60, Height = 60, Margin = new Thickness(14, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        if (icon is not null)
            content.Children.Add(new Image { Source = icon, Width = iconSize, Height = iconSize, Margin = new Thickness(avatar is null ? 14 : 0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(avatar is null && icon is null ? 18 : 0, 0, 28, 0) };
        text.Children.Add(new TextBlock { Text = name, FontSize = 20, FontWeight = FontWeights.Bold, Foreground = TextBrush, FontFamily = Font });
        text.Children.Add(new TextBlock { Text = caption, FontSize = 14, Foreground = soon ? SoonCaption : Accent, FontFamily = Font });
        content.Children.Add(text);
        if (soon) content.Opacity = SoonContentOpacity;
        return content;
    }

    /// <summary>Gives the banner's edge the look of <paramref name="soon"/> (grey-blue) or ready (gold), without animating.</summary>
    public static void SetSoon(Border banner, bool soon) =>
        banner.BorderBrush = new SolidColorBrush(soon ? SoonEdgeColor : AccentColor);

    /// <summary>The combo it counted down to is ready: the edge turns gold and flares, and the banner pops.</summary>
    public static void BecomeReady(Border banner)
    {
        var edge = new SolidColorBrush(SoonEdgeColor);
        banner.BorderBrush = edge;
        edge.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(AccentColor, PopTime) { EasingFunction = EaseOut });
        banner.BeginAnimation(Border.BorderThicknessProperty, new ThicknessAnimation(
            new Thickness(FlareWidth, 0, 0, 0), new Thickness(EdgeWidth, 0, 0, 0), PulseTime) { EasingFunction = EaseOut });
        var pop = new DoubleAnimation(PopScale, 1, PopTime) { EasingFunction = EaseOut };
        Scale(banner).BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        Scale(banner).BeginAnimation(ScaleTransform.ScaleYProperty, pop);
    }

    /// <summary>The banner as it looks <paramref name="sinceReady"/> after <see cref="BecomeReady"/>, without running it (for the tools).</summary>
    public static void PoseBecomeReady(Border banner, TimeSpan sinceReady)
    {
        double Part(TimeSpan duration) => EaseOut.Ease(Math.Clamp(sinceReady.TotalMilliseconds / duration.TotalMilliseconds, 0, 1));
        double c = Part(PopTime);
        byte Mix(byte from, byte to) => (byte)Math.Round(from + (to - from) * c);
        banner.BorderBrush = new SolidColorBrush(Color.FromRgb(Mix(SoonEdgeColor.R, AccentColor.R), Mix(SoonEdgeColor.G, AccentColor.G), Mix(SoonEdgeColor.B, AccentColor.B)));
        banner.BorderThickness = new Thickness(FlareWidth + (EdgeWidth - FlareWidth) * Part(PulseTime), 0, 0, 0);
        double scale = PopScale + (1 - PopScale) * c;
        Scale(banner).ScaleX = scale;
        Scale(banner).ScaleY = scale;
    }

    /// <summary>The slanted banner around <paramref name="content"/>, <paramref name="height"/> tall (its space in a stack opens to that).</summary>
    public static Border Create(UIElement content, double height = Height) => new()
    {
        MinWidth = 280,
        Height = height,
        VerticalAlignment = VerticalAlignment.Center,
        Background = new LinearGradientBrush(Color.FromArgb(0xE6, 0x10, 0x14, 0x18), Color.FromArgb(0x30, 0x10, 0x14, 0x18), 0),
        BorderBrush = Accent,
        BorderThickness = new Thickness(EdgeWidth, 0, 0, 0),
        RenderTransformOrigin = new Point(0, 0.5),
        RenderTransform = new TransformGroup { Children = { new ScaleTransform(1, 1), new SkewTransform(-12, 0), new TranslateTransform() } },
        Child = content,
    };

    /// <summary>
    /// The banner's space in a stack of banners. It opens and closes with the banner, so the
    /// banners below glide to their new place instead of jumping.
    /// </summary>
    public static Grid Holder(Border banner, object? tag = null) => new() { Tag = tag, Height = 0, Children = { banner } };

    public static void SlideIn(Grid holder, Border banner)
    {
        holder.BeginAnimation(FrameworkElement.HeightProperty, new DoubleAnimation(0, banner.Height + Gap, GapTime) { EasingFunction = EaseOut });
        Translation(banner).BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-SlideDistance, 0, SlideInTime) { EasingFunction = EaseOut });
        banner.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, SlideInTime));
        banner.BeginAnimation(Border.BorderThicknessProperty, new ThicknessAnimation(
            new Thickness(FlareWidth, 0, 0, 0), new Thickness(EdgeWidth, 0, 0, 0), FlareTime) { BeginTime = FlareStart, EasingFunction = EaseOut });
    }

    public static void SlideOut(Grid holder, Border banner, Action removed)
    {
        Translation(banner).BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(-SlideDistance / 2, SlideOutTime) { EasingFunction = EaseIn });
        banner.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, SlideOutTime));
        var close = new DoubleAnimation(0, GapTime) { BeginTime = CloseStart, EasingFunction = EaseInOut };
        close.Completed += (_, _) => removed();
        holder.BeginAnimation(FrameworkElement.HeightProperty, close);
    }

    /// <summary>
    /// A banner slides into a space already open (a kitchen's row kept for the session, <see cref="OpenHolder"/>): as
    /// <see cref="SlideIn"/>, but nothing around it moves.
    /// </summary>
    public static void SlideInPlace(Border banner)
    {
        Translation(banner).BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-SlideDistance, 0, SlideInTime) { EasingFunction = EaseOut });
        banner.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, SlideInTime));
        banner.BeginAnimation(Border.BorderThicknessProperty, new ThicknessAnimation(
            new Thickness(FlareWidth, 0, 0, 0), new Thickness(EdgeWidth, 0, 0, 0), FlareTime) { BeginTime = FlareStart, EasingFunction = EaseOut });
    }

    /// <summary>A banner slides out of a space that stays open (a kitchen's row): as <see cref="SlideOut"/>, its space kept.</summary>
    public static void SlideOutInPlace(Border banner, Action removed)
    {
        Translation(banner).BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(-SlideDistance / 2, SlideOutTime) { EasingFunction = EaseIn });
        var fade = new DoubleAnimation(0, SlideOutTime);
        fade.Completed += (_, _) => removed();
        banner.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    /// <summary>A space for banners of <paramref name="height"/> (+ the gap), opening once and then kept (a kitchen's row).</summary>
    public static Grid OpenHolder(double height, object? tag = null)
    {
        var holder = new Grid { Tag = tag, Height = 0 };
        holder.BeginAnimation(FrameworkElement.HeightProperty, new DoubleAnimation(0, height + Gap, GapTime) { EasingFunction = EaseOut });
        return holder;
    }

    /// <summary>
    /// Sets the banner as it looks <paramref name="sinceIn"/> after it started sliding in, and
    /// <paramref name="sinceOut"/> after it started sliding out (null: not yet). The same timings
    /// and easing as <see cref="SlideIn"/> and <see cref="SlideOut"/>, without running them.
    /// </summary>
    public static void Pose(Grid holder, Border banner, TimeSpan sinceIn, TimeSpan? sinceOut)
    {
        double In(TimeSpan duration, TimeSpan start = default) =>
            Math.Clamp((sinceIn - start).TotalMilliseconds / duration.TotalMilliseconds, 0, 1);
        double x = -SlideDistance * (1 - EaseOut.Ease(In(SlideInTime)));
        double opacity = In(SlideInTime);
        double height = (banner.Height + Gap) * EaseOut.Ease(In(GapTime));
        // Before the flare starts the edge has its normal width, as the animation leaves it.
        double edge = sinceIn < FlareStart ? EdgeWidth : FlareWidth + (EdgeWidth - FlareWidth) * EaseOut.Ease(In(FlareTime, FlareStart));

        if (sinceOut is { } since)
        {
            double Out(TimeSpan duration, TimeSpan start = default) =>
                Math.Clamp((since - start).TotalMilliseconds / duration.TotalMilliseconds, 0, 1);
            // Like the animation: from wherever it is towards -SlideDistance / 2.
            x += (-SlideDistance / 2 - x) * EaseIn.Ease(Out(SlideOutTime));
            opacity *= 1 - Out(SlideOutTime);
            height *= 1 - EaseInOut.Ease(Out(GapTime, CloseStart));
        }

        holder.Height = height;
        banner.Opacity = opacity;
        banner.BorderThickness = new Thickness(edge, 0, 0, 0);
        Translation(banner).X = x;
    }

    private static ScaleTransform Scale(Border banner) =>
        ((TransformGroup)banner.RenderTransform).Children.OfType<ScaleTransform>().Single();

    private static TranslateTransform Translation(Border banner) =>
        ((TransformGroup)banner.RenderTransform).Children.OfType<TranslateTransform>().Single();

    public static BitmapSource ToBitmap(Vision.Frame frame)
    {
        var bitmap = BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null, frame.Bgra, frame.Width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
