using EndHud.Vision;
using EndHud.Vision.Inn;
using Xunit;

namespace EndHud.Tests;

/// <summary>
/// The inn in the reading loop (<see cref="PartyMonitor.TryInnAsync"/>, <see cref="InnFrameReader"/>):
/// a frame of the inn raises <see cref="PartyMonitor.InnUpdated"/> and the squad panel isn't read;
/// a fight never looks for the inn; out of both, the inn is looked for once a second.
/// </summary>
public class InnWireUpTests
{
    private static readonly Frame AnyFrame = new(1, 1, new byte[4]);

    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    /// <summary>Screens that say what the test tells them to, counting what was asked.</summary>
    private sealed class FakeScreens : IInnScreens
    {
        public InnScreenKind? Kind { get; set; }
        public DispatchPanel? Panel { get; set; }
        public IReadOnlyList<TableCard?> Tables { get; set; } = [.. Enumerable.Range(1, 6).Select(n => (TableCard?)new TableCard(n, TableState.Empty, [], []))];
        public int Detections, BubbleReads, PanelReads, SetupReads;

        public Task<InnScreenKind?> DetectAsync(Frame frame)
        {
            Detections++;
            return Task.FromResult(Kind);
        }

        public SessionClock Clock { get; set; } = new(S(240), false);

        public SessionClock? ReadClock(Frame frame) =>
            Kind is InnScreenKind.Session or InnScreenKind.DispatchPanel ? Clock : null;

        public Task<IReadOnlyList<TableCard?>> ReadTablesAsync(Frame frame) => Task.FromResult(Tables);

        public Task<FireMeter?> ReadFireAsync(Frame frame) => Task.FromResult<FireMeter?>(new FireMeter(30, 4, false));

        public Task<DispatchPanel?> ReadPanelAsync(Frame frame)
        {
            PanelReads++;
            return Task.FromResult(Kind == InnScreenKind.DispatchPanel ? Panel : null);
        }

        public Task<GourmandBubble?> ReadBubbleAsync(Frame frame)
        {
            BubbleReads++;
            return Task.FromResult<GourmandBubble?>(new GourmandBubble("x", ["x"]));
        }

        public Task<InnSetup?> ReadSetupAsync(Frame frame)
        {
            SetupReads++;
            return Task.FromResult<InnSetup?>(null);
        }

        public Task<InnMenu?> ReadMenuAsync(Frame frame) => Task.FromResult<InnMenu?>(null);

        public Task<MenuRequirements?> ReadMenuRequirementsAsync(Frame frame) => Task.FromResult<MenuRequirements?>(null);
    }

    private static (PartyMonitor Monitor, FakeScreens Screens, List<InnReading?> Raised) Monitor()
    {
        var screens = new FakeScreens();
        var monitor = new PartyMonitor(TimeSpan.FromMilliseconds(250)) { InnFrames = new InnFrameReader(screens) };
        var raised = new List<InnReading?>();
        monitor.InnUpdated += raised.Add;
        return (monitor, screens, raised);
    }

    [Fact]
    public async Task A_session_frame_raises_an_inn_reading_and_the_squad_panel_is_not_read()
    {
        var (monitor, screens, raised) = Monitor();
        screens.Kind = InnScreenKind.Session;

        bool inn = await monitor.TryInnAsync(AnyFrame, S(0), squadPanelSeen: false);

        Assert.True(inn);
        var reading = Assert.Single(raised);
        Assert.Equal(InnScreenKind.Session, reading!.Screen);
        Assert.Equal(S(240), reading.Clock!.Left);
        Assert.Equal(6, reading.TableCards.Count);
        Assert.Equal(30, reading.Fire!.Fire);
    }

    [Fact]
    public async Task In_a_fight_the_inn_is_never_looked_for()
    {
        var (monitor, screens, raised) = Monitor();
        screens.Kind = null;

        for (int i = 0; i < 20; i++)
            Assert.False(await monitor.TryInnAsync(AnyFrame, S(i * 0.25), squadPanelSeen: true));

        Assert.Equal(0, screens.Detections);
        Assert.Empty(raised);
    }

    [Fact]
    public async Task Out_of_a_fight_and_the_inn_it_is_looked_for_once_a_second()
    {
        var (monitor, screens, raised) = Monitor();
        screens.Kind = null;

        foreach (double t in new[] { 0, 0.25, 0.5, 0.75, 1.0, 1.25, 2.0 })
            Assert.False(await monitor.TryInnAsync(AnyFrame, S(t), squadPanelSeen: false));

        Assert.Equal(3, screens.Detections); // at 0, 1 and 2 s
        Assert.Empty(raised);
    }

    [Fact]
    public async Task Leaving_the_inn_is_said_once_so_its_advice_can_be_put_away()
    {
        var (monitor, screens, raised) = Monitor();
        screens.Kind = InnScreenKind.Setup;
        await monitor.TryInnAsync(AnyFrame, S(0), false);
        screens.Kind = null;

        Assert.False(await monitor.TryInnAsync(AnyFrame, S(0.25), false)); // held: maybe a missed frame
        Assert.False(await monitor.TryInnAsync(AnyFrame, S(0.5), true)); // the squad panel: left

        Assert.Null(raised[^1]);
        Assert.Single(raised, r => r is null);
    }

    /// <summary>
    /// Owner, live 2026-10-20: "the capture is too slow" after an alt-tab. Back in the game on an
    /// inn screen, a first frame that misses it (the game redrawing as it gets the focus) holds the
    /// screen as any missed frame does: time with the reading off doesn't run the hold out, so the
    /// inn is looked for on the next frame, not a second later, and its advice isn't put away.
    /// </summary>
    [Fact]
    public async Task Back_from_an_alt_tab_the_inn_screen_is_held_and_found_on_the_next_frame()
    {
        var (monitor, screens, raised) = Monitor();
        screens.Kind = InnScreenKind.Menu;
        monitor.FrameRead(S(0));
        Assert.True(await monitor.TryInnAsync(AnyFrame, S(0), false));
        monitor.Reading.Stop(S(0.5)); // another window in front: the reading off for 27 s

        screens.Kind = null;
        monitor.FrameRead(S(27.5));
        Assert.False(await monitor.TryInnAsync(AnyFrame, S(27.5), false));
        screens.Kind = InnScreenKind.Menu;
        monitor.FrameRead(S(27.75));

        Assert.True(await monitor.TryInnAsync(AnyFrame, S(27.75), false));
        Assert.DoesNotContain(null, raised);
        Assert.False(raised[1]!.Fresh);
        Assert.True(raised[^1]!.Fresh);
    }

    /// <summary>
    /// Owner, live 2026-10-19: "the page refreshes too slowly". The log showed "inn: left" then the
    /// setup screens found again ~1.2 s later: one frame whose title wasn't read counted as leaving, the
    /// advice was put away, and the inn was looked for only once a second after. Every inn screen is
    /// held through missed frames like the session (<see cref="PartyMonitor.SessionHold"/>), looked for
    /// on every frame meanwhile, and its reading cadence carries on.
    /// </summary>
    [Theory]
    [InlineData(InnScreenKind.Setup)]
    [InlineData(InnScreenKind.ChefPicker)]
    [InlineData(InnScreenKind.Menu)]
    [InlineData(InnScreenKind.ThemeList)]
    public async Task A_frame_missed_on_a_setup_screen_keeps_the_inn_and_its_advice(InnScreenKind screen)
    {
        var (monitor, screens, raised) = Monitor();
        screens.Kind = screen;
        Assert.True(await monitor.TryInnAsync(AnyFrame, S(0), false));
        int reads = screens.SetupReads;

        screens.Kind = null;
        Assert.False(await monitor.TryInnAsync(AnyFrame, S(0.1), false));
        screens.Kind = screen;
        Assert.True(await monitor.TryInnAsync(AnyFrame, S(0.2), false)); // found again on the next frame, not a second later

        Assert.DoesNotContain(null, raised);
        Assert.Equal([screen, screen, screen], raised.Select(r => r!.Screen));
        Assert.False(raised[1]!.Fresh); // the held frame carries nothing new
        Assert.Equal(reads, screens.SetupReads); // the same screen: not read again before its time
    }

    [Fact]
    public async Task A_setup_screen_missed_for_eight_seconds_is_left()
    {
        var (monitor, screens, raised) = Monitor();
        screens.Kind = InnScreenKind.Menu;
        await monitor.TryInnAsync(AnyFrame, S(0), false);
        screens.Kind = null;

        for (double t = 1; t < 9; t += 1) await monitor.TryInnAsync(AnyFrame, S(t), false);
        Assert.DoesNotContain(null, raised);
        await monitor.TryInnAsync(AnyFrame, S(9), false);

        Assert.Null(raised[^1]);
    }

    /// <summary>
    /// Owner, 2026-10-19: "when session starts, the hint panel flashes all the time". The timer can't
    /// be read on a frame here and there (the dispatch panel opening, the HUD fading, the camera
    /// moving); such a frame is not leaving the inn: the session is kept, the strip stays.
    /// </summary>
    [Fact]
    public async Task A_frame_missed_in_a_session_keeps_the_session_and_the_strip()
    {
        var (monitor, screens, raised) = Monitor();
        screens.Kind = InnScreenKind.Session;
        await monitor.TryInnAsync(AnyFrame, S(0), false);

        // Missed frames for 3 s, then the session again.
        screens.Kind = null;
        foreach (double t in new[] { 0.25, 0.5, 1.25, 2.25, 3.0 }) await monitor.TryInnAsync(AnyFrame, S(t), false);
        screens.Kind = InnScreenKind.DispatchPanel;
        screens.Panel = new DispatchPanel(1, 3, [null, null], [null, null], null, [], []);
        await monitor.TryInnAsync(AnyFrame, S(3.25), false);

        Assert.DoesNotContain(null, raised);
        Assert.All(raised, r => Assert.True(r!.InSession));
        // A held frame carries nothing new: no tables, no panel, no clock.
        var held = raised.Where(r => !r!.Fresh).ToList();
        Assert.Equal(5, held.Count);
        Assert.All(held, r => Assert.True(r!.TableCards.Count == 0 && r.Panel is null && r.Clock is null));
        Assert.Equal(InnScreenKind.DispatchPanel, raised[^1]!.Screen);
    }

    [Fact]
    public async Task No_inn_screen_for_eight_seconds_leaves_the_session()
    {
        var (monitor, screens, raised) = Monitor();
        screens.Kind = InnScreenKind.Session;
        await monitor.TryInnAsync(AnyFrame, S(0), false);
        screens.Kind = null;

        for (double t = 1; t < 9; t += 1) await monitor.TryInnAsync(AnyFrame, S(t), false);
        Assert.DoesNotContain(null, raised);
        await monitor.TryInnAsync(AnyFrame, S(9), false);

        Assert.Null(raised[^1]);
        Assert.Equal(1, raised.Count(r => r is null));
    }

    [Fact]
    public async Task The_squad_panel_seen_while_a_session_is_held_leaves_at_once()
    {
        var (monitor, screens, raised) = Monitor();
        screens.Kind = InnScreenKind.Session;
        await monitor.TryInnAsync(AnyFrame, S(0), false);
        screens.Kind = null;

        // The held frame isn't the inn as far as the reading loop goes: the squad panel is read.
        Assert.False(await monitor.TryInnAsync(AnyFrame, S(0.25), false));
        Assert.False(await monitor.TryInnAsync(AnyFrame, S(0.5), squadPanelSeen: true));

        Assert.NotNull(raised[1]);
        Assert.Null(raised[^1]);
    }

    [Fact]
    public async Task After_closing_and_the_last_serving_time_a_missed_frame_is_the_end()
    {
        var (monitor, screens, raised) = Monitor();
        screens.Kind = InnScreenKind.Session;
        screens.Clock = new SessionClock(TimeSpan.Zero, true);
        await monitor.TryInnAsync(AnyFrame, S(0), false);
        await monitor.TryInnAsync(AnyFrame, S(InnState.ServingAfterCloseSeconds + 1), false);
        screens.Kind = null; // the results screen

        await monitor.TryInnAsync(AnyFrame, S(InnState.ServingAfterCloseSeconds + 1.25), false);

        Assert.Null(raised[^1]);
    }

    [Fact]
    public async Task With_the_helper_off_the_inn_is_not_read()
    {
        var (monitor, screens, raised) = Monitor();
        screens.Kind = InnScreenKind.Session;
        monitor.ReadInn = false;

        Assert.False(await monitor.TryInnAsync(AnyFrame, S(0), false));

        Assert.Equal(0, screens.Detections);
        Assert.Empty(raised);
    }

    [Fact]
    public async Task In_the_session_the_screen_is_told_by_the_timer_and_the_panels_tabs_not_by_reading_titles()
    {
        var (monitor, screens, raised) = Monitor();
        screens.Kind = InnScreenKind.Session;
        await monitor.TryInnAsync(AnyFrame, S(0), false);
        screens.Kind = InnScreenKind.DispatchPanel;
        screens.Panel = new DispatchPanel(2, 3, [null, null], [null, null], null, [], []);

        for (int i = 1; i <= 4; i++) await monitor.TryInnAsync(AnyFrame, S(i * 0.25), false);

        Assert.Equal(1, screens.Detections);
        Assert.All(raised.Skip(1), r => Assert.Equal(InnScreenKind.DispatchPanel, r!.Screen));
        Assert.Equal(2, raised[^1]!.Panel!.Kitchen);
        Assert.Null(raised[^1]!.Fire); // hidden behind the panel: not read
    }

    [Fact]
    public async Task The_gourmands_bubble_is_read_only_while_a_gourmand_is_at_a_table()
    {
        var (monitor, screens, raised) = Monitor();
        screens.Kind = InnScreenKind.Session;
        await monitor.TryInnAsync(AnyFrame, S(0), false);
        Assert.Equal(0, screens.BubbleReads);
        Assert.Null(raised[^1]!.Bubble);

        screens.Tables = [.. screens.Tables.Select((t, i) => i == 2 ? new TableCard(3, TableState.GourmandOrdering, [], []) : t)];
        await monitor.TryInnAsync(AnyFrame, S(0.25), false);

        Assert.Equal(1, screens.BubbleReads);
        Assert.NotNull(raised[^1]!.Bubble);
    }

    [Fact]
    public async Task The_setup_screen_is_read_again_every_half_second()
    {
        var (monitor, screens, _) = Monitor();
        screens.Kind = InnScreenKind.Setup;

        foreach (double t in new[] { 0, 0.25, 0.5, 0.75, 0.9, 1.0 }) Assert.True(await monitor.TryInnAsync(AnyFrame, S(t), false));

        Assert.Equal(3, screens.SetupReads); // at 0, 0.5 and 1 s
    }

    /// <summary>
    /// The chef picker is told apart (it keeps the setup remembered) and nothing on it is read: it shows no advice since the
    /// owner's 2026-10-21 test ("the recommendation in the choose cook or dish menu ... just delete that for now").
    /// </summary>
    [Fact]
    public async Task Nothing_on_the_chef_picker_is_read()
    {
        var (monitor, screens, raised) = Monitor();
        screens.Kind = InnScreenKind.Setup;
        Assert.True(await monitor.TryInnAsync(AnyFrame, S(0), false));
        screens.Kind = InnScreenKind.ChefPicker;

        foreach (double t in new[] { 0.25, 0.5, 2.25 }) Assert.True(await monitor.TryInnAsync(AnyFrame, S(t), false));

        Assert.Equal(1, screens.SetupReads);
        Assert.All(raised, r => Assert.True(r!.OnSetupScreens));
        Assert.Equal(InnScreenKind.ChefPicker, raised[^1]!.Screen);
    }

    /// <summary>The theme list is told apart and nothing on it is read: it shows no advice.</summary>
    [Fact]
    public async Task Nothing_on_the_theme_list_is_read()
    {
        var (monitor, screens, raised) = Monitor();
        screens.Kind = InnScreenKind.ThemeList;

        foreach (double t in new[] { 0, 2.5 }) Assert.True(await monitor.TryInnAsync(AnyFrame, S(t), false));

        Assert.Equal(0, screens.SetupReads);
        Assert.All(raised, r => Assert.False(r!.OnSetupScreens));
    }

    [Fact]
    public async Task A_reader_that_throws_does_not_stop_the_reading_the_squad_panel_is_read_instead()
    {
        var monitor = new PartyMonitor(TimeSpan.FromMilliseconds(250)) { InnFrames = new InnFrameReader(new ThrowingScreens()) };

        Assert.False(await monitor.TryInnAsync(AnyFrame, S(0), false));
    }

    private sealed class ThrowingScreens : IInnScreens
    {
        public Task<InnScreenKind?> DetectAsync(Frame frame) => throw new InvalidOperationException("broken reader");
        public SessionClock? ReadClock(Frame frame) => null;
        public Task<IReadOnlyList<TableCard?>> ReadTablesAsync(Frame frame) => throw new NotSupportedException();
        public Task<FireMeter?> ReadFireAsync(Frame frame) => throw new NotSupportedException();
        public Task<DispatchPanel?> ReadPanelAsync(Frame frame) => throw new NotSupportedException();
        public Task<GourmandBubble?> ReadBubbleAsync(Frame frame) => throw new NotSupportedException();
        public Task<InnSetup?> ReadSetupAsync(Frame frame) => throw new NotSupportedException();
        public Task<InnMenu?> ReadMenuAsync(Frame frame) => throw new NotSupportedException();
        public Task<MenuRequirements?> ReadMenuRequirementsAsync(Frame frame) => throw new NotSupportedException();
    }
}
