using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using EndHud.Vision;
using EndHud.Vision.Capture;
using EndHud.Vision.Operators;

namespace EndHud;

/// <summary>
/// Background loop: while the HUD is showing over the game, grab a frame of the game window
/// every interval, read the squad panel and name the operators. Events fire on worker threads.
/// </summary>
internal sealed class PartyMonitor : IDisposable
{
    /// <summary>
    /// The operator library on this machine: the roster, avatars and skill icons (reference/, from
    /// the operator package EndHud carries and updates from GitHub: see OperatorPackage), and the
    /// user's learned portraits and calibration.
    /// </summary>
    public static string LibraryFolder { get; } = Path.Combine(ConfigStore.Folder, "operators");

    private static string ReferenceFolder => Path.Combine(LibraryFolder, OperatorLibrary.ReferenceFolderName);

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

    private static readonly TimeSpan BossStatusEvery = TimeSpan.FromSeconds(1);
    private int _libraryBusy;
    private DateTime _lastCheckUtc = DateTime.MinValue;

    public PartyMonitor(TimeSpan interval) => _interval = interval;

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
        _target = gameWindow;
        _active = active;
    }

    public void Start()
    {
        _ = Task.Run(KeepLibraryAsync);
        _ = Task.Run(RunAsync);
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
                if (OperatorPackage.BuiltIn() is { } builtIn) OperatorPackage.InstallIfNewer(ReferenceFolder, builtIn);
            }
            catch (InvalidDataException ex)
            {
                Debug.WriteLine($"EndHud operator package: {ex.Message}"); // keep what's installed
            }
            await LoadLibraryAsync();

            while (!_cts.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), _cts.Token); // soon after start, like the other update checks
                if (UpdatesEnabled() && DateTime.UtcNow - _lastCheckUtc >= RefreshCheckEvery)
                {
                    var (result, _) = await SyncPackageAsync(announce: false);
                    if (result is { Added.Count: > 0 }) NewOperators?.Invoke(result.Added);
                }
                await Task.Delay(RefreshCheckEvery, _cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
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
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var result = await OperatorPackageSync.SyncAsync(ReferenceFolder, http,
                OperatorPackageSync.ContentsApi(ReleaseChecker.Repository), ReleaseChecker.UserAgent, _cts.Token);
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
            Debug.WriteLine($"EndHud operator package: {ex}");
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
        var library = await OperatorLibrary.LoadAsync(LibraryFolder);
        // An empty library recognises nobody; leave the identifier unset so the menu says why.
        _identifier = library.IsEmpty ? null : new SquadIdentifier(library);
        _bossStatuses = library.IsEmpty ? null : new BossStatusReader(library);
        _teamBuffs = library.IsEmpty ? null : new TeamBuffReader(library);
    }

    private async Task RunAsync()
    {
        PartyReader reader;
        try
        {
            reader = new PartyReader(new TextRecognizer());
        }
        catch (InvalidOperationException)
        {
            Updated?.Invoke(null, "Party.OcrUnavailable");
            return;
        }

        WindowCapture? capture = null;
        IntPtr captured = IntPtr.Zero;
        var tracker = new PartyTracker();
        var bossClock = Stopwatch.StartNew();
        var ct = _cts.Token;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(_interval, ct);

                IntPtr hwnd = _target;
                if (!_active || hwnd == IntPtr.Zero)
                {
                    // Stop capturing while the HUD is hidden; restart on demand.
                    capture?.Dispose();
                    capture = null;
                    continue;
                }

                try
                {
                    if (capture is null || captured != hwnd)
                    {
                        capture?.Dispose();
                        capture = new WindowCapture(hwnd);
                        if (captured != hwnd) tracker = new PartyTracker(); // another game window: nothing carries over
                        captured = hwnd;
                    }

                    var frame = await capture.GetFrameAsync(ct);
                    var client = Native.ClientAreaInCapture(hwnd, frame.Width, frame.Height);
                    if (client.IsEmpty) continue;
                    // Death first, so a fallen operator's death mark is never matched as a portrait.
                    var gameFrame = frame.Crop(client);
                    var state = tracker.Track(await reader.ReadAsync(gameFrame));
                    if (_identifier is { } identifier)
                    {
                        state = await identifier.IdentifyAsync(state);
                        // The controlled operator's own counter (e.g. 熔火): drawn differently per
                        // operator, so read once we know who it is.
                        state = state with { Counters = OperatorCounters.ReadControlled(gameFrame, state) };
                        // Team buffs (Link) beside it: drawn in combat only.
                        if (state.InCombat && _teamBuffs is { } buffs) state = state with { Buffs = buffs.Read(gameFrame) };
                        // The boss's statuses take ~70 ms to read at 4K: only when the rotation asks,
                        // and at most once a second (they change far more slowly than skill points).
                        if (ReadBossStatuses && _bossStatuses is { } boss && bossClock.Elapsed >= BossStatusEvery)
                        {
                            state = state with { BossStatuses = boss.Read(gameFrame) };
                            bossClock.Restart();
                        }
                    }
                    Updated?.Invoke(state, null);
                }
                catch (TimeoutException)
                {
                    // The window didn't redraw (Windows only sends frames on change); try next tick.
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Debug.WriteLine($"EndHud capture failed: {ex}");
                    capture?.Dispose();
                    capture = null;
                    Updated?.Invoke(null, "Party.CaptureFailed");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            capture?.Dispose();
        }
    }

    public void Dispose() => _cts.Cancel();
}

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
