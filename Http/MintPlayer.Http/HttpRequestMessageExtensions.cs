using System.Net.Http.Headers;
using System.Runtime.Serialization;
using System.Text;
using System.Text.Json;

namespace MintPlayer.Http;

public static class HttpRequestMessageExtensions
{
    public static HttpRequestMessage WithCookie(this HttpRequestMessage message, string name, string value)
    {
        ArgumentNullException.ThrowIfNull(message);
        string newValue;
        if (message.Headers.TryGetValues("Cookie", out var existing))
        {
            newValue = string.Join("; ", existing) + $"; {name}={value}";
            message.Headers.Remove("Cookie");
        }
        else
        {
            newValue = $"{name}={value}";
        }

        message.Headers.TryAddWithoutValidation("Cookie", newValue);
        return message;
    }

    public static HttpRequestMessage WithHeader(this HttpRequestMessage message, string name, string value)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.Headers.TryAddWithoutValidation(name, value);
        return message;
    }

    public static HttpRequestMessage WithHeaders(this HttpRequestMessage message, IReadOnlyDictionary<string, string> headers)
    {
        ArgumentNullException.ThrowIfNull(message);
        foreach (var (k, v) in headers)
            message.Headers.TryAddWithoutValidation(k, v);
        return message;
    }

    public static HttpRequestMessage WithAuthorizationBearer(this HttpRequestMessage message, string token)
        => message.WithHeader("Authorization", $"Bearer {token}");

    public static HttpRequestMessage WithAuthorizationBasic(this HttpRequestMessage message, string username, string password)
    {
        var raw = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
        return message.WithHeader("Authorization", $"Basic {raw}");
    }

    public static HttpRequestMessage WithAccept(this HttpRequestMessage message, params string[] mediaTypes)
    {
        ArgumentNullException.ThrowIfNull(message);
        foreach (var mt in mediaTypes)
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(mt));
        return message;
    }

    public static HttpRequestMessage AcceptJson(this HttpRequestMessage message)
        => message.WithAccept("application/json");

    public static HttpRequestMessage AcceptXml(this HttpRequestMessage message)
        => message.WithAccept("application/xml", "text/xml");

    public static HttpRequestMessage WithUserAgent(this HttpRequestMessage message, string product, string version)
    {
        message.Headers.UserAgent.Add(new ProductInfoHeaderValue(product, version));
        return message;
    }

    public static HttpRequestMessage WithIfNoneMatch(this HttpRequestMessage message, string etag)
    {
        message.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(etag));
        return message;
    }

    public static HttpRequestMessage WithRange(this HttpRequestMessage message, long from, long? to = null)
    {
        message.Headers.Range = to is long rTo ? new RangeHeaderValue(from, rTo) : new RangeHeaderValue(from, null);
        return message;
    }

    public static HttpRequestMessage WithVersion(this HttpRequestMessage message, Version version, HttpVersionPolicy? policy = null)
    {
        message.Version = version;
        if (policy is not null)
            message.VersionPolicy = policy.Value;
        return message;
    }

    public static HttpRequestMessage WithQuery(this HttpRequestMessage message, string name, string value)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.RequestUri is null) throw new InvalidOperationException("RequestUri must be set before adding query.");
        var ub = new UriBuilder(message.RequestUri);
        var query = System.Web.HttpUtility.ParseQueryString(ub.Query);
        query[name] = value;
        ub.Query = query.ToString()!;
        message.RequestUri = ub.Uri;
        return message;
    }

    public static HttpRequestMessage WithJsonContent<T>(this HttpRequestMessage message, T content, JsonSerializerOptions? options = null)
    {
        var json = JsonSerializer.Serialize(content, options);
        message.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return message;
    }

    public static HttpRequestMessage WithFormUrlEncodedContent(this HttpRequestMessage message, IEnumerable<KeyValuePair<string, string>> form)
    {
        message.Content = new FormUrlEncodedContent(form);
        return message;
    }

    public static HttpRequestMessage WithMultipartContent(this HttpRequestMessage message, Action<MultipartFormDataContent> build)
    {
        var mp = new MultipartFormDataContent();
        build(mp);
        message.Content = mp;
        return message;
    }

    public static HttpRequestMessage WithStringContent(this HttpRequestMessage message, string content, Encoding? encoding = null, string? mediaType = null)
    {
        message.Content = new StringContent(content, encoding ?? Encoding.UTF8, mediaType);
        return message;
    }

    public static HttpRequestMessage WithXmlContent<T>(this HttpRequestMessage message, T content, Encoding? encoding = null, string? mediaType = null)
    {
        //var serializer = new System.Xml.Serialization.XmlSerializer(typeof(T));
        using var ms = new MemoryStream();
        encoding ??= Encoding.UTF8;
        using var xmlWriter = System.Xml.XmlWriter.Create(ms, new System.Xml.XmlWriterSettings { OmitXmlDeclaration = false, Indent = false, Encoding = encoding });
        //serializer.Serialize(sw, content);
        //var stringContent = sw.ToString();

        var serializer = new DataContractSerializer(typeof(T));
        serializer.WriteObject(xmlWriter, content);
        xmlWriter.Flush();
        var stringContent = Encoding.UTF8.GetString(ms.ToArray());

        message.Content = new StringContent(stringContent, encoding, mediaType ?? "application/xml");
        return message;
    }

#if NET8_0_OR_GREATER
    private static readonly HttpRequestOptionsKey<TimeSpan> RequestTimeoutKey = new("RequestTimeout");
    public static HttpRequestMessage WithRequestTimeout(this HttpRequestMessage message, TimeSpan timeout)
    {
        message.Options.Set(RequestTimeoutKey, timeout);
        return message;
    }
#endif
}
