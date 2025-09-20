using System.Net.Http.Headers;

namespace MintPlayer.Http;

public sealed record HttpResult<T>(
    T? Value,
    System.Net.HttpStatusCode StatusCode,
    Version Version,
    HttpResponseHeaders Headers,
    string? ReasonPhrase = null,
    Uri? Location = null
)
{
    public void Deconstruct(
        out T? result,
        out System.Net.HttpStatusCode statusCode,
        out HttpResponseHeaders headers)
    {
        result = Value;
        statusCode = StatusCode;
        headers = Headers;
    }
}
