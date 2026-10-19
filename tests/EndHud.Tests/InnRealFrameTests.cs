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
public class InnRealFrameTests(Xunit.Abstractions.ITestOutputHelper output)
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

    /// <summary>
    /// The owner's 养身心 setup, read by the real readers: 营业设定 alone gives the panel's
    /// per-kitchen suggestions (no chef picker, no menu screen needed; owner, 2026-10-19), meeting every requirement
    /// on screen (Rating 4: at least 3 types, 6 dishes and 3 drinks; 养身心: at least 2 [素食] or
    /// [滋补] dishes). With the menu screen's two frames seen as well, still the same shape.
    /// </summary>
    [Fact]
    public async Task The_setup_screen_alone_gives_kitchen_blocks_meeting_every_rule()
    {
        var (monitor, raised) = await Monitor();
        double at = 0;
        async Task Show(string fixture)
        {
            Assert.True(await monitor.TryInnAsync(await Load(Path.Combine("inn", fixture + ".jpg")), S(at), squadPanelSeen: false), fixture);
            at += 3;
        }
        await Show("setup-chefs-placed");
        await Show("chef-picker"); // told apart, nothing read
        Assert.Equal(InnScreenKind.ChefPicker, raised[^1]!.Screen);

        var session = NewSession();
        foreach (var reading in raised) session.Update(reading!);
        var inputs = session.SetupInputs();
        Assert.Empty(inputs.Gaps);
        var advice = InnSetupAdvice.For(new InnPlanner(Inn), inputs.Input!);
        var plan = advice.Plan;

        Assert.True(plan.RulesMet);
        Assert.InRange(plan.Dishes.Count, 6, 8);
        Assert.InRange(plan.Drinks.Count, 3, 8);
        Assert.True(plan.Dishes.Select(d => d.Dish.Type).Distinct().Count() >= 3);
        Assert.True(plan.Dishes.Count(d => d.Dish.Traits.Contains("Veggy") || d.Dish.Traits.Contains("Rich")) >= 2);
        Assert.Equal(6, plan.Kitchens.Sum(k => k.Chefs.Count));

        var lines = InnSetupView.Build(inputs, advice, Inn, Text).Lines.Select(l => l.Text).ToList();
        foreach (string line in lines) output.WriteLine(line);
        Assert.Equal("缘缘庄 · 推荐（没有的干员/菜，换「可换」里的）", lines[0]);
        string[] kitchen = ["干员：", "可换：", "做菜：", "可换："];
        // Kitchen 1 is full (Perlica and Avywenna placed): no chefs to suggest there.
        Assert.Equal(["厨房一", "做菜：", "可换：", "厨房二", .. kitchen, "厨房三", .. kitchen, "饮品：", "可换："],
            lines.Skip(1).Select(l => l.StartsWith("厨房") ? l[..3] : l[..(l.IndexOf('：') + 1)]));

        // The menu screen seen too: it refines (stock, the rules as stated), the panel keeps its shape.
        await Show("menu-dishes");
        await Show("menu-drinks");
        foreach (var reading in raised.Skip(2)) session.Update(reading!);
        var refined = session.SetupInputs();
        Assert.Empty(refined.Gaps);
        Assert.Equal((6, 3, 3), (refined.Input!.Rules.MinDishes, refined.Input.Rules.MinDrinks, refined.Input.Rules.MinTypes));
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
