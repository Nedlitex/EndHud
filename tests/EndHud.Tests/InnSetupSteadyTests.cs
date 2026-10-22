using System.IO;
using EndHud.Vision;
using EndHud.Vision.Inn;
using Xunit;
using Xunit.Abstractions;
using static EndHud.Tests.InnTestKit;

namespace EndHud.Tests;

/// <summary>
/// The recommendation the player has seen stays the same across the setup screen and the chef picker (owner,
/// test_yuanyuan6 at about 58 s: "the recommendation of kitchen1 changed from [Antal + Wulfgard] to [Akekuri +
/// Snowshine] when clicked into the kitchen, the recommendation should not change, unless some kitchen is filled, but
/// this is the first kitchen"). What the recording and the log show: the quick plan went on screen at 56.6 s with the
/// three kitchens empty; the simulator's plan for the same inputs came 8.4 s after they were asked for (the first plan
/// of the visit), at 64.1 s, with the player in kitchen 1's picker placing Wulfgard, and was judged clearly better, so it replaced the
/// plan the player was following. Replayed here on frames of that recording through the real readers, memory,
/// planner and judge; the simulator's plan held back until the player is in the picker, as it was.
/// </summary>
public class InnSetupSteadyTests(ITestOutputHelper output)
{
    private static readonly Lazy<Task<InnScreens>> Screens = new(async () =>
    {
        var text = InnText.TryCreate()
            ?? throw new InvalidOperationException("The inn tests need the Simplified Chinese OCR language (Windows Settings > Language).");
        return new InnScreens(text, await InnPictures.LoadAsync(Folder, Inn), Inn, Library);
    });

    private static readonly InnPlanner Planner = new(Inn);

    private static Task<Frame> Load(string name) => ImageFile.LoadAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "inn", name));

    private static IReadOnlyList<string> Chefs(InnSetupAdvice advice, int kitchen) =>
        [.. advice.Plan.Kitchens.Single(k => k.Number == kitchen).Chefs.Select(c => c.Id)];

    private static string Say(IReadOnlyList<InnAdvicePanel> panels) =>
        string.Join(" | ", panels.Select(p => $"{p.Spot}: {string.Join(" / ", p.Model.Lines.Select(l => l.Text))}"));

    /// <summary>The names on a panel's "recommended" row.</summary>
    private static IReadOnlyList<string> Recommended(InnAdvicePanel panel) =>
        [.. panel.Model.Lines.Single(l => l.Text.StartsWith(Text("Inn.Setup.Recommend"), StringComparison.Ordinal)).Segments
            .Select(s => s.PictureId).OfType<string>()];

    [Fact]
    public async Task Kitchen_ones_recommendation_stays_from_the_setup_screen_into_its_picker_and_after_a_chef_is_placed()
    {
        var reader = new InnFrameReader(await Screens.Value);
        var session = NewSession();
        var refinedMayCome = new ManualResetEventSlim(false);
        int refinedDone = 0;
        var planning = new InnSetupPlanning(new InnPlanJudge(Planner), input => InnSetupAdvice.Fast(Planner, input), (input, cancel) =>
        {
            refinedMayCome.Wait(cancel); // seconds, as the first plan of a visit took (8.4 s in the log)
            var refined = InnSetupAdvice.For(Planner, input, cancel);
            Interlocked.Increment(ref refinedDone);
            return refined;
        }, note: output.WriteLine);

        // 55.5 s: the setup screen, theme Soup & Entree, the three kitchens empty.
        var setup = await reader.ReadAsync(await Load("setup-soup-entree-empty.jpg"), S(0), squadPanelSeen: false);
        Assert.NotNull(setup?.Setup);
        Assert.Equal("soup-entree", setup.Setup.Theme?.Id);
        Assert.All(setup.Setup.Kitchens!, k => Assert.Equal(0, k.ChefsPlaced));
        session.Update(setup);
        var work = planning.Request(session.SetupInputs(), S(0));
        Assert.NotNull(work);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
        while (planning.Showable(session.SetupInputs()) is null)
        {
            Assert.True(DateTime.UtcNow < deadline, "the quick advice never came");
            await Task.Delay(10);
        }
        var seen = planning.Showable(session.SetupInputs())!;
        var kitchenOne = Chefs(seen, 1);
        var onSetup = InnSetupView.Setup(session.SetupInputs(), seen, Inn, Text);
        output.WriteLine($"setup screen: {Say(onSetup)}");
        Assert.Equal(kitchenOne.Select(id => InnSetupView.OperatorPicture + id), Recommended(onSetup.Single(p => p.Spot == InnAdviceSpot.Kitchen1)));

        // 61 s: kitchen 1's chef picker. Its reading changes none of the setup's inputs.
        var picker = await reader.ReadAsync(await Load("chef-picker-kitchen1-empty.jpg"), S(4), squadPanelSeen: false);
        Assert.Equal(InnScreenKind.ChefPicker, picker?.Screen);
        Assert.Equal(1, picker!.PickerKitchen);
        session.Update(picker);
        Assert.Null(planning.Request(session.SetupInputs(), S(4)));

        // 64.1 s: the simulator's plan for these inputs arrives, the player still in the picker.
        refinedMayCome.Set();
        await work;
        Assert.Equal(1, refinedDone);
        var inPicker = InnSetupView.Picker(session.PickerKitchen, session.SetupInputs(), planning.Showable(session.SetupInputs()), Inn, Text);
        output.WriteLine($"picker after the simulator's plan: {Say(inPicker)}");
        Assert.Equal(kitchenOne, Chefs(planning.Showable(session.SetupInputs())!, 1));
        Assert.Equal(kitchenOne.Select(id => InnSetupView.OperatorPicture + id), Recommended(inPicker.Single()));

        // Back on the setup screen with kitchen 1's first recommended chef placed: the rest of the same advice.
        var placed = Library.Operators.Single(o => o.Id == kitchenOne[0]);
        var oneIn = setup with
        {
            At = S(8),
            Setup = setup.Setup with
            {
                Kitchens = [.. setup.Setup.Kitchens!.Select(k => k.Number == 1 ? new SetupKitchen(1, 2, [placed.Id, null], [placed.NameZh, null]) { ChefsPlaced = 1 } : k)],
            },
        };
        session.Update(oneIn);
        if (planning.Request(session.SetupInputs(), S(8)) is { } again)
        {
            refinedMayCome.Set();
            await again;
        }
        var after = planning.Showable(session.SetupInputs())!;
        var panels = InnSetupView.Setup(session.SetupInputs(), after, Inn, Text);
        output.WriteLine($"setup screen, {placed.NameZh} placed: {Say(panels)}");
        Assert.Equal(kitchenOne, Chefs(after, 1));
        Assert.Equal([InnSetupView.OperatorPicture + kitchenOne[1]], Recommended(panels.Single(p => p.Spot == InnAdviceSpot.Kitchen1)));
        Assert.Equal(1, planning.Changes); // one plan put on screen, never another
    }
}
