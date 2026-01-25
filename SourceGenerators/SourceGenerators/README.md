# Dependency-Injection generators
This library contains source-generators to simplify dependency-injection in your application

## Getting started
You need to install both [`MintPlayer.SourceGenerators`](https://nuget.org/packages/MintPlayer.SourceGenerators) and [`MintPlayer.SourceGenerators.Attributes`](https://nuget.org/packages/MintPlayer.SourceGenerators.Attributes) packages in your project.

## Service-registration

Place this class in your abstractions library:

```csharp
public interface ICustomerService { }
```

Place this class in your implementation library:

```csharp
[Register(typeof(ICustomerService), ServiceLifetime.Scoped)]
internal class CustomerService : ICustomerService { }
```

Now you get an extension method generated for you, which will register all services for you:

```csharp
var services = new ServiceCollection()
    .AddMyCompanyServices()  // Method name derived from assembly name
    .BuildServiceProvider();
```

### Method Name Resolution

The generated method name follows this precedence:

1. **Explicit hint** on `[Register]` attribute (e.g., `"CoreServices"`) → `AddCoreServices()`
2. **Assembly-level configuration** via `[assembly: ServiceRegistrationConfiguration]`
3. **Assembly name** (default) - sanitized and prefixed with "Add" (e.g., `MyCompany.Services` → `AddMyCompanyServices()`)

### Assembly-Level Configuration

You can configure the default method name and accessibility at the assembly level:

```csharp
using MintPlayer.SourceGenerators.Attributes;

[assembly: ServiceRegistrationConfiguration(
    DefaultMethodName = "MyServices",
    DefaultAccessibility = EGeneratedAccessibility.Internal
)]
```

This will generate `AddMyServices()` as an `internal` method for all services without an explicit method hint.

### Explicit Method Hints

You can still specify a method hint on individual registrations to group services:

```csharp
[Register(typeof(ICustomerService), ServiceLifetime.Scoped, "DemoServices")]
internal class CustomerService : ICustomerService { }

[Register(typeof(IProductService), ServiceLifetime.Scoped, "DemoServices")]
internal class ProductService : IProductService { }
```

This generates:

```csharp
var services = new ServiceCollection()
    .AddDemoServices()  // Contains CustomerService and ProductService
    .BuildServiceProvider();
```

### Registering Third-Party Types

You can register types from NuGet packages or external libraries at the assembly level:

```csharp
using MintPlayer.SourceGenerators.Attributes;

// Self-registration (implementation = service type)
[assembly: Register(typeof(ThirdPartyClass), ServiceLifetime.Singleton)]

// Interface + implementation registration
[assembly: Register(typeof(IExternalService), typeof(ExternalServiceImpl), ServiceLifetime.Scoped)]
```

This generates:

```csharp
public static IServiceCollection AddMyAssembly(this IServiceCollection services)
{
    return services
        .AddSingleton<ThirdPartyClass>()
        .AddScoped<IExternalService, ExternalServiceImpl>();
}
```

### Registration Patterns Summary

| Pattern | Target | Example |
|---------|--------|---------|
| Self-registration | Class | `[Register(ServiceLifetime.Scoped)]` |
| Interface registration | Class | `[Register(typeof(IService), ServiceLifetime.Scoped)]` |
| Third-party self-registration | Assembly | `[assembly: Register(typeof(Impl), ServiceLifetime.Scoped)]` |
| Third-party interface registration | Assembly | `[assembly: Register(typeof(IService), typeof(Impl), ServiceLifetime.Scoped)]` |

### Registration Diagnostics

| Rule ID | Severity | Description |
|---------|----------|-------------|
| REGISTER001 | Error | Assembly-level `[Register]` requires at least the implementation type |
| REGISTER002 | Error | Class-level `[Register]` should not specify implementation type |

## Dependency Injection

Inject a registered service anywhere:

```csharp
public partial class CustomerController {
    [Inject] private readonly ICustomerService customerService;
}
```

The source-generator will generate the constructor for you. It supports DI when using inheritance too.

### Post-Construction Initialization

If you need to run initialization logic after all dependencies are injected, use the `[PostConstruct]` attribute:

```csharp
public partial class CustomerController {
    [Inject] private readonly ICustomerService customerService;
    [Inject] private readonly ILogger<CustomerController> logger;

    [PostConstruct]
    private void OnInitialized()
    {
        logger.LogInformation("CustomerController initialized with {Service}", customerService.GetType().Name);
    }
}
```

The `[PostConstruct]` method will be called automatically at the end of the generated constructor, after all field assignments are complete.

**Rules:**
- The method must be parameterless
- Only one `[PostConstruct]` method is allowed per class
- The method cannot be static
- Each class in an inheritance hierarchy can have its own `[PostConstruct]` method (base class method runs first)
- Nested classes each have their own scope for `[PostConstruct]`

**Diagnostics:**
| Rule ID | Severity | Description |
|---------|----------|-------------|
| INJECT001 | Error | PostConstruct method must be parameterless |
| INJECT002 | Error | Only one PostConstruct method allowed per class |
| INJECT003 | Error | PostConstruct method cannot be static |
| INJECT004 | Warning | PostConstruct method in class without `[Inject]` members |

## Interface Implementation

There's also an analyzer that will check if all `public` class members are known on the implemented interface. The analyzer provides a code-fix to add the missing members.

```csharp
public interface ICustomerService { }

[Register(typeof(ICustomerService), ServiceLifetime.Scoped)]
internal class CustomerService : ICustomerService {
    public Task<Customer> GetCustomer(int id) => throw new NotImplementedException();
}
```

This also works when the interface resides in an abstractions-library and the class resides in an implementation-library. Which is why this analyzer is so powerful.

