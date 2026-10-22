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
/// Each kitchen's call as a big round icon like the game's buffs (owner, live 2026-10-21, test_yuanyuan5: "show the
/// recommended dish for each kitchen with a big circle (like buff) that is placed in the middle of the screen above where
/// the currently held dishes are"), in one row at ONE fixed place, the dispatch panel open or closed (owner, 2026-10-22:
/// "that must stay at the middle bottom, above the dishes in hands"; "USE the original circle style";
/// <see cref="InnKitchenCircleLayout"/>). One slot per kitchen, kitchen 1 on the left, 2 in the middle, 3
/// on the right; a kitchen with no call leaves its slot empty, so no circle moves sideways when another comes or goes.
/// Each circle: the kitchen and what it asks for over it, the dish's picture inside a glowing ring, the dish's name and one
/// line of why under it. The kinds read apart at a glance by the ring's colour and the word beside the kitchen:
/// <list type="bullet">
/// <item><b>An order</b> (点单): gold, glowing; "桌4 点的".</item>
/// <item><b>Cooking ahead</b> (预做): cyan, a dashed ring that doesn't glow (nobody waits for it); "还没人点，先做好".</item>
/// <item><b>A swap</b> (换菜): orange-red, glowing; "先拿掉X · 桌5".</item>
/// </list>
/// The calls are <see cref="InnCallHold"/>'s, held steady as before: this is only how they are shown. Every string from
/// i18n/zh-CN.xaml. Drawn at a 1080p window's size and scaled with it (never larger).
/// </summary>
internal static class InnKitchenCircleView
{
    /// <summary>One slot per kitchen the inn can have.</summary>
    public const int Slots = KitchenMath.MaxKitchens;

    /// <summary>
    /// The circle, the dish's picture in it, a slot's width and the row's height, and the words' sizes, at 1080p: the
    /// circle with its words beside it, as v0.6.0 drew it under the open panel, which the owner asked back (2026-10-22, with
    /// their screenshot of that row: "USE the original circle style").
    /// </summary>
    public const double CircleSize = 136, PictureSize = 112, SlotWidth = 300, RowHeight = CircleSize, LabelFont = 22, DishFont = 22, ReasonFont = 20;

    /// <summary>The words' column beside the ring.</summary>
    private const double WordsWidth = SlotWidth - CircleSize - 10;

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
    /// A circle as the overlay draws it, <see cref="SlotWidth"/> wide and <see cref="RowHeight"/> tall at most: the kitchen
    /// and the kind's word, the ring with the dish's picture, the dish and the reason.
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
        // The ring on the left; beside it the kitchen and the kind's word, the dish and the reason.
        header.HorizontalAlignment = HorizontalAlignment.Left;
        dish.HorizontalAlignment = reason.HorizontalAlignment = HorizontalAlignment.Left;
        ring.VerticalAlignment = VerticalAlignment.Center;
        var beside = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), Width = WordsWidth, Children = { header, dish, reason } };
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Width = SlotWidth,
            Height = RowHeight,
            VerticalAlignment = VerticalAlignment.Bottom,
            Children = { ring, beside },
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
        MaxWidth = WordsWidth,
        // Readable over any scene, as the gauges' words are.
        Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 5, ShadowDepth = 0, Opacity = 0.95 },
    };

    /// <summary>What shows while the menu is open (where the circles stand): one circle of each kind, one per slot, with dishes of the inn's own.</summary>
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
    private readonly Border?[] _marks = new Border?[InnKitchenCircleView.Slots];

    private static readonly TimeSpan InTime = TimeSpan.FromMilliseconds(300), OutTime = TimeSpan.FromMilliseconds(180);
    private static readonly IEasingFunction Pop = Frozen(new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 });
    // Half see-through: the game shows behind the row (owner, 2026-10-22: "make sure the transparency is not 100%").
    private static readonly Brush Backing = Frozen(new SolidColorBrush(Color.FromArgb(0x80, 0x12, 0x14, 0x18)));
    private const string BackingTag = "circle-row-backing";
    private static readonly Brush MarkFill = Frozen(new SolidColorBrush(Color.FromArgb(0x38, 0xFF, 0xC8, 0x3C)));

    private readonly (string Key, FrameworkElement Element)?[] _shown = new (string, FrameworkElement)?[InnKitchenCircleView.Slots];
    private readonly List<FrameworkElement> _leaving = [];

    /// <summary>Circles up now (leaving ones not counted).</summary>
    public int Count => _shown.Count(s => s is not null);

    /// <summary>The circle shown for <paramref name="kitchen"/> now; null for none.</summary>
    public FrameworkElement? CircleIn(int kitchen) => kitchen is >= 1 and <= InnKitchenCircleView.Slots ? _shown[kitchen - 1]?.Element : null;

    /// <summary>A row's grid: one fixed column per kitchen, the row's height; the circles stand on its bottom.</summary>
    public static Grid NewGrid() => Shape(new Grid());

    /// <summary>Gives <paramref name="grid"/> (the overlay's, from its XAML) the row's columns and height.</summary>
    public static Grid Shape(Grid grid)
    {
        grid.Height = InnKitchenCircleView.RowHeight;
        grid.ColumnDefinitions.Clear();
        for (int i = 0; i < InnKitchenCircleView.Slots; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(InnKitchenCircleView.SlotWidth) });
        // A dark backing under the whole row: the words stay readable over the floor and over the open panel's bonus column.
        // Hidden while no circle is up (Update): an empty row draws nothing, out of a session above all.
        var backing = new Border { Background = Backing, CornerRadius = new CornerRadius(14), IsHitTestVisible = false, Tag = BackingTag, Visibility = Visibility.Collapsed };
        Grid.SetColumnSpan(backing, InnKitchenCircleView.Slots);
        Panel.SetZIndex(backing, -2);
        grid.Children.Add(backing);
        return grid;
    }

    /// <summary>The kitchen emphasised (the open panel's tab): a gold frame around its slot; null for none.</summary>
    public int? Emphasised { get; private set; }

    /// <summary>The frame drawn around <paramref name="kitchen"/>'s slot now; null for none.</summary>
    public FrameworkElement? MarkIn(int kitchen) => kitchen is >= 1 and <= InnKitchenCircleView.Slots ? _marks[kitchen - 1] : null;

    /// <summary>
    /// Emphasises <paramref name="kitchen"/>'s slot (the kitchen whose tab is selected on the open panel) with a gold frame
    /// around it, in place (the circle doesn't move or change); null takes the frame away.
    /// </summary>
    public void Emphasise(int? kitchen)
    {
        if (Emphasised == kitchen) return;
        Emphasised = kitchen;
        for (int i = 0; i < InnKitchenCircleView.Slots; i++)
        {
            bool on = kitchen == i + 1;
            if (on && _marks[i] is null)
            {
                var frame = new Border
                {
                    BorderBrush = ComboBanner.Accent, BorderThickness = new Thickness(3), CornerRadius = new CornerRadius(12), Background = MarkFill,
                    IsHitTestVisible = false,
                };
                Grid.SetColumn(frame, i);
                Panel.SetZIndex(frame, -1);
                row.Children.Add(frame);
                _marks[i] = frame;
            }
            else if (!on && _marks[i] is { } gone)
            {
                row.Children.Remove(gone);
                _marks[i] = null;
            }
        }
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
            // Pops in where it stands: grows about its own ring, never slides.
            element.RenderTransformOrigin = new Point(0.5, 0.6);
            element.RenderTransform = new ScaleTransform(1, 1);
            row.Children.Add(element);
            _shown[i] = (model.Key, element);
            element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, InTime));
            var grow = new DoubleAnimation(0.7, 1, InTime) { EasingFunction = Pop };
            ((ScaleTransform)element.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            ((ScaleTransform)element.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        }
        // The backing only under circles: with none up, nothing of the row is drawn.
        foreach (var backing in row.Children.OfType<Border>().Where(b => Equals(b.Tag, BackingTag)))
            backing.Visibility = Count > 0 ? Visibility.Visible : Visibility.Collapsed;
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
/// Where the kitchens' circles go: ONE place, the same with the dispatch panel open or closed and whatever is up: in the
/// middle at the bottom, right above the held-dishes tray (owner, 2026-10-22: "WHY did you move the circles! that must stay
/// at the middle bottom, above the dishes in hands"; positions FIXED, nothing moves with the panel or the count). Nothing to
/// drag.
/// <para>
/// The tray's slots start at y 0.853 of the window's centred 16:9 area (x 0.38–0.72, measured on the 4K session frames), so
/// the row is centred on the area and stands on <see cref="Bottom"/> 0.845: 780 × 240 at 1080p, x 0.297–0.703, y
/// 0.623–0.845. With the dispatch panel open the same row covers the panel's lower part: the queue strip's right end and
/// the lower dish cards' middle (the cards' rows end at y 0.80); the owner accepted that rather than another style.
/// </para>
/// <para>
/// The player's size makes the row smaller, never larger. Another aspect ratio: the row stays in the centred 16:9 area, as
/// the game's HUD and the gourmands' spots do.
/// </para>
/// </summary>
internal static class InnKitchenCircleLayout
{
    /// <summary>The row's centre and bottom, as fractions of the window's centred 16:9 area.</summary>
    public const double CenterX = 0.5, Bottom = 0.845;

    /// <summary>The size the row is drawn at: <paramref name="scale"/> (the window's times the player's), at most the 16:9 area's own (its height over 1080).</summary>
    public static double Scale(double width, double height, double scale) => Math.Min(scale, Area(width, height).Height / 1080);

    /// <summary>The row's size at <paramref name="scale"/>: three slots, the row's height.</summary>
    public static Size Size(double scale) => new(InnKitchenCircleView.Slots * InnKitchenCircleView.SlotWidth * scale, InnKitchenCircleView.RowHeight * scale);

    /// <summary>
    /// The row's place in a window of <paramref name="width"/> x <paramref name="height"/> (the overlay's units), at
    /// <paramref name="scale"/> (the window's times the player's; see <see cref="Scale"/>). It takes nothing else: no panel,
    /// no count of circles, so nothing can move it.
    /// </summary>
    public static Rect Place(double width, double height, double scale)
    {
        var (left, top, areaWidth, areaHeight) = Area(width, height);
        var size = Size(Scale(width, height, scale));
        return new Rect(new Point(left + CenterX * areaWidth - size.Width / 2, top + Bottom * areaHeight - size.Height), size);
    }

    private static (double Left, double Top, double Width, double Height) Area(double width, double height)
    {
        double unit = Math.Min(width / 16, height / 9);
        return ((width - unit * 16) / 2, (height - unit * 9) / 2, unit * 16, unit * 9);
    }
}
