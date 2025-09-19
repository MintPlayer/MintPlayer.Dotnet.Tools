namespace MintPlayer.Http;

public static class HttpClientExtensions
{
    public static async Task<T?> FromJsonAsync<T>(this HttpClient client, HttpRequestMessage message, System.Text.Json.JsonSerializerOptions? options = null, CancellationToken ct = default)
    {
        var response = await client.SendAsync(message, ct).ConfigureAwait(false);
        var value = await response.ReadJsonAsync<T>(options, ct).ConfigureAwait(false);
        return value;
    }

    public static async Task<HttpResult<T?>> FromJsonWithMetaAsync<T>(this HttpClient client, HttpRequestMessage message, System.Text.Json.JsonSerializerOptions? options = null, CancellationToken ct = default)
    {
        var response = await client.SendAsync(message, ct).ConfigureAwait(false);
        var value = await response.ReadJsonWithMetaAsync<T>(options, ct).ConfigureAwait(false);
        return value;
    }

    public static async Task<T?> FromXmlAsync<T>(this HttpClient client, HttpRequestMessage message, CancellationToken ct = default)
    {
        var response = await client.SendAsync(message.WithAccept("text/xml"), ct).ConfigureAwait(false);
        var value = await response.ReadXmlAsync<T>(ct).ConfigureAwait(false);
        return value;
    }

    public static async Task<HttpResult<T?>> FromXmlWithMetaAsync<T>(this HttpClient client, HttpRequestMessage message, CancellationToken ct = default)
    {
        var response = await client.SendAsync(message.WithAccept("text/xml"), ct).ConfigureAwait(false);
        var value = await response.ReadXmlWithMetaAsync<T>(ct).ConfigureAwait(false);
        return value;
    }

    public static async Task<string> FromTextAsync(this HttpClient client, HttpRequestMessage message, CancellationToken ct = default)
    {
        var response = await client.SendAsync(message.WithAccept("application/json"), ct).ConfigureAwait(false);
        var value = await response.ReadTextAsync(ct).ConfigureAwait(false);
        return value;
    }

    public static async Task<HttpResult<string>> FromTextWithMetaAsync(this HttpClient client, HttpRequestMessage message, CancellationToken ct = default)
    {
        var response = await client.SendAsync(message.WithAccept("application/json"), ct).ConfigureAwait(false);
        var value = await response.ReadTextWithMetaAsync(ct).ConfigureAwait(false);
        return value;
    }
}
