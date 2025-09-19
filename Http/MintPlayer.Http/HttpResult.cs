using System.Net.Http.Headers;

namespace MintPlayer.Http;

public sealed record HttpResult<T>(
    T? Value,
    System.Net.HttpStatusCode StatusCode,
    Version Version,
    HttpResponseHeaders Headers,
    string? ReasonPhrase = null,
    Uri? Location = null
);
