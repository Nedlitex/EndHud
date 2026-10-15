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
public class GaugesTests
{
    private static readonly Frame NoPortrait = new(1, 1, new byte[4]);

    private static PartySlot Slot(int number, string? id, UltimateReading? ultimate = null, bool dead = false) =>
        new(number, null, dead ? 0 : 1, "", NoPortrait, null, ultimate, dead,
            id is null ? null : new OperatorMatch(new OperatorInfo(id, id, id + "-zh", id + ".png"), 1, true));

    private static RememberedCounter Counter(string id, CounterDefinition counter, int? value, bool live = true) => new(id, counter, value, live);

    private static GaugeBoard Build(PartyState state, IReadOnlyList<RememberedCounter>? counters = null, IReadOnlyList<string>? buffsSeen = null) =>
        GaugesView.Build(state, counters ?? [], buffsSeen ?? [], (_, _) => null, _ => null, _ => null);

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
        var active = Build(new PartyState(squad, PanelMarks: 8) { Buffs = ["Link"] }, buffsSeen: ["Link"]);
        var lapsed = Build(new PartyState(squad, PanelMarks: 8), buffsSeen: ["Link"]);

        Assert.DoesNotContain(never.Gauges, g => g.Kind == GaugeKind.Buff);
        Assert.Equal((GaugeKind.Buff, StatusNames.Zh("Link"), true), (active.Gauges[0].Kind, active.Gauges[0].Label, active.Gauges[0].Ready));
        Assert.Equal((GaugeKind.Buff, false), (lapsed.Gauges[0].Kind, lapsed.Gauges[0].Ready));
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

    [Fact]
    public void A_ready_ultimate_is_a_full_glowing_gold_ring_and_a_charging_one_a_dimmer_partial_arc()
    {
        int ready = Render(new Gauge(GaugeKind.Ultimate, null, null, "ultimate", 1, true)).Count(Gold);
        int charging = Render(new Gauge(GaugeKind.Ultimate, null, null, "ultimate", 0.5, false)).Count(Gold, minAlpha: 250);

        Assert.True(ready > 900, $"{ready} gold pixels when ready");
        Assert.True(charging < ready / 3, $"{charging} vs {ready}");
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
