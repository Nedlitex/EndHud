using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using EndHud.Vision;
using EndHud.Vision.Inn;

namespace EndHud;

/// <summary>A mark drawn on the open dispatch panel.</summary>
/// <param name="Area">What it marks, at 4K on the window's centred 16:9 area (<see cref="DispatchPanelReader.CardArea"/>, <see cref="DispatchPanelReader.TabArea"/>).</param>
/// <param name="Label">Its words: what to do with the card, or that the tab's kitchen has something to queue.</param>
/// <param name="OnCard">A dish's card (an outline and a label), else a kitchen's tab (a small label).</param>
internal sealed record InnPanelMark(PixelRect Area, string Label, bool OnCard)
{
    /// <summary>Who it is: the same key, drawn as it is.</summary>
    public string Key => $"{Area.X},{Area.Y},{Area.Width},{Area.Height}:{OnCard}:{Label}";
}

/// <summary>
/// What's marked on the open dispatch panel (owner, live 2026-10-20: "is it possible to ALSO have a suggestion marker
/// by the dish itself on the right side?"; the banners must not cover the dispatch panel, so while it's open they're put
/// away and the marks take over, <see cref="InnAdvice.BannerCalls"/>):
/// <list type="bullet">
/// <item>the card of the dish called for the kitchen the panel shows, outlined, with "queue this (table 3)", "cook this
/// ahead" or "take out X, then queue this" (i18n Inn.Mark.Queue, CookAhead, Swap): only when that card was read as that
/// dish (<see cref="KitchenDish.Card"/>); nothing when unsure;</item>
/// <item>the tab of every other kitchen with a dish called, "a dish to queue" (Inn.Mark.Tab), so the player knows to
/// switch to it.</item>
/// </list>
/// Full Blaze and the last call aren't marked: they wait for the panel to close. Every string from i18n/zh-CN.xaml.
/// </summary>
internal static class InnPanelMarks
{
    /// <param name="text">The i18n lookup (<see cref="Loc.Get"/>; the tests pass zh-CN.xaml's strings).</param>
    public static IReadOnlyList<InnPanelMark> Of(DispatchPanel? panel, IReadOnlyList<InnCall> calls, Func<string, string> text)
    {
        if (panel is null) return [];
        var marks = new List<InnPanelMark>();
        var kitchenCalls = calls.Where(c => c.Kind is InnCallKind.Queue or InnCallKind.CookAhead or InnCallKind.Swap && c.Kitchen is not null && c.Dish is not null).ToList();
        if (kitchenCalls.FirstOrDefault(c => c.Kitchen == panel.Kitchen) is { } call
            && panel.Dishes.FirstOrDefault(d => d.DishId == call.Dish!.Id) is { } card
            && card.Card is >= 0 and < DispatchPanelReader.Cards)
        {
            string label = call.Kind switch
            {
                InnCallKind.Queue => string.Format(text("Inn.Mark.Queue"), call.Table),
                InnCallKind.CookAhead => text("Inn.Mark.CookAhead"),
                _ => string.Format(text("Inn.Mark.Swap"), call.Remove!.Zh),
            };
            marks.Add(new InnPanelMark(DispatchPanelReader.CardArea(card.Card), label, OnCard: true));
        }
        foreach (int kitchen in kitchenCalls.Select(c => c.Kitchen!.Value).Where(k => k != panel.Kitchen && k <= panel.Kitchens).Distinct().Order())
            marks.Add(new InnPanelMark(DispatchPanelReader.TabArea(kitchen), text("Inn.Mark.Tab"), OnCard: false));
        return marks;
    }

    /// <summary>
    /// A mark's area in a game window of <paramref name="width"/> × <paramref name="height"/> (the overlay's units): the
    /// panel is laid out on the window's centred 16:9 area, scaled with it (as the readers place it, InnLayout).
    /// </summary>
    public static Rect Place(PixelRect area, double width, double height)
    {
        double scale = Math.Min(width / UiScaler.ReferenceWidth, height / UiScaler.ReferenceHeight);
        double left = (width - UiScaler.ReferenceWidth * scale) / 2, top = (height - UiScaler.ReferenceHeight * scale) / 2;
        return new Rect(left + area.X * scale, top + area.Y * scale, area.Width * scale, area.Height * scale);
    }

    /// <summary>The outline around a card and the labels' text, at 1080p (scaled with the window and the player's size).</summary>
    internal const double OutlineWidth = 4, LabelFontSize = 18;

    private static readonly FontFamily Font = new("Microsoft YaHei UI, Segoe UI");

    /// <summary>
    /// A mark as drawn over the game, placed on the overlay's canvas (<paramref name="width"/> × <paramref name="height"/>):
    /// a card's mark is a glowing gold outline just outside the card with its label in the card's blank lower right; a
    /// tab's is a small gold label on the tab's top edge. Never hit-tested (the game's clicks go through).
    /// </summary>
    /// <param name="scale">The inn's size: the window's (ComboHintLayout) times the player's.</param>
    public static FrameworkElement Element(InnPanelMark mark, double width, double height, double scale)
    {
        var area = Place(mark.Area, width, height);
        var label = new Border
        {
            Background = ComboBanner.Accent,
            CornerRadius = new CornerRadius(4 * scale),
            Padding = new Thickness(8 * scale, 2 * scale, 8 * scale, 3 * scale),
            Child = new TextBlock
            {
                Text = mark.Label,
                FontFamily = Font,
                FontSize = LabelFontSize * scale,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.Black,
            },
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 6 * scale, ShadowDepth = 0, Opacity = 0.7 },
        };
        if (!mark.OnCard)
        {
            // On the tab's top edge, centred.
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var size = label.DesiredSize;
            label.IsHitTestVisible = false;
            Canvas.SetLeft(label, area.Left + (area.Width - size.Width) / 2);
            Canvas.SetTop(label, area.Top - size.Height / 2);
            return label;
        }
        double gap = OutlineWidth * scale;
        var outline = new Border
        {
            BorderBrush = ComboBanner.Accent,
            BorderThickness = new Thickness(OutlineWidth * scale),
            CornerRadius = new CornerRadius(8 * scale),
            Effect = new DropShadowEffect { Color = Color.FromRgb(0xE6, 0xC2, 0x29), BlurRadius = 18 * scale, ShadowDepth = 0, Opacity = 1 },
        };
        label.HorizontalAlignment = HorizontalAlignment.Right;
        label.VerticalAlignment = VerticalAlignment.Bottom;
        label.Margin = new Thickness(0, 0, gap + 6 * scale, gap + 6 * scale);
        var mark2 = new Grid
        {
            Width = area.Width + 2 * gap,
            Height = area.Height + 2 * gap,
            IsHitTestVisible = false,
            Children = { outline, label },
        };
        Canvas.SetLeft(mark2, area.Left - gap);
        Canvas.SetTop(mark2, area.Top - gap);
        return mark2;
    }
}
