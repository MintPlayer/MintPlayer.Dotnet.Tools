using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace MintPlayer.Http.Tests;

public class HttpResponseAndClientTests
{
    private sealed record Dto(int Id, string Name);

    private static HttpRequestMessage Get() => new(HttpMethod.Get, "https://example.test/api");

    #region SendAsync content negotiation

    [Fact]
    public async Task SendAsync_ReadsAJsonBody()
    {
        var client = StubHandlerExtensions.AlwaysReturns(_ => StubHandler.Json("""{"Id":1,"Name":"x"}"""), out _);

        var result = await client.SendAsync<Dto>(Get());

        result.Value.Should().Be(new Dto(1, "x"));
        result.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SendAsync_MatchesJsonPropertiesCaseInsensitively()
    {
        var client = StubHandlerExtensions.AlwaysReturns(_ => StubHandler.Json("""{"id":1,"name":"x"}"""), out _);

        (await client.SendAsync<Dto>(Get())).Value.Should().Be(new Dto(1, "x"));
    }

    [Fact]
    public async Task SendAsync_ReadsAnXmlBody()
    {
        var client = StubHandlerExtensions.AlwaysReturns(
            _ => StubHandler.Xml("""<XmlDto xmlns:i="http://www.w3.org/2001/XMLSchema-instance"><Id>9</Id></XmlDto>"""),
            out _);

        (await client.SendAsync<XmlDto>(Get())).Value!.Id.Should().Be(9);
    }

    [Fact]
    public async Task SendAsync_ReadsATextBodyAsString()
    {
        var client = StubHandlerExtensions.AlwaysReturns(_ => StubHandler.Text("plain body"), out _);

        (await client.SendAsync<string>(Get())).Value.Should().Be("plain body");
    }

    /// <summary>
    /// Regression for the removed implicit operator on HttpResult&lt;T&gt;. Reading a text/plain
    /// response into a non-string T used to recurse through
    /// <c>implicit operator HttpResult&lt;T?&gt;(HttpResult&lt;string?&gt;) =&gt; result;</c> until the
    /// process died of an uncatchable StackOverflowException. It must now be a plain,
    /// catchable NotSupportedException.
    /// </summary>
    [Fact]
    public async Task SendAsync_ReadingATextBodyIntoANonStringType_ThrowsRatherThanOverflowingTheStack()
    {
        var client = StubHandlerExtensions.AlwaysReturns(_ => StubHandler.Text("plain body"), out _);

        var act = async () => await client.SendAsync<Dto>(Get());

        (await act.Should().ThrowAsync<NotSupportedException>())
            .WithMessage("*text/plain*");
    }

    [Fact]
    public async Task SendAsync_WithoutAContentType_Throws()
    {
        var client = StubHandlerExtensions.AlwaysReturns(_ => StubHandler.NoContentType("body"), out _);

        var act = async () => await client.SendAsync<Dto>(Get());

        (await act.Should().ThrowAsync<NotSupportedException>()).WithMessage("*Missing content type*");
    }

    [Fact]
    public async Task SendAsync_WithAnUnsupportedContentType_Throws()
    {
        var client = StubHandlerExtensions.AlwaysReturns(_ =>
        {
            var content = new StringContent("data");
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }, out _);

        var act = async () => await client.SendAsync<Dto>(Get());

        (await act.Should().ThrowAsync<NotSupportedException>()).WithMessage("*Unsupported content type*");
    }

    [Fact]
    public async Task SendAsync_OnAFailureStatus_ThrowsWithStatusUrlHeadersAndBody()
    {
        var client = StubHandlerExtensions.AlwaysReturns(_ =>
        {
            var response = StubHandler.Json("""{"error":"nope"}""", HttpStatusCode.BadRequest);
            response.Headers.Add("X-Trace", "abc");
            return response;
        }, out _);

        var act = async () => await client.SendAsync<Dto>(Get());

        var ex = (await act.Should().ThrowAsync<HttpRequestException>()).Which;
        ex.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        ex.Message.Should().Contain("HTTP 400");
        ex.Message.Should().Contain("https://example.test/api");
        ex.Message.Should().Contain("X-Trace");
        ex.Message.Should().Contain("nope");
    }

    [Fact]
    public async Task SendAsync_PassesTheRequestToTheHandler()
    {
        var client = StubHandlerExtensions.AlwaysReturns(_ => StubHandler.Text("ok"), out var handler);

        await client.SendAsync<string>(Get().WithAuthorizationBearer("tok"));

        handler.Requests.Should().ContainSingle()
            .Which.Headers.Authorization!.ToString().Should().Be("Bearer tok");
    }

    #endregion

    #region ReadJsonAsync options handling

    /// <summary>
    /// Regression: ReadJsonAsync used to assign PropertyNameCaseInsensitive directly on the
    /// caller's JsonSerializerOptions. A JsonSerializerOptions becomes read-only after its
    /// first use, so the second call with the same instance threw
    /// InvalidOperationException — and the first call silently mutated the caller's options.
    /// </summary>
    [Fact]
    public async Task ReadJsonAsync_CanReuseTheSameOptionsInstance()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var client = StubHandlerExtensions.AlwaysReturns(_ => StubHandler.Json("""{"id":1,"name":"x"}"""), out _);

        var first = await client.SendAsync<Dto>(Get(), options);
        var second = await client.SendAsync<Dto>(Get(), options);

        first.Value.Should().Be(new Dto(1, "x"));
        second.Value.Should().Be(new Dto(1, "x"));
    }

    [Fact]
    public async Task ReadJsonAsync_DoesNotMutateTheCallersOptions()
    {
        var options = new JsonSerializerOptions();
        var client = StubHandlerExtensions.AlwaysReturns(_ => StubHandler.Json("""{"Id":1,"Name":"x"}"""), out _);

        await client.SendAsync<Dto>(Get(), options);

        options.PropertyNameCaseInsensitive.Should().BeFalse();
    }

    #endregion

    #region FromStreamAsync

    [Fact]
    public async Task FromStreamAsync_ReturnsTheBody()
    {
        var client = StubHandlerExtensions.AlwaysReturns(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) }, out _);

        await using var stream = await client.FromStreamAsync(Get());

        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        buffer.ToArray().Should().Equal([1, 2, 3]);
    }

    /// <summary>
    /// Regression for D6 in docs/PRD-TestCoverage.md. FromStreamAsync set Position = 0
    /// unconditionally, which throws NotSupportedException on a non-seekable stream — the
    /// normal case for an unbuffered download, i.e. exactly what this method is for.
    /// </summary>
    [Fact]
    public async Task FromStreamAsync_OnANonSeekableBody_DoesNotThrow()
    {
        var client = StubHandlerExtensions.AlwaysReturns(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new NonSeekableStream([4, 5, 6])),
            }, out _);

        await using var stream = await client.FromStreamAsync(Get());

        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        buffer.ToArray().Should().Equal([4, 5, 6]);
    }

    #endregion

    #region Response helpers

    private static HttpResponseMessage ResponseWith(Action<HttpResponseMessage> configure)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("x") };
        configure(response);
        return response;
    }

    [Fact]
    public void GetETag_ReturnsTheEtag()
        => ResponseWith(r => r.Headers.ETag = new EntityTagHeaderValue("\"v1\""))
            .GetETag()!.Tag.Should().Be("\"v1\"");

    [Fact]
    public void GetETag_WithoutAnEtag_IsNull()
        => ResponseWith(_ => { }).GetETag().Should().BeNull();

    [Fact]
    public void GetLocation_ReturnsTheLocation()
        => ResponseWith(r => r.Headers.Location = new Uri("https://example.test/new"))
            .GetLocation()!.ToString().Should().Be("https://example.test/new");

    [Fact]
    public void GetLocation_WithoutALocation_IsNull()
        => ResponseWith(_ => { }).GetLocation().Should().BeNull();

    [Fact]
    public void GetRetryAfter_WithAbsoluteDate_ReturnsIt()
    {
        var when = DateTimeOffset.UtcNow.AddMinutes(5);
        var response = ResponseWith(r => r.Headers.RetryAfter = new RetryConditionHeaderValue(when));

        response.GetRetryAfter()!.Value.Should().BeCloseTo(when, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void GetRetryAfter_WithADelta_ProjectsFromNow()
    {
        var response = ResponseWith(r => r.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(120)));

        response.GetRetryAfter()!.Value.Should()
            .BeCloseTo(DateTimeOffset.UtcNow.AddSeconds(120), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void GetRetryAfter_WithoutTheHeader_IsNull()
        => ResponseWith(_ => { }).GetRetryAfter().HasValue.Should().BeFalse();

    #endregion

    #region RFC 5988 Link parsing

    private static HttpResponseMessage WithLink(string link)
        => ResponseWith(r => r.Headers.TryAddWithoutValidation("Link", link));

    [Fact]
    public void GetPaginationLinks_ParsesAllFourRelations()
    {
        var response = WithLink(
            "<https://example.test/p2>; rel=\"next\", " +
            "<https://example.test/p0>; rel=\"prev\", " +
            "<https://example.test/p1>; rel=\"first\", " +
            "<https://example.test/p9>; rel=\"last\"");

        var (next, prev, first, last) = response.GetPaginationLinks();

        next!.ToString().Should().Be("https://example.test/p2");
        prev!.ToString().Should().Be("https://example.test/p0");
        first!.ToString().Should().Be("https://example.test/p1");
        last!.ToString().Should().Be("https://example.test/p9");
    }

    [Fact]
    public void GetPaginationLinks_ParsesASingleRelation()
    {
        var (next, prev, first, last) = WithLink("<https://example.test/p2>; rel=\"next\"").GetPaginationLinks();

        next!.ToString().Should().Be("https://example.test/p2");
        prev.Should().BeNull();
        first.Should().BeNull();
        last.Should().BeNull();
    }

    [Fact]
    public void GetPaginationLinks_WithoutTheHeader_IsAllNull()
        => ResponseWith(_ => { }).GetPaginationLinks().Should().Be((null, null, null, null));

    [Fact]
    public void GetPaginationLinks_IgnoresAnUnknownRelation()
        => WithLink("<https://example.test/x>; rel=\"describedby\"").GetPaginationLinks()
            .Should().Be((null, null, null, null));

    /// <summary>
    /// Regression for a platform split. The assertion used to rely on
    /// <c>Uri.TryCreate("/p2", UriKind.Absolute, …)</c> returning false — true on Windows, but
    /// FALSE on Linux, where a leading slash is a valid Unix file path and the value parses as
    /// the absolute URI <c>file:///p2</c>. So this passed locally and failed on the CI runner,
    /// and the library handed back a bogus <c>file://</c> URI a caller might try to fetch.
    /// <c>GetPaginationLinks</c> now requires http/https, which makes the result identical on
    /// both platforms.
    /// </summary>
    [Fact]
    public void GetPaginationLinks_IgnoresARelativeUrl()
        => WithLink("</p2>; rel=\"next\"").GetPaginationLinks().Should().Be((null, null, null, null));

    [Fact]
    public void GetPaginationLinks_IgnoresANonHttpScheme()
    {
        // The guard is on the scheme, not on parse success, so an explicitly non-HTTP target is
        // dropped too. Without it, this is what a relative target silently became on Linux.
        WithLink("<file:///p2>; rel=\"next\"").GetPaginationLinks().Should().Be((null, null, null, null));
        WithLink("<ftp://example.test/p2>; rel=\"next\"").GetPaginationLinks().Should().Be((null, null, null, null));
    }

    [Fact]
    public void GetPaginationLinks_AcceptsBothHttpAndHttps()
    {
        WithLink("<http://example.test/p2>; rel=\"next\"").GetPaginationLinks()
            .next!.Scheme.Should().Be("http");
        WithLink("<https://example.test/p2>; rel=\"next\"").GetPaginationLinks()
            .next!.Scheme.Should().Be("https");
    }

    [Fact]
    public void GetPaginationLinks_ToleratesExtraParameters()
        => WithLink("<https://example.test/p2>; rel=\"next\"; title=\"Next page\"")
            .GetPaginationLinks().next!.ToString().Should().Be("https://example.test/p2");

    [Fact]
    public void GetPaginationLinks_ToleratesAnUnquotedRel()
        => WithLink("<https://example.test/p2>; rel=next")
            .GetPaginationLinks().next!.ToString().Should().Be("https://example.test/p2");

    [Fact]
    public void GetPaginationLinks_LastRelationWins_WhenRepeated()
    {
        var response = WithLink("<https://example.test/a>; rel=\"next\", <https://example.test/b>; rel=\"next\"");

        response.GetPaginationLinks().next!.ToString().Should().Be("https://example.test/b");
    }

    #endregion

    #region EnsureSuccessWithBodyAsync

    [Fact]
    public async Task EnsureSuccessWithBodyAsync_OnSuccess_DoesNothing()
    {
        var act = async () => await new HttpResponseMessage(HttpStatusCode.OK).EnsureSuccessWithBodyAsync();
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task EnsureSuccessWithBodyAsync_OnFailure_IncludesTheBody()
    {
        var response = new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("missing thing"),
            RequestMessage = Get(),
        };

        var act = async () => await response.EnsureSuccessWithBodyAsync();

        var ex = (await act.Should().ThrowAsync<HttpRequestException>()).Which;
        ex.StatusCode.Should().Be(HttpStatusCode.NotFound);
        ex.Message.Should().Contain("HTTP 404");
        ex.Message.Should().Contain("missing thing");
        ex.Message.Should().Contain("https://example.test/api");
    }

    [Fact]
    public async Task EnsureSuccessWithBodyAsync_WithAnEmptyBody_OmitsTheBodySection()
    {
        var response = new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent(string.Empty),
        };

        var act = async () => await response.EnsureSuccessWithBodyAsync();

        (await act.Should().ThrowAsync<HttpRequestException>()).Which.Message.Should().NotContain("Body:");
    }

    #endregion

    #region SaveAsFileAsync

    [Fact]
    public async Task SaveAsFileAsync_WritesTheBodyAndReportsProgress()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mintplayer-http-{Guid.NewGuid():N}.bin");
        var payload = Enumerable.Range(0, 200_000).Select(i => (byte)(i % 251)).ToArray();
        var reports = new List<long>();

        try
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) };

            await response.SaveAsFileAsync(path, new Progress<long>(reports.Add));

            // Progress is reported per 80KB buffer, so a 200KB payload reports more than once.
            (await File.ReadAllBytesAsync(path)).Should().Equal(payload);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task SaveAsFileAsync_OnAFailureStatus_ThrowsAndWritesNothing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mintplayer-http-{Guid.NewGuid():N}.bin");

        var response = new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("denied") };

        var act = async () => await response.SaveAsFileAsync(path);

        await act.Should().ThrowAsync<HttpRequestException>();
        File.Exists(path).Should().BeFalse();
    }

    #endregion

    #region HttpResult

    [Fact]
    public void HttpResult_DeconstructsIntoValueAndStatus()
    {
        var result = new HttpResult<int>(42, HttpStatusCode.Accepted, HttpVersion.Version11,
            new HttpResponseMessage().Headers);

        var (value, status) = result;

        value.Should().Be(42);
        status.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public void HttpResult_DeconstructsIntoThreeParts()
    {
        var headers = new HttpResponseMessage().Headers;
        var result = new HttpResult<int>(1, HttpStatusCode.OK, HttpVersion.Version11, headers);

        var (value, status, actualHeaders) = result;

        value.Should().Be(1);
        status.Should().Be(HttpStatusCode.OK);
        actualHeaders.Should().BeSameAs(headers);
    }

    [Fact]
    public void HttpResult_DeconstructsIntoFourParts()
    {
        var location = new Uri("https://example.test/created");
        var result = new HttpResult<int>(1, HttpStatusCode.Created, HttpVersion.Version11,
            new HttpResponseMessage().Headers, "Created", location);

        var (value, status, _, actualLocation) = result;

        value.Should().Be(1);
        status.Should().Be(HttpStatusCode.Created);
        actualLocation.Should().BeSameAs(location);
    }

    [Fact]
    public async Task SendAsync_CarriesTheReasonPhraseAndLocation()
    {
        var client = StubHandlerExtensions.AlwaysReturns(_ =>
        {
            var response = StubHandler.Json("""{"Id":1,"Name":"x"}""", HttpStatusCode.Created);
            response.ReasonPhrase = "Created";
            response.Headers.Location = new Uri("https://example.test/api/1");
            return response;
        }, out _);

        var result = await client.SendAsync<Dto>(Get());

        result.ReasonPhrase.Should().Be("Created");
        result.Location!.ToString().Should().Be("https://example.test/api/1");
    }

    #endregion
}

/// <summary>A read-only, forward-only stream — what an unbuffered HTTP body behaves like.</summary>
internal sealed class NonSeekableStream(byte[] data) : Stream
{
    private readonly MemoryStream _inner = new(data);

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) _inner.Dispose();
        base.Dispose(disposing);
    }
}
