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
        public int Detections, BubbleReads, PanelReads, SetupReads, PickerReads;

        public Task<InnScreenKind?> DetectAsync(Frame frame)
        {
            Detections++;
            return Task.FromResult(Kind);
        }

        public SessionClock? ReadClock(Frame frame) =>
            Kind is InnScreenKind.Session or InnScreenKind.DispatchPanel ? new SessionClock(S(240), false) : null;

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

        public int? PickerKitchen { get; set; }

        public Task<int?> ReadPickerKitchenAsync(Frame frame)
        {
            PickerReads++;
            return Task.FromResult(PickerKitchen);
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
        screens.Kind = InnScreenKind.Session;
        await monitor.TryInnAsync(AnyFrame, S(0), false);
        screens.Kind = null;

        Assert.False(await monitor.TryInnAsync(AnyFrame, S(0.25), false));
        Assert.False(await monitor.TryInnAsync(AnyFrame, S(0.5), true));

        Assert.Equal(2, raised.Count);
        Assert.Null(raised[1]);
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
    public async Task The_setup_screen_is_read_at_most_every_two_seconds()
    {
        var (monitor, screens, _) = Monitor();
        screens.Kind = InnScreenKind.Setup;

        foreach (double t in new[] { 0, 0.25, 0.5, 1.9, 2.0, 2.5 }) Assert.True(await monitor.TryInnAsync(AnyFrame, S(t), false));

        Assert.Equal(2, screens.SetupReads);
    }

    /// <summary>
    /// Of the chef picker only its title is read, for its kitchen (the advice there is that kitchen's;
    /// owner, 2026-10-19: never require reading the picker's grid), at once when it opens, then every two seconds.
    /// </summary>
    [Fact]
    public async Task Of_the_chef_picker_only_its_kitchen_is_read_at_once_when_it_opens()
    {
        var (monitor, screens, raised) = Monitor();
        screens.Kind = InnScreenKind.Setup;
        Assert.True(await monitor.TryInnAsync(AnyFrame, S(0), false));
        screens.Kind = InnScreenKind.ChefPicker;
        screens.PickerKitchen = 2;

        foreach (double t in new[] { 0.25, 0.5, 2.25 }) Assert.True(await monitor.TryInnAsync(AnyFrame, S(t), false));

        Assert.Equal(1, screens.SetupReads);
        Assert.Equal(2, screens.PickerReads);
        Assert.Equal([2, null, 2], raised.Skip(1).Select(r => r!.PickerKitchen));
        Assert.All(raised, r => Assert.True(r!.OnSetupScreens));
    }

    /// <summary>The theme list is told apart and nothing on it is read: it shows no advice.</summary>
    [Fact]
    public async Task Nothing_on_the_theme_list_is_read()
    {
        var (monitor, screens, raised) = Monitor();
        screens.Kind = InnScreenKind.ThemeList;

        foreach (double t in new[] { 0, 2.5 }) Assert.True(await monitor.TryInnAsync(AnyFrame, S(t), false));

        Assert.Equal((0, 0), (screens.SetupReads, screens.PickerReads));
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
        public Task<int?> ReadPickerKitchenAsync(Frame frame) => throw new NotSupportedException();
        public Task<InnMenu?> ReadMenuAsync(Frame frame) => throw new NotSupportedException();
        public Task<MenuRequirements?> ReadMenuRequirementsAsync(Frame frame) => throw new NotSupportedException();
    }
}
