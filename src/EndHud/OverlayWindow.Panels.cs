using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace EndHud;

/// <summary>
/// The menu and the debug panel can be dragged while the menu is open (the overlay only takes the
/// mouse then), by any part that isn't a control. Where they were left is saved (see
/// <see cref="PanelPlacement"/>).
/// </summary>
public partial class OverlayWindow
{
    private FrameworkElement? _draggedPanel;
    private Point _dragStart;
    private Vector _dragStartOffset;

    private FrameworkElement Area => (FrameworkElement)Content;

    private void SetPanelDragging(bool editing)
    {
        DebugPanelDragLabel.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        StatusPanel.Cursor = editing ? Cursors.SizeAll : null;
        PlacePanelsFromConfig();
    }

    private void PlacePanelsFromConfig()
    {
        var window = new Size(Area.ActualWidth, Area.ActualHeight);
        PlacePanel(MenuPanel, PanelPlacement.FromFraction(new Vector(_config.MenuOffsetX, _config.MenuOffsetY), window));
        PlacePanel(StatusPanel, PanelPlacement.FromFraction(new Vector(_config.DebugOffsetX, _config.DebugOffsetY), window));
    }

    private static TranslateTransform Offset(FrameworkElement panel) => (TranslateTransform)panel.RenderTransform;

    /// <summary>Move <paramref name="panel"/> by <paramref name="wanted"/> from its usual spot, as far as the window allows.</summary>
    private void PlacePanel(FrameworkElement panel, Vector wanted)
    {
        if (!panel.IsVisible || Area.ActualWidth <= 0 || Area.ActualHeight <= 0) return;
        var offset = Offset(panel);
        // Where the panel's layout puts it, without the offset.
        var at = panel.TransformToAncestor(Area).Transform(new Point(0, 0)) - new Vector(offset.X, offset.Y);
        var home = new Rect(at, new Size(panel.ActualWidth, panel.ActualHeight));
        var placed = PanelPlacement.Clamp(home, wanted, new Size(Area.ActualWidth, Area.ActualHeight));
        offset.X = placed.X;
        offset.Y = placed.Y;
    }

    private void DraggablePanel_SizeChanged(object sender, SizeChangedEventArgs e) => PlacePanelsFromConfig();

    private void DraggablePanel_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var panel = (FrameworkElement)sender;
        if (!_menuOpen || PanelPlacement.IsOnControl(e.OriginalSource as DependencyObject, panel)) return;
        _draggedPanel = panel;
        _dragStart = e.GetPosition(Area);
        var offset = Offset(panel);
        _dragStartOffset = new Vector(offset.X, offset.Y);
        panel.CaptureMouse();
        e.Handled = true;
    }

    private void DraggablePanel_MouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedPanel is not { } panel || !ReferenceEquals(panel, sender)) return;
        PlacePanel(panel, _dragStartOffset + (e.GetPosition(Area) - _dragStart));
    }

    private void DraggablePanel_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_draggedPanel is not { } panel || !ReferenceEquals(panel, sender)) return;
        _draggedPanel = null;
        panel.ReleaseMouseCapture();
        var offset = Offset(panel);
        var saved = PanelPlacement.ToFraction(new Vector(offset.X, offset.Y), new Size(Area.ActualWidth, Area.ActualHeight));
        if (ReferenceEquals(panel, MenuPanel)) (_config.MenuOffsetX, _config.MenuOffsetY) = (saved.X, saved.Y);
        else (_config.DebugOffsetX, _config.DebugOffsetY) = (saved.X, saved.Y);
        ConfigStore.Save(_config);
        e.Handled = true;
    }
}
