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
    /// The operator library on this machine: the roster, avatars and skill icons downloaded from
    /// the wiki (reference/), and the user's learned portraits and calibration. EndHud ships no
    /// game art, so the first start downloads it.
    /// </summary>
    public static string LibraryFolder { get; } = Path.Combine(ConfigStore.Folder, "operators");

    private static string ReferenceFolder => Path.Combine(LibraryFolder, OperatorLibrary.ReferenceFolderName);

    /// <summary>A downloaded roster older than this is refreshed in the background, to pick up new operators.</summary>
    private static readonly TimeSpan RefreshAfter = TimeSpan.FromDays(7);

    /// <summary>
    /// A manual check doesn't ask the wiki again if it was asked this recently (polite to the wiki,
    /// whose volunteers pay for it); it reports that it's up to date as of then.
    /// </summary>
    public static readonly TimeSpan MinManualCheckInterval = TimeSpan.FromMinutes(15);

    /// <summary>While EndHud runs, how often it looks whether the roster is due for its weekly refresh.</summary>
    private static readonly TimeSpan RefreshCheckEvery = TimeSpan.FromHours(6);

    /// <summary>With no roster at all (first start offline), the download is retried after these waits, then every 30 minutes.</summary>
    private static readonly TimeSpan[] FirstDownloadRetries =
        [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30)];

    private readonly TimeSpan _interval;
    private readonly CancellationTokenSource _cts = new();
    private volatile IntPtr _target;
    private volatile bool _active;
    private volatile SquadIdentifier? _identifier;
    private int _libraryBusy;
    private DateTime _lastWikiCheckUtc = DateTime.MinValue;

    public PartyMonitor(TimeSpan interval) => _interval = interval;

    /// <summary>A reading, or an i18n key describing why there is none.</summary>
    public event Action<PartyState?, string?>? Updated;

    /// <summary>Operator library progress: an i18n key and its format arguments.</summary>
    public event Action<string, object[]>? LibraryStatus;

    /// <summary>Null until the operator library has been downloaded and loaded.</summary>
    public SquadIdentifier? Identifier => _identifier;

    /// <summary>True when the first download of the operator library has failed and is being retried.</summary>
    public bool LibraryDownloadFailed { get; private set; }

    /// <summary>The weekly background refresh found operators that weren't there before.</summary>
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
    /// Check the wiki for new operators now (and new icons), unless it was checked in the last
    /// <see cref="MinManualCheckInterval"/>. Learned portraits are kept.
    /// </summary>
    public async Task<OperatorCheck> CheckOperatorsAsync()
    {
        var since = DateTime.UtcNow - _lastWikiCheckUtc;
        int total = _identifier?.Library.Operators.Count ?? 0;
        if (since < MinManualCheckInterval) return new OperatorCheck(OperatorCheckStatus.CheckedRecently, [], total, since);
        var (update, busy) = await DownloadLibraryAsync(announce: true);
        return busy ? new OperatorCheck(OperatorCheckStatus.AlreadyChecking, [], total, TimeSpan.Zero)
            : update is null ? new OperatorCheck(OperatorCheckStatus.Failed, [], total, TimeSpan.Zero)
            : new OperatorCheck(OperatorCheckStatus.Checked, update.Added, update.Operators, TimeSpan.Zero);
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
    /// Load the downloaded library if there is one; download it if not (retrying while offline). Then,
    /// while EndHud runs, refresh it in the background whenever it's older than a week, keeping the
    /// current copy in use meanwhile and announcing new operators.
    /// </summary>
    private async Task KeepLibraryAsync()
    {
        try
        {
            if (OperatorReference.Exists(ReferenceFolder)) await LoadLibraryAsync();

            // First start: names are unknown until it's done, so it's announced and retried.
            for (int attempt = 0; !OperatorReference.Exists(ReferenceFolder); attempt++)
            {
                if ((await DownloadLibraryAsync(announce: true)).Update is not null) break;
                await Task.Delay(FirstDownloadRetries[Math.Min(attempt, FirstDownloadRetries.Length - 1)], _cts.Token);
            }

            while (!_cts.IsCancellationRequested)
            {
                if (DateTime.UtcNow - OperatorReference.DownloadedAtUtc(ReferenceFolder) >= RefreshAfter
                    && DateTime.UtcNow - _lastWikiCheckUtc >= RefreshCheckEvery)
                {
                    var (update, _) = await DownloadLibraryAsync(announce: false); // quiet: the current copy keeps working
                    if (update is { Added.Count: > 0 }) NewOperators?.Invoke(update.Added);
                }
                await Task.Delay(RefreshCheckEvery, _cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>
    /// Download (or refresh) the reference data and reload the library. Update is null if it failed;
    /// Busy is true if another download was already running (that one reports).
    /// </summary>
    private async Task<(OperatorUpdate? Update, bool Busy)> DownloadLibraryAsync(bool announce)
    {
        if (Interlocked.Exchange(ref _libraryBusy, 1) == 1) return (null, true);
        try
        {
            _lastWikiCheckUtc = DateTime.UtcNow;
            if (announce) LibraryStatus?.Invoke("Operators.Downloading", []);
            using var http = WikiOperatorSource.CreateHttpClient();
            var update = await OperatorReference.UpdateAsync(ReferenceFolder, http, WikiPoliteness.Default, _cts.Token);
            LibraryDownloadFailed = false;
            await LoadLibraryAsync();
            if (announce) LibraryStatus?.Invoke("Operators.Updated", [update.Operators]);
            return (update, false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException
                                       or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            Debug.WriteLine($"EndHud operator library: {ex}");
            if (_cts.IsCancellationRequested) return (null, false);
            bool haveCopy = _identifier is not null;
            LibraryDownloadFailed = !haveCopy;
            if (announce) LibraryStatus?.Invoke(haveCopy ? "Operators.UpdateFailed" : "Operators.FirstDownloadFailed", []);
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
                    var state = tracker.Track(await reader.ReadAsync(frame.Crop(client)));
                    if (_identifier is { } identifier) state = await identifier.IdentifyAsync(state);
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
