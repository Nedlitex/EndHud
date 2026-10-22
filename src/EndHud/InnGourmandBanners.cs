using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
internal sealed record InnGourmandBanner(string Key, int Table, string Title, IReadOnlyList<InnGourmandRow> Rows)
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

    /// <summary>A banner's title line and each row's height, its padding, and a row's picture, at 1080p (scaled with the window).</summary>
    public const double TitleHeight = 30, RowHeight = 46, Padding = 10, PictureSize = 40;

    /// <summary>
    /// The compact banner's row height, padding and picture, at 1080p: its title beside the rows instead of over them, so a
    /// gourmand of two requests is 82 units tall instead of 142 (six fit on the right, <see cref="InnGourmandLayout.Arrange"/>).
    /// Its words a little smaller (15; the title 17) and its margins narrower, so six stay clear of the held-dishes tray.
    /// </summary>
    public const double CompactRowHeight = 36, CompactPadding = 5, CompactPictureSize = 32;

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
            return new InnGourmandBanner($"gourmand:{g.Key}", g.Key, string.Format(text("Inn.Gourmand.Title"), g.Key), rows);
        })];
    }

    /// <summary>A banner's height, at 1080p: its title and a row per request.</summary>
    public static double Height(InnGourmandBanner banner) => 2 * Padding + TitleHeight + banner.Rows.Count * RowHeight;

    /// <summary>A banner's height, at 1080p, full (<see cref="Height(InnGourmandBanner)"/>) or compact: its rows only.</summary>
    public static double Height(InnGourmandBanner banner, bool compact) =>
        compact ? 2 * CompactPadding + Math.Max(1, banner.Rows.Count) * CompactRowHeight : Height(banner);

    private static readonly FontFamily Font = new("Microsoft YaHei UI, Segoe UI");
    private static readonly Brush TextBrush = Frozen(new SolidColorBrush(Color.FromArgb(0xEE, 0xFF, 0xFF, 0xFF)));

    /// <summary>A gourmand's banner as the overlay draws it: <see cref="ComboBanner"/>'s look, a title and the rows under it.</summary>
    /// <param name="picture">A dish's or drink's picture by id; null when the package has none (the row is drawn without it).</param>
    public static Border Banner(InnGourmandBanner banner, Func<string, BitmapSource?> picture) => Banner(banner, picture, compact: false);

    /// <summary>
    /// A gourmand's banner, full (the title over the rows) or <paramref name="compact"/> (the title beside them, smaller rows:
    /// when many gourmands are up, <see cref="InnGourmandLayout.Arrange"/>).
    /// </summary>
    public static Border Banner(InnGourmandBanner banner, Func<string, BitmapSource?> picture, bool compact)
    {
        double padding = compact ? CompactPadding : Padding, rowHeight = compact ? CompactRowHeight : RowHeight;
        double pictureSize = compact ? CompactPictureSize : PictureSize, fontSize = compact ? 15 : 17;
        var rows = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        foreach (var row in banner.Rows)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Height = rowHeight };
            if (row.Request is not null)
                line.Children.Add(Words($"{row.Number} {row.Request}", TextBrush, bold: false, fontSize));
            if (row.Request is not null && row.Item is not null)
                line.Children.Add(Words(" → ", ComboBanner.Accent, bold: true, fontSize));
            if (row.PictureId is { } id && picture(id) is { } image)
                line.Children.Add(new Image { Source = image, Width = pictureSize, Height = pictureSize, Margin = new Thickness(2, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            if (row.Item is not null)
                line.Children.Add(Words(row.Item, ComboBanner.Accent, bold: row.PictureId is not null, fontSize));
            rows.Children.Add(line);
        }
        var title = new TextBlock
        {
            Text = banner.Title, FontSize = compact ? 17 : 20, FontWeight = FontWeights.Bold, Foreground = TextBrush, FontFamily = Font,
            VerticalAlignment = VerticalAlignment.Center,
        };
        // Slanted back upright inside the slanted banner, as the combo banner's content is.
        StackPanel content;
        if (compact)
        {
            title.Margin = new Thickness(0, 0, 8, 0);
            content = new StackPanel { Orientation = Orientation.Horizontal, Children = { title, rows } };
        }
        else
        {
            title.Height = TitleHeight;
            content = new StackPanel { Children = { title, rows } };
        }
        content.RenderTransform = new SkewTransform(12, 0);
        content.Margin = new Thickness(compact ? 12 : 18, padding, compact ? 16 : 28, padding);
        content.VerticalAlignment = VerticalAlignment.Center;
        var element = ComboBanner.Create(content, Height(banner, compact));
        element.HorizontalAlignment = HorizontalAlignment.Right;
        return element;
    }

    private static TextBlock Words(string words, Brush brush, bool bold, double fontSize) => new()
    {
        Text = words, FontSize = fontSize, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal, Foreground = brush, FontFamily = Font,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>What to drag while the menu is open out of a session: one gourmand asking for a dish and a drink of the inn's own.</summary>
    public static InnGourmandBanner Sample(InnData? data, Func<string, string> text)
    {
        string numbers = text("Inn.Gourmand.Numbers");
        return new InnGourmandBanner("gourmand:sample", 0, text("Inn.Gourmand.Sample"),
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

/// <summary>
/// Where the gourmands' banners go (owner, live 2026-10-21: "like the banner show on the right side"): their own stack,
/// its right edge at the player's <c>InnGourmandRight</c> and its top at <c>InnGourmandTop</c> (fractions of the game
/// window; by default the right edge near the window's, the top at 0.2: below the status at the top right and right of
/// table 6's card, above the Full Blaze meter with one gourmand), kept on the window. Draggable while the menu is open.
/// </summary>
internal static class InnGourmandLayout
{
    /// <summary>The stack's distance from the window's right edge and its top, in a window of <paramref name="width"/> × <paramref name="height"/>.</summary>
    public static (double FromRight, double Top) Place(double width, double height, double right, double top) =>
        (Math.Clamp((1 - right) * width, 0, width), Math.Clamp(top * height, 0, height));

    /// <summary>
    /// The game's Full Blaze meter on the right (its ring, the fire count and the streak under it), as fractions of the
    /// window's centred 16:9 area, a little wider than measured on the 4K fixtures (session-two-dishes.jpg: x 0.825–0.98,
    /// y 0.48–0.67): banners stay above or below it.
    /// </summary>
    public const double MeterLeft = 0.82, MeterTop = 0.465, MeterBottom = 0.685;

    /// <summary>How low a banner may reach, a fraction of the window's height (the right side is floor down there).</summary>
    public const double LowestBottom = 0.97;

    /// <summary>The frame's padding and border around the stack (OverlayWindow.xaml), in its units.</summary>
    public const double Frame = 6 + 2;

    /// <summary>The smallest share of the player's size the stack is shrunk to when even compact banners don't fit.</summary>
    private const double SmallestShrink = 0.4;

    /// <summary>
    /// Where each of <paramref name="banners"/> (by table) goes in the stack, from its top at <paramref name="top"/> (as
    /// <see cref="Place"/>) at <paramref name="scale"/> (the window's times the player's): one under another, a gap between,
    /// none over the Full Blaze meter (one that would reach it goes below it, when the stack is at the right where the meter
    /// is) or below <see cref="LowestBottom"/>. Full banners while they fit, else compact ones (six gourmands: compact, three
    /// above the meter and three below); if even those don't fit (a big player size) the stack is drawn smaller until they do.
    /// The same banners always land in the same places, in table order.
    /// </summary>
    public static InnGourmandArrangement Arrange(IReadOnlyList<InnGourmandBanner> banners, double width, double height, double scale, double right, double top)
    {
        var (_, frameTop) = Place(width, height, right, top);
        double unit = Math.Min(width / 16, height / 9);
        double areaLeft = (width - unit * 16) / 2, areaTop = (height - unit * 9) / 2;
        bool meterBeside = right * width > areaLeft + MeterLeft * unit * 16;
        double bandTop = areaTop + MeterTop * unit * 9, bandBottom = areaTop + MeterBottom * unit * 9;

        InnGourmandArrangement? Fit(bool compact, double s)
        {
            var tops = new List<double>();
            double y = frameTop + Frame * s; // on screen
            foreach (var banner in banners)
            {
                double tall = InnGourmandView.Height(banner, compact) * s;
                if (meterBeside && y < bandBottom && y + tall > bandTop) y = bandBottom;
                if (y + tall > LowestBottom * height) return null;
                tops.Add((y - frameTop) / s - Frame);
                y += tall + ComboBanner.Gap * s;
            }
            double extent = banners.Count == 0 ? 0 : tops[^1] + InnGourmandView.Height(banners[^1], compact) + ComboBanner.Gap;
            return new InnGourmandArrangement(compact, s, tops, extent);
        }

        if (Fit(false, scale) is { } full) return full;
        for (double s = scale; s >= scale * SmallestShrink; s *= 0.95)
            if (Fit(true, s) is { } compact) return compact;
        // Nothing fits even that small (a window a few hundred pixels tall): compact, as small as allowed, one under another.
        var stacked = new List<double>();
        double at = 0;
        foreach (var banner in banners)
        {
            stacked.Add(at);
            at += InnGourmandView.Height(banner, true) + ComboBanner.Gap;
        }
        return new InnGourmandArrangement(true, scale * SmallestShrink, stacked, at);
    }
}

/// <summary>Where the gourmands' banners go in their stack (<see cref="InnGourmandLayout.Arrange"/>).</summary>
/// <param name="Compact">Drawn compact (<see cref="InnGourmandView.Banner(InnGourmandBanner, Func{string, BitmapSource?}, bool)"/>).</param>
/// <param name="Scale">The size the stack is drawn at: the one asked for, or smaller when even compact banners didn't fit.</param>
/// <param name="Tops">Each banner's top in the stack, in its units (at 1080p), by table.</param>
/// <param name="Height">The stack's height in its units.</param>
internal sealed record InnGourmandArrangement(bool Compact, double Scale, IReadOnlyList<double> Tops, double Height);
