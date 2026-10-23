using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EndHud.Vision;
using EndHud.Vision.Operators;
using Xunit;

namespace EndHud.Tests;

/// <summary>
/// The gauges: what each circle shows for a reading (<see cref="GaugesView"/>), and how it's
/// drawn (<see cref="Gauges"/>): ready glows, charging doesn't, a counter lights one segment per stack,
/// an active buff glows.
/// </summary>
[Collection(WpfCollection.Name)]
public class GaugesTests
{
    private static readonly Frame NoPortrait = new(1, 1, new byte[4]);

    private static PartySlot Slot(int number, string? id, UltimateReading? ultimate = null, bool dead = false) =>
        new(number, null, dead ? 0 : 1, "", NoPortrait, null, ultimate, dead,
            id is null ? null : new OperatorMatch(new OperatorInfo(id, id, id + "-zh", id + ".png"), 1, true));

    private static RememberedCounter Counter(string id, CounterDefinition counter, int? value, bool live = true) => new(id, counter, value, live);

    private static GaugeBoard Build(PartyState state, IReadOnlyList<RememberedCounter>? counters = null, IReadOnlyList<string>? buffsSeen = null,
        IReadOnlyDictionary<string, SkillTimerView>? timers = null) =>
        GaugesView.Build(state, counters ?? [], buffsSeen ?? [], timers ?? new Dictionary<string, SkillTimerView>(), (_, _) => null, _ => null, _ => null);

    private static PartyState SiSquad() => new([Slot(1, "Si", new UltimateReading(false, 0.3)), Slot(2, "Arcane")], PanelMarks: 8);

    /// <summary>Si's damage stacks have no maximum (owner): a plain number, never a share of segments, never capped.</summary>
    [Fact]
    public void Si_stacks_are_a_plain_number_never_a_share_of_a_maximum()
    {
        foreach (int stacks in new[] { 0, 21, 45, 150 })
        {
            var gauges = Build(SiSquad(), [Counter("Si", OperatorCounters.SiStacks, stacks)]).Gauges;

            var counter = Assert.Single(gauges, g => g.Kind == GaugeKind.Counter);
            Assert.Equal((OperatorCounters.SiStacks.NameZh, stacks.ToString(), 0, null, false),
                (counter.Label, counter.Number, counter.Segments, counter.Fill, counter.Ready));
        }
    }

    /// <summary>The timer circle is there while there's something to show (the swirl seen), and only then.</summary>
    [Fact]
    public void Si_timer_gauge_shows_only_while_a_view_exists()
    {
        var running = new Dictionary<string, SkillTimerView> { ["Si"] = new(0.5, 11, Estimated: false) };

        var with = Build(SiSquad(), timers: running).Gauges;
        var without = Build(SiSquad()).Gauges;

        var timer = Assert.Single(with, g => g.Kind == GaugeKind.SkillTimer);
        Assert.Equal((Loc.Get("Gauge.SkillRunning"), 0.5, true), (timer.Label, timer.Fill, timer.Running));
        Assert.Same(timer, with[1]); // right after her ultimate
        Assert.DoesNotContain(without, g => g.Kind == GaugeKind.SkillTimer);
        Assert.NotEqual(GaugesView.Key(new GaugeBoard(without)), GaugesView.Key(new GaugeBoard(with)));
    }

    /// <summary>The seconds left, rounded up; "≈" in front when estimated (the ring couldn't be read).</summary>
    [Fact]
    public void Si_timer_gauge_shows_seconds_and_marks_an_estimate()
    {
        string Number(double seconds, bool estimated) => Build(SiSquad(),
            timers: new Dictionary<string, SkillTimerView> { ["Si"] = new(seconds / 22, seconds, estimated) }).Gauges.Single(g => g.Kind == GaugeKind.SkillTimer).Number!;

        Assert.Equal("11", Number(11, false));
        Assert.Equal("11", Number(10.2, false));
        Assert.Equal("≈11", Number(11, true));
        Assert.Equal("1", Number(0.3, false));
    }

    /// <summary>Every text in a drawn gauge, with its size, and how many ring arcs it has.</summary>
    private static (List<(string Text, double Size)> Texts, int Arcs) Parts(Gauge gauge) => OnSta(() =>
    {
        var texts = new List<(string, double)>();
        int arcs = 0;
        void Walk(object node)
        {
            if (node is System.Windows.Controls.TextBlock t) texts.Add((t.Text, t.FontSize));
            if (node is System.Windows.Shapes.Path) arcs++;
            if (node is DependencyObject d) foreach (object child in LogicalTreeHelper.GetChildren(d)) Walk(child);
        }
        Walk(Gauges.Build(new GaugeBoard([gauge])));
        return (texts, arcs);
    });

    [Fact]
    public void Si_stacks_draw_as_a_number_without_segments()
    {
        var (texts, arcs) = Parts(new Gauge(GaugeKind.Counter, null, null, "stacks", null, false) { Number = "21", Segments = 0 });
        var (segmentedTexts, segmentedArcs) = Parts(new Gauge(GaugeKind.Counter, null, null, "arrows", 0.5, false) { Number = "2", Segments = 4 });

        Assert.Contains(("21", 34.0), texts);           // large, in the middle
        Assert.Equal(0, arcs);                          // no segments
        Assert.Equal(2, segmentedArcs);                 // a segmented counter still has its two lit segments
        Assert.Contains(("2", 25.0), segmentedTexts);   // and its count in the corner badge
    }

    private static bool Grey(byte r, byte g, byte b) => r is > 160 and < 210 && g is > 175 and < 220 && b is > 190 and < 230 && b > r;

    /// <summary>The game's grey-white, glowing, draining like a running ultimate; the seconds in the corner.</summary>
    [Fact]
    public void Si_timer_is_a_grey_ring_that_drains()
    {
        Rendered Timer(double left) => Render(new Gauge(GaugeKind.SkillTimer, null, null, "timer", left, false) { Running = true, Number = "11" });

        int full = Timer(1).Count(Grey), half = Timer(0.5).Count(Grey), nearlyOut = Timer(0.1).Count(Grey);
        Assert.True(full > 900, $"{full} grey pixels when full");
        Assert.True(half < full * 0.75 && nearlyOut < half / 2, $"{full}, {half}, {nearlyOut}");
        Assert.Equal(0, Timer(0.5).Count(Violet));
        Assert.Contains(("11", 25.0), Parts(new Gauge(GaugeKind.SkillTimer, null, null, "timer", 0.5, false) { Running = true, Number = "11" }).Texts);
    }

    [Fact]
    public void Each_recognised_operator_gets_their_ultimate_then_their_own_counters_in_slot_order()
    {
        var state = new PartyState(
        [
            Slot(1, "Zhuang_Fangyi", new UltimateReading(true, 1)),
            Slot(2, null, new UltimateReading(true, 1)), // not recognised yet: nothing to show
            Slot(3, "Typhoeus", new UltimateReading(false, 0.78)),
            Slot(4, "Arcane"),
        ], PanelMarks: 8);
        var counters = new[]
        {
            Counter("Zhuang_Fangyi", OperatorCounters.Sunderblade, 9),
            Counter("Typhoeus", OperatorCounters.HuntingArrow, 2, live: false),
            Counter("Typhoeus", OperatorCounters.Sign, null, live: false),
        };

        var gauges = Build(state, counters).Gauges;

        string ultimate = Loc.Get("Gauge.Ultimate");
        Assert.Equal(
            [(GaugeKind.Ultimate, ultimate), (GaugeKind.Counter, OperatorCounters.Sunderblade.NameZh), (GaugeKind.Ultimate, ultimate),
                (GaugeKind.Counter, OperatorCounters.HuntingArrow.NameZh), (GaugeKind.Counter, OperatorCounters.Sign.NameZh), (GaugeKind.Ultimate, ultimate)],
            gauges.Select(g => (g.Kind, g.Label)));
        Assert.Equal([true, false, true, false, false, true], gauges.Select(g => g.FirstOfOperator)); // spacing before each operator
        Assert.Equal((1.0, true), (gauges[0].Fill, gauges[0].Ready));
        Assert.Equal(("9", 9, true), (gauges[1].Number, gauges[1].Segments, gauges[1].Ready));
        Assert.Equal((0.78, false), (gauges[2].Fill, gauges[2].Ready));
        Assert.Equal(("2", 0.5, false), (gauges[3].Number, gauges[3].Fill, gauges[3].Live));
        Assert.Equal(("?", null), (gauges[4].Number, gauges[4].Fill)); // not seen yet: no guess
        Assert.Null(gauges[5].Fill); // unreadable now (an effect over it): empty, not a guess
    }

    [Fact]
    public void A_buff_shows_once_seen_this_battle_lit_only_while_active_before_the_operators()
    {
        var squad = new[] { Slot(1, "Laevatain", new UltimateReading(false, 0.3)) };

        var never = Build(new PartyState(squad, PanelMarks: 8));
        var active = Build(new PartyState(squad, PanelMarks: 8) { Buffs = [new TeamBuff("Link", 1)] }, buffsSeen: ["Link"]);
        var lapsed = Build(new PartyState(squad, PanelMarks: 8), buffsSeen: ["Link"]);

        Assert.DoesNotContain(never.Gauges, g => g.Kind == GaugeKind.Buff);
        Assert.Equal((GaugeKind.Buff, StatusNames.Zh("Link"), true), (active.Gauges[0].Kind, active.Gauges[0].Label, active.Gauges[0].Ready));
        Assert.Equal((GaugeKind.Buff, false), (lapsed.Gauges[0].Kind, lapsed.Gauges[0].Ready));
        Assert.Null(lapsed.Gauges[0].Number);
    }

    /// <summary>Link stacks (up to four): one ring segment per stack and the count in the corner, as for an operator's counter.</summary>
    [Theory]
    [InlineData(1, "1", 0.25)]
    [InlineData(2, "2", 0.5)]
    [InlineData(4, "4", 1.0)]
    [InlineData(null, "?", 1.0)]
    public void Links_stacks_are_shown(int? stacks, string number, double fill)
    {
        var squad = new[] { Slot(1, "Laevatain", new UltimateReading(false, 0.3)) };

        var link = Build(new PartyState(squad, PanelMarks: 8) { Buffs = [new TeamBuff("Link", stacks)] }, buffsSeen: ["Link"]).Gauges[0];

        Assert.Equal((number, fill, TeamBuffReader.MaxStacks, true), (link.Number, link.Fill, link.Segments, link.Ready));
    }

    [Fact]
    public void A_fallen_operators_gauges_are_marked()
    {
        var board = Build(new PartyState([Slot(1, "Zhuang_Fangyi", dead: true)], PanelMarks: 8),
            [Counter("Zhuang_Fangyi", OperatorCounters.Sunderblade, 3)]);

        Assert.All(board.Gauges, g => Assert.True(g.Fallen));
    }

    [Fact]
    public void The_key_changes_when_something_drawn_changes_but_not_for_tiny_fill_changes()
    {
        static string Key(double charge, bool ready) => GaugesView.Key(Build(new PartyState([Slot(1, "Arcane", new UltimateReading(ready, charge))], PanelMarks: 8)));

        Assert.Equal(Key(0.500, false), Key(0.501, false));
        Assert.NotEqual(Key(0.5, false), Key(0.6, false));
        Assert.NotEqual(Key(1, false), Key(1, true));
    }

    // ---- Drawn ----

    private sealed record Rendered(int Width, int Height, byte[] Bgra)
    {
        public int Count(Func<byte, byte, byte, bool> match, int minAlpha = 120)
        {
            int n = 0;
            for (int i = 0; i < Bgra.Length; i += 4)
            {
                int a = Bgra[i + 3];
                if (a < minAlpha) continue;
                // Un-premultiply.
                byte r = (byte)(Bgra[i + 2] * 255 / a), g = (byte)(Bgra[i + 1] * 255 / a), b = (byte)(Bgra[i] * 255 / a);
                if (match(r, g, b)) n++;
            }
            return n;
        }
    }

    private static Rendered Render(params Gauge[] gauges) => OnSta(() =>
    {
        var element = (FrameworkElement)Gauges.Build(new GaugeBoard(gauges));
        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        element.Arrange(new Rect(element.DesiredSize));
        int w = (int)Math.Ceiling(element.DesiredSize.Width), h = (int)Math.Ceiling(element.DesiredSize.Height);
        var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var pixels = new byte[w * h * 4];
        bitmap.CopyPixels(pixels, w * 4, 0);
        return new Rendered(w, h, pixels);
    });

    private static bool Gold(byte r, byte g, byte b) => r > 220 && g is > 170 and < 225 && b < 110;
    private static bool Cyan(byte r, byte g, byte b) => r < 140 && g > 200 && b > 230;
    private static bool Green(byte r, byte g, byte b) => r is > 90 and < 160 && g > 220 && b is > 120 and < 190;
    private static bool Violet(byte r, byte g, byte b) => r > 190 && g is > 90 and < 160 && b > 230;

    /// <summary>
    /// A lasting ultimate running (Laevatain's, Zhuang Fangyi's): the controlled operator's gauge shows
    /// its time left instead of the charge, labelled 持续中; the others keep their charge.
    /// </summary>
    [Fact]
    public void A_running_ultimate_shows_its_time_left_on_the_controlled_operators_gauge()
    {
        var state = new PartyState([Slot(1, "Laevatain", new UltimateReading(false, 0)), Slot(2, "Arcane", new UltimateReading(false, 0.4))], PanelMarks: 8)
        {
            ActiveSlot = 1,
            UltimateTimeLeft = 0.6,
        };

        var gauges = Build(state).Gauges;

        Assert.Equal((true, 0.6, Loc.Get("Gauge.UltimateRunning")), (gauges[0].Running, gauges[0].Fill, gauges[0].Label));
        Assert.Equal((false, 0.4), (gauges[1].Running, gauges[1].Fill));
        Assert.NotEqual(GaugesView.Key(Build(state with { UltimateTimeLeft = null })), GaugesView.Key(Build(state))); // redrawn when it starts
    }

    /// <summary>
    /// Zhuang Fangyi's ultimate cast while Perlica is controlled (measured, Fixtures/lasting-ultimate-background
    /// zh-38_5s): the bar under Perlica's HP is Zhuang's. Her gauge shows it; Perlica's keeps its charge.
    /// </summary>
    [Fact]
    public void A_lasting_ultimate_of_an_operator_in_the_background_shows_on_her_gauge_not_the_controlled_ones()
    {
        var state = new PartyState([Slot(1, "Zhuang_Fangyi", new UltimateReading(false, 0)), Slot(2, "Perlica", new UltimateReading(true, 1))], PanelMarks: 8)
        {
            ActiveSlot = 2,
            UltimateTimeLeft = 0.86,
        };

        var gauges = Build(state).Gauges;

        var zhuang = gauges[0];
        var perlica = gauges.Single(g => g.Kind == GaugeKind.Ultimate && !ReferenceEquals(g, zhuang));
        Assert.Equal((true, 0.86, Loc.Get("Gauge.UltimateRunning")), (zhuang.Running, zhuang.Fill, zhuang.Label));
        Assert.Equal((false, 1.0, true, Loc.Get("Gauge.Ultimate")), (perlica.Running, perlica.Fill, perlica.Ready, perlica.Label));
    }

    /// <summary>Drawn violet and glowing, not gold, and draining: less time left, less ring.</summary>
    [Fact]
    public void A_running_ultimate_is_a_violet_ring_that_drains()
    {
        Rendered Running(double left) => Render(new Gauge(GaugeKind.Ultimate, null, null, "持续中", left, false) { Running = true });

        int full = Running(1).Count(Violet), half = Running(0.5).Count(Violet), nearlyOut = Running(0.1).Count(Violet);
        Assert.True(full > 900, $"{full} violet pixels when full");
        Assert.True(half < full * 0.75 && nearlyOut < half / 2, $"{full}, {half}, {nearlyOut}");
        Assert.Equal(0, Running(0.5).Count(Gold));
    }

    [Fact]
    public void A_ready_ultimate_is_a_full_glowing_gold_ring_and_a_charging_one_a_dimmer_partial_arc()
    {
        int ready = Render(new Gauge(GaugeKind.Ultimate, null, null, "ultimate", 1, true)).Count(Gold);
        int charging = Render(new Gauge(GaugeKind.Ultimate, null, null, "ultimate", 0.5, false)).Count(Gold, minAlpha: 250);

        Assert.True(ready > 900, $"{ready} gold pixels when ready");
        Assert.True(charging < ready / 3, $"{charging} vs {ready}");
    }

    /// <summary>A charge of 99% drawn as a closed ring looked ready (the owner's full battle): a gap stays open.</summary>
    [Fact]
    public void An_ultimate_nearly_charged_still_shows_a_gap_until_it_is_ready()
    {
        Rendered Ultimate(double fill, bool ready) => Render(new Gauge(GaugeKind.Ultimate, null, null, "ultimate", fill, ready));

        // 99% is drawn as 92%: the same picture, a gap at 12 o'clock.
        Assert.Equal(Ultimate(Gauges.NotReadyMaxFill, false).Bgra, Ultimate(0.99, false).Bgra);
        Assert.NotEqual(Ultimate(0.5, false).Bgra, Ultimate(0.99, false).Bgra);
        // Ready closes it, glowing.
        Assert.True(Ultimate(1, true).Count(Gold) > 900);
    }

    [Fact]
    public void A_counter_lights_one_segment_per_stack()
    {
        int Lit(int value) => Render(new Gauge(GaugeKind.Counter, null, null, "arrows", value / 4.0, false) { Number = $"{value}", Segments = 4 })
            .Count(Cyan);

        // Each stack lights more of the ring (the count's badge covers part of the bottom-right segment,
        // so they don't add up exactly).
        int[] lit = [Lit(0), Lit(1), Lit(2), Lit(3), Lit(4)];
        Assert.Equal(0, lit[0]);
        Assert.True(lit[1] > 80, $"{lit[1]} lit pixels for 1");
        for (int n = 1; n < 4; n++) Assert.True(lit[n + 1] > lit[n] + 40, $"{lit[n + 1]} lit for {n + 1}, {lit[n]} for {n}");
    }

    [Fact]
    public void An_active_buff_glows_green_and_a_lapsed_one_has_no_ring()
    {
        Assert.True(Render(new Gauge(GaugeKind.Buff, null, null, "Link", null, true)).Count(Green) > 900);
        Assert.Equal(0, Render(new Gauge(GaugeKind.Buff, null, null, "Link", null, false)).Count(Green, minAlpha: 1));
    }

    private static T OnSta<T>(Func<T> work)
    {
        T result = default!;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { result = work(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw failure;
        return result;
    }
}
