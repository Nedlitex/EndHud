using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace EndHud;

/// <summary>
/// The inn session's banners (owner, live 2026-10-20: "the notification should be more obvious when a dish should
/// be added to a kitchen, try to use the same ui as combo ready"; "remove all hints from the current panel, use the
/// combo instead"): each call of <see cref="InnAdvice.Calls"/> as a <see cref="ComboBanner"/> with the dish's picture,
/// up for as long as the call stands (the calls are held steady, <see cref="InnCallHold"/>), sliding in when it's
/// new and out when it's done. Stacked in a fixed order (Full Blaze, the kitchens by number, the last call). Only
/// during a session; at the combo hint's place by default, draggable while the menu is open (a sample banner shows
/// then); the inn helper's size and opacity apply.
/// </summary>
public partial class OverlayWindow
{
    private readonly List<(string Key, Border Element, Grid Holder)> _innCallBanners = [];
    private Point? _innCallDragOffset;

    /// <summary>Puts up the banners for <paramref name="calls"/> (none: all slide out); a banner already up for a call stays as it is.</summary>
    private void RenderInnCalls(IReadOnlyList<InnCall> calls)
    {
        var models = _config.InnHelperEnabled && _config.InnCallsEnabled ? InnCallView.Of(calls, Loc.Get).ToList() : [];
        // Something to drag while placing it.
        if (_menuOpen && models.Count == 0 && _config.InnHelperEnabled && _config.InnCallsEnabled)
            models.Add(InnCallView.Sample(_party.Identifier?.Library.Inn, Loc.Get));

        foreach (var banner in _innCallBanners.Where(b => models.All(m => m.Key != b.Key)).ToList())
        {
            _innCallBanners.Remove(banner);
            ComboBanner.SlideOut(banner.Holder, banner.Element, () => InnCallStack.Children.Remove(banner.Holder));
        }
        foreach (var model in models)
        {
            if (_innCallBanners.Any(b => b.Key == model.Key)) continue;
            var element = ComboBanner.Create(ComboBanner.Content(null, model.PictureId is { } id ? InnPicture(id) : null, model.Title, model.Caption));
            var holder = ComboBanner.Holder(element, model.Order);
            // In their order; banners still sliding out count too.
            int index = InnCallStack.Children.OfType<FrameworkElement>().Count(c => c.Tag is int order && order < model.Order);
            InnCallStack.Children.Insert(index, holder);
            _innCallBanners.Add((model.Key, element, holder));
            ComboBanner.SlideIn(holder, element);
        }
    }

    private void InnCallsCheckBox_Click(object sender, RoutedEventArgs e)
    {
        _config.InnCallsEnabled = InnCallsCheckBox.IsChecked == true;
        ConfigStore.Save(_config);
        RenderInn();
    }

    // ---- Placing the banners ----

    private void SetInnCallEditing(bool editing)
    {
        InnCallFrame.BorderBrush = editing ? (Brush)FindResource("Accent") : Brushes.Transparent;
        InnCallDragLabel.Visibility = editing && _config.InnHelperEnabled && _config.InnCallsEnabled ? Visibility.Visible : Visibility.Collapsed;
        InnCallFrame.Cursor = editing ? Cursors.SizeAll : null;
    }

    /// <param name="size">The game window's scale (ComboHintLayout) times the player's size.</param>
    private void ScaleInnCalls(double size)
    {
        InnCallFrame.LayoutTransform = new ScaleTransform(size, size);
        InnCallStack.Opacity = _config.InnStripOpacity;
        PlaceInnCallsFromConfig();
    }

    private void PlaceInnCallsFromConfig() => PlaceInnCalls(_config.InnCallX * HintCanvas.ActualWidth, _config.InnCallY * HintCanvas.ActualHeight);

    private void PlaceInnCalls(double left, double top)
    {
        (left, top) = ComboHintLayout.Place(left, top, HintCanvas.ActualWidth, HintCanvas.ActualHeight);
        Canvas.SetLeft(InnCallFrame, left);
        Canvas.SetTop(InnCallFrame, top);
    }

    private void InnCall_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_menuOpen) return;
        _innCallDragOffset = e.GetPosition(InnCallFrame);
        InnCallFrame.CaptureMouse();
        e.Handled = true;
    }

    private void InnCall_MouseMove(object sender, MouseEventArgs e)
    {
        if (_innCallDragOffset is not { } offset) return;
        var at = e.GetPosition(HintCanvas);
        PlaceInnCalls(at.X - offset.X, at.Y - offset.Y);
    }

    private void InnCall_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_innCallDragOffset is null) return;
        _innCallDragOffset = null;
        InnCallFrame.ReleaseMouseCapture();
        if (HintCanvas.ActualWidth <= 0 || HintCanvas.ActualHeight <= 0) return;
        _config.InnCallX = Canvas.GetLeft(InnCallFrame) / HintCanvas.ActualWidth;
        _config.InnCallY = Canvas.GetTop(InnCallFrame) / HintCanvas.ActualHeight;
        ConfigStore.Save(_config);
    }
}
