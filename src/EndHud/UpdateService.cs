using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace EndHud;

internal enum UpdateStatus
{
    /// <summary>A newer release is out (<see cref="UpdateCheck.Release"/>).</summary>
    NewVersion,

    /// <summary>This is the latest release (or newer).</summary>
    UpToDate,

    /// <summary>A development build: there's no version to compare.</summary>
    DevelopmentBuild,

    /// <summary>GitHub couldn't be reached, or has no published release to compare with.</summary>
    Unavailable,
}

internal sealed record UpdateCheck(UpdateStatus Status, ReleaseInfo? Release);

/// <summary>
/// Looks for a newer EndHud on GitHub: once soon after start and then daily while running (when
/// <see cref="HudConfig.CheckForUpdates"/> is on), and whenever the user asks. GitHub allows 60
/// unauthenticated requests an hour; a manual check reuses the last answer if it's under a minute old.
/// </summary>
internal sealed class UpdateService : IDisposable
{
    // Soon after start: a first start (e.g. a portable copy just downloaded) gets the latest right away.
    private static readonly TimeSpan FirstCheckAfter = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CheckEvery = TimeSpan.FromDays(1);
    private static readonly TimeSpan ReuseAnswerFor = TimeSpan.FromMinutes(1);

    private readonly CancellationTokenSource _cts = new();
    private readonly Func<HttpClient> _http;
    private readonly Version? _current;
    private readonly string _api;
    private UpdateCheck? _last;
    private DateTime _lastAtUtc = DateTime.MinValue;

    /// <summary>Checks for the running build (<see cref="ReleaseChecker.Current"/>) against GitHub (or <paramref name="api"/>).</summary>
    public UpdateService(string api = ReleaseChecker.LatestReleaseApi)
        : this(() => GitHubHttp.Create(TimeSpan.FromSeconds(20)), ReleaseChecker.Current, api)
    {
    }

    /// <param name="current">This build's version; null for a development build.</param>
    internal UpdateService(Func<HttpClient> http, Version? current, string api = ReleaseChecker.LatestReleaseApi) =>
        (_http, _current, _api) = (http, current, api);

    /// <summary>The newer release found by the last check, if any.</summary>
    public ReleaseInfo? Available => _last is { Status: UpdateStatus.NewVersion } c ? c.Release : null;

    /// <summary>A check (automatic or manual) found a newer release than before.</summary>
    public event Action<ReleaseInfo>? NewVersionFound;

    /// <summary>Start the automatic checks; each one happens only if <paramref name="enabled"/> says so at the time.</summary>
    public void Start(Func<bool> enabled) => _ = Task.Run(async () =>
    {
        try
        {
            await Task.Delay(FirstCheckAfter, _cts.Token);
            while (!_cts.IsCancellationRequested)
            {
                bool failed = enabled() && (await CheckAsync()).Status == UpdateStatus.Unavailable;
                await Task.Delay(failed ? GitHubHttp.RetryAfterFailure : CheckEvery, _cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }
    });

    public async Task<UpdateCheck> CheckAsync()
    {
        if (_current is not { } current) return new UpdateCheck(UpdateStatus.DevelopmentBuild, null);
        if (_last is { } recent && DateTime.UtcNow - _lastAtUtc < ReuseAnswerFor) return recent;

        UpdateCheck check;
        try
        {
            using var http = _http();
            var latest = await ReleaseChecker.LatestAsync(http, _cts.Token, _api);
            check = latest is null ? new UpdateCheck(UpdateStatus.Unavailable, null)
                : latest.Version > current ? new UpdateCheck(UpdateStatus.NewVersion, latest)
                : new UpdateCheck(UpdateStatus.UpToDate, latest);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException
                                       or JsonException or InvalidDataException && !_cts.IsCancellationRequested)
        {
            DiagnosticLog.Warn($"EndHud update check failed: {GitHubHttp.Explain(ex)}");
            check = new UpdateCheck(UpdateStatus.Unavailable, null);
        }

        bool newer = check.Status == UpdateStatus.NewVersion && check.Release!.Version != Available?.Version;
        (_last, _lastAtUtc) = (check, DateTime.UtcNow);
        if (newer) NewVersionFound?.Invoke(check.Release!);
        return check;
    }

    public void Dispose() => _cts.Cancel();
}
