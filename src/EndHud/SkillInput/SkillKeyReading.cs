namespace EndHud.SkillInput;

/// <summary>
/// Whether this build offers battle-skill key reading at all (the menu rows, <see cref="SkillInputReader"/>).
/// <para>
/// Not offered (owner, 2026-10-25): "given we are able to rather reliably support reading battle skill without
/// keyboard hook, hide that for now - we want to minimize risk that user is blamed by anticheat". The screen alone
/// follows the battle skills well enough (<c>ScreenCastJudge</c> with no presses), and holding the game's own keys
/// as global hotkeys, opening the PlayStation pad and polling XInput while the game is in front is exactly the kind
/// of thing an anti-cheat may flag. So with this false the menu rows are hidden and the reader never starts, even
/// for a player whose config.json still says <c>ReadSkillPresses = true</c> (the saved value is kept, not cleared,
/// so turning this back on restores each player's own choice).
/// </para>
/// <para>To offer it again: set this to true. Nothing else changes.</para>
/// </summary>
internal static class SkillKeyReading
{
    public static readonly bool Offered = false;
}
