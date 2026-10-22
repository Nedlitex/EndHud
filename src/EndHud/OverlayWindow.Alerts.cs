using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EndHud.Vision;
using EndHud.Vision.Alerts;

namespace EndHud;

/// <summary>
/// The player's alert rules (<c>%APPDATA%\EndHud\alerts.json</c>): checked on every reading in
/// battle (<see cref="AlertFollower"/>), popped up as the combo hint's mirror image. The pop-ups sit
/// on the right at x = 1 − the combo hint's x, at its height, and follow it when it's dragged.
/// </summary>
public partial class OverlayWindow
{
    private readonly AlertFollower _alertFollower = new();
    private readonly AlertStore _alertStore = new(Path.Combine(ConfigStore.Folder, "alerts.json"));
    private readonly Dictionary<string, (Grid Holder, Grid PopUp)> _alertPopUps = [];
    private IReadOnlyList<AlertRule> _alertRules = [];
    private (Grid Holder, Grid PopUp)? _alertSample;
    private AlertEditor? _alertEditor;
    private bool _loadingAlertSettings;

    /// <summary>(Re)load the rules, at start and after the alert editor saved them.</summary>
    internal void ReloadAlerts()
    {
        var load = _alertStore.Load();
        _alertRules = load.Rules;
        if (load.RenamedUnreadable is { } renamed) DiagnosticLog.Warn($"alerts.json couldn't be read; renamed to {Path.GetFileName(renamed)}");
        if (load.Newer) DiagnosticLog.Warn("alerts.json is from a newer EndHud; no alerts until EndHud is updated");
        if (load.Failed is { } why) DiagnosticLog.Warn($"alerts.json couldn't be opened: {why}");
        AlertsCountText.Text = _alertRules.Count == 0 ? Loc.Get("Menu.Alerts.None") : Loc.Format("Menu.Alerts.Count", _alertRules.Count);
    }

    private void InitAlertSettings()
    {
        _loadingAlertSettings = true;
        AlertsCheckBox.IsChecked = _config.AlertsEnabled;
        _loadingAlertSettings = false;
        ReloadAlerts();
        ApplyAlertSettings();
    }

    private void AlertsCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (_loadingAlertSettings) return;
        _config.AlertsEnabled = AlertsCheckBox.IsChecked == true;
        ConfigStore.Save(_config);
        ApplyAlertSettings();
    }

    /// <summary>The menu's alerts card: it needs the squad panel read.</summary>
    private void ApplyAlertSettings()
    {
        AlertsNeedsPartyOcr.Visibility = _config.PartyOcrEnabled ? Visibility.Collapsed : Visibility.Visible;
        AlertsCheckBox.IsEnabled = _config.PartyOcrEnabled;
    }

    /// <summary>
    /// A reading in or out of battle (the strip's rule for "in battle"): pop up the rules that turned
    /// true, take down those that stopped holding. True when a rule asks about the boss's statuses.
    /// </summary>
    private bool TrackAlerts(PartyState? state, bool inBattle)
    {
        var follow = _alertFollower.Update(state, inBattle, _config.AlertsEnabled && _config.PartyOcrEnabled, _alertRules, _clock.Elapsed);
        foreach (string id in follow.TakeDown) TakeDownAlert(id);
        foreach (var rule in follow.Show) ShowAlert(rule);
        return follow.ReadBossStatuses;
    }

    /// <summary>The overlay's timer: a pop-up leaves after 3 s even when no reading comes.</summary>
    private void ExpireAlerts()
    {
        foreach (string id in _alertFollower.Expire(_clock.Elapsed)) TakeDownAlert(id);
    }

    private void ShowAlert(AlertRule rule)
    {
        TakeDownAlert(rule.Id);
        string text = rule.Message.Trim().Length > 0 ? rule.Message.Trim() : AlertText.Describe(rule.When, _party.Identifier?.Library);
        var popUp = AlertPopUp.Create(AlertIcon(rule), text);
        var holder = AlertPopUp.Holder(popUp, rule.Id);
        AlertStack.Children.Add(holder); // the newest at the bottom
        _alertPopUps[rule.Id] = (holder, popUp);
        AlertPopUp.SlideIn(holder, popUp);
    }

    private void TakeDownAlert(string id)
    {
        if (!_alertPopUps.Remove(id, out var shown)) return;
        AlertPopUp.SlideOut(shown.Holder, shown.PopUp, () => AlertStack.Children.Remove(shown.Holder));
    }

    /// <summary>The circle's icon: the subject of the rule's first condition (owner, 2026-10-22).</summary>
    private UIElement AlertIcon(AlertRule rule)
    {
        var subject = AlertText.Subject(rule.When);
        var library = _party.Identifier?.Library;
        BitmapSource? picture = subject switch
        {
            { Kind: AlertSubjectKind.Status, Id: { } status } => StatusIcon(status),
            { Kind: AlertSubjectKind.Operator, Id: { } op } when library?.Find(op) is { } info => Avatar(info),
            _ => null,
        };
        return AlertPopUp.Icon(picture, subject.Kind == AlertSubjectKind.SkillPoints);
    }

    // ---- Placing: the combo hint's mirror image ----

    /// <summary>Right edge at the window's width less the combo hints' left edge; same top. Called whenever the combo hints move.</summary>
    private void PlaceAlerts()
    {
        if (HintCanvas.ActualWidth <= 0) return;
        double scale = (AlertFrame.LayoutTransform as ScaleTransform)?.ScaleX ?? 1;
        double right = HintCanvas.ActualWidth - Canvas.GetLeft(ComboHintFrame);
        Canvas.SetLeft(AlertFrame, right - AlertFrame.ActualWidth * scale);
        Canvas.SetTop(AlertFrame, Canvas.GetTop(ComboHintFrame));
    }

    private void AlertFrame_SizeChanged(object sender, SizeChangedEventArgs e) => PlaceAlerts();

    /// <summary>The same size as the combo hints (they follow the game window together).</summary>
    private void ScaleAlerts(Transform comboHintsScale)
    {
        AlertFrame.LayoutTransform = comboHintsScale;
        PlaceAlerts();
    }

    /// <summary>While the menu is open: a sample pop-up shows where alerts appear (drag the combo hint to move both).</summary>
    private void SetAlertsEditing(bool editing)
    {
        if (editing && _alertSample is null)
        {
            var popUp = AlertPopUp.Create(AlertPopUp.Icon(null, skillPoints: false), Loc.Get("Alert.Sample"));
            var holder = AlertPopUp.Holder(popUp, "sample");
            AlertStack.Children.Insert(0, holder);
            AlertPopUp.ShowStill(holder);
            _alertSample = (holder, popUp);
        }
        else if (!editing && _alertSample is { } sample)
        {
            AlertStack.Children.Remove(sample.Holder);
            _alertSample = null;
        }
    }

    // ---- The editor ----

    private void EditAlertsButton_Click(object sender, RoutedEventArgs e) => OpenAlertEditor();

    /// <summary>The alert rules editor (one at a time).</summary>
    public void OpenAlertEditor()
    {
        if (_alertEditor is not null)
        {
            _alertEditor.Activate();
            return;
        }
        if (_party.Identifier?.Library is not { } library)
        {
            MessageBox.Show(Loc.Get("Alerts.NoLibrary"), Loc.Get("Alerts.Title"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _alertEditor = new AlertEditor(_alertStore, library, ReloadAlerts);
        _alertEditor.Closed += (_, _) => _alertEditor = null;
        if (_menuOpen) SetMenuOpen(false); // hand the screen to the editor
        _alertEditor.Show();
    }
}
