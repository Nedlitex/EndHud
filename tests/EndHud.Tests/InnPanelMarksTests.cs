using System.Windows;
using System.Windows.Controls;
using EndHud.Vision;
using EndHud.Vision.Inn;
using Xunit;
using static EndHud.Tests.InnTestKit;

namespace EndHud.Tests;

/// <summary>
/// The open dispatch panel's marks (<see cref="InnPanelMarks"/>; owner, live 2026-10-20: "is it possible to ALSO have a
/// suggestion marker by the dish itself on the right side?") and the banners put away while it's open ("banners must
/// not cover the 派单 panel").
/// </summary>
public class InnPanelMarksTests
{
    // The panel of the owner's 2026-10-20 frame dispatch-panel-banners.jpg: kitchen 2 of 3, six cards.
    private static readonly string[] Cards = ["roll-of-fin-supreme", "melon-meatball-soup", "berry-sweet-sip", "berry-fruity-toss", "spoon-swimmer", "fin-mato-soup"];

    private static DispatchPanel KitchenTwo(params int[] unread) => new(2, 3, [null, null], [null, null], null,
        [.. Cards.Select((id, i) => new KitchenDish(unread.Contains(i) ? null : id, Dish(id).Zh, Dish(id).CookSeconds) { Card = i })],
        [.. Enumerable.Repeat(new QueueSlot(false, null, null), KitchenQueue.Slots)]);

    [Fact]
    public void The_dish_called_for_the_kitchen_shown_is_marked_on_its_card()
    {
        var calls = new[] { new InnCall(InnCallKind.Queue, 2, Dish("berry-sweet-sip"), 4) };

        var mark = Assert.Single(InnPanelMarks.Of(KitchenTwo(), calls, Text));

        Assert.True(mark.OnCard);
        Assert.Equal(DispatchPanelReader.CardArea(2), mark.Area);
        Assert.Equal("排这个（桌4）", mark.Label);
    }

    [Fact]
    public void A_dish_to_cook_ahead_and_a_swap_say_so()
    {
        Assert.Equal("预做这个", Assert.Single(InnPanelMarks.Of(KitchenTwo(), [new InnCall(InnCallKind.CookAhead, 2, Dish("roll-of-fin-supreme"))], Text)).Label);
        var swap = new InnCall(InnCallKind.Swap, 2, Dish("fin-mato-soup"), 3, Remove: Dish("spoon-swimmer"));
        Assert.Equal($"先拿掉{Dish("spoon-swimmer").Zh}，再排这个", Assert.Single(InnPanelMarks.Of(KitchenTwo(), [swap], Text)).Label);
    }

    /// <summary>"Only when that card is identified on screen; nothing when unsure."</summary>
    [Fact]
    public void Nothing_is_marked_when_the_dishs_card_was_not_read_as_that_dish()
    {
        var calls = new[] { new InnCall(InnCallKind.Queue, 2, Dish("berry-sweet-sip"), 4) };

        Assert.Empty(InnPanelMarks.Of(KitchenTwo(unread: 2), calls, Text));
        Assert.Empty(InnPanelMarks.Of(KitchenTwo(), [new InnCall(InnCallKind.Queue, 2, Dish("meat-baozi"), 4)], Text)); // not on the panel
        Assert.Empty(InnPanelMarks.Of(null, calls, Text));
    }

    [Fact]
    public void Another_kitchens_call_marks_its_tab_and_not_a_card()
    {
        var calls = new[] { new InnCall(InnCallKind.Queue, 1, Dish("berry-sweet-sip"), 4), new InnCall(InnCallKind.BlazeNow) };

        var mark = Assert.Single(InnPanelMarks.Of(KitchenTwo(), calls, Text));

        Assert.False(mark.OnCard);
        Assert.Equal(DispatchPanelReader.TabArea(1), mark.Area);
        Assert.Equal("有菜要排", mark.Label);
    }

    /// <summary>A mark is drawn on its card's place in the window: the card measured on the 4K frames, at every size.</summary>
    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(2560, 1440)]
    [InlineData(3840, 2160)]
    public void A_cards_mark_is_placed_on_the_card(int width, int height)
    {
        var mark = Assert.Single(InnPanelMarks.Of(KitchenTwo(), [new InnCall(InnCallKind.CookAhead, 2, Dish("roll-of-fin-supreme"))], Text));

        var place = InnPanelMarks.Place(mark.Area, width, height);

        // The first card's face at 4K: x 1468–2364, y 630–888 (dispatch-panel-queue.jpg).
        double scale = width / 3840.0;
        Assert.Equal(1468 * scale, place.Left, 2 * scale + 0.5);
        Assert.Equal(630 * scale, place.Top, 2 * scale + 0.5);
        Assert.Equal(2364 * scale, place.Right, 2 * scale + 0.5);
        Assert.Equal(888 * scale, place.Bottom, 2 * scale + 0.5);
    }

    [Fact]
    public void While_the_panel_is_open_no_banner_shows_and_the_marks_take_over()
    {
        var session = NewSession();
        session.Update(SetupScreen(0, 1, 2, 3));
        session.Update(MenuScreen(1, Cards));

        session.Update(Session(10));
        var closed = session.Advice(S(10))!;
        Assert.NotEmpty(closed.BannerCalls);
        Assert.Null(closed.Panel);

        // Read open on two frames in a row (InnSession.PanelFramesToOpen).
        session.Update(Session(10.7, panel: KitchenTwo()));
        Assert.NotEmpty(session.Advice(S(10.7))!.BannerCalls);
        session.Update(Session(11, panel: KitchenTwo()));
        var open = session.Advice(S(11))!;
        Assert.Empty(open.BannerCalls);
        Assert.NotEmpty(open.Calls);
        var marks = InnPanelMarks.Of(open.Panel, open.Calls, Text);
        var call = open.Calls.Single(c => c.Kitchen == 2);
        Assert.Contains(marks, m => m.OnCard && m.Area == DispatchPanelReader.CardArea(Array.IndexOf(Cards, call.Dish!.Id)));
        Assert.Equal(2, marks.Count(m => !m.OnCard)); // kitchens 1 and 3

        // Read closed: the banners come back once no frame has read the panel for InnSession.PanelClosedAfter.
        session.Update(Session(12));
        Assert.Empty(session.Advice(S(12))!.BannerCalls);
        session.Update(Session(11 + InnSession.PanelClosedAfter.TotalSeconds));
        Assert.NotEmpty(session.Advice(S(11 + InnSession.PanelClosedAfter.TotalSeconds))!.BannerCalls);
    }
}

/// <summary>The marks as drawn (<see cref="InnPanelMarks.Element"/>): the outline just outside its card, the longest label inside the card's blank lower right, at every size.</summary>
[Collection(WpfCollection.Name)]
public class InnPanelMarkLayoutTests
{
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

    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(2560, 1440)]
    [InlineData(3840, 2160)]
    public void A_cards_outline_hugs_the_card_and_the_longest_label_fits_inside_it(int width, int height)
    {
        var longest = Inn.Dishes.OrderByDescending(d => d.Zh.Length).First();
        var mark = new InnPanelMark(DispatchPanelReader.CardArea(7), string.Format(Text("Inn.Mark.Swap"), longest.Zh), OnCard: true);
        double scale = ComboHintLayout.Scale(height, dpiScale: 1);
        var card = InnPanelMarks.Place(mark.Area, width, height);

        var (bounds, label) = OnSta(() =>
        {
            var element = InnPanelMarks.Element(mark, width, height, scale);
            element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            element.Arrange(new Rect(element.DesiredSize));
            var tag = ((Grid)element).Children.OfType<Border>().Single(b => b.Child is TextBlock);
            return (new Rect(Canvas.GetLeft(element), Canvas.GetTop(element), element.ActualWidth, element.ActualHeight), tag.DesiredSize);
        });

        double gap = InnPanelMarks.OutlineWidth * scale;
        Assert.Equal(card.Left - gap, bounds.Left, 3);
        Assert.Equal(card.Width + 2 * gap, bounds.Width, 3);
        Assert.Equal(card.Height + 2 * gap, bounds.Height, 3);
        // In the card's blank lower right: clear of the stock ("可烹饪N", the card's left fifth) and of its name (top third).
        Assert.True(label.Width <= card.Width * 0.75, $"the label is {label.Width:F0} of the card's {card.Width:F0} units");
        Assert.True(label.Height <= card.Height * 0.3, $"the label is {label.Height:F0} of the card's {card.Height:F0} units tall");
    }
}

/// <summary>
/// The same card mark on the setup screens (owner, live 2026-10-20: "in the choose dish and choose cook menu, show the
/// same suggestion like the suggestion in session for cooking", <see cref="InnSetupMarks"/>): on the menu screen each
/// recommended dish's and drink's card in view, in a chef picker each recommended chef's card in view; only cards read
/// for sure.
/// </summary>
public class InnSetupMarksTests
{
    private static MenuCard MenuCard(string? id, int x, int y, bool inView = true) =>
        new(id, id ?? "?", OnMenu: false, Unlimited: true, Stock: null) { Area = inView ? new PixelRect(x, y, 898, 257) : null };

    private static ChefCard ChefCard(string? id, int x, int y) =>
        new(default, [ChefIcon.Entree, ChefIcon.Side], "Salty", id is null ? [] : [id], id) { Area = new PixelRect(x, y, 240, 398) };

    [Fact]
    public void On_the_menu_screen_each_recommended_card_in_view_is_marked()
    {
        IReadOnlyList<MenuCard> cards =
        [
            MenuCard("melon-meatball-soup", 1667, 412), MenuCard("berry-fruity-toss", 2625, 412),
            MenuCard("spoon-swimmer", 2625, 729), MenuCard(null, 1667, 1045), MenuCard("fin-mato-soup", 1667, 1362, inView: false),
        ];

        var marks = InnSetupMarks.Of(InnScreenKind.Menu, ["melon-meatball-soup", "spoon-swimmer", "fin-mato-soup", "pre-tide-lungjing"], cards, [], Text);

        Assert.Equal([new PixelRect(1667, 412, 898, 257), new PixelRect(2625, 729, 898, 257)], marks.Select(m => m.Area));
        Assert.All(marks, m => Assert.True(m.OnCard));
        Assert.All(marks, m => Assert.Equal("推荐", m.Label));
    }

    [Fact]
    public void In_a_chef_picker_each_recommended_chefs_card_in_view_is_marked_and_no_unnamed_card()
    {
        IReadOnlyList<ChefCard> cards = [ChefCard("Lifeng", 109, 272), ChefCard(null, 379, 272), ChefCard("Yvonne", 650, 272), ChefCard("Akekuri", 109, 698)];

        var marks = InnSetupMarks.Of(InnScreenKind.ChefPicker, ["Yvonne", "Akekuri", "Perlica"], [], cards, Text);

        Assert.Equal([new PixelRect(650, 272, 240, 398), new PixelRect(109, 698, 240, 398)], marks.Select(m => m.Area));
        Assert.All(marks, m => Assert.Equal("推荐", m.Label));
    }

    [Fact]
    public void Nothing_is_marked_on_other_screens_or_with_nothing_recommended()
    {
        IReadOnlyList<MenuCard> menu = [MenuCard("melon-meatball-soup", 1667, 412)];
        IReadOnlyList<ChefCard> chefs = [ChefCard("Yvonne", 650, 272)];

        Assert.Empty(InnSetupMarks.Of(InnScreenKind.Setup, ["melon-meatball-soup", "Yvonne"], menu, chefs, Text));
        Assert.Empty(InnSetupMarks.Of(null, ["melon-meatball-soup", "Yvonne"], menu, chefs, Text));
        Assert.Empty(InnSetupMarks.Of(InnScreenKind.Menu, [], menu, chefs, Text));
        // The menu's marks never come from the picker's cards, nor the picker's from the menu's.
        Assert.Empty(InnSetupMarks.Of(InnScreenKind.Menu, ["Yvonne"], menu, chefs, Text));
        Assert.Empty(InnSetupMarks.Of(InnScreenKind.ChefPicker, ["melon-meatball-soup"], menu, chefs, Text));
    }
}

/// <summary>
/// The setup screens' cards as the display keeps them for the marks (<see cref="InnDisplay"/>): the menu screen and the
/// chef picker are read every half second, so a frame between reads (or a read that found no card) keeps the cards
/// last read; another screen, or leaving the inn, forgets them, so no mark is left on a screen it isn't about.
/// </summary>
public class InnSetupCardsTests
{
    private static InnReading Menu(double at, params MenuCard[] cards) =>
        new(InnScreenKind.Menu, S(at), Menu: new InnMenu(MenuTab.AllDishes, null, null, cards));

    private static readonly MenuCard Soup = new("melon-meatball-soup", "?", false, true, null) { Area = new PixelRect(1667, 412, 898, 257) };

    [Fact]
    public void The_menus_cards_are_kept_between_reads_and_forgotten_on_another_screen()
    {
        var display = new InnDisplay();
        display.Show(null, Menu(0, Soup), S(0));
        Assert.Equal([Soup], display.MenuCards);

        display.Show(null, new InnReading(InnScreenKind.Menu, S(0.3), Fresh: false), S(0.3));
        display.Show(null, Menu(0.5), S(0.5));
        Assert.Equal([Soup], display.MenuCards);

        display.Show(null, new InnReading(InnScreenKind.Setup, S(1)), S(1));
        Assert.Empty(display.MenuCards);
        display.Show(null, Menu(1.5, Soup), S(1.5));
        display.Show(null, null, S(2));
        Assert.Empty(display.MenuCards);
    }

    [Fact]
    public void The_pickers_cards_are_kept_between_reads_and_forgotten_on_another_screen()
    {
        var yvonne = new ChefCard(default, [ChefIcon.Dessert, ChefIcon.Dessert], "Savory", ["Yvonne"], "Yvonne") { Area = new PixelRect(650, 272, 240, 398) };
        var display = new InnDisplay();
        display.Show(null, new InnReading(InnScreenKind.ChefPicker, S(0)) { PickerCards = [yvonne] }, S(0));
        display.Show(null, new InnReading(InnScreenKind.ChefPicker, S(0.3), Fresh: false), S(0.3));
        Assert.Equal([yvonne], display.PickerCards);

        display.Show(null, Menu(1), S(1));
        Assert.Empty(display.PickerCards);
    }
}
