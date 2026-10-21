using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EndHud.Vision.Inn;
using Xunit;
using static EndHud.Tests.InnTestKit;

namespace EndHud.Tests;

/// <summary>
/// What each gourmand wants as glowing circles in the gauges' buff style (<see cref="InnWantsView"/>; owner, live
/// 2026-10-20: "for 老饕, reuse the buff to show the dish/drink they want to be a lot more visible"), and where they go
/// (<see cref="InnWantsLayout"/>: the gauges' place, bottom middle).
/// </summary>
[Collection(WpfCollection.Name)]
public class InnWantsTests
{
    private const string A = "melon-meatball-soup", B = "fin-mato-soup", C = "mala-xiang-guo", D = "sweetbeans-on-snow", E = "meat-baozi";
    private const string Tea = "pre-tide-lungjing", Milk = "bottled-milk-a";

    private static InnAdvice Advice(params InnGourmandNote[] gourmands) =>
        new(new DispatchAdvice([], [], [], []), null, null, false, gourmands, [], new HashSet<int>(), false);

    private static GourmandOption Option(string id) => Inn.Dishes.Any(d => d.Id == id)
        ? new GourmandOption(Dish(id), null, GourmandSource.Counter, null, 0)
        : new GourmandOption(null, Drink(id), GourmandSource.DrinkBar, null, 0);

    private static GourmandRequestAdvice Request(params string[] options) => new(new GourmandRequest("said", "x"), "plain", [.. options.Select(Option)]);

    [Fact]
    public void One_glowing_circle_per_request_with_the_best_item_and_the_table()
    {
        var note = new InnGourmandNote(4, new GourmandAdvice([Request(B, A), Request(Tea, Milk)], null), null, false);

        var wants = InnWantsView.Of(Advice(note), Text);

        Assert.Equal([(4, B), (4, Tea)], wants.Select(w => (w.Table, w.ItemId)));
        var board = InnWantsView.Board(wants, _ => null, Text);
        Assert.All(board.Gauges, g =>
        {
            Assert.Equal(GaugeKind.Buff, g.Kind);
            Assert.True(g.Ready); // glowing
            Assert.Equal("桌4", g.Label);
        });
        Assert.Equal([Dish(B).Zh, Drink(Tea).Zh], board.Gauges.Select(g => g.Detail));
    }

    [Fact]
    public void For_two_requests_the_circles_are_in_the_order_to_serve_them()
    {
        var serve = new[] { Option(Milk), Option(A) };
        var note = new InnGourmandNote(2, new GourmandAdvice([Request(A, B), Request(Tea, Milk)], serve), null, false);

        Assert.Equal([Milk, A], InnWantsView.Of(Advice(note), Text).Select(w => w.ItemId));
    }

    /// <summary>
    /// What the gourmand panel said that matters, now in the circles (owner, live 2026-10-20: "no longer need the older
    /// panel for gourmand"): a request nothing on the menu meets is a circle saying so; a gourmand not recognised, one
    /// saying that; a drink request before any drink of the menu was seen, none (it would be wrong).
    /// </summary>
    [Fact]
    public void A_request_met_by_nothing_or_not_recognised_is_a_circle_saying_so()
    {
        var unknown = new InnGourmandNote(1, null, "read text", false);
        var unmet = new InnGourmandNote(3, new GourmandAdvice([Request(), Request(C)], null), null, false);
        var drinksUnseen = new InnGourmandNote(5, new GourmandAdvice([Request()], null), null, DrinksUnknown: true);

        var wants = InnWantsView.Of(Advice(unknown, unmet, drinksUnseen), Text);

        Assert.Equal([(1, null, "没认出要求"), (3, null, "没有合适的"), (3, C, Dish(C).Zh)], wants.Select(w => (w.Table, w.ItemId, w.Name)));
        var asked = new List<string>();
        var board = InnWantsView.Board(wants, id => { asked.Add(id); return null; }, Text);
        Assert.Equal([C], asked); // no picture is asked for a circle without an item
        Assert.Equal(["桌1", "桌3", "桌3"], board.Gauges.Select(g => g.Label));
        Assert.Null(board.Gauges[0].Avatar);
    }

    /// <summary>The circles are the session's gourmands: kept while the order is open, through the open dispatch panel, gone once fulfilled.</summary>
    [Fact]
    public void The_circles_stay_while_the_order_is_open_also_over_the_dispatch_panel_and_go_when_it_is_fulfilled()
    {
        var session = NewSession();
        session.Update(SetupScreen(0, 1));
        session.Update(MenuScreen(1, A, B, C, D, E, Tea, Milk));
        var (request, _) = Inn.Gourmands.SelectMany(g => g.Requests.Select(r => (r, GourmandCondition.Parse(r.When, Inn))))
            .First(x => !x.Item2.ForDrink && new[] { A, B, C, D, E }.Any(id => x.Item2.MetBy(Dish(id))));
        var gourmand = Tables(new TableCard(4, TableState.GourmandOrder, [], []));
        session.Update(Session(10, tables: gourmand, bubble: new GourmandBubble(request.Text, [request.Text])));
        Assert.Equal(4, Assert.Single(InnWantsView.Of(session.Advice(S(10))!, Text)).Table);

        session.Update(Session(11, tables: Tables(), panel: Panel(1, 1, [A, B, C])));
        Assert.Single(InnWantsView.Of(session.Advice(S(11))!, Text));

        // Eating, read on two readings in a row.
        session.Update(Session(12, tables: Tables(new TableCard(4, TableState.GourmandEating, [], []))));
        session.Update(Session(12.3, tables: Tables(new TableCard(4, TableState.GourmandEating, [], []))));
        Assert.Empty(InnWantsView.Of(session.Advice(S(12.3))!, Text));
    }

    /// <summary>
    /// Owner, live 2026-10-20: "the circle should be at where the buff is (middle bottom)". The most circles, with the
    /// longest names, centred where the gauges are (the player's place, bottom middle by default) and standing on their
    /// bottom line, inside a 16:9 window at 1080p, 1440p and 4K; a place dragged to the window's edge keeps them inside.
    /// </summary>
    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(2560, 1440)]
    [InlineData(3840, 2160)]
    public void The_most_circles_stand_at_the_gauges_place_bottom_middle_inside_the_window(int width, int height)
    {
        var longest = Inn.Dishes.OrderByDescending(d => d.Zh.Length).Take(InnWantsView.MostCircles).ToList();
        var wants = longest.Select((d, i) => new InnWant(i / 2 + 1, d.Id, d.Zh)).ToList();
        double scale = ComboHintLayout.Scale(height, dpiScale: 1);

        var size = OnSta(() =>
        {
            var element = (FrameworkElement)Gauges.Build(InnWantsView.Board(wants, _ => null, Text));
            element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return new Size(element.DesiredSize.Width * scale, element.DesiredSize.Height * scale);
        });
        var at = InnWantsLayout.TopLeft(width, height, size, HudConfig.DefaultGaugesCenterX, HudConfig.DefaultGaugesBottomY);

        Assert.Equal(width / 2.0, at.X + size.Width / 2, 1);
        Assert.Equal(HudConfig.DefaultGaugesBottomY * height, at.Y + size.Height, 1);
        Assert.True(at.X >= 0 && at.X + size.Width <= width, $"{at.X:F0}..{at.X + size.Width:F0} of {width}");
        Assert.True(at.Y >= 0, "inside the window");
        Assert.True(Gauges.Size * scale >= 90, "each circle is large");

        var atEdge = InnWantsLayout.TopLeft(width, height, size, 0.98, 1);
        Assert.True(atEdge.X + size.Width <= width + 0.5 && atEdge.Y + size.Height <= height + 0.5, "kept inside from a place at the edge");
    }

    [Fact]
    public void A_circle_has_the_items_picture_in_the_middle_and_the_glowing_ring()
    {
        var wants = new[] { new InnWant(4, A, Dish(A).Zh) };

        var (images, glows) = OnSta(() =>
        {
            var picture = System.Windows.Media.Imaging.BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[16], 8);
            var element = (FrameworkElement)Gauges.Build(InnWantsView.Board(wants, _ => picture, Text));
            element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            element.Arrange(new Rect(element.DesiredSize));
            var all = Descendants(element).ToList();
            return (all.OfType<Image>().Select(i => i.ActualWidth).ToList(),
                all.OfType<System.Windows.Shapes.Shape>().Count(s => s.Effect is System.Windows.Media.Effects.DropShadowEffect { BlurRadius: > 10 }));
        });

        double inside = Gauges.Size - 2 * (7 + 4);
        Assert.Equal(inside * InnWantsView.PictureShare, Assert.Single(images), 1);
        Assert.True(glows >= 1, "the ring glows");
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
