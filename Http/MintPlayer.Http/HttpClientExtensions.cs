namespace MintPlayer.Http;

public static class HttpClientExtensions
{
    public static async Task<HttpResult<T?>> SendAsync<T>(this HttpClient client, HttpRequestMessage message, System.Text.Json.JsonSerializerOptions? options = null, CancellationToken ct = default)
    {
        var response = await client.SendAsync(message, ct).ConfigureAwait(false);
        var value = response.Headers.GetValues("Content-Type") switch
        {
            var ctype when ctype.Contains("application/json") => await response.ReadJsonAsync<T>(options, ct).ConfigureAwait(false),
            var ctype when ctype.Contains("text/xml") || ctype.Contains("application/xml") => await response.ReadXmlAsync<T>(ct).ConfigureAwait(false),
            var ctype when ctype.Contains("text/plain") && typeof(T) == typeof(string) => await response.ReadTextAsync(ct).ConfigureAwait(false),
            _ => throw new NotSupportedException("Unsupported content type"),
        };
        return value;
    }

    public static async Task<Stream> FromStreamAsync(this HttpClient client, HttpRequestMessage message, CancellationToken ct = default)
    {
        var response = await client.SendAsync(message, ct).ConfigureAwait(false);
        var fileContents = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        fileContents.Position = 0;
        return fileContents;
    }
}
