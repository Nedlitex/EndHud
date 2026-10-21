using System.Windows;
using System.Windows.Media.Imaging;
using EndHud.Vision;
using EndHud.Vision.Inn;

namespace EndHud;

/// <summary>One thing a gourmand wants: the best dish or drink on the menu for one of their requests, or what stops one being named.</summary>
/// <param name="ItemId">The dish's or drink's id (its picture); null for a request nothing on the menu meets, or not recognised.</param>
/// <param name="Name">The item's name, or the words saying why there's none.</param>
internal sealed record InnWant(int Table, string? ItemId, string Name);

/// <summary>
/// What each gourmand wants, as round glowing circles in the gauges' buff style (owner, live 2026-10-20: "for [gourmands], reuse
/// the buff to show the dish/drink they want to be a lot more visible"; "no longer need the older panel for gourmand"):
/// one circle per request, the dish's or drink's picture in the middle, the table under it ("table 4", Inn.Want.Table)
/// and the item's name below that, up for as long as the gourmand's order is open (<see cref="InnSession"/> keeps the
/// request until it's fulfilled). For two requests, the items in the order to serve them (each fills a request
/// perfectly); otherwise each request's best option. A request nothing on the menu meets is a circle saying so
/// (Inn.Want.None); a gourmand whose words weren't recognised, one saying that (Inn.Want.Unknown). A request for a drink
/// before any drink of the menu was seen has no circle: "none fits" would be wrong.
/// </summary>
internal static class InnWantsView
{
    /// <summary>The most circles shown: three gourmands of two requests each; they fit across the bottom middle at 1080p (InnWantsTests).</summary>
    public const int MostCircles = 6;

    /// <summary>A dish's picture fills most of the ring (a status glyph fills 0.78).</summary>
    internal const double PictureShare = 0.95;

    /// <param name="text">The i18n lookup (<see cref="Loc.Get"/>; the tests pass zh-CN.xaml's strings).</param>
    public static IReadOnlyList<InnWant> Of(InnAdvice advice, Func<string, string> text)
    {
        var wants = new List<InnWant>();
        foreach (var note in advice.Gourmands.OrderBy(g => g.Table))
        {
            if (note.Advice is not { } gourmand)
            {
                wants.Add(new InnWant(note.Table, null, text("Inn.Want.Unknown")));
                continue;
            }
            if (gourmand.Serve is { } serve && serve.Count == gourmand.Requests.Count)
            {
                wants.AddRange(serve.Select(o => Want(note.Table, o)));
                continue;
            }
            foreach (var request in gourmand.Requests)
            {
                if (request.Options.FirstOrDefault() is { } best) wants.Add(Want(note.Table, best));
                else if (!note.DrinksUnknown) wants.Add(new InnWant(note.Table, null, text("Inn.Want.None")));
            }
        }
        return [.. wants.Take(MostCircles)];
    }

    private static InnWant Want(int table, GourmandOption option) => new(table, option.Id, option.Dish?.Zh ?? option.Drink!.Zh);

    /// <param name="picture">A dish's or drink's picture by id; null when the package has none (the circle is drawn without it).</param>
    /// <param name="text">The i18n lookup (<see cref="Loc.Get"/>; the tests pass zh-CN.xaml's strings).</param>
    public static GaugeBoard Board(IReadOnlyList<InnWant> wants, Func<string, BitmapSource?> picture, Func<string, string> text) =>
        new([.. wants.Select((w, i) => new Gauge(GaugeKind.Buff, w.ItemId is { } id ? picture(id) : null, null, string.Format(text("Inn.Want.Table"), w.Table), null, Ready: true)
        {
            Detail = w.Name,
            // Evenly spaced: the table under each circle tells two gourmands apart.
            FirstOfOperator = i == 0,
            PictureShare = PictureShare,
        })]);

    /// <summary>What would change on screen.</summary>
    public static string Key(IReadOnlyList<InnWant> wants) => string.Join("|", wants.Select(w => $"{w.Table}:{w.ItemId}:{w.Name}"));
}

/// <summary>
/// Where the gourmands' circles go (owner, live 2026-10-20: "the circle should be at where the buff is (middle bottom)"):
/// the gauges' place, centred on the player's GaugesCenterX and standing on GaugesBottomY (bottom middle by default). The
/// gauges show only in a fight, never in the inn, so the place is free; kept inside the window.
/// </summary>
internal static class InnWantsLayout
{
    /// <summary>The circles' top-left corner for a board of <paramref name="board"/>'s size (on screen) in a window of <paramref name="width"/> × <paramref name="height"/>.</summary>
    /// <param name="centerX">The gauges' centre, a fraction of the window's width (HudConfig.GaugesCenterX).</param>
    /// <param name="bottomY">The gauges' bottom, a fraction of the window's height (HudConfig.GaugesBottomY).</param>
    public static Point TopLeft(double width, double height, Size board, double centerX, double bottomY)
    {
        double left = Math.Clamp(centerX * width - board.Width / 2, 0, Math.Max(0, width - board.Width));
        double top = Math.Clamp(bottomY * height - board.Height, 0, Math.Max(0, height - board.Height));
        return new Point(left, top);
    }
}
