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
/// keeps the request until it's fulfilled), held steady (<see cref="InnWantsHold"/>), each at its table's fixed spot
/// (<see cref="InnGourmandLayout"/>). Every string from i18n/zh-CN.xaml.
/// </summary>
internal static class InnGourmandView
{
    /// <summary>
    /// The most banners up at once: every table a gourmand's (owner, 2026-10-21: "we need to be prepared to support all 6
    /// tables are gourmand"), each at its own spot (<see cref="InnGourmandLayout"/>).
    /// </summary>
    public const int MostBanners = TableCardReader.Tables;

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



    /// <summary>
    /// A tile's parts at 1080p (scaled with the window, and smaller with the player's size): the picture of a one-request
    /// gourmand's item as large as a kitchen circle's (<see cref="InnKitchenCircleView.PictureSize"/>; owner, 2026-10-21: "at
    /// least same size as the circule today"); two items side by side as large as the spot beside the open dispatch panel
    /// allows (<see cref="InnGourmandLayout.SpotWidth"/>: 0.105 of the 16:9 area, 201 at 1080p, holds two of 90 and not two of
    /// 112); the item's name under it on up to two lines; the table and the request's number as pills on the first picture.
    /// The words are smaller than the circles' 20, not the pictures (the spots are what's left of the screen).
    /// </summary>
    public const double PictureSize = InnKitchenCircleView.PictureSize, PairPictureSize = 90, NameFont = 16, NameLineHeight = 19,
        NameHeight = 2 * NameLineHeight, Padding = 2, ItemGap = 6, BadgeFont = 16;

    /// <summary>The most requests a gourmand makes: a tile holds this many items.</summary>
    public const int MostRequests = GourmandAdvisor.MaxRequests;

    /// <summary>An item's picture in a tile of <paramref name="requests"/> items: the circles' size alone, <see cref="PairPictureSize"/> two side by side.</summary>
    public static double Picture(int requests) => Math.Clamp(requests, 1, MostRequests) == 1 ? PictureSize : PairPictureSize;

    /// <summary>A tile's width, at 1080p, for a gourmand of <paramref name="requests"/> requests.</summary>
    public static double Width(int requests)
    {
        int n = Math.Clamp(requests, 1, MostRequests);
        return 2 * Padding + n * Picture(n) + (n - 1) * ItemGap;
    }

    /// <summary>A tile's height, at 1080p, for a gourmand of <paramref name="requests"/> requests.</summary>
    public static double Height(int requests) => 2 * Padding + Picture(requests) + NameHeight;

    private static readonly FontFamily Font = new("Microsoft YaHei UI, Segoe UI");
    private static readonly Brush Back = Frozen(new SolidColorBrush(Color.FromArgb(0xD8, 0x16, 0x18, 0x1C)));
    private static readonly Brush Slot = Frozen(new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)));
    private static readonly Brush Ink = Frozen(new SolidColorBrush(Color.FromRgb(0x14, 0x16, 0x1A)));

    /// <summary>
    /// A gourmand's tile as the overlay draws it, at 1080p (<see cref="Width"/> × <see cref="Height"/>): a dark rounded card
    /// with a gold edge, per request its item's picture (<see cref="Picture"/>) with the request's number over its right
    /// corner and the item's name under it; the table (Inn.Gourmand.Table) over the first picture's left corner. A request
    /// with no item shows an empty disc and why.
    /// </summary>
    /// <param name="picture">A dish's or drink's picture by id; null when the package has none (an empty disc is drawn).</param>
    public static Border Tile(InnGourmandBanner banner, Func<string, BitmapSource?> picture)
    {
        int n = Math.Clamp(banner.Rows.Count, 1, MostRequests);
        double size = Picture(n);
        var items = new StackPanel { Orientation = Orientation.Horizontal };
        for (int i = 0; i < banner.Rows.Count; i++)
        {
            var row = banner.Rows[i];
            var item = new StackPanel { Width = size, Margin = i == 0 ? new Thickness(0) : new Thickness(ItemGap, 0, 0, 0) };
            var disc = new Grid { Width = size, Height = size };
            disc.Children.Add(new Border { CornerRadius = new CornerRadius(14), Background = Slot });
            if (row.PictureId is { } id && picture(id) is { } image)
                disc.Children.Add(new Image { Source = image, Width = size, Height = size, Stretch = Stretch.UniformToFill });
            if (i == 0) disc.Children.Add(Badge(banner.Short, HorizontalAlignment.Left));
            if (row.Number.Length > 0) disc.Children.Add(Badge(row.Number, HorizontalAlignment.Right));
            item.Children.Add(disc);
            // The item's name; with nothing to name yet (a drink asked for before any drink of the menu was seen), the request.
            if ((row.Item ?? row.Request) is { } words) item.Children.Add(Name(words, size));
            items.Children.Add(item);
        }
        return new Border
        {
            Width = Width(n),
            Height = Height(n),
            Background = Back,
            BorderBrush = ComboBanner.Accent,
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(Padding - 2),
            Child = items,
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

    /// <summary>An item's name under its picture: on up to two lines, the rest cut with an ellipsis.</summary>
    private static TextBlock Name(string words, double width) => new()
    {
        Text = words,
        FontSize = NameFont,
        FontWeight = FontWeights.Bold,
        Foreground = ComboBanner.Accent,
        FontFamily = Font,
        TextAlignment = TextAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
        TextWrapping = TextWrapping.Wrap,
        TextTrimming = TextTrimming.CharacterEllipsis,
        LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        LineHeight = NameLineHeight,
        Height = NameHeight,
        Width = width,
        Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 4, ShadowDepth = 0, Opacity = 0.9 },
    };

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

/// <summary>
/// Where the gourmands' tiles go (owner, live 2026-10-21, test_yuanyuan8: "split the 6 gourmand table hints at the 6 corners of
/// the window, make the position FIXED, so user does not need to chase around and not know what to look at"): each table at
/// its own spot, whoever else is there and whether the dispatch panel is open or not, so nothing ever moves. Tables 1–3 down
/// the left edge, 4–6 down the right, top to bottom: the first half of the table cards' row on the left, the second half on
/// the right, in the same order. The spots are what the game's own screen leaves free in both states, measured on the 4K
/// frames as fractions of the window's centred 16:9 area:
/// <list type="bullet">
/// <item>Across: beside the open dispatch panel (<see cref="Panel"/>, x 0.105–0.895), so <see cref="SpotWidth"/> 190 at
/// 1080p.</item>
/// <item>Left, down: below the timer, theme and objective block (to y 0.475 on every recorded theme: <see cref="LeftTop"/>)
/// and above the UID (<see cref="Uid"/>): three spots of <see cref="SpotHeight"/>.</item>
/// <item>Right, down: table 4 below the status at the top right (<see cref="RightTop"/>: the avatar and its line end at y
/// 0.195) and above the Full Blaze meter (<see cref="MeterTop"/>, y 0.465); tables 5 and 6 below the meter
/// (<see cref="MeterBottom"/>, 0.685) down to <see cref="LowestBottom"/>.</item>
/// </list>
/// None of them is over the table cards at the top, the held-dishes tray and its 派单 button, the kitchens' circles (above
/// the tray or below the open panel), the meter, the panel or the UID (InnSixGourmandsTests). A tile stands in its spot's
/// top outer corner (left spots: left; right spots: right), its height at most the spot's. The player's size makes the
/// tiles smaller, never larger than their spot.
/// </summary>
internal static class InnGourmandLayout
{
    /// <summary>A spot's size at 1080p: the widest and tallest tile (<see cref="InnGourmandView.Width"/>, <see cref="InnGourmandView.Height"/>).</summary>
    public static readonly double SpotWidth = Enumerable.Range(1, InnGourmandView.MostRequests).Max(InnGourmandView.Width),
        SpotHeight = Enumerable.Range(1, InnGourmandView.MostRequests).Max(InnGourmandView.Height);

    /// <summary>
    /// The game's Full Blaze meter on the right (its ring, the fire count and the streak under it, the countdown while it runs),
    /// as fractions of the window's centred 16:9 area, a little wider than measured on the 4K fixtures (session-two-dishes.jpg:
    /// x 0.825–0.98, y 0.48–0.67).
    /// </summary>
    public const double MeterLeft = 0.82, MeterTop = 0.465, MeterBottom = 0.685;

    /// <summary>The open dispatch panel on the 16:9 area (the 4K panel frames: x 0.109–0.891, y 0.194–0.853), a little wider.</summary>
    public static readonly Rect Panel = new(0.105, 0.185, 0.79, 0.68);

    /// <summary>The UID and ping in the window's lower-left corner (masked on the fixtures: x up to 0.26, y from 0.94).</summary>
    public static readonly Rect Uid = new(0, 0.93, 0.27, 0.07);

    /// <summary>The left spots' top (below the objective block) and the right top spot's (below the status at the top right), on the 16:9 area.</summary>
    public const double LeftTop = 0.485, RightTop = 0.2;

    /// <summary>How low a tile may reach, as a fraction of the 16:9 area's height.</summary>
    public const double LowestBottom = 0.99;

    /// <summary>The gap between two spots in a column and the spots' margin from the area's edge, in 1080p units.</summary>
    public const double Gap = 6, Margin = 6;

    /// <summary>Whether <paramref name="table"/>'s spot is on the left edge (tables 1–3) or the right (4–6).</summary>
    public static bool OnLeft(int table) => table <= TableCardReader.Tables / 2;

    /// <summary>
    /// <paramref name="table"/>'s spot in a window of <paramref name="width"/> × <paramref name="height"/> (the overlay's units):
    /// <see cref="SpotWidth"/> × <see cref="SpotHeight"/> at the window's own scale (the 16:9 area's height over 1080).
    /// </summary>
    public static Rect Spot(int table, double width, double height)
    {
        double unit = Math.Min(width / 16, height / 9);
        double areaLeft = (width - unit * 16) / 2, areaTop = (height - unit * 9) / 2, areaHeight = unit * 9, s = areaHeight / 1080;
        double w = SpotWidth * s, h = SpotHeight * s, gap = Gap * s, margin = Margin * s;
        int t = Math.Clamp(table, 1, TableCardReader.Tables);
        double top = t switch
        {
            1 or 2 or 3 => LeftTop * areaHeight + (t - 1) * (h + gap),
            4 => RightTop * areaHeight,
            5 => MeterBottom * areaHeight + gap,
            _ => LowestBottom * areaHeight - h,
        };
        double left = OnLeft(t) ? areaLeft + margin : areaLeft + unit * 16 - margin - w;
        return new Rect(left, areaTop + top, w, h);
    }

    /// <summary>
    /// Where each of <paramref name="banners"/> goes: its tile's top-left corner in the window, at its table's spot, and the
    /// size it's drawn at: <paramref name="scale"/> (the window's times the player's), at most the window's own (the spot's).
    /// </summary>
    public static InnGourmandArrangement Arrange(IReadOnlyList<InnGourmandBanner> banners, double width, double height, double scale)
    {
        double unit = Math.Min(width / 16, height / 9), fit = unit * 9 / 1080;
        double s = Math.Min(scale, fit);
        var places = banners.Select(b =>
        {
            var spot = Spot(b.Table, width, height);
            double w = InnGourmandView.Width(b.Rows.Count) * s;
            return new Point(OnLeft(b.Table) ? spot.Left : spot.Right - w, spot.Top);
        }).ToList();
        return new InnGourmandArrangement(s, places);
    }
}

/// <summary>Where the gourmands' tiles go (<see cref="InnGourmandLayout.Arrange"/>).</summary>
/// <param name="Scale">The size they're drawn at: the one asked for, at most the window's own.</param>
/// <param name="Places">Each tile's top-left corner in the window, in the banners' order.</param>
internal sealed record InnGourmandArrangement(double Scale, IReadOnlyList<Point> Places);
