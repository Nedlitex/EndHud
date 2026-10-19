using System.IO;
using EndHud.Vision;
using EndHud.Vision.Inn;
using Xunit;
using static EndHud.Tests.InnTestKit;

namespace EndHud.Tests;

/// <summary>
/// The wire-up end to end with the real readers (<see cref="InnScreens"/>) on frames of the owner's
/// recordings (tests/data/Fixtures/inn, what each shows in its README) and a fight: frame → reading
/// loop → session → the strip's lines. Needs the Simplified Chinese OCR language, like the readers' own tests.
/// </summary>
public class InnRealFrameTests
{
    private static readonly Lazy<Task<InnScreens>> Screens = new(async () =>
    {
        var text = InnText.TryCreate()
            ?? throw new InvalidOperationException("The inn tests need the Simplified Chinese OCR language (Windows Settings > Language).");
        return new InnScreens(text, await InnPictures.LoadAsync(Folder, Inn), Inn, Library);
    });

    private static Task<Frame> Load(string name) => ImageFile.LoadAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private static async Task<(PartyMonitor Monitor, List<InnReading?> Raised)> Monitor()
    {
        var monitor = new PartyMonitor(TimeSpan.FromMilliseconds(250)) { InnFrames = new InnFrameReader(await Screens.Value) };
        var raised = new List<InnReading?>();
        monitor.InnUpdated += raised.Add;
        return (monitor, raised);
    }

    [Fact]
    public async Task A_fight_is_not_the_inn_even_when_looked_at()
    {
        var (monitor, raised) = await Monitor();

        Assert.False(await monitor.TryInnAsync(await Load("combat-combos-nearly-refilled.jpg"), S(0), squadPanelSeen: false));
        Assert.Empty(raised);
    }

    [Fact]
    public async Task A_gourmands_order_frame_becomes_a_gourmand_card_with_both_requests()
    {
        var (monitor, raised) = await Monitor();

        Assert.True(await monitor.TryInnAsync(await Load(Path.Combine("inn", "session-gourmand-order.jpg")), S(0), squadPanelSeen: false));

        var reading = Assert.Single(raised)!;
        Assert.Equal(InnScreenKind.Session, reading.Screen);
        Assert.Equal(TableState.GourmandOrder, reading.TableCards[0]!.State);
        Assert.Equal(2, reading.Bubble!.Requests(Inn).Count);

        var session = NewSession();
        session.Update(SetupScreen(-2, 1, 2, 3));
        session.Update(MenuScreen(-1, "melon-meatball-soup", "fin-mato-soup", "mala-xiang-guo", "goldfloss-chiffon", "bottled-duskberry-juice", "pre-tide-lungjing", "bottled-milk-a"));
        session.Update(reading);
        var note = Assert.Single(session.Advice(S(0))!.Gourmands);
        Assert.Equal(1, note.Table);
        Assert.Equal(2, note.Advice!.Requests.Count);

        var lines = InnStripView.Build(session.Advice(S(0))!, Text).Lines;
        Assert.Contains(lines, l => l.Text == Format("Inn.Gourmand.Title", 1));
    }

    [Fact]
    public async Task A_dispatch_panel_frame_gives_the_kitchens_queue_exactly()
    {
        var (monitor, raised) = await Monitor();

        Assert.True(await monitor.TryInnAsync(await Load(Path.Combine("inn", "dispatch-panel-queue.jpg")), S(0), squadPanelSeen: false));

        var reading = Assert.Single(raised)!;
        Assert.Equal(InnScreenKind.DispatchPanel, reading.Screen);
        Assert.Equal(1, reading.Panel!.Kitchen);
        Assert.Equal("melon-meatball-soup", reading.Panel.Queue[0].DishId);

        var session = NewSession();
        session.Update(reading);
        var advice = session.Advice(S(0))!;
        Assert.Contains(1, advice.ExactKitchens);
        Assert.NotNull(advice.Dispatch);
    }
}
