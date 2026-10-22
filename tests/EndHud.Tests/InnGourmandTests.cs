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

        // Drawn: the table, then per request its number on the picture, the item's name and the request in plain words.
        var (texts, pictures) = OnSta(() =>
        {
            var element = InnGourmandView.Tile(banner, _ => Picture(), InnGourmandShape.Full);
            element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            element.Arrange(new Rect(element.DesiredSize));
            var all = Descendants(element).ToList();
            return (all.OfType<TextBlock>().Select(t => t.Text).ToList(), all.OfType<Image>().Count());
        });
        Assert.Equal([Format("Inn.Gourmand.Title", 4), First, Dish(A).Zh, "soup, no seed oil", Second, Drink(Tea).Zh, Format("Inn.Gourmand.Drink", "no alcohol")], texts);
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
        OnSta(() => banners.Select(b => InnGourmandView.Tile(b, id => { asked.Add(id); return null; }, InnGourmandShape.Full)).ToList());
        Assert.Equal([C], asked);
    }

    /// <summary>
    /// Through the overlay's own display: the tile is up while the order is open, and stays while the dispatch panel is open,
    /// beside it (<see cref="InnViews.GourmandsBesidePanel"/>; owner, 2026-10-21, test_yuanyuan6 at 6:10: "gourmand's
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
        Assert.True(open.GourmandsBesidePanel);
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
    /// picture bigger, at least same size as the circule today"): in every shape each item's picture is the kitchen circle's
    /// (112 at 1080p, the old banner's 40) and no word is under the circles' 20; the tile, with the longest requests and dish
    /// names, is exactly its slot (<see cref="InnGourmandView.Width(InnGourmandShape, int)"/> × <see cref="InnGourmandView.Height"/>).
    /// </summary>
    [Theory]
    [InlineData("Full")]
    [InlineData("Compact")]
    [InlineData("Tall")]
    [InlineData("TallBare")]
    public void A_tiles_pictures_are_as_large_as_a_circles_and_its_words_as_large_as_the_circles(string shapeName)
    {
        var shape = Enum.Parse<InnGourmandShape>(shapeName);
        var said = Inn.Gourmands.SelectMany(g => g.Requests).Select(r => GourmandCondition.Parse(r.When, Inn))
            .Select(c => c.ForDrink ? Format("Inn.Gourmand.Drink", c.Describe()) : c.Describe()).OrderByDescending(d => d.Length).Take(2).ToList();
        var names = Inn.Dishes.OrderByDescending(d => d.Zh.Length).ThenBy(d => d.Id).Take(2).ToList();
        var banner = Assert.Single(InnGourmandView.Banners([.. Enumerable.Range(0, 2).Select(i => new InnWant(6, i, said[i], names[i].Id, names[i].Zh))], Text));

        var (pictures, fonts, size) = OnSta(() =>
        {
            var element = InnGourmandView.Tile(banner, PackagePicture, shape);
            element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            element.Arrange(new Rect(element.DesiredSize));
            var all = Descendants(element).ToList();
            return (all.OfType<Image>().Select(i => i.ActualHeight).ToList(), all.OfType<TextBlock>().Select(t => t.FontSize).ToList(), element.DesiredSize);
        });
        output.WriteLine($"{shape}: pictures {string.Join(",", pictures)}, fonts {string.Join(",", fonts)}, tile {size.Width:F0}x{size.Height:F0}");
        Assert.Equal(2, pictures.Count);
        Assert.All(pictures, p => Assert.True(p >= InnKitchenCircleView.PictureSize, $"a picture {p:F0} units"));
        Assert.All(fonts, f => Assert.True(f >= 20, $"a word {f} units tall"));
        Assert.Equal((InnGourmandView.Width(shape, 2), InnGourmandView.Height(shape, 2)), (size.Width, size.Height));
    }

    /// <summary>A place dragged off the window is kept on it.</summary>
    [Fact]
    public void A_place_off_the_window_is_kept_on_it()
    {
        Assert.Equal((0.0, 1080.0), InnGourmandLayout.Place(1920, 1080, 1.5, 2));
        Assert.Equal((1920.0, 0.0), InnGourmandLayout.Place(1920, 1080, -1, -1));
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
