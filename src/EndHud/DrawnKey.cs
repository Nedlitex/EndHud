namespace EndHud;

/// <summary>
/// What a strip last drew, as a key, so it is rebuilt only when what it would show changes. A null
/// key is a strip with nothing to show (collapsed).
/// </summary>
internal sealed class DrawnKey
{
    private string? _drawn;
    private bool _stale;

    /// <summary>True when <paramref name="key"/> differs from what was last drawn (and it is now taken as drawn).</summary>
    public bool Changed(string? key)
    {
        if (!_stale && key == _drawn) return false;
        _drawn = key;
        _stale = false;
        return true;
    }

    /// <summary>
    /// Forget what was drawn: the next <see cref="Changed"/> is true, whatever its key. Not a reset to
    /// null: null is "nothing to show", so a strip with nothing to show would never collapse.
    /// </summary>
    public void Invalidate() => _stale = true;
}
