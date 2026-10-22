using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EndHud.Vision.Inn;
using Xunit;
using Xunit.Abstractions;
using static EndHud.Tests.InnTestKit;

namespace EndHud.Tests;

/// <summary>
/// The kitchens' calls as big circles above the held-dishes tray (owner, live 2026-10-21, test_yuanyuan5: "the notification
/// of each kitchen is very hard to read. try another UI: show the recommended dish for each kitchen with a big circle (like
/// buff) that is placed in the middle of the screen above where the currently held dishes are"), and nothing of the inn's
/// helper over the open dispatch panel but its marks.
/// </summary>
[Collection(WpfCollection.Name)]
public class InnKitchenCircleTests(ITestOutputHelper output)
{
    private const string A = "melon-meatball-soup", B = "fin-mato-soup", C = "mala-xiang-guo", D = "sweetbeans-on-snow", E = "meat-baozi";

    /// <summary>The overlay's frame around the circles: its padding and border (OverlayWindow.xaml).</summary>
    private const double Frame = 2 * (6 + 2);

    private static InnCall Order(int kitchen, string dish, int table) => new(InnCallKind.Queue, kitchen, Dish(dish), table);

    private static InnCall Ahead(int kitchen, string dish) => new(InnCallKind.CookAhead, kitchen, Dish(dish));

    private static InnCall Swap(int kitchen, string remove, string put, int table) => new(InnCallKind.Swap, kitchen, Dish(put), table, Dish(remove));

    // ---- One slot per kitchen ----

    /// <summary>
    /// Kitchen 1 is always the left slot, 2 the middle and 3 the right; a kitchen with no call leaves its slot empty, so a
    /// circle never moves sideways when another comes or goes. Full Blaze and the last call are not a kitchen's: no circle.
    /// </summary>
    [Fact]
    public void Each_kitchen_has_its_own_slot_left_to_right()
    {
        var slots = InnKitchenCircleView.Of([new InnCall(InnCallKind.BlazeNow), Order(3, A, 4), Ahead(1, D), new InnCall(InnCallKind.LastCall, Dish: Dish(E), Table: 2)], Text);
        Assert.Equal(InnKitchenCircleView.Slots, slots.Count);
        Assert.Equal([1, null, 3], slots.Select(s => s?.Kitchen));

        var middleOnly = InnKitchenCircleView.Of([Order(2, B, 1)], Text);
        Assert.Equal([null, 2, null], middleOnly.Select(s => s?.Kitchen));
        Assert.All(InnKitchenCircleView.Of([], Text), Assert.Null);
    }

    /// <summary>
    /// What a circle says, every word from i18n/zh-CN.xaml: the kitchen over the circle, the dish under it, and one line of
    /// why: the table that ordered it, cooking ahead, or what to take out first. The three kinds read apart at a glance: each
    /// its own ring colour and its own word next to the kitchen.
    /// </summary>
    [Fact]
    public void Order_cook_ahead_and_swap_read_apart()
    {
        var order = InnKitchenCircleView.Of(Order(1, A, 4), Text);
        var ahead = InnKitchenCircleView.Of(Ahead(2, D), Text);
        var swap = InnKitchenCircleView.Of(Swap(3, D, B, 5), Text);

        Assert.Equal((Text("Inn.Kitchen.1"), Dish(A).Zh, Format("Inn.Call.ForTable", 4), A), (order.Label, order.Dish, order.Reason, order.PictureId));
        Assert.Equal((Text("Inn.Kitchen.2"), Dish(D).Zh, Text("Inn.Circle.CookAheadReason")), (ahead.Label, ahead.Dish, ahead.Reason));
        Assert.Equal((Text("Inn.Kitchen.3"), Dish(B).Zh, Format("Inn.Circle.SwapReason", Dish(D).Zh, 5), B), (swap.Label, swap.Dish, swap.Reason, swap.PictureId));
        Assert.Equal([InnCircleKind.Order, InnCircleKind.CookAhead, InnCircleKind.Swap], new[] { order.Kind, ahead.Kind, swap.Kind });
        Assert.Equal(3, new[] { order.KindWord, ahead.KindWord, swap.KindWord }.Distinct().Count());
        Assert.Equal(3, new[] { InnCircleKind.Order, InnCircleKind.CookAhead, InnCircleKind.Swap }.Select(InnKitchenCircleView.ColorOf).Distinct().Count());
        // The key is the call's: a circle for the same call stays as it is.
        Assert.Equal(Order(1, A, 4).Key, order.Key);
    }

    /// <summary>The sample shown while the menu is open: three circles, one of each kind, one per slot.</summary>
    [Fact]
    public void The_sample_shows_three_circles_one_of_each_kind()
    {
        var sample = InnKitchenCircleView.Sample(Inn, Text);
        Assert.Equal([1, 2, 3], sample.Select(s => s?.Kitchen));
        Assert.Equal(3, sample.Select(s => s!.Kind).Distinct().Count());
        Assert.All(sample, s => Assert.NotNull(s!.PictureId));
    }

    /// <summary>
    /// Drawn: a circle already up for the same call is the same element (never drawn or animated again), each in its
    /// kitchen's column; a kitchen's call going empties its slot only.
    /// </summary>
    [Fact]
    public void A_circle_stays_as_it_is_in_its_slot_while_its_call_stands()
    {
        OnSta(() =>
        {
            var grid = new Grid();
            var row = new InnCircleRow(grid, m => InnKitchenCircleView.Circle(m, _ => null));
            var one = InnKitchenCircleView.Of(Order(1, A, 4), Text);
            var three = InnKitchenCircleView.Of(Ahead(3, D), Text);
            var two = InnKitchenCircleView.Of(Order(2, B, 1), Text);

            row.Update([one, null, three]);
            var first = row.CircleIn(1)!;
            var third = row.CircleIn(3)!;
            Assert.Null(row.CircleIn(2));
            Assert.Equal((0, 2), (Grid.GetColumn(first), Grid.GetColumn(third)));

            row.Update([one, two, three]);
            Assert.Same(first, row.CircleIn(1));
            Assert.Same(third, row.CircleIn(3));
            Assert.Equal(1, Grid.GetColumn(row.CircleIn(2)!));

            var middle = row.CircleIn(2);
            row.Update([null, two, three]);
            Assert.Null(row.CircleIn(1));
            Assert.Same(middle, row.CircleIn(2));
            Assert.Same(third, row.CircleIn(3));

            // Another call in kitchen 3: a new circle in the same column.
            row.Update([null, two, InnKitchenCircleView.Of(Order(3, C, 6), Text)]);
            Assert.NotSame(third, row.CircleIn(3));
            Assert.Equal(2, Grid.GetColumn(row.CircleIn(3)!));
            return 0;
        });
    }

    /// <summary>
    /// Readable at a glance: the dish's picture clearly bigger than the banner's (<see cref="InnCallView.PictureSize"/>), and
    /// no word smaller than the banner's title (20). The drawn circle fits its slot.
    /// </summary>
    [Fact]
    public void A_circles_picture_is_bigger_than_the_banners_and_its_words_as_large_as_the_banners_title()
    {
        var swap = InnKitchenCircleView.Of(Swap(3, LongestDish(0), LongestDish(1), 5), Text);
        var (picture, fonts, size) = OnSta(() =>
        {
            var element = InnKitchenCircleView.Circle(swap, PackagePicture);
            element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            element.Arrange(new Rect(element.DesiredSize));
            var image = Descendants(element).OfType<Image>().Single();
            return (image.ActualHeight, Descendants(element).OfType<TextBlock>().Select(t => t.FontSize).ToList(), element.DesiredSize);
        });
        output.WriteLine($"picture {picture:F0}, fonts {string.Join(",", fonts)}, circle {size.Width:F0}x{size.Height:F0}");

        Assert.True(picture >= 1.3 * InnCallView.PictureSize, $"the dish's picture is {picture:F0} units (the banner's {InnCallView.PictureSize})");
        Assert.NotEmpty(fonts);
        Assert.All(fonts, f => Assert.True(f >= 20, $"a word {f} units tall"));
        Assert.True(size.Width <= InnKitchenCircleView.SlotWidth, $"{size.Width:F0} wide in a {InnKitchenCircleView.SlotWidth} slot");
        Assert.True(size.Height <= InnKitchenCircleView.RowHeight, $"{size.Height:F0} tall in a {InnKitchenCircleView.RowHeight} row");
    }

    /// <summary>
    /// From their default place, at the player's default size: the row centred on the window, above the held-dishes tray
    /// (its slots from y 0.855 of a 16:9 window, the 4K fixtures), below the middle of the screen, inside the window.
    /// </summary>
    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(2560, 1440)]
    [InlineData(3840, 2160)]
    public void The_circles_stand_centred_just_above_the_held_dishes_tray(int width, int height)
    {
        double scale = ComboHintLayout.Scale(height, dpiScale: 1);
        var row = InnKitchenCircleLayout.Place(width, height, scale, InnKitchenCircleLayout.DefaultCenterX, InnKitchenCircleLayout.DefaultBottomY);
        output.WriteLine($"{width}x{height}: x {row.Left / width:F3}–{row.Right / width:F3}, y {row.Top / height:F3}–{row.Bottom / height:F3}");

        Assert.Equal(width / 2.0, row.Left + row.Width / 2, 1);
        Assert.True(row.Bottom <= 0.855 * height, $"bottom {row.Bottom / height:F3} of the window, the tray from 0.855");
        Assert.True(row.Bottom >= 0.80 * height, $"bottom {row.Bottom / height:F3}: right above the tray");
        Assert.True(row.Top >= 0.55 * height, $"top {row.Top / height:F3}");
        Assert.True(row.Left >= 0 && row.Right <= width);
        // The row is what's drawn: three slots and the frame, at that scale.
        Assert.Equal((InnKitchenCircleView.Slots * InnKitchenCircleView.SlotWidth + Frame) * scale, row.Width, 3);
    }

    // ---- Below the open dispatch panel ----

    /// <summary>
    /// While the panel is open the circles stand in a row just below it (owner, 2026-10-21, test_yuanyuan6 at 6:10: "the
    /// kitchen shows 有菜要排, BUT there is no circle under"): the panel spans y 0.194–0.853 of a 16:9 window on the 4K
    /// frames and covers the tray, the floor below it is free. The row is centred, under the panel's bottom, inside the
    /// window, and right of the UID in the lower-left corner (x up to 0.26 from y 0.94).
    /// </summary>
    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(2560, 1440)]
    [InlineData(3840, 2160)]
    public void Below_the_open_panel_the_circles_stand_under_it_inside_the_window(int width, int height)
    {
        double scale = ComboHintLayout.Scale(height, dpiScale: 1);
        var row = InnKitchenCircleLayout.BelowPanel(width, height, scale);
        output.WriteLine($"{width}x{height}: x {row.Left / width:F3}–{row.Right / width:F3}, y {row.Top / height:F3}–{row.Bottom / height:F3}");

        Assert.Equal(width / 2.0, row.Left + row.Width / 2, 1);
        Assert.True(row.Top >= 0.853 * height, $"top {row.Top / height:F3}, the panel down to 0.853");
        Assert.True(row.Bottom <= height, $"bottom {row.Bottom / height:F3}");
        Assert.True(row.Left >= 0.26 * width, $"left {row.Left / width:F3}, the UID up to 0.26");
        Assert.Equal((InnKitchenCircleView.Slots * InnKitchenCircleView.PanelSlotWidth + Frame) * scale, row.Width, 3);
    }

    /// <summary>The circle below the panel: the same picture (112) and words (20–22) beside it, inside its slot.</summary>
    [Fact]
    public void A_circle_below_the_panel_keeps_its_picture_and_words_and_fits_its_slot()
    {
        var swap = InnKitchenCircleView.Of(Swap(3, LongestDish(0), LongestDish(1), 5), Text);
        var (picture, fonts, size) = OnSta(() =>
        {
            var element = InnKitchenCircleView.PanelCircle(swap, PackagePicture);
            element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            element.Arrange(new Rect(element.DesiredSize));
            var image = Descendants(element).OfType<Image>().Single();
            return (image.ActualHeight, Descendants(element).OfType<TextBlock>().Select(t => t.FontSize).ToList(), element.DesiredSize);
        });
        output.WriteLine($"picture {picture:F0}, fonts {string.Join(",", fonts)}, circle {size.Width:F0}x{size.Height:F0}");
        Assert.Equal(InnKitchenCircleView.PictureSize, picture);
        Assert.All(fonts, f => Assert.True(f >= 20, $"a word {f} units tall"));
        Assert.True(size.Width <= InnKitchenCircleView.PanelSlotWidth, $"{size.Width:F0} wide");
        Assert.True(size.Height <= InnKitchenCircleView.PanelRowHeight, $"{size.Height:F0} tall");
    }

    /// <summary>
    /// The views while the panel is open: the circles below it, the selected tab's kitchen emphasised, the same calls the
    /// tabs and the card are marked from (one picture).
    /// </summary>
    [Fact]
    public void While_the_panel_is_open_the_circles_move_below_it_with_its_kitchen_emphasised()
    {
        var session = Prepared();
        var display = new InnDisplay();
        var tables = Tables(InnTestKit.Order(3, A));
        display.Show(session, Session(10, tables: tables), S(10));
        var closed = display.Views(session, S(10), callsEnabled: true, Text);
        Assert.Equal((InnCirclePlace.AboveTray, (int?)null), (closed.Circles, closed.SelectedKitchen));

        display.Show(session, Session(10.3, tables: tables, panel: Panel(2, 2, Menu)), S(10.3));
        var open = display.Views(session, S(10.3), callsEnabled: true, Text);
        Assert.Equal((InnCirclePlace.BelowPanel, (int?)2), (open.Circles, open.SelectedKitchen));
        Assert.Equal(closed.Calls.Select(c => c.Key), open.Calls.Select(c => c.Key));
    }

    // ---- Nothing over the open dispatch panel ----

    private static readonly string[] Menu = [A, B, C, D, E];

    private static InnSession Prepared()
    {
        var session = NewSession();
        session.Update(SetupScreen(0, 1, 2));
        session.Update(MenuScreen(1, Menu));
        return session;
    }

    /// <summary>
    /// Owner, live 2026-10-21 (test_yuanyuan5 at 02:25, 01:20, 00:09): banners drawn over the open dispatch panel. The
    /// circles and banners are put away on the first frame that reads the panel, not after it has read it twice: every
    /// frame read open in between showed them over it.
    /// </summary>
    [Fact]
    public void The_first_frame_reading_the_panel_puts_everything_away()
    {
        var session = Prepared();
        var display = new InnDisplay();
        var tables = Tables(InnTestKit.Order(3, A));
        display.Show(session, Session(10, tables: tables), S(10));
        var closed = display.Views(session, S(10), callsEnabled: true, Text);
        Assert.False(closed.Away);
        Assert.NotEmpty(closed.Calls);

        display.Show(session, Session(10.3, tables: tables, panel: Panel(1, 2, Menu)), S(10.3));
        Assert.True(display.Views(session, S(10.3), callsEnabled: true, Text).Away);
        // A tick between frames: still away.
        Assert.True(display.Views(session, S(10.5), callsEnabled: true, Text).Away);
    }

    /// <summary>
    /// Over the owner's two recorded sessions (Fixtures/inn-replay): no fresh frame that reads the dispatch panel open leaves
    /// the circles or banners up. Also said: how often they were hidden for under a second (a panel read for one frame).
    /// </summary>
    [Theory]
    [InlineData("test-yuanyuan3-readings.jsonl.gz")]
    [InlineData("test-yuanyuan4-readings.jsonl.gz")]
    public void Over_the_recorded_sessions_nothing_shows_on_a_frame_reading_the_panel(string fixture)
    {
        var events = InnReplay.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "inn-replay", fixture));
        var session = NewSession();
        var display = new InnDisplay();
        // The session's kitchens: a real panel shows a tab for each; a frame reading fewer is a misreading (a single frame
        // between frames showing the Full Blaze meter the panel would cover, on test_yuanyuan3 48 times).
        int tabs = events.Where(x => x.Reading is { Fresh: true, Panel: not null }).Max(x => x.Reading!.Panel!.Kitchens);
        int panelFrames = 0, shownOver = 0, fewerTabs = 0, shownOverFewer = 0, shortHides = 0;
        TimeSpan? awaySince = null;
        foreach (var e in events)
        {
            display.Show(session, e.Reading, e.At);
            var views = display.Views(session, e.At, callsEnabled: true, Text);
            bool shown = !views.Away && (views.Calls.Count > 0 || views.Wants.Count > 0);
            if (e.Reading is { Fresh: true, InSession: true, Panel: { } panel })
            {
                if (panel.Kitchens == tabs)
                {
                    panelFrames++;
                    if (shown) shownOver++;
                }
                else
                {
                    fewerTabs++;
                    if (shown) shownOverFewer++;
                }
            }
            if (views.Away) awaySince ??= e.At;
            else if (awaySince is { } since)
            {
                if (e.At - since < TimeSpan.FromSeconds(1)) shortHides++;
                awaySince = null;
            }
        }
        output.WriteLine($"{fixture}: {panelFrames} frames read the panel with its {tabs} tabs, shown over it on {shownOver}; "
                         + $"{fewerTabs} read fewer tabs, shown on {shownOverFewer}; put away for under a second {shortHides} times");
        Assert.True(panelFrames > 50);
        Assert.Equal(0, shownOver);
        // Not put away and back on every misread frame (that would blink everything off and on).
        Assert.True(shortHides <= 2, $"{shortHides} times put away for under a second");
    }

    // ---- Drawn over a real frame ----

    /// <summary>
    /// The circles and the gourmands' tiles drawn where they go over a real 4K frame (the UID corner masked), saved as a
    /// picture for a person to look at (.tracking/changes/2026-10-21-inn-y6-…png, local only; nothing is asserted of it but
    /// that it was drawn): a session (session-two-dishes.jpg, the circles above the tray, the tiles on the right) and the
    /// open dispatch panel (dispatch-panel-queue.jpg, the circles below it with kitchen 1's emphasised, the tiles beside
    /// it), with 1, 3 and 6 gourmands.
    /// </summary>
    [Theory]
    [InlineData("session-two-dishes.jpg", false, 1)]
    [InlineData("session-two-dishes.jpg", false, 3)]
    [InlineData("session-two-dishes.jpg", false, 6)]
    [InlineData("dispatch-panel-queue.jpg", true, 1)]
    [InlineData("dispatch-panel-queue.jpg", true, 3)]
    [InlineData("dispatch-panel-queue.jpg", true, 6)]
    public void The_circles_and_gourmands_drawn_over_a_4K_frame(string frameName, bool panelOpen, int gourmands)
    {
        string frame = Path.Combine(AppContext.BaseDirectory, "Fixtures", "inn", frameName);
        if (!File.Exists(frame)) return; // the test data submodule isn't there
        string saved = Path.Combine(RepositoryRoot(), ".tracking", "changes", $"2026-10-21-inn-y6-{(panelOpen ? "panel" : "session")}-{gourmands}-gourmands.png");
        var circles = InnKitchenCircleView.Of([Order(1, A, 4), Ahead(2, D), Swap(3, D, B, 5)], Text);
        var tiles = InnGourmandView.Banners([.. SixGourmandsWants().Where(w => w.Table <= gourmands)], Text);

        OnSta(() =>
        {
            var background = new BitmapImage(new Uri(frame));
            int w = background.PixelWidth, h = background.PixelHeight;
            double scale = ComboHintLayout.Scale(h, dpiScale: 1);
            var canvas = new Canvas { Width = w, Height = h, Background = new ImageBrush(background) };

            var place = panelOpen ? InnKitchenCircleLayout.BelowPanel(w, h, scale)
                : InnKitchenCircleLayout.Place(w, h, scale, InnKitchenCircleLayout.DefaultCenterX, InnKitchenCircleLayout.DefaultBottomY);
            var grid = panelOpen ? InnCircleRow.PanelShape(new Grid()) : InnCircleRow.NewGrid();
            var row = new InnCircleRow(grid, m => panelOpen ? InnKitchenCircleView.PanelCircle(m, PackagePicture) : InnKitchenCircleView.Circle(m, PackagePicture));
            row.Update(circles);
            row.Settle();
            if (panelOpen) row.Emphasise(1);
            var circleFrame = new Border { Padding = new Thickness(6), BorderThickness = new Thickness(2), Child = grid, LayoutTransform = new ScaleTransform(scale, scale) };
            Canvas.SetLeft(circleFrame, place.Left);
            Canvas.SetTop(circleFrame, place.Top);
            canvas.Children.Add(circleFrame);

            var arrangement = InnGourmandLayout.Arrange(tiles, w, h, scale, HudConfig.DefaultInnGourmandRight, HudConfig.DefaultInnGourmandTop, panelOpen, place);
            for (int i = 0; i < tiles.Count; i++)
            {
                var tile = InnGourmandView.Tile(tiles[i], PackagePicture, arrangement.Shape);
                tile.LayoutTransform = new ScaleTransform(arrangement.Scale, arrangement.Scale);
                Canvas.SetLeft(tile, arrangement.Places[i].X);
                Canvas.SetTop(tile, arrangement.Places[i].Y);
                canvas.Children.Add(tile);
            }

            canvas.Measure(new Size(w, h));
            canvas.Arrange(new Rect(0, 0, w, h));
            canvas.UpdateLayout();
            var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(canvas);
            Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
            using var file = File.Create(saved);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(bitmap));
            png.Save(file);
            return 0;
        });
        output.WriteLine(saved);
        Assert.True(new FileInfo(saved).Length > 100_000);
    }

    // ---- Six gourmands ----

    /// <summary>Six gourmands, tables 6 down to 1 (given out of order), each asking for a dish and a drink.</summary>
    internal static IReadOnlyList<InnWant> SixGourmandsWants()
    {
        var dishes = Inn.Dishes.OrderByDescending(d => d.Zh.Length).ThenBy(d => d.Id).ToList();
        var drinks = Inn.Drinks.OrderByDescending(d => d.Zh.Length).ThenBy(d => d.Id).ToList();
        return [.. Enumerable.Range(1, 6).Reverse().SelectMany(table => new[]
        {
            new InnWant(table, 0, "清爽无油的汤羹", dishes[table].Id, dishes[table].Zh),
            new InnWant(table, 1, Format("Inn.Gourmand.Drink", "不带酒的"), drinks[table % drinks.Count].Id, drinks[table % drinks.Count].Zh),
        })];
    }

    private static string LongestDish(int rank) => Inn.Dishes.OrderByDescending(d => d.Zh.Length).ThenBy(d => d.Id).ElementAt(rank).Id;

    internal static BitmapSource? PackagePicture(string id)
    {
        string path = InnData.PicturePath(Folder, id);
        if (!File.Exists(path)) return null;
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(path);
        image.EndInit();
        image.Freeze();
        return image;
    }

    internal static T OnSta<T>(Func<T> work)
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

    internal static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var deeper in Descendants(child)) yield return deeper;
        }
    }
}
