using System.Windows;
using EndHud.Vision.Inn;
using Xunit;
using Xunit.Abstractions;
using static EndHud.Tests.InnTestKit;

namespace EndHud.Tests;

/// <summary>
/// All six tables gourmands at once (owner, 2026-10-21: "also we need to be prepared to support all 6 tables are
/// gourmand"): each keeps their requests, gets a banner, and the six banners fit on the right without covering the
/// Full Blaze meter or leaving the window, in table order.
/// </summary>
[Collection(WpfCollection.Name)]
public class InnSixGourmandsTests(ITestOutputHelper output)
{
    private const string A = "melon-meatball-soup", B = "fin-mato-soup", C = "mala-xiang-guo", D = "sweetbeans-on-snow", E = "meat-baozi";
    private const string Tea = "pre-tide-lungjing", Milk = "bottled-milk-a";

    /// <summary>The overlay's frame around the banners: its padding and border (OverlayWindow.xaml).</summary>
    private const double Frame = 6 + 2;

    /// <summary>The game's Full Blaze meter on the right, as fractions of a 16:9 window (owner: about x 0.86–0.98, y 0.48–0.66; widened a little).</summary>
    private static readonly Rect Meter = new(0.84, 0.47, 0.15, 0.20);

    /// <summary>The held-dishes tray, its 派单 button and the bar right of it (session-two-dishes.jpg: x 0.38–0.78, y 0.855–0.94).</summary>
    private static readonly Rect Tray = new(0.38, 0.855, 0.40, 0.085);

    [Fact]
    public void Six_gourmands_each_get_a_banner_in_table_order()
    {
        var banners = InnGourmandView.Banners(InnKitchenCircleTests.SixGourmandsWants(), Text);
        Assert.Equal([1, 2, 3, 4, 5, 6], banners.Select(b => b.Table));
        Assert.All(banners, b => Assert.Equal(2, b.Rows.Count));
    }

    /// <summary>One or two gourmands keep the full look: compact only when there are many.</summary>
    [Fact]
    public void A_few_gourmands_keep_the_full_banner()
    {
        var two = InnGourmandView.Banners([.. InnKitchenCircleTests.SixGourmandsWants().Where(w => w.Table <= 2)], Text);
        var arrangement = InnGourmandLayout.Arrange(two, 1920, 1080, 1, HudConfig.DefaultInnGourmandRight, HudConfig.DefaultInnGourmandTop);
        Assert.False(arrangement.Compact);
        Assert.Equal(1, arrangement.Scale);
    }

    /// <summary>
    /// Six gourmands of two requests each, from the banners' default place at the player's default size: every banner
    /// inside the window, none over the Full Blaze meter, none over another, top to bottom by table.
    /// </summary>
    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(2560, 1440)]
    [InlineData(3840, 2160)]
    public void Six_gourmands_banners_fit_on_the_right_clear_of_the_Full_Blaze_meter(int width, int height)
    {
        var banners = InnGourmandView.Banners(InnKitchenCircleTests.SixGourmandsWants(), Text);
        double scale = ComboHintLayout.Scale(height, dpiScale: 1);
        var arrangement = InnGourmandLayout.Arrange(banners, width, height, scale, HudConfig.DefaultInnGourmandRight, HudConfig.DefaultInnGourmandTop);
        var widths = InnKitchenCircleTests.OnSta(() => banners.Select(b =>
        {
            var element = InnGourmandView.Banner(b, InnKitchenCircleTests.PackagePicture, arrangement.Compact);
            element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return element.DesiredSize.Width;
        }).ToList());
        var (fromRight, top) = InnGourmandLayout.Place(width, height, HudConfig.DefaultInnGourmandRight, HudConfig.DefaultInnGourmandTop);
        double s = arrangement.Scale;
        var meter = new Rect(Meter.X * width, Meter.Y * height, Meter.Width * width, Meter.Height * height);

        double lastBottom = double.NegativeInfinity;
        for (int i = 0; i < banners.Count; i++)
        {
            double bannerTop = top + (Frame + arrangement.Tops[i]) * s;
            double bannerHeight = InnGourmandView.Height(banners[i], arrangement.Compact) * s;
            double right = width - fromRight - Frame * s;
            var rect = new Rect(right - widths[i] * s, bannerTop, widths[i] * s, bannerHeight);
            output.WriteLine($"table {banners[i].Table}: x {rect.Left / width:F3}–{rect.Right / width:F3}, y {rect.Top / height:F3}–{rect.Bottom / height:F3}{(arrangement.Compact ? " compact" : "")} scale {s:F2}");
            Assert.True(rect.Left >= 0 && rect.Top >= 0 && rect.Right <= width && rect.Bottom <= height, $"table {banners[i].Table} off the window: {rect}");
            Assert.False(rect.IntersectsWith(meter) && Overlap(rect, meter) > 1, $"table {banners[i].Table} over the Full Blaze meter: {rect} / {meter}");
            var tray = new Rect(Tray.X * width, Tray.Y * height, Tray.Width * width, Tray.Height * height);
            Assert.False(Overlap(rect, tray) > 1, $"table {banners[i].Table} over the held-dishes tray: {rect} / {tray}");
            Assert.True(rect.Top >= lastBottom,$"table {banners[i].Table} over the one above it");
            lastBottom = rect.Bottom;
        }
    }

    private static double Overlap(Rect a, Rect b)
    {
        var both = Rect.Intersect(a, b);
        return both.IsEmpty ? 0 : both.Width * both.Height;
    }

    /// <summary>
    /// Through the real session: six gourmands arriving one after another, each saying two requests, are each kept with their
    /// own words (no cap on how many tables, the bubble given to the newest gourmand with none), and the views give all six.
    /// </summary>
    [Fact]
    public void Six_gourmands_at_once_are_all_kept_and_shown()
    {
        var session = NewSession();
        var display = new InnDisplay();
        session.Update(SetupScreen(0, 1, 2, 3));
        session.Update(MenuScreen(1, A, B, C, D, E, Tea, Milk));
        var said = DistinctBubbles(6);
        var cards = new List<TableCard>();
        for (int table = 1; table <= 6; table++)
        {
            cards.Add(new TableCard(table, TableState.GourmandOrder, [], []));
            double at = 10 + table;
            display.Show(session, Session(at, tables: Tables([.. cards]), bubble: said[table - 1]), S(at));
        }
        var advice = session.Advice(S(16))!;
        Assert.Equal([1, 2, 3, 4, 5, 6], advice.Gourmands.Select(g => g.Table));
        Assert.All(advice.Gourmands, g => Assert.Equal(2, g.Advice!.Requests.Count));
        // Their words each their own.
        Assert.Equal(6, advice.Gourmands.Select(g => string.Join("|", g.Advice!.Requests.Select(r => r.Request.Text))).Distinct().Count());

        display.Show(session, Session(17, tables: Tables([.. cards])), S(17));
        display.Views(session, S(17), callsEnabled: true, Text);
        // Up once what they say has stood a moment (InnWantsHold.ShowAfter).
        var wants = display.Views(session, S(17.6), callsEnabled: true, Text).Wants;
        Assert.Equal([1, 2, 3, 4, 5, 6], InnGourmandView.Banners(wants, Text).Select(b => b.Table));
    }

    /// <summary>Bubbles of <paramref name="count"/> gourmands, two requests each, no request said by two of them, each read back as said.</summary>
    private static IReadOnlyList<GourmandBubble> DistinctBubbles(int count)
    {
        var used = new HashSet<GourmandRequest>();
        var bubbles = new List<GourmandBubble>();
        foreach (var gourmand in Inn.Gourmands)
        {
            var two = gourmand.Requests.Where(r => !used.Contains(r) && Inn.MatchSaid(r.Text).FirstOrDefault()?.Request == r).Take(2).ToList();
            if (two.Count < 2) continue;
            string[] lines = [.. two.Select(r => r.Text)];
            var bubble = new GourmandBubble(string.Concat(lines), lines);
            if (!bubble.Requests(Inn).Select(m => m.Request).SequenceEqual(two)) continue;
            used.UnionWith(two);
            bubbles.Add(bubble);
            if (bubbles.Count == count) break;
        }
        Assert.Equal(count, bubbles.Count);
        return bubbles;
    }
}
