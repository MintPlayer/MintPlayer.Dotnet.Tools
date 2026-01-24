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
