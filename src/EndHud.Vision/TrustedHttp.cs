using System.Net;
using System.Net.Http;

namespace EndHud.Vision;

/// <summary>An address EndHud refuses to download from (not one of the places it trusts for this download).</summary>
public sealed class UntrustedSourceException(string message) : HttpRequestException(message);


/// <summary>
/// Every download EndHud makes (updates, recommended rotations, the operator package) goes through here: the
/// address is checked against the places trusted for that download before it is asked, redirects are followed
/// by hand (the HTTP client must not follow them itself: <see cref="NoAutoRedirect"/>) and each one is checked
/// the same way, and a body is read only up to a size limit.
/// </summary>
public static class TrustedHttp
{
    /// <summary>GitHub sends a release download through one redirect; a few more are allowed, never a loop.</summary>
    public const int MaxRedirects = 5;

    /// <summary>The handler settings for a client used here: redirects are followed by <see cref="GetAsync"/>, which checks them.</summary>
    public static SocketsHttpHandler NoAutoRedirect() => new() { AllowAutoRedirect = false };

    /// <summary>
    /// GET <paramref name="url"/>, following redirects only to trusted addresses. Throws
    /// <see cref="UntrustedSourceException"/> (an <see cref="HttpRequestException"/>, so callers treat it as a
    /// failed download) naming the refused host, before anything is sent to it.
    /// </summary>
    /// <param name="trusted">May the first address be asked?</param>
    /// <param name="trustedRedirect">May a redirect be followed there? Null: the same rule as <paramref name="trusted"/>.</param>
    /// <param name="prepare">Headers for each request (User-Agent, Accept).</param>
    public static async Task<HttpResponseMessage> GetAsync(HttpClient http, Uri url, Func<Uri, bool> trusted, Action<HttpRequestMessage>? prepare,
        HttpCompletionOption completion, CancellationToken ct, Func<Uri, bool>? trustedRedirect = null)
    {
        if (!trusted(url)) throw Refused(url);
        for (int hop = 0; ; hop++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            prepare?.Invoke(request);
            var response = await http.SendAsync(request, completion, ct);
            if (!IsRedirect(response.StatusCode)) return response;

            var location = response.Headers.Location;
            response.Dispose();
            if (location is null) throw new HttpRequestException($"{url.Host}: a redirect without an address");
            if (hop >= MaxRedirects) throw new HttpRequestException($"{url.Host}: more than {MaxRedirects} redirects");
            var next = location.IsAbsoluteUri ? location : new Uri(url, location);
            if (!(trustedRedirect ?? trusted)(next)) throw Refused(next);
            url = next;
        }
    }

    /// <summary>
    /// The files synced from a repository (recommended rotations, the operator package): its folder listings on
    /// GitHub's API (<c>https://api.github.com/repos/&lt;repository&gt;/…</c>) and its files as GitHub serves them
    /// (<c>https://raw.githubusercontent.com/&lt;repository&gt;/…</c>); https, the usual port, no user name.
    /// </summary>
    public static Func<Uri, bool> GitHubRepository(string repository)
    {
        string path = "/" + repository.Trim('/') + "/";
        return url => url.IsAbsoluteUri && url.Scheme == Uri.UriSchemeHttps && url.IsDefaultPort && url.UserInfo.Length == 0 && url.IdnHost.ToLowerInvariant() switch
        {
            "api.github.com" => url.AbsolutePath.StartsWith("/repos" + path, StringComparison.OrdinalIgnoreCase),
            "raw.githubusercontent.com" => url.AbsolutePath.StartsWith(path, StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }

    private static UntrustedSourceException Refused(Uri url) =>
        new($"refused to download from {(url.IsAbsoluteUri ? $"{url.Scheme}://{url.Host}" : url.OriginalString)}: not a source EndHud trusts for this");

    private static bool IsRedirect(HttpStatusCode status) => status is HttpStatusCode.MovedPermanently or HttpStatusCode.Found
        or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    /// <summary>The whole body, refused (an <see cref="InvalidDataException"/>) as soon as it is over <paramref name="maxBytes"/>.</summary>
    public static async Task<byte[]> ReadCappedAsync(HttpResponseMessage response, long maxBytes, CancellationToken ct)
    {
        await using var body = await OpenCappedAsync(response, maxBytes, ct);
        using var copy = new MemoryStream();
        await body.CopyToAsync(copy, ct);
        return copy.ToArray();
    }

    /// <summary>The body as a stream that throws an <see cref="InvalidDataException"/> once more than <paramref name="maxBytes"/> are read.</summary>
    public static async Task<Stream> OpenCappedAsync(HttpResponseMessage response, long maxBytes, CancellationToken ct)
    {
        if (response.Content.Headers.ContentLength is long length && length > maxBytes) throw TooLarge(maxBytes);
        return new CappedStream(await response.Content.ReadAsStreamAsync(ct), maxBytes);
    }

    private static InvalidDataException TooLarge(long maxBytes) => new($"larger than {maxBytes} bytes, the most accepted for it");

    /// <summary>Reads through to <paramref name="inner"/>, throwing once more than <paramref name="maxBytes"/> came out of it.</summary>
    public sealed class CappedStream(Stream inner, long maxBytes) : Stream
    {
        private long _read;

        public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));

        public override int Read(Span<byte> buffer) => Count(inner.Read(buffer));

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => Count(await inner.ReadAsync(buffer, ct));

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();

        private int Count(int read)
        {
            _read += read;
            if (_read > maxBytes) throw TooLarge(maxBytes);
            return read;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _read; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
