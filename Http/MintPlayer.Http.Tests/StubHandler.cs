using System.Net;

namespace MintPlayer.Http.Tests;

/// <summary>
/// Answers every request from a supplied factory, so nothing in this suite opens a socket.
/// Also records the requests it saw, which is how the WithXxx builder tests verify that a
/// header or body actually reaches the wire rather than just inspecting the message object.
/// </summary>
internal sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);

        var response = respond(request);
        response.RequestMessage ??= request;

        return Task.FromResult(response);
    }

    public static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK)
        => WithContent(new StringContent(body, System.Text.Encoding.UTF8, "application/json"), status);

    public static HttpResponseMessage Xml(string body, HttpStatusCode status = HttpStatusCode.OK)
        => WithContent(new StringContent(body, System.Text.Encoding.UTF8, "application/xml"), status);

    public static HttpResponseMessage Text(string body, HttpStatusCode status = HttpStatusCode.OK)
        => WithContent(new StringContent(body, System.Text.Encoding.UTF8, "text/plain"), status);

    /// <summary>A body with no Content-Type at all, which SendAsync rejects.</summary>
    public static HttpResponseMessage NoContentType(string body)
    {
        var content = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(body));
        content.Headers.ContentType = null;
        return WithContent(content, HttpStatusCode.OK);
    }

    private static HttpResponseMessage WithContent(HttpContent content, HttpStatusCode status)
        => new(status) { Content = content };
}

internal static class StubHandlerExtensions
{
    public static HttpClient Client(this StubHandler handler)
        => new(handler) { BaseAddress = new Uri("https://example.test/") };

    public static HttpClient AlwaysReturns(Func<HttpRequestMessage, HttpResponseMessage> respond, out StubHandler handler)
    {
        handler = new StubHandler(respond);
        return handler.Client();
    }
}
