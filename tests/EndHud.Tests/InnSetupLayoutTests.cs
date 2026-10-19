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
/// The setup panel as drawn (<see cref="InnStrip"/> over <see cref="InnSetupView"/>), measured off
/// screen: the fullest panel (a block per kitchen) fits from its default bottom-left anchor to the
/// window's edge at 1080p, 1440p and 4K, and every operator, dish and drink has its picture beside
/// its name, or its name alone without one.
/// </summary>
[Collection(WpfCollection.Name)]
public class InnSetupLayoutTests
{
    /// <summary>The overlay's frame around the panel: its padding and border (OverlayWindow.xaml).</summary>
    private const double Frame = 2 * (6 + 2);

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
    /// The fullest panel: three kitchens, each with two chefs and three substitutes and two dish types
    /// in its heading; the menu's eight dishes shared out as <paramref name="split"/> says, each
    /// kitchen with three dish substitutes; eight drinks and three substitutes; the longest names
    /// everywhere; a rule not understood and the note that the rules can't all be met.
    /// </summary>
    private static InnStripModel Fullest(params int[] split)
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
            menu.Skip(split.Take(k).Sum()).Take(split[k]).ToList(), offMenu.Skip(k * per).Take(per).ToList(), types)).ToList();
        var suggestions = new InnSuggestions(kitchens, [], drinks.Take(MenuRules.MostSeen).ToList(), drinks.Skip(MenuRules.MostSeen).Take(per).ToList());
        var plan = new InnPlan([.. kitchens.Select(k => new PlannedKitchen(k.Number, k.Chefs))], menu, drinks.Take(MenuRules.MostSeen).ToList(), null,
            InnPlanScore.Zero, RulesMet: false, []);
        var inputs = new InnSetupInputs([], new InnPlanInput([], 4, 0, 6, [], [], [], [], new MenuRules(6, 8, 3, 8, 3, [])), "k",
            ["至少有2道带有[某种没见过的]特性的菜品"]);
        return InnSetupView.Build(inputs, new InnSetupAdvice(plan, suggestions), Inn, Text);
    }

    private static Size Measure(InnStripModel model) => OnSta(() =>
    {
        var element = InnStrip.Build(model, PackagePicture);
        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return element.DesiredSize;
    });

    [Theory]
    [InlineData(1920, 1080, 3)]
    [InlineData(2560, 1440, 3)]
    [InlineData(3840, 2160, 3)]
    [InlineData(1920, 1080, 8)]
    [InlineData(2560, 1440, 8)]
    [InlineData(3840, 2160, 8)]
    public void The_fullest_panel_fits_from_its_default_place_to_the_windows_edge(int width, int height, int dishesInTheFirstKitchen)
    {
        // The menu shared 3/3/2, or all eight dishes in the first kitchen (its 做菜 row wraps most).
        int[] split = dishesInTheFirstKitchen == 8 ? [8, 0, 0] : [3, 3, 2];
        var size = Measure(Fullest(split));
        double scale = ComboHintLayout.Scale(height, dpiScale: 1);

        double right = HudConfig.DefaultInnSetupX * width + (size.Width + Frame) * scale;
        // Anchored by its bottom: the fullest panel grows up, and must not run off the top.
        double top = HudConfig.DefaultInnSetupBottomY * height - (size.Height + Frame) * scale;

        Assert.True(right <= width, $"right edge {right:F0} of {width} ({size.Width:F0} units wide at scale {scale:F2})");
        Assert.True(top >= 0, $"top edge {top:F0} ({size.Height:F0} units tall at scale {scale:F2})");
    }

    [Fact]
    public void The_sample_fits_too()
    {
        var size = Measure(InnSetupView.Sample(Inn, Library.Operators, Text));
        double top = HudConfig.DefaultInnSetupBottomY * 1080 - (size.Height + Frame) * ComboHintLayout.Scale(1080, dpiScale: 1);
        Assert.True(top >= 0, $"top edge {top:F0}");
    }

    [Fact]
    public void Every_chef_dish_and_drink_has_its_picture_beside_its_name_or_its_name_alone_without_one()
    {
        var model = Fullest(3, 3, 2);
        var named = model.Lines.SelectMany(l => l.Segments).Where(s => s.PictureId is not null).ToList();
        string missing = named.First(s => s.PictureId!.StartsWith(InnSetupView.OperatorPicture, StringComparison.Ordinal)).PictureId!;

        var (images, texts) = OnSta(() =>
        {
            var element = InnStrip.Build(model, id => id == missing ? null : PackagePicture(id));
            element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            element.Arrange(new Rect(element.DesiredSize));
            var all = Descendants(element).ToList();
            return (all.OfType<Image>().Count(i => i.Source is not null), all.OfType<TextBlock>().Select(t => t.Text).ToList());
        });

        Assert.Equal(named.Count(s => s.PictureId != missing), images);
        Assert.All(named, s => Assert.Contains(s.Text, texts));
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
