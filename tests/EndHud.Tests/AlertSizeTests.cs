using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;
using Xunit.Abstractions;

namespace EndHud.Tests;

/// <summary>
/// Owner, 2026-10-25: "the default notification should be larger, right now it is hard to notice, and notification
/// should be able to adjust size". The alert pop-ups have a size (the alerts card's size slider, 50–200%), 150% by
/// default; at every size, on 16:9, 16:10 and 21:9 game windows from 1080p to 4K, the stack of pop-ups stays on the
/// window, clear of the game's centre (where the character stands) and off the combo hint it mirrors.
/// </summary>
[Collection(WpfCollection.Name)]
public class AlertSizeTests(ITestOutputHelper output)
{
    /// <summary>The longest text a rule can show: wraps to two lines and ends in "…".</summary>
    private const string LongText = "寒冷附着至少三层并且技力至少一点八的时候立刻释放终结技然后切换到下一位干员继续输出";

    /// <summary>The slider's steps: 50% to 200% by 5%.</summary>
    private static IEnumerable<double> Sizes() => Enumerable.Range(0, 31).Select(i => Math.Round(HudConfig.MinAlertScale + i * 0.05, 2));

    /// <summary>Game windows, in the overlay's units, and the display scaling: 16:9, 16:10 and 21:9 from 1080p to 4K.</summary>
    public static TheoryData<double, double, double> Screens() => new()
    {
        { 1920, 1080, 1 }, { 1920, 1200, 1 }, { 2560, 1080, 1 }, { 2560, 1440, 1 }, { 2560, 1600, 1 }, { 3440, 1440, 1 },
        { 3840, 2160, 1 }, { 3840, 2400, 1 }, { 5120, 2160, 1 }, { 2560, 1440, 1.5 }, { 1920, 1080, 2 },
    };

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

    /// <summary>
    /// The fullest stack, as the overlay draws it (AlertFrame): the menu's sample and the most pop-ups shown at once, each
    /// with the longest text, at <paramref name="scale"/>. Its size on screen.
    /// </summary>
    private static Size AlertStack(double scale, double textMaxWidth)
    {
        var stack = new StackPanel();
        for (int i = 0; i <= AlertFollower.MostShown; i++)
        {
            var popUp = AlertPopUp.Create(AlertPopUp.Icon(null, skillPoints: false), LongText, textMaxWidth);
            var holder = AlertPopUp.Holder(popUp);
            AlertPopUp.ShowStill(holder);
            stack.Children.Add(holder);
        }
        var frame = new Border { BorderThickness = new Thickness(2), Padding = new Thickness(6), Child = stack, LayoutTransform = new ScaleTransform(scale, scale) };
        frame.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return frame.DesiredSize;
    }

    /// <summary>The combo hint's frame with a banner as wide as an operator's (avatar, combo icon, a long name), on screen.</summary>
    private static Size ComboHint(double scale)
    {
        var picture = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[16], 8);
        var banner = ComboBanner.Create(ComboBanner.Content(picture, picture, "按住拖动调整提示位置", "连携就绪"));
        var holder = ComboBanner.Holder(banner);
        holder.Height = ComboBanner.Height + ComboBanner.Gap;
        var frame = new Border
        {
            BorderThickness = new Thickness(2), Padding = new Thickness(6), LayoutTransform = new ScaleTransform(scale, scale),
            Child = new StackPanel { Children = { new TextBlock { Text = "按住拖动，调整提示位置", Margin = new Thickness(0, 0, 0, 4) }, holder } },
        };
        frame.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return frame.DesiredSize;
    }

    /// <summary>Where the overlay puts the stack and the combo hint for the combo hint's place (<paramref name="x"/>, <paramref name="y"/>).</summary>
    private static (Rect Alerts, Rect Combo, double TextMaxWidth) Placed(double width, double height, double dpi, double size, double x, double y)
    {
        double windowScale = ComboHintLayout.Scale(height, dpi);
        double scale = AlertLayout.Scale(windowScale, size);
        var (comboLeft, comboTop) = ComboHintLayout.Place(x * width, y * height, width, height);
        double right = width - comboLeft;
        double textMaxWidth = AlertLayout.TextMaxWidth(right, width, scale);
        var (alerts, combo) = OnSta(() => (AlertStack(scale, textMaxWidth), ComboHint(windowScale)));
        double left = AlertLayout.Left(right, alerts.Width, width);
        return (new Rect(left, AlertLayout.Top(comboTop, height, scale), alerts.Width, alerts.Height),
            new Rect(comboLeft, comboTop, combo.Width, combo.Height), textMaxWidth);
    }

    [Fact]
    public void The_default_size_is_150_percent_and_old_settings_files_get_it()
    {
        Assert.Equal(1.5, HudConfig.DefaultAlertScale);
        Assert.Equal(1.5, new HudConfig().AlertScale);
        // Nobody could choose a size before version 8: a file from then has none and reads the new default.
        Assert.Equal(1.5, ConfigStore.Parse("""{ "SettingsVersion": 7, "AlertsEnabled": true }""").AlertScale);
        Assert.Equal(8, HudConfig.CurrentSettingsVersion);
    }

    [Theory]
    [InlineData("""{ "AlertScale": 0.7 }""", 0.7)]
    [InlineData("""{ "AlertScale": 10 }""", 2.0)]
    [InlineData("""{ "AlertScale": 0 }""", 0.5)]
    [InlineData("""{ "AlertScale": 2, "SettingsVersion": 8 }""", 2.0)]
    public void A_saved_size_is_kept_within_the_sliders_range(string json, double expected) =>
        Assert.Equal(expected, ConfigStore.Parse(json).AlertScale);

    /// <summary>
    /// Today's pop-up is drawn at the combo banner's height (72 units), smaller than the rotation strip's skill icons (80):
    /// the owner found it hard to notice. At the default size its circle is clearly bigger than both.
    /// </summary>
    [Fact]
    public void At_the_default_size_the_circle_is_clearly_bigger_than_the_combo_banner_and_the_strips_icons()
    {
        double circle = AlertPopUp.CircleSize * AlertLayout.Scale(1, HudConfig.DefaultAlertScale);
        Assert.True(circle >= 1.25 * 80, $"circle {circle} units at 1080p, the strip's icons 80");
        Assert.True(circle >= 1.4 * ComboBanner.Height, $"circle {circle} units, the combo banner {ComboBanner.Height}");
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public void At_the_default_place_every_size_stays_on_the_window_clear_of_the_centre_and_off_the_combo_hint(double width, double height, double dpi)
    {
        foreach (double size in Sizes())
        {
            var (alerts, combo, text) = Placed(width, height, dpi, size, HudConfig.DefaultComboHintX, HudConfig.DefaultComboHintY);
            if (size is 1 or 1.5 or 2)
                output.WriteLine($"{width}x{height}@{dpi} {size:P0}: alerts {alerts.Left:F0}..{alerts.Right:F0} x {alerts.Top:F0}..{alerts.Bottom:F0} (text {text:F0}), combo hint ..{combo.Right:F0}, centre {width / 2:F0}");
            string at = $"{width}x{height}@{dpi} at {size:P0}";
            Assert.True(alerts.Left >= -0.5 && alerts.Right <= width + 0.5, $"{at}: alerts {alerts.Left:F0}..{alerts.Right:F0} on a window {width} wide");
            Assert.True(alerts.Top >= -0.5 && alerts.Bottom <= height + 0.5, $"{at}: alerts {alerts.Top:F0}..{alerts.Bottom:F0} on a window {height} tall");
            Assert.True(alerts.Left >= width * (0.5 + AlertLayout.ClearOfCentre) - 0.5, $"{at}: alerts from {alerts.Left:F0}, over the centre {width / 2:F0}");
            Assert.True(combo.Right <= alerts.Left, $"{at}: combo hint to {combo.Right:F0}, alerts from {alerts.Left:F0}");
        }
    }

    /// <summary>The combo hint dragged anywhere left of the centre: the stack stays on the window and off the combo hint.</summary>
    [Theory]
    [MemberData(nameof(Screens))]
    public void With_the_combo_hint_dragged_left_of_centre_the_stack_stays_on_the_window_and_off_it(double width, double height, double dpi)
    {
        foreach (double x in new[] { 0, 0.05, 0.1, 0.15, 0.2 })
            foreach (double y in new[] { 0, 0.2, 0.36, 0.6, 0.8, 1 })
                foreach (double size in new[] { 0.5, 1, 1.5, 2 })
                {
                    var (alerts, combo, _) = Placed(width, height, dpi, size, x, y);
                    string at = $"{width}x{height}@{dpi} at {size:P0}, combo hint at ({x}, {y})";
                    Assert.True(alerts.Left >= -0.5 && alerts.Right <= width + 0.5, $"{at}: alerts {alerts.Left:F0}..{alerts.Right:F0} on a window {width} wide");
                    Assert.True(alerts.Top >= -0.5 && alerts.Bottom <= height + 0.5, $"{at}: alerts {alerts.Top:F0}..{alerts.Bottom:F0} on a window {height} tall");
                    Assert.True(!alerts.IntersectsWith(combo) || combo.Right <= alerts.Left + 0.5, $"{at}: alerts {alerts} over the combo hint {combo}");
                }
    }

    /// <summary>Wherever the combo hint is dragged (right of centre too), the stack stays on the window.</summary>
    [Theory]
    [MemberData(nameof(Screens))]
    public void Wherever_the_combo_hint_is_the_stack_stays_on_the_window(double width, double height, double dpi)
    {
        foreach (double x in new[] { 0.3, 0.5, 0.7, 0.9, 1 })
            foreach (double y in new[] { 0, 0.5, 1 })
                foreach (double size in new[] { 0.5, 2 })
                {
                    var (alerts, _, _) = Placed(width, height, dpi, size, x, y);
                    string at = $"{width}x{height}@{dpi} at {size:P0}, combo hint at ({x}, {y})";
                    Assert.True(alerts.Left >= -0.5 && alerts.Right <= width + 0.5, $"{at}: alerts {alerts.Left:F0}..{alerts.Right:F0} on a window {width} wide");
                    Assert.True(alerts.Top >= -0.5 && alerts.Bottom <= height + 0.5, $"{at}: alerts {alerts.Top:F0}..{alerts.Bottom:F0} on a window {height} tall");
                }
    }
}
