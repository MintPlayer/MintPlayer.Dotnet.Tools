using System.Net.Http.Headers;

namespace MintPlayer.Http;

public sealed record HttpResult<T>(T? Value, System.Net.HttpStatusCode StatusCode, Version Version, HttpResponseHeaders Headers, string? ReasonPhrase = null, Uri? Location = null)
{
    public void Deconstruct(out T? result, out System.Net.HttpStatusCode statusCode)
    {
        result = Value;
        statusCode = StatusCode;
    }
    public void Deconstruct(out T? result, out System.Net.HttpStatusCode statusCode, out HttpResponseHeaders headers)
    {
        result = Value;
        statusCode = StatusCode;
        headers = Headers;
    }
    public void Deconstruct(out T? result, out System.Net.HttpStatusCode statusCode, out HttpResponseHeaders headers, out Uri? location)
    {
        result = Value;
        statusCode = StatusCode;
        headers = Headers;
        location = Location;
    }

    // Used for the SendAsync switch case when T is string
    public static implicit operator HttpResult<T?>(HttpResult<string?> result) => result;
}
