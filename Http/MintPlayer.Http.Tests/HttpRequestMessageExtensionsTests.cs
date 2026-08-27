using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace MintPlayer.Http.Tests;

public class HttpRequestMessageExtensionsTests
{
    private static HttpRequestMessage Message(string url = "https://example.test/api")
        => new(HttpMethod.Get, url);

    private static string? Header(HttpRequestMessage message, string name)
        => message.Headers.TryGetValues(name, out var values) ? string.Join(", ", values) : null;

    #region Cookies

    [Fact]
    public void WithCookie_SetsTheFirstCookie()
        => Header(Message().WithCookie("a", "1"), "Cookie").Should().Be("a=1");

    [Fact]
    public void WithCookie_AppendsToAnExistingCookieHeader()
    {
        var message = Message().WithCookie("a", "1").WithCookie("b", "2");

        Header(message, "Cookie").Should().Be("a=1; b=2");
    }

    [Fact]
    public void WithCookie_AppendsAThirdCookie()
    {
        var message = Message().WithCookie("a", "1").WithCookie("b", "2").WithCookie("c", "3");

        Header(message, "Cookie").Should().Be("a=1; b=2; c=3");
    }

    [Fact]
    public void WithCookie_LeavesASingleCookieHeader()
    {
        var message = Message().WithCookie("a", "1").WithCookie("b", "2");

        message.Headers.GetValues("Cookie").Should().HaveCount(1);
    }

    [Fact]
    public void WithCookie_OnNullMessage_Throws()
    {
        var act = () => ((HttpRequestMessage)null!).WithCookie("a", "1");
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region Headers

    [Fact]
    public void WithHeader_AddsTheHeader()
        => Header(Message().WithHeader("X-Test", "value"), "X-Test").Should().Be("value");

    [Fact]
    public void WithHeaders_AddsEveryHeader()
    {
        var message = Message().WithHeaders(new Dictionary<string, string>
        {
            ["X-One"] = "1",
            ["X-Two"] = "2",
        });

        Header(message, "X-One").Should().Be("1");
        Header(message, "X-Two").Should().Be("2");
    }

    [Fact]
    public void WithHeaders_OnAnEmptyDictionary_IsANoOp()
        => Message().WithHeaders(new Dictionary<string, string>()).Headers.Should().BeEmpty();

    [Fact]
    public void WithHeader_UsesTryAddWithoutValidation_SoAnInvalidValueIsAccepted()
    {
        // TryAddWithoutValidation means a value the strongly-typed API would reject still
        // lands on the message. Pinned because it is a deliberate design choice.
        var message = Message().WithHeader("Date", "not-a-date");

        Header(message, "Date").Should().Be("not-a-date");
    }

    [Fact]
    public void WithHeader_OnNullMessage_Throws()
    {
        var act = () => ((HttpRequestMessage)null!).WithHeader("a", "1");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void WithHeaders_OnNullMessage_Throws()
    {
        var act = () => ((HttpRequestMessage)null!).WithHeaders(new Dictionary<string, string>());
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region Authorization

    [Fact]
    public void WithAuthorizationBearer_SetsTheBearerScheme()
        => Header(Message().WithAuthorizationBearer("abc123"), "Authorization").Should().Be("Bearer abc123");

    [Fact]
    public void WithAuthorizationBasic_Base64EncodesTheCredentials()
    {
        var message = Message().WithAuthorizationBasic("user", "pass");

        var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes("user:pass"));
        Header(message, "Authorization").Should().Be($"Basic {expected}");
    }

    [Fact]
    public void WithAuthorizationBasic_HandlesNonAsciiCredentials()
    {
        var message = Message().WithAuthorizationBasic("ünïcode", "pä55");

        var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes("ünïcode:pä55"));
        Header(message, "Authorization").Should().Be($"Basic {expected}");
    }

    [Fact]
    public void WithAuthorizationBasic_AllowsEmptyPassword()
    {
        var message = Message().WithAuthorizationBasic("user", string.Empty);

        var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes("user:"));
        Header(message, "Authorization").Should().Be($"Basic {expected}");
    }

    #endregion

    #region Accept

    [Fact]
    public void WithAccept_AddsOneMediaType()
        => Message().WithAccept("application/json").Headers.Accept
            .Select(a => a.MediaType).Should().Equal(["application/json"]);

    [Fact]
    public void WithAccept_AddsSeveralMediaTypes()
        => Message().WithAccept("a/b", "c/d").Headers.Accept
            .Select(a => a.MediaType).Should().Equal(["a/b", "c/d"]);

    [Fact]
    public void AcceptJson_AcceptsApplicationJson()
        => Message().AcceptJson().Headers.Accept.Select(a => a.MediaType).Should().Equal(["application/json"]);

    [Fact]
    public void AcceptXml_AcceptsBothXmlMediaTypes()
        => Message().AcceptXml().Headers.Accept.Select(a => a.MediaType)
            .Should().Equal(["application/xml", "text/xml"]);

    [Fact]
    public void WithAccept_OnNullMessage_Throws()
    {
        var act = () => ((HttpRequestMessage)null!).WithAccept("a/b");
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region Typed headers

    [Fact]
    public void WithUserAgent_AddsTheProduct()
        => Message().WithUserAgent("MyApp", "1.2.3").Headers.UserAgent.ToString().Should().Be("MyApp/1.2.3");

    [Fact]
    public void WithIfNoneMatch_AddsTheEtag()
        => Message().WithIfNoneMatch("\"abc\"").Headers.IfNoneMatch
            .Select(e => e.Tag).Should().Equal(["\"abc\""]);

    [Fact]
    public void WithIfNoneMatch_WithAnUnquotedEtag_Throws()
    {
        // EntityTagHeaderValue validates, unlike WithHeader's TryAddWithoutValidation.
        var act = () => Message().WithIfNoneMatch("abc");
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void WithRange_WithAnOpenEnd_EmitsAnOpenRange()
        => Message().WithRange(100).Headers.Range!.ToString().Should().Be("bytes=100-");

    [Fact]
    public void WithRange_WithBothEnds_EmitsAClosedRange()
        => Message().WithRange(100, 199).Headers.Range!.ToString().Should().Be("bytes=100-199");

    [Fact]
    public void WithVersion_SetsTheVersion()
        => Message().WithVersion(HttpVersion.Version20).Version.Should().Be(HttpVersion.Version20);

    [Fact]
    public void WithVersion_WithoutAPolicy_LeavesThePolicyAtItsDefault()
        => Message().WithVersion(HttpVersion.Version20).VersionPolicy
            .Should().Be(HttpVersionPolicy.RequestVersionOrLower);

    [Fact]
    public void WithVersion_WithAPolicy_SetsBoth()
    {
        var message = Message().WithVersion(HttpVersion.Version20, HttpVersionPolicy.RequestVersionExact);

        message.Version.Should().Be(HttpVersion.Version20);
        message.VersionPolicy.Should().Be(HttpVersionPolicy.RequestVersionExact);
    }

    [Fact]
    public void WithRequestTimeout_StoresTheTimeoutInOptions()
    {
        var message = Message().WithRequestTimeout(TimeSpan.FromSeconds(5));

        message.Options.TryGetValue(new HttpRequestOptionsKey<TimeSpan>("RequestTimeout"), out var timeout)
            .Should().BeTrue();
        timeout.Should().Be(TimeSpan.FromSeconds(5));
    }

    #endregion

    #region Query

    [Fact]
    public void WithQuery_AddsAQueryParameter()
        => Message("https://example.test/api").WithQuery("a", "1")
            .RequestUri!.ToString().Should().Be("https://example.test/api?a=1");

    [Fact]
    public void WithQuery_AppendsToAnExistingQuery()
        => Message("https://example.test/api?a=1").WithQuery("b", "2")
            .RequestUri!.Query.Should().Be("?a=1&b=2");

    [Fact]
    public void WithQuery_ReplacesAnExistingKey()
        => Message("https://example.test/api?a=1").WithQuery("a", "2")
            .RequestUri!.Query.Should().Be("?a=2");

    [Fact]
    public void WithQuery_UrlEncodesTheValue()
        => Message().WithQuery("q", "a b&c").RequestUri!.Query.Should().Contain("a+b%26c");

    [Fact]
    public void WithQuery_PreservesThePath()
        => Message("https://example.test/deep/path").WithQuery("a", "1")
            .RequestUri!.AbsolutePath.Should().Be("/deep/path");

    [Fact]
    public void WithQuery_WithoutARequestUri_Throws()
    {
        var act = () => new HttpRequestMessage().WithQuery("a", "1");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*RequestUri must be set*");
    }

    [Fact]
    public void WithQuery_OnNullMessage_Throws()
    {
        var act = () => ((HttpRequestMessage)null!).WithQuery("a", "1");
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region Content

    private sealed record Dto(int Id, string Name);

    [Fact]
    public async Task WithJsonContent_SerializesAndSetsTheContentType()
    {
        var message = Message().WithJsonContent(new Dto(1, "x"));

        message.Content!.Headers.ContentType!.MediaType.Should().Be("application/json");
        message.Content.Headers.ContentType.CharSet.Should().Be("utf-8");
        (await message.Content.ReadAsStringAsync()).Should().Be("""{"Id":1,"Name":"x"}""");
    }

    [Fact]
    public async Task WithJsonContent_HonoursSerializerOptions()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        var message = Message().WithJsonContent(new Dto(1, "x"), options);

        (await message.Content!.ReadAsStringAsync()).Should().Be("""{"id":1,"name":"x"}""");
    }

    [Fact]
    public async Task WithFormUrlEncodedContent_EncodesTheForm()
    {
        var message = Message().WithFormUrlEncodedContent([
            new KeyValuePair<string, string>("a", "1"),
            new KeyValuePair<string, string>("b", "x y"),
        ]);

        message.Content!.Headers.ContentType!.MediaType.Should().Be("application/x-www-form-urlencoded");
        (await message.Content.ReadAsStringAsync()).Should().Be("a=1&b=x+y");
    }

    [Fact]
    public void WithMultipartContent_InvokesTheBuilder()
    {
        var built = false;

        var message = Message().WithMultipartContent(mp =>
        {
            built = true;
            mp.Add(new StringContent("v"), "field");
        });

        built.Should().BeTrue();
        message.Content.Should().BeOfType<MultipartFormDataContent>();
    }

    [Fact]
    public async Task WithStringContent_DefaultsToUtf8()
    {
        var message = Message().WithStringContent("hello");

        message.Content!.Headers.ContentType!.CharSet.Should().Be("utf-8");
        (await message.Content.ReadAsStringAsync()).Should().Be("hello");
    }

    [Fact]
    public void WithStringContent_HonoursAnExplicitMediaType()
        => Message().WithStringContent("a,b", mediaType: "text/csv")
            .Content!.Headers.ContentType!.MediaType.Should().Be("text/csv");

    [Fact]
    public void WithStringContent_HonoursAnExplicitEncoding()
        => Message().WithStringContent("hello", Encoding.Unicode)
            .Content!.Headers.ContentType!.CharSet.Should().Be("utf-16");

    [Fact]
    public async Task WithXmlContent_SerializesWithTheDataContractSerializer()
    {
        var message = Message().WithXmlContent(new XmlDto { Id = 7 });

        message.Content!.Headers.ContentType!.MediaType.Should().Be("application/xml");
        (await message.Content.ReadAsStringAsync()).Should().Contain("<Id>7</Id>");
    }

    [Fact]
    public void WithXmlContent_HonoursAnExplicitMediaType()
        => Message().WithXmlContent(new XmlDto { Id = 1 }, mediaType: "text/xml")
            .Content!.Headers.ContentType!.MediaType.Should().Be("text/xml");

    #endregion

    #region Chaining

    [Fact]
    public void TheBuildersChain()
    {
        var message = Message()
            .WithAuthorizationBearer("t")
            .AcceptJson()
            .WithHeader("X-A", "1")
            .WithCookie("c", "v")
            .WithQuery("q", "1")
            .WithJsonContent(new Dto(1, "n"));

        Header(message, "Authorization").Should().Be("Bearer t");
        Header(message, "X-A").Should().Be("1");
        Header(message, "Cookie").Should().Be("c=v");
        message.Headers.Accept.Should().ContainSingle();
        message.RequestUri!.Query.Should().Be("?q=1");
        message.Content.Should().NotBeNull();
    }

    #endregion
}

[System.Runtime.Serialization.DataContract(Namespace = "")]
public class XmlDto
{
    [System.Runtime.Serialization.DataMember]
    public int Id { get; set; }
}
