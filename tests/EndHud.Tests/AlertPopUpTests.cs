using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Xunit;

namespace EndHud.Tests;

/// <summary>
/// The alert pop-up's shape (owner, 2026-10-22: the combo hint's mirror image): the circle on the right,
/// the text panel out to its left, a long text wrapped rather than running off, at the combo banner's height.
/// </summary>
[Collection(WpfCollection.Name)]
public class AlertPopUpTests
{
    private static T OnSta<T>(Func<T> act)
    {
        T result = default!;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { result = act(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw failure;
        return result;
    }

    /// <summary>The pop-up laid out alone: its width, the circle's left and right, the panel's left, the text's width and height.</summary>
    private static (double Width, double CircleLeft, double CircleRight, double PanelLeft, double TextWidth, double TextHeight) Layout(string text) => OnSta(() =>
    {
        var popUp = AlertPopUp.Create(AlertPopUp.Icon(null, skillPoints: true), text);
        popUp.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        popUp.Arrange(new Rect(popUp.DesiredSize));
        popUp.UpdateLayout();
        var circle = popUp.Children.OfType<Grid>().Single(g => g.Children.OfType<Ellipse>().Any());
        var panel = popUp.Children.OfType<Border>().Single();
        var words = (TextBlock)panel.Child;
        double circleLeft = circle.TranslatePoint(new Point(0, 0), popUp).X;
        return (popUp.ActualWidth, circleLeft, circleLeft + circle.ActualWidth, panel.TranslatePoint(new Point(0, 0), popUp).X, words.ActualWidth, words.ActualHeight);
    });

    [Fact]
    public void The_circle_is_on_the_right_and_the_text_to_its_left()
    {
        var at = Layout("Cast now");

        Assert.Equal(at.Width, at.CircleRight, 1);
        Assert.Equal(AlertPopUp.CircleSize, at.CircleRight - at.CircleLeft, 1);
        Assert.True(at.PanelLeft < at.CircleLeft - 40, $"panel from {at.PanelLeft}, circle from {at.CircleLeft}");
    }

    [Fact]
    public void A_long_text_wraps_within_the_panel()
    {
        var at = Layout(string.Join(" ", Enumerable.Repeat("Armor break window now", 4)));

        Assert.True(at.TextWidth <= AlertPopUp.TextMaxWidth + 0.5, $"text {at.TextWidth} wide");
        Assert.True(at.TextHeight > 30, $"text {at.TextHeight} tall: not wrapped"); // two lines
        Assert.True(at.TextHeight <= 56.5, $"text {at.TextHeight} tall: more than two lines");
    }
}
