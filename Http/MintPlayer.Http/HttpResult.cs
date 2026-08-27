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

    // NOTE: there used to be an
    //     public static implicit operator HttpResult<T?>(HttpResult<string?> result) => result;
    // here, "used for the SendAsync switch case when T is string". Its body converted
    // HttpResult<string?> to HttpResult<T?>, which resolves to the operator itself — so for
    // any T other than string it recursed until the process died of a StackOverflowException,
    // which cannot be caught. It was reachable from SendAsync<T> whenever a server answered
    // a typed request with text/plain. SendAsync now converts explicitly instead.
}
