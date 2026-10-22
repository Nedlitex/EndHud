using System.Windows;
using System.Windows.Controls;
using EndHud.Vision.Inn;
using Xunit;
using Xunit.Abstractions;
using static EndHud.Tests.InnTestKit;

namespace EndHud.Tests;

/// <summary>
/// Owner, 2026-10-22: "do not change the circle location, keep them at fixed location, like gourmand banners. user is
/// very hard to trace things that are flying around"; "that must stay at the middle bottom, above the dishes in hands". Each kitchen's circle stands at one place on the screen: with the
/// dispatch panel open or closed, one circle up or three, any kind of call, and that place is free of the game's own
/// screen in both states.
/// </summary>
[Collection(WpfCollection.Name)]
public class InnCirclesFixedTests(ITestOutputHelper output)
{
    private const string A = "melon-meatball-soup", B = "fin-mato-soup", C = "mala-xiang-guo", D = "sweetbeans-on-snow", E = "meat-baozi";
    private static readonly string[] Menu = [A, B, C, D, E];

    private static InnCall Order(int kitchen, string dish, int table) => new(InnCallKind.Queue, kitchen, Dish(dish), table);

    private static InnCall Ahead(int kitchen, string dish) => new(InnCallKind.CookAhead, kitchen, Dish(dish));

    private static InnCall Swap(int kitchen, string remove, string put, int table) => new(InnCallKind.Swap, kitchen, Dish(put), table, Dish(remove));

    /// <summary>The views the overlay draws from, with the dispatch panel closed and then open (its kitchen 2 tab selected).</summary>
    private static (InnViews Closed, InnViews Open) PanelClosedAndOpen()
    {
        var session = NewSession();
        session.Update(SetupScreen(0, 1, 2));
        session.Update(MenuScreen(1, Menu));
        var display = new InnDisplay();
        var tables = Tables(InnTestKit.Order(3, A));
        display.Show(session, Session(10, tables: tables), S(10));
        var closed = display.Views(session, S(10), callsEnabled: true, Text);
        display.Show(session, Session(10.3, tables: tables, panel: Panel(2, 2, Menu)), S(10.3));
        var open = display.Views(session, S(10.3), callsEnabled: true, Text);
        Assert.True(open.Away);
        return (closed, open);
    }

    /// <summary>
    /// Where the overlay draws <paramref name="kitchen"/>'s circle on a <paramref name="width"/> × <paramref name="height"/>
    /// window, for <paramref name="views"/> (the panel's state) with <paramref name="slots"/> up: the row's place, plus the
    /// circle's own place in the row as WPF lays it out, at the row's scale.
    /// </summary>
    private static Rect CircleOnScreen(int kitchen, InnViews views, IReadOnlyList<InnCircleModel?> slots, int width, int height)
    {
        double scale = ComboHintLayout.Scale(height, dpiScale: 1);
        _ = views; // the place takes no panel state: one row, panel open or closed
        var row = InnKitchenCircleLayout.Place(width, height, scale);
        double s = InnKitchenCircleLayout.Scale(width, height, scale);
        var inRow = InnKitchenCircleTests.OnSta(() =>
        {
            var grid = InnCircleRow.NewGrid();
            var circles = new InnCircleRow(grid, m => InnKitchenCircleView.Circle(m, InnKitchenCircleTests.PackagePicture));
            circles.Update(slots);
            circles.Settle();
            grid.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            grid.Arrange(new Rect(grid.DesiredSize));
            grid.UpdateLayout();
            var element = circles.CircleIn(kitchen)!;
            var at = element.TranslatePoint(new Point(0, 0), grid);
            return new Rect(at, new Size(element.ActualWidth, element.ActualHeight));
        });
        return new Rect(row.Left + inRow.Left * s, row.Top + inRow.Top * s, inRow.Width * s, inRow.Height * s);
    }

    /// <summary>
    /// Each kitchen's circle at one place: panel closed or open, alone or with one or two others (every subset of the three
    /// kitchens), whatever kind of call, at 1080p, 1440p and 4K.
    /// </summary>
    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(2560, 1440)]
    [InlineData(3840, 2160)]
    public void Each_kitchens_circle_stands_at_one_place_panel_open_or_closed_with_one_to_three_up(int width, int height)
    {
        var (closed, open) = PanelClosedAndOpen();
        InnCall[][] kinds =
        [
            [Order(1, A, 4), Ahead(2, D), Swap(3, D, B, 5)],
            [Swap(1, C, E, 2), Order(2, B, 6), Ahead(3, A)],
        ];
        for (int kitchen = 1; kitchen <= InnKitchenCircleView.Slots; kitchen++)
        {
            var places = new List<(string When, Rect Where)>();
            foreach (var calls in kinds)
                for (int subset = 1; subset < 1 << InnKitchenCircleView.Slots; subset++)
                {
                    if ((subset & (1 << (kitchen - 1))) == 0) continue;
                    var up = InnKitchenCircleView.Of([.. calls.Where(c => (subset & (1 << (c.Kitchen!.Value - 1))) != 0)], Text);
                    foreach (var (state, views) in new[] { ("closed", closed), ("open", open) })
                        places.Add(($"panel {state}, kitchens {Convert.ToString(subset, 2)}", CircleOnScreen(kitchen, views, up, width, height)));
                }
            var distinct = places.Select(p => p.Where).Distinct().ToList();
            output.WriteLine($"{width}x{height} kitchen {kitchen}: {places.Count} cases, {distinct.Count} place(s): {string.Join("  ", distinct.Select(r => $"x {r.Left / width:F3}-{r.Right / width:F3} y {r.Top / height:F3}-{r.Bottom / height:F3}"))}");
            Assert.True(distinct.Count == 1, $"kitchen {kitchen}'s circle moves: {string.Join("; ", places.GroupBy(p => p.Where).Select(g => $"{g.Key} ({g.First().When})"))}");
        }
    }

    // ---- Clear of the game's screen ----

    /// <summary>
    /// The row, at the player's default size and the largest (the size slider never grows it past its place): centred, its
    /// bottom right above the held-dishes tray (owner, 2026-10-22: "that must stay at the middle bottom, above the dishes in
    /// hands"); with the panel closed over none of the game's HUD, nor the gourmands' six spots or the inn's banners at their
    /// default place. With the panel open the same row covers the panel's lower part (the owner accepted that over another
    /// style): how much of the dish cards and the queue it covers is written out, and it never reaches the tabs or the close
    /// button.
    /// </summary>
    [Theory]
    [InlineData(1920, 1080, 1.0)]
    [InlineData(2560, 1440, 1.0)]
    [InlineData(3840, 2160, 1.0)]
    [InlineData(1920, 1080, 1.5)]
    [InlineData(3840, 2160, 0.6)]
    public void The_circles_are_clear_of_the_games_screen_panel_open_or_closed(int width, int height, double size)
    {
        double scale = ComboHintLayout.Scale(height, dpiScale: 1) * size;
        var row = InnKitchenCircleLayout.Place(width, height, scale);
        output.WriteLine($"{width}x{height} size {size}: x {row.Left / width:F4}-{row.Right / width:F4}, y {row.Top / height:F4}-{row.Bottom / height:F4} "
                         + $"({row.Width * 1080 / height:F0} x {row.Height * 1080 / height:F0} at 1080p)");
        var avoid = InnScreenAreas.PanelClosed(width, height).ToList();
        avoid.Add(("the panel's kitchen tabs", InnScreenAreas.Of(InnScreenAreas.Tabs, width, height)));
        avoid.Add(("the panel's close button", InnScreenAreas.Of(InnScreenAreas.Close, width, height)));
        for (int table = 1; table <= TableCardReader.Tables; table++) avoid.Add(($"gourmand table {table}'s spot", InnGourmandLayout.Spot(table, width, height)));
        avoid.Add(("the inn's banners", InnScreenAreas.Of(InnScreenAreas.Banners, width, height)));
        foreach (var (what, where) in new[] { ("dish cards", InnScreenAreas.DishCards), ("queue", InnScreenAreas.Queue) })
        {
            var both = Rect.Intersect(row, InnScreenAreas.Of(where, width, height));
            output.WriteLine(both.IsEmpty ? $"  panel open: over none of its {what}"
                : $"  panel open: over its {what} at x {both.Left / width:F3}-{both.Right / width:F3}, y {both.Top / height:F3}-{both.Bottom / height:F3}");
        }

        Assert.True(row.Left >= 0 && row.Top >= 0 && row.Right <= width && row.Bottom <= height, $"off the window: {row}");
        Assert.True(Math.Abs((row.Left + row.Right) / 2 - width / 2.0) < 1, $"not centred: x {row.Left / width:F3}-{row.Right / width:F3}");
        double trayTop = InnScreenAreas.Tray.Top * height;
        Assert.True(row.Bottom <= trayTop && row.Bottom >= trayTop - 0.02 * height, $"not right above the tray: bottom {row.Bottom / height:F3}");
        foreach (var (what, where) in avoid) Assert.True(InnScreenAreas.Overlap(row, where) < 1, $"over {what}: {row} / {where}");
    }
}

/// <summary>
/// The game's screen during an inn session, as fractions of the window's centred 16:9 area, measured on the 4K frames of
/// tests/data (Fixtures/inn): what the inn's overlays must not cover with the dispatch panel open and with it closed.
/// </summary>
internal static class InnScreenAreas
{
    // ---- The open dispatch panel (dispatch-panel-queue.jpg, dispatch-panel-banners.jpg, session-dispatch-panel.jpg) ----

    /// <summary>The whole panel: x 0.109-0.891, y 0.194-0.856.</summary>
    public static readonly Rect Panel = new(0.109, 0.194, 0.782, 0.662);

    /// <summary>The kitchen tabs along its top (three tabs and the E key end at x 0.67), y 0.2-0.28.</summary>
    public static readonly Rect Tabs = new(0.12, 0.2, 0.555, 0.08);

    /// <summary>The close button at its top right.</summary>
    public static readonly Rect Close = new(0.855, 0.2, 0.04, 0.06);

    /// <summary>The dish cards (from x 0.3827, y 0.29 to 0.80 with four rows) and the 显示制作原料 switch under them.</summary>
    public static readonly Rect DishCards = new(0.38, 0.285, 0.48, 0.56);

    /// <summary>The selected kitchen's queue at the lower left: the dish being cooked's ring from y 0.697, the slots, to y 0.85.</summary>
    public static readonly Rect Queue = new(0.13, 0.695, 0.25, 0.155);

    /// <summary>
    /// The panel's left column above the queue: the chefs' portraits (y 0.307-0.467) and their bonus (y 0.518-0.653), x 0.112-0.38.
    /// Nothing the player needs while dispatching: the circles may stand over it.
    /// </summary>
    public static readonly Rect PanelLeftColumn = new(0.112, 0.285, 0.268, 0.41);

    // ---- The session's HUD, panel closed (session-*.jpg) ----

    /// <summary>
    /// The held-dishes tray with everything around it: 切换手持 Q (from x 0.304), the five slots (x 0.378-0.623), 派单 and the
    /// cooking bar right of it (to x 0.782), from y 0.854 to the slots' numbers at 0.963.
    /// </summary>
    public static readonly Rect Tray = new(0.30, 0.85, 0.485, 0.115);

    /// <summary>The table cards along the top (x 0.147-0.588, to y 0.132) and the game's last-call bar under them (打烊, to x 0.6194, y 0.137-0.167).</summary>
    public static readonly Rect TableCards = new(0.14, 0, 0.4794, 0.17);

    /// <summary>The timer, theme, objective and counts at the top left (its last line, 出售菜品数, to y 0.487), x up to 0.285.</summary>
    public static readonly Rect TopLeftHud = new(0, 0, 0.285, 0.488);

    /// <summary>The status pill at the top right (from x 0.8405, y 0.05-0.114) and the avatar right of it.</summary>
    public static readonly Rect Status = new(0.838, 0, 0.162, 0.125);

    /// <summary>The line the avatar says, under the status (its bracket from x 0.8126, y 0.137-0.202).</summary>
    public static readonly Rect AvatarLine = new(0.8115, 0.125, 0.1885, 0.08);

    /// <summary>The Full Blaze meter on the right (x 0.825-0.98, y 0.48-0.67), widened.</summary>
    public static readonly Rect Meter = new(0.82, 0.465, 0.18, 0.22);

    /// <summary>The UID in the lower-left corner (masked on the fixtures: x up to 0.287, y from 0.94).</summary>
    public static readonly Rect Uid = new(0, 0.93, 0.29, 0.07);

    /// <summary>The inn's own banners (开启满堂彩, the last call) at their default place (HudConfig.DefaultInnCallX/Y), two banners' height.</summary>
    public static readonly Rect Banners = new(HudConfig.DefaultInnCallX, HudConfig.DefaultInnCallY, 0.2, 0.14);

    /// <summary>What the player needs on the open panel.</summary>
    public static IEnumerable<(string What, Rect Where)> PanelOpen(int width, int height) =>
    [
        ("the panel's dish cards", Of(DishCards, width, height)), ("the panel's kitchen tabs", Of(Tabs, width, height)),
        ("the panel's queue", Of(Queue, width, height)), ("the panel's close button", Of(Close, width, height)),
    ];

    /// <summary>The game's HUD with the panel closed.</summary>
    public static IEnumerable<(string What, Rect Where)> PanelClosed(int width, int height) =>
    [
        ("the tray and 派单", Of(Tray, width, height)), ("the table cards", Of(TableCards, width, height)),
        ("the HUD at the top left", Of(TopLeftHud, width, height)), ("the status at the top right", Of(Status, width, height)),
        ("the avatar's line", Of(AvatarLine, width, height)), ("the Full Blaze meter", Of(Meter, width, height)), ("the UID", Of(Uid, width, height)),
    ];

    /// <summary><paramref name="fractions"/> of a 16:9 <paramref name="width"/> x <paramref name="height"/> window, in its units.</summary>
    public static Rect Of(Rect fractions, int width, int height) => new(fractions.X * width, fractions.Y * height, fractions.Width * width, fractions.Height * height);

    public static double Overlap(Rect a, Rect b)
    {
        var both = Rect.Intersect(a, b);
        return both.IsEmpty ? 0 : both.Width * both.Height;
    }
}
