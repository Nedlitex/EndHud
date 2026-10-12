namespace EndHud;

/// <summary>Command-line options.</summary>
/// <param name="TargetTitle">
/// Track any window whose title contains this text (e.g. "Notepad") instead of the game.
/// Null means: look for the Endfield game window.
/// </param>
/// <param name="HideFromCapture">
/// Exclude the overlay from screenshots/recordings (WDA_EXCLUDEFROMCAPTURE). Off by default
/// so you can screenshot the sample; turn it on once the HUD captures the game itself.
/// </param>
internal sealed record HudOptions(string? TargetTitle, bool HideFromCapture)
{
    public static HudOptions Parse(string[] args)
    {
        string? target = null;
        bool hideFromCapture = false;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--target" when i + 1 < args.Length:
                    target = args[++i];
                    break;
                case "--hide-from-capture":
                    hideFromCapture = true;
                    break;
            }
        }
        return new HudOptions(target, hideFromCapture);
    }
}
