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
        var planning = new InnSetupPlanning(Replacing, input =>
        {
            gate.Wait();
            return InnSetupAdvice.Fast(Planner, input);
        }, note: _ => { });
        var session = NewSession();
        session.Update(Staffed(0));
        await planning.Request(session.SetupInputs(), Settled)!;
        var staffed = planning.Showable(session.SetupInputs());
        Assert.NotNull(staffed);
        Assert.Equal(Text("Inn.Setup.Full"), KitchenLines(InnSetupView.Setup(session.SetupInputs(), staffed, Inn, Text), 1)[^1]);

        session.Update(Cleared(2));
        gate.Reset(); // the plan for the empty kitchens is being worked out
        var cleared = planning.Request(session.SetupInputs(), Settled);
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

    /// <summary>
    /// The quick advice shows first unless the simulator's comes within <see cref="InnSetupPlanning.RefinedWithin"/>;
    /// the simulator's replaces it only when a panel would show something else.
    /// </summary>
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
        var same = new InnSetupPlanning(Replacing, _ => quick, (_, _) =>
        {
            release.Wait();
            return quick with { };
        }, note: _ => { });
        var work = same.Request(session.SetupInputs(), Settled, () => Interlocked.Increment(ref doneCalls))!;
        await WaitUntil(() => same.Showable(session.SetupInputs()) is not null);
        Assert.Same(quick, same.Showable(session.SetupInputs())); // shown before the refined one is ready
        release.Set();
        await work;
        Assert.Same(quick, same.Showable(session.SetupInputs()));
        Assert.Equal(0, same.Replaced);
        Assert.Equal(1, doneCalls);

        // A different, clearly better refined advice coming after InnSetupPlanning.RefinedWithin, the quick one only
        // just shown (a clock that doesn't move), replaces it.
        doneCalls = 0;
        var late = new InnSetupPlanning(Replacing, _ => quick, (_, _) =>
        {
            Thread.Sleep(InnSetupPlanning.RefinedWithin + TimeSpan.FromMilliseconds(300));
            return other;
        }, note: _ => { }, clock: Frozen);
        await late.Request(session.SetupInputs(), Settled, () => Interlocked.Increment(ref doneCalls))!;
        Assert.Same(other, late.Showable(session.SetupInputs()));
        Assert.Equal(1, late.Replaced);
        Assert.Equal(2, doneCalls);
        Assert.Equal(2, late.Changes);

        // One ready within it is shown directly: the quick advice never shows, nothing swaps on screen.
        doneCalls = 0;
        var soon = new InnSetupPlanning(Replacing, _ => quick, (_, _) => other, note: _ => { });
        await soon.Request(session.SetupInputs(), Settled, () => Interlocked.Increment(ref doneCalls))!;
        Assert.Same(other, soon.Showable(session.SetupInputs()));
        Assert.Equal(1, doneCalls);
        Assert.Equal(1, soon.Changes);
    }

    /// <summary>
    /// The v0.6.5 release's CI failure, made to happen every time: the worker held up between working out how long to wait for
    /// the simulator and waiting (an overloaded machine: the runner, or a player's PC with the game running) found a refinement
    /// that took 1.3 s already done. It was ready after <see cref="InnSetupPlanning.RefinedWithin"/>, so the quick advice shows
    /// first and the refined one replaces it, as if the worker had not been held up: what's shown depends on when the
    /// refinement was ready, not on when the worker got to look. The hold-up: the worker's thread stopped for 1.5 s just as it
    /// begins its wait (<see cref="HoldUpTheWait"/>).
    /// </summary>
    [Fact]
    public async Task A_worker_held_up_before_it_waits_still_shows_the_quick_advice_before_a_late_refinement()
    {
        var session = NewSession();
        session.Update(Staffed(0));
        var quick = Quick(session);
        var other = InnSetupAdvice.Fast(Planner, NewSessionWith(Cleared(0)).SetupInputs().Input!);
        int worker = 0, doneCalls = 0;
        using var holdUp = new HoldUpTheWait(() => Volatile.Read(ref worker), TimeSpan.FromSeconds(1.5));
        var planning = new InnSetupPlanning(Replacing, _ =>
        {
            Volatile.Write(ref worker, Environment.CurrentManagedThreadId); // the quick advice is worked out on the worker
            return quick;
        }, (_, _) =>
        {
            Thread.Sleep(InnSetupPlanning.RefinedWithin + TimeSpan.FromMilliseconds(300));
            return other;
        }, note: _ => { }, clock: Frozen);

        await planning.Request(session.SetupInputs(), Settled, () => Interlocked.Increment(ref doneCalls))!;

        Assert.True(holdUp.HeldUp, "the worker's wait was never held up: this test proved nothing");
        Assert.Same(other, planning.Showable(session.SetupInputs()));
        Assert.Equal(2, doneCalls); // the quick advice, then the refined one: not the refined one shown directly
        Assert.Equal(2, planning.Changes);
        Assert.Equal(1, planning.Replaced);
    }

    /// <summary>
    /// Whether the refined advice came within <see cref="InnSetupPlanning.RefinedWithin"/> is a matter of the planning's clock,
    /// never of how fast the machine running the test is: a refinement ready at once on that clock (here a clock that doesn't
    /// move) is shown directly, the quick advice never, though the machine took 1.5 s to run it (the CI runner, overloaded,
    /// starting the simulator's work late). Otherwise every test of "ready within the second" holds only on a machine
    /// fast enough, and fails on one that isn't.
    /// </summary>
    [Fact]
    public async Task Ready_within_the_second_on_the_plannings_clock_is_shown_directly_however_slow_the_machine()
    {
        var session = NewSession();
        session.Update(Staffed(0));
        var quick = Quick(session);
        var other = InnSetupAdvice.Fast(Planner, NewSessionWith(Cleared(0)).SetupInputs().Input!);
        int doneCalls = 0;
        var planning = new InnSetupPlanning(Replacing, _ => quick, (_, _) =>
        {
            Thread.Sleep(TimeSpan.FromSeconds(1.5)); // the machine, busy; on the planning's clock no time passes
            return other;
        }, note: _ => { }, clock: Frozen);

        await planning.Request(session.SetupInputs(), Settled, () => Interlocked.Increment(ref doneCalls))!;

        Assert.Same(other, planning.Showable(session.SetupInputs()));
        Assert.Equal(1, doneCalls); // shown directly: the quick advice never went on screen
        Assert.Equal(1, planning.Changes);
    }

    /// <summary>
    /// Holds up one thread for a while as it begins to wait on a task (the runtime's task events, raised on the waiting
    /// thread just before it looks whether the task is done): what an overloaded machine does to the worker at that moment.
    /// Once only, and only on <paramref name="thread"/>; other tests' threads are left alone.
    /// </summary>
    private sealed class HoldUpTheWait(Func<int> thread, TimeSpan hold) : System.Diagnostics.Tracing.EventListener
    {
        private const int TaskWaitBegin = 10; // TplEventSource's event id
        private int _heldUp;

        public bool HeldUp => Volatile.Read(ref _heldUp) == 1;

        protected override void OnEventSourceCreated(System.Diagnostics.Tracing.EventSource source)
        {
            // Only the events that say a task is waited on (TaskTransfer); nothing that tracks activities.
            if (source.Name == "System.Threading.Tasks.TplEventSource")
                EnableEvents(source, System.Diagnostics.Tracing.EventLevel.Informational, (System.Diagnostics.Tracing.EventKeywords)1);
        }

        protected override void OnEventWritten(System.Diagnostics.Tracing.EventWrittenEventArgs e)
        {
            // Called while the constructor runs, before the primary constructor's parameters are captured: thread is null then.
            if (e.EventId != TaskWaitBegin || thread is null || Environment.CurrentManagedThreadId != thread()) return;
            if (Interlocked.Exchange(ref _heldUp, 1) == 0) Thread.Sleep(hold);
        }
    }

    /// <summary>
    /// The quick advice on screen longer than <see cref="InnSetupPlanning.RefinedWithin"/> (the player may be following it:
    /// owner, test_yuanyuan6, "the recommendation should not change, unless some kitchen is filled") stays when the
    /// refined one comes, however much better the judge finds it (<see cref="Replacing"/>: always).
    /// </summary>
    [Fact]
    public async Task A_refined_advice_coming_after_the_quick_one_has_been_seen_does_not_replace_it()
    {
        var session = NewSession();
        session.Update(Cleared(0));
        var quick = Quick(session);
        var other = InnSetupAdvice.Fast(Planner, NewSessionWith(Staffed(0)).SetupInputs().Input!);
        Assert.NotEqual(quick.Signature, other.Signature);
        var now = TimeSpan.Zero;
        var release = new ManualResetEventSlim(false);
        var planning = new InnSetupPlanning(Replacing, _ => quick, (_, _) =>
        {
            release.Wait();
            return other;
        }, note: _ => { }, clock: () => now);

        var work = planning.Request(session.SetupInputs(), Settled)!;
        await WaitUntil(() => planning.Showable(session.SetupInputs()) is not null);
        now += InnSetupPlanning.RefinedWithin + TimeSpan.FromSeconds(7); // the 8.4 s of the owner's log
        release.Set();
        await work;

        Assert.Same(quick, planning.Showable(session.SetupInputs()));
        Assert.Equal(0, planning.Replaced);
        Assert.Equal(1, planning.Changes);
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
        var planning = new InnSetupPlanning(Replacing, input =>
        {
            if (input.Kitchens.Any(k => k.Placed.Count > 0)) firstGate.Wait(); // the older inputs' quick advice is slow
            return input.Kitchens.Any(k => k.Placed.Count > 0) ? staffedAdvice : clearedAdvice;
        }, (input, cancel) =>
        {
            if (input.Kitchens.Any(k => k.Placed.Count > 0)) Interlocked.Increment(ref olderRefined);
            return input.Kitchens.Any(k => k.Placed.Count > 0) ? staffedAdvice : clearedAdvice;
        }, note: _ => { });

        // Older inputs, then newer ones while the older are still worked out; the older finish last.
        var older = planning.Request(staffed.SetupInputs(), Settled)!;
        var newer = planning.Request(cleared.SetupInputs(), Settled)!;
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
        var planning = new InnSetupPlanning(Replacing, input => InnSetupAdvice.Fast(Planner, input), (input, cancel) =>
        {
            seen.Enqueue(cancel);
            entered.Set();
            return InnSetupAdvice.For(Planner, input, cancel);
        }, log: logged.Add, note: _ => { });
        var first = planning.Request(NewSessionWith(Cleared(0)).SetupInputs(), Settled)!;
        entered.Wait(TimeSpan.FromSeconds(30));
        var watch = Stopwatch.StartNew();
        var second = planning.Request(NewSessionWith(Staffed(0)).SetupInputs(), Settled)!;
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
            await planning.Request(session.SetupInputs(), Settled)!;
            var watch = Stopwatch.StartNew();
            session.Update(Cleared(1));
            var work = planning.Request(session.SetupInputs(), Settled);
            await WaitUntil(() => planning.Showable(session.SetupInputs()) is not null, TimeSpan.FromSeconds(120));
            long ms = watch.ElapsedMilliseconds;
            await work!;
            return ms;
        }

        long before = await Measure(new InnSetupPlanning(Replacing, input => InnSetupAdvice.For(Planner, input, CancellationToken.None), note: _ => { }));
        long after = await Measure(new InnSetupPlanning(Replacing, input => InnSetupAdvice.Fast(Planner, input),
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
