using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EndHud.Vision.Inn;
using Xunit;
using static EndHud.Tests.InnTestKit;

namespace EndHud.Tests;

/// <summary>
/// The inn panel and the inn's banners as drawn (<see cref="InnStrip"/>, <see cref="ComboBanner"/>), measured off
/// screen: the busiest gourmand panel a session can show, and the fullest stack of banners, each fit from its
/// default place to the window's edge at 1080p, 1440p and 4K; every dish and drink has its picture beside its name,
/// or its name alone when the package has no picture.
/// </summary>
[Collection(WpfCollection.Name)]
public class InnStripLayoutTests
{
    /// <summary>The overlay's frame around the strip: its padding and border (OverlayWindow.xaml).</summary>
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

    /// <summary>
    /// The most the panel shows: two gourmands at once, each with two requests and three options per request, the
    /// longest names (the kitchens' calls are banners, never in the panel).
    /// </summary>
    private static InnStripModel Busiest()
    {
        var longest = Inn.Dishes.OrderByDescending(d => d.Zh.Length).ThenBy(d => d.Id).Take(9).ToList();
        var drinks = Inn.Drinks.OrderByDescending(d => d.Zh.Length).Take(3).ToList();
        var request = new GourmandRequest("said", "x");
        string said = GourmandCondition.Parse(Inn.Gourmands[0].Requests[0].When, Inn).Describe();
        InnGourmandNote Gourmand(int table)
        {
            var dishOptions = longest.Take(3).Select((d, i) => new GourmandOption(d, null, GourmandSource.ToCook, 2, 35 + i)).ToList();
            var drinkOptions = drinks.Select(d => new GourmandOption(null, d, GourmandSource.DrinkBar, null, 0)).ToList();
            return new InnGourmandNote(table, new GourmandAdvice(
                [new GourmandRequestAdvice(request, said, dishOptions), new GourmandRequestAdvice(request, said, drinkOptions)],
                [dishOptions[0], drinkOptions[0]]), null, false);
        }
        var kitchens = Enumerable.Range(1, 3).Select(k => new KitchenAdvice(k, [new QueueAdvice(longest[k], k, 40)], [])).ToList();
        var advice = new InnAdvice(new DispatchAdvice(kitchens, [], [], []), null, FullBlazeAdvice.StartNow, false, [Gourmand(5), Gourmand(6)], [2],
            new HashSet<int>(), false);
        return InnStripView.Build(advice, Text);
    }

    private static Size Measure(InnStripModel model, Func<string, BitmapSource?> picture) => OnSta(() =>
    {
        var element = InnStrip.Build(model, picture, InnStrip.LineWidth);
        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return element.DesiredSize;
    });

    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(2560, 1440)]
    [InlineData(3840, 2160)]
    public void The_busiest_strip_fits_from_its_default_place_to_the_windows_edge(int width, int height)
    {
        var size = Measure(Busiest(), PackagePicture);
        double scale = ComboHintLayout.Scale(height, dpiScale: 1);

        double right = HudConfig.DefaultInnStripX * width + (size.Width + Frame) * scale;
        double bottom = HudConfig.DefaultInnStripY * height + (size.Height + Frame) * scale;

        Assert.True(right <= width, $"right edge {right:F0} of {width} ({size.Width:F0} units wide at scale {scale:F2})");
        Assert.True(bottom <= height, $"bottom edge {bottom:F0} of {height} ({size.Height:F0} units tall at scale {scale:F2})");
    }

    /// <summary>
    /// The fullest stack of banners (<see cref="InnCallView.MostBanners"/>): Full Blaze, three kitchens swapping the
    /// dishes with the longest names (the widest banner there is), and last calls with the longest names, at the
    /// player's default size, from the banners' default place (where the combo-ready hint is).
    /// </summary>
    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(2560, 1440)]
    [InlineData(3840, 2160)]
    public void The_fullest_banner_stack_fits_from_its_default_place_to_the_windows_edge(int width, int height)
    {
        var longest = Inn.Dishes.OrderByDescending(d => d.Zh.Length).ThenBy(d => d.Id).Take(9).ToList();
        var kitchens = Enumerable.Range(1, 3).Select(k => new KitchenAdvice(k, [], [new ReplaceAdvice(k, 1, longest[k], longest[k + 3], k, 30, 70)])).ToList();
        var leftovers = Enumerable.Range(0, 6).Select(i => new LeftoverAdvice(longest[i], i + 1, longest[8])).ToList();
        var calls = new InnCallHold().Update(new DispatchAdvice(kitchens, [], [], leftovers), FullBlazeAdvice.GetReady,
            new InnKitchenView((_, _) => 0, _ => true, (_, _) => 1000));
        var banners = InnCallView.Of(calls, Text);
        Assert.Equal(InnCallView.MostBanners, banners.Count);

        var (bannerWidth, label) = OnSta(() =>
        {
            double widest = banners.Max(b =>
            {
                var element = ComboBanner.Create(ComboBanner.Content(null, b.PictureId is { } id ? PackagePicture(id) : null, b.Title, b.Caption));
                element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                return element.DesiredSize.Width;
            });
            var drag = new TextBlock { Text = Text("Inn.Call.Drag"), Margin = new Thickness(0, 0, 0, 4) };
            drag.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return (widest, drag.DesiredSize.Height);
        });
        double scale = ComboHintLayout.Scale(height, dpiScale: 1);
        double stack = banners.Count * (ComboBanner.Height + ComboBanner.Gap) + label;

        double right = HudConfig.DefaultComboHintX * width + (bannerWidth + Frame) * scale;
        double bottom = HudConfig.DefaultComboHintY * height + (stack + Frame) * scale;

        Assert.True(right <= width, $"right edge {right:F0} of {width} ({bannerWidth:F0} units wide at scale {scale:F2})");
        Assert.True(bottom <= height, $"bottom edge {bottom:F0} of {height} ({stack:F0} units tall at scale {scale:F2})");
    }

    [Fact]
    public void A_long_gourmand_line_wraps_between_dishes_not_off_the_panel()
    {
        var size = Measure(Busiest(), PackagePicture);

        Assert.True(size.Width <= InnStrip.LineWidth + 40, $"{size.Width:F0} units wide");
    }

    [Fact]
    public void Every_dish_and_drink_has_its_picture_beside_its_name_or_its_name_alone_without_one()
    {
        var model = Busiest();
        string missing = model.Lines.SelectMany(l => l.Segments).First(s => s.PictureId is not null).PictureId!;
        var named = model.Lines.SelectMany(l => l.Segments).Where(s => s.PictureId is not null).ToList();

        var (images, texts) = OnSta(() =>
        {
            var element = InnStrip.Build(model, id => id == missing ? null : PackagePicture(id), InnStrip.LineWidth);
            element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            element.Arrange(new Rect(element.DesiredSize));
            var all = Descendants(element).ToList();
            return (all.OfType<Image>().Count(i => i.Source is not null), all.OfType<TextBlock>().Select(t => t.Text).ToList());
        });

        Assert.Equal(named.Count(s => s.PictureId != missing), images);
        Assert.All(named, s => Assert.Contains(s.Text, texts)); // the name is there, picture or not
        Assert.True(named.All(s => PackagePicture(s.PictureId!) is not null), "the package has a picture for every dish and drink shown");
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
