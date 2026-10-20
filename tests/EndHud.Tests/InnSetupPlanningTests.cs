using System.Diagnostics;
using EndHud.Vision.Inn;
using Xunit;
using Xunit.Abstractions;
using static EndHud.Tests.InnTestKit;

namespace EndHud.Tests;

/// <summary>
/// The setup advice follows the screen (owner, live 2026-10-19: "the page refreshes too slowly"; three
/// empty kitchens, two of them advised "set"). <see cref="InnSetupPlanning"/>: a quick first advice,
/// replaced by the simulator's only when it differs; newer inputs cancel the older work; a plan for
/// chefs since taken out is never shown for the kitchens they left.
/// </summary>
public class InnSetupPlanningTests(ITestOutputHelper output)
{
    private static readonly InnPlanner Planner = new(Inn);

    private static InnReading Wholesome(double at, params SetupKitchen[] kitchens) =>
        new(InnScreenKind.Setup, S(at), Setup: new InnSetup("养身心", Inn.Themes.Single(t => t.Zh == "养身心"), "开启2次满堂彩",
            ["至少有2道带有[素食]或[滋补]特性的菜品"], ["Veggy", "Rich"], kitchens));

    private static SetupKitchen EmptyKitchen(int number) => new(number, 2, [null, null], [null, null]) { ChefsPlaced = 0 };

    private static InnReading Staffed(double at) => Wholesome(at,
        new SetupKitchen(1, 2, ["Perlica", "Avywenna"], ["佩丽卡", "艾维文娜"]) { ChefsPlaced = 2 },
        new SetupKitchen(2, 2, ["Chen_Qianyu", "Ardelia"], ["陈千语", "艾尔黛拉"]) { ChefsPlaced = 2 },
        EmptyKitchen(3));

    private static InnReading Cleared(double at) => Wholesome(at, EmptyKitchen(1), EmptyKitchen(2), EmptyKitchen(3));

    /// <summary>The quick advice for a session's setup inputs.</summary>
    private static InnSetupAdvice Quick(InnSession session) => InnSetupAdvice.Fast(Planner, session.SetupInputs().Input!);

    private static IReadOnlyList<string> KitchenLines(IReadOnlyList<InnAdvicePanel> panels, int kitchen) =>
        [.. panels.Single(p => p.Spot == (InnAdviceSpot)kitchen).Model.Lines.Select(l => l.Text)];

    /// <summary>
    /// The owner's screenshot: chefs were placed, then every place cleared. The plan worked out for the
    /// placed chefs called kitchens 1 and 2 set, and it stayed on screen for the empty kitchens while
    /// theirs was worked out (seconds). It isn't shown for kitchens whose chefs have gone; the new plan
    /// advises chefs for every place.
    /// </summary>
    [Fact]
    public async Task A_plan_for_chefs_since_taken_out_is_not_shown_for_their_empty_kitchens()
    {
        var gate = new ManualResetEventSlim(true);
        var planning = new InnSetupPlanning(input =>
        {
            gate.Wait();
            return InnSetupAdvice.Fast(Planner, input);
        }, note: _ => { });
        var session = NewSession();
        session.Update(Staffed(0));
        await planning.Request(session.SetupInputs())!;
        var staffed = planning.Showable(session.SetupInputs());
        Assert.NotNull(staffed);
        Assert.Equal(Text("Inn.Setup.Full"), KitchenLines(InnSetupView.Setup(session.SetupInputs(), staffed, Inn, Text), 1)[^1]);

        session.Update(Cleared(2));
        gate.Reset(); // the plan for the empty kitchens is being worked out
        var cleared = planning.Request(session.SetupInputs());
        Assert.NotNull(cleared);
        Assert.Null(planning.Showable(session.SetupInputs())); // not the plan saying kitchens 1 and 2 are set

        gate.Set();
        await cleared;
        var advice = planning.Showable(session.SetupInputs());
        Assert.NotNull(advice);
        var panels = InnSetupView.Setup(session.SetupInputs(), advice, Inn, Text);
        foreach (int kitchen in new[] { 1, 2, 3 })
        {
            var lines = KitchenLines(panels, kitchen);
            Assert.DoesNotContain(Text("Inn.Setup.Full"), lines);
            Assert.Contains(lines, l => l.StartsWith(Text("Inn.Setup.Recommend"), StringComparison.Ordinal));
        }
    }

    /// <summary>The quick advice shows first; the simulator's replaces it only when a panel would show something else.</summary>
    [Fact]
    public async Task The_quick_advice_shows_first_and_the_refined_one_replaces_it_only_when_different()
    {
        var session = NewSession();
        session.Update(Staffed(0));
        var quick = Quick(session);
        var other = InnSetupAdvice.Fast(Planner, NewSessionWith(Cleared(0)).SetupInputs().Input!);
        Assert.NotEqual(quick.Signature, other.Signature);

        // The same advice refined (another object, the same on screen): the quick one stays, nothing redrawn.
        var release = new ManualResetEventSlim(false);
        int doneCalls = 0;
        var same = new InnSetupPlanning(_ => quick, (_, _) =>
        {
            release.Wait();
            return quick with { };
        }, note: _ => { });
        var work = same.Request(session.SetupInputs(), () => Interlocked.Increment(ref doneCalls))!;
        await WaitUntil(() => same.Showable(session.SetupInputs()) is not null);
        Assert.Same(quick, same.Showable(session.SetupInputs())); // shown before the refined one is ready
        release.Set();
        await work;
        Assert.Same(quick, same.Showable(session.SetupInputs()));
        Assert.Equal(0, same.Replaced);
        Assert.Equal(1, doneCalls);

        // A different refined advice replaces it.
        doneCalls = 0;
        var different = new InnSetupPlanning(_ => quick, (_, _) => other, note: _ => { });
        await different.Request(session.SetupInputs(), () => Interlocked.Increment(ref doneCalls))!;
        Assert.Same(other, different.Showable(session.SetupInputs()));
        Assert.Equal(1, different.Replaced);
        Assert.Equal(2, doneCalls);
    }

    /// <summary>Inputs changed while older ones are worked out: the older work is cancelled, and its late result is never kept.</summary>
    [Fact]
    public async Task Newer_inputs_cancel_the_older_work_and_only_their_advice_is_kept()
    {
        var staffed = NewSessionWith(Staffed(0));
        var cleared = NewSessionWith(Cleared(0));
        var staffedAdvice = Quick(staffed);
        var clearedAdvice = Quick(cleared);
        var firstGate = new ManualResetEventSlim(false);
        int olderRefined = 0;
        var planning = new InnSetupPlanning(input =>
        {
            if (input.Kitchens.Any(k => k.Placed.Count > 0)) firstGate.Wait(); // the older inputs' quick advice is slow
            return input.Kitchens.Any(k => k.Placed.Count > 0) ? staffedAdvice : clearedAdvice;
        }, (input, cancel) =>
        {
            if (input.Kitchens.Any(k => k.Placed.Count > 0)) Interlocked.Increment(ref olderRefined);
            return input.Kitchens.Any(k => k.Placed.Count > 0) ? staffedAdvice : clearedAdvice;
        }, note: _ => { });

        // Older inputs, then newer ones while the older are still worked out; the older finish last.
        var older = planning.Request(staffed.SetupInputs())!;
        var newer = planning.Request(cleared.SetupInputs())!;
        await newer;
        firstGate.Set();
        await older;

        Assert.Same(clearedAdvice, planning.Showable(cleared.SetupInputs())); // not replaced by the older inputs' late advice
        Assert.Equal(0, olderRefined); // their refinement never ran
        Assert.Equal(2, planning.Started);
    }

    [Fact]
    public async Task Cancelled_refinement_stops_the_simulator_and_says_nothing()
    {
        var logged = new List<string>();
        var entered = new ManualResetEventSlim(false);
        var seen = new System.Collections.Concurrent.ConcurrentQueue<CancellationToken>();
        var planning = new InnSetupPlanning(input => InnSetupAdvice.Fast(Planner, input), (input, cancel) =>
        {
            seen.Enqueue(cancel);
            entered.Set();
            return InnSetupAdvice.For(Planner, input, cancel);
        }, log: logged.Add, note: _ => { });
        var first = planning.Request(NewSessionWith(Cleared(0)).SetupInputs())!;
        entered.Wait(TimeSpan.FromSeconds(30));
        var watch = Stopwatch.StartNew();
        var second = planning.Request(NewSessionWith(Staffed(0)).SetupInputs())!;
        await first;
        output.WriteLine($"the cancelled refinement stopped {watch.ElapsedMilliseconds} ms after newer inputs");
        Assert.True(seen.TryPeek(out var older) && older.IsCancellationRequested);
        Assert.Empty(logged);
        await second;
    }

    // ---- Measured: from a changed reading to the advice for it ----

    /// <summary>
    /// The time from a reading that changes the setup (every place cleared) to the advice for it on screen
    /// (<see cref="InnSetupPlanning.Showable"/> non-null for the new inputs), with the real planner. Before: one
    /// stage, the simulator's plan. After: the quick advice first. Numbers to the test output; asserted only
    /// that the quick advice comes first.
    /// </summary>
    [Fact]
    public async Task From_a_changed_reading_to_the_advice_on_screen()
    {
        async Task<long> Measure(InnSetupPlanning planning)
        {
            var session = NewSessionWith(Staffed(0));
            await planning.Request(session.SetupInputs())!;
            var watch = Stopwatch.StartNew();
            session.Update(Cleared(1));
            var work = planning.Request(session.SetupInputs());
            await WaitUntil(() => planning.Showable(session.SetupInputs()) is not null, TimeSpan.FromSeconds(120));
            long ms = watch.ElapsedMilliseconds;
            await work!;
            return ms;
        }

        long before = await Measure(new InnSetupPlanning(input => InnSetupAdvice.For(Planner, input, CancellationToken.None), note: _ => { }));
        long after = await Measure(new InnSetupPlanning(input => InnSetupAdvice.Fast(Planner, input),
            (input, cancel) => InnSetupAdvice.For(Planner, input, cancel), note: _ => { }));
        output.WriteLine($"changed reading to advice on screen: before (simulator's plan only) {before} ms, after (quick advice first) {after} ms");
        Assert.True(after < before, $"after {after} ms, before {before} ms");
    }

    private static InnSession NewSessionWith(InnReading reading)
    {
        var session = NewSession();
        session.Update(reading);
        return session;
    }

    private static async Task WaitUntil(Func<bool> condition, TimeSpan? limit = null)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.Elapsed > (limit ?? TimeSpan.FromSeconds(30))) throw new TimeoutException("the advice never came");
            await Task.Delay(5);
        }
    }
}
