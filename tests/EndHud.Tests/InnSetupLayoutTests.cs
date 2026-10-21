using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EndHud.Vision;
using EndHud.Vision.Inn;
using Xunit;
using static EndHud.Tests.InnTestKit;

namespace EndHud.Tests;

/// <summary>
/// The setup advice as drawn (<see cref="InnStrip"/> over <see cref="InnSetupView"/>), measured off
/// screen: each spot's fullest panel fits inside its spot (<see cref="InnAdviceLayout"/>: under a
/// kitchen's card, the chef picker's blank, the menu screen's left column) at 1080p, 1440p and 4K,
/// and every operator, dish and drink has its picture beside its name, or its name alone without one.
/// </summary>
[Collection(WpfCollection.Name)]
public class InnSetupLayoutTests(Xunit.Abstractions.ITestOutputHelper output)
{
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

    private static BitmapSource? Load(string path)
    {
        if (!File.Exists(path)) return null;
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(path);
        image.EndInit();
        image.Freeze();
        return image;
    }

    /// <summary>As the overlay finds them: an operator's avatar from the package's icons, a dish's or drink's picture.</summary>
    private static BitmapSource? PackagePicture(string id) => id.StartsWith(InnSetupView.OperatorPicture, StringComparison.Ordinal)
        ? Load(Path.Combine(Folder, "icons", id[InnSetupView.OperatorPicture.Length..] + ".png"))
        : Load(InnData.PicturePath(Folder, id));

    /// <summary>
    /// The fullest advice: three kitchens, each with two chefs to put in, three substitutes and two
    /// dish types; a menu of eight dishes and eight drinks, all on the menu already (each marked),
    /// three substitutes each, picked with the chefs (the longer title), a rule not understood and
    /// the note that the rules can't all be met. The longest names everywhere.
    /// </summary>
    private static (InnSetupInputs Inputs, InnSetupAdvice Advice, IReadOnlyCollection<string> OnMenu) Fullest()
    {
        int per = InnSuggester.MaxSubstitutes;
        var cooks = Library.Operators.Where(o => Inn.ChefBonusesOf(o).Count > 0).OrderByDescending(o => o.NameZh.Length).ThenBy(o => o.Id).ToList();
        var dishes = Inn.Dishes.OrderByDescending(d => d.Zh.Length).ThenBy(d => d.Id).Select(d => new MenuDish(d, null)).ToList();
        var drinks = Inn.Drinks.OrderByDescending(d => d.Zh.Length).ThenBy(d => d.Id).Select(d => new MenuDrink(d, null)).ToList();
        var types = Inn.DishTypes.OrderByDescending(t => t.Zh.Length).Take(2).Select(t => t.Id).ToList();
        var menu = dishes.Take(MenuRules.MostSeen).ToList();
        var offMenu = dishes.Skip(MenuRules.MostSeen).ToList();
        var kitchens = Enumerable.Range(0, 3).Select(k => new KitchenSuggestion(k + 1,
            cooks.Skip(k * (2 + per)).Take(2).ToList(), cooks.Skip(k * (2 + per) + 2).Take(per).ToList(),
            menu.Skip(k * 3).Take(3).ToList(), offMenu.Skip(k * per).Take(per).ToList(), types)).ToList();
        var suggestions = new InnSuggestions(kitchens, [], drinks.Take(MenuRules.MostSeen).ToList(), drinks.Skip(MenuRules.MostSeen).Take(per).ToList());
        var plan = new InnPlan([.. kitchens.Select(k => new PlannedKitchen(k.Number, k.Chefs))], menu, drinks.Take(MenuRules.MostSeen).ToList(), null,
            InnPlanScore.Zero, RulesMet: false, []);
        var inputs = new InnSetupInputs([], new InnPlanInput([], 4, 0, 6, [new PlanKitchen(1, 2), new PlanKitchen(2, 2), new PlanKitchen(3, 2)], [], [], [],
            new MenuRules(6, 8, 3, 8, 3, [])) { Preferred = [] }, "k", ["至少有2道带有[某种没见过的]特性的菜品"]);
        return (inputs, new InnSetupAdvice(plan, suggestions), [.. menu.Select(d => d.Dish.Id), .. drinks.Select(d => d.Drink.Id)]);
    }

    private static IReadOnlyList<InnAdvicePanel> Panels()
    {
        var (inputs, advice, onMenu) = Fullest();
        return InnSetupView.Setup(inputs, advice, Inn, Text);
    }

    private static Size Measure(InnStripModel model, double lineWidth, InnStripLook look) => OnSta(() =>
    {
        var element = InnStrip.Build(model, PackagePicture, lineWidth, look);
        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return element.DesiredSize;
    });

    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(2560, 1440)]
    [InlineData(3840, 2160)]
    public void Each_spots_fullest_panel_fits_inside_its_spot(int width, int height)
    {
        double scale = ComboHintLayout.Scale(height, dpiScale: 1);
        var panels = Panels();
        Assert.Equal([InnAdviceSpot.Kitchen1, InnAdviceSpot.Kitchen2, InnAdviceSpot.Kitchen3], panels.Select(p => p.Spot));

        foreach (var panel in panels)
        {
            var spot = InnAdviceLayout.Place(panel.Spot, width, height);
            var size = Measure(panel.Model, InnAdviceLayout.LineWidth(panel.Spot, width, height, scale), InnStripLook.Normal);
            output.WriteLine($"{panel.Spot} at {width}x{height}: {size.Width * scale:F0}x{size.Height * scale:F0} in {spot.Width:F0}x{spot.Height:F0}");
            Assert.True(size.Width * scale <= spot.Width + 0.5, $"{panel.Spot}: {size.Width * scale:F0} wide in a spot {spot.Width:F0} wide");
            Assert.True(size.Height * scale <= spot.Height + 0.5, $"{panel.Spot}: {size.Height * scale:F0} tall in a spot {spot.Height:F0} tall");
        }
    }

    /// <summary>The spots stay on the game's 16:9 area, and the kitchens' each under its own card (setup-chefs-placed.jpg).</summary>
    [Fact]
    public void The_spots_sit_in_the_games_blank_areas()
    {
        double[][] cards = [[0.418, 0.588], [0.594, 0.764], [0.771, 0.941]];
        for (int k = 0; k < 3; k++)
        {
            var spot = InnAdviceLayout.Of((InnAdviceSpot)(k + 1));
            Assert.InRange(spot.Left, cards[k][0], cards[k][1]);
            Assert.InRange(spot.Left + spot.Width, cards[k][0], cards[k][1]);
            Assert.InRange(spot.Top, 0.655, 0.7);
            Assert.InRange(spot.Top + spot.Height, 0.8, 0.865);
        }

        // A window wider than 16:9: the game's area is centred, and the spots with it.
        var wide = InnAdviceLayout.Place(InnAdviceSpot.Kitchen1, 2560, 1080);
        Assert.Equal((2560 - 1920) / 2 + 0.424 * 1920, wide.Left, 3);
    }

    [Fact]
    public void Every_chef_dish_and_drink_has_its_picture_beside_its_name_or_its_name_alone_without_one()
    {
        var models = Panels().Select(p => p.Model).ToList();
        var named = models.SelectMany(m => m.Lines).SelectMany(l => l.Segments).Where(s => s.PictureId is not null).ToList();
        string missing = named.First(s => s.PictureId!.StartsWith(InnSetupView.OperatorPicture, StringComparison.Ordinal)).PictureId!;

        foreach (var model in models)
        {
            var mine = model.Lines.SelectMany(l => l.Segments).Where(s => s.PictureId is not null).ToList();
            var (images, texts) = OnSta(() =>
            {
                var element = InnStrip.Build(model, id => id == missing ? null : PackagePicture(id), lineWidth: 520);
                element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                element.Arrange(new Rect(element.DesiredSize));
                var all = Descendants(element).ToList();
                return (all.OfType<Image>().Count(i => i.Source is not null), all.OfType<TextBlock>().Select(t => t.Text).ToList());
            });
            Assert.Equal(mine.Count(s => s.PictureId != missing), images);
            Assert.All(mine, s => Assert.Contains(s.Text, texts));
        }
        Assert.True(named.All(s => PackagePicture(s.PictureId!) is not null), "the package has a picture for every chef, dish and drink shown");
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
