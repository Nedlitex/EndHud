namespace EndHud.Vision;

/// <summary>What a run of filled samples on a battle-skill button was (<see cref="CastFlashTracker"/>).</summary>
public enum FlashKind
{
    /// <summary>A cast's fill, the button's rim not lit before it: a battle skill was cast, or an empowered glow is
    /// starting (told later by <see cref="EmpoweredStart"/>). Only a skill-point drop makes it a cast.</summary>
    Fill,

    /// <summary>A fill while the button's rim was lit (empowered): a cast of the empowered battle skill.</summary>
    EmpoweredFill,

    /// <summary>Told later, of an <see cref="EmpoweredFill"/>: the rim went off after it, so the empowered battle skill
    /// was used. The one flash that counts as a cast without a skill-point drop (some empowered casts are free).</summary>
    EmpoweredCast,

    /// <summary>Told later, of a <see cref="Fill"/>: the rim lit after it and stayed, so it was the empowered glow
    /// starting, not a cast.</summary>
    EmpoweredStart,
}

/// <summary>A flash on a battle-skill button.</summary>
/// <param name="Button">0-3: from the left in the keyboard row (a squad of fewer than four is drawn from the right); the
/// slot's place (slot 1 to 4) in the controller's diamonds.</param>
/// <param name="At">The fill's first sample (on the clock the samples were stamped with).</param>
/// <param name="Places">Where the buttons were when the fill started, which says how <paramref name="Button"/> maps to a slot.</param>
public sealed record FlashEvent(int Button, TimeSpan At, FlashKind Kind, SkillButtonPlaces Places);

/// <summary>
/// The cast flash from the battle-skill buttons sampled many times a second (<see cref="BattleSkillFlash"/>), one
/// button at a time. A run of filled samples counts when at least two of them stand out (one, sampled at 20 Hz or
/// slower), or when the button goes dark with a countdown right after it (a cast with a cooldown). It is dropped when
/// it lasts too long (scene light), or when another button filled within 0.1 s without a countdown on either
/// (scene light through several see-through buttons). Measured on 30 fps recordings (2026-10-24): the fill lasts
/// 67-330 ms.
/// <para>A <see cref="FlashKind.Fill"/> or <see cref="FlashKind.EmpoweredFill"/> is told 0.17 s after the fill ends
/// (time to see a countdown); whether the empowered rim went on or off after it, 0.65 s after.</para>
/// </summary>
public sealed class CastFlashTracker
{
    /// <summary>A countdown seen this soon after the fill is the cooldown starting.</summary>
    internal static readonly TimeSpan CountdownWithin = TimeSpan.FromSeconds(0.17);

    /// <summary>A countdown already there this long before isn't one the fill started.</summary>
    private static readonly TimeSpan CountdownBefore = TimeSpan.FromSeconds(0.2);

    /// <summary>The rim before a fill: the samples from 0.3 s to 0.067 s before it.</summary>
    private static readonly TimeSpan RimBeforeFrom = TimeSpan.FromSeconds(0.3), RimBeforeTo = TimeSpan.FromSeconds(0.067);

    /// <summary>The rim after a fill: the samples from 0.2 s to 0.63 s after it (the cast ring has gone by then).</summary>
    private static readonly TimeSpan RimAfterFrom = TimeSpan.FromSeconds(0.2), RimAfterTo = TimeSpan.FromSeconds(0.63);

    /// <summary>The rim counts as lit over a window when it was lit on this share of its samples.</summary>
    private const double RimSteady = 0.7;

    /// <summary>The button's own brightness before a sample: the median from 0.4 s to 0.067 s before it.</summary>
    private static readonly TimeSpan BaselineFrom = TimeSpan.FromSeconds(0.4), BaselineTo = TimeSpan.FromSeconds(0.067);

    /// <summary>A fill longer than this is scene light (the longest cast fill measured: 10 frames).</summary>
    private static readonly TimeSpan LongestFill = TimeSpan.FromSeconds(0.4);

    /// <summary>Fills on two buttons starting this close, neither followed by a countdown: scene light.</summary>
    private static readonly TimeSpan Together = TimeSpan.FromSeconds(0.1);

    /// <summary>Samples further apart than this: sampling stopped (out of battle, paused); what was in progress is forgotten.</summary>
    internal static readonly TimeSpan Gap = TimeSpan.FromSeconds(0.5);

    /// <summary>Sampled this far apart or more, one standing-out sample is enough (a 2-frame fill gives one).</summary>
    private static readonly TimeSpan Sparse = TimeSpan.FromSeconds(0.05);

    private static readonly TimeSpan Kept = TimeSpan.FromSeconds(1.2);

    private readonly List<(TimeSpan At, FlashSample[] Buttons)> _samples = [];
    private readonly Run?[] _open = new Run?[4];
    private readonly List<Run> _ended = [];
    private TimeSpan? _last;

    private sealed class Run(int button, TimeSpan start, SkillButtonPlaces places)
    {
        public int Button { get; } = button;
        public TimeSpan Start { get; } = start;
        public SkillButtonPlaces Places { get; } = places;
        public TimeSpan End { get; set; } = start;
        public int StandingOut { get; set; }
        public int MissedInARow { get; set; }
        public bool Told { get; set; }
        public bool Counted { get; set; }
        public bool RimBefore { get; set; }
        public bool? Countdown { get; set; }
    }

    /// <summary>A new sample of the four buttons, taken at <paramref name="at"/>; the flashes told by it, oldest first.</summary>
    public IReadOnlyList<FlashEvent> Add(TimeSpan at, IReadOnlyList<FlashSample> buttons)
    {
        if (_last is { } last && (at <= last || at - last > Gap)) Forget();
        var interval = _last is { } previous ? at - previous : TimeSpan.Zero;
        _last = at;
        _samples.Add((at, buttons.Take(4).ToArray()));
        _samples.RemoveAll(s => at - s.At > Kept);

        var told = new List<FlashEvent>();
        for (int b = 0; b < Math.Min(4, buttons.Count); b++)
        {
            var sample = buttons[b];
            if (BattleSkillFlash.Filled(sample))
            {
                var run = _open[b] ??= new Run(b, at, sample.Places) { RimBefore = RimLit(b, at - RimBeforeFrom, at - RimBeforeTo) == true };
                run.End = at;
                run.MissedInARow = 0;
                if (BattleSkillFlash.StandsOut(sample, Baseline(b, at))) run.StandingOut++;
            }
            else if (_open[b] is { } run && ++run.MissedInARow >= 2)
            {
                // One sample short of filled inside a fill is allowed; two end it.
                _open[b] = null;
                if (run.End - run.Start <= LongestFill) _ended.Add(run);
            }
        }

        foreach (var run in _ended.Where(r => !r.Told && at - r.End >= CountdownWithin).OrderBy(r => r.Start).ToList())
        {
            run.Told = true;
            run.Countdown = CountdownAfter(run);
            bool enough = run.StandingOut >= (interval >= Sparse ? 1 : 2) || run.Countdown == true;
            if (!enough || Scene(run)) continue;
            run.Counted = true;
            told.Add(new FlashEvent(run.Button, run.Start, run.RimBefore ? FlashKind.EmpoweredFill : FlashKind.Fill, run.Places));
        }
        foreach (var run in _ended.Where(r => r.Told && at - r.End >= RimAfterTo).ToList())
        {
            _ended.Remove(run);
            if (!run.Counted) continue;
            bool? after = RimLit(run.Button, run.End + RimAfterFrom, run.End + RimAfterTo);
            if (run.RimBefore && after == false) told.Add(new FlashEvent(run.Button, run.Start, FlashKind.EmpoweredCast, run.Places));
            else if (!run.RimBefore && after == true) told.Add(new FlashEvent(run.Button, run.Start, FlashKind.EmpoweredStart, run.Places));
        }
        return told;
    }

    /// <summary>The empowered glow is judged over this much of the latest samples (it stays for 0.5-10 s; a cast's ring crosses the rim in under 0.2).</summary>
    internal static readonly TimeSpan GlowOver = TimeSpan.FromSeconds(0.5);

    /// <summary>At least this many samples in <see cref="GlowOver"/> to judge it (about 8 at 15 Hz).</summary>
    private const int GlowSamples = 5;

    /// <summary>
    /// Each button's battle skill empowered at <paramref name="at"/>: its rim lit (the steady white glow,
    /// <see cref="BattleSkillFlash.Glow"/>) on at least 70 % of the samples of the last half second (true), seen and
    /// not lit on at least 70 % (false), else unknown: too few samples, scene light as bright as the glow, the button not
    /// seen, or the glow just starting or ending. By button, as the samples are (<see cref="FlashEvent.Button"/>).
    /// </summary>
    public IReadOnlyList<bool?> Empowered(TimeSpan at)
    {
        var window = _samples.Where(s => s.At <= at && at - s.At < GlowOver).ToList();
        var verdict = new bool?[4];
        if (window.Count < GlowSamples) return verdict;
        for (int b = 0; b < 4; b++)
        {
            int lit = 0, unlit = 0;
            foreach (var (_, buttons) in window)
            {
                if (b >= buttons.Length) continue;
                switch (BattleSkillFlash.Glow(buttons[b]))
                {
                    case true: lit++; break;
                    case false: unlit++; break;
                }
            }
            verdict[b] = lit >= RimSteady * window.Count ? true : unlit >= RimSteady * window.Count ? false : null;
        }
        return verdict;
    }

    /// <summary>Sampling stopped: nothing in progress carries over.</summary>
    public void Forget()
    {
        _samples.Clear();
        _ended.Clear();
        Array.Clear(_open);
        _last = null;
    }

    /// <summary>The button's fill just before <paramref name="at"/>: the median over the baseline window (the sample itself if none).</summary>
    private double Baseline(int button, TimeSpan at)
    {
        var values = _samples.Where(s => s.At >= at - BaselineFrom && s.At <= at - BaselineTo).Select(s => s.Buttons[button].Fill).Order().ToList();
        return values.Count == 0 ? double.MaxValue : values[values.Count / 2];
    }

    /// <summary>The rim lit on most samples between two times; null when there are none.</summary>
    private bool? RimLit(int button, TimeSpan from, TimeSpan to)
    {
        int n = 0, lit = 0;
        foreach (var (at, buttons) in _samples)
        {
            if (at < from || at > to) continue;
            n++;
            if (BattleSkillFlash.RimLit(buttons[button])) lit++;
        }
        return n == 0 ? null : lit >= RimSteady * n;
    }

    /// <summary>The button went dark with a countdown right after the fill, and wasn't before it.</summary>
    private bool CountdownAfter(Run run)
    {
        bool after = false, before = false;
        foreach (var (at, buttons) in _samples)
        {
            bool dark = BattleSkillFlash.Countdown(buttons[run.Button]);
            if (at > run.End && at <= run.End + CountdownWithin && dark) after = true;
            if (at < run.Start && at >= run.Start - CountdownBefore && dark) before = true;
        }
        return after && !before;
    }

    /// <summary>Another button filled within 0.1 s of this one, and neither went to a countdown: light from the scene.</summary>
    private bool Scene(Run run)
    {
        if (run.Countdown == true) return false;
        foreach (var other in _ended.Concat(_open.OfType<Run>()))
        {
            if (other == run || other.Button == run.Button || other.Countdown == true) continue;
            if ((other.Start - run.Start).Duration() <= Together) return true;
        }
        return false;
    }
}
