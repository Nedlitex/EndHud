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
    /// <summary>Operator icons, names and learned portraits: %APPDATA%\EndHud\operators.</summary>
    public static string LibraryFolder { get; } = Path.Combine(ConfigStore.Folder, "operators");

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

    /// <summary>Null until the operator library has loaded (or if it couldn't be downloaded).</summary>
    public SquadIdentifier? Identifier => _identifier;

    /// <summary>Called from the UI thread whenever the game window or HUD visibility changes.</summary>
    public void SetTarget(IntPtr gameWindow, bool active)
    {
        _target = gameWindow;
        _active = active;
    }

    public void Start()
    {
        _ = Task.Run(() => LoadLibraryAsync(forceDownload: false));
        _ = Task.Run(RunAsync);
    }

    /// <summary>Re-download the roster from the wiki (new operators, new icons). Learned portraits are kept.</summary>
    public void UpdateLibrary() => _ = Task.Run(() => LoadLibraryAsync(forceDownload: true));

    public Task CorrectAsync(int slotNumber, OperatorInfo? actual) =>
        _identifier?.CorrectAsync(slotNumber, actual) ?? Task.CompletedTask;

    public void ForgetLearned()
    {
        if (_identifier is not { } identifier) return;
        identifier.Library.ForgetAllLearned();
        _identifier = new SquadIdentifier(identifier.Library); // drop cached answers too
    }

    private async Task LoadLibraryAsync(bool forceDownload)
    {
        if (Interlocked.Exchange(ref _libraryBusy, 1) == 1) return;
        try
        {
            // First start: install the bundled library so recognition works offline straight away.
            if (OperatorLibrary.SeedIfMissing(LibraryFolder, Path.Combine(AppContext.BaseDirectory, "seed", "operators")))
                LibraryStatus?.Invoke("Operators.Seeded", []);

            var library = await OperatorLibrary.LoadAsync(LibraryFolder);
            if (library.IsEmpty || forceDownload)
            {
                LibraryStatus?.Invoke("Operators.Downloading", []);
                using var http = WikiOperatorSource.CreateHttpClient();
                var roster = await WikiOperatorSource.DownloadAsync(LibraryFolder, http, _cts.Token);
                library = await OperatorLibrary.LoadAsync(LibraryFolder);
                LibraryStatus?.Invoke("Operators.Updated", [roster.Count]);
            }
            _identifier = new SquadIdentifier(library);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException
                                       or InvalidDataException or JsonException)
        {
            Debug.WriteLine($"EndHud operator library: {ex}");
            if (!_cts.IsCancellationRequested) LibraryStatus?.Invoke("Operators.DownloadFailed", []);
        }
        finally
        {
            Interlocked.Exchange(ref _libraryBusy, 0);
        }
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
                        captured = hwnd;
                    }

                    var frame = await capture.GetFrameAsync(ct);
                    var client = Native.ClientAreaInCapture(hwnd, frame.Width, frame.Height);
                    if (client.IsEmpty) continue;
                    var state = await reader.ReadAsync(frame.Crop(client));
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
