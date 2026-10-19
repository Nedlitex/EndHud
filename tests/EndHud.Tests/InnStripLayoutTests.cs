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
/// The inn strip as drawn (<see cref="InnStrip"/>), measured off screen: the busiest strip a session
/// can show fits from its default place to the window's edge at 1080p, 1440p and 4K, and every dish
/// and drink has its picture beside its name, or its name alone when the package has no picture.
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
    /// The most a session's strip shows: the Full Blaze line, three full kitchens of the longest
    /// names each cooked ahead (the widest line there is), a swap, what can't be done, two
    /// unreadable tables, and a gourmand with two requests and three options each.
    /// </summary>
    private static InnStripModel Busiest()
    {
        var longest = Inn.Dishes.OrderByDescending(d => d.Zh.Length).ThenBy(d => d.Id).Take(9).ToList();
        var drinks = Inn.Drinks.OrderByDescending(d => d.Zh.Length).Take(3).ToList();
        var kitchens = Enumerable.Range(1, 3).Select(k =>
            new KitchenAdvice(k, [.. longest.Skip((k - 1) * 3).Take(3).Select(d => new QueueAdvice(d, null, 40))], [])).ToList();
        kitchens[1] = kitchens[1] with { Replaces = [new ReplaceAdvice(2, 1, longest[3], longest[4], 6, 30, 70)] };
        var request = new GourmandRequest("said", "x");
        var dishOptions = longest.Take(3).Select((d, i) => new GourmandOption(d, null, GourmandSource.ToCook, 2, 35 + i)).ToList();
        var drinkOptions = drinks.Select(d => new GourmandOption(null, d, GourmandSource.DrinkBar, null, 0)).ToList();
        var gourmand = new InnGourmandNote(6, new GourmandAdvice(
            [new GourmandRequestAdvice(request, GourmandCondition.Parse(Inn.Gourmands[0].Requests[0].When, Inn).Describe(), dishOptions),
             new GourmandRequestAdvice(request, GourmandCondition.Parse(Inn.Gourmands[0].Requests[0].When, Inn).Describe(), drinkOptions)],
            [dishOptions[0], drinkOptions[0]]), null, false);
        var advice = new InnAdvice(new DispatchAdvice(kitchens, [new OrderedDish(5, longest[5])], [new OrderedDish(4, longest[6])]), null,
            FullBlazeAdvice.StartNow, false, [gourmand], [2, 3], new HashSet<int>(), false);
        return InnStripView.Build(advice, Text);
    }

    private static Size Measure(InnStripModel model, Func<string, BitmapSource?> picture) => OnSta(() =>
    {
        var element = InnStrip.Build(model, picture);
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

    [Fact]
    public void The_sentence_saying_what_to_install_wraps_within_the_strip()
    {
        var model = InnStripView.Build(new InnAdvice(null, InnGap.NoKitchens, null, false, [], [], new HashSet<int>(), true), Text, "Inn.Problem.NoTextRecognition");

        var size = Measure(model, PackagePicture);

        Assert.True(size.Width <= InnStrip.LineWidth + 40, $"{size.Width:F0} units wide");
        var oneLine = Measure(InnStripView.Build(new InnAdvice(null, InnGap.NoKitchens, null, false, [], [], new HashSet<int>(), true), Text), PackagePicture);
        Assert.True(size.Height > oneLine.Height, $"{size.Height:F0} units tall against {oneLine.Height:F0} for one line: not wrapped");
    }

    [Fact]
    public void A_long_kitchen_line_wraps_between_dishes_not_off_the_strip()
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
            var element = InnStrip.Build(model, id => id == missing ? null : PackagePicture(id));
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
