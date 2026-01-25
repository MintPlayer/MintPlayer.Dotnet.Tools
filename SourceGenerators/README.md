# SourceGenerators
This folder contains several .NET source generators that can help you reduce boilerplate code in .NET apps.

## Example usage

```csharp
using MintPlayer.SourceGenerators.Attributes;

namespace Demo;

// Some services for your application
public interface IServiceA { }
public interface IServiceB { }
public interface IServiceC { }
public interface IServiceD { }

// Several services that inherit one another
public interface ITestServiceBaseBase { }

[Register(typeof(ITestServiceBaseBase), ServiceLifetime.Scoped)]
public partial class TestServiceBaseBase : ITestServiceBaseBase
{
    [Inject] private readonly IServiceA serviceA;
    [Inject] private readonly IServiceB serviceB;
}

public interface ITestServiceBase { }

[Register(typeof(ITestServiceBase), ServiceLifetime.Scoped)]
public partial class TestServiceBase : TestServiceBaseBase, ITestServiceBase
{
    [Inject] private readonly IServiceC serviceC;
}

public interface ITestService { }

[Register(typeof(ITestService), ServiceLifetime.Scoped)]
public partial class TestService : TestServiceBase, ITestService
{
    [Inject] private readonly IServiceD serviceD;
}
```


## Dependency Injection

In the example above, the child services always have to inject all services from its base types and pass them to the `base()` constructor.
The code above automatically generates the following code for you, thanks to the `[Inject]` attribute:

```csharp
namespace Demo
{
    public partial class TestServiceBaseBase
    {
        public TestServiceBaseBase(global::MintPlayer.Spark.IServiceA serviceA, global::MintPlayer.Spark.IServiceB serviceB)
        {
            this.serviceA = serviceA;
            this.serviceB = serviceB;
        }
    }
    public partial class TestServiceBase
    {
        public TestServiceBase(global::MintPlayer.Spark.IServiceC serviceC, global::MintPlayer.Spark.IServiceA serviceA, global::MintPlayer.Spark.IServiceB serviceB)
            : base(serviceA, serviceB)
        {
            this.serviceC = serviceC;
        }
    }
    public partial class TestService
    {
        public TestService(global::MintPlayer.Spark.IServiceD serviceD, global::MintPlayer.Spark.IServiceC serviceC, global::MintPlayer.Spark.IServiceA serviceA, global::MintPlayer.Spark.IServiceB serviceB)
            : base(serviceC, serviceA, serviceB)
        {
            this.serviceD = serviceD;
        }
    }
}
```

### Post-Construction Initialization

If you need to execute initialization logic after all dependencies are injected, use the `[PostConstruct]` attribute:

```csharp
[Register(typeof(ITestService), ServiceLifetime.Scoped)]
public partial class TestService : ITestService
{
    [Inject] private readonly IServiceA serviceA;
    [Inject] private readonly ILogger<TestService> logger;

    [PostConstruct]
    private void OnInitialized()
    {
        logger.LogInformation("TestService initialized");
    }
}
```

This generates:

```csharp
public partial class TestService
{
    public TestService(global::IServiceA serviceA, global::ILogger<TestService> logger)
    {
        this.serviceA = serviceA;
        this.logger = logger;
        OnInitialized();
    }
}
```

**Rules:**
- Method must be parameterless
- Only one `[PostConstruct]` method per class
- Cannot be static
- Works with inheritance (each class can have its own)

## Service registration
Similarly, for the first snippet, the `[Register]` attribute generates the following code for you:

```csharp
using Microsoft.Extensions.DependencyInjection;

namespace Demo
{
    public static class DependencyInjectionExtensionMethods
    {
        public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection AddDemo(this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)
        {
            return services
                .AddScoped<global::Demo.ITestServiceBaseBase, global::Demo.TestServiceBaseBase>()
                .AddScoped<global::Demo.ITestServiceBase, global::Demo.TestServiceBase>()
                .AddScoped<global::Demo.ITestService, global::Demo.TestService>();
        }
    }
}
```

### Method Name Resolution

The generated method name is determined by (in order of precedence):

1. **Explicit hint** on `[Register]` attribute: `[Register(typeof(IService), ServiceLifetime.Scoped, "MyServices")]` → `AddMyServices()`
2. **Assembly-level configuration**: `[assembly: ServiceRegistrationConfiguration(DefaultMethodName = "CoreServices")]` → `AddCoreServices()`
3. **Assembly name** (default): Assembly `MyCompany.Demo` → `AddMyCompanyDemo()`

### Assembly-Level Configuration

Configure defaults for all service registrations in your assembly:

```csharp
using MintPlayer.SourceGenerators.Attributes;

[assembly: ServiceRegistrationConfiguration(
    DefaultMethodName = "MyServices",
    DefaultAccessibility = EGeneratedAccessibility.Internal
)]
```

## Configuration Binding

Simplify reading configuration values with the `[Config]`, `[ConnectionString]`, and `[Options]` attributes.

### Basic Configuration Values

```csharp
public partial class DatabaseService
{
    [Config("Database:Type")]
    private readonly DatabaseType databaseType;  // Enum parsing

    [Config("Database:MaxRetries", DefaultValue = 3)]
    private readonly int maxRetries;  // With default value

    [Config("Database:Timeout")]
    private readonly TimeSpan timeout;  // TimeSpan parsing

    [ConnectionString("DefaultConnection")]
    private readonly string connectionString;
}
```

Generated:
```csharp
public partial class DatabaseService
{
    public DatabaseService(IConfiguration __configuration)
    {
        this.databaseType = Enum.Parse<DatabaseType>(__configuration["Database:Type"] ?? throw ...);
        this.maxRetries = __configuration["Database:MaxRetries"] is string val ? int.Parse(val) : 3;
        this.timeout = TimeSpan.Parse(__configuration["Database:Timeout"] ?? throw ..., CultureInfo.InvariantCulture);
        this.connectionString = __configuration.GetConnectionString("DefaultConnection") ?? throw ...;
    }
}
```

### Nullable Fields (Optional Values)

Fields marked as nullable are treated as optional:

```csharp
public partial class OptionalConfigService
{
    [Config("Optional:String")]
    private readonly string? optionalString;  // Optional - won't throw if missing

    [Config("Optional:Int")]
    private readonly int? optionalInt;  // Optional nullable int

    [ConnectionString("OptionalDb")]
    private readonly string? optionalConnection;  // Optional connection string
}
```

### IOptions Pattern

Use `[Options]` for strongly-typed configuration with hot-reload support:

```csharp
public partial class EmailService
{
    [Options("Email")]
    private readonly IOptions<EmailSettings> emailOptions;  // Read once

    [Options("Customer")]
    private readonly IOptionsSnapshot<CustomerConfig> customerOptions;  // Scoped, re-read per request

    [Options("Features")]
    private readonly IOptionsMonitor<FeatureFlags> featureFlags;  // Singleton with change notifications
}
```

### IConfiguration Deduplication

When you explicitly inject `IConfiguration`, the generator reuses it:

```csharp
public partial class ConfigAwareService
{
    [Inject] private readonly IConfiguration configuration;  // Explicit injection

    [Config("App:Name")]
    private readonly string appName;  // Uses 'configuration' field, not a separate parameter

    public string GetCustomValue(string key) => configuration[key];
}
```

### Supported Types

| Category | Types |
|----------|-------|
| **Primitives** | `string`, `bool`, `char`, `byte`, `short`, `int`, `long`, `float`, `double`, `decimal` |
| **Nullable Primitives** | `int?`, `bool?`, etc. |
| **Enums** | Any enum type, including nullable enums |
| **Date/Time** | `DateTime`, `DateTimeOffset`, `TimeSpan`, `DateOnly`, `TimeOnly` |
| **Other** | `Guid`, `Uri` |
| **Complex Types** | POCO classes (via `GetSection().Get<T>()`) |
| **Collections** | `T[]`, `List<T>`, `IEnumerable<T>` |

### Configuration Diagnostics

| Rule ID | Severity | Description |
|---------|----------|-------------|
| CONFIG001 | Error | Class must be partial to use [Config] |
| CONFIG002 | Error | Configuration key cannot be empty |
| CONFIG003 | Error | Unsupported field type |
| CONFIG006 | Error | Cannot have both [Config] and [ConnectionString] |
| CONFIG007 | Warning | Duplicate configuration key |
| CONFIG008 | Error | Cannot have both [Config] and [Inject] |
| CONNSTR001 | Error | Connection string name cannot be empty |
| CONNSTR002 | Error | [ConnectionString] requires string type |
| OPTIONS001 | Error | [Options] requires IOptions<T>/IOptionsSnapshot<T>/IOptionsMonitor<T> |
| OPTIONS003 | Error | Cannot have both [Options] and [Inject] |
