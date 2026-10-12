using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;

namespace EndHud.Vision.Operators;

/// <summary>
/// How EndHud treats the community wiki, which is run by volunteers on shared hosting. Following
/// the MediaWiki API etiquette:
/// <list type="bullet">
/// <item>One request at a time, from the whole process, with a pause between them
/// (<see cref="ApiInterval"/> for API calls, a shorter <see cref="FileInterval"/> for images,
/// which the wiki serves from a CDN).</item>
/// <item>API calls send <c>maxlag</c>, so when the wiki's database is behind it answers "busy"
/// instead of doing the work; EndHud then waits and retries.</item>
/// <item>A busy answer (HTTP 429 or 5xx, or maxlag) is retried after the wiki's Retry-After (up to
/// <see cref="MaxRetryAfter"/>) or <see cref="RetryDelays"/>; then EndHud gives up until the next
/// scheduled check.</item>
/// <item>Requests carry a User-Agent naming EndHud and where to find it (see
/// <see cref="WikiOperatorSource.CreateHttpClient"/>).</item>
/// </list>
/// How often EndHud checks at all is limited by its callers: weekly in the background, and a
/// manual check at most every 15 minutes.
/// </summary>
public sealed class WikiPoliteness
{
    /// <summary>The app's rules; one instance, so all its wiki traffic shares one queue.</summary>
    public static readonly WikiPoliteness Default = new();

    public TimeSpan ApiInterval { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan FileInterval { get; init; } = TimeSpan.FromMilliseconds(100);
    public TimeSpan[] RetryDelays { get; init; } = [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(45)];
    public TimeSpan MaxRetryAfter { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Asked of the wiki's database on every API call: answer "busy" if more than this many seconds behind.</summary>
    public int MaxLagSeconds { get; init; } = 5;

    private readonly SemaphoreSlim _queue = new(1, 1);
    private DateTime _lastRequest = DateTime.MinValue;

    /// <summary>Waits for this request's turn, keeping <paramref name="interval"/> since the previous one.</summary>
    internal async Task<IDisposable> TurnAsync(TimeSpan interval, CancellationToken ct)
    {
        await _queue.WaitAsync(ct);
        try
        {
            var wait = _lastRequest + interval - DateTime.UtcNow;
            if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
        }
        catch
        {
            _queue.Release();
            throw;
        }
        return new Turn(this);
    }

    private sealed class Turn(WikiPoliteness owner) : IDisposable
    {
        private bool _done;

        public void Dispose()
        {
            if (_done) return;
            _done = true;
            owner._lastRequest = DateTime.UtcNow;
            owner._queue.Release();
        }
    }
}

/// <summary>An <see cref="HttpClient"/> that follows <see cref="WikiPoliteness"/>.</summary>
internal sealed class WikiClient(HttpClient http, WikiPoliteness rules)
{
    /// <summary>An API call (maxlag added), parsed. A maxlag "busy" answer is waited out and retried.</summary>
    public async Task<JsonNode> GetJsonAsync(string url, CancellationToken ct)
    {
        url += (url.Contains('?') ? "&" : "?") + "maxlag=" + rules.MaxLagSeconds;
        for (int attempt = 0; ; attempt++)
        {
            var (body, retryAfter) = await GetAsync(url, rules.ApiInterval, ct);
            var json = JsonNode.Parse(body) ?? throw new InvalidDataException($"Empty response from {url}");
            if (json["error"]?["code"]?.GetValue<string>() != "maxlag") return json;
            if (attempt >= rules.RetryDelays.Length)
                throw new HttpRequestException("The wiki is busy (maxlag); try again later.", null, HttpStatusCode.ServiceUnavailable);
            await Task.Delay(Delay(retryAfter, attempt), ct);
        }
    }

    /// <summary>An image or other file.</summary>
    public async Task<byte[]> GetBytesAsync(string url, CancellationToken ct) => (await GetAsync(url, rules.FileInterval, ct)).Body;

    /// <summary>
    /// GET in turn, with retries: a busy wiki (429 or 5xx), a dropped connection or a timeout is
    /// tried again; any other error status fails at once.
    /// </summary>
    private async Task<(byte[] Body, TimeSpan? RetryAfter)> GetAsync(string url, TimeSpan interval, CancellationToken ct)
    {
        for (int attempt = 0; ; attempt++)
        {
            bool last = attempt >= rules.RetryDelays.Length;
            TimeSpan? retryAfter = null;
            try
            {
                using var turn = await rules.TurnAsync(interval, ct);
                using var response = await http.GetAsync(url, ct);
                retryAfter = response.Headers.RetryAfter?.Delta;
                if (response.IsSuccessStatusCode) return (await response.Content.ReadAsByteArrayAsync(ct), retryAfter);
                bool busy = response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500;
                if (!busy || last) response.EnsureSuccessStatusCode();
            }
            catch (Exception ex) when (ex is HttpRequestException { StatusCode: null } or TaskCanceledException
                                       && !ct.IsCancellationRequested && !last)
            {
                // Connection failure or timeout: try again.
            }
            await Task.Delay(Delay(retryAfter, attempt), ct);
        }
    }

    private TimeSpan Delay(TimeSpan? retryAfter, int attempt) =>
        retryAfter is { } w && w > TimeSpan.Zero ? (w < rules.MaxRetryAfter ? w : rules.MaxRetryAfter) : rules.RetryDelays[attempt];
}
