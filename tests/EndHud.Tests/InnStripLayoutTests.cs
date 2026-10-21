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
/// The inn's banners as drawn (<see cref="ComboBanner"/>), measured off screen: the fullest stack of banners fits from
/// its default place to the window's edge at 1080p, 1440p and 4K, and a banner's dish picture is large.
/// </summary>
[Collection(WpfCollection.Name)]
public class InnStripLayoutTests
{
    /// <summary>The overlay's frame around the banners: its padding and border (OverlayWindow.xaml).</summary>
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
                var element = InnCallView.Banner(b, PackagePicture);
                element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                return element.DesiredSize.Width;
            });
            var drag = new TextBlock { Text = Text("Inn.Call.Drag"), Margin = new Thickness(0, 0, 0, 4) };
            drag.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return (widest, drag.DesiredSize.Height);
        });
        double scale = ComboHintLayout.Scale(height, dpiScale: 1);
        double stack = banners.Count * (InnCallView.BannerHeight + ComboBanner.Gap) + label;

        double right = HudConfig.DefaultComboHintX * width + (bannerWidth + Frame) * scale;
        double bottom = HudConfig.DefaultComboHintY * height + (stack + Frame) * scale;

        Assert.True(right <= width, $"right edge {right:F0} of {width} ({bannerWidth:F0} units wide at scale {scale:F2})");
        Assert.True(bottom <= height, $"bottom edge {bottom:F0} of {height} ({stack:F0} units tall at scale {scale:F2})");
    }

    /// <summary>
    /// Owner, live 2026-10-20: "the picture in the banner is too small, make it very hard for user to notice". The dish's
    /// picture fills most of the banner's height: at least <see cref="InnCallView.PictureSize"/> units, a 1080p window's
    /// pixels (76 px; it was the combo icon's 44).
    /// </summary>
    [Fact]
    public void A_banners_dish_picture_is_large_enough_to_read_at_a_glance()
    {
        var banner = InnCallView.Of(new InnCall(InnCallKind.Queue, 2, Inn.Dishes[0], 3), Text);

        var (picture, height) = OnSta(() =>
        {
            var element = InnCallView.Banner(banner, PackagePicture);
            element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            element.Arrange(new Rect(element.DesiredSize));
            var image = Descendants(element).OfType<Image>().Single();
            return (image.ActualHeight, element.ActualHeight);
        });

        Assert.True(picture >= 72, $"the dish's picture is {picture:F0} units tall");
        Assert.True(picture >= 0.85 * height, $"the picture is {picture:F0} of the banner's {height:F0} units");
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
