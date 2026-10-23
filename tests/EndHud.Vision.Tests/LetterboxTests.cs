using EndHud.Vision;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// A screen that isn't 16:9 (a player's 2560×1600): the game draws its 16:9 picture between black
/// bars, and the squad panel is found only once they're cut off (<see cref="Letterbox"/>).
/// </summary>
public sealed class LetterboxTests
{
    /// <summary>A 16:9 game picture at 2560×1440 with black bars to make it <paramref name="width"/>×<paramref name="height"/>.</summary>
    private static async Task<(Frame Padded, PixelRect Picture)> PaddedAsync(int width, int height)
    {
        var game = (await ImageFile.LoadAsync(CombatFrameTests.Fixture("combat-all-ready.jpg"))).ResizeSmooth(2560, 1440);
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
}
