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

    /// <summary>
    /// The session's HUD at the top left: 结束经营, the timer, the theme, its objective and the income, guests and dishes counts,
    /// on every recorded theme down to y 0.475 (session-two-dishes.jpg, session-gourmand-soup-and-drink.jpg,
    /// session-full-blaze-running.jpg, test_yuanyuan8 at 200 s and 300 s), x up to 0.28.
    /// </summary>
    private static readonly Rect TopLeftHud = new(0, 0, 0.285, 0.48);

    /// <summary>The status at the top right (the avatar, 结账中/空闲中, the line the avatar says under it): to y 0.195, from x 0.8.</summary>
    private static readonly Rect TopRightHud = new(0.8, 0, 0.2, 0.195);

    private static InnGourmandArrangement Arrange(IReadOnlyList<InnGourmandBanner> banners, int width, int height, double scale) =>
        InnGourmandLayout.Arrange(banners, width, height, scale);

    /// <summary>
    /// Six gourmands at their fixed spots (owner, test_yuanyuan8: "split the 6 gourmand table hints at the 6 corners of the
    /// window, make the position FIXED"), one and two requests, at the player's default size: every tile inside the window and
    /// its spot, none over the game's HUD at the top left or the top right, the table cards, the Full Blaze meter, the tray,
    /// the open dispatch panel, the kitchens' circles in either of their places, the UID or another spot. Since a spot doesn't
    /// depend on the panel, every one is checked against what the panel closed and the panel open both show.
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
            ("the circles above the tray", InnKitchenCircleLayout.Place(width, height, scale, InnKitchenCircleLayout.DefaultCenterX, InnKitchenCircleLayout.DefaultBottomY)),
            ("the circles below the panel", InnKitchenCircleLayout.BelowPanel(width, height, scale)),
            ("the UID", Of(UidArea)), ("the table cards", Of(new Rect(0.14, 0, 0.47, 0.135))), ("the HUD at the top left", Of(TopLeftHud)),
            ("the status at the top right", Of(TopRightHud)), ("the open panel", Of(PanelArea)), ("the Full Blaze meter", Of(Meter)),
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
    /// Owner, live 2026-10-21 (test_yuanyuan8): "split the 6 gourmand table hints at the 6 corners of the window, make the
    /// position FIXED, so user does not need to chase around and not know what to look at". A table's tile stands at one
    /// place: alone or with the other five (the panel can't move it: its state isn't an input of the layout). Tables 1–3 down
    /// the left edge, 4–6 down the right, each column top to bottom.
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
        Assert.All(at.Take(3), p => Assert.True(p.X < width / 2.0, "tables 1-3 on the left"));
        Assert.All(at.Skip(3), p => Assert.True(p.X > width / 2.0, "tables 4-6 on the right"));
        Assert.True(at[0].Y < at[1].Y && at[1].Y < at[2].Y && at[3].Y < at[4].Y && at[4].Y < at[5].Y, "top to bottom");
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
