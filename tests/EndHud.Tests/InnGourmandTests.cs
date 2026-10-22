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

        // Drawn: the title, then each row's words, the arrow, the item's picture and its name, in that order.
        var (texts, pictures) = OnSta(() =>
        {
            var element = InnGourmandView.Banner(banner, _ => Picture());
            element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            element.Arrange(new Rect(element.DesiredSize));
            var all = Descendants(element).ToList();
            return (all.OfType<TextBlock>().Select(t => t.Text).ToList(), all.OfType<Image>().Count());
        });
        Assert.Equal([Format("Inn.Gourmand.Title", 4), $"{First} soup, no seed oil", " → ", Dish(A).Zh, $"{Second} {Format("Inn.Gourmand.Drink", "no alcohol")}", " → ", Drink(Tea).Zh], texts);
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
        OnSta(() => banners.Select(b => InnGourmandView.Banner(b, id => { asked.Add(id); return null; })).ToList());
        Assert.Equal([C], asked);
    }

    /// <summary>
    /// Through the overlay's own display: the banner is up while the order is open, put away in place while the dispatch
    /// panel is open (it would cover the panel, as the cooking banners would; <see cref="InnViews.Away"/>, the same banner
    /// shown again after, no slide-in), back when it's closed, gone once fulfilled.
    /// </summary>
    [Fact]
    public void The_banner_stays_while_the_order_is_open_is_put_away_over_the_panel_and_goes_when_fulfilled()
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
        Assert.True(display.Views(session, S(11.3), callsEnabled: true, Text).Away);
        Assert.Equal(4, Assert.Single(Wants(11.3)).Table);

        display.Show(session, Session(11.6 + InnSession.PanelClosedAfter.TotalSeconds, tables: gourmand), S(11.6 + InnSession.PanelClosedAfter.TotalSeconds));
        Assert.Single(Wants(11.6 + InnSession.PanelClosedAfter.TotalSeconds));

        // Eating, read on two readings in a row.
        display.Show(session, Session(20, tables: Tables(new TableCard(4, TableState.GourmandEating, [], []))), S(20));
        display.Show(session, Session(20.3, tables: Tables(new TableCard(4, TableState.GourmandEating, [], []))), S(20.3));
        Assert.Empty(Wants(20.3));
    }

    /// <summary>
    /// The fullest stack: <see cref="InnGourmandView.MostBanners"/> gourmands of two requests each, the longest requests in
    /// the gourmands' table put in plain words and the longest dish names, at the player's default size, from the stack's
    /// default place: inside the window at 1080p, 1440p and 4K, on the right half, below the table cards (their bottom at
    /// y 0.13, session-four-orders.jpg) and the status at the top right.
    /// </summary>
    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(2560, 1440)]
    [InlineData(3840, 2160)]
    public void The_fullest_stack_fits_on_the_right_from_its_default_place(int width, int height)
    {
        var said = Inn.Gourmands.SelectMany(g => g.Requests).Select(r => GourmandCondition.Parse(r.When, Inn))
            .Select(c => c.ForDrink ? Format("Inn.Gourmand.Drink", c.Describe()) : c.Describe()).OrderByDescending(d => d.Length).Take(2 * InnGourmandView.MostBanners).ToList();
        var names = Inn.Dishes.OrderByDescending(d => d.Zh.Length).ThenBy(d => d.Id).Take(2 * InnGourmandView.MostBanners).ToList();
        var wants = Enumerable.Range(0, 2 * InnGourmandView.MostBanners).Select(i => new InnWant(i / 2 + 1, i % 2, said[i], names[i].Id, names[i].Zh)).ToList();
        var banners = InnGourmandView.Banners(wants, Text);
        Assert.Equal(InnGourmandView.MostBanners, banners.Count);

        double scale = ComboHintLayout.Scale(height, dpiScale: 1);
        var arrangement = InnGourmandLayout.Arrange(banners, width, height, scale, HudConfig.DefaultInnGourmandRight, HudConfig.DefaultInnGourmandTop);
        var (widest, label) = OnSta(() =>
        {
            double max = banners.Max(b =>
            {
                var element = InnGourmandView.Banner(b, PackagePicture, arrangement.Compact);
                element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                return element.DesiredSize.Width;
            });
            var drag = new TextBlock { Text = Text("Inn.Gourmand.Drag"), Margin = new Thickness(0, 0, 0, 4) };
            drag.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return (max, drag.DesiredSize.Height);
        });
        const double frame = 2 * (6 + 2); // the overlay's frame around the stack: padding and border (OverlayWindow.xaml)
        // Six gourmands: compact, placed around the Full Blaze meter (InnSixGourmandsTests checks each banner's place).
        Assert.True(arrangement.Compact);
        double stackWidth = (widest + frame) * arrangement.Scale;
        double stackHeight = (arrangement.Height + label + frame) * arrangement.Scale;
        var (fromRight, top) = InnGourmandLayout.Place(width, height, HudConfig.DefaultInnGourmandRight, HudConfig.DefaultInnGourmandTop);
        double left = width - fromRight - stackWidth;
        output.WriteLine($"{width}x{height}: stack {stackWidth:F0}x{stackHeight:F0} at left {left:F0}, top {top:F0}");

        Assert.True(left >= width / 2.0, $"left edge {left:F0}: not on the right half of {width}");
        Assert.True(top >= 0.13 * height, $"top {top:F0}: over the table cards");
        Assert.True(top + stackHeight <= height, $"bottom {top + stackHeight:F0} of {height}");
        Assert.True(width - fromRight <= width, "inside the window");
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
