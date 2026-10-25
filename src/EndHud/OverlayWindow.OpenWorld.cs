using System.Windows;

namespace EndHud;

/// <summary>
/// The menu's "show in open-world battles too" rows, one in each battle overlay's card (排轴, 连携就绪提示, 战斗提示,
/// 状态监视): which overlays show in a fight in the open world (<see cref="BattleOverlays"/>). Always clickable, like the
/// editors: a choice can be made before the overlay is turned on.
/// </summary>
public partial class OverlayWindow
{
    private void InitOpenWorldSettings()
    {
        RotationOpenWorldCheckBox.IsChecked = _config.ShowRotationInOpenWorld;
        ComboHintOpenWorldCheckBox.IsChecked = _config.ShowComboHintsInOpenWorld;
        AlertsOpenWorldCheckBox.IsChecked = _config.ShowAlertsInOpenWorld;
        GaugesOpenWorldCheckBox.IsChecked = _config.ShowGaugesInOpenWorld;
    }

    private void OpenWorldCheckBox_Click(object sender, RoutedEventArgs e)
    {
        _config.ShowRotationInOpenWorld = RotationOpenWorldCheckBox.IsChecked == true;
        _config.ShowComboHintsInOpenWorld = ComboHintOpenWorldCheckBox.IsChecked == true;
        _config.ShowAlertsInOpenWorld = AlertsOpenWorldCheckBox.IsChecked == true;
        _config.ShowGaugesInOpenWorld = GaugesOpenWorldCheckBox.IsChecked == true;
        ConfigStore.Save(_config);
        // The strip and the hints follow at once; the gauges and the alerts on the next reading (a quarter of a second).
        RenderRotation();
        UpdateComboHints();
    }
}
