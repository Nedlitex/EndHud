using Xunit;

namespace EndHud.Tests;

/// <summary>
/// The combo-ready banner's size and position for laptops and desktops: game windows from
/// 1280x720 to 5K, at the display scaling Windows uses on them (100% on most desktop monitors,
/// 125-200% on laptops and high-DPI monitors).
/// </summary>
public class ComboHintLayoutTests
{
    private const double BannerHeight = 72; // units, before scaling (see OverlayWindow.ComboHint.cs)
    private const double NameFontSize = 20;

    /// <summary>Game window sizes in physical pixels: laptop panels, desktop monitors, windows.</summary>
    public static readonly (int Width, int Height)[] GameWindows =
    [
        (1280, 720), (1366, 768), (1600, 900), (1920, 1080), (1920, 1200), (2256, 1504), (2560, 1440),
        (2560, 1600), (2880, 1800), (3000, 2000), (3440, 1440), (3840, 2160), (5120, 1440), (5120, 2880),
    ];

    public static readonly double[] DisplayScalings = [1.0, 1.25, 1.5, 1.75, 2.0];

    public static TheoryData<int, int> Windows()
    {
        var data = new TheoryData<int, int>();
        foreach (var (w, h) in GameWindows) data.Add(w, h);
        return data;
    }

    private static double PhysicalBannerHeight(int windowHeight, double dpiScale) =>
        BannerHeight * ComboHintLayout.Scale(windowHeight / dpiScale, dpiScale) * dpiScale;

    [Theory]
    [MemberData(nameof(Windows))]
    public void Banner_is_the_same_size_on_the_game_window_at_any_display_scaling(int width, int height)
    {
        // A laptop at 150% and a desktop at 100% with the same game window must show the same banner.
        double atHundred = PhysicalBannerHeight(height, 1.0);
        foreach (double dpi in DisplayScalings)
            Assert.Equal(atHundred, PhysicalBannerHeight(height, dpi), 6);
        _ = width;
    }

    [Theory]
    [MemberData(nameof(Windows))]
    public void Banner_is_readable_and_never_covers_much_of_the_game(int width, int height)
    {
        foreach (double dpi in DisplayScalings)
        {
            double scale = ComboHintLayout.Scale(height / dpi, dpi);
            double namePixels = NameFontSize * scale * dpi;
            double share = PhysicalBannerHeight(height, dpi) / height;
            Assert.True(namePixels >= 12, $"{width}x{height} at {dpi:P0}: name only {namePixels:F1} px tall");
            Assert.True(share <= 0.12, $"{width}x{height} at {dpi:P0}: one banner is {share:P0} of the window's height");
        }
    }

    [Fact]
    public void Banner_scales_with_the_game_window_between_its_limits()
    {
        Assert.Equal(1.0, PhysicalBannerHeight(1080, 1.0) / BannerHeight, 6);
        Assert.Equal(2.0, PhysicalBannerHeight(2160, 1.5) / BannerHeight, 6);
        Assert.Equal(ComboHintLayout.MinScale, PhysicalBannerHeight(480, 2.0) / BannerHeight, 6);
        Assert.Equal(ComboHintLayout.MaxScale, PhysicalBannerHeight(4320, 1.0) / BannerHeight, 6);
    }

    [Theory]
    [MemberData(nameof(Windows))]
    public void Saved_position_lands_inside_the_window_at_any_size_and_scaling(int width, int height)
    {
        // Positions are saved as a fraction of the window, and may come from a different screen.
        double[] fractions = [0, 0.2, 0.36, 0.5, 0.99, 1.0, 1.5, -0.2];
        foreach (double dpi in DisplayScalings)
        {
            double w = width / dpi, h = height / dpi;
            foreach (double fx in fractions)
            foreach (double fy in fractions)
            {
                var (left, top) = ComboHintLayout.Place(fx * w, fy * h, w, h);
                Assert.InRange(left, 0, w - ComboHintLayout.MinVisible);
                Assert.InRange(top, 0, h - ComboHintLayout.MinVisible);
            }
        }
    }

    [Fact]
    public void Default_position_is_the_same_spot_on_every_screen()
    {
        // Left of centre, as a share of the window, whatever its size or scaling.
        foreach (var (width, height) in GameWindows)
        foreach (double dpi in DisplayScalings)
        {
            double w = width / dpi, h = height / dpi;
            var (left, top) = ComboHintLayout.Place(HudConfig.DefaultComboHintX * w, HudConfig.DefaultComboHintY * h, w, h);
            Assert.Equal(HudConfig.DefaultComboHintX, left / w, 6);
            Assert.Equal(HudConfig.DefaultComboHintY, top / h, 6);
        }
    }
}
