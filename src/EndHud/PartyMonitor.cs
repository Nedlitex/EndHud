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

    /// <summary>With no roster at all (first start offline), the download is retried after these waits, then every 30 minutes.</summary>
    private static readonly TimeSpan[] FirstDownloadRetries =
        [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30)];

    private readonly TimeSpan _interval;
    private readonly CancellationTokenSource _cts = new();
    private volatile IntPtr _target;
    private volatile bool _active;
    private volatile SquadIdentifier? _identifier;
    private int _libraryBusy;

    public PartyMonitor(TimeSpan interval) => _interval = interval;

    /// <summary>A reading, or an i18n key describing why there is none.</summary>
    public event Action<PartyState?, string?>? Updated;

    /// <summary>Operator library progress: an i18n key and its format arguments.</summary>
    public event Action<string, object[]>? LibraryStatus;

    /// <summary>Null until the operator library has been downloaded and loaded.</summary>
    public SquadIdentifier? Identifier => _identifier;

    /// <summary>True when the first download of the operator library has failed and is being retried.</summary>
    public bool LibraryDownloadFailed { get; private set; }

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

    /// <summary>Download the current roster from the wiki now (new operators, new icons). Learned portraits are kept.</summary>
    public void UpdateLibrary() => _ = Task.Run(() => DownloadLibraryAsync(announce: true));

    public Task CorrectAsync(int slotNumber, OperatorInfo? actual) =>
        _identifier?.CorrectAsync(slotNumber, actual) ?? Task.CompletedTask;

    public void ForgetLearned()
    {
        if (_identifier is not { } identifier) return;
        identifier.Library.ForgetAllLearned();
        _identifier = new SquadIdentifier(identifier.Library); // drop cached answers too
    }

    /// <summary>
    /// Load the downloaded library if there is one; download it if not (retrying while offline), or
    /// refresh it in the background if it's old, keeping the current copy in use meanwhile.
    /// </summary>
    private async Task KeepLibraryAsync()
    {
        if (OperatorReference.Exists(ReferenceFolder)) await LoadLibraryAsync();

        bool missing = !OperatorReference.Exists(ReferenceFolder);
        if (!missing && DateTime.UtcNow - OperatorReference.DownloadedAtUtc(ReferenceFolder) < RefreshAfter) return;

        // First start (announced: names are unknown until it's done) or a quiet weekly refresh.
        for (int attempt = 0; !_cts.IsCancellationRequested; attempt++)
        {
            if (await DownloadLibraryAsync(announce: missing) || !missing) return;
            try
            {
                await Task.Delay(FirstDownloadRetries[Math.Min(attempt, FirstDownloadRetries.Length - 1)], _cts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>Download (or refresh) the reference data and reload the library. False if it failed.</summary>
    private async Task<bool> DownloadLibraryAsync(bool announce)
    {
        if (Interlocked.Exchange(ref _libraryBusy, 1) == 1) return true; // one at a time; the running one reports
        try
        {
            if (announce) LibraryStatus?.Invoke("Operators.Downloading", []);
            using var http = WikiOperatorSource.CreateHttpClient();
            var result = await OperatorReference.UpdateAsync(ReferenceFolder, http, _cts.Token);
            LibraryDownloadFailed = false;
            await LoadLibraryAsync();
            if (announce) LibraryStatus?.Invoke("Operators.Updated", [result.Operators]);
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException
                                       or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            Debug.WriteLine($"EndHud operator library: {ex}");
            if (_cts.IsCancellationRequested) return false;
            bool haveCopy = _identifier is not null;
            LibraryDownloadFailed = !haveCopy;
            // A failed background refresh is silent: the current copy keeps working.
            if (announce) LibraryStatus?.Invoke(haveCopy ? "Operators.UpdateFailed" : "Operators.FirstDownloadFailed", []);
            return false;
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
