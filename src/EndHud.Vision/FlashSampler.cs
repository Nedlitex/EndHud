using System.Diagnostics;

namespace EndHud.Vision;

/// <summary>
/// The fast path for the cast flash: corners of the game picture (<see cref="BattleSkillFlash.Region"/>) handed in many
/// times a second, from the capture's own thread (<see cref="Capture.WindowCapture.SetCornerTap"/>), measured and told
/// (<see cref="CastFlashTracker"/>); each reading takes the flashes told since the one before (<see cref="Take"/>).
/// Thread-safe: sampled on one thread, taken on another.
/// </summary>
public sealed class FlashSampler
{
    /// <summary>How often the corner is sampled: 30 times a second, the rate the flash was measured at (it lasts 2-10 frames of it).</summary>
    public static readonly TimeSpan Every = TimeSpan.FromSeconds(1 / 30.0);

    private readonly object _lock = new();
    private readonly CastFlashTracker _tracker = new();
    private readonly List<FlashEvent> _told = [];
    private long _samples, _ticks;
    private readonly int[] _byKind = new int[Enum.GetValues<FlashKind>().Length];

    /// <summary>A corner of the game picture: its bottom-right corner is the picture's.</summary>
    /// <param name="scale">The picture's HUD scale (<see cref="UiScaler.Scale"/>).</param>
    /// <param name="at">When the frame arrived, on the readings' clock.</param>
    public void Sample(Frame corner, double scale, TimeSpan at)
    {
        long start = Stopwatch.GetTimestamp();
        var buttons = BattleSkillFlash.MeasureAll(corner, scale);
        lock (_lock)
        {
            foreach (var flash in _tracker.Add(at, buttons))
            {
                _told.Add(flash);
                _byKind[(int)flash.Kind]++;
            }
            _samples++;
            _ticks += Stopwatch.GetTimestamp() - start;
        }
    }

    /// <summary>The flashes told since the last take, oldest first.</summary>
    public IReadOnlyList<FlashEvent> Take()
    {
        lock (_lock)
        {
            if (_told.Count == 0) return [];
            var taken = _told.ToList();
            _told.Clear();
            return taken;
        }
    }

    /// <summary>Sampling stopped (out of battle, another layout): nothing carries over.</summary>
    public void Forget()
    {
        lock (_lock)
        {
            _tracker.Forget();
            _told.Clear();
        }
    }

    /// <summary>Counts only, for the diagnostic log: samples, the time measuring took, flashes by kind.</summary>
    public string Describe()
    {
        lock (_lock)
        {
            double ms = Stopwatch.GetElapsedTime(0, _ticks).TotalMilliseconds;
            return $"battle-skill flash: {_samples} samples, {(_samples == 0 ? 0 : ms / _samples):F2} ms each; " +
                   string.Join(", ", Enum.GetValues<FlashKind>().Select(k => $"{k} {_byKind[(int)k]}"));
        }
    }
}
