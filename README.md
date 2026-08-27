# .NET Tools

## Assertions
[MintPlayer.Assertions](Assertions/MintPlayer.Assertions/README.md) is a fluent assertion library for .NET — a free-forever alternative to FluentAssertions, which became a commercial product in January 2025.

| Package | Release | Preview | Downloads |
|---------|---------|---------|-----------|
| MintPlayer.Assertions | [![NuGet Version](https://img.shields.io/nuget/v/MintPlayer.Assertions.svg?style=flat)](https://www.nuget.org/packages/MintPlayer.Assertions) | [![NuGet Version](https://img.shields.io/nuget/vpre/MintPlayer.Assertions.svg?style=flat)](https://www.nuget.org/packages/MintPlayer.Assertions) | [![NuGet](https://img.shields.io/nuget/dt/MintPlayer.Assertions.svg?style=flat)](https://www.nuget.org/packages/MintPlayer.Assertions) |

What it offers over the alternatives: object-graph equivalency that runs on **source-generated accessors instead of reflection** (~15× faster, AOT- and trimming-safe), and **analyzers in the box** that turn an assertion which cannot fail — a forgotten `await` on an async assertion — into a build error rather than a green test.

```csharp
using MintPlayer.Assertions;

order.Total.Should().Be(120m, because: "the order was paid");
actual.Should().BeEquivalentTo(expected, opt => opt.Excluding(x => x.Id));
await act.Should().ThrowAsync<TimeoutException>().WithMessage("*timed out*");
```

## Source Generators
This repository contains several .NET Source generators
- [ValueComparerGenerator](SourceGenerators/ValueComparerGenerator/README.md): Makes it easier to write your own source-generators by generating the value-comparers for you
- [MapperGenerator](SourceGenerators/Mapper/README.md): Automatically generates mapper-extension-methods for you. It has support for property-name remapping and property-type remapping
- [SourceGenerators](SourceGenerators/SourceGenerators/README.md)
    - Generates extension methods to register services decorated with the `[Register]` attribute
    - Allows you to use the `[Inject]` attribute, removing the constructor completely
    - Contains an interface-implementation analyzer

## HTTP helpers
This repository contains [extension methods](Http/MintPlayer.Http/README.md) that build on the .NET standard `Http` library. Example:

```csharp
var req = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/widgets")
    .WithAuthorizationBearer("your_jwt_here")               // ← auth
    .WithHeader("X-TraceId", Guid.NewGuid().ToString())     // ← any header
    .WithJsonContent(new CreateWidget("Minty", "green"));   // ← body

var (dto, status, headers) = await client.FromJsonWithMetaAsync<WidgetDto>(req, null, ct);
```