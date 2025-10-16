namespace MintPlayer.Http;

public static class HttpClientExtensions
{
    public static async Task<HttpResult<T?>> SendAsync<T>(this HttpClient client, HttpRequestMessage message, System.Text.Json.JsonSerializerOptions? options = null, CancellationToken ct = default)
    {
        var response = await client.SendAsync(message, ct).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
        {
            // TODO: response.EnsureSuccessStatusCode();
            switch (response.Content.Headers.ContentType?.MediaType)
            {
                case null:
                    throw new NotSupportedException("Missing content type");
                case "application/json":
                    return await response.ReadJsonAsync<T>(options, ct).ConfigureAwait(false);
                case "text/xml":
                case "application/xml":
                    return await response.ReadXmlAsync<T>(ct).ConfigureAwait(false);
                case "text/plain":
                    return await response.ReadTextAsync(ct).ConfigureAwait(false);
                default:
                    throw new NotSupportedException("Unsupported content type");
            }
        }
        else
        {
            throw new HttpRequestException($"""
                HTTP {(int)response.StatusCode} {response.ReasonPhrase}
                URL: {response.RequestMessage?.RequestUri}

                HEADERS
                {string.Join(Environment.NewLine, response.Headers.Select(h => $"{h.Key}: {h.Value}"))}

                BODY
                {await response.Content.ReadAsStringAsync(ct)}
                """, null, response.StatusCode);
        }
    }

    public static async Task<Stream> FromStreamAsync(this HttpClient client, HttpRequestMessage message, CancellationToken ct = default)
    {
        var response = await client.SendAsync(message, ct).ConfigureAwait(false);
        var fileContents = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        fileContents.Position = 0;
        return fileContents;
    }
}
