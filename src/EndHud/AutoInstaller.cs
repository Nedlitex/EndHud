namespace EndHud;

/// <summary>
/// Installs a newer EndHud by itself (<see cref="HudConfig.AutoInstallUpdates"/>, on by default),
/// but never while the game is running: a version found while playing waits until the game is
/// closed. Each version is tried once per run; a failed attempt waits for the next start (or "update
/// now" in the tray).
/// </summary>
internal sealed class AutoInstaller(Func<bool> enabled, Func<bool> gameRunning)
{
    private readonly HashSet<Version> _tried = [];
    private ReleaseInfo? _pending;

    /// <summary>A newer release was found; true if it will be installed by itself (now or once the game is closed).</summary>
    public bool Found(ReleaseInfo release)
    {
        if (!enabled() || _tried.Contains(release.Version)) return false;
        _pending = release;
        return true;
    }

    /// <summary>The release to install now, if one is waiting and the game isn't running; it's then taken (tried once).</summary>
    public ReleaseInfo? Due()
    {
        if (_pending is not { } release || !enabled() || gameRunning()) return null;
        _pending = null;
        _tried.Add(release.Version);
        return release;
    }
}
