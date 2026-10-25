using System.Diagnostics;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;

namespace EndHud;

/// <summary>
/// The HTTP client every GitHub request goes through (EndHud updates, recommended rotations, the
/// operator package): each request is logged with its host and path, status and time, and a
/// failure with its whole chain of causes. "The SSL connection could not be established" alone
/// doesn't say whether the connection was reset (a network between the player and GitHub) or the
/// certificate refused (a clock, a proxy, an antivirus); its inner exceptions do.
/// </summary>
internal static class GitHubHttp
{
    /// <summary>
    /// After a failed check, when to try again instead of the usual hours or a day: GitHub is often
    /// reachable one minute and not the next from where many players are (seen in players' logs: the
    /// same request answered, then six minutes later "The SSL connection could not be established").
    /// </summary>
    public static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(30);

    /// <summary>
    /// A client that never follows a redirect by itself: every caller goes through <see cref="EndHud.Vision.TrustedHttp"/>,
    /// which follows them by hand, each checked against where that download may come from.
    /// </summary>
    public static HttpClient Create(TimeSpan timeout) => new(new LoggingHandler(EndHud.Vision.TrustedHttp.NoAutoRedirect())) { Timeout = timeout };

    /// <summary>An exception and every cause under it, one "Type: message" each, socket errors with their code.</summary>
    public static string Explain(Exception ex)
    {
        var sb = new StringBuilder();
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (sb.Length > 0) sb.Append(" <- ");
            sb.Append(e.GetType().Name);
            if (e is SocketException socket) sb.Append($" ({socket.SocketErrorCode}, {socket.ErrorCode})");
            sb.Append(": ").Append(e.Message);
        }
        return sb.ToString();
    }

    private sealed class LoggingHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string what = $"{request.Method} {request.RequestUri?.Host}{request.RequestUri?.AbsolutePath}";
            var clock = Stopwatch.StartNew();
            try
            {
                var response = await base.SendAsync(request, ct);
                DiagnosticLog.Info($"{what}: {(int)response.StatusCode} in {clock.ElapsedMilliseconds} ms");
                return response;
            }
            catch (Exception ex)
            {
                // A timeout arrives as a cancellation too (HttpClient.Timeout): logged like any failure.
                DiagnosticLog.Warn($"{what}: failed after {clock.ElapsedMilliseconds} ms: {Explain(ex)}");
                throw;
            }
        }
    }
}
