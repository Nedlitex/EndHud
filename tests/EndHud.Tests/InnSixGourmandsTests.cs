using System.Windows;
using EndHud.Vision.Inn;
using Xunit;
using Xunit.Abstractions;
using static EndHud.Tests.InnTestKit;

namespace EndHud.Tests;

/// <summary>
/// All six tables gourmands at once (owner, 2026-10-21: "also we need to be prepared to support all 6 tables are
/// gourmand"): each keeps their requests, gets a tile, and the six tiles fit without covering the Full Blaze meter, the
/// tray, the circles or the open panel, or leaving the window, in table order.
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

    /// <summary>The open dispatch panel on a 16:9 window (the 4K panel frames: x 0.109–0.891, y 0.194–0.853).</summary>
    private static readonly Rect PanelArea = new(0.109, 0.194, 0.782, 0.659);

    /// <summary>The UID and ping in the lower-left corner (masked on the fixtures: x up to 0.26, y from 0.94).</summary>
    private static readonly Rect UidArea = new(0, 0.94, 0.26, 0.06);

    /// <summary>One to three gourmands keep the full tile (the requests in words): the smaller one only when there are many.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void A_few_gourmands_keep_the_full_tile(int count)
    {
        var some = InnGourmandView.Banners([.. InnKitchenCircleTests.SixGourmandsWants().Where(w => w.Table <= count)], Text);
        var arrangement = Arrange(some, 1920, 1080, 1, panelOpen: false);
        Assert.Equal((InnGourmandShape.Full, 1.0), (arrangement.Shape, arrangement.Scale));
    }

    private static InnGourmandArrangement Arrange(IReadOnlyList<InnGourmandBanner> banners, int width, int height, double scale, bool panelOpen) =>
        InnGourmandLayout.Arrange(banners, width, height, scale, HudConfig.DefaultInnGourmandRight, HudConfig.DefaultInnGourmandTop, panelOpen,
            panelOpen ? InnKitchenCircleLayout.BelowPanel(width, height, scale)
                : InnKitchenCircleLayout.Place(width, height, scale, InnKitchenCircleLayout.DefaultCenterX, InnKitchenCircleLayout.DefaultBottomY));

    /// <summary>
    /// Six gourmands of two requests each (owner, 2026-10-21: pictures "at least same size as the circule today"; the tiles
    /// must not overlap the fire meter, the table cards, the tray or the circles in either place): at the player's default
    /// size, with the panel closed and open, every tile inside the window, none over the Full Blaze meter or the tray (panel
    /// closed), the open panel (panel open), the kitchens' circles where they stand, the UID, the table cards at the top or
    /// another tile; drawn at the size asked for (pictures 112 at 1080p), the same places every time, in table order.
    /// </summary>
    [Theory]
    [InlineData(1920, 1080, false)]
    [InlineData(2560, 1440, false)]
    [InlineData(3840, 2160, false)]
    [InlineData(1920, 1080, true)]
    [InlineData(2560, 1440, true)]
    [InlineData(3840, 2160, true)]
    public void Six_gourmands_tiles_fit_clear_of_the_games_hud_and_the_circles(int width, int height, bool panelOpen)
    {
        var banners = InnGourmandView.Banners(InnKitchenCircleTests.SixGourmandsWants(), Text);
        double scale = ComboHintLayout.Scale(height, dpiScale: 1);
        var arrangement = Arrange(banners, width, height, scale, panelOpen);
        Assert.Equal(arrangement.Places, Arrange(banners, width, height, scale, panelOpen).Places);
        Assert.Equal(scale, arrangement.Scale);
        Assert.Contains(arrangement.Shape, panelOpen ? new[] { InnGourmandShape.Tall, InnGourmandShape.TallBare } : [InnGourmandShape.Full, InnGourmandShape.Compact]);

        Rect Of(Rect r) => new(r.X * width, r.Y * height, r.Width * width, r.Height * height);
        var circles = panelOpen ? InnKitchenCircleLayout.BelowPanel(width, height, scale)
            : InnKitchenCircleLayout.Place(width, height, scale, InnKitchenCircleLayout.DefaultCenterX, InnKitchenCircleLayout.DefaultBottomY);
        var avoid = new List<(string What, Rect Where)> { ("the circles", circles), ("the UID", Of(UidArea)), ("the table cards", Of(new Rect(0.14, 0, 0.47, 0.135))) };
        if (panelOpen) avoid.Add(("the open panel", Of(PanelArea)));
        else
        {
            avoid.Add(("the Full Blaze meter", Of(Meter)));
            avoid.Add(("the held-dishes tray", Of(Tray)));
        }
        var tiles = new List<Rect>();
        for (int i = 0; i < banners.Count; i++)
        {
            double s = arrangement.Scale;
            var rect = new Rect(arrangement.Places[i].X, arrangement.Places[i].Y,
                InnGourmandView.Width(arrangement.Shape) * s, InnGourmandView.Height(arrangement.Shape, banners[i].Rows.Count) * s);
            output.WriteLine($"table {banners[i].Table}: x {rect.Left / width:F3}–{rect.Right / width:F3}, y {rect.Top / height:F3}–{rect.Bottom / height:F3} {arrangement.Shape}");
            Assert.True(rect.Left >= 0 && rect.Top >= 0 && rect.Right <= width && rect.Bottom <= height, $"table {banners[i].Table} off the window: {rect}");
            foreach (var (what, where) in avoid)
                Assert.False(Overlap(rect, where) > 1, $"table {banners[i].Table} over {what}: {rect} / {where}");
            Assert.All(tiles, other => Assert.False(Overlap(rect, other) > 1, $"table {banners[i].Table} over another tile"));
            tiles.Add(rect);
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
