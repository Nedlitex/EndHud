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
    /// C1 on the owner's 养身心 setup: 营业设定, 厨房一's chef picker, the menu's dishes and its drinks,
    /// read by the real readers, give a plan that meets every requirement on screen and uses only
    /// the chefs, dishes and drinks the screens showed.
    /// </summary>
    [Fact]
    public async Task The_setup_screens_give_a_plan_of_only_what_they_showed_meeting_every_rule()
    {
        var (monitor, raised) = await Monitor();
        double at = 0;
        async Task Show(string fixture)
        {
            Assert.True(await monitor.TryInnAsync(await Load(Path.Combine("inn", fixture + ".jpg")), S(at), squadPanelSeen: false), fixture);
            at += 3;
        }
        await Show("setup-chefs-placed");
        // The picker's portraits are compared with the avatars once those are described for this
        // window size, in the background (seconds on a first start): until then its cards stay
        // unnamed, and the screen is read again every two seconds, as here.
        await Show("chef-picker");
        for (int tries = 0; tries < 120 && raised[^1]!.Picker!.Cards.All(c => c.OperatorId is null); tries++)
        {
            await Task.Delay(500);
            await Show("chef-picker");
        }
        await Show("menu-dishes");
        await Show("menu-drinks");
        var picker = raised.Last(r => r!.Picker is not null)!.Picker!;

        var session = NewSession();
        foreach (var reading in raised) session.Update(reading!);
        var inputs = session.SetupInputs();
        Assert.Empty(inputs.Gaps);
        var plan = new InnPlanner(Inn).Plan(inputs.Input!);

        // The chefs shown: the picker's named cards and chosen chefs, and the one placed in kitchen 2.
        var shown = picker.Named.Append("Chen_Qianyu").ToHashSet();
        Assert.Equal(15, shown.Count);
        Assert.All(plan.Kitchens.SelectMany(k => k.Chefs), c => Assert.Contains(c.Id, shown));
        var offered = raised.Where(r => r!.Menu is not null).SelectMany(r => r!.Menu!.Cards).Select(c => c.Id).ToHashSet();
        Assert.All(plan.Dishes, d => Assert.Contains(d.Dish.Id, offered));
        Assert.All(plan.Drinks, d => Assert.Contains(d.Drink.Id, offered));
        // 菜单要求: Rating 4 (≥3 types, ≥6 dishes and 3 drinks) and 养身心's ≥2 [素食] or [滋补] dishes.
        Assert.True(plan.RulesMet);
        Assert.InRange(plan.Dishes.Count, 6, 8);
        Assert.InRange(plan.Drinks.Count, 3, 8);
        Assert.True(plan.Dishes.Select(d => d.Dish.Type).Distinct().Count() >= 3);
        Assert.True(plan.Dishes.Count(d => d.Dish.Traits.Contains("Veggy") || d.Dish.Traits.Contains("Rich")) >= 2);
        Assert.Equal(6, plan.Kitchens.Sum(k => k.Chefs.Count));

        var lines = InnSetupView.Build(inputs, plan, Text).Lines.Select(l => l.Text).ToList();
        foreach (string line in lines) output.WriteLine(line);
        output.WriteLine($"picker named {picker.Cards.Count(c => c.OperatorId is not null)} of {picker.Cards.Count} cards, chosen {string.Join(" ", picker.Selected)}");
        Assert.Equal(["缘缘庄 · 推荐", "厨房一：", "厨房二：", "厨房三：", "菜单：", "饮品："], lines.Select(l => l.Contains('：') ? l[..(l.IndexOf('：') + 1)] : l));
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
