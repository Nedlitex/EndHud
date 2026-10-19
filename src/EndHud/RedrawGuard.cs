namespace EndHud;

/// <summary>
/// Redraws an overlay part only when what it shows changed (its key), and always once after
/// <see cref="Invalidate"/> (the menu opened or closed, so its frame changed). Nothing to show is a
/// key too (null): resetting the last key to null instead of invalidating made a menu closing out of
/// battle look like "no change", so the rotation strip's sample stayed on screen (0.5.3).
/// </summary>
internal sealed class RedrawGuard
{
    private string? _shown;
    private bool _stale = true;

    /// <summary>True when <paramref name="key"/> must be drawn: it differs from the last one drawn, or the guard was invalidated.</summary>
    public bool Changed(string? key)
    {
        if (!_stale && key == _shown) return false;
        _shown = key;
        _stale = false;
        return true;
    }

    public void Invalidate() => _stale = true;
}
