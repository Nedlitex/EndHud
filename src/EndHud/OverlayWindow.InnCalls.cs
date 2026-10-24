using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace EndHud;

/// <summary>
/// The inn session's banners (owner, live 2026-10-20: "remove all hints from the current panel, use the combo instead"):
/// Full Blaze and the last call of <see cref="InnAdvice.Calls"/> as <see cref="ComboBanner"/>s (a last call with the dish's
/// picture), up for as long as the call stands, sliding in when it's new and out when it's done. The kitchens' calls
/// are big circles instead (owner, live 2026-10-21: the kitchens' banners were "very hard to read";
/// <see cref="InnKitchenCircleView"/>). Stacked in a fixed order (Full Blaze, then the last calls by table), hidden in
/// place while the dispatch panel is open. Only
/// during a session; at the combo hint's height, right of table 1's tile, by default (HudConfig.DefaultInnCallX/Y), draggable while the menu is open (a sample banner shows
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
        // The kitchens' calls are their circles (RenderInnCircles); the banners are Full Blaze's and the last call's.
        var rows = on && views.Session ? _innCallRows.Rows(views.Kitchens, calls, Loc.Get).Where(r => !r.IsKitchen).ToList() : [];
        // Something to drag while placing it.
        if (_menuOpen && rows.Count == 0 && on)
            rows.Add(new InnBannerRow("sample", InnCallView.Sample(_party.Identifier?.Library.Inn, Loc.Get)));
        InnCallStack.Visibility = away && !_menuOpen ? Visibility.Hidden : Visibility.Visible;
        InnCallBanners.Update(rows);
        SetInnCallEditing(_menuOpen);
    }

    private void InnCallsCheckBox_Click(object sender, RoutedEventArgs e)
    {
        _config.InnCallsEnabled = InnCallsCheckBox.IsChecked == true;
        ConfigStore.Save(_config);
        RenderInn();
    }

    /// <summary>The swaps setting (建议换菜): handed to the session at once, so the next advice follows it.</summary>
    private void InnSwapsCheckBox_Click(object sender, RoutedEventArgs e)
    {
        _config.InnSwapsEnabled = InnSwapsCheckBox.IsChecked == true;
        ConfigStore.Save(_config);
        if (_innSession is not null) _innSession.SuggestSwaps = _config.InnSwapsEnabled;
        RenderInn();
    }

    // ---- Placing the banners ----

    /// <summary>
    /// The banners' drag frame (outline, label, move cursor) while the menu is open, only while they're on: with the inn
    /// helper or its calls turned off there is nothing to place (owner 2026-10-24: the frame stayed with the helper off).
    /// Applied on every render, so turning either switch off takes the frame away at once.
    /// </summary>
    private void SetInnCallEditing(bool editing)
    {
        bool placing = InnCallPlacing(editing, _config);
        InnCallFrame.BorderBrush = placing ? (Brush)FindResource("Accent") : Brushes.Transparent;
        InnCallDragLabel.Visibility = placing ? Visibility.Visible : Visibility.Collapsed;
        InnCallFrame.Cursor = placing ? Cursors.SizeAll : null;
        InnCallFrame.IsHitTestVisible = placing;
    }

    /// <summary>Whether the banners' drag frame is up: the menu is open and the inn helper and its calls are both on.</summary>
    internal static bool InnCallPlacing(bool editing, HudConfig config) => editing && config.InnHelperEnabled && config.InnCallsEnabled;

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
