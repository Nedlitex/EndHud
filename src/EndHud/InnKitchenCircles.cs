using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using EndHud.Vision.Inn;

namespace EndHud;

/// <summary>What a kitchen's circle asks for.</summary>
internal enum InnCircleKind
{
    /// <summary>Queue the dish for a table's order.</summary>
    Order,

    /// <summary>Queue the dish to cook ahead: no order claims it yet.</summary>
    CookAhead,

    /// <summary>Take a dish cooked ahead out of the queue, then queue this one for a table's order.</summary>
    Swap,
}

/// <summary>One kitchen's circle, as drawn.</summary>
/// <param name="Kitchen">1 to <see cref="KitchenMath.MaxKitchens"/>: its slot, left to right.</param>
/// <param name="Key">The call's key (<see cref="InnCall.Key"/>): a circle with the same key stays up as it is.</param>
/// <param name="Label">The kitchen, over the circle ("厨房一").</param>
/// <param name="KindWord">What it asks for, in a pill of the kind's colour beside the kitchen ("点单", "预做", "换菜").</param>
/// <param name="Dish">The dish to queue, under the circle.</param>
/// <param name="Reason">One line under the dish: the table that ordered it, cooking ahead, or what to take out first.</param>
/// <param name="PictureId">The dish's picture, inside the circle.</param>
internal sealed record InnCircleModel(int Kitchen, string Key, InnCircleKind Kind, string Label, string KindWord, string Dish, string Reason, string? PictureId);

/// <summary>
/// Each kitchen's call as a big round icon like the game's buffs, in a row above the held-dishes tray (owner, live
/// 2026-10-21, test_yuanyuan5: "the notification of each kitchen is very hard to read. try another UI: show the
/// recommended dish for each kitchen with a big circle (like buff) that is placed in the middle of the screen above where
/// the currently held dishes are"). One slot per kitchen, kitchen 1 on the left, 2 in the middle, 3 on the right; a kitchen
/// with no call leaves its slot empty, so no circle moves sideways when another comes or goes. Each circle: the kitchen
/// and what it asks for over it, the dish's picture large inside a glowing ring, the dish's name and one line of why under
/// it. The kinds read apart at a glance by the ring's colour and the word beside the kitchen:
/// <list type="bullet">
/// <item><b>An order</b> (点单): gold, glowing; "桌4 点的".</item>
/// <item><b>Cooking ahead</b> (预做): cyan, a dashed ring that doesn't glow (nobody waits for it); "还没人点，先做好".</item>
/// <item><b>A swap</b> (换菜): orange-red, glowing; "先拿掉X · 桌5".</item>
/// </list>
/// The calls are <see cref="InnCallHold"/>'s, held steady as before: this is only how they are shown. Every string from
/// i18n/zh-CN.xaml. Drawn at a 1080p window's size and scaled with it.
/// </summary>
internal static class InnKitchenCircleView
{
    /// <summary>One slot per kitchen the inn can have.</summary>
    public const int Slots = KitchenMath.MaxKitchens;

    /// <summary>
    /// The circle, the dish's picture in it (the banner's was 76: <see cref="InnCallView.PictureSize"/>), a slot's width and
    /// the row's height, and the words' sizes (the banner's title was 20), at 1080p.
    /// </summary>
    public const double CircleSize = 136, PictureSize = 112, SlotWidth = 260, RowHeight = 240, LabelFont = 22, DishFont = 22, ReasonFont = 20;

    private const double RingThickness = 8;

    // Frozen: shared by every circle drawn.
    internal static readonly Color OrderColor = Color.FromRgb(0xFF, 0xC8, 0x3C);
    internal static readonly Color CookAheadColor = Color.FromRgb(0x5E, 0xE6, 0xFF);
    internal static readonly Color SwapColor = Color.FromRgb(0xFF, 0x6A, 0x3D);
    private static readonly Brush Disc = Frozen(new SolidColorBrush(Color.FromArgb(0xD8, 0x16, 0x18, 0x1C)));
    private static readonly Brush Track = Frozen(new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)));
    private static readonly Brush TextBrush = Frozen(new SolidColorBrush(Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF)));
    private static readonly Brush PillText = Frozen(new SolidColorBrush(Color.FromRgb(0x14, 0x16, 0x1A)));
    private static readonly FontFamily Font = new("Microsoft YaHei UI, Segoe UI");

    /// <summary>The slots, left to right: each kitchen's call (a dish to queue, cook ahead or swap), null for none.</summary>
    /// <param name="text">The i18n lookup (<see cref="Loc.Get"/>; the tests pass zh-CN.xaml's strings).</param>
    public static IReadOnlyList<InnCircleModel?> Of(IReadOnlyList<InnCall> calls, Func<string, string> text)
    {
        var slots = new InnCircleModel?[Slots];
        foreach (var call in calls.Where(c => c.ForKitchen && c.Kitchen is >= 1 and <= Slots && c.Dish is not null))
            slots[call.Kitchen!.Value - 1] ??= Of(call, text);
        return slots;
    }

    /// <summary>One kitchen's call as a circle (a dish to queue, cook ahead or swap).</summary>
    public static InnCircleModel Of(InnCall call, Func<string, string> text)
    {
        var kind = call.Kind switch
        {
            InnCallKind.CookAhead => InnCircleKind.CookAhead,
            InnCallKind.Swap => InnCircleKind.Swap,
            _ => InnCircleKind.Order,
        };
        string reason = kind switch
        {
            InnCircleKind.CookAhead => text("Inn.Circle.CookAheadReason"),
            InnCircleKind.Swap => string.Format(text("Inn.Circle.SwapReason"), call.Remove!.Zh, call.Table!),
            _ => string.Format(text("Inn.Call.ForTable"), call.Table!),
        };
        string word = text(kind switch
        {
            InnCircleKind.CookAhead => "Inn.Circle.CookAhead",
            InnCircleKind.Swap => "Inn.Circle.Swap",
            _ => "Inn.Circle.Order",
        });
        return new InnCircleModel(call.Kitchen!.Value, call.Key, kind, text($"Inn.Kitchen.{call.Kitchen}"), word, call.Dish!.Zh, reason, call.Dish.Id);
    }

    /// <summary>The ring's colour for a kind of call.</summary>
    public static Color ColorOf(InnCircleKind kind) => kind switch
    {
        InnCircleKind.CookAhead => CookAheadColor,
        InnCircleKind.Swap => SwapColor,
        _ => OrderColor,
    };

    /// <summary>
    /// A circle as the overlay draws it, <see cref="SlotWidth"/> wide at most and <see cref="RowHeight"/> tall at most:
    /// the kitchen and the kind's word, the ring with the dish's picture, the dish and the reason.
    /// </summary>
    /// <param name="picture">A dish's picture by id; null when the package has none (the ring is drawn empty).</param>
    public static FrameworkElement Circle(InnCircleModel model, Func<string, BitmapSource?> picture)
    {
        var color = ColorOf(model.Kind);
        var brush = Frozen(new SolidColorBrush(color));

        var header = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 4) };
        header.Children.Add(Words(model.Label, LabelFont, TextBrush, bold: true));
        header.Children.Add(new Border
        {
            Background = brush,
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(9, 0, 9, 1),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = model.KindWord, FontSize = ReasonFont, FontWeight = FontWeights.Bold, Foreground = PillText, FontFamily = Font },
        });

        var ring = new Grid { Width = CircleSize, Height = CircleSize, HorizontalAlignment = HorizontalAlignment.Center };
        ring.Children.Add(new Ellipse { Fill = Disc });
        if (model.PictureId is { } id && picture(id) is { } image)
        {
            ring.Children.Add(new Image
            {
                Source = image,
                Width = PictureSize,
                Height = PictureSize,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Clip = new EllipseGeometry(new Point(PictureSize / 2, PictureSize / 2), PictureSize / 2, PictureSize / 2),
            });
        }
        ring.Children.Add(new Ellipse { Stroke = Track, StrokeThickness = RingThickness });
        var stroke = new Ellipse { Stroke = brush, StrokeThickness = RingThickness };
        if (model.Kind == InnCircleKind.CookAhead)
        {
            // Nobody waits for it: a dashed ring, no glow.
            stroke.StrokeDashArray = [1.6, 0.9];
            stroke.Opacity = 0.9;
        }
        else stroke.Effect = new DropShadowEffect { Color = color, BlurRadius = 24, ShadowDepth = 0, Opacity = 1 };
        ring.Children.Add(stroke);

        var dish = Words(model.Dish, DishFont, brush, bold: true);
        dish.Margin = new Thickness(0, 4, 0, 0);
        var reason = Words(model.Reason, ReasonFont, TextBrush, bold: false);
        return new StackPanel
        {
            Width = SlotWidth,
            VerticalAlignment = VerticalAlignment.Bottom,
            Children = { header, ring, dish, reason },
        };
    }

    private static TextBlock Words(string words, double size, Brush brush, bool bold) => new()
    {
        Text = words,
        FontSize = size,
        FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
        Foreground = brush,
        FontFamily = Font,
        HorizontalAlignment = HorizontalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis,
        MaxWidth = SlotWidth,
        // Readable over any scene, as the gauges' words are.
        Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 5, ShadowDepth = 0, Opacity = 0.95 },
    };

    /// <summary>What to drag while the menu is open: one circle of each kind, one per slot, with dishes of the inn's own.</summary>
    public static IReadOnlyList<InnCircleModel?> Sample(InnData? data, Func<string, string> text)
    {
        if (data is null || data.Dishes.Count < 3) return new InnCircleModel?[Slots];
        var dishes = data.Dishes;
        return Of([
            new InnCall(InnCallKind.Queue, 1, dishes[0], 3),
            new InnCall(InnCallKind.CookAhead, 2, dishes[1]),
            new InnCall(InnCallKind.Swap, 3, dishes[2], 5, dishes[1]),
        ], text);
    }

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}

/// <summary>
/// The circles drawn into their row, one column per kitchen (<see cref="InnKitchenCircleView"/>): a circle already up for
/// the same call stays as it is (the same element, not drawn or animated again); a kitchen's call changing fades the old
/// circle out and pops the new one in, in the same column; nothing else moves.
/// </summary>
/// <param name="row">The row's grid (<see cref="NewGrid"/>).</param>
/// <param name="build">A circle's element for its model (<see cref="InnKitchenCircleView.Circle"/>).</param>
internal sealed class InnCircleRow(Grid row, Func<InnCircleModel, FrameworkElement> build)
{
    private static readonly TimeSpan InTime = TimeSpan.FromMilliseconds(300), OutTime = TimeSpan.FromMilliseconds(180);
    private static readonly IEasingFunction Pop = Frozen(new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 });

    private readonly (string Key, FrameworkElement Element)?[] _shown = new (string, FrameworkElement)?[InnKitchenCircleView.Slots];
    private readonly List<FrameworkElement> _leaving = [];

    /// <summary>Circles up now (leaving ones not counted).</summary>
    public int Count => _shown.Count(s => s is not null);

    /// <summary>The circle shown for <paramref name="kitchen"/> now; null for none.</summary>
    public FrameworkElement? CircleIn(int kitchen) => kitchen is >= 1 and <= InnKitchenCircleView.Slots ? _shown[kitchen - 1]?.Element : null;

    /// <summary>A row's grid: one fixed column per kitchen, as tall as the tallest circle; the circles stand on its bottom.</summary>
    public static Grid NewGrid() => Shape(new Grid());

    /// <summary>Gives <paramref name="grid"/> (the overlay's, from its XAML) the row's columns and height.</summary>
    public static Grid Shape(Grid grid)
    {
        grid.Height = InnKitchenCircleView.RowHeight;
        grid.ColumnDefinitions.Clear();
        for (int i = 0; i < InnKitchenCircleView.Slots; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(InnKitchenCircleView.SlotWidth) });
        return grid;
    }

    /// <summary>Draws <paramref name="slots"/> (left to right; null or missing: that slot empty).</summary>
    public void Update(IReadOnlyList<InnCircleModel?> slots)
    {
        for (int i = 0; i < InnKitchenCircleView.Slots; i++)
        {
            var model = i < slots.Count ? slots[i] : null;
            if (_shown[i] is { } shown)
            {
                if (shown.Key == model?.Key) continue;
                Leave(shown.Element);
                _shown[i] = null;
            }
            if (model is null) continue;
            var element = build(model);
            Grid.SetColumn(element, i);
            element.VerticalAlignment = VerticalAlignment.Bottom;
            element.RenderTransformOrigin = new Point(0.5, 0.6);
            element.RenderTransform = new ScaleTransform(1, 1);
            row.Children.Add(element);
            _shown[i] = (model.Key, element);
            element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, InTime));
            var grow = new DoubleAnimation(0.7, 1, InTime) { EasingFunction = Pop };
            ((ScaleTransform)element.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            ((ScaleTransform)element.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        }
    }

    /// <summary>Ends every animation as it would end (the tests' pictures): leaving circles gone, the others fully in.</summary>
    public void Settle()
    {
        foreach (var gone in _leaving) row.Children.Remove(gone);
        _leaving.Clear();
        foreach (var element in _shown.Where(s => s is not null).Select(s => s!.Value.Element))
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = 1;
            if (element.RenderTransform is ScaleTransform scale)
            {
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            }
        }
    }

    private void Leave(FrameworkElement element)
    {
        _leaving.Add(element);
        var fade = new DoubleAnimation(0, OutTime);
        fade.Completed += (_, _) =>
        {
            _leaving.Remove(element);
            row.Children.Remove(element);
        };
        element.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}

/// <summary>
/// Where the kitchens' circles go: their row centred on the player's <c>InnCircleX</c> and standing on <c>InnCircleY</c>,
/// fractions of the window's centred 16:9 area (where the game lays out its HUD). By default centred, its bottom at 0.845:
/// right above the held-dishes tray (its slots from y 0.855, x 0.38–0.62, on the 4K fixtures), on floor that is otherwise
/// empty. Kept inside the window. Draggable while the menu is open.
/// </summary>
internal static class InnKitchenCircleLayout
{
    public const double DefaultCenterX = 0.5, DefaultBottomY = 0.845;

    /// <summary>The overlay's frame around the row: its padding and border on both sides (OverlayWindow.xaml).</summary>
    public const double Frame = 2 * (6 + 2);

    /// <summary>The row's size on screen at <paramref name="scale"/>: three slots and the frame.</summary>
    public static Size Size(double scale) => new(
        (InnKitchenCircleView.Slots * InnKitchenCircleView.SlotWidth + Frame) * scale,
        (InnKitchenCircleView.RowHeight + Frame) * scale);

    /// <summary>The row's place in a window of <paramref name="width"/> × <paramref name="height"/> (the overlay's units).</summary>
    public static Rect Place(double width, double height, double scale, double centerX, double bottomY)
    {
        var (left, top, areaWidth, areaHeight) = Area(width, height);
        var size = Size(scale);
        double x = Math.Clamp(left + centerX * areaWidth - size.Width / 2, 0, Math.Max(0, width - size.Width));
        double y = Math.Clamp(top + bottomY * areaHeight - size.Height, 0, Math.Max(0, height - size.Height));
        return new Rect(x, y, size.Width, size.Height);
    }

    /// <summary>The fractions (<c>InnCircleX</c>, <c>InnCircleY</c>) for the row placed at <paramref name="placed"/>.</summary>
    public static (double CenterX, double BottomY) Fractions(Rect placed, double width, double height)
    {
        var (left, top, areaWidth, areaHeight) = Area(width, height);
        return ((placed.Left + placed.Width / 2 - left) / areaWidth, (placed.Bottom - top) / areaHeight);
    }

    private static (double Left, double Top, double Width, double Height) Area(double width, double height)
    {
        double unit = Math.Min(width / 16, height / 9);
        return ((width - unit * 16) / 2, (height - unit * 9) / 2, unit * 16, unit * 9);
    }
}
