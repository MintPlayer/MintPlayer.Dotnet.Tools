# .NET Tools
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