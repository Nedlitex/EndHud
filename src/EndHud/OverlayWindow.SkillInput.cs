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
/// <para>
/// Not offered in this build (<see cref="SkillKeyReading.Offered"/>, owner 2026-10-25, the anti-cheat risk): the card
/// stays hidden and the reader never listens, whatever <see cref="HudConfig.ReadSkillPresses"/> says.
/// </para>
/// </summary>
public partial class OverlayWindow
{
    private readonly SkillInputReader _skillInput = new();

    /// <summary>The reader, for the tests that prove it never starts while key reading isn't offered.</summary>
    internal SkillInputReader SkillInput => _skillInput;

    /// <summary>The slot whose key is being chosen (Change clicked, no key pressed yet); null otherwise.</summary>
    private int? _capturingSlot;

    /// <summary>The presses for the strip: null when the player hasn't turned reading on (nothing is read then).</summary>
    internal ISkillPressSource? ActiveSkillPresses => PressesFor(_config, _skillInput);

    /// <summary>The presses the strip follows under these settings in this build: none while key reading isn't offered.</summary>
    internal static ISkillPressSource? PressesFor(HudConfig config, ISkillPressSource reader) => PressesFor(config, reader, SkillKeyReading.Offered);

    /// <summary>The presses the strip follows: the reader's when key reading is offered and the player turned it on, else none.</summary>
    internal static ISkillPressSource? PressesFor(HudConfig config, ISkillPressSource reader, bool offered) =>
        offered && config.ReadSkillPresses ? reader : null;

    private void InitSkillInput()
    {
        // Hidden in the XAML; shown only in a build that offers key reading. Not offered: nothing below is wired,
        // so no row, key capture or click can be reached.
        if (!SkillKeyReading.Offered) return;
        SkillInputCard.Visibility = Visibility.Visible;
        SkillInputCheckBox.IsChecked = _config.ReadSkillPresses;
        // The rows depend on the strip's and the squad reading's switches: follow their clicks at once.
        RotationCheckBox.Click += (_, _) => ApplySkillInputSettings();
        PartyOcrCheckBox.Click += (_, _) => ApplySkillInputSettings();
        MenuPanel.IsVisibleChanged += (_, _) => { if (!MenuPanel.IsVisible) EndSkillKeyCapture(); };
        ShowSkillKeys();
        ApplySkillInputSettings();
    }

    /// <summary>Called on every refresh: listen or not, and show whether Windows refused.</summary>
    internal void UpdateSkillInput(bool gameInFront)
    {
        // Not offered: the reader is never asked to listen (nor to stop: it never started).
        if (!SkillKeyReading.Offered) return;
        _skillInput.SetListening(SkillInputListens(_config, gameInFront), _config.SkillKeys);
        ApplySkillInputSettings();
    }

    /// <summary>Whether the reader listens under these settings in this build: never while key reading isn't offered.</summary>
    internal static bool SkillInputListens(HudConfig config, bool gameInFront) => SkillInputListens(config, gameInFront, SkillKeyReading.Offered);

    /// <summary>
    /// Whether the reader listens (and so holds the four keys as hotkeys): only in a build that offers it, with the
    /// player's switch, the strip and the squad reading on, and the game itself in front. Anywhere else the keys are
    /// let go at once and type as usual.
    /// </summary>
    internal static bool SkillInputListens(HudConfig config, bool gameInFront, bool offered) =>
        offered && config.ReadSkillPresses && config.RotationEnabled && config.PartyOcrEnabled && gameInFront;

    /// <summary>The card's rows: usable only with the strip and the squad reading on; the keys only with reading on.</summary>
    private void ApplySkillInputSettings()
    {
        bool possible = _config.RotationEnabled && _config.PartyOcrEnabled;
        SkillInputCheckBox.IsEnabled = possible;
        SkillInputNeedsRotation.Visibility = possible ? Visibility.Collapsed : Visibility.Visible;
        SkillInputFailed.Visibility = _config.ReadSkillPresses && _skillInput.Failed ? Visibility.Visible : Visibility.Collapsed;
        string unread = _config.ReadSkillPresses ? UnreadKeysText(_skillInput.UnreadKeys, _config.SkillKeys, SkillKeyDisplayName, Loc.Get) : "";
        if (SkillInputUnreadKeys.Text != unread) SkillInputUnreadKeys.Text = unread;
        SkillInputUnreadKeys.Visibility = unread.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        bool rows = possible && _config.ReadSkillPresses;
        if (!rows && _capturingSlot is not null) EndSkillKeyCapture();
        SkillKeyRows.IsEnabled = rows;
    }

    /// <summary>
    /// One sentence per bound key the reader could not hold, naming the key and its slot ("按键 1 已被其他程序占用，
    /// 无法读取 1 号位战技…"); empty when every key is read. <paramref name="text"/> gives a string resource's text.
    /// </summary>
    internal static string UnreadKeysText(IReadOnlyList<UnreadKey> unread, IReadOnlyList<int> keys, Func<int, string> keyName, Func<string, string> text) =>
        string.Join("\n", unread
            .Where(key => key.Slot is >= 1 and <= 4 && key.Slot <= keys.Count)
            .Select(key => string.Format(
                text(key.TakenByAnotherProgram ? "Menu.SkillInput.KeyTaken" : "Menu.SkillInput.KeyRefused"),
                keyName(keys[key.Slot - 1]), key.Slot)));

    /// <summary>A stored key's name on the player's layout, or "unnamed key".</summary>
    private static string SkillKeyDisplayName(int code)
    {
        string name = SkillInputNative.KeyName(code);
        return name.Length > 0 ? name : Loc.Get("Menu.SkillInput.UnnamedKey");
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
            SkillKeyName(slot).Text = SkillKeyDisplayName(_config.SkillKeys[slot - 1]);
        }
    }

    private TextBlock SkillKeyLabel(int slot) => slot switch { 1 => SkillKeyLabel1, 2 => SkillKeyLabel2, 3 => SkillKeyLabel3, _ => SkillKeyLabel4 };

    private TextBlock SkillKeyName(int slot) => slot switch { 1 => SkillKeyName1, 2 => SkillKeyName2, 3 => SkillKeyName3, _ => SkillKeyName4 };
}
