namespace EndHud;

/// <summary>
/// Time spent reading the game: it runs while frames of the game are being read, and stands still
/// while they aren't (the reading turned off because another window is in front, the game minimised,
/// a capture that sends nothing). What "the player has been somewhere else for 30 s" is measured in:
/// the inn's memory is forgotten after time spent reading screens that aren't the inn, never after
/// time the game was in the background (owner, live 2026-10-20: the kitchens set, a 27 s alt-tab,
/// and the menu said it hadn't seen the kitchens).
/// <para>Fed by the reading loop (<see cref="Frame"/>, <see cref="Stop"/>) on its worker thread,
/// asked by the overlay (<see cref="At"/>) on the UI thread; all on <see cref="InnClock"/>.</para>
/// </summary>
internal sealed class ReadingClock
{
    /// <summary>
    /// The most one gap between two frames counts for: longer than a capture's wait for a picture
    /// (2 s) plus a slow reading. A longer gap is a stall, and only this much of it counts.
    /// </summary>
    public static readonly TimeSpan FrameGap = TimeSpan.FromSeconds(5);

    private readonly object _lock = new();
    private TimeSpan _banked; // reading time up to the last frame (or the stop)
    private TimeSpan? _lastFrame; // when the last frame was read; null while stopped
    private TimeSpan? _stoppedAt; // when reading stopped (the last frame before a pause)

    /// <summary>
    /// A frame was read at <paramref name="now"/>. Returns the time since the last frame that didn't
    /// count (a pause, or the part of a stall over <see cref="FrameGap"/>); zero when reading went on.
    /// </summary>
    public TimeSpan Frame(TimeSpan now)
    {
        lock (_lock)
        {
            TimeSpan skipped = TimeSpan.Zero;
            if (_lastFrame is { } last)
            {
                var gap = now - last;
                var counted = Clamp(gap);
                _banked += counted;
                skipped = gap - counted;
            }
            else if (_stoppedAt is { } stopped && now > stopped)
            {
                skipped = now - stopped;
            }
            _lastFrame = now;
            _stoppedAt = null;
            return skipped < TimeSpan.Zero ? TimeSpan.Zero : skipped;
        }
    }

    /// <summary>Reading stopped at <paramref name="now"/> (turned off): the clock stands still until the next frame.</summary>
    public void Stop(TimeSpan now)
    {
        lock (_lock)
        {
            if (_lastFrame is not { } last) return;
            _banked += Clamp(now - last);
            _lastFrame = null;
            _stoppedAt = now;
        }
    }

    /// <summary>Reading time at <paramref name="now"/> (on <see cref="InnClock"/>); never goes back.</summary>
    public TimeSpan At(TimeSpan now)
    {
        lock (_lock)
            return _lastFrame is { } last ? _banked + Clamp(now - last) : _banked;
    }

    private static TimeSpan Clamp(TimeSpan gap) => gap < TimeSpan.Zero ? TimeSpan.Zero : gap > FrameGap ? FrameGap : gap;
}
