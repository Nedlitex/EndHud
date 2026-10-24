using EndHud.Vision;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// A screen that isn't 16:9 (a player's 2560×1600): the game draws its 16:9 picture between black
/// bars, and the squad panel is found only once they're cut off (<see cref="Letterbox"/>).
/// </summary>
public sealed class LetterboxTests
{
    /// <summary>A 16:9 game picture (2560×1440 on a 1440p or 1600p screen) with black bars to make it <paramref name="width"/>×<paramref name="height"/>.</summary>
    private static async Task<(Frame Padded, PixelRect Picture)> PaddedAsync(int width, int height)
    {
        int pictureW = Math.Min(width, height * 16 / 9);
        var game = (await ImageFile.LoadAsync(CombatFrameTests.Fixture("combat-all-ready.jpg"))).ResizeSmooth(pictureW, pictureW * 9 / 16);
        int x0 = (width - game.Width) / 2, y0 = (height - game.Height) / 2;
        var bgra = new byte[width * height * 4];
        for (int i = 3; i < bgra.Length; i += 4) bgra[i] = 255;
        for (int y = 0; y < game.Height; y++)
            Buffer.BlockCopy(game.Bgra, y * game.Width * 4, bgra, ((y + y0) * width + x0) * 4, game.Width * 4);
        return (new Frame(width, height, bgra), new PixelRect(x0, y0, game.Width, game.Height));
    }

    [Fact]
    public async Task On_a_16_10_screen_the_bars_are_cut_off_and_the_squad_panel_is_found()
    {
        var (padded, picture) = await PaddedAsync(2560, 1600);
        var reader = new PartyReader(new TextRecognizer());

        Assert.False((await reader.ReadAsync(padded)).PanelVisible); // read with the bars: 80 px too low
        Assert.Equal(picture, Letterbox.Content(padded));
        Assert.True((await reader.ReadAsync(padded.Crop(Letterbox.Content(padded)))).PanelVisible);
    }

    [Fact]
    public async Task Bars_at_the_sides_of_a_wider_screen_are_cut_off_too()
    {
        var (padded, picture) = await PaddedAsync(3440, 1440);

        Assert.Equal(picture, Letterbox.Content(padded));
    }

    [Fact]
    public async Task A_16_9_picture_is_left_whole()
    {
        var game = await ImageFile.LoadAsync(CombatFrameTests.Fixture("combat-all-ready.jpg"));

        Assert.Equal(new PixelRect(0, 0, game.Width, game.Height), Letterbox.Content(game));
    }

    [Fact]
    public async Task Rows_that_aren_t_black_aren_t_bars()
    {
        var (padded, _) = await PaddedAsync(2560, 1600);
        // Something drawn across the top "bar": not a letterbox, so nothing is cut.
        for (int x = 0; x < padded.Width; x++) padded.Bgra[(10 * padded.Width + x) * 4 + 1] = 200;

        Assert.Equal(new PixelRect(0, 0, padded.Width, padded.Height), Letterbox.Content(padded));
    }
    /// <summary>
    /// MSI Afterburner's on-screen display (drawn by RivaTuner inside the game's frames) lands in a black bar on a screen
    /// that isn't 16:9, wherever the player puts it. One glyph used to make the bar "not black", so the bars were kept, every
    /// HUD region was read 60–80 px off and nothing was found (2026-10-24 investigation: 392 of 392 variants). The bars
    /// are still cut off, at every position, with and without the display's background, the one-line FPS counter too.
    /// </summary>
    [Theory]
    [InlineData(2560, 1600)]
    [InlineData(1920, 1200)]
    [InlineData(3440, 1440)]
    public async Task An_on_screen_display_in_a_bar_doesn_t_keep_the_bars(int width, int height)
    {
        var (padded, picture) = await PaddedAsync(width, height);
        foreach (var position in SyntheticOsd.Positions)
        foreach (bool background in new[] { true, false })
        foreach (bool minimal in new[] { false, true })
        {
            var covered = SyntheticOsd.Draw(padded, position, background, minimal);
            Assert.True(Letterbox.Content(covered) == picture,
                $"{width}x{height}, display at {position}{(background ? " on its background" : "")}{(minimal ? ", FPS only" : "")}: read {Letterbox.Content(covered)}");
        }
    }

    [Fact]
    public async Task With_the_display_in_a_bar_the_squad_panel_is_found()
    {
        var (padded, _) = await PaddedAsync(2560, 1600);
        var covered = SyntheticOsd.Draw(padded, OsdPosition.TopLeft, background: true);

        Assert.True((await new PartyReader(new TextRecognizer()).ReadAsync(covered.Crop(Letterbox.Content(covered)))).PanelVisible);
    }

    /// <summary>A 16:10 picture that fills the screen (no bars): its top and bottom rows are the picture, not bars.</summary>
    [Fact]
    public async Task A_picture_filling_a_16_10_frame_is_left_whole()
    {
        var stretched = (await ImageFile.LoadAsync(CombatFrameTests.Fixture("combat-all-ready.jpg"))).ResizeSmooth(2560, 1600);

        Assert.Equal(new PixelRect(0, 0, 2560, 1600), Letterbox.Content(stretched));
    }
}
