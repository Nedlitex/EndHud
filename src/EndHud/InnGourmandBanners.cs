using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using EndHud.Vision.Inn;

namespace EndHud;

/// <summary>
/// One of a gourmand's requests and what meets it: a row of their banner.
/// </summary>
/// <param name="Request">Which request (0 = the first said); 0 for a gourmand whose words weren't recognised.</param>
/// <param name="Description">The request in plain words (<see cref="GourmandCondition.Describe"/>, a drink's as a drink, Inn.Gourmand.Drink); null for words not recognised.</param>
/// <param name="ItemId">The best dish's or drink's id (its picture); null when nothing on the menu meets it, the words weren't recognised, or no drink of the menu was seen yet.</param>
/// <param name="Name">The item's name, or the words saying why there's none; null when nothing can be said yet (a drink request before any drink of the menu was seen: "none fits" would be wrong).</param>
internal sealed record InnWant(int Table, int Request, string? Description, string? ItemId, string? Name);

/// <summary>A row of a gourmand's banner as drawn: "(1) a soup without seed oil → [picture] the dish".</summary>
/// <param name="Number">The request's number (Inn.Gourmand.Numbers).</param>
/// <param name="Request">The request in plain words; null for words not recognised (the row is <paramref name="Item"/> only).</param>
/// <param name="Item">The item's name or why there's none; null for nothing to say yet (the row is the request only).</param>
internal sealed record InnGourmandRow(string Number, string? Request, string? PictureId, string? Item);

/// <summary>One gourmand's banner: "gourmand, table 4" (Inn.Gourmand.Title) over one numbered row per request.</summary>
/// <param name="Key">Who the banner is (the gourmand, by table): it stays up while the gourmand's order is open, what it says updated in place.</param>
/// <param name="Short">The table alone (Inn.Gourmand.Table, "桌4"): the compact tile's badge, on its first picture.</param>
internal sealed record InnGourmandBanner(string Key, int Table, string Title, string Short, IReadOnlyList<InnGourmandRow> Rows)
{
    /// <summary>What it says: a banner whose content changes is drawn again where it is, never slid out and in.</summary>
    public string Content => Title + "|" + string.Join("|", Rows.Select(r => $"{r.Number}{r.Request}>{r.PictureId}>{r.Item}"));
}

/// <summary>
/// What each gourmand wants, as banners in the cooking banners' combo style on the right side of the screen (owner,
/// live 2026-10-21: "the recommendation of gourmand is very confusing now, user cannot distinguish between whether the
/// two circles are alternatives or two requirements ... change the style of gourmand to be something like the banner"):
/// one banner per gourmand, titled with their table (Inn.Gourmand.Title), then one numbered row per REQUEST, each the request in plain
/// words, an arrow, and the best dish or drink on the menu for it with its picture, so two rows read as two things to
/// serve. Served in the rows' order, each item fills its own request (a dish fills the first request not yet filled
/// that it meets, and the first row's item meets the first request). A request nothing on the menu meets says so
/// (Inn.Gourmand.None); a gourmand whose words weren't recognised, that (Inn.Gourmand.Unknown); a drink request before
/// any drink of the menu was seen shows the request alone. Up while the gourmand's order is open (<see cref="InnSession"/>
/// keeps the request until it's fulfilled), held steady (<see cref="InnWantsHold"/>), put away while the dispatch panel
/// is open (they'd cover it, as the cooking banners would). Every string from i18n/zh-CN.xaml.
/// </summary>
internal static class InnGourmandView
{
    /// <summary>
    /// The most banners up at once: every table a gourmand's (owner, 2026-10-21: "we need to be prepared to support all 6
    /// tables are gourmand"). Many are drawn compact and placed around the Full Blaze meter (<see cref="InnGourmandLayout.Arrange"/>).
    /// </summary>
    public const int MostBanners = TableCardReader.Tables;

    /// <summary>
    /// A tile's parts at 1080p (scaled with the window and the player's size): each item's picture as large as a kitchen
    /// circle's (<see cref="InnKitchenCircleView.PictureSize"/>; owner, 2026-10-21: "make at least the picture bigger, at least
    /// same size as the circule today"), every word at least the circles' 20; an item's column, the line under a picture (a
    /// name), the request's two lines, the title's line, the padding inside the tile and between items.
    /// </summary>
    public const double PictureSize = InnKitchenCircleView.PictureSize, ItemWidth = 132, CompactItemWidth = 116, NameHeight = 23,
        RequestHeight = 2 * NameHeight, TitleHeight = 30, Padding = 8, CompactPadding = 2, ItemGap = 6, Font20 = 20, TitleFont = 22, BadgeFont = 20;

    /// <param name="text">The i18n lookup (<see cref="Loc.Get"/>; the tests pass zh-CN.xaml's strings).</param>
    public static IReadOnlyList<InnWant> Of(InnAdvice advice, Func<string, string> text) =>
        [.. advice.Gourmands.OrderBy(g => g.Table).SelectMany(note => Of(note, text))];

    /// <summary>One gourmand's rows, as this moment's advice has them: one per request, in the order said.</summary>
    internal static IReadOnlyList<InnWant> Of(InnGourmandNote note, Func<string, string> text)
    {
        if (note.Advice is not { } gourmand) return [new InnWant(note.Table, 0, null, null, text("Inn.Gourmand.Unknown"))];
        var chosen = Assigned(gourmand);
        var wants = new List<InnWant>();
        for (int i = 0; i < gourmand.Requests.Count; i++)
        {
            var request = gourmand.Requests[i];
            string description = request.ForDrink ? string.Format(text("Inn.Gourmand.Drink"), request.Description) : request.Description;
            if ((chosen?[i] ?? request.Options.FirstOrDefault()) is { } best)
                wants.Add(new InnWant(note.Table, i, description, best.Id, best.Dish?.Zh ?? best.Drink!.Zh));
            else if (request.ForDrink && note.DrinksUnknown) wants.Add(new InnWant(note.Table, i, description, null, null));
            else wants.Add(new InnWant(note.Table, i, description, null, text("Inn.Gourmand.None")));
        }
        return wants;
    }

    /// <summary>
    /// The advice's serving (<see cref="GourmandAdvice.Serve"/>, one item per request so each fills one) put back on the
    /// requests: served one by one, an item fills the first request not yet filled that it meets. Null without one.
    /// </summary>
    private static GourmandOption?[]? Assigned(GourmandAdvice gourmand)
    {
        if (gourmand.Serve is not { } serve || serve.Count != gourmand.Requests.Count) return null;
        var chosen = new GourmandOption?[gourmand.Requests.Count];
        foreach (var item in serve)
        {
            int request = Enumerable.Range(0, chosen.Length).FirstOrDefault(r => chosen[r] is null && gourmand.Requests[r].Options.Any(o => o.Id == item.Id), -1);
            if (request < 0) return null;
            chosen[request] = item;
        }
        return chosen;
    }

    /// <summary>The banners for <paramref name="wants"/>: one per gourmand, by table, at most <see cref="MostBanners"/>.</summary>
    public static IReadOnlyList<InnGourmandBanner> Banners(IReadOnlyList<InnWant> wants, Func<string, string> text)
    {
        string numbers = text("Inn.Gourmand.Numbers");
        return [.. wants.GroupBy(w => w.Table).OrderBy(g => g.Key).Take(MostBanners).Select(g =>
        {
            var rows = g.OrderBy(w => w.Request).Select(w => new InnGourmandRow(
                w.Description is null ? "" : w.Request < numbers.Length ? numbers[w.Request].ToString() : $"{w.Request + 1}.", w.Description, w.ItemId, w.Name)).ToList();
            return new InnGourmandBanner($"gourmand:{g.Key}", g.Key, string.Format(text("Inn.Gourmand.Title"), g.Key), string.Format(text("Inn.Gourmand.Table"), g.Key), rows);
        })];
    }

    /// <summary>A tile's height, at 1080p, in <paramref name="shape"/> for a gourmand of <paramref name="requests"/> requests (side by side: the same for one or two).</summary>
    public static double Height(InnGourmandShape shape, int requests)
    {
        int n = Math.Clamp(requests, 1, MostRequests);
        return shape switch
        {
            InnGourmandShape.Full => 2 * Padding + TitleHeight + PictureSize + NameHeight + RequestHeight,
            InnGourmandShape.Compact => 2 * CompactPadding + PictureSize + NameHeight,
            InnGourmandShape.Tall => 2 * Padding + TitleHeight + n * (PictureSize + NameHeight) + (n - 1) * ItemGap,
            _ => 2 * Padding + TitleHeight + n * PictureSize + (n - 1) * ItemGap,
        };
    }

    /// <summary>A tile's slot's width, at 1080p, in <paramref name="shape"/>: room for <see cref="MostRequests"/> items.</summary>
    public static double Width(InnGourmandShape shape) => Width(shape, MostRequests);

    /// <summary>A tile's width, at 1080p, in <paramref name="shape"/> for a gourmand of <paramref name="requests"/> requests.</summary>
    public static double Width(InnGourmandShape shape, int requests)
    {
        int n = shape is InnGourmandShape.Tall or InnGourmandShape.TallBare ? 1 : Math.Clamp(requests, 1, MostRequests);
        return shape switch
        {
            InnGourmandShape.Full => 2 * Padding + n * ItemWidth + (n - 1) * ItemGap,
            InnGourmandShape.Compact => 2 * CompactPadding + n * CompactItemWidth + (n - 1) * ItemGap,
            _ => 2 * Padding + CompactItemWidth,
        };
    }

    /// <summary>The most requests a gourmand makes: a tile's slot holds this many items.</summary>
    public const int MostRequests = GourmandAdvisor.MaxRequests;

    private static readonly FontFamily Font = new("Microsoft YaHei UI, Segoe UI");
    private static readonly Brush TextBrush = Frozen(new SolidColorBrush(Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF)));
    private static readonly Brush Back = Frozen(new SolidColorBrush(Color.FromArgb(0xD8, 0x16, 0x18, 0x1C)));
    private static readonly Brush Slot = Frozen(new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)));
    private static readonly Brush Ink = Frozen(new SolidColorBrush(Color.FromRgb(0x14, 0x16, 0x1A)));

    /// <summary>
    /// A gourmand's tile as the overlay draws it, at 1080p (<see cref="Width"/> × <see cref="Height"/> at most): a dark
    /// rounded card with a gold edge, the table (Inn.Gourmand.Title), and per request its item's picture as large as a
    /// kitchen circle's (112) with its number over the corner, the item's name under it and, in <see cref="InnGourmandShape.Full"/>,
    /// the request in plain words. Items side by side (<see cref="InnGourmandShape.Full"/>, <see cref="InnGourmandShape.Compact"/>,
    /// the table then on the first picture) or one above the other (<see cref="InnGourmandShape.Tall"/>,
    /// <see cref="InnGourmandShape.TallBare"/>: beside the open panel). A request with no item shows an empty disc and why.
    /// </summary>
    /// <param name="picture">A dish's or drink's picture by id; null when the package has none (an empty disc is drawn).</param>
    public static Border Tile(InnGourmandBanner banner, Func<string, BitmapSource?> picture, InnGourmandShape shape)
    {
        bool tall = shape is InnGourmandShape.Tall or InnGourmandShape.TallBare;
        bool names = shape is not InnGourmandShape.TallBare;
        double itemWidth = shape == InnGourmandShape.Full ? ItemWidth : CompactItemWidth;
        var items = new StackPanel { Orientation = tall ? Orientation.Vertical : Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        for (int i = 0; i < banner.Rows.Count; i++)
        {
            var row = banner.Rows[i];
            var item = new StackPanel { Width = itemWidth, Margin = i == 0 ? new Thickness(0) : tall ? new Thickness(0, ItemGap, 0, 0) : new Thickness(ItemGap, 0, 0, 0) };
            var disc = new Grid { Width = PictureSize, Height = PictureSize, HorizontalAlignment = HorizontalAlignment.Center };
            disc.Children.Add(new Border { CornerRadius = new CornerRadius(14), Background = Slot });
            if (row.PictureId is { } id && picture(id) is { } image)
                disc.Children.Add(new Image { Source = image, Width = PictureSize, Height = PictureSize, Stretch = Stretch.UniformToFill });
            // The compact tile has no title line: its table on the first picture's left corner, the numbers on the right.
            bool compact = shape == InnGourmandShape.Compact;
            if (compact && i == 0)
                disc.Children.Add(Badge(banner.Short, HorizontalAlignment.Left));
            if (row.Number.Length > 0)
                disc.Children.Add(Badge(row.Number, compact ? HorizontalAlignment.Right : HorizontalAlignment.Left));
            item.Children.Add(disc);
            if (names && row.Item is not null)
                item.Children.Add(Words(row.Item, ComboBanner.Accent, bold: true, NameHeight, wrap: false, itemWidth));
            if (shape == InnGourmandShape.Full && row.Request is not null)
                item.Children.Add(Words(row.Request, TextBrush, bold: false, RequestHeight, wrap: true, itemWidth));
            items.Children.Add(item);
        }
        var content = new StackPanel();
        if (shape != InnGourmandShape.Compact)
        {
            var title = Words(banner.Title, TextBrush, bold: true, TitleHeight, wrap: false, Width(shape) - 2 * Padding);
            title.FontSize = TitleFont;
            title.HorizontalAlignment = HorizontalAlignment.Left;
            content.Children.Add(title);
        }
        content.Children.Add(items);
        double padding = shape == InnGourmandShape.Compact ? CompactPadding : Padding;
        return new Border
        {
            Width = Width(shape, banner.Rows.Count),
            Height = Height(shape, banner.Rows.Count),
            Background = Back,
            BorderBrush = ComboBanner.Accent,
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(padding - 2),
            Child = content,
            IsHitTestVisible = false,
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 10, ShadowDepth = 0, Opacity = 0.6 },
        };
    }

    /// <summary>A small gold pill over a picture's top corner: a request's number, or the table on a compact tile.</summary>
    private static Border Badge(string words, HorizontalAlignment side) => new()
    {
        Background = ComboBanner.Accent,
        CornerRadius = new CornerRadius(10),
        Padding = new Thickness(6, 0, 6, 1),
        Margin = new Thickness(3),
        HorizontalAlignment = side,
        VerticalAlignment = VerticalAlignment.Top,
        Child = new TextBlock { Text = words, FontFamily = Font, FontSize = BadgeFont, FontWeight = FontWeights.Bold, Foreground = Ink },
    };

    private static TextBlock Words(string words, Brush brush, bool bold, double height, bool wrap, double width) => new()
    {
        Text = words,
        FontSize = Font20,
        FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
        Foreground = brush,
        FontFamily = Font,
        TextAlignment = TextAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
        TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
        TextTrimming = TextTrimming.CharacterEllipsis,
        LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        LineHeight = NameHeight,
        Height = height,
        MaxWidth = width,
        Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 4, ShadowDepth = 0, Opacity = 0.9 },
    };

    /// <summary>What to drag while the menu is open out of a session: one gourmand asking for a dish and a drink of the inn's own.</summary>
    public static InnGourmandBanner Sample(InnData? data, Func<string, string> text)
    {
        string numbers = text("Inn.Gourmand.Numbers");
        return new InnGourmandBanner("gourmand:sample", 0, text("Inn.Gourmand.Sample"), text("Inn.Gourmand.Sample"),
        [
            new InnGourmandRow(numbers[0].ToString(), text("Inn.Gourmand.SampleDish"), data?.Dishes.FirstOrDefault()?.Id, data?.Dishes.FirstOrDefault()?.Zh ?? ""),
            new InnGourmandRow(numbers[1].ToString(), string.Format(text("Inn.Gourmand.Drink"), text("Inn.Gourmand.SampleDrink")), data?.Drinks.FirstOrDefault()?.Id, data?.Drinks.FirstOrDefault()?.Zh ?? ""),
        ]);
    }

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}

/// <summary>
/// The gourmands' banners held steady (owner, live 2026-10-20: "the display of both banner and the circle should just
/// stay, not flash all the time"). The advice ranks what meets a request by how soon it's ready, worked out afresh on
/// every frame: two dishes a second apart swap places as the kitchens' queues move on, and a dish on the counter
/// comes and goes. A gourmand's rows once shown stay while every item in them still meets one of the gourmand's
/// requests on the menu (it is among the advice's options), and change only when the gourmand's requests read
/// differently (another count, recognised now) or the advice's choice is ready at least
/// <see cref="ClearlySoonerSeconds"/> sooner (a dish now on the counter, say, where the one shown is still to cook).
/// A gourmand not yet shown goes up once their rows have read the same for <see cref="ShowAfter"/>: measured on the
/// 2026-10-20 recording, a bubble read half-faded named one request, then both 0.3 s later, and a banner put up as the
/// dispatch panel opened was put away 0.3 s later.
/// </summary>
internal sealed class InnWantsHold
{
    /// <summary>Another choice replaces the rows shown only when it's ready at least this much sooner.</summary>
    public const double ClearlySoonerSeconds = 10;

    /// <summary>A gourmand's rows must read the same this long before their banner first goes up.</summary>
    public static readonly TimeSpan ShowAfter = TimeSpan.FromSeconds(0.5);

    private readonly Dictionary<int, IReadOnlyList<InnWant>> _shown = [];
    private readonly Dictionary<int, (IReadOnlyList<InnWant> Rows, TimeSpan Since)> _waiting = [];

    /// <summary>The rows to show at <paramref name="now"/>, for this moment's advice.</summary>
    public IReadOnlyList<InnWant> Update(InnAdvice advice, Func<string, string> text, TimeSpan now)
    {
        var wants = new List<InnWant>();
        var tables = advice.Gourmands.Select(g => g.Table).ToHashSet();
        foreach (int gone in _shown.Keys.Concat(_waiting.Keys).Where(t => !tables.Contains(t)).ToList())
        {
            _shown.Remove(gone);
            _waiting.Remove(gone);
        }
        foreach (var note in advice.Gourmands.OrderBy(g => g.Table))
        {
            var rows = InnGourmandView.Of(note, text);
            if (_shown.TryGetValue(note.Table, out var shown))
            {
                if (!Stands(shown, rows, note)) _shown[note.Table] = shown = rows;
                wants.AddRange(shown);
                continue;
            }
            if (!_waiting.TryGetValue(note.Table, out var waiting) || !waiting.Rows.SequenceEqual(rows)) _waiting[note.Table] = waiting = (rows, now);
            if (now - waiting.Since < ShowAfter) continue;
            _waiting.Remove(note.Table);
            _shown[note.Table] = rows;
            wants.AddRange(rows);
        }
        return wants;
    }

    /// <summary>Forgets every gourmand (the session ended).</summary>
    public void Clear()
    {
        _shown.Clear();
        _waiting.Clear();
    }

    /// <summary>The rows shown still stand against this moment's <paramref name="now"/>.</summary>
    private static bool Stands(IReadOnlyList<InnWant> shown, IReadOnlyList<InnWant> now, InnGourmandNote note)
    {
        if (shown.Count != now.Count || !shown.Select(w => w.Description).SequenceEqual(now.Select(w => w.Description))) return false;
        // Not an item (none fits, not recognised, drinks not seen): stands while the advice says the same.
        if (shown.Any(w => w.ItemId is null) || now.Any(w => w.ItemId is null)) return shown.SequenceEqual(now);
        if (note.Advice is not { } advice) return false;
        // Each row's item still meets its own request.
        for (int i = 0; i < shown.Count; i++)
            if (shown[i].Request >= advice.Requests.Count || advice.Requests[shown[i].Request].Options.All(o => o.Id != shown[i].ItemId)) return false;
        if (shown.SequenceEqual(now)) return true;
        var ready = advice.Requests.SelectMany(r => r.Options).GroupBy(o => o.Id).ToDictionary(g => g.Key, g => g.Min(o => o.ReadyIn));
        double Ready(IReadOnlyList<InnWant> wants) => wants.Max(w => ready.GetValueOrDefault(w.ItemId!));
        return Ready(now) + ClearlySoonerSeconds > Ready(shown);
    }
}

/// <summary>How a gourmand's tile is drawn (<see cref="InnGourmandView.Tile"/>).</summary>
internal enum InnGourmandShape
{
    /// <summary>The table over the items side by side, each with its name and the request in plain words.</summary>
    Full,

    /// <summary>The items side by side with their names, the table on the first picture; no request words (many gourmands).</summary>
    Compact,

    /// <summary>Beside the open dispatch panel: the table over the items one above the other, each with its name.</summary>
    Tall,

    /// <summary>Beside the open panel with many gourmands: the table over the pictures alone.</summary>
    TallBare,
}

/// <summary>
/// Where the gourmands' tiles go (owner, 2026-10-21: "the banner for gormand suggestion is too small for human to read, make
/// at least the picture bigger, at least same size as the circule today"): each a tile with pictures as large as a kitchen
/// circle's, placed one by one in table order into two columns, never over the game's Full Blaze meter, the held-dishes
/// tray, the kitchens' circles or another tile, and kept on the window:
/// <list type="bullet">
/// <item>With the dispatch panel closed: the columns' right edge at the player's <c>InnGourmandRight</c> and their top at
/// <c>InnGourmandTop</c> (fractions of the window: by default 0.985 and 0.2, right of table 6's card and below the status at
/// the top right), the odd tables in the left column and the even ones in the right (one gourmand: the right column), each
/// going down past what it would cover. <see cref="InnGourmandShape.Full"/> while all fit, else <see cref="InnGourmandShape.Compact"/>.</item>
/// <item>With the panel open (owner, 2026-10-21: "gourmand's requirement window still flashes always ... the requirement is
/// gone"; until then they were hidden while it was open): in the strips left and right of the panel, from the top,
/// <see cref="InnGourmandShape.Tall"/> while all fit, else <see cref="InnGourmandShape.TallBare"/>, clear of the UID in the
/// lower-left corner and the circles below the panel.</item>
/// </list>
/// If even the smaller shape doesn't fit (a big player size), the tiles are drawn smaller until they do.
/// </summary>
internal static class InnGourmandLayout
{
    /// <summary>The stack's distance from the window's right edge and its top, in a window of <paramref name="width"/> × <paramref name="height"/>.</summary>
    public static (double FromRight, double Top) Place(double width, double height, double right, double top) =>
        (Math.Clamp((1 - right) * width, 0, width), Math.Clamp(top * height, 0, height));

    /// <summary>
    /// The game's Full Blaze meter on the right (its ring, the fire count and the streak under it), as fractions of the
    /// window's centred 16:9 area, a little wider than measured on the 4K fixtures (session-two-dishes.jpg: x 0.825–0.98,
    /// y 0.48–0.67).
    /// </summary>
    public const double MeterLeft = 0.82, MeterTop = 0.465, MeterBottom = 0.685;

    /// <summary>The held-dishes tray, its 派单 button and the bar right of it, on the 16:9 area (session-two-dishes.jpg: x 0.38–0.78, y 0.855–0.94).</summary>
    public static readonly Rect Tray = new(0.38, 0.855, 0.40, 0.085);

    /// <summary>The open dispatch panel on the 16:9 area (the 4K panel frames: x 0.109–0.891, y 0.194–0.853), a little wider.</summary>
    public static readonly Rect Panel = new(0.105, 0.185, 0.79, 0.68);

    /// <summary>The UID and ping in the window's lower-left corner (masked on the fixtures: x up to 0.26, y from 0.94).</summary>
    public static readonly Rect Uid = new(0, 0.93, 0.27, 0.07);

    /// <summary>How low a tile may reach, and the strips' top beside the open panel, as fractions of the window's height.</summary>
    public const double LowestBottom = 0.99, BesidePanelTop = 0.08;

    /// <summary>The gap between two tiles and the strips' margin from the window's edge, in 1080p units.</summary>
    public const double Gap = 6, Margin = 10;

    /// <summary>The smallest share of the player's size the tiles are shrunk to when even the smaller shape doesn't fit.</summary>
    private const double SmallestShrink = 0.4;

    /// <summary>
    /// Where each of <paramref name="banners"/> (by table) goes: its tile's top-left corner in the window (the overlay's units),
    /// the shape they're all drawn in and the size (<paramref name="scale"/>, the window's times the player's, or smaller).
    /// </summary>
    /// <param name="panelOpen">The dispatch panel is open: the tiles go beside it.</param>
    /// <param name="circles">Where the kitchens' circles are drawn now (above the tray or below the panel), in the window.</param>
    public static InnGourmandArrangement Arrange(IReadOnlyList<InnGourmandBanner> banners, double width, double height, double scale,
        double right, double top, bool panelOpen, Rect circles)
    {
        double unit = Math.Min(width / 16, height / 9);
        double areaLeft = (width - unit * 16) / 2, areaTop = (height - unit * 9) / 2;
        Rect OnArea(Rect r) => new(areaLeft + r.X * unit * 16, areaTop + r.Y * unit * 9, r.Width * unit * 16, r.Height * unit * 9);
        var avoid = new List<Rect> { circles, new(Uid.X * width, Uid.Y * height, Uid.Width * width, Uid.Height * height) };
        if (panelOpen) avoid.Add(OnArea(Panel));
        else
        {
            avoid.Add(OnArea(Tray));
            avoid.Add(OnArea(new Rect(MeterLeft, MeterTop, 1 - MeterLeft, MeterBottom - MeterTop)));
        }
        var shapes = panelOpen ? new[] { InnGourmandShape.Tall, InnGourmandShape.TallBare } : [InnGourmandShape.Full, InnGourmandShape.Compact];

        InnGourmandArrangement? Fit(InnGourmandShape shape, double s)
        {
            double w = InnGourmandView.Width(shape) * s, gap = Gap * s, margin = Margin * s;
            double start = panelOpen ? BesidePanelTop * height : Math.Clamp(top * height, 0, height);
            double rightEdge = panelOpen ? width - margin : Math.Clamp(right * width, w, width);
            // Two columns; their order of preference for each tile alternates (odd tables first in the first column).
            double[] columns = panelOpen ? [rightEdge - w, margin] : banners.Count == 1 ? [rightEdge - w] : [rightEdge - 2 * w - gap, rightEdge - w];
            double[] cursor = [.. columns.Select(_ => start)];
            var placed = new List<Rect>();
            var places = new List<Point>();
            for (int i = 0; i < banners.Count; i++)
            {
                double h = InnGourmandView.Height(shape, banners[i].Rows.Count) * s;
                bool done = false;
                foreach (int c in Enumerable.Range(0, columns.Length).Select(c => (c + i) % columns.Length))
                {
                    double y = cursor[c];
                    while (y + h <= LowestBottom * height)
                    {
                        var rect = new Rect(columns[c], y, w, h);
                        var blocks = avoid.Concat(placed).Where(b => Overlap(rect, b)).ToList();
                        if (blocks.Count == 0) break;
                        y = blocks.Max(b => b.Bottom) + gap;
                    }
                    if (y + h > LowestBottom * height) continue;
                    placed.Add(new Rect(columns[c], y, w, h));
                    places.Add(new Point(columns[c], y));
                    cursor[c] = y + h + gap;
                    done = true;
                    break;
                }
                if (!done) return null;
            }
            return new InnGourmandArrangement(shape, s, places);
        }

        foreach (var shape in shapes)
            if (Fit(shape, scale) is { } fits) return fits;
        for (double s = scale * 0.95; s >= scale * SmallestShrink; s *= 0.95)
            if (Fit(shapes[^1], s) is { } smaller) return smaller;
        // Nothing fits even that small (a window a few hundred pixels tall): one under another from the top right.
        var stacked = new List<Point>();
        double at = 0;
        foreach (var banner in banners)
        {
            stacked.Add(new Point(width - InnGourmandView.Width(shapes[^1]) * scale * SmallestShrink, at));
            at += (InnGourmandView.Height(shapes[^1], banner.Rows.Count) + Gap) * scale * SmallestShrink;
        }
        return new InnGourmandArrangement(shapes[^1], scale * SmallestShrink, stacked);
    }

    /// <summary>Two rectangles share more than a sliver (a pixel's rounding is no overlap).</summary>
    private static bool Overlap(Rect a, Rect b)
    {
        var both = Rect.Intersect(a, b);
        return !both.IsEmpty && both.Width > 0.5 && both.Height > 0.5;
    }
}

/// <summary>Where the gourmands' tiles go (<see cref="InnGourmandLayout.Arrange"/>).</summary>
/// <param name="Shape">How they're all drawn.</param>
/// <param name="Scale">The size they're drawn at: the one asked for, or smaller when even the smaller shape didn't fit.</param>
/// <param name="Places">Each tile's slot's top-left corner in the window, by table (a tile of one request drawn at its slot's right).</param>
internal sealed record InnGourmandArrangement(InnGourmandShape Shape, double Scale, IReadOnlyList<Point> Places);
