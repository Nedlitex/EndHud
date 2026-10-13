using System.Diagnostics;
using System.IO;
using System.Net.Http;
using EndHud.Vision.Rotations;

namespace EndHud;

/// <summary>
/// Keeps the recommended rotations (<see cref="RotationLibrary.Published"/>) the same as the
/// repository's <c>rotations/</c> folder (<see cref="RotationSync"/>): soon after start and then
/// daily while <see cref="HudConfig.CheckForUpdates"/> is on, with the tray's "check for updates",
/// and when the editor restores deleted ones. One sync runs at a time.
/// </summary>
internal sealed class PublishedRotationService(RotationLibrary library) : IDisposable
{
    public static readonly string Api = RotationSync.ContentsApi(ReleaseChecker.Repository);

    private static readonly TimeSpan FirstCheckAfter = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan CheckEvery = TimeSpan.FromDays(1);

    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _one = new(1, 1);

    /// <summary>A sync changed the published rotations (raised on a worker thread). Automatic is false when the player asked.</summary>
    public event Action<RotationSyncResult, bool>? Changed;

    public void Start(Func<bool> enabled) => _ = Task.Run(async () =>
    {
        try
        {
            await Task.Delay(FirstCheckAfter, _cts.Token);
            while (!_cts.IsCancellationRequested)
            {
                if (enabled()) await SyncAsync(automatic: true);
                await Task.Delay(CheckEvery, _cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }
    });

    /// <summary>Sync now. Null when GitHub couldn't be reached or the folder couldn't be listed.</summary>
    public async Task<RotationSyncResult?> SyncAsync(bool automatic = false)
    {
        await _one.WaitAsync(_cts.Token);
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            var result = await RotationSync.SyncAsync(library, http, Api, ReleaseChecker.UserAgent, _cts.Token);
            if (result is { Changed: true }) Changed?.Invoke(result, automatic);
            return result;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException
                                       && !_cts.IsCancellationRequested)
        {
            Debug.WriteLine($"EndHud published rotations: {ex.Message}");
            return null;
        }
        finally
        {
            _one.Release();
        }
    }

    public void Dispose() => _cts.Cancel();
}
