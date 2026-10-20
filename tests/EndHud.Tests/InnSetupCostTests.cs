using System.Diagnostics;
using System.IO;
using EndHud.Vision;
using EndHud.Vision.Inn;
using Xunit;
using Xunit.Abstractions;
using static EndHud.Tests.InnTestKit;

namespace EndHud.Tests;

/// <summary>
/// What the setup advice costs, measured on this machine (the numbers go to the test output; they
/// set the reading cadence, <see cref="InnFrameReader.SetupEvery"/>, and the two-stage planning,
/// <see cref="InnSetupPlanning"/>). Nothing here asserts a time: a slow test machine is not a defect.
/// </summary>
public class InnSetupCostTests(ITestOutputHelper output)
{
    private static readonly Lazy<Task<InnScreens>> Screens = new(async () =>
    {
        var text = InnText.TryCreate()
            ?? throw new InvalidOperationException("The inn tests need the Simplified Chinese OCR language (Windows Settings > Language).");
        return new InnScreens(text, await InnPictures.LoadAsync(Folder, Inn), Inn, Library);
    });

    private static Task<Frame> Load(string name) => ImageFile.LoadAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "inn", name));

    private async Task<long> Time(string what, Func<Task> work, int runs = 3)
    {
        await work(); // warm: the OCR engine, the JIT
        var watch = Stopwatch.StartNew();
        for (int i = 0; i < runs; i++) await work();
        long ms = watch.ElapsedMilliseconds / runs;
        output.WriteLine($"{what}: {ms} ms");
        return ms;
    }

    [Fact]
    public async Task What_reading_each_setup_screen_costs()
    {
        var screens = await Screens.Value;
        var setup = await Load("setup-chefs-placed.jpg");
        var menu = await Load("menu-dishes.jpg");
        var picker = await Load("chef-picker.jpg");

        await Time("detect (setup screen)", () => screens.DetectAsync(setup));
        await Time("setup screen (theme, kitchens, chefs)", () => screens.ReadSetupAsync(setup));
        await Time("menu screen (cards, counters)", () => screens.ReadMenuAsync(menu));
        await Time("menu requirements", () => screens.ReadMenuRequirementsAsync(menu));
        await Time("chef picker's kitchen", () => screens.ReadPickerKitchenAsync(picker));
    }

    [Fact]
    public void What_planning_costs()
    {
        var planner = new InnPlanner(Inn);
        var empty = NewSession();
        empty.Update(new InnReading(InnScreenKind.Setup, S(0), Setup: new InnSetup("养身心", Inn.Themes.FirstOrDefault(t => t.Zh == "养身心"), "开启2次满堂彩",
            ["至少有2道带有[素食]或[滋补]特性的菜品"], ["Veggy", "Rich"],
            [.. Enumerable.Range(1, 3).Select(n => new SetupKitchen(n, 2, [null, null], [null, null]) { ChefsPlaced = 0 })])));
        var placed = NewSession();
        placed.Update(new InnReading(InnScreenKind.Setup, S(0), Setup: new InnSetup("养身心", Inn.Themes.FirstOrDefault(t => t.Zh == "养身心"), "开启2次满堂彩",
            ["至少有2道带有[素食]或[滋补]特性的菜品"], ["Veggy", "Rich"],
            [
                new SetupKitchen(1, 2, ["Perlica", "Avywenna"], ["佩丽卡", "艾维文娜"]) { ChefsPlaced = 2 },
                new SetupKitchen(2, 2, ["Chen_Qianyu", null], ["陈千语", null]) { ChefsPlaced = 1 },
                new SetupKitchen(3, 2, [null, null], [null, null]) { ChefsPlaced = 0 },
            ])));

        foreach (var (name, input) in new[]
        {
            ("all kitchens empty", empty.SetupInputs().Input!),
            ("chefs placed, chefs' advice", placed.SetupInputs().Input!),
            ("chefs placed, menu advice", placed.MenuInputs().Input!),
        })
        {
            planner.PlanByModel(input); // warm
            var watch = Stopwatch.StartNew();
            var fast = InnSetupAdvice.Fast(planner, input);
            long fastMs = watch.ElapsedMilliseconds;
            watch.Restart();
            var refined = InnSetupAdvice.For(planner, input, CancellationToken.None);
            long refinedMs = watch.ElapsedMilliseconds;
            watch.Restart();
            var byModel = planner.PlanByModel(input);
            long modelMs = watch.ElapsedMilliseconds;
            watch.Restart();
            var simulated = planner.Plan(input);
            long planMs = watch.ElapsedMilliseconds;
            watch.Restart();
            InnSuggester.Of(planner, input, byModel);
            long suggestMs = watch.ElapsedMilliseconds;
            output.WriteLine($"{name}: model plan {modelMs} ms, simulated plan {planMs} ms, suggestions {suggestMs} ms");
            output.WriteLine($"{name}: fast (model + suggestions) {fastMs} ms, refined (simulator + suggestions) {refinedMs} ms, same advice: {fast.Signature == refined.Signature}");
        }
    }
}
