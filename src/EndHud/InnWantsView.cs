using System.Windows;
using System.Windows.Media.Imaging;
using EndHud.Vision;
using EndHud.Vision.Inn;

namespace EndHud;

/// <summary>One thing a gourmand wants: the best dish or drink on the menu for one of their requests.</summary>
/// <param name="ItemId">The dish's or drink's id (its picture).</param>
internal sealed record InnWant(int Table, string ItemId, string Name);

/// <summary>
/// What each gourmand wants, as round glowing circles in the gauges' buff style (owner, live 2026-10-20: "for 老饕, reuse
/// the buff to show the dish/drink they want to be a lot more visible"): one circle per request, the dish's or drink's
/// picture in the middle, the table under it ("桌4") and the item's name below that, glowing for as long as the
/// gourmand's order is open (<see cref="InnSession"/> keeps the request until it's fulfilled). For two requests, the
/// items in the order to serve them (each fills a request perfectly); otherwise each request's best option. The panel
/// (<see cref="InnStripView"/>) keeps the detail: the requests in words, a second choice, where each item is.
/// </summary>
internal static class InnWantsView
{
    /// <summary>The most circles shown: two gourmands of two requests each; they fit between the table cards and the top right's status (InnWantsLayoutTests).</summary>
    public const int MostCircles = 4;

    /// <summary>A dish's picture fills most of the ring (a status glyph fills 0.78).</summary>
    internal const double PictureShare = 0.95;

    public static IReadOnlyList<InnWant> Of(InnAdvice advice)
    {
        var wants = new List<InnWant>();
        foreach (var note in advice.Gourmands.OrderBy(g => g.Table))
        {
            if (note.Advice is not { } gourmand) continue;
            IEnumerable<GourmandOption> items = gourmand.Serve is { } serve && serve.Count == gourmand.Requests.Count
                ? serve
                : gourmand.Requests.Select(r => r.Options.FirstOrDefault()).OfType<GourmandOption>();
            wants.AddRange(items.Select(o => new InnWant(note.Table, o.Id, o.Dish?.Zh ?? o.Drink!.Zh)));
        }
        return [.. wants.Take(MostCircles)];
    }

    /// <param name="picture">A dish's or drink's picture by id; null when the package has none (the circle is drawn without it).</param>
    /// <param name="text">The i18n lookup (<see cref="Loc.Get"/>; the tests pass zh-CN.xaml's strings).</param>
    public static GaugeBoard Board(IReadOnlyList<InnWant> wants, Func<string, BitmapSource?> picture, Func<string, string> text) =>
        new([.. wants.Select((w, i) => new Gauge(GaugeKind.Buff, picture(w.ItemId), null, string.Format(text("Inn.Want.Table"), w.Table), null, Ready: true)
        {
            Detail = w.Name,
            // Evenly spaced: the table under each circle tells two gourmands apart, and four must fit before the status.
            FirstOfOperator = i == 0,
            PictureShare = PictureShare,
        })]);

    /// <summary>What would change on screen.</summary>
    public static string Key(IReadOnlyList<InnWant> wants) => string.Join("|", wants.Select(w => $"{w.Table}:{w.ItemId}"));
}

/// <summary>
/// Where the gourmands' circles go: right of the table cards at the top of the session's screen, where the cards'
/// row leaves the screen free up to the status at the top right (the 4K frames: table 6's card ends at x 2258, the
/// status ("上菜品中", "结账中") starts at about x 3200; the dispatch panel's top edge is at y 418). Anchored to the
/// top-left corner like the table cards (InnLayout), so it follows them at any window size; not dragged, nothing in config.
/// </summary>
internal static class InnWantsLayout
{
    /// <summary>The circles' top-left corner at 4K.</summary>
    internal const double Left4K = 2300, Top4K = 20;

    /// <summary>What the circles must stay clear of at 4K: the top right's status (right) and the dispatch panel's top (bottom).</summary>
    internal const double Right4K = 3180, Bottom4K = 418;

    /// <summary>The circles' top-left corner in a game window of <paramref name="width"/> × <paramref name="height"/> (the overlay's units).</summary>
    public static Point TopLeft(double width, double height)
    {
        double scale = Math.Min(width / UiScaler.ReferenceWidth, height / UiScaler.ReferenceHeight);
        return new Point(Left4K * scale, Top4K * scale);
    }
}
