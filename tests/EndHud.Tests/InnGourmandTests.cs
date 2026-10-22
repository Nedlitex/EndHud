using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EndHud.Vision.Inn;
using Xunit;
using Xunit.Abstractions;
using static EndHud.Tests.InnTestKit;

namespace EndHud.Tests;

/// <summary>
/// What each gourmand wants, as banners on the right (<see cref="InnGourmandView"/>; owner, live 2026-10-21: "user cannot
/// distinguish between whether the two circles are alternatives or two requirements ... change the style of gourmand to
/// be something like the banner show on the right side"): one banner per gourmand, one numbered row per request, and
/// where the stack goes (<see cref="InnGourmandLayout"/>).
/// </summary>
[Collection(WpfCollection.Name)]
public class InnGourmandTests(ITestOutputHelper output)
{
    private const string A = "melon-meatball-soup", B = "fin-mato-soup", C = "mala-xiang-guo", D = "sweetbeans-on-snow", E = "meat-baozi";
    private const string Tea = "pre-tide-lungjing", Milk = "bottled-milk-a";

    /// <summary>The first and second request's numbers, as the banner writes them.</summary>
    private static string First => Text("Inn.Gourmand.Numbers")[0].ToString();

    private static string Second => Text("Inn.Gourmand.Numbers")[1].ToString();

    private static InnAdvice Advice(params InnGourmandNote[] gourmands) =>
        new(new DispatchAdvice([], [], [], []), null, null, false, gourmands, [], new HashSet<int>(), false);

    private static GourmandOption Option(string id) => Inn.Dishes.Any(d => d.Id == id)
        ? new GourmandOption(Dish(id), null, GourmandSource.Counter, null, 0)
        : new GourmandOption(null, Drink(id), GourmandSource.DrinkBar, null, 0);

    private static GourmandRequestAdvice Request(string words, params string[] options) =>
        new(new GourmandRequest("said", "x"), words, [.. options.Select(Option)]) { ForDrink = options.Length > 0 && Inn.Drinks.Any(d => d.Id == options[0]) };

    private static GourmandRequestAdvice DrinkRequest(string words, params string[] options) => Request(words, options) with { ForDrink = true };

    /// <summary>
    /// The owner's gourmand at table 4 (session-gourmand-soup-and-drink.jpg): a soup without seed oil and a drink without
    /// alcohol. One banner, titled with the table (Inn.Gourmand.Title), and a numbered row per request: the request in plain words, an arrow, the best item
    /// with its picture. A drink request says it's a drink.
    /// </summary>
    [Fact]
    public void One_banner_per_gourmand_with_a_numbered_row_per_request()
    {
        var note = new InnGourmandNote(4, new GourmandAdvice([Request("soup, no seed oil", A, D), DrinkRequest("no alcohol", Tea, Milk)], null), null, false);

        var banners = InnGourmandView.Banners(InnGourmandView.Of(Advice(note), Text), Text);

        var banner = Assert.Single(banners);
        Assert.Equal(Format("Inn.Gourmand.Title", 4), banner.Title);
        Assert.Equal([(First, "soup, no seed oil", A, Dish(A).Zh), (Second, Format("Inn.Gourmand.Drink", "no alcohol"), Tea, Drink(Tea).Zh)],
            banner.Rows.Select(r => (r.Number, r.Request, r.PictureId, r.Item)));

        // Drawn: the table and the first request's number on the first picture, its item's name, then the second's number and
        // item. The request in plain words isn't drawn (the spot beside the open panel has no room; the number tells the two apart).
        var (texts, pictures) = OnSta(() =>
        {
            var element = InnGourmandView.Tile(banner, _ => Picture());
            element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            element.Arrange(new Rect(element.DesiredSize));
            var all = Descendants(element).ToList();
            return (all.OfType<TextBlock>().Select(t => t.Text).ToList(), all.OfType<Image>().Count());
        });
        Assert.Equal([Format("Inn.Gourmand.Table", 4), First, Dish(A).Zh, Second, Drink(Tea).Zh], texts);
        Assert.Equal(2, pictures);
    }

    /// <summary>
    /// For two requests the advice chooses the items to serve together (<see cref="GourmandAdvice.Serve"/>, in the order to
    /// serve them); each goes on the row of the request it fills, so the rows stay in the order said.
    /// </summary>
    [Fact]
    public void The_items_chosen_to_serve_together_each_go_on_their_own_requests_row()
    {
        var serve = new[] { Option(Milk), Option(A) };
        var note = new InnGourmandNote(2, new GourmandAdvice([Request("a soup", A, B), DrinkRequest("milky", Tea, Milk)], serve), null, false);

        Assert.Equal([(0, A), (1, Milk)], InnGourmandView.Of(Advice(note), Text).Select(w => (w.Request, w.ItemId)));
    }

    /// <summary>
    /// A request nothing on the menu meets says so; a gourmand not recognised, that; a drink request before any drink of
    /// the menu was seen shows the request alone ("none fits" would be wrong). No picture is asked for a row without an item.
    /// </summary>
    [Fact]
    public void A_request_met_by_nothing_not_recognised_or_with_drinks_unseen_says_so()
    {
        var unknown = new InnGourmandNote(1, null, "read text", false);
        var unmet = new InnGourmandNote(3, new GourmandAdvice([Request("a dessert"), Request("a side", C)], null), null, false);
        var drinksUnseen = new InnGourmandNote(5, new GourmandAdvice([DrinkRequest("tea")], null), null, DrinksUnknown: true);

        var banners = InnGourmandView.Banners(InnGourmandView.Of(Advice(unknown, unmet, drinksUnseen), Text), Text);

        Assert.Equal([Format("Inn.Gourmand.Title", 1), Format("Inn.Gourmand.Title", 3), Format("Inn.Gourmand.Title", 5)], banners.Select(b => b.Title));
        Assert.Equal([("", null, null, Text("Inn.Gourmand.Unknown"))], banners[0].Rows.Select(r => (r.Number, r.Request, r.PictureId, r.Item)));
        Assert.Equal([(First, "a dessert", null, Text("Inn.Gourmand.None")), (Second, "a side", C, Dish(C).Zh)], banners[1].Rows.Select(r => (r.Number, r.Request, r.PictureId, r.Item)));
        Assert.Equal([(First, Format("Inn.Gourmand.Drink", "tea"), (string?)null, (string?)null)], banners[2].Rows.Select(r => (r.Number, r.Request, r.PictureId, r.Item)));
        var asked = new List<string>();
        OnSta(() => banners.Select(b => InnGourmandView.Tile(b, id => { asked.Add(id); return null; })).ToList());
        Assert.Equal([C], asked);
    }

    /// <summary>
    /// Through the overlay's own display: the tile is up while the order is open, and stays while the dispatch panel is open,
    /// at its fixed spot beside it (<see cref="InnGourmandLayout"/>; owner, 2026-10-21, test_yuanyuan6 at 6:10: "gourmand's
    /// requirement window still flashes always ... hasnt been fulfilled but the requirement is gone", it was hidden while the
    /// panel was open), gone once fulfilled.
    /// </summary>
    [Fact]
    public void The_tile_stays_while_the_order_is_open_beside_the_open_panel_and_goes_when_fulfilled()
    {
        var session = NewSession();
        var display = new InnDisplay();
        session.Update(SetupScreen(0, 1));
        session.Update(MenuScreen(1, A, B, C, D, E, Tea, Milk));
        var (request, _) = Inn.Gourmands.SelectMany(g => g.Requests.Select(r => (r, GourmandCondition.Parse(r.When, Inn))))
            .First(x => !x.Item2.ForDrink && new[] { A, B, C, D, E }.Any(id => x.Item2.MetBy(Dish(id))));
        var gourmand = Tables(new TableCard(4, TableState.GourmandOrder, [], []));
        List<InnWant> Wants(double at) => [.. display.Views(session, S(at), callsEnabled: true, Text).Wants];

        display.Show(session, Session(10, tables: gourmand, bubble: new GourmandBubble(request.Text, [request.Text])), S(10));
        Assert.Empty(Wants(10)); // up once what it says has stood a moment (InnWantsHold.ShowAfter)
        Assert.Equal(4, Assert.Single(Wants(10.6)).Table);

        display.Show(session, Session(11, tables: gourmand, panel: Panel(1, 1, [A, B, C])), S(11));
        display.Show(session, Session(11.3, tables: gourmand, panel: Panel(1, 1, [A, B, C])), S(11.3));
        var open = display.Views(session, S(11.3), callsEnabled: true, Text);
        Assert.True(open.Away);
        Assert.Equal(4, Assert.Single(Wants(11.3)).Table);

        display.Show(session, Session(11.6 + InnSession.PanelClosedAfter.TotalSeconds, tables: gourmand), S(11.6 + InnSession.PanelClosedAfter.TotalSeconds));
        Assert.Single(Wants(11.6 + InnSession.PanelClosedAfter.TotalSeconds));

        // Eating, read on two readings in a row.
        display.Show(session, Session(20, tables: Tables(new TableCard(4, TableState.GourmandEating, [], []))), S(20));
        display.Show(session, Session(20.3, tables: Tables(new TableCard(4, TableState.GourmandEating, [], []))), S(20.3));
        Assert.Empty(Wants(20.3));
    }

    /// <summary>
    /// Readable (owner, 2026-10-21: "the banner for gormand suggestion is too small for human to read, make at least the
    /// picture bigger, at least same size as the circule today"): a one-request gourmand's picture is the kitchen circle's (112
    /// at 1080p, the old banner's 40); two side by side are as large as the spot beside the open panel holds (90: two of 112
    /// need 234 of its 201), the names under them smaller (16) rather than the pictures. The tile, with the longest dish
    /// names, is exactly <see cref="InnGourmandView.Width"/> × <see cref="InnGourmandView.Height"/>.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void A_tiles_pictures_are_as_large_as_its_spot_allows(int requests)
    {
        var said = Inn.Gourmands.SelectMany(g => g.Requests).Select(r => GourmandCondition.Parse(r.When, Inn))
            .Select(c => c.ForDrink ? Format("Inn.Gourmand.Drink", c.Describe()) : c.Describe()).OrderByDescending(d => d.Length).Take(2).ToList();
        var names = Inn.Dishes.OrderByDescending(d => d.Zh.Length).ThenBy(d => d.Id).Take(2).ToList();
        var banner = Assert.Single(InnGourmandView.Banners([.. Enumerable.Range(0, requests).Select(i => new InnWant(6, i, said[i], names[i].Id, names[i].Zh))], Text));

        var (pictures, fonts, size) = OnSta(() =>
        {
            var element = InnGourmandView.Tile(banner, PackagePicture);
            element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            element.Arrange(new Rect(element.DesiredSize));
            var all = Descendants(element).ToList();
            return (all.OfType<Image>().Select(i => i.ActualHeight).ToList(), all.OfType<TextBlock>().Select(t => t.FontSize).ToList(), element.DesiredSize);
        });
        output.WriteLine($"{requests}: pictures {string.Join(",", pictures)}, fonts {string.Join(",", fonts)}, tile {size.Width:F0}x{size.Height:F0}");
        Assert.Equal(requests, pictures.Count);
        Assert.All(pictures, p => Assert.Equal(InnKitchenCircleView.PictureSize, p));
        Assert.All(fonts, f => Assert.True(f >= 20, $"a word {f} units tall"));
        Assert.Equal((InnGourmandView.Width(requests), InnGourmandView.Height(requests)), (size.Width, size.Height));
        Assert.True(size.Width <= InnGourmandLayout.SpotWidth && size.Height <= InnGourmandLayout.SpotHeight);
    }

    private static BitmapSource Picture() => BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[16], 8);

    private static BitmapSource? PackagePicture(string id)
    {
        string path = InnData.PicturePath(Folder, id);
        if (!File.Exists(path)) return null;
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(path);
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static T OnSta<T>(Func<T> work)
    {
        T result = default!;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { result = work(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw failure;
        return result;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var deeper in Descendants(child)) yield return deeper;
        }
    }
}
