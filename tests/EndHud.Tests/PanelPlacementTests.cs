using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using Xunit;

namespace EndHud.Tests;

/// <summary>Where a dragged panel (the menu, the debug panel) ends up: moved by the drag, but never off the game window.</summary>
public class PanelPlacementTests
{
    private static readonly Size Window = new(1920, 1080);
    // The menu at its default spot: centred, 480 wide, 600 tall.
    private static readonly Rect Menu = new(720, 240, 480, 600);

    [Fact]
    public void A_drag_inside_the_window_moves_the_panel_by_the_drag()
    {
        Assert.Equal(new Vector(-300, 100), PanelPlacement.Clamp(Menu, new Vector(-300, 100), Window));
    }

    [Fact]
    public void Dragged_past_an_edge_it_stops_at_that_edge()
    {
        Assert.Equal(new Vector(-720, -240), PanelPlacement.Clamp(Menu, new Vector(-5000, -5000), Window));
        Assert.Equal(new Vector(720, 240), PanelPlacement.Clamp(Menu, new Vector(5000, 5000), Window));
    }

    [Fact]
    public void A_panel_bigger_than_the_window_keeps_its_top_left_corner_on_screen()
    {
        var tall = new Rect(720, -100, 480, 1300); // a menu taller than a small game window

        Assert.Equal(new Vector(0, 100), PanelPlacement.Clamp(tall, new Vector(0, 400), Window));
    }

    [Fact]
    public void A_saved_offset_follows_the_window_size()
    {
        var saved = PanelPlacement.ToFraction(new Vector(-300, 120), Window);

        var restored = PanelPlacement.FromFraction(saved, new Size(1280, 720));

        Assert.Equal(-200, restored.X, 9);
        Assert.Equal(80, restored.Y, 9);
    }

    /// <summary>Pressing a control keeps it working; pressing text or empty space drags the panel.</summary>
    [Fact]
    public void Only_presses_off_the_controls_drag_the_panel()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var label = new TextBlock { Text = "label" };
                var checkLabel = new TextBlock { Text = "check" };
                var check = new CheckBox { Content = checkLabel };
                var run = new Run("button");
                var button = new Button { Content = new TextBlock(run) };
                var list = new StackPanel { Height = 2000 };
                var scroll = new ScrollViewer { Content = list, Height = 200, VerticalScrollBarVisibility = ScrollBarVisibility.Visible };
                var panel = new Border { Child = new StackPanel { Children = { label, check, button, scroll } } };
                panel.Measure(new Size(400, 600));
                panel.Arrange(new Rect(0, 0, 400, 600));
                panel.UpdateLayout();
                var scrollBar = Descendants(scroll).OfType<ScrollBar>().First(b => b.Orientation == Orientation.Vertical);

                Assert.False(PanelPlacement.IsOnControl(label, panel));
                Assert.False(PanelPlacement.IsOnControl(panel, panel));
                Assert.False(PanelPlacement.IsOnControl(list, panel)); // empty space inside a scrolling area
                Assert.True(PanelPlacement.IsOnControl(checkLabel, panel));
                Assert.True(PanelPlacement.IsOnControl(run, panel)); // text inside a button
                Assert.True(PanelPlacement.IsOnControl(Descendants(scrollBar).First(), panel));
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw failure;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var d in Descendants(child)) yield return d;
        }
    }
}
