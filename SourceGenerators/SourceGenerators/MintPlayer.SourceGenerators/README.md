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

The type must be `partial`, and so must every type it is nested in; otherwise the generator reports INJECT006.
The constructor is `public`, unless a dependency's type is less accessible than the type itself: a public class
that injects an internal interface gets an `internal` constructor. Nullable dependencies (`IFoo? foo`) become
optional parameters, placed after the required ones.

### Records and structs

`[Inject]` works on a `class`, `record` (or `record class`), `struct` and `record struct`, at top level or nested
in any of them (or in an interface). A value type's constructor chains `: this()`. Some consequences of the type kind:

- **Structs:** `default(S)` and `new S()` do not run the generated constructor, so the injected fields are `null`
  and `[PostConstruct]` does not run. Only a constructed instance (through the container, `ActivatorUtilities` or
  the generated constructor) has its dependencies.
- **Records:** the compiler-generated equality compares every field, so two records are equal only when they hold
  the same injected service instances.
- **`with` expressions** copy the injected fields into the new instance and do **not** run `[PostConstruct]` again.
- A type with a parameter list (a positional record, or a class or struct with a primary constructor) cannot get
  a second constructor: that is INJECT007. Take the dependencies as primary-constructor parameters instead.

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
- The method must return `void`: a constructor cannot await a `Task`, so an async method would run fire-and-forget
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
| INJECT005 | Error | PostConstruct method must return void |
| INJECT006 | Error | The type, or a type it is nested in, is not partial (or is file-local, or an interface) |
| INJECT007 | Error | The type has a parameter list (positional record or primary constructor) |

## Configuration Binding

Simplify reading configuration values with `[Config]`, `[ConnectionString]`, and `[Options]` attributes:

```csharp
public partial class DatabaseService
{
    // Required config values (throws if missing)
    [Config("Database:Type")]
    private readonly DatabaseType databaseType;

    // Optional with default value
    [Config("Database:MaxRetries", DefaultValue = 3)]
    private readonly int maxRetries;

    // TimeSpan parsing
    [Config("Database:Timeout")]
    private readonly TimeSpan timeout;

    // Connection string
    [ConnectionString("DefaultConnection")]
    private readonly string connectionString;

    // Nullable = optional (won't throw if missing)
    [ConnectionString("OptionalDb")]
    private readonly string? optionalConnection;
}
```

### IOptions Pattern

`[Options]` injects an `IOptions<T>`, `IOptionsSnapshot<T>` or `IOptionsMonitor<T>` through the constructor. It
does **not** bind the options: the section argument is documentation only. Bind each options type once, where it
is registered:

```csharp
services.AddOptions<EmailSettings>().BindConfiguration("Email");
services.AddOptions<CustomerConfig>().BindConfiguration("Customer");
services.AddOptions<FeatureFlags>().BindConfiguration("Features");
```

```csharp
public partial class EmailService
{
    [Options("Email")]   // documents the section; the registration above does the binding
    private readonly IOptions<EmailSettings> emailOptions;

    [Options("Customer")]
    private readonly IOptionsSnapshot<CustomerConfig> customerOptions;

    [Options("Features")]
    private readonly IOptionsMonitor<FeatureFlags> featureFlags;
}
```

Keys and names (`[Config(Keys.Database)]`, `[ConnectionString(Names.Main)]`, `[Config("Db:" + "Host")]`) may be
any constant expression.

### IConfiguration Deduplication

When you inject `IConfiguration` explicitly, the generator reuses it:

```csharp
public partial class ConfigAwareService
{
    [Inject] private readonly IConfiguration configuration;

    [Config("App:Name")]
    private readonly string appName;  // Uses 'configuration' field

    public string GetCustomValue(string key) => configuration[key];
}
```

### Supported Types

| Category | Types |
|----------|-------|
| **Primitives** | `string`, `bool`, `char`, `byte`, `short`, `int`, `long`, `float`, `double`, `decimal` |
| **Nullable** | `int?`, `bool?`, `string?`, etc. |
| **Enums** | Any enum type |
| **Date/Time** | `DateTime`, `DateTimeOffset`, `TimeSpan`, `DateOnly`, `TimeOnly` |
| **Other** | `Guid`, `Uri` |
| **Complex** | POCO classes, arrays, `List<T>` |

### Configuration Diagnostics

| Rule ID | Severity | Description |
|---------|----------|-------------|
| CONFIG001 | Error | Class must be partial |
| CONFIG002 | Error | Empty configuration key |
| CONFIG003 | Error | Unsupported field type |
| CONFIG006 | Error | Conflicting [Config] and [ConnectionString] |
| CONFIG008 | Error | Conflicting [Config] and [Inject] |
| CONNSTR001 | Error | Empty connection string name |
| CONNSTR002 | Error | [ConnectionString] requires string type |
| OPTIONS001 | Error | Invalid options type |
| OPTIONS003 | Error | Conflicting [Options] and [Inject] |

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

## Generic Method Overloads

`[GenericMethod]` turns a private method that takes one collection of `object` into public generic overloads, one per
arity, each passing its arguments to the method as one collection expression and returning its result:

```csharp
using MintPlayer.SourceGenerators.Attributes;

public partial class QueryBuilder
{
    [GenericMethod(3)]
    private QueryBuilder Where(object[] values) { /* ... */ return this; }
}

// Generated:
// public QueryBuilder Where<T1, T2>(T1 t1, T2 t2) { return this.Where([t1, t2]); }
// public QueryBuilder Where<T1, T2, T3>(T1 t1, T2 t2, T3 t3) { return this.Where([t1, t2, t3]); }
```

- **Count** (constructor argument or `Count = n`) is the highest arity, from 2 to 16. The default is 4. Constants,
  arithmetic and casts all work: `[GenericMethod(Count = MaxArity)]`.
- **The method** must be `private` (or have no accessibility modifier), must not be generic, and must take exactly one
  by-value parameter that a collection expression can build from `object` elements: `object[]`, `params object[]`,
  `IEnumerable<object>`, `IReadOnlyList<object>`, `List<object>`, `ReadOnlySpan<object>`, `ImmutableArray<object>`, ...
  It may be static, and may return anything (`void`, a value, `Task`, `ValueTask<T>`): the overloads return its result.
- **The type** may be a class, record, struct, record struct or interface, generic or not, nested or not, with any
  accessibility; it and every containing type must be `partial`. When a type parameter in scope is already called
  `T1`, the overloads use `TArg1`, `TArg2`, ... instead.
- **Nullable:** when the collection's elements are non-nullable `object`, each type parameter gets `where T1 : notnull`.
  Declare the elements as `object?` to accept null arguments.

There is no one-argument overload. `Where<T1>(T1 t1)` would also bind a call that passes a whole collection,
`Where(list)`, and wrap the list in a one-element collection.

### When to prefer `params` collections

Since C# 13, `params` works on any collection type, `params ReadOnlySpan<object>` included, without an array
allocation. If the method's callers need nothing more than "any number of arguments", a public
`Where(params ReadOnlySpan<object> values)` is simpler and needs no generator. `[GenericMethod]` is worth it when the
overloads have to be generic, for instance so that every argument keeps its static type at the call site (overload
resolution and analyzers see `T1`, `T2`, ... rather than `object`), or when the project targets a language version
before C# 13. Either way the arguments end up in one collection of `object`, so value types are boxed.

### Generic Method Diagnostics

| Rule ID | Severity | Description |
|---------|----------|-------------|
| GENMETHOD001 | Error | The type or a containing type is not partial (or is `file`-local) |
| GENMETHOD002 | Error | The method is not private |
| GENMETHOD003 | Error | The method's shape is unsupported: not exactly one parameter, not a collection of `object`, generic, `ref` |
| GENMETHOD004 | Error | `Count` is outside 2..16 |
