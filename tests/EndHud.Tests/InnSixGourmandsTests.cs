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

    /// <summary>
    /// The game's Full Blaze meter on the right, as fractions of a 16:9 window: from x 0.855 with 开启满堂彩 up
    /// (session-fire-ready.jpg; 0.872 otherwise, session-fire-meter.jpg), y 0.50–0.645; widened a little in height.
    /// </summary>
    private static readonly Rect Meter = new(0.855, 0.47, 0.145, 0.20);

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

    /// <summary>The status at the top right (the avatar, 结账中/空闲中, the line the avatar says under it): to y 0.195, from x 0.8.</summary>
    private static readonly Rect TopRightHud = new(0.8, 0, 0.2, 0.195);

    private static InnGourmandArrangement Arrange(IReadOnlyList<InnGourmandBanner> banners, int width, int height, double scale) =>
        InnGourmandLayout.Arrange(banners, width, height, scale);

    /// <summary>
    /// Six gourmands at their fixed spots (owner, 2026-10-22: "should be roughly at where I draw"), one and two requests, at
    /// the player's default size: every tile inside the window and its spot, none over the status at the top right, the table
    /// cards, the Full Blaze meter, the tray, the kitchens' circles, the inn's banners, the UID or another spot; with the panel
    /// open, none over its kitchen tabs or close button. The owner's left spots lie over the lower part of the top-left HUD
    /// (the theme's line and the counts), as drawn.
    /// </summary>
    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(2560, 1440)]
    [InlineData(3840, 2160)]
    public void Six_gourmands_tiles_fit_clear_of_the_games_hud_and_the_circles(int width, int height)
    {
        double scale = ComboHintLayout.Scale(height, dpiScale: 1);
        Rect Of(Rect r) => new(r.X * width, r.Y * height, r.Width * width, r.Height * height);
        var avoid = new List<(string What, Rect Where)>
        {
            ("the circles", InnKitchenCircleLayout.Place(width, height, scale)),
            ("the inn's banners", InnScreenAreas.Of(InnScreenAreas.Banners, width, height)),
            ("the UID", Of(UidArea)), ("the table cards", Of(new Rect(0.14, 0, 0.47, 0.135))),
            ("the panel's kitchen tabs", Of(InnScreenAreas.Tabs)), ("the panel's close button", Of(InnScreenAreas.Close)),
            ("the status at the top right", Of(TopRightHud)), ("the Full Blaze meter", Of(Meter)),
            ("the held-dishes tray", Of(Tray)),
        };
        var spots = new List<Rect>();
        foreach (int requests in new[] { 1, 2 })
        {
            var banners = InnGourmandView.Banners([.. InnKitchenCircleTests.SixGourmandsWants().Where(w => w.Request < requests)], Text);
            var arrangement = Arrange(banners, width, height, scale);
            Assert.Equal(scale, arrangement.Scale);
            for (int i = 0; i < banners.Count; i++)
            {
                var spot = InnGourmandLayout.Spot(banners[i].Table, width, height);
                var rect = new Rect(arrangement.Places[i], new Size(InnGourmandView.Width(requests) * scale, InnGourmandView.Height(requests) * scale));
                output.WriteLine($"table {banners[i].Table}, {requests} request(s): x {rect.Left / width:F3}–{rect.Right / width:F3}, y {rect.Top / height:F3}–{rect.Bottom / height:F3}");
                Assert.True(rect.Left >= 0 && rect.Top >= 0 && rect.Right <= width && rect.Bottom <= height, $"table {banners[i].Table} off the window: {rect}");
                Assert.True(spot.Contains(rect.TopLeft) && rect.Right <= spot.Right + 0.5 && rect.Bottom <= spot.Bottom + 0.5, $"table {banners[i].Table} outside its spot {spot}: {rect}");
                foreach (var (what, where) in avoid)
                    Assert.False(Overlap(rect, where) > 1, $"table {banners[i].Table} over {what}: {rect} / {where}");
                if (requests == 2) spots.Add(spot);
            }
        }
        for (int a = 0; a < spots.Count; a++)
            for (int b = a + 1; b < spots.Count; b++)
                Assert.False(Overlap(spots[a], spots[b]) > 1, $"spots of tables {a + 1} and {b + 1} overlap");
    }

    /// <summary>
    /// Owner, live 2026-10-21 (test_yuanyuan8): "make the position FIXED, so user does not need to chase around and not know
    /// what to look at". A table's tile stands at one place: alone or with the other five (the panel can't move it: its state
    /// isn't an input of the layout).
    /// </summary>
    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(2560, 1440)]
    [InlineData(3840, 2160)]
    public void Each_tables_tile_has_one_fixed_place(int width, int height)
    {
        var all = InnGourmandView.Banners(InnKitchenCircleTests.SixGourmandsWants(), Text);
        double scale = ComboHintLayout.Scale(height, dpiScale: 1);
        var at = new List<Point>();
        for (int i = 0; i < all.Count; i++)
        {
            var places = new List<Point>
            {
                Arrange(all, width, height, scale).Places[i],
                Arrange([all[i]], width, height, scale).Places[0],
                Arrange([.. all.Take(i + 1)], width, height, scale).Places[i],
                Arrange([.. all.Skip(i)], width, height, scale).Places[0],
            };
            output.WriteLine($"table {all[i].Table}: {string.Join("  ", places.Select(p => $"({p.X:F0}, {p.Y:F0})"))}");
            Assert.True(places.Distinct().Count() == 1, $"table {all[i].Table} moves: {string.Join(" ", places.Distinct())}");
            at.Add(places[0]);
        }
    }

    /// <summary>
    /// Owner, 2026-10-22: "the left 3 banner for gourmand should be table 1-3, right should be 4-6, make sure you do it this
    /// way"; "the gourmand location IS TOO close to center! should be roughly at where I draw". Tables 1, 2, 3 down the left
    /// of the screen, top to bottom, x 0.11–0.24 as drawn; 4, 5, 6 down the right, top to bottom, x 0.73–0.91.
    /// </summary>
    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(3840, 2160)]
    [InlineData(2560, 1080)]
    public void Tables_1_to_3_go_down_the_left_and_4_to_6_down_the_right(int width, int height)
    {
        double unit = Math.Min(width / 16.0, height / 9.0), areaLeft = (width - unit * 16) / 2, areaWidth = unit * 16;
        var spots = Enumerable.Range(1, TableCardReader.Tables).Select(t => InnGourmandLayout.Spot(t, width, height)).ToList();
        double X(double x) => (x - areaLeft) / areaWidth;
        for (int t = 1; t <= 6; t++)
            output.WriteLine($"table {t}: x {X(spots[t - 1].Left):F3}-{X(spots[t - 1].Right):F3}, y {spots[t - 1].Top / height:F3}-{spots[t - 1].Bottom / height:F3}");
        for (int t = 0; t < 3; t++)
        {
            Assert.True(spots[t].Right <= width / 2.0 && X(spots[t].Left) >= 0.10 && X(spots[t].Right) <= 0.25, $"table {t + 1} not at the left: {spots[t]}");
            Assert.True(spots[t + 3].Left >= width / 2.0 && X(spots[t + 3].Left) >= 0.72 && X(spots[t + 3].Right) <= 0.92, $"table {t + 4} not at the right: {spots[t + 3]}");
        }
        Assert.True(spots[0].Bottom <= spots[1].Top && spots[1].Bottom <= spots[2].Top, "left column: tables 1, 2, 3 top to bottom");
        Assert.True(spots[3].Bottom <= spots[4].Top && spots[4].Bottom <= spots[5].Top, "right column: tables 4, 5, 6 top to bottom");
    }

    /// <summary>The player's size makes the tiles smaller, never larger than their spot (a larger one would cover the game's HUD or the panel).</summary>
    [Theory]
    [InlineData(0.6)]
    [InlineData(1.0)]
    [InlineData(1.5)]
    public void The_players_size_shrinks_the_tiles_and_never_grows_them_past_their_spot(double size)
    {
        var all = InnGourmandView.Banners(InnKitchenCircleTests.SixGourmandsWants(), Text);
        double scale = ComboHintLayout.Scale(1080, dpiScale: 1);
        var arrangement = Arrange(all, 1920, 1080, scale * size);
        Assert.Equal(scale * Math.Min(size, 1), arrangement.Scale, 6);
        for (int i = 0; i < all.Count; i++)
        {
            var spot = InnGourmandLayout.Spot(all[i].Table, 1920, 1080);
            var rect = new Rect(arrangement.Places[i], new Size(InnGourmandView.Width(2) * arrangement.Scale, InnGourmandView.Height(2) * arrangement.Scale));
            Assert.True(rect.Left >= spot.Left - 0.5 && rect.Top >= spot.Top - 0.5 && rect.Right <= spot.Right + 0.5 && rect.Bottom <= spot.Bottom + 0.5, $"table {all[i].Table} past its spot");
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
