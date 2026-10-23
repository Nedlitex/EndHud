namespace EndHud;

/// <summary>
/// "Close EndHud when the game exits" (<see cref="HudConfig.CloseWithGame"/>): the game counts as
/// exited once its window, seen earlier in this run, has been gone for <see cref="GoneFor"/>. The
/// wait rides out the window being made again (switching fullscreen and windowed); EndHud started
/// before the game waits for it to appear first.
/// </summary>
internal sealed class GameExitWatch
{
    public static readonly TimeSpan GoneFor = TimeSpan.FromSeconds(10);

    private bool _seen;
    private DateTime? _goneSince;

    /// <summary>One check: whether the game window is there now. True once the game has exited.</summary>
    public bool Update(bool gameFound, DateTime now)
    {
        if (gameFound)
        {
            _seen = true;
            _goneSince = null;
            return false;
        }
        if (!_seen) return false;
        _goneSince ??= now;
        return now - _goneSince.Value >= GoneFor;
    }
}
