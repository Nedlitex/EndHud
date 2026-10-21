using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using EndHud.Vision;
using EndHud.Vision.Capture;
using EndHud.Vision.Inn;
using EndHud.Vision.Operators;

namespace EndHud;

/// <summary>
/// Background loop: while the HUD is showing over the game, grab a frame of the game window
/// every interval, read the squad panel and name the operators; in the inn, read the inn's screens
/// instead (<see cref="InnFrameReader"/>). Events fire on worker threads.
/// </summary>
internal sealed class PartyMonitor : IDisposable
{
    /// <summary>
    /// The operator library on this machine: the roster, avatars and skill icons (reference/, from
    /// the operator package EndHud carries and updates from GitHub: see OperatorPackage), and the
    /// user's learned portraits and calibration.
    /// </summary>
    public static string LibraryFolder { get; } = Path.Combine(ConfigStore.Folder, "operators");

    /// <summary>The operator package's folder: the roster, avatars, and the inn's data and pictures.</summary>
    internal static string ReferenceFolder => Path.Combine(LibraryFolder, OperatorLibrary.ReferenceFolderName);

    /// <summary>A manual check doesn't ask GitHub again if it was asked this recently; it reports that it's up to date as of then.</summary>
    public static readonly TimeSpan MinManualCheckInterval = TimeSpan.FromMinutes(15);

    /// <summary>While EndHud runs, how often it looks for a newer operator package on GitHub (when updates are on).</summary>
    private static readonly TimeSpan RefreshCheckEvery = TimeSpan.FromHours(6);

    /// <summary>Whether to look for newer operator packages on GitHub (the "check for updates" setting); set by the overlay.</summary>
    public Func<bool> UpdatesEnabled { get; set; } = () => true;

    private readonly TimeSpan _interval;
    private readonly CancellationTokenSource _cts = new();
    private volatile IntPtr _target;
    private volatile bool _active;
    private volatile SquadIdentifier? _identifier;
    private volatile BossStatusReader? _bossStatuses;
    private volatile TeamBuffReader? _teamBuffs;

    /// <summary>Read the boss's statuses too (the loaded rotation asks about them); set by the overlay.</summary>
    public bool ReadBossStatuses { get; set; }

    /// <summary>Read the squad panel (the "read the squad panel" setting); set by the overlay.</summary>
    public bool ReadSquad { get; set; } = true;

    /// <summary>Look for the inn and read it (the inn helper's setting); set by the overlay.</summary>
    public bool ReadInn { get; set; } = true;

    private volatile InnFrameReader? _innFrames;

    /// <summary>
    /// The inn's reader, set once the operator library (with the inn's data and pictures) has loaded;
    /// replaceable by tests.
    /// </summary>
    internal InnFrameReader? InnFrames
    {
        get => _innFrames;
        set => _innFrames = value;
    }

    /// <summary>
    /// Why the inn can only be partly read (an i18n key), or null: without the Simplified Chinese
    /// OCR language only the session's timer is read, so the helper can say what's missing.
    /// </summary>
    public string? InnProblem { get; private set; }

    /// <summary>
    /// A frame of the inn, as read; null for the first frame after the inn that isn't one of its
    /// screens (a fight, the open world), so the inn's advice can be put away.
    /// </summary>
    public event Action<InnReading?>? InnUpdated;

    private bool _inInn;

    /// <summary>
    /// Time spent reading the game (<see cref="ReadingClock"/>): runs while frames are read, stands
    /// still while reading is off; the inn's memory measures leaving the inn in it.
    /// </summary>
    public ReadingClock Reading { get; } = new();

    /// <summary>A frame of the game was read at <paramref name="now"/> (<see cref="InnClock"/>): the reading clock moves on.</summary>
    internal void FrameRead(TimeSpan now)
    {
        var skipped = Reading.Frame(now);
        if (skipped <= TimeSpan.Zero) return;
        // Back from a pause: the inn's holds pick up where they were, as if no time had passed
        // (InnFrameReader.Resume), so the screen held before the pause is looked for on every frame.
        _innFrames?.Resume(skipped);
        if (_missedSince is { } since) _missedSince = since + skipped;
    }

    private static readonly TimeSpan BossStatusEvery = TimeSpan.FromSeconds(1);
    private int _libraryBusy;
    private DateTime _lastCheckUtc = DateTime.MinValue;

    // What the reading loop is doing, and since when: the watchdog logs a step that hangs.
    private volatile string _stage = "starting";
    private long _stageSince = Stopwatch.GetTimestamp();
    private long _stuckReported;
    private static readonly TimeSpan StuckAfter = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan RepeatLogEvery = TimeSpan.FromSeconds(30);
    // When the squad naming running beside the readings started (0: none running), and the last
    // one the watchdog reported.
    private long _namingSince;
    private long _namingReported;

    /// <summary>
    /// Keep the overlay out of screen captures (WDA_EXCLUDEFROMCAPTURE), so the game can be read
    /// off the screen without reading EndHud's own HUD; false if Windows refused (before Windows 10
    /// 2004), and then the screen isn't read. Set by the overlay.
    /// </summary>
    public Func<bool>? ExcludeOverlayFromCapture { get; set; }

    /// <summary>The overlay's window (its menu takes the focus while open); set by the overlay.</summary>
    public IntPtr OverlayWindow { get; set; }

    /// <summary>Window captures in a row that sent nothing (2 s each) before the game is read off the screen.</summary>
    private const int SilentCapturesBeforeScreen = 2;

    private string _source = "window capture";

    /// <summary>
    /// The game's area copied off the screen (<see cref="ScreenCapture"/>), or null with the reason
    /// reported: only the whole game area, and only while the game (or EndHud's own menu) is in
    /// front, since the screen shows whatever covers it.
    /// </summary>
    private Frame? ReadScreen(IntPtr hwnd)
    {
        _source = "screen";
        var area = Native.ClientAreaOnScreen(hwnd);
        var desktop = ScreenCapture.VirtualScreen();
        if (area.IsEmpty)
        {
            Report(null, "Party.WindowEmpty");
            return null;
        }
        if (area.X < desktop.X || area.Y < desktop.Y || area.Right > desktop.Right || area.Bottom > desktop.Bottom)
        {
            DiagnosticLog.Throttled("off-screen", RepeatLogEvery, $"reading from the screen, but part of the game is off it ({area}, screen {desktop})");
            Report(null, "Party.OffScreen");
            return null;
        }
        // The overlay is kept out of screen captures; anything else in front (another app, the
        // rotation editor) would be read as the game.
        IntPtr front = Native.GetForegroundWindow();
        if (front != hwnd && front != OverlayWindow)
        {
            Report(null, "Party.GameCovered");
            return null;
        }
        var frame = ScreenCapture.Copy(area);
        DiagnosticLog.Throttled("screen-read", TimeSpan.FromHours(1), $"read the game from the screen: {area}", "INFO");
        return frame;
    }

    /// <summary>How long a reading waits for the squad naming before showing the last names instead.</summary>
    internal static readonly TimeSpan QuickNaming = TimeSpan.FromMilliseconds(150);

    /// <summary>
    /// <paramref name="state"/> with each slot's name from the last finished naming (<paramref name="names"/>,
    /// index 0 = slot 1) while naming this reading is still running. Only while the squad panel is
    /// seen: off it, nobody is shown by name anyway.
    /// </summary>
    internal static PartyState WithNames(PartyState state, IReadOnlyList<OperatorMatch?> names) =>
        !state.PanelVisible ? state : state with
        {
            Slots = state.Slots.Select(s => s.Operator is null && !s.Empty && s.Number >= 1 && s.Number <= names.Count ? s with { Operator = names[s.Number - 1] } : s).ToList(),
        };

    public PartyMonitor(TimeSpan interval) => _interval = interval;

    /// <summary>A reading with no squad panel in it: what the tracker is given for a frame of the inn.</summary>
    private static readonly PartyState NothingSeen = new([]);

    /// <summary>
    /// One frame's inn reading (see <see cref="InnFrameReader"/>): true when the frame is the inn and
    /// <see cref="InnUpdated"/> has been raised with it, so the squad panel isn't read; false when
    /// it isn't (or the inn isn't read), and then the squad panel is read as always. Every inn screen
    /// (the session's, the setup screens) is kept through frames that miss it (<see cref="SessionHold"/>):
    /// those raise a held reading and return false, so the squad panel, if it shows, leaves the inn at once.
    /// </summary>
    /// <param name="now">A clock that keeps running.</param>
    /// <param name="squadPanelSeen">The last squad reading saw the panel: the inn isn't looked for.</param>
    internal async Task<bool> TryInnAsync(Frame frame, TimeSpan now, bool squadPanelSeen)
    {
        InnReading? reading = null;
        try
        {
            if (ReadInn && _innFrames is { } inn) reading = await inn.ReadAsync(frame, now, squadPanelSeen);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A reader's failure mustn't stop the capture; the squad panel is read as usual meanwhile.
            DiagnosticLog.Throttled("inn-failed-" + ex.GetType().Name, RepeatLogEvery, $"reading the inn failed: {ex}", "ERROR");
        }
        if (reading is null)
        {
            if (_inInn)
            {
                if (HoldInn(now, squadPanelSeen) is { } held)
                {
                    // Not seen as the inn, so the squad panel is read too: seeing it ends the hold at once.
                    InnUpdated?.Invoke(held);
                    return false;
                }
                LeaveInn(now);
            }
            return false;
        }
        if (!_inInn)
        {
            _inInn = true;
            DiagnosticLog.Info($"inn: {reading.Screen} at {frame.Width}x{frame.Height}");
        }
        EndMissStreak(now);
        _lastInnScreen = reading.Screen;
        if (reading.InSession)
        {
            if (reading.Clock is { Closed: true }) _innClosedAt ??= now;
            else if (reading.Clock is not null) _innClosedAt = null;
        }
        else _innClosedAt = null;
        KeepRecent(frame, DescribeInn(reading));
        InnUpdated?.Invoke(reading);
        return true;
    }

    /// <summary>
    /// An inn screen is kept through frames that show none for this long. The timer can't be
    /// read on a frame here and there (the dispatch panel sliding in or out, the HUD fading, the
    /// camera moving, a gourmand's bubble over it): owner, 2026-10-19, the strip "flashes all the
    /// time" when each such frame counted as leaving. The setup screens' titles likewise (a page
    /// sliding in, a click's highlight): the log showed "inn: left" and the screen found again ~1.2 s
    /// later, the advice put away meanwhile (owner, live 2026-10-19: "the page refreshes too slowly").
    /// A real exit (the results screen, a fight) is told at once by the squad panel, or after this long.
    /// </summary>
    internal static readonly TimeSpan SessionHold = TimeSpan.FromSeconds(8);

    private InnScreenKind? _lastInnScreen; // the last inn reading's screen (held through missed frames)
    private TimeSpan? _innClosedAt; // when the session's timer first read 00:00
    private TimeSpan? _missedSince; // the first frame of the current streak without an inn screen
    private int _missed;

    /// <summary>
    /// A frame with no inn screen while one was showing: that screen held (a reading that carries
    /// nothing new, <see cref="InnReading.Fresh"/> false), or null when the inn was left: the squad
    /// panel is on screen, no inn screen for <see cref="SessionHold"/>, or the inn closed and its last
    /// serving time (<see cref="InnState.ServingAfterCloseSeconds"/>) is over.
    /// </summary>
    private InnReading? HoldInn(TimeSpan now, bool squadPanelSeen)
    {
        if (_lastInnScreen is not { } screen || squadPanelSeen) return null;
        if (_innClosedAt is { } closed && (now - closed).TotalSeconds >= InnState.ServingAfterCloseSeconds) return null;
        _missedSince ??= now;
        if (now - _missedSince.Value >= SessionHold) return null;
        _missed++;
        return new InnReading(screen, now, Fresh: false);
    }

    /// <summary>An inn screen again after missed frames: the streak said in one line.</summary>
    private void EndMissStreak(TimeSpan now)
    {
        if (_missedSince is not { } since) return;
        DiagnosticLog.Info($"inn: {_missed} frame(s) without an inn screen over {(now - since).TotalSeconds:F1} s; the inn ({_lastInnScreen}) was kept");
        _missedSince = null;
        _missed = 0;
    }

    private void LeaveInn(TimeSpan now)
    {
        string why = _missedSince is { } since ? $" (no inn screen for {(now - since).TotalSeconds:F1} s)" : "";
        _inInn = false;
        _lastInnScreen = null;
        _innClosedAt = null;
        _missedSince = null;
        _missed = 0;
        DiagnosticLog.Info("inn: left" + why);
        InnUpdated?.Invoke(null);
    }

    /// <summary>What an inn reading saw, in a line, for the diagnostic export's pictures.</summary>
    internal static string DescribeInn(InnReading r) =>
        $"inn {r.Screen}, time left {r.Clock?.Left.ToString(@"m\:ss") ?? "-"}, " +
        $"tables {string.Join(" ", r.TableCards.Select(t => t is null ? "?" : $"{t.State}{(t.Items.Count > 0 ? ":" + string.Join("+", t.Items.Select(i => (i.Id ?? "?") + (i.Served ? " served" : ""))) : "")}"))}, " +
        $"fire {(r.Fire is { } f ? (f.Ready ? "ready" : f.Fire?.ToString() ?? "?") : "-")}" +
        (r.Panel is { } p ? $", panel kitchen {p.Kitchen}/{p.Kitchens} queue {string.Join("+", p.Queue.Where(q => q.Filled).Select(q => q.DishId ?? "?"))}" : "") +
        (r.Bubble is { } b ? $", bubble \"{b.Text}\"" : "") +
        (r.Screen == InnScreenKind.ChefPicker && r.Fresh ? $", picker kitchen {r.PickerKitchen?.ToString() ?? "?"}" : "") +
        (r.Setup is { } s ? $", theme {s.ThemeName}, kitchens {string.Join(" ", (s.Kitchens ?? []).Select(k => $"{k.Number}:{k.ChefsPlaced?.ToString() ?? "?"}/{k.ChefSlots?.ToString() ?? "?"}"))}" : "") +
        (r.Requirements is { } q ? $", menu rules: rating {q.Rating?.ToString() ?? "?"}, types {q.MinTypes?.ToString() ?? "?"}, dishes {q.MinDishes?.ToString() ?? "?"}, drinks {q.MinDrinks?.ToString() ?? "?"}" : "");

    /// <summary>The last game picture read (the game area only), for the diagnostic export.</summary>
    public Frame? LatestFrame { get; private set; }

    /// <summary>
    /// The pictures read over the last ~12 s (one every <see cref="RecentFrameEvery"/>, the last
    /// <see cref="RecentFrameCount"/>), at most 960 px wide, each with when and what was read: the
    /// diagnostic export includes them (UID blacked out), so it shows what EndHud saw as something
    /// went wrong, not only the moment of the export.
    /// </summary>
    public IReadOnlyList<RecentFrame> RecentFrames
    {
        get { lock (_recent) return _recent.ToList(); }
    }

    private volatile WindowCapture? _capture; // the window capture in use, for Describe
    private readonly Queue<RecentFrame> _recent = new();
    private DateTime _recentAt = DateTime.MinValue;
    private static readonly TimeSpan RecentFrameEvery = TimeSpan.FromSeconds(2);
    private const int RecentFrameCount = 6;
    private const int RecentFrameWidth = 960;

    private void KeepRecent(Frame gameFrame, PartyState raw) =>
        KeepRecent(gameFrame, $"panel {(raw.PanelVisible ? "seen" : "not seen")}, {DescribeLayout(raw)}");

    private void KeepRecent(Frame gameFrame, string read)
    {
        var now = DateTime.Now;
        if (now - _recentAt < RecentFrameEvery) return;
        _recentAt = now;
        var small = gameFrame.Width <= RecentFrameWidth ? gameFrame
            : gameFrame.ResizeSmooth(RecentFrameWidth, (int)Math.Round(gameFrame.Height * RecentFrameWidth / (double)gameFrame.Width));
        lock (_recent)
        {
            _recent.Enqueue(new RecentFrame(now, small, $"{read}, from {_source}"));
            while (_recent.Count > RecentFrameCount) _recent.Dequeue();
        }
    }

    /// <summary>
    /// What a reading that missed the squad panel did see, for the log: the picture's size and
    /// brightness (an all-black picture means the game's picture can't be captured at all), and the
    /// panel's parts one by one (marks, level text, combo bars, HP bars, ultimates, controlled slot).
    /// </summary>
    internal static string DescribeMiss(Frame frame, PartyState raw)
    {
        long sum = 0;
        int black = 0, samples = 0;
        for (int y = 0; y < frame.Height; y += 8)
        for (int x = 0; x < frame.Width; x += 8)
        {
            var (r, g, b) = frame.GetPixel(x, y);
            int v = (r + g + b) / 3;
            sum += v;
            if (v < 8) black++;
            samples++;
        }
        string slots = string.Join(" | ", raw.Slots.Select(s =>
            $"{s.Number}: hp {(s.Dead ? "fallen" : s.Hp?.ToString("F2") ?? "-")}, level {s.Level?.ToString() ?? "-"}, " +
            $"combo {(s.Combo is { } c ? (c.Ready ? "ready" : c.Progress.ToString("F2")) : "-")}, " +
            $"ultimate {(s.Ultimate is { } u ? (u.Ready ? "ready" : u.Progress.ToString("F2")) : "-")}"));
        return $"picture {frame.Width}x{frame.Height}, mean brightness {(samples == 0 ? 0 : sum / samples)}, {(samples == 0 ? 0 : black * 100 / samples)}% black; " +
               $"{DescribeLayout(raw)}, controlled slot {raw.ActiveSlot?.ToString() ?? "-"}, skill points {raw.SkillPoints?.ToString("F2") ?? "-"}; {slots}";
    }

    /// <summary>
    /// The HUD layout a reading found, and for keyboard and mouse how many of the panel's "-" marks
    /// (the controller layouts don't draw them).
    /// </summary>
    internal static string DescribeLayout(PartyState state) => state.Layout switch
    {
        HudLayout.Controller => "controller layout",
        HudLayout.ControllerSkillsHeld => "controller layout, LB held",
        _ => $"{state.PanelMarks}/8 panel marks",
    };

    /// <summary>Why the last reading gave nothing (an i18n key), or null when it read; for the diagnostic export.</summary>
    public string? LastProblem { get; private set; }

    /// <summary>What the reading loop is doing and how it went last, in a line, for the diagnostic export.</summary>
    public string Describe()
    {
        var since = Stopwatch.GetElapsedTime(Interlocked.Read(ref _stageSince));
        var library = _identifier?.Library;
        return $"reading {(_active ? "on" : "off")} from {_source}, step {_stage} for {since.TotalSeconds:F1} s, last problem {LastProblem ?? "none"}; " +
               $"library {(library is null ? "not loaded" : $"{library.Operators.Count} operators")}; " +
               $"last frame {(LatestFrame is { } f ? $"{f.Width}x{f.Height}" : "none")}" +
               (_capture is { } capture ? $"; {capture.Describe()}" : "");
    }

    /// <summary>A reading, or an i18n key describing why there is none.</summary>
    public event Action<PartyState?, string?>? Updated;

    /// <summary>Operator library progress: an i18n key and its format arguments.</summary>
    public event Action<string, object[]>? LibraryStatus;

    /// <summary>Null until the operator library has been loaded (a moment after start).</summary>
    public SquadIdentifier? Identifier => _identifier;

    /// <summary>A newer operator package from GitHub brought operators that weren't there before.</summary>
    public event Action<IReadOnlyList<OperatorInfo>>? NewOperators;

    /// <summary>Called from the UI thread whenever the game window or HUD visibility changes.</summary>
    public void SetTarget(IntPtr gameWindow, bool active)
    {
        bool wasOn = _active && _target != IntPtr.Zero, on = active && gameWindow != IntPtr.Zero;
        if (gameWindow != _target || active != _active)
            DiagnosticLog.Info($"reading {(on ? "on" : "off")}: {Native.Describe(gameWindow)}");
        _target = gameWindow;
        _active = active;
        // Back on (the game in front again): read now, not after the rest of the interval.
        if (on && !wasOn && _wake.CurrentCount == 0)
        {
            try
            {
                _wake.Release();
            }
            catch (SemaphoreFullException)
            {
            }
        }
    }

    /// <summary>Wakes the reading loop's wait between frames.</summary>
    private readonly SemaphoreSlim _wake = new(0, 1);

    /// <summary>
    /// Back from a pause with the window capture kept, how long to wait for a new picture before
    /// building the capture again (which sends one at once). The game draws every frame while in front.
    /// </summary>
    internal static readonly TimeSpan ResumeWait = TimeSpan.FromSeconds(0.5);

    public void Start()
    {
        _ = Task.Run(KeepLibraryAsync);
        StartReading();
        _ = Task.Run(WatchAsync);
    }

    /// <summary>Only the reading loop, without the operator library (tests set <see cref="InnFrames"/> themselves).</summary>
    internal void StartReading() => _ = Task.Run(RunAsync);

    private int _capturesStarted;

    /// <summary>Window captures built so far (each one logs "capture started").</summary>
    internal int CapturesStarted => Volatile.Read(ref _capturesStarted);

    private void Enter(string stage)
    {
        _stage = stage;
        Interlocked.Exchange(ref _stageSince, Stopwatch.GetTimestamp());
    }

    /// <summary>
    /// Every few seconds: if one step of a reading has taken more than <see cref="StuckAfter"/>
    /// (a capture, the text recognition, the character model that never returns), log it and say
    /// so in the debug panel, once per hang.
    /// </summary>
    private async Task WatchAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), _cts.Token);
                long since = Interlocked.Read(ref _stageSince);
                string stage = _stage;
                long naming = Interlocked.Read(ref _namingSince);
                if (naming != 0 && _active && Stopwatch.GetElapsedTime(naming) >= StuckAfter && Interlocked.Read(ref _namingReported) != naming)
                {
                    // Readings go on meanwhile, so only logged: the HUD still shows the squad, unnamed.
                    Interlocked.Exchange(ref _namingReported, naming);
                    DiagnosticLog.Error($"naming the squad has taken {Stopwatch.GetElapsedTime(naming).TotalSeconds:F0} s so far (character model too slow on this PC, or stuck)");
                }
                if (stage == "waiting" || !_active || Stopwatch.GetElapsedTime(since) < StuckAfter || Interlocked.Read(ref _stuckReported) == since) continue;
                Interlocked.Exchange(ref _stuckReported, since);
                DiagnosticLog.Error($"reading stuck: step {stage} has taken {Stopwatch.GetElapsedTime(since).TotalSeconds:F0} s; {Native.Describe(_target)}");
                Report(null, "Party.Stuck");
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void Report(PartyState? state, string? problem)
    {
        LastProblem = problem;
        Updated?.Invoke(state, problem);
    }

    /// <summary>
    /// Look for a newer operator package on GitHub now, unless it was asked in the last
    /// <see cref="MinManualCheckInterval"/>. Learned portraits are kept.
    /// </summary>
    public async Task<OperatorCheck> CheckOperatorsAsync()
    {
        var since = DateTime.UtcNow - _lastCheckUtc;
        int total = _identifier?.Library.Operators.Count ?? 0;
        if (since < MinManualCheckInterval) return new OperatorCheck(OperatorCheckStatus.CheckedRecently, [], total, since);
        var (result, busy) = await SyncPackageAsync(announce: true);
        total = _identifier?.Library.Operators.Count ?? 0;
        return busy ? new OperatorCheck(OperatorCheckStatus.AlreadyChecking, [], total, TimeSpan.Zero)
            : result is null ? new OperatorCheck(OperatorCheckStatus.Failed, [], total, TimeSpan.Zero)
            : new OperatorCheck(OperatorCheckStatus.Checked, result.Added, total, TimeSpan.Zero);
    }

    public Task CorrectAsync(int slotNumber, OperatorInfo? actual) =>
        _identifier?.CorrectAsync(slotNumber, actual) ?? Task.CompletedTask;

    public void ForgetLearned()
    {
        if (_identifier is not { } identifier) return;
        identifier.Library.ForgetAllLearned();
        _identifier = new SquadIdentifier(identifier.Library); // drop cached answers too
    }

    /// <summary>
    /// Install the operator package built into EndHud if it's newer than the library on this machine
    /// (a first start, or a newer EndHud), and load it: no network needed. Then, while EndHud runs,
    /// look for a newer package on GitHub every <see cref="RefreshCheckEvery"/> (when updates are on).
    /// </summary>
    private async Task KeepLibraryAsync()
    {
        try
        {
            try
            {
                if (OperatorPackage.BuiltIn() is { } builtIn)
                {
                    var added = OperatorPackage.InstallIfNewer(ReferenceFolder, builtIn);
                    DiagnosticLog.Info(added is null ? "built-in operator package: the installed one is as new" : $"built-in operator package installed ({added.Count} new operators)");
                }
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                DiagnosticLog.Error("built-in operator package not installed (keeping what's there)", ex);
            }
            await LoadLibraryAsync();

            bool failed = false;
            while (!_cts.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), _cts.Token); // soon after start, like the other update checks
                // After a failed check, again in RetryAfterFailure; otherwise every RefreshCheckEvery
                // (unless the player checked by hand in the meantime).
                var next = failed ? GitHubHttp.RetryAfterFailure : RefreshCheckEvery;
                if (UpdatesEnabled() && DateTime.UtcNow - _lastCheckUtc >= next - TimeSpan.FromSeconds(10))
                {
                    var (result, busy) = await SyncPackageAsync(announce: false);
                    if (result is { Added.Count: > 0 }) NewOperators?.Invoke(result.Added);
                    if (!busy) failed = result is null;
                }
                await Task.Delay(failed ? GitHubHttp.RetryAfterFailure : RefreshCheckEvery, _cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            // Without a library nobody is ever named: this must not go unseen.
            DiagnosticLog.Error("operator library failed", ex);
        }
    }

    /// <summary>
    /// Look for a newer operator package on GitHub and install it. Result is null if GitHub couldn't
    /// be asked or the package couldn't be installed (the current library stays); Busy is true if a
    /// check was already running (that one reports).
    /// </summary>
    private async Task<(OperatorPackageSyncResult? Result, bool Busy)> SyncPackageAsync(bool announce)
    {
        if (Interlocked.Exchange(ref _libraryBusy, 1) == 1) return (null, true);
        try
        {
            _lastCheckUtc = DateTime.UtcNow;
            using var http = GitHubHttp.Create(TimeSpan.FromSeconds(30));
            var result = await OperatorPackageSync.SyncAsync(ReferenceFolder, http,
                OperatorPackageSync.ContentsApi(ReleaseChecker.Repository), ReleaseChecker.UserAgent, _cts.Token);
            DiagnosticLog.Info($"operator package check: {(result is null ? "failed" : result.Installed ? $"installed a newer one ({result.Added.Count} new operators)" : "up to date")}");
            if (result is { Installed: true })
            {
                await LoadLibraryAsync();
                if (announce) LibraryStatus?.Invoke("Operators.Updated", [_identifier?.Library.Operators.Count ?? 0]);
            }
            if (result is null && announce) LibraryStatus?.Invoke("Operators.UpdateFailed", []);
            return (result, false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException
                                       or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            DiagnosticLog.Warn($"operator package check failed: {GitHubHttp.Explain(ex)}");
            if (!_cts.IsCancellationRequested && announce) LibraryStatus?.Invoke("Operators.UpdateFailed", []);
            return (null, false);
        }
        finally
        {
            Interlocked.Exchange(ref _libraryBusy, 0);
        }
    }

    private async Task LoadLibraryAsync()
    {
        var clock = Stopwatch.StartNew();
        var library = await OperatorLibrary.LoadAsync(LibraryFolder);
        DiagnosticLog.Info($"operator library loaded in {clock.ElapsedMilliseconds} ms: {library.Operators.Count} operators" +
                           (OperatorPackage.Installed(ReferenceFolder) is { } package ? $", package of {package.ContentAtUtc:yyyy-MM-dd}" : ", no package"));
        // An empty library recognises nobody; leave the identifier unset so the menu says why.
        _identifier = library.IsEmpty ? null : new SquadIdentifier(library);
        _bossStatuses = library.IsEmpty ? null : new BossStatusReader(library);
        _teamBuffs = library.IsEmpty ? null : new TeamBuffReader(library);
        await LoadInnAsync(library);
    }

    private InnText? _innText;
    private bool _innTextTried;

    /// <summary>
    /// The inn's readers over the library's inn data and the package's dish and drink pictures.
    /// Without the Simplified Chinese OCR language only the timer can be read (<see cref="ClockOnlyScreens"/>).
    /// </summary>
    private async Task LoadInnAsync(OperatorLibrary library)
    {
        try
        {
            if (!_innTextTried)
            {
                _innTextTried = true;
                _innText = InnText.TryCreate();
                if (_innText is null) DiagnosticLog.Warn("inn: the Simplified Chinese OCR language isn't installed; only the session's timer can be read");
            }
            if (_innText is null)
            {
                InnProblem = "Inn.Problem.NoTextRecognition";
                _innFrames = new InnFrameReader(new ClockOnlyScreens());
                return;
            }
            if (library.Inn is not { } data)
            {
                DiagnosticLog.Warn("inn: the operator package has no inn data; the inn helper can't read the inn");
                _innFrames = null;
                return;
            }
            var pictures = await InnPictures.LoadAsync(ReferenceFolder, data);
            DiagnosticLog.Info($"inn: {data.Dishes.Count} dishes, {data.Drinks.Count} drinks, {pictures.Count} pictures");
            InnProblem = null;
            _innFrames = new InnFrameReader(new InnScreens(_innText, pictures, data, library));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            DiagnosticLog.Error("inn: the inn's pictures couldn't be loaded; the inn helper can't read the inn", ex);
            _innFrames = null;
        }
    }

    private async Task RunAsync()
    {
        PartyReader reader;
        try
        {
            reader = new PartyReader(new TextRecognizer());
        }
        catch (InvalidOperationException ex)
        {
            DiagnosticLog.Error("text recognition unavailable", ex);
            Enter("waiting");
            Report(null, "Party.OcrUnavailable");
            return;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Error("reading couldn't start", ex);
            Enter("waiting");
            Report(null, "Party.StartFailed");
            return;
        }

        WindowCapture? capture = null;
        IntPtr captured = IntPtr.Zero;
        (int Width, int Height) capturedSize = default; // the game's client area when its capture was built
        bool paused = false; // the reading was off since the last frame
        var tracker = new PartyTracker();
        var bossClock = Stopwatch.StartNew();
        var ct = _cts.Token;
        bool panelSeen = false, firstReading = true;
        string names = "";
        Task? avatars = null;
        Task<PartyState>? naming = null;
        bool fromScreen = false, screenRefused = false;
        IntPtr describedWindow = IntPtr.Zero;
        int missed = 0;
        HudLayout? layoutSeen = null; // the last controller-or-not layout a visible panel showed
        int silentCaptures = 0;
        OperatorMatch?[] lastNames = [];
        IReadOnlyList<TeamBuff> lastBuffs = []; // kept while the panel is out of sight (a cutscene over the HUD)
        var panelReadAt = DateTime.MinValue; // when a reading last saw the squad panel
        bool panelSeenLast = false; // the last squad reading saw the panel (then the inn isn't looked for)
        try
        {
            while (!ct.IsCancellationRequested)
            {
                Enter("waiting");
                // Woken at once when the reading turns back on (SetTarget), not after the interval.
                await _wake.WaitAsync(_interval, ct);

                IntPtr hwnd = _target;
                if (!_active || hwnd == IntPtr.Zero)
                {
                    // Paused while the HUD is hidden (another window in front). Time stands still for
                    // the inn meanwhile: an alt-tab isn't leaving the inn. The window capture is kept,
                    // so coming back reads at once instead of building a capture again; it's let go
                    // only when the window really changed: closed, minimised, or another window.
                    Reading.Stop(InnClock.Now);
                    if (capture is not null && (hwnd == IntPtr.Zero || hwnd != captured || !Native.IsWindow(hwnd) || Native.IsIconic(hwnd)))
                    {
                        capture.Dispose();
                        capture = null;
                    }
                    paused = true;
                    continue;
                }

                try
                {
                    var clock = Stopwatch.StartNew();
                    Enter("capture");
                    Frame gameFrame;
                    if (captured != hwnd)
                    {
                        // Another game window (the game restarted): nothing carries over, and its
                        // own window capture is tried first again.
                        tracker = new PartyTracker();
                        fromScreen = false;
                        screenRefused = false;
                        silentCaptures = 0;
                    }
                    if (fromScreen)
                    {
                        captured = hwnd;
                        if (ReadScreen(hwnd) is not { } fromDesktop) continue; // why is reported
                        gameFrame = fromDesktop;
                    }
                    else
                    {
                        _source = "window capture";
                        bool started = false;
                        var size = Native.ClientAreaOnScreen(hwnd);
                        if (capture is not null && (captured != hwnd || (size.Width, size.Height) != capturedSize))
                        {
                            // Another window, or the game's window resized: a capture for it as it is now.
                            capture.Dispose();
                            capture = null;
                        }
                        Frame? frame = null;
                        if (capture is not null && paused)
                        {
                            // Back from a pause with the capture kept: a picture the window presents
                            // now, never the last one from before the pause (Windows sends frames
                            // only when the picture changes, and those during the pause were dropped).
                            frame = await capture.TryGetNewFrameAsync(ResumeWait, ct);
                            if (frame is null)
                            {
                                capture.Dispose();
                                capture = null;
                            }
                        }
                        paused = false;
                        if (capture is null)
                        {
                            capture?.Dispose();
                            capture = new WindowCapture(hwnd);
                            Interlocked.Increment(ref _capturesStarted);
                            _capture = capture;
                            if (describedWindow != hwnd)
                            {
                                // Before any frame: a capture that never delivers still leaves this.
                                describedWindow = hwnd;
                                DiagnosticLog.Info($"graphics (capture device on {capture.Adapter}):{Environment.NewLine}{GraphicsInfo.Describe(hwnd)}");
                            }
                            captured = hwnd;
                            capturedSize = (size.Width, size.Height);
                            started = true;
                        }

                        frame ??= await capture.GetFrameAsync(ct);
                        silentCaptures = 0;
                        var client = Native.ClientAreaInCapture(hwnd, frame.Width, frame.Height);
                        if (started) DiagnosticLog.Info($"capture started: frame {frame.Width}x{frame.Height}, game area {client}; {Native.Describe(hwnd)}");
                        if (client.IsEmpty)
                        {
                            DiagnosticLog.Throttled("empty-client", RepeatLogEvery, $"the game area in the captured frame ({frame.Width}x{frame.Height}) is empty; {Native.Describe(hwnd)}");
                            Report(null, "Party.WindowEmpty");
                            continue;
                        }
                        // Death first, so a fallen operator's death mark is never matched as a portrait.
                        gameFrame = frame.Crop(client);
                    }
                    LatestFrame = gameFrame;
                    var frameAt = InnClock.Now;
                    FrameRead(frameAt);
                    // The inn first: in it the squad panel isn't on screen, and its screens are read instead.
                    Enter("inn");
                    // The overlay's advice runs on the same clock (InnClock), so its estimates line up with the readings.
                    if (await TryInnAsync(gameFrame, frameAt, panelSeenLast))
                    {
                        if (ReadSquad) Report(tracker.Track(NothingSeen), null);
                        continue;
                    }
                    if (!ReadSquad) continue;
                    Enter("read");
                    var raw = await reader.ReadAsync(gameFrame);
                    var state = tracker.Track(raw);
                    panelSeenLast = raw.PanelVisible;
                    long readMs = clock.ElapsedMilliseconds;
                    KeepRecent(gameFrame, raw);
                    // The panel missed for 2 s in a row: say what was seen instead (a menu or a map
                    // explains itself; a battle that never shows the panel is a reading problem).
                    missed = raw.PanelVisible ? 0 : missed + 1;
                    if (missed >= PresenceMemory.ReadingsToLeave)
                        DiagnosticLog.Throttled("panel-missed", RepeatLogEvery, $"no squad panel for {missed} readings: {DescribeMiss(gameFrame, raw)}", "INFO");
                    // Keyboard-and-mouse or controller: the game follows the last device used, mid-fight too.
                    bool controller = raw.Layout != HudLayout.KeyboardMouse;
                    if (raw.PanelVisible && (layoutSeen is null || (layoutSeen != HudLayout.KeyboardMouse) != controller))
                    {
                        if (layoutSeen is not null) DiagnosticLog.Info($"HUD layout now: {(controller ? "controller" : "keyboard and mouse")}");
                        layoutSeen = raw.Layout;
                    }
                    if (state.PanelVisible != panelSeen)
                    {
                        panelSeen = state.PanelVisible;
                        DiagnosticLog.Info(panelSeen ? $"squad panel seen ({DescribeLayout(state)}) at {gameFrame.Width}x{gameFrame.Height}" : "squad panel gone");
                    }
                    // Back after long enough for the team menu: the last names may be another squad's.
                    // Shown while a slow naming runs, they put the old squad on the HUD for seconds.
                    if (raw.PanelVisible)
                    {
                        if (DateTime.UtcNow - panelReadAt >= SquadIdentifier.SquadMayChangeAfter) lastNames = [];
                        panelReadAt = DateTime.UtcNow;
                    }
                    if (_identifier is { } identifier)
                    {
                        if (avatars is null && state.PanelVisible)
                        {
                            var described = Stopwatch.StartNew();
                            avatars = identifier.PrepareAsync(state).ContinueWith(t => DiagnosticLog.Info(t.IsCompletedSuccessfully
                                ? $"wiki avatars described for this screen in {described.Elapsed.TotalSeconds:F1} s"
                                : $"wiki avatars couldn't be described: {t.Exception?.GetBaseException()}"), TaskScheduler.Default);
                        }
                        // Naming runs beside the readings, never in their way: the character model
                        // takes 0.2 s a portrait on a fast PC and several times that on a slow one
                        // or while the wiki avatars are being described, and a reading that waited
                        // for it left the HUD blank (the debug panel on "recognising the squad
                        // panel") or updating once a second. A reading takes the names as soon as
                        // naming finishes within QuickNaming; otherwise it shows the last names and
                        // naming goes on.
                        Enter("identify");
                        if (naming is { IsCompleted: true } done)
                        {
                            naming = null;
                            lastNames = (await done).Slots.Select(s => s.Operator).ToArray();
                        }
                        if (naming is null)
                        {
                            naming = identifier.IdentifyAsync(state);
                            Interlocked.Exchange(ref _namingSince, Stopwatch.GetTimestamp());
                        }
                        if (await Task.WhenAny(naming, Task.Delay(QuickNaming, ct)) == naming)
                        {
                            var finished = naming;
                            naming = null;
                            Interlocked.Exchange(ref _namingSince, 0);
                            state = await finished;
                            lastNames = state.Slots.Select(s => s.Operator).ToArray();
                        }
                        else
                        {
                            state = WithNames(state, lastNames);
                        }
                        long identifyMs = clock.ElapsedMilliseconds - readMs;
                        if (identifyMs > 2000)
                            DiagnosticLog.Throttled("slow-identify", RepeatLogEvery, $"naming the squad took {identifyMs} ms", "INFO");
                        string now = string.Join(", ", state.Slots.Select(s => s.Operator?.Operator.Id ?? "-"));
                        if (state.PanelVisible && now != names)
                        {
                            names = now;
                            DiagnosticLog.Info($"squad: {now}");
                        }
                        // The controlled operator's own counter (e.g. Melting Flame): drawn differently per
                        // operator, so read once we know who it is.
                        Enter("counters");
                        state = state with { Counters = OperatorCounters.ReadControlled(gameFrame, state) };
                        // Team buffs (Link) beside it: drawn in combat only. While the panel is out of
                        // sight the frame is something else (an ultimate's cutscene): the last ones stay.
                        Enter("buffs");
                        if (!raw.PanelVisible) state = state with { Buffs = state.InCombat ? lastBuffs : [] };
                        else if (state.InCombat && _teamBuffs is { } buffs) state = state with { Buffs = lastBuffs = buffs.Read(gameFrame) };
                        else lastBuffs = [];
                        // The boss's statuses take ~70 ms to read at 4K: only when the rotation asks,
                        // and at most once a second (they change far more slowly than skill points).
                        if (ReadBossStatuses && _bossStatuses is { } boss && bossClock.Elapsed >= BossStatusEvery)
                        {
                            Enter("boss statuses");
                            state = state with { BossStatuses = boss.Read(gameFrame) };
                            bossClock.Restart();
                        }
                    }
                    if (firstReading)
                    {
                        firstReading = false;
                        DiagnosticLog.Info($"first reading in {clock.ElapsedMilliseconds} ms (panel {(state.PanelVisible ? "seen" : "not seen")}, library {(_identifier is null ? "not loaded yet" : "loaded")})");
                    }
                    Report(state, null);
                }
                catch (TimeoutException)
                {
                    // No picture at all since capture started (Windows sends one at once, then on
                    // every change). After SilentCapturesBeforeScreen of those, read the game's area
                    // off the screen instead, once the overlay is kept out of screen captures. Windows
                    // 10 refuses that for a see-through (layered) window like the overlay (error 8):
                    // it's asked once per game window, not on every reading.
                    string what = $"the game window sends no picture to capture ({capture?.Describe() ?? "no capture"}); {Native.Describe(hwnd)}";
                    bool toScreen = false;
                    if (++silentCaptures >= SilentCapturesBeforeScreen && !screenRefused && ExcludeOverlayFromCapture is { } exclude)
                    {
                        toScreen = exclude();
                        screenRefused = !toScreen;
                    }
                    if (toScreen)
                    {
                        DiagnosticLog.Warn($"{what}; reading the game from the screen instead");
                        capture?.Dispose();
                        capture = null;
                        fromScreen = true;
                    }
                    else
                    {
                        DiagnosticLog.Throttled("no-frames", RepeatLogEvery, what);
                        Report(null, "Party.NoFrames");
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    DiagnosticLog.Throttled("failed-" + ex.GetType().Name, RepeatLogEvery, $"reading failed at step {_stage}: {ex}", "ERROR");
                    capture?.Dispose();
                    capture = null;
                    Report(null, "Party.CaptureFailed");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            DiagnosticLog.Error("reading stopped", ex);
            Report(null, "Party.StartFailed");
        }
        finally
        {
            Enter("waiting");
            capture?.Dispose();
        }
    }

    public void Dispose() => _cts.Cancel();
}

/// <summary>A picture kept for the diagnostic export: when, the picture (at most 960 px wide), and what was read from it.</summary>
internal sealed record RecentFrame(DateTime At, Frame Picture, string Read);

internal enum OperatorCheckStatus
{
    /// <summary>The wiki was asked; <see cref="OperatorCheck.Added"/> lists the new operators (often none).</summary>
    Checked,

    /// <summary>The wiki was asked within <see cref="PartyMonitor.MinManualCheckInterval"/>, so not again.</summary>
    CheckedRecently,

    /// <summary>A download was already running.</summary>
    AlreadyChecking,

    /// <summary>The wiki couldn't be reached or answered badly; the current copy is kept.</summary>
    Failed,
}

/// <param name="Total">Operators in the roster now.</param>
/// <param name="Since">For <see cref="OperatorCheckStatus.CheckedRecently"/>: how long ago the wiki was asked.</param>
internal sealed record OperatorCheck(OperatorCheckStatus Status, IReadOnlyList<OperatorInfo> Added, int Total, TimeSpan Since);
