using System.Net.Http.Headers;
using System.Runtime.Serialization;
using System.Text.Json;

namespace MintPlayer.Http;

public static class HttpResponseMessageExtensions
{
    public static async Task EnsureSuccessWithBodyAsync(this HttpResponseMessage response, CancellationToken ct = default)
    {
        if (response.IsSuccessStatusCode) return;

        string body = string.Empty;
        try
        {
            body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch { /* ignore */ }

        var msg = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}\n" +
                  $"URL: {response.RequestMessage?.RequestUri}\n" +
                  (string.IsNullOrWhiteSpace(body) ? string.Empty : $"Body:\n{body}");
        throw new HttpRequestException(msg, null, response.StatusCode);
    }

    public static async Task<HttpResult<T?>> ReadJsonAsync<T>(this HttpResponseMessage response, JsonSerializerOptions? options = null, CancellationToken ct = default)
    {
        await response.EnsureSuccessWithBodyAsync(ct);
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);

        // Copy rather than mutate. A JsonSerializerOptions becomes read-only the first time
        // it is used, so assigning PropertyNameCaseInsensitive directly on the caller's
        // instance threw InvalidOperationException on the second call with the same options
        // — and silently changed the caller's options on the first.
        options = options is null
            ? new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true }
            : new JsonSerializerOptions(options) { PropertyNameCaseInsensitive = true };

        var data = await JsonSerializer.DeserializeAsync<T>(stream, options, ct).ConfigureAwait(false);

        return new(data, response.StatusCode, response.Version, response.Headers, response.ReasonPhrase, response.GetLocation());
    }

    public static async Task<HttpResult<T?>> ReadXmlAsync<T>(this HttpResponseMessage response, CancellationToken ct = default)
    {
        await response.EnsureSuccessWithBodyAsync(ct);
        //var serializer = new System.Xml.Serialization.XmlSerializer(typeof(T));
        var serializer = new DataContractSerializer(typeof(T));
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        //using var xmlReader = System.Xml.XmlReader.Create(stream, new System.Xml.XmlReaderSettings { Async = true });
        var data = (T?)serializer.ReadObject(stream);

        return new(data, response.StatusCode, response.Version, response.Headers, response.ReasonPhrase, response.GetLocation());
    }

    public static async Task<HttpResult<string?>> ReadTextAsync(this HttpResponseMessage response, CancellationToken ct = default)
    {
        await response.EnsureSuccessWithBodyAsync(ct);
        var data = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        return new(data, response.StatusCode, response.Version, response.Headers, response.ReasonPhrase, response.GetLocation());
    }

    public static EntityTagHeaderValue? GetETag(this HttpResponseMessage response)
        => response.Headers.ETag;

    public static Uri? GetLocation(this HttpResponseMessage response)
        => response.Headers.Location;

    public static (Uri? next, Uri? prev, Uri? first, Uri? last) GetPaginationLinks(this HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Link", out var values))
            return (null, null, null, null);

        Uri? next = null, prev = null, first = null, last = null;
        foreach (var v in values)
        {
            foreach (var part in v.Split(','))
            {
                var segs = part.Split(';', StringSplitOptions.TrimEntries);
                var url = segs[0].Trim().Trim('<', '>');
                var rel = segs.Skip(1).FirstOrDefault(s => s.StartsWith("rel=", StringComparison.OrdinalIgnoreCase))?.Split('=')[1].Trim('"');

                // UriKind.Absolute alone is not enough, and the difference is platform-dependent:
                // a relative link target like "/p2" is rejected on Windows but parses as the
                // absolute file URI "file:///p2" on Linux, because a leading slash is a valid
                // Unix file path. That produced a bogus file:// URI a caller might then try to
                // fetch. Requiring http/https makes the result identical on both platforms and
                // keeps the Windows behaviour the library already shipped with.
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                    (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                {
                    switch (rel) { case "next": next = uri; break; case "prev": prev = uri; break; case "first": first = uri; break; case "last": last = uri; break; }
                }
            }
        }
        return (next, prev, first, last);
    }

    public static DateTimeOffset? GetRetryAfter(this HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter is { } ra)
            return ra.Date ?? (ra.Delta is { } d ? DateTimeOffset.UtcNow + d : null);
        return null;
    }

    public static async Task SaveAsFileAsync(this HttpResponseMessage response, string path, IProgress<long>? progress = null, CancellationToken ct = default)
    {
        await response.EnsureSuccessWithBodyAsync(ct);
        await using var src = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var dst = File.Create(path);
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await src.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, read), ct);
            total += read;
            progress?.Report(total);
        }
    }
}

