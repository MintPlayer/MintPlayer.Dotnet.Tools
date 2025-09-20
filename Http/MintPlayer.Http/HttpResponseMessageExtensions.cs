using System.Net.Http.Headers;
using System.Reflection.PortableExecutable;
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
                  (string.IsNullOrWhiteSpace(body) ? "" : $"Body:\n{body}");
        throw new HttpRequestException(msg, null, response.StatusCode);
    }

    public static async Task<T?> ReadJsonAsync<T>(this HttpResponseMessage response, JsonSerializerOptions? options = null, CancellationToken ct = default)
    {
        await response.EnsureSuccessWithBodyAsync(ct);
        await using var s = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<T>(s, options, ct).ConfigureAwait(false);
    }

    public static async Task<HttpResult<T?>> ReadJsonWithMetaAsync<T>(this HttpResponseMessage response, JsonSerializerOptions? options = null, CancellationToken ct = default)
    {
        await response.EnsureSuccessWithBodyAsync(ct);
        await using var s = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var data = await JsonSerializer.DeserializeAsync<T>(s, options, ct).ConfigureAwait(false);

        return new(data, response.StatusCode, response.Version, response.Headers, response.ReasonPhrase, response.GetLocation());
    }

    public static async Task<(bool ok, T? value)> TryReadJsonAsync<T>(this HttpResponseMessage response, JsonSerializerOptions? options = null, CancellationToken ct = default)
    {
        try
        {
            await using var s = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var v = await JsonSerializer.DeserializeAsync<T>(s, options, ct).ConfigureAwait(false);
            return (true, v);
        }
        catch { return (false, default); }
    }

    public static async Task<T?> ReadXmlAsync<T>(this HttpResponseMessage response, CancellationToken ct = default)
    {
        await response.EnsureSuccessWithBodyAsync(ct);
        var serializer = new System.Xml.Serialization.XmlSerializer(typeof(T));
        await using var s = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return (T?)serializer.Deserialize(s);
    }

    public static async Task<HttpResult<T?>> ReadXmlWithMetaAsync<T>(this HttpResponseMessage response, CancellationToken ct = default)
    {
        await response.EnsureSuccessWithBodyAsync(ct);
        var serializer = new System.Xml.Serialization.XmlSerializer(typeof(T));
        await using var s = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var data = (T?)serializer.Deserialize(s);

        return new(data, response.StatusCode, response.Version, response.Headers, response.ReasonPhrase, response.GetLocation());
    }

    public static async Task<string> ReadTextAsync(this HttpResponseMessage response, CancellationToken ct = default)
    {
        await response.EnsureSuccessWithBodyAsync(ct);
        var s = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return s;
    }

    public static async Task<HttpResult<string>> ReadTextWithMetaAsync(this HttpResponseMessage response, CancellationToken ct = default)
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
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
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

