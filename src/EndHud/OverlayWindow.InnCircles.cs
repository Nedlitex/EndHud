using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EndHud;

/// <summary>
/// The kitchens' circles (<see cref="InnKitchenCircleView"/>): during a session, each kitchen's call as a big circle in its
/// own column of one row at one fixed place (<see cref="InnKitchenCircleLayout"/>: the middle bottom, right above the
/// held-dishes tray), the same with the dispatch panel open or closed; a circle stays as it is while its call stands
/// (<see cref="InnCircleRow"/>). While the panel is open, the kitchen whose tab is selected gets a gold frame, in place.
/// With the banners' setting (InnCallsEnabled), the inn's size (smaller only) and opacity. Nothing to drag: three sample
/// circles show at their place while the menu is open, out of a session.
/// </summary>
public partial class OverlayWindow
{
    private InnCircleRow? _innCircleRow;

    /// <summary>The circles drawn into <c>InnCircleSlots</c> (made on first use: the XAML's elements exist by then).</summary>
    private InnCircleRow InnCircles => _innCircleRow ??= new InnCircleRow(InnCircleRow.Shape(InnCircleSlots), model => InnKitchenCircleView.Circle(model, InnPicture));

    private void RenderInnCircles(InnViews views)
    {
        bool on = _config.InnHelperEnabled && _config.InnCallsEnabled;
        var slots = on && views.Session ? InnKitchenCircleView.Of(views.Calls, Loc.Get) : [];
        // Where they stand, while the menu is open.
        if (_menuOpen && on && slots.All(s => s is null)) slots = InnKitchenCircleView.Sample(_party.Identifier?.Library.Inn, Loc.Get);
        InnCircles.Update(slots);
        InnCircles.Emphasise(views.Session && views.Away && !_menuOpen ? views.SelectedKitchen : null);
    }

    /// <summary>Puts the row at its place, at the inn's size (smaller only) and opacity.</summary>
    private void PlaceInnCircles()
    {
        double width = HintCanvas.ActualWidth, height = HintCanvas.ActualHeight, scale = InnScale();
        var place = InnKitchenCircleLayout.Place(width, height, scale);
        double s = InnKitchenCircleLayout.Scale(width, height, scale);
        InnCircleFrame.LayoutTransform = new ScaleTransform(s, s);
        InnCircleSlots.Opacity = _config.InnStripOpacity;
        Canvas.SetLeft(InnCircleFrame, place.Left);
        Canvas.SetTop(InnCircleFrame, place.Top);
    }
}
