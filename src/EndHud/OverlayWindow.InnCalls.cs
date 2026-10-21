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
/// new and out when it's done. Stacked in a fixed order (the kitchens by number, each in a row kept for the session;
/// then Full Blaze and the last call), hidden in place while the dispatch panel is open. Only
/// during a session; at the combo hint's place by default, draggable while the menu is open (a sample banner shows
/// then); the inn helper's size and opacity apply.
/// </summary>
public partial class OverlayWindow
{
    private InnBannerStack? _innCallBannersStack;
    private readonly InnBannerRows _innCallRows = new();
    private Point? _innCallDragOffset;

    /// <summary>The banners drawn into <c>InnCallStack</c> (made on first use: the XAML's elements exist by then).</summary>
    private InnBannerStack InnCallBanners => _innCallBannersStack ??= new InnBannerStack(InnCallStack, model => InnCallView.Banner(model, InnPicture));

    /// <summary>
    /// Puts up the calls' banners in their rows (<see cref="InnBannerRows"/>; off a session: all slide out and the kitchens'
    /// rows are forgotten). A banner already up for a call stays as it is; a kitchen's banner changing slides out and in
    /// within its row, which stays, so nothing else moves. While the dispatch panel is open the stack is hidden in place
    /// and shown again as it is when the panel closes (<see cref="InnViews.Away"/>).
    /// </summary>
    private void RenderInnCalls(InnViews views)
    {
        var (calls, away) = (views.Calls, views.Away);
        bool on = _config.InnHelperEnabled && _config.InnCallsEnabled;
        if (!on || !views.Session) _innCallRows.Clear();
        var rows = on && views.Session ? _innCallRows.Rows(views.Kitchens, calls, Loc.Get).ToList() : [];
        // Something to drag while placing it.
        if (_menuOpen && rows.Count == 0 && on)
            rows.Add(new InnBannerRow("sample", InnCallView.Sample(_party.Identifier?.Library.Inn, Loc.Get)));
        InnCallStack.Visibility = away && !_menuOpen ? Visibility.Hidden : Visibility.Visible;
        InnCallBanners.Update(rows);
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
