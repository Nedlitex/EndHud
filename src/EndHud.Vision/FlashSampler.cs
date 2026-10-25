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
    private TimeSpan? _last; // the latest sample measured
    private bool? _controller; // the layout of the latest sample: a controller's (true) or keyboard and mouse
    private readonly int[] _byKind = new int[Enum.GetValues<FlashKind>().Length];

    /// <summary>A corner of the game picture: its bottom-right corner is the picture's.</summary>
    /// <param name="scale">The picture's HUD scale (<see cref="UiScaler.Scale"/>).</param>
    /// <param name="at">When the frame arrived, on the readings' clock.</param>
    /// <param name="layout">The HUD layout the readings last saw: keyboard and mouse measure the row; with a controller
    /// the corner itself says which diamond it shows (LB is held and let go faster than the readings follow), and a
    /// corner showing neither (a cutscene, the HUD fading between them) is passed over.</param>
    public void Sample(Frame corner, double scale, TimeSpan at, HudLayout layout)
    {
        long start = Stopwatch.GetTimestamp();
        bool controller = layout != HudLayout.KeyboardMouse;
        var places = controller ? BattleSkillFlash.PlacesOf(corner, scale) : SkillButtonPlaces.Row;
        var buttons = places is { } p ? BattleSkillFlash.MeasureAll(corner, scale, p) : null;
        lock (_lock)
        {
            // Another layout: its buttons are other places, so nothing in progress carries over.
            if (_controller is { } was && was != controller) _tracker.Forget();
            _controller = controller;
            if (buttons is not null)
            {
                _last = at;
                foreach (var flash in _tracker.Add(at, buttons))
                {
                    _told.Add(flash);
                    _byKind[(int)flash.Kind]++;
                }
            }
            _samples++;
            _ticks += Stopwatch.GetTimestamp() - start;
        }
    }

    /// <summary>
    /// Each slot's battle skill empowered as of the latest sample (<see cref="CastFlashTracker.Empowered"/>), slot 1
    /// first: true, false, or null when it can't be told (and for every slot before anything was sampled). The keyboard
    /// row is drawn from the right for a squad of fewer than four; the controller's diamond keeps every place.
    /// </summary>
    /// <param name="squadSize">The squad's operators (1-4).</param>
    public IReadOnlyList<bool?> Empowered(int squadSize)
    {
        lock (_lock)
        {
            var slots = new bool?[4];
            if (_last is not { } at || _controller is not { } controller) return slots;
            var buttons = _tracker.Empowered(at);
            int offset = controller ? 0 : 4 - Math.Clamp(squadSize, 1, 4);
            for (int slot = 0; slot < 4; slot++)
                if (slot + offset < 4) slots[slot] = buttons[slot + offset];
            return slots;
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
            _last = null;
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
