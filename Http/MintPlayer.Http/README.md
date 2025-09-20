# MintPlayer.Http

**Request-first HTTP ergonomics** for .NET:

- Keep **full control** over your `HttpRequestMessage` (headers, cookies, auth, content, etc.).
- Still enjoy one-liners for **JSON/XML (de)serialization**.
- Get **actionable errors** (response body included when available).
- And when you need more than “just the DTO”, use the **`…WithMeta`** methods to grab the **payload + status code + headers** in one go.

> **Motivation**  
> The default `System.Net.Http.Json` helpers are convenient but inflexible: you lose easy access to cookies/headers and fine-grained request control. `MintPlayer.Http` flips that: you **build the request you want**, then use lightweight helpers for reading JSON/XML and surfacing rich error context.

---

## Install

```powershell
dotnet add package MintPlayer.Http
```

Or reference the project directly in your solution.

---

## Quick start

### Build the request fluently (no manual property mutation needed)

```csharp
using System.Net.Http;
using MintPlayer.Http;

record CreateWidget(string Name, string Color);
record WidgetDto(string Id, string Name, string Color);

using var client = new HttpClient();

// Start with the request...
var req = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/widgets")
    .WithAuthorizationBearer("your_jwt_here")               // ← auth
    .WithHeader("X-TraceId", Guid.NewGuid().ToString())     // ← any header
    .WithJsonContent(new CreateWidget("Minty", "green"));   // ← body

// ...and then deserialize the response in one line:
WidgetDto created = await client.FromJsonAsync<WidgetDto>(req, null, ct);
```

### Need status code and headers too? Use `…WithMeta`

```csharp
using var client = new HttpClient();
var req = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/widgets")
    .WithAuthorizationBearer("your_jwt_here")               // ← auth
    .WithHeader("X-TraceId", Guid.NewGuid().ToString())     // ← any header
    .WithJsonContent(new CreateWidget("Minty", "green"));   // ← body

var result = await client.FromJsonWithMetaAsync<WidgetDto>(req, null, ct);

// You get both the value and the metadata:
WidgetDto dto                 = result.Value;
HttpStatusCode status         = result.StatusCode;
HttpResponseHeaders headers   = result.Headers;        // response headers
Uri location                  = result.Location;       // recirect url
```

> There are also response-first variants (when you already have a `HttpResponseMessage`), e.g. `ReadJsonWithMetaAsync<T>()`, `ReadJsonAsync<T>()`, `ReadXmlAsync<T>()`.

### Deconstruct the result
You can also easily deconstruct the result tuple-style:

```csharp
using var client = new HttpClient();
var req = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/widgets")
    .WithAuthorizationBearer("your_jwt_here")               // ← auth
    .WithHeader("X-TraceId", Guid.NewGuid().ToString())     // ← any header
    .WithJsonContent(new CreateWidget("Minty", "green"));   // ← body

var (dto, status, headers) = await client.FromJsonWithMetaAsync<WidgetDto>(req, null, ct);
```

---

## Request helpers (examples)

These are **extension methods on `HttpRequestMessage`** so you **don't** have to mutate properties manually:

- `.WithHeader(name, value)` / `.WithHeaders(IDictionary<string,string>)`
- `.WithAuthorizationBearer(token)` / `.WithAuthorizationBasic(username, password)`
- `.WithCookie(uri, name, value)` (when using a cookie container)
- `.WithJsonContent(value)` (uses `System.Text.Json`)
- `.WithFormUrlEncodedContent(IEnumerable<KeyValuePair<string,string>> form)`
- `.WithMultipartContent(c => { })`
- `.WithStringContent(str)` (serialize to XML)
- `.WithXmlContent(value)` (serialize to XML)

> Exact names/overloads may differ slightly; check the source for the full list in  
> `HttpRequestMessageExtensions.cs` and `HttpResponseMessageExtensions.cs`.

---

## Response helpers

When you already have a `HttpResponseMessage` (e.g., because you needed `ResponseHeadersRead`, rate-limit headers, etc.):

```csharp
using var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);

// Throws with an error message that includes the response body when available.
await res.EnsureSuccessWithBodyAsync(ct);

// Just the DTO
var dto = await res.ReadJsonAsync<MyDto>(null, ct);

// DTO + metadata
var meta = await res.ReadJsonWithMetaAsync<MyDto>(ct);
// meta.Value, meta.StatusCode, meta.Headers
```

XML equivalents are available as well (`ReadXmlAsync<T>`, `ReadXmlWithMetaAsync<T>`).

---

## End-to-end examples

### A) GET with cookies + read JSON with metadata

```csharp
var handler = new HttpClientHandler
{
    UseCookies = true,
    CookieContainer = new CookieContainer(),
};

using var client = new HttpClient(handler);

var req = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/me")
    .WithCookie(new Uri("https://api.example.com"), "session", "abc123")
    .AcceptJson();

var result = await client.FromJsonWithMetaAsync<UserDto>(req, null, ct);

Console.WriteLine($"Status: {(int)result.StatusCode}");
if (result.Headers.TryGetValues("x-ratelimit-remaining", out var values))
{
    Console.WriteLine($"Rate remaining: {values.FirstOrDefault()}");
}
```

### B) Response-first with streaming (large payloads)

```csharp
var req = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/huge")
    .Accept("application/octet-stream");

using var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
await res.EnsureSuccessWithBodyAsync(ct);

await using var stream = await res.Content.ReadAsStreamAsync(ct);
// process stream…
```

---

## Error handling

`EnsureSuccessWithBodyAsync()` is like `EnsureSuccessStatusCode()` but **includes the response body** in the exception message when possible—hugely helpful when APIs return details in JSON/text.

```csharp
try
{
    var dto = await client.FromJsonAsync<MyDto>(req, null, ct);
}
catch (HttpRequestException ex)
{
    // ex.Message contains status + a snippet of the response body (when available)
    _logger.LogError(ex, "Call failed");
}
```

---

## Custom JSON options

Pass your own `JsonSerializerOptions` or source-gen context when reading:

```csharp
var opts = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
};

var dto = await client.FromJsonAsync<MyDto>(req, opts, ct);
```

---

## FAQ

**How is this different from `System.Net.Http.Json`?**  
You **don't** give up request control. You assemble the exact `HttpRequestMessage` you want (headers, cookies, auth, body), then call `FromJsonAsync`/`FromXmlAsync` or the response-first `Read…`/`…WithMeta` helpers.

**What do the `…WithMeta` methods return?**  
A lightweight result object that contains the **deserialized value** plus **status code** and **headers** (both response + content headers).

**Does it work with Polly/handlers/logging?**  
Yes—everything sits on top of the standard `HttpClient` pipeline.

**Cancellation tokens?**  
All async helpers accept a `CancellationToken`.

---

## Requirements

- Recent .NET (as targeted by the project)
- `System.Text.Json` (inbox on modern .NET)

---

## Contributing

PRs welcome! Keep extensions small and composable, add tests, and document new helpers with a short example.

---

## Source

- Request helpers: `HttpRequestMessageExtensions.cs`  
- Response helpers: `HttpResponseMessageExtensions.cs`
