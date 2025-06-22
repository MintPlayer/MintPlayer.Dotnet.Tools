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

## Service registration
Similarily, for the first snippet, the `[Register]` attribute generates the following code for you:

```csharp

using Microsoft.Extensions.DependencyInjection;

namespace Demo
{
    public static class DependencyInjectionExtensionMethods
    {
        public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection AddServices(this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)
        {
            return services
                .AddScoped<global::MintPlayer.Spark.ITestServiceBaseBase, global::MintPlayer.Spark.TestServiceBaseBase>()
                .AddScoped<global::MintPlayer.Spark.ITestServiceBase, global::MintPlayer.Spark.TestServiceBase>()
                .AddScoped<global::MintPlayer.Spark.ITestService, global::MintPlayer.Spark.TestService>();
        }
    }
}
```

Now you can call this generated `.AddServices()` method on any service collection in your application.
You can change the name of this method by adding the desired name as parameter to the `[Register]` attribute.
