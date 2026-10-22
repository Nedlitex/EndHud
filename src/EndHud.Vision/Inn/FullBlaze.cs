namespace EndHud.Vision.Inn;

/// <summary>What to tell the player about Full Blaze (满堂彩).</summary>
public enum FullBlazeAdvice
{
    /// <summary>Start it now (the button at the right, [R]).</summary>
    StartNow,

    /// <summary>It's nearly full, or full and worth a few seconds' wait: fill every kitchen so the rush is fed.</summary>
    GetReady,
}

/// <summary>
/// When to start Full Blaze (满堂彩, plan S5, C17). Serving whole orders fills the fire (灶中火) to
/// 100; the player then starts it by hand, and for a minute tips double and guests come, order and
/// eat much faster. The fire can't go past 100, so every order served while it's full is fire
/// wasted: start it as soon as it's full, unless the rush would find nothing to serve.
/// </summary>
public static class FullBlazeAdvisor
{
    /// <summary>The fire is full at 100 (the meter's "N/100").</summary>
    public const int Full = 100;

    /// <summary>From this much fire the kitchens are filled ahead of the rush (plan S5).</summary>
    public const int GetReadyFrom = 80;

    /// <summary>With less than this left, start at once when full: a short Full Blaze beats none (plan S5).</summary>
    public static readonly TimeSpan LastMinute = TimeSpan.FromSeconds(60);

    /// <summary>When nothing is ready, wait at most this long for a dish to come out (plan S5).</summary>
    public static readonly TimeSpan WaitForDish = TimeSpan.FromSeconds(10);

    /// <summary>The advice, or null for nothing to say.</summary>
    /// <param name="fire">The fire, 0 to 100; <see cref="Full"/> when the meter shows the ready button (the number goes at full).</param>
    /// <param name="running">Full Blaze is on (<see cref="FullBlazeTracker.Running"/>).</param>
    /// <param name="left">Time left in the session.</param>
    /// <param name="dishesReady">Dishes on the counter or carried, ready to serve.</param>
    /// <param name="nextDishIn">When the next dish comes out of a kitchen; null when nothing is cooking.</param>
    /// <param name="fullFor">How long the fire has been full (<see cref="FullBlazeTracker.FullFor"/>).</param>
    public static FullBlazeAdvice? Advise(int fire, bool running, TimeSpan left, int dishesReady, TimeSpan? nextDishIn, TimeSpan fullFor)
    {
        if (running) return null;
        if (fire >= Full)
        {
            if (left < LastMinute) return FullBlazeAdvice.StartNow;
            bool dishDueSoon = nextDishIn is { } next && next <= WaitForDish;
            if (dishesReady == 0 && dishDueSoon && fullFor < WaitForDish) return FullBlazeAdvice.GetReady;
            return FullBlazeAdvice.StartNow;
        }
        return fire >= GetReadyFrom ? FullBlazeAdvice.GetReady : null;
    }
}

/// <summary>
/// Whether Full Blaze is on, and how long the fire has been full. Running is read off the screen: while it runs the
/// meter's button is a red circle counting down what's left (<see cref="FireMeter.BlazeLeft"/>, test_yuanyuan5). Until
/// that countdown is read, the ready button going while the session continues (pressing R is the only way it goes)
/// counts as running for <see cref="Lasts"/>. While it runs, a frame read as ready is not taken: the game's
/// "满堂生彩" line over the red circle was read as the ready label, and kept "开启满堂彩（R）" up all through it.
/// </summary>
public sealed class FullBlazeTracker
{
    /// <summary>Full Blaze lasts one minute (the game's 满堂彩说明).</summary>
    public static readonly TimeSpan Lasts = TimeSpan.FromSeconds(60);

    private TimeSpan _now;
    private TimeSpan? _fullSince;
    private TimeSpan? _runningUntil;
    private bool _countdownSeen; // the running time came from the countdown read off the meter

    /// <summary>Full Blaze is on.</summary>
    public bool Running => _runningUntil > _now;

    /// <summary>How long it has left; zero when it isn't running.</summary>
    public TimeSpan RunningLeft => Running ? _runningUntil!.Value - _now : TimeSpan.Zero;

    /// <summary>How long the fire has been full (the ready button showing); zero when it isn't.</summary>
    public TimeSpan FullFor => _fullSince is { } since ? _now - since : TimeSpan.Zero;

    /// <summary>One frame's reading.</summary>
    /// <param name="now">A clock that keeps running (not the session timer).</param>
    /// <param name="ready">The ready button shows; null when the meter couldn't be read this frame (nothing changes).</param>
    /// <param name="blazeLeft">The meter shows Full Blaze running with this long left (<see cref="FireMeter.BlazeLeft"/>); null when it doesn't or wasn't read.</param>
    /// <param name="inSession">A session is on screen; when it isn't, everything is forgotten.</param>
    public void Update(TimeSpan now, bool? ready, TimeSpan? blazeLeft, bool inSession)
    {
        _now = now;
        if (!inSession)
        {
            _fullSince = null;
            _runningUntil = null;
            _countdownSeen = false;
            return;
        }
        if (blazeLeft is { } left)
        {
            _fullSince = null;
            _runningUntil = now + left;
            _countdownSeen = true;
            return;
        }
        // A ready read while the countdown shows it running is the game's own line over the red circle, not the button.
        if (Running && _countdownSeen) return;
        _countdownSeen = false;
        if (ready is true)
        {
            _fullSince ??= now;
            _runningUntil = null;
        }
        else if (ready is false && _fullSince is not null)
        {
            _fullSince = null;
            _runningUntil = now + Lasts;
        }
    }
}
