using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using EndHud.SkillInput;
using EndHud.Vision.Input;

namespace EndHud;

/// <summary>
/// The rotation strip card's battle-skill key reading: the player's consent switch (<see cref="HudConfig.ReadSkillPresses"/>,
/// off until ticked), the four keys, chosen by pressing them, and the reader (<see cref="SkillInputReader"/>),
/// which listens only while the switch is on, the strip and the squad reading are on, and the game itself is
/// in front: never in another program, never in this menu.
/// </summary>
public partial class OverlayWindow
{
    private readonly SkillInputReader _skillInput = new();

    /// <summary>The slot whose key is being chosen (Change clicked, no key pressed yet); null otherwise.</summary>
    private int? _capturingSlot;

    /// <summary>The presses for the strip: null when the player hasn't turned reading on (nothing is read then).</summary>
    internal ISkillPressSource? ActiveSkillPresses => _config.ReadSkillPresses ? _skillInput : null;

    private void InitSkillInput()
    {
        SkillInputCheckBox.IsChecked = _config.ReadSkillPresses;
        // The rows depend on the strip's and the squad reading's switches: follow their clicks at once.
        RotationCheckBox.Click += (_, _) => ApplySkillInputSettings();
        PartyOcrCheckBox.Click += (_, _) => ApplySkillInputSettings();
        MenuPanel.IsVisibleChanged += (_, _) => { if (!MenuPanel.IsVisible) EndSkillKeyCapture(); };
        ShowSkillKeys();
        ApplySkillInputSettings();
    }

    /// <summary>Called on every refresh: listen or not, and show whether Windows refused.</summary>
    private void UpdateSkillInput(bool gameInFront)
    {
        _skillInput.SetListening(
            _config.ReadSkillPresses && _config.RotationEnabled && _config.PartyOcrEnabled && gameInFront, _config.SkillKeys);
        ApplySkillInputSettings();
    }

    /// <summary>The card's rows: usable only with the strip and the squad reading on; the keys only with reading on.</summary>
    private void ApplySkillInputSettings()
    {
        bool possible = _config.RotationEnabled && _config.PartyOcrEnabled;
        SkillInputCheckBox.IsEnabled = possible;
        SkillInputNeedsRotation.Visibility = possible ? Visibility.Collapsed : Visibility.Visible;
        SkillInputFailed.Visibility = _config.ReadSkillPresses && _skillInput.Failed ? Visibility.Visible : Visibility.Collapsed;
        bool rows = possible && _config.ReadSkillPresses;
        if (!rows && _capturingSlot is not null) EndSkillKeyCapture();
        SkillKeyRows.IsEnabled = rows;
    }

    private void SkillInputCheckBox_Click(object sender, RoutedEventArgs e)
    {
        _config.ReadSkillPresses = SkillInputCheckBox.IsChecked == true;
        ConfigStore.Save(_config);
        DiagnosticLog.Info($"skill input: reading turned {(_config.ReadSkillPresses ? "on" : "off")} by the player");
        ApplySkillInputSettings();
    }

    /// <summary>Change on a row: the next key pressed becomes that slot's key (Esc keeps the old one).</summary>
    private void ChangeSkillKey_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } || !int.TryParse(tag, out int slot)) return;
        EndSkillKeyCapture();
        _capturingSlot = slot;
        SkillKeyName(slot).Text = Loc.Get("Menu.SkillInput.Waiting");
        // Before WPF sees the key: a focused button would take Space or Enter, and keyboard navigation
        // Tab and the arrows, and the window would never be told of them.
        ComponentDispatcher.ThreadPreprocessMessage += CaptureSkillKeyMessage;
    }

    private void ResetSkillKeys_Click(object sender, RoutedEventArgs e)
    {
        EndSkillKeyCapture();
        _config.SkillKeys = [.. SkillKeyCodes.Defaults];
        ConfigStore.Save(_config);
        ShowSkillKeys();
    }

    private void CaptureSkillKeyMessage(ref MSG msg, ref bool handled)
    {
        if (_capturingSlot is not int slot || msg.message is not (SkillInputNative.WM_KEYDOWN or SkillInputNative.WM_SYSKEYDOWN)) return;
        handled = true; // the key chooses; it does nothing else in the menu
        var (result, takenBy) = SkillKeyCapture.Capture(_config.SkillKeys, slot, SkillKeyCodes.FromKeyMessage(msg.lParam));
        switch (result)
        {
            case CaptureResult.Saved:
                ConfigStore.Save(_config);
                EndSkillKeyCapture();
                break;
            case CaptureResult.Cancelled:
                EndSkillKeyCapture();
                break;
            case CaptureResult.Taken:
                SkillKeyTaken.Text = Loc.Format("Menu.SkillInput.Taken", takenBy);
                SkillKeyTaken.Visibility = Visibility.Visible;
                break;
        }
    }

    private void EndSkillKeyCapture()
    {
        ComponentDispatcher.ThreadPreprocessMessage -= CaptureSkillKeyMessage;
        _capturingSlot = null;
        SkillKeyTaken.Visibility = Visibility.Collapsed;
        ShowSkillKeys();
    }

    private void ShowSkillKeys()
    {
        for (int slot = 1; slot <= 4; slot++)
        {
            SkillKeyLabel(slot).Text = Loc.Format("Menu.SkillInput.Slot", slot);
            string name = SkillInputNative.KeyName(_config.SkillKeys[slot - 1]);
            SkillKeyName(slot).Text = name.Length > 0 ? name : Loc.Get("Menu.SkillInput.UnnamedKey");
        }
    }

    private TextBlock SkillKeyLabel(int slot) => slot switch { 1 => SkillKeyLabel1, 2 => SkillKeyLabel2, 3 => SkillKeyLabel3, _ => SkillKeyLabel4 };

    private TextBlock SkillKeyName(int slot) => slot switch { 1 => SkillKeyName1, 2 => SkillKeyName2, 3 => SkillKeyName3, _ => SkillKeyName4 };
}
