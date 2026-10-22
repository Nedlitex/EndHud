using System.IO;
using EndHud.Vision;
using EndHud.Vision.Inn;
using Xunit;
using Xunit.Abstractions;
using static EndHud.Tests.InnTestKit;

namespace EndHud.Tests;

/// <summary>
/// A test that runs only on a developer's machine with a recording of the inn named in ENDHUD_INN_RECORDING (and
/// ENDHUD_INN_TRACE_OUT, a folder for the text it writes); skipped everywhere else.
/// </summary>
public sealed class InnRecordingFactAttribute : FactAttribute
{
    public InnRecordingFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("ENDHUD_INN_RECORDING") is not { Length: > 0 } path || !File.Exists(path)
            || Environment.GetEnvironmentVariable("ENDHUD_INN_TRACE_OUT") is not { Length: > 0 })
            Skip = "set ENDHUD_INN_RECORDING to an inn recording and ENDHUD_INN_TRACE_OUT to a folder for the trace";
    }
}

/// <summary>
/// The session's overlays held steady (owner, live 2026-10-20: "it is still flashing, the display of both banner and
/// the circle should just stay, not flash all the time"), measured by replaying readings through the real session and
/// views (<see cref="InnReplay"/>).
/// </summary>
public class InnReplayTests(ITestOutputHelper output)
{
    /// <summary>The live reading loop's pace on a 4K game (the owner's log: a frame about every 0.3 s in the inn).</summary>
    private const double FrameEvery = 0.3;

    /// <summary>
    /// A recording through the real readers and the reading loop's holds (PartyMonitor.TryInnAsync), one frame every
    /// <see cref="FrameEvery"/> s up to ENDHUD_INN_RECORDING_TO seconds (default: all but the last 10 s): the readings
    /// saved as text (readings.jsonl.gz) and the replay's changes (views.txt) in ENDHUD_INN_TRACE_OUT. No frame is kept.
    /// </summary>
    [InnRecordingFact]
    public async Task Record_the_readings_of_a_recording_and_trace_the_overlays()
    {
        string path = Environment.GetEnvironmentVariable("ENDHUD_INN_RECORDING")!;
        string outFolder = Environment.GetEnvironmentVariable("ENDHUD_INN_TRACE_OUT")!;
        Directory.CreateDirectory(outFolder);
        var text = InnText.TryCreate() ?? throw new InvalidOperationException("needs the Simplified Chinese OCR language");
        var screens = new InnScreens(text, await InnPictures.LoadAsync(Folder, Inn), Inn, Library);
        var monitor = new PartyMonitor(TimeSpan.FromMilliseconds(250)) { InnFrames = new InnFrameReader(screens) };
        var events = new List<InnReplayEvent>();
        TimeSpan now = TimeSpan.Zero;
        monitor.InnUpdated += r => events.Add(new InnReplayEvent(now, r));

        var video = await VideoFile.OpenAsync(path);
        double to = Environment.GetEnvironmentVariable("ENDHUD_INN_RECORDING_TO") is { Length: > 0 } t ? double.Parse(t, System.Globalization.CultureInfo.InvariantCulture)
            : video.Duration.TotalSeconds - 10;
        for (int i = 0; i * FrameEvery < to; i++)
        {
            now = TimeSpan.FromSeconds(Math.Round(i * FrameEvery, 3));
            var frame = await video.GetFrameAsync(now);
            await monitor.TryInnAsync(frame, now, squadPanelSeen: false);
        }
        InnReplay.Save(Path.Combine(outFolder, "readings.jsonl.gz"), events);
        var replay = InnReplay.Run(InnReplay.Load(Path.Combine(outFolder, "readings.jsonl.gz")));
        File.WriteAllText(Path.Combine(outFolder, "views.txt"), replay.Report());
        foreach (var (display, rate) in replay.PerMinute()) output.WriteLine($"{display}: {rate:F1}/min");
    }

    /// <summary>
    /// The owner's recorded sessions, their readings taken through the real readers and the reading loop's holds, a frame
    /// every 0.3 s (Fixtures/inn-replay, text only), replayed through the session and the views:
    /// <list type="bullet">
    /// <item>test_yuanyuan3 (2026-10-20, 0–430 s). Before the displays were held steady: 69 banners went and came back
    /// within a second and 37 were up for under half a second (all banners: 60.8 changes a minute); 26 marks went and came
    /// back within a second.</item>
    /// <item>test_yuanyuan4 (2026-10-21, 0–470 s; owner: "the recommended dish flashes heavily and it jumps around ... the
    /// recommendation should stay steady").</item>
    /// <item>test_yuanyuan5 (2026-10-21, 0–415 s, the last seconds of the recording left out; the first fixture with the tray
    /// of held dishes, Full Blaze's countdown and where the timer was read). Before that day's fixes (the code of ebf2b77 over
    /// these readings): 58 changes (10.7 a minute), 0 flashes, 1 jump (367.8 s, an order moved off a kitchen it could no
    /// longer come out of in time), 14 new orders of which 1 was never shown; swaps calling out dishes queued as called at
    /// 128.7 s (界龙肉卷, 白瓜肉丸汤) and 335.7 s (福寿全).</item>
    /// <item>test_yuanyuan6 (2026-10-21, 0–515 s).</item>
    /// <item>test_yuanyuan7 (2026-10-21, 0–390 s; owner: the recommended dish "duplicated in multiple kitchens", and "stuck
    /// behind a lot of soups but the kitchen recommendation is not even picking that up"). Before that day's fixes: 9
    /// duplicates (82 s), table 6's immortal-galore-of-fortune with no call for 133.8 s, a counter estimate of up to 20 on a counter of 8.</item>
    /// </list>
    /// Before 2026-10-21's fixes, the kitchens' calls: y3 88 changes (16.2 a minute), 3 flashes, 1 jump; y4 46 changes
    /// (8.6 a minute), 1 flash, 3 jumps (<see cref="InnReplay.Steadiness"/>); and every banner slid out and in again each
    /// time the dispatch panel opened and closed.
    /// </summary>
    /// <summary>
    /// Owner, live 2026-10-21 (test_yuanyuan8): the swap circles ("先拿掉X，再排Y") are "really too fast for user to follow, I
    /// think this can be a check box feature, default on". Over the five recorded sessions through the real session and views:
    /// with swaps on (the setting's default) swaps are called; with them off no circle, card mark or call ever says to take a
    /// dish out, and the orders are still called (a plain order where a kitchen has room).
    /// </summary>
    [Fact]
    public void With_swaps_off_nothing_ever_says_to_take_a_dish_out()
    {
        string[] fixtures = ["test-yuanyuan3-readings.jsonl.gz", "test-yuanyuan4-readings.jsonl.gz", "test-yuanyuan5-readings.jsonl.gz",
            "test-yuanyuan6-readings.jsonl.gz", "test-yuanyuan7-readings.jsonl.gz"];
        Assert.True(new HudConfig().InnSwapsEnabled, "swaps are on by default");
        int swapsOn = 0;
        foreach (string fixture in fixtures)
        {
            var events = InnReplay.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "inn-replay", fixture)).ToList();
            var on = InnReplay.Run(events, suggestSwaps: true);
            var off = InnReplay.Run(events, suggestSwaps: false);
            int Swaps(InnReplay r) => r.Draws.Sum(d => d.Calls.Values.Count(c => c.Kind == InnCallKind.Swap)) + r.Steps.Sum(s => s.Kitchens.Values.Count(c => c.Kind == InnCallKind.Swap));
            int Orders(InnReplay r) => r.Steps.Sum(s => s.Kitchens.Values.Count(c => c.Kind == InnCallKind.Queue));
            int swapMarks = off.Changes.Count(c => c.Display == "mark" && c.What.Contains("先拿掉", StringComparison.Ordinal));
            output.WriteLine($"{fixture}: swaps on {Swaps(on)}, off {Swaps(off)} (marks {swapMarks}); order calls on {Orders(on)}, off {Orders(off)}");
            swapsOn += Swaps(on);
            Assert.Equal(0, Swaps(off));
            Assert.Equal(0, swapMarks);
            Assert.True(Orders(off) > 0, $"{fixture}: no order called with swaps off");
        }
        Assert.True(swapsOn > 0, "no swap called on any recorded session with swaps on");
    }

    [Theory]
    [InlineData("test-yuanyuan3-readings.jsonl.gz")]
    [InlineData("test-yuanyuan4-readings.jsonl.gz")]
    [InlineData("test-yuanyuan5-readings.jsonl.gz")]
    [InlineData("test-yuanyuan6-readings.jsonl.gz")]
    [InlineData("test-yuanyuan7-readings.jsonl.gz")]
    public void Over_the_owners_recorded_sessions_nothing_flashes_or_jumps(string fixture)
    {
        var replay = InnReplay.Run(InnReplay.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "inn-replay", fixture)));
        foreach (var (display, rate) in replay.PerMinute()) output.WriteLine($"{display}: {rate:F1} changes/min");
        if (Environment.GetEnvironmentVariable("ENDHUD_REPLAY_DUMP") is { Length: > 0 } dump) File.WriteAllText(dump.Replace("{fixture}", fixture, StringComparison.Ordinal), replay.Report());

        var banners = Flashes(replay, "banner");
        var marks = Flashes(replay, "mark");
        var gourmands = Flashes(replay, "gourmand");
        var (flashes, jumps) = replay.Steadiness();
        var orders = replay.NewOrders();
        output.WriteLine($"banners: {banners.HiddenUnderASecond} hidden under a second, {banners.UpUnderHalfASecond} up under half a second");
        output.WriteLine($"marks: {marks.HiddenUnderASecond} hidden under a second, {marks.UpUnderHalfASecond} up under half a second");
        output.WriteLine($"calls: {replay.CallRates().Changes:F1} changes a minute, {flashes.Count} flashes, {jumps.Count} jumps");
        output.WriteLine($"new orders a kitchen can take: {orders.Count}, shown at once {orders.Count(o => o.ShownAfter == 0)}");
        foreach (var u in flashes) output.WriteLine($"flash {u.At.TotalSeconds:F1} s kitchen {u.Kitchen}: {u.What} <= {u.Why}");
        foreach (var u in jumps) output.WriteLine($"jump {u.At.TotalSeconds:F1} s kitchen {u.Kitchen}: {u.What} <= {u.Why}");
        output.WriteLine(replay.Consistency());
        var (tabNoCircle, callNoCard, switchNoCard, gourmandHidden, aheadHidden) = replay.Agreement();
        // One picture (owner, 2026-10-21, test_yuanyuan6): a tab marked 有菜要排 always has its kitchen's circle on screen
        // (below the open panel), and a kitchen with a call shown on the panel has its card marked. Before: y3 14 (111.3 s),
        // y4 9 (68.4 s), y5 17 (79.8 s) tabs marked with the circles hidden under the panel.
        Assert.Equal(0, tabNoCircle.Count);
        Assert.Equal(0, callNoCard.Count);
        Assert.Empty(switchNoCard);
        Assert.Equal(0, aheadHidden.Count);
        // A gourmand's tile never hidden while their order is open (owner, 2026-10-21: "gourmand's requirement window still
        // flashes always"). Before, hidden whenever the panel was open: y3 4 times (42.8 s), y4 4 (31.3 s), y5 7 (39.9 s),
        // y6 7 (38.2 s; 367.5 s is the owner's 6:10). And none taken down while its table still waits.
        Assert.Equal(0, gourmandHidden.Count);
        Assert.Equal(0, replay.GourmandLives().DownUnserved);
        // A dish to cook ahead stays until done or impossible, or an order takes its kitchen (owner, 2026-10-21: "the blue
        // circle just shows very indeterministically"). Before: y4 206.1 s, kitchen 1's dish dropped as "no longer advised"
        // while the order that displaced it in the advice was shown as kitchen 2's swap (the advice didn't keep a swap shown).
        var (_, aheadFlickers, flickerList) = replay.CookAheadFlicker();
        Assert.True(aheadFlickers == 0, string.Join("; ", flickerList));

        Assert.True(replay.SessionSpan > TimeSpan.FromMinutes(5), "the whole session was replayed");
        // One dish in one circle unless two tables each need it (owner, 2026-10-21, test_yuanyuan7 00:46: "the recommended queue
        // will be duplicated in multiple kitchens"). Before: y3 6 (90.9 s), y4 9 (169.5 s), y5 4 (25.9 s), y6 0, y7 9 (82.2 s).
        var (duplicates, duplicateList) = replay.DuplicateCircles();
        Assert.True(duplicates.Count == 0, string.Join("; ", duplicateList));
        // No order left uncalled with its dish taken to be somewhere it isn't (owner, test_yuanyuan7: "stuck behind a lot of soups
        // but the kitchen recommendation is not even picking that up"): a dish tables wait for, not in hand nor queued, a kitchen
        // able to have it out in time, and no circle naming it never waits longer than a dish out for an order is trusted
        // (InnSession.CoverTrustedFor), unless the player then picks it up or serves it (it was on the counter). Before:
        // y7 immortal-galore-of-fortune 143.1 s with no call (227.7 s), white-fin soups 21.6 s (318.0 s).
        var uncalled = replay.UncalledWaits(InnReplay.UncalledAfterSeconds).Where(u => u.Counts).ToList();
        Assert.True(uncalled.All(u => u.Seconds <= InnSession.CoverTrustedFor.TotalSeconds),
            string.Join("; ", uncalled.Select(u => $"{u.Dish} from {u.From.TotalSeconds:F1} s for {u.Seconds:F1} s ({u.Ended})")));
        // The kitchens' calls: none gone and back, none switched with nothing done to it.
        Assert.Empty(flashes);
        Assert.Empty(jumps);
        // A new order is called at once wherever a kitchen isn't showing another order.
        Assert.NotEmpty(orders);
        Assert.All(orders.Where(o => o.KitchenFree), o => Assert.Equal(0, o.ShownAfter));
        // The banners as drawn: none back within a second, a kitchen's banner never moving in the stack.
        Assert.Equal(0, banners.HiddenUnderASecond);
        Assert.True(banners.UpUnderHalfASecond <= 5, $"{banners.UpUnderHalfASecond} banners up under half a second");
        // Since 2026-10-21 (no counter gate) about three times as many orders are called (a dish cooked ahead covers none)
        // and the kitchens nothing was ordered for always cook ahead: y3 11.4 -> 22.9 changes a minute, y4 6.4 -> 15.4, every
        // banner up once and down once (none back within a second, above). This bound only catches churn on that scale.
        Assert.True(replay.PerMinute()["banner"] <= 25, $"{replay.PerMinute()["banner"]:F1} banner changes a minute");
        Assert.DoesNotContain(replay.Changes, c => c.Display == "move" && !c.What.StartsWith("LastCall", StringComparison.Ordinal) && !c.What.StartsWith("Blaze", StringComparison.Ordinal));
        // The marks go the moment a frame shows the panel closed for sure (owner, live 2026-10-21: a mark left over the dining
        // room); on test_yuanyuan3 that is up to 14 marks for under a second, each a frame reading the Full Blaze meter the
        // open panel covers (closing and opening it again, or its slide) between frames reading it open, or a tab switch.
        // 11 before 2026-10-21: the same moments, with more kitchens showing a call (so more marks) since orders and cooking
        // ahead no longer wait on the counter.
        Assert.True(marks.HiddenUnderASecond <= 14, $"{marks.HiddenUnderASecond} marks hidden under a second");
        Assert.Equal((0, 0), (gourmands.HiddenUnderASecond, gourmands.UpUnderHalfASecond));
    }

    /// <summary>
    /// test_yuanyuan6 (2026-10-21, theme 一汤一饭: 主食 and 汤羹 hot; kitchen 1 = 秋栗 + 昼雪, 主食 +40 %): the owner at 201 s,
    /// "the first kitchen has all the bonus for 主食, but the precook suggest a 甜点". Over the replay, before: kitchen 1 was
    /// told to cook 金丝小贝 (a dessert) ahead from 197.7 s to 221.7 s and again from 210.9 s; now no dish outside the theme is
    /// called to cook ahead in any kitchen while one matching it is in stock.
    /// </summary>
    [Fact]
    public void Over_test_yuanyuan6_cooking_ahead_keeps_to_the_theme()
    {
        var replay = InnReplay.Run(InnReplay.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "inn-replay", "test-yuanyuan6-readings.jsonl.gz")));
        var ahead = replay.Steps.SelectMany(s => s.Kitchens.Values).Where(c => c.Kind == InnCallKind.CookAhead).Select(c => $"{c.Kitchen}:{c.Dish!.Zh}").Distinct().ToList();
        output.WriteLine(string.Join(", ", ahead));
        Assert.NotEmpty(ahead);
        Assert.All(replay.Steps.SelectMany(s => s.Kitchens.Values).Where(c => c.Kind == InnCallKind.CookAhead),
            c => Assert.Contains(c.Dish!.Type, new[] { DishType.Entree, DishType.Soup }));
        Assert.DoesNotContain(replay.Steps, s => s.Kitchens.TryGetValue(1, out var call) && call is { Kind: InnCallKind.CookAhead, Dish.Zh: "金丝小贝" });
    }

    /// <summary>How often a display's item went and came back within a second, and came and went within half a second.</summary>
    private static (int HiddenUnderASecond, int UpUnderHalfASecond) Flashes(InnReplay replay, string display)
    {
        int hidden = 0, up = 0;
        var last = new Dictionary<string, InnViewChange>();
        foreach (var change in replay.Changes.Where(c => c.Display == display))
        {
            if (last.TryGetValue(change.What, out var before))
            {
                double seconds = (change.At - before.At).TotalSeconds;
                if (before.Change == "-" && change.Change == "+" && seconds < 1) hidden++;
                if (before.Change == "+" && change.Change == "-" && seconds < 0.5) up++;
            }
            last[change.What] = change;
        }
        return (hidden, up);
    }

    /// <summary>A trace of saved readings again (ENDHUD_INN_TRACE_OUT/readings.jsonl.gz), without the recording: views.txt.</summary>
    [InnRecordingFact]
    public void Trace_the_overlays_over_saved_readings()
    {
        string outFolder = Environment.GetEnvironmentVariable("ENDHUD_INN_TRACE_OUT")!;
        string saved = Path.Combine(outFolder, "readings.jsonl.gz");
        if (!File.Exists(saved)) return;
        var replay = InnReplay.Run(InnReplay.Load(saved));
        File.WriteAllText(Path.Combine(outFolder, "views.txt"), replay.Report());
        foreach (var (display, rate) in replay.PerMinute()) output.WriteLine($"{display}: {rate:F1}/min");
    }
}
