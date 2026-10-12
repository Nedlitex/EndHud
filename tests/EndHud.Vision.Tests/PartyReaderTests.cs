using EndHud.Vision;
using Xunit;

namespace EndHud.Vision.Tests;

public class PartyReaderTests
{
    // In-game 4K screenshot: four level-90 operators at full HP, ping 164 ms. (UID blacked out.)
    private static readonly string Fixture4K = Path.Combine(AppContext.BaseDirectory, "Fixtures", "exploration-levels-visible.png");

    [Fact]
    public async Task Reads_levels_hp_and_ping_from_4k_screenshot()
    {
        var frame = await ImageFile.LoadAsync(Fixture4K);

        var state = await new PartyReader(new TextRecognizer()).ReadAsync(frame);

        Assert.True(state.PanelVisible);
        Assert.Equal([90, 90, 90, 90], state.Slots.Select(s => s.Level));
        Assert.All(state.Slots, s => Assert.InRange(s.Hp!.Value, 0.98, 1.0));
        Assert.Equal(164, state.PingMs);
    }

    [Fact]
    public async Task Reports_panel_not_visible_on_a_blank_frame()
    {
        var frame = new Frame(1920, 1080, new byte[1920 * 1080 * 4]);

        var state = await new PartyReader(new TextRecognizer()).ReadAsync(frame);

        Assert.False(state.PanelVisible);
        Assert.All(state.Slots, s => Assert.Equal(0, s.Hp));
    }

    [Theory]
    [InlineData("LV.90", 90)]
    [InlineData("LV 90", 90)]
    [InlineData("Lv. 9O", 90)]
    [InlineData("LY 90", 90)]
    [InlineData("1 90", 90)]
    [InlineData("TV 90,", 90)]
    [InlineData("901", 90)]
    [InlineData("LV.1", 1)]
    [InlineData("LV", null)]
    [InlineData("", null)]
    public void ParseLevel(string ocrText, int? expected) => Assert.Equal(expected, PartyReader.ParseLevel(ocrText));

    [Theory]
    [InlineData("164ms", 164)]
    [InlineData("164 ms", 164)]
    [InlineData("I64ms", 164)]
    [InlineData("ms", null)]
    public void ParsePing(string ocrText, int? expected) => Assert.Equal(expected, PartyReader.ParsePing(ocrText));
}

public class HpBarTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(37)]
    [InlineData(100)]
    public void Measures_fill_fraction(int percent)
    {
        // A 200×12 bar: cyan fill up to `percent`, dark grey track after (game colours).
        const int width = 200, height = 12;
        var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int i = (y * width + x) * 4;
            bool filled = x < width * percent / 100;
            (pixels[i + 2], pixels[i + 1], pixels[i]) = filled ? ((byte)0x16, (byte)0xC5, (byte)0xFD) : ((byte)0x1D, (byte)0x1F, (byte)0x21);
            pixels[i + 3] = 255;
        }

        double? hp = HpBar.Measure(new Frame(width, height, pixels), new PixelRect(0, 0, width, height));

        Assert.Equal(percent / 100.0, hp!.Value, 2);
    }
}
