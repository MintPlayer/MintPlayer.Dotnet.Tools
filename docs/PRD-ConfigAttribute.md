# Product Requirements Document: Configuration Binding Attributes

## Document Information

- **Project**: MintPlayer.SourceGenerators
- **Feature**: Configuration Binding via Source Generation
- **Version**: 1.0
- **Last Updated**: 2026-01-25

---

## Table of Contents

1. [Executive Summary](#executive-summary)
2. [Background and Context](#background-and-context)
3. [Problem Statement](#problem-statement)
4. [Proposed Solution](#proposed-solution)
5. [Detailed Requirements](#detailed-requirements)
6. [Code Generation Behavior](#code-generation-behavior)
7. [IOptions Pattern Support](#ioptions-pattern-support)
8. [Validation and Diagnostics](#validation-and-diagnostics)
9. [Implementation Plan](#implementation-plan)
10. [File Structure](#file-structure)
11. [API Examples](#api-examples)
12. [Appendices](#appendices)

---

## Executive Summary

This PRD describes a new source generator feature for MintPlayer.SourceGenerators that simplifies configuration value injection. The feature introduces four new attributes:

1. **`[Config]`** - Reads values from `IConfiguration` by key path
2. **`[ConnectionString]`** - Reads connection strings via `IConfiguration.GetConnectionString()`
3. **`[Options]`** - Injects `IOptions<T>` for strongly-typed configuration sections
4. **`[OptionsSnapshot]`** / **`[OptionsMonitor]`** - Injects `IOptionsSnapshot<T>` or `IOptionsMonitor<T>` for reloadable configuration

These attributes integrate with the existing `[Inject]` system to generate constructor code that reads and parses configuration values at construction time.

---

## Background and Context

### Existing MintPlayer.SourceGenerators Architecture

The MintPlayer.SourceGenerators project provides compile-time code generation for dependency injection patterns. Understanding the existing architecture is essential for implementing this feature.

#### Project Structure

```
SourceGenerators/
├── SourceGenerators/
│   ├── MintPlayer.SourceGenerators.Attributes/     # Public attributes
│   │   ├── InjectAttribute.cs
│   │   ├── PostConstructAttribute.cs
│   │   ├── RegisterAttribute.cs
│   │   └── RegisterFactoryAttribute.cs
│   │
│   └── MintPlayer.SourceGenerators/                # Generator implementations
│       ├── Generators/
│       │   ├── InjectSourceGenerator.cs
│       │   ├── InjectSourceGenerator.Producer.cs
│       │   ├── InjectSourceGenerator.Rules.cs
│       │   ├── ServiceRegistrationsGenerator.cs
│       │   └── ServiceRegistrationsGenerator.Producer.cs
│       │
│       └── Models/
│           ├── InjectField.cs
│           └── ClassWithBaseDependenciesAndInjectFields.cs
│
└── MintPlayer.SourceGenerators.Tools/              # Shared infrastructure
    ├── IncrementalGenerator.cs                     # Base class for generators
    ├── Producer.cs                                 # Base class for code producers
    └── IDiagnosticReporter.cs                      # Interface for diagnostics
```

#### Existing Attributes

**`[Inject]`** - Marks fields/properties for constructor injection:
```csharp
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class InjectAttribute : Attribute { }
```

**`[PostConstruct]`** - Marks a method to be called after constructor injection:
```csharp
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public class PostConstructAttribute : Attribute { }
```

**`[Register]`** - Registers a class for dependency injection:
```csharp
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Assembly, AllowMultiple = true)]
public class RegisterAttribute : Attribute
{
    public RegisterAttribute(Type serviceType, ServiceLifetime lifetime) { }
}
```

**`[RegisterFactory]`** - Marks a static factory method for service instantiation:
```csharp
[AttributeUsage(AttributeTargets.Method)]
public class RegisterFactoryAttribute : Attribute { }
```

#### How InjectSourceGenerator Works

1. **Discovery Phase**: Scans classes for `[Inject]` fields/properties
2. **Inheritance Resolution**: Collects dependencies from base classes
3. **Constructor Generation**: Produces a constructor that:
   - Accepts all injected dependencies as parameters
   - Calls base constructor with inherited dependencies
   - Assigns injected values to fields/properties
   - Calls `[PostConstruct]` method if present

**Example Input:**
```csharp
public partial class MyService
{
    [Inject] private readonly ILogger<MyService> logger;
    [Inject] private readonly IRepository repository;

    [PostConstruct]
    private void Initialize() { /* ... */ }
}
```

**Generated Output:**
```csharp
public partial class MyService
{
    public MyService(
        Microsoft.Extensions.Logging.ILogger<MyService> logger,
        IRepository repository)
    {
        this.logger = logger;
        this.repository = repository;
        Initialize();
    }
}
```

#### Code Generation Patterns

The existing generators use these patterns:

1. **Incremental Generation**: Uses `IIncrementalGenerator` for performance
2. **Producer Pattern**: Separates discovery from code generation
3. **Diagnostic Reporting**: Uses `IDiagnosticReporter` for compile-time errors
4. **IndentedTextWriter**: Formats generated code with proper indentation

---

## Problem Statement

Currently, developers using MintPlayer.SourceGenerators must manually:

1. Inject `IConfiguration` using the `[Inject]` attribute
2. Write `[PostConstruct]` methods or factory methods to read configuration values
3. Handle type conversion and parsing manually (especially for enums and complex types)
4. Repeat this boilerplate for every configuration-backed field

### Current Approach (Verbose)

```csharp
[Register(typeof(IDatabaseConnection), ServiceLifetime.Scoped)]
public partial class DatabaseConnection : IDatabaseConnection
{
    [Inject] private readonly IConfiguration configuration;
    private EDatabaseType databaseType;
    private string connectionString;
    private int maxRetries;

    [PostConstruct]
    private void ReadConfiguration()
    {
        databaseType = Enum.Parse<EDatabaseType>(configuration["Database:Type"]!);
        connectionString = configuration.GetConnectionString("CoreDatabase")!;
        maxRetries = int.Parse(configuration["Database:MaxRetries"] ?? "3");
    }
}
```

### Problems

- **Verbose, repetitive code**: Same pattern repeated across many classes
- **Error-prone**: Manual parsing can introduce bugs
- **Inconsistent**: Different developers may parse values differently
- **No compile-time validation**: Typos in config keys aren't caught until runtime

---

## Proposed Solution

Introduce four new attributes that generate configuration binding code automatically:

### Desired Developer Experience

```csharp
[Register(typeof(IDatabaseConnection), ServiceLifetime.Scoped)]
public partial class DatabaseConnection : IDatabaseConnection
{
    [Config("Database:Type")]
    private readonly EDatabaseType databaseType;

    [Config("Database:MaxRetries", Required = false, DefaultValue = 3)]
    private readonly int maxRetries;

    [ConnectionString("CoreDatabase")]
    private readonly string connectionString;

    [Options("Email")]
    private readonly IOptions<EmailOptions> emailOptions;

    [Inject]
    private readonly ILogger<DatabaseConnection> logger;
}
```

The generator produces all necessary constructor code, handling type parsing, null checks, and IConfiguration injection automatically.

---

## Detailed Requirements

### 1. Attribute Definitions

#### 1.1 ConfigAttribute

```csharp
namespace MintPlayer.SourceGenerators.Attributes
{
    /// <summary>
    /// Marks a field or property to be populated from IConfiguration at construction time.
    /// The generator will automatically inject IConfiguration and read the specified key.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    public class ConfigAttribute : Attribute
    {
        /// <summary>
        /// The configuration key path (e.g., "Database:Type" or "Logging:LogLevel:Default").
        /// Uses the same colon-separated format as IConfiguration indexer.
        /// </summary>
        public string Key { get; }

        /// <summary>
        /// Whether the configuration value is required.
        /// If true (default) and value is missing, throws InvalidOperationException at construction.
        /// If false and value is missing, uses DefaultValue or type's default.
        /// </summary>
        public bool Required { get; set; } = true;

        /// <summary>
        /// Default value when Required=false and the configuration key is not found.
        /// Must be a compile-time constant. Type must match or be convertible to field type.
        /// </summary>
        public object? DefaultValue { get; set; }

        public ConfigAttribute(string key)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
        }
    }
}
```

#### 1.2 ConnectionStringAttribute

```csharp
namespace MintPlayer.SourceGenerators.Attributes
{
    /// <summary>
    /// Marks a string field or property to be populated from a connection string.
    /// Uses IConfiguration.GetConnectionString() which reads from the "ConnectionStrings" section.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    public class ConnectionStringAttribute : Attribute
    {
        /// <summary>
        /// The name of the connection string in the ConnectionStrings configuration section.
        /// This is passed to IConfiguration.GetConnectionString(name).
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Whether the connection string is required.
        /// If true (default) and not found, throws InvalidOperationException at construction.
        /// </summary>
        public bool Required { get; set; } = true;

        public ConnectionStringAttribute(string name)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
        }
    }
}
```

#### 1.3 OptionsAttribute

```csharp
namespace MintPlayer.SourceGenerators.Attributes
{
    /// <summary>
    /// Marks a field or property to receive IOptions&lt;T&gt; for a configuration section.
    /// The field type must be IOptions&lt;T&gt;, IOptionsSnapshot&lt;T&gt;, or IOptionsMonitor&lt;T&gt;.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    public class OptionsAttribute : Attribute
    {
        /// <summary>
        /// The configuration section name to bind to the options type.
        /// If null or empty, binds to the root configuration.
        /// </summary>
        public string? Section { get; }

        /// <summary>
        /// Creates an OptionsAttribute that binds to the specified configuration section.
        /// </summary>
        /// <param name="section">The configuration section name (e.g., "Email", "Database:Settings")</param>
        public OptionsAttribute(string? section = null)
        {
            Section = section;
        }
    }
}
```

### 2. Supported Field Types

The source generator must support the following field types with appropriate parsing:

#### 2.1 For `[Config]` Attribute

| Category | Types | Generated Code Pattern |
|----------|-------|------------------------|
| **String** | `string` | Direct assignment from `configuration[key]` |
| **Signed Integers** | `sbyte`, `short`, `int`, `long` | `int.Parse(value)` |
| **Unsigned Integers** | `byte`, `ushort`, `uint`, `ulong` | `uint.Parse(value)` |
| **Floating Point** | `float`, `double`, `decimal` | `double.Parse(value, CultureInfo.InvariantCulture)` |
| **Boolean** | `bool` | `bool.Parse(value)` |
| **Character** | `char` | `value[0]` |
| **Nullable Primitives** | `int?`, `bool?`, etc. | `value is null ? null : int.Parse(value)` |
| **Enums** | Any enum type | `Enum.Parse<TEnum>(value)` |
| **Nullable Enums** | `TEnum?` | `value is null ? null : Enum.Parse<TEnum>(value)` |
| **Date/Time** | `DateTime` | `DateTime.Parse(value, CultureInfo.InvariantCulture)` |
| | `DateTimeOffset` | `DateTimeOffset.Parse(value, CultureInfo.InvariantCulture)` |
| | `TimeSpan` | `TimeSpan.Parse(value, CultureInfo.InvariantCulture)` |
| | `DateOnly` (.NET 6+) | `DateOnly.Parse(value, CultureInfo.InvariantCulture)` |
| | `TimeOnly` (.NET 6+) | `TimeOnly.Parse(value, CultureInfo.InvariantCulture)` |
| **Other Built-in** | `Guid` | `Guid.Parse(value)` |
| | `Uri` | `new Uri(value)` |
| **Complex Types** | POCO classes | `configuration.GetSection(key).Get<T>()` |
| **Collections** | `T[]`, `List<T>`, `IEnumerable<T>` | `configuration.GetSection(key).Get<T[]>()` |

#### 2.2 For `[ConnectionString]` Attribute

| Type | Behavior |
|------|----------|
| `string` | Direct assignment from `configuration.GetConnectionString(name)` |
| Any other type | Compile error (CONNSTR002) |

#### 2.3 For `[Options]` Attribute

| Type | Behavior |
|------|----------|
| `IOptions<T>` | Inject `IOptions<T>` from DI container |
| `IOptionsSnapshot<T>` | Inject `IOptionsSnapshot<T>` from DI container |
| `IOptionsMonitor<T>` | Inject `IOptionsMonitor<T>` from DI container |
| Any other type | Compile error (OPTIONS001) |

---

## Code Generation Behavior

### 3. Integration with Existing [Inject] System

The new attributes integrate with the existing `InjectSourceGenerator`:

#### 3.1 Automatic IConfiguration Injection (Deduplication)

**Critical Requirement**: The generator must detect if `IConfiguration` is already being injected (via `[Inject]`) and reuse that instance rather than adding a duplicate parameter.

**Detection Logic:**
1. Scan all `[Inject]` fields/properties in the class
2. Check if any have type `Microsoft.Extensions.Configuration.IConfiguration`
3. If found: use the existing field name for config operations
4. If not found: add `IConfiguration` as a constructor parameter with name `__configuration`

**Example - Without Explicit IConfiguration:**
```csharp
public partial class MyService
{
    [Config("App:Name")]
    private readonly string appName;
}
```
Generated:
```csharp
public partial class MyService
{
    public MyService(Microsoft.Extensions.Configuration.IConfiguration __configuration)
    {
        this.appName = __configuration["App:Name"]
            ?? throw new global::System.InvalidOperationException("Configuration key 'App:Name' is required but was not found.");
    }
}
```

**Example - With Explicit IConfiguration (No Duplication):**
```csharp
public partial class MyService
{
    [Inject] private readonly IConfiguration configuration;

    [Config("App:Name")]
    private readonly string appName;
}
```
Generated:
```csharp
public partial class MyService
{
    public MyService(Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        this.configuration = configuration;

        // Reuses the injected 'configuration' field, not a separate parameter
        this.appName = configuration["App:Name"]
            ?? throw new global::System.InvalidOperationException("Configuration key 'App:Name' is required but was not found.");
    }
}
```

#### 3.2 Constructor Generation Order

The generated constructor follows this order:
1. Call base constructor (if applicable)
2. Assign `[Inject]` fields/properties
3. Assign `[Config]` fields (using IConfiguration)
4. Assign `[ConnectionString]` fields (using IConfiguration)
5. `[Options]` fields are injected like regular `[Inject]` fields
6. Call `[PostConstruct]` method (if present)

#### 3.3 Full Example

**Input:**
```csharp
[Register(typeof(IDatabaseConnection), ServiceLifetime.Scoped)]
public partial class DatabaseConnection : IDatabaseConnection
{
    // Explicit IConfiguration injection - generator will reuse this
    [Inject] private readonly IConfiguration configuration;

    // Config values - will use 'configuration' field
    [Config("Database:Type")]
    private readonly EDatabaseType databaseType;

    [Config("Database:MaxRetries", Required = false, DefaultValue = 3)]
    private readonly int maxRetries;

    [Config("Database:Timeout")]
    private readonly TimeSpan timeout;

    // Connection string - will use 'configuration' field
    [ConnectionString("CoreDatabase")]
    private readonly string connectionString;

    // Options - injected as constructor parameter
    [Options("Database:Pool")]
    private readonly IOptions<PoolOptions> poolOptions;

    // Regular injection
    [Inject]
    private readonly ILogger<DatabaseConnection> logger;

    [PostConstruct]
    private void OnConstructed() { /* ... */ }
}
```

**Generated:**
```csharp
public partial class DatabaseConnection
{
    public DatabaseConnection(
        Microsoft.Extensions.Configuration.IConfiguration configuration,
        Microsoft.Extensions.Options.IOptions<PoolOptions> poolOptions,
        Microsoft.Extensions.Logging.ILogger<DatabaseConnection> logger)
    {
        // [Inject] field assignments
        this.configuration = configuration;
        this.poolOptions = poolOptions;
        this.logger = logger;

        // [Config] field assignments (reusing 'configuration' field)
        this.databaseType = global::System.Enum.Parse<EDatabaseType>(
            configuration["Database:Type"]
                ?? throw new global::System.InvalidOperationException("Configuration key 'Database:Type' is required but was not found."));

        this.maxRetries = configuration["Database:MaxRetries"] is string __maxRetriesValue
            ? int.Parse(__maxRetriesValue)
            : 3;

        this.timeout = global::System.TimeSpan.Parse(
            configuration["Database:Timeout"]
                ?? throw new global::System.InvalidOperationException("Configuration key 'Database:Timeout' is required but was not found."),
            global::System.Globalization.CultureInfo.InvariantCulture);

        // [ConnectionString] field assignments
        this.connectionString = configuration.GetConnectionString("CoreDatabase")
            ?? throw new global::System.InvalidOperationException("Connection string 'CoreDatabase' is required but was not found.");

        // [PostConstruct] call
        OnConstructed();
    }
}
```

### 4. Factory Method Support

When used with `[RegisterFactory]`, the pattern changes slightly. Factory methods receive dependencies as parameters.

**Input:**
```csharp
[Register(typeof(IDatabaseConnection), ServiceLifetime.Scoped)]
public partial class DatabaseConnection : IDatabaseConnection
{
    [Config("Database:Type")]
    private readonly EDatabaseType databaseType;

    [ConnectionString("CoreDatabase")]
    private readonly string connectionString;

    [RegisterFactory]
    public static IDatabaseConnection Create(IConfiguration configuration, ILogger<DatabaseConnection> logger)
    {
        // Factory can perform additional logic
        return new DatabaseConnection(configuration);
    }
}
```

**Generated:**
```csharp
public partial class DatabaseConnection
{
    public DatabaseConnection(Microsoft.Extensions.Configuration.IConfiguration __configuration)
    {
        this.databaseType = global::System.Enum.Parse<EDatabaseType>(__configuration["Database:Type"]!);
        this.connectionString = __configuration.GetConnectionString("CoreDatabase")!;
    }
}
```

---

## IOptions Pattern Support

### 5. Overview

The `[Options]` attribute provides integration with Microsoft.Extensions.Options for strongly-typed configuration. This is the recommended pattern for complex configuration sections that benefit from:

- Strongly-typed access
- Validation via DataAnnotations
- Hot reload support (with `IOptionsMonitor<T>`)
- Scoped configuration (with `IOptionsSnapshot<T>`)

### 5.1 Supported Options Types

| Field Type | Behavior | Use Case |
|------------|----------|----------|
| `IOptions<T>` | Singleton, read once at startup | Static configuration that never changes |
| `IOptionsSnapshot<T>` | Scoped, re-read per request | Configuration that may change between requests |
| `IOptionsMonitor<T>` | Singleton with change notifications | Long-lived services that need to react to config changes |

### 5.2 How [Options] Differs from [Config]

| Aspect | `[Config]` | `[Options]` |
|--------|-----------|-------------|
| **Value Type** | Primitive, enum, or POCO | `IOptions<T>`, `IOptionsSnapshot<T>`, `IOptionsMonitor<T>` |
| **Read Time** | At construction (eager) | At access via `.Value` (lazy) |
| **Reloadable** | No (value is copied) | Yes (with `IOptionsSnapshot`/`IOptionsMonitor`) |
| **Validation** | No | Yes (via `IValidateOptions<T>`) |
| **Dependencies** | Requires `IConfiguration` | Requires options infrastructure |

### 5.3 Usage Examples

**Basic IOptions:**
```csharp
public partial class EmailService
{
    [Options("Email")]
    private readonly IOptions<EmailOptions> emailOptions;

    public void SendEmail()
    {
        var settings = emailOptions.Value;
        // Use settings.SmtpServer, settings.Port, etc.
    }
}
```

**IOptionsSnapshot for Scoped Configuration:**
```csharp
[Register(typeof(IReportService), ServiceLifetime.Scoped)]
public partial class ReportService : IReportService
{
    [Options("Reporting")]
    private readonly IOptionsSnapshot<ReportingOptions> reportingOptions;

    public void GenerateReport()
    {
        // Gets fresh config values for each scope (e.g., HTTP request)
        var options = reportingOptions.Value;
    }
}
```

**IOptionsMonitor for Change Notifications:**
```csharp
[Register(typeof(IBackgroundService), ServiceLifetime.Singleton)]
public partial class BackgroundService : IBackgroundService
{
    [Options("Background")]
    private readonly IOptionsMonitor<BackgroundOptions> backgroundOptions;

    [PostConstruct]
    private void Initialize()
    {
        // React to configuration changes
        backgroundOptions.OnChange(newOptions =>
        {
            // Handle configuration update
        });
    }
}
```

### 5.4 Generated Code for [Options]

The `[Options]` attribute generates code that injects the options interface directly, since options are registered in DI via `services.Configure<T>()`.

**Input:**
```csharp
public partial class MyService
{
    [Options("MySection")]
    private readonly IOptions<MyOptions> options;
}
```

**Generated:**
```csharp
public partial class MyService
{
    public MyService(Microsoft.Extensions.Options.IOptions<MyOptions> options)
    {
        this.options = options;
    }
}
```

**Note:** The `Section` parameter in `[Options("MySection")]` is informational/documentary. The actual section binding is done when registering the options:
```csharp
services.Configure<MyOptions>(configuration.GetSection("MySection"));
```

The generator can optionally emit a diagnostic hint (OPTIONS002 - Info) if the section binding registration is not detected, but this is not a blocking error.

### 5.5 Combining [Config] and [Options]

Both patterns can be used together in the same class:

```csharp
[Register(typeof(IHybridService), ServiceLifetime.Scoped)]
public partial class HybridService : IHybridService
{
    // Simple values via [Config] - read once at construction
    [Config("App:Environment")]
    private readonly string environment;

    [Config("App:DebugMode")]
    private readonly bool debugMode;

    // Complex reloadable config via [Options]
    [Options("App:Features")]
    private readonly IOptionsSnapshot<FeatureOptions> featureOptions;

    // Injected services
    [Inject]
    private readonly ILogger<HybridService> logger;
}
```

---

## Validation and Diagnostics

### 6. Compile-Time Diagnostics

#### 6.1 Config Diagnostics

| Rule ID | Severity | Condition | Message |
|---------|----------|-----------|---------|
| CONFIG001 | Error | `[Config]` on non-partial class | Class '{0}' must be partial to use [Config] attribute |
| CONFIG002 | Error | Empty or whitespace key | Configuration key cannot be empty or whitespace |
| CONFIG003 | Error | Unsupported field type | Type '{0}' is not supported for [Config]. Supported: primitives, enums, DateTime, TimeSpan, Guid, Uri, and POCO classes |
| CONFIG004 | Warning | `Required = false` without `DefaultValue` on non-nullable value type | Non-nullable field '{0}' with Required=false should specify a DefaultValue to avoid default(T) |
| CONFIG005 | Error | `DefaultValue` type mismatch | DefaultValue type '{0}' is not compatible with field type '{1}' |
| CONFIG006 | Error | Both `[Config]` and `[ConnectionString]` on same member | Field '{0}' cannot have both [Config] and [ConnectionString] attributes |
| CONFIG007 | Warning | Duplicate configuration key in same class | Configuration key '{0}' is used multiple times in class '{1}' |
| CONFIG008 | Error | `[Config]` combined with `[Inject]` on same member | Field '{0}' cannot have both [Config] and [Inject] attributes |

#### 6.2 ConnectionString Diagnostics

| Rule ID | Severity | Condition | Message |
|---------|----------|-----------|---------|
| CONNSTR001 | Error | Empty or whitespace name | Connection string name cannot be empty or whitespace |
| CONNSTR002 | Error | Non-string field type | [ConnectionString] can only be applied to string fields. Field '{0}' has type '{1}' |
| CONNSTR003 | Error | `[ConnectionString]` combined with `[Inject]` | Field '{0}' cannot have both [ConnectionString] and [Inject] attributes |

#### 6.3 Options Diagnostics

| Rule ID | Severity | Condition | Message |
|---------|----------|-----------|---------|
| OPTIONS001 | Error | Invalid field type | [Options] requires field type IOptions<T>, IOptionsSnapshot<T>, or IOptionsMonitor<T>. Field '{0}' has type '{1}' |
| OPTIONS002 | Info | Section specified but binding not detected | Consider registering options binding: services.Configure<{0}>(configuration.GetSection("{1}")) |
| OPTIONS003 | Error | `[Options]` combined with `[Inject]` | Field '{0}' cannot have both [Options] and [Inject] attributes. [Options] already handles injection |
| OPTIONS004 | Error | `[Options]` combined with `[Config]` | Field '{0}' cannot have both [Options] and [Config] attributes |

### 6.2 Runtime Validation

- **Required values**: When `Required = true` (default), a missing configuration value throws `InvalidOperationException` with a descriptive message including the key name
- **Parse failures**: Invalid values throw `FormatException` with context about which key failed to parse
- **Connection strings**: Missing required connection strings throw `InvalidOperationException`

### 6.3 Diagnostic Implementation

```csharp
namespace MintPlayer.SourceGenerators.Diagnostics
{
    public static class ConfigDiagnostics
    {
        private const string Category = "MintPlayer.SourceGenerators.Config";

        public static readonly DiagnosticDescriptor CONFIG001 = new(
            id: "CONFIG001",
            title: "Non-partial class",
            messageFormat: "Class '{0}' must be partial to use [Config] attribute",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor CONFIG002 = new(
            id: "CONFIG002",
            title: "Empty configuration key",
            messageFormat: "Configuration key cannot be empty or whitespace",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor CONFIG003 = new(
            id: "CONFIG003",
            title: "Unsupported type",
            messageFormat: "Type '{0}' is not supported for [Config]. Supported: primitives, enums, DateTime, TimeSpan, Guid, Uri, and POCO classes",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor CONFIG004 = new(
            id: "CONFIG004",
            title: "Missing default value",
            messageFormat: "Non-nullable field '{0}' with Required=false should specify a DefaultValue to avoid default(T)",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor CONFIG005 = new(
            id: "CONFIG005",
            title: "Default value type mismatch",
            messageFormat: "DefaultValue type '{0}' is not compatible with field type '{1}'",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor CONFIG006 = new(
            id: "CONFIG006",
            title: "Conflicting attributes",
            messageFormat: "Field '{0}' cannot have both [Config] and [ConnectionString] attributes",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor CONFIG007 = new(
            id: "CONFIG007",
            title: "Duplicate configuration key",
            messageFormat: "Configuration key '{0}' is used multiple times in class '{1}'",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor CONFIG008 = new(
            id: "CONFIG008",
            title: "Conflicting with Inject",
            messageFormat: "Field '{0}' cannot have both [Config] and [Inject] attributes",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);
    }

    public static class ConnectionStringDiagnostics
    {
        private const string Category = "MintPlayer.SourceGenerators.Config";

        public static readonly DiagnosticDescriptor CONNSTR001 = new(
            id: "CONNSTR001",
            title: "Empty connection string name",
            messageFormat: "Connection string name cannot be empty or whitespace",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor CONNSTR002 = new(
            id: "CONNSTR002",
            title: "Invalid field type",
            messageFormat: "[ConnectionString] can only be applied to string fields. Field '{0}' has type '{1}'",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor CONNSTR003 = new(
            id: "CONNSTR003",
            title: "Conflicting with Inject",
            messageFormat: "Field '{0}' cannot have both [ConnectionString] and [Inject] attributes",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);
    }

    public static class OptionsDiagnostics
    {
        private const string Category = "MintPlayer.SourceGenerators.Config";

        public static readonly DiagnosticDescriptor OPTIONS001 = new(
            id: "OPTIONS001",
            title: "Invalid options type",
            messageFormat: "[Options] requires field type IOptions<T>, IOptionsSnapshot<T>, or IOptionsMonitor<T>. Field '{0}' has type '{1}'",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor OPTIONS002 = new(
            id: "OPTIONS002",
            title: "Options binding hint",
            messageFormat: "Consider registering options binding: services.Configure<{0}>(configuration.GetSection(\"{1}\"))",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Info,
            isEnabledByDefault: false);  // Disabled by default as it's just a hint

        public static readonly DiagnosticDescriptor OPTIONS003 = new(
            id: "OPTIONS003",
            title: "Conflicting with Inject",
            messageFormat: "Field '{0}' cannot have both [Options] and [Inject] attributes. [Options] already handles injection",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor OPTIONS004 = new(
            id: "OPTIONS004",
            title: "Conflicting with Config",
            messageFormat: "Field '{0}' cannot have both [Options] and [Config] attributes",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);
    }
}
```

---

## Implementation Plan

### Phase 1: Core Attributes and Basic Types
1. Create `ConfigAttribute` in MintPlayer.SourceGenerators.Attributes
2. Create `ConnectionStringAttribute` in MintPlayer.SourceGenerators.Attributes
3. Modify `InjectSourceGenerator` to detect `[Config]` and `[ConnectionString]` fields
4. Implement basic type parsing (string, int, bool, long, double)
5. Implement IConfiguration deduplication logic
6. Add diagnostic rules CONFIG001-CONFIG003, CONNSTR001-CONNSTR002

### Phase 2: Advanced Type Support
1. Add enum parsing with `Enum.Parse<T>()`
2. Add nullable type support (`T?`)
3. Add DateTime, TimeSpan, DateOnly, TimeOnly support
4. Add Guid and Uri support
5. Add diagnostic rules CONFIG004-CONFIG006

### Phase 3: Complex Types and Collections
1. Implement complex type binding via `GetSection().Get<T>()`
2. Implement array and `List<T>` support
3. Add diagnostic rules CONFIG007-CONFIG008

### Phase 4: IOptions Support
1. Create `OptionsAttribute`
2. Detect `IOptions<T>`, `IOptionsSnapshot<T>`, `IOptionsMonitor<T>` field types
3. Generate constructor parameters for options types
4. Add diagnostic rules OPTIONS001-OPTIONS004

### Phase 5: Inheritance and Edge Cases
1. Support configuration fields in base classes
2. Support generic classes with configuration fields
3. Test integration with `[RegisterFactory]` pattern
4. Handle nested classes

### Phase 6: Polish and Documentation
1. Add MSBuild property support for global defaults
2. Write XML documentation for all public APIs
3. Create sample projects demonstrating all features
4. Add unit tests for all scenarios

---

## File Structure

### New Files

```
SourceGenerators/
├── MintPlayer.SourceGenerators.Attributes/
│   ├── ConfigAttribute.cs                    [NEW]
│   ├── ConnectionStringAttribute.cs          [NEW]
│   └── OptionsAttribute.cs                   [NEW]
│
└── MintPlayer.SourceGenerators/
    ├── Diagnostics/
    │   ├── ConfigDiagnostics.cs              [NEW]
    │   ├── ConnectionStringDiagnostics.cs    [NEW]
    │   └── OptionsDiagnostics.cs             [NEW]
    │
    └── Models/
        ├── ConfigField.cs                    [NEW]
        ├── ConnectionStringField.cs          [NEW]
        └── OptionsField.cs                   [NEW]
```

### Modified Files

```
SourceGenerators/
└── MintPlayer.SourceGenerators/
    └── Generators/
        ├── InjectSourceGenerator.cs          [MODIFY]
        │   - Add detection of [Config], [ConnectionString], [Options] fields
        │   - Add IConfiguration deduplication logic
        │   - Collect configuration field metadata
        │
        └── InjectSourceGenerator.Producer.cs [MODIFY]
            - Generate config value assignments
            - Generate connection string assignments
            - Ensure correct ordering in constructor
```

---

## API Examples

### Minimal Usage

```csharp
public partial class MyService
{
    [Config("AppSettings:ApiKey")]
    private readonly string apiKey;
}
```

### All Attributes Combined

```csharp
[Register(typeof(IEmailService), ServiceLifetime.Scoped)]
public partial class EmailService : IEmailService
{
    // ============ Configuration Values ============

    // Required string - throws if missing
    [Config("Email:SmtpServer")]
    private readonly string smtpServer;

    // Required enum - automatically parsed
    [Config("Email:Protocol")]
    private readonly EmailProtocol protocol;

    // Optional int with default value
    [Config("Email:Port", Required = false, DefaultValue = 587)]
    private readonly int port;

    // Required TimeSpan - parsed with InvariantCulture
    [Config("Email:Timeout")]
    private readonly TimeSpan timeout;

    // Complex type - bound via GetSection().Get<T>()
    [Config("Email:Credentials")]
    private readonly SmtpCredentials credentials;

    // ============ Connection String ============

    [ConnectionString("EmailDatabase")]
    private readonly string dbConnectionString;

    // ============ Options Pattern ============

    // Singleton options - read once
    [Options("Email:Templates")]
    private readonly IOptions<TemplateOptions> templateOptions;

    // Scoped options - fresh per request
    [Options("Email:RateLimits")]
    private readonly IOptionsSnapshot<RateLimitOptions> rateLimitOptions;

    // Monitored options - reactive to changes
    [Options("Email:Features")]
    private readonly IOptionsMonitor<FeatureFlags> featureFlags;

    // ============ Regular Dependency Injection ============

    [Inject]
    private readonly ILogger<EmailService> logger;

    [Inject]
    private readonly IEmailRenderer renderer;

    // ============ Post-Construction Hook ============

    [PostConstruct]
    private void Initialize()
    {
        logger.LogInformation(
            "Email service configured: {Server}:{Port}, Protocol={Protocol}",
            smtpServer, port, protocol);

        // Subscribe to feature flag changes
        featureFlags.OnChange(flags =>
        {
            logger.LogInformation("Feature flags updated: {Flags}", flags);
        });
    }
}
```

### Corresponding appsettings.json

```json
{
  "Email": {
    "SmtpServer": "smtp.example.com",
    "Protocol": "Tls",
    "Port": 465,
    "Timeout": "00:00:30",
    "Credentials": {
      "Username": "user@example.com",
      "Password": "secret"
    },
    "Templates": {
      "WelcomeTemplate": "welcome.html",
      "ResetTemplate": "reset.html"
    },
    "RateLimits": {
      "MaxPerMinute": 100,
      "MaxPerHour": 1000
    },
    "Features": {
      "EnableTracking": true,
      "EnableTemplateCache": true
    }
  },
  "ConnectionStrings": {
    "EmailDatabase": "Server=localhost;Database=EmailDb;..."
  }
}
```

### With Explicit IConfiguration (Reused, Not Duplicated)

```csharp
public partial class ConfigAwareService
{
    // Explicit IConfiguration injection - generator will reuse this
    [Inject]
    private readonly IConfiguration configuration;

    // These use the 'configuration' field above, not a separate parameter
    [Config("App:Name")]
    private readonly string appName;

    [Config("App:Version")]
    private readonly string version;

    [ConnectionString("MainDb")]
    private readonly string connectionString;

    public string GetCustomValue(string key)
    {
        // Developer can also use configuration directly
        return configuration[key];
    }
}
```

**Generated (note single IConfiguration parameter):**
```csharp
public partial class ConfigAwareService
{
    public ConfigAwareService(Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        this.configuration = configuration;
        this.appName = configuration["App:Name"] ?? throw new ...;
        this.version = configuration["App:Version"] ?? throw new ...;
        this.connectionString = configuration.GetConnectionString("MainDb") ?? throw new ...;
    }
}
```

---

## Appendices

### Appendix A: Type Parsing Code Templates

#### String (Required)
```csharp
this.{fieldName} = {configVar}["{key}"]
    ?? throw new global::System.InvalidOperationException("Configuration key '{key}' is required but was not found.");
```

#### String (Optional)
```csharp
this.{fieldName} = {configVar}["{key}"];
```

#### Int32 (Required)
```csharp
this.{fieldName} = int.Parse({configVar}["{key}"]
    ?? throw new global::System.InvalidOperationException("Configuration key '{key}' is required but was not found."));
```

#### Int32 (Optional with Default)
```csharp
this.{fieldName} = {configVar}["{key}"] is string __{fieldName}Value
    ? int.Parse(__{fieldName}Value)
    : {defaultValue};
```

#### Int32? (Nullable)
```csharp
this.{fieldName} = {configVar}["{key}"] is string __{fieldName}Value
    ? int.Parse(__{fieldName}Value)
    : null;
```

#### Enum (Required)
```csharp
this.{fieldName} = global::System.Enum.Parse<{enumType}>({configVar}["{key}"]
    ?? throw new global::System.InvalidOperationException("Configuration key '{key}' is required but was not found."));
```

#### Enum (Optional with Default)
```csharp
this.{fieldName} = {configVar}["{key}"] is string __{fieldName}Value
    ? global::System.Enum.Parse<{enumType}>(__{fieldName}Value)
    : {defaultValue};
```

#### Nullable Enum
```csharp
this.{fieldName} = {configVar}["{key}"] is string __{fieldName}Value
    ? global::System.Enum.Parse<{enumType}>(__{fieldName}Value)
    : null;
```

#### Boolean (Required)
```csharp
this.{fieldName} = bool.Parse({configVar}["{key}"]
    ?? throw new global::System.InvalidOperationException("Configuration key '{key}' is required but was not found."));
```

#### Double (Required, with InvariantCulture)
```csharp
this.{fieldName} = double.Parse(
    {configVar}["{key}"]
        ?? throw new global::System.InvalidOperationException("Configuration key '{key}' is required but was not found."),
    global::System.Globalization.CultureInfo.InvariantCulture);
```

#### TimeSpan (Required)
```csharp
this.{fieldName} = global::System.TimeSpan.Parse(
    {configVar}["{key}"]
        ?? throw new global::System.InvalidOperationException("Configuration key '{key}' is required but was not found."),
    global::System.Globalization.CultureInfo.InvariantCulture);
```

#### DateTime (Required)
```csharp
this.{fieldName} = global::System.DateTime.Parse(
    {configVar}["{key}"]
        ?? throw new global::System.InvalidOperationException("Configuration key '{key}' is required but was not found."),
    global::System.Globalization.CultureInfo.InvariantCulture);
```

#### Guid (Required)
```csharp
this.{fieldName} = global::System.Guid.Parse({configVar}["{key}"]
    ?? throw new global::System.InvalidOperationException("Configuration key '{key}' is required but was not found."));
```

#### Uri (Required)
```csharp
this.{fieldName} = new global::System.Uri({configVar}["{key}"]
    ?? throw new global::System.InvalidOperationException("Configuration key '{key}' is required but was not found."));
```

#### Complex Type (Required)
```csharp
this.{fieldName} = {configVar}.GetSection("{key}").Get<{typeName}>()
    ?? throw new global::System.InvalidOperationException("Configuration section '{key}' is required but could not be bound to type '{typeName}'.");
```

#### Array/List (Required)
```csharp
this.{fieldName} = {configVar}.GetSection("{key}").Get<{elementType}[]>()
    ?? throw new global::System.InvalidOperationException("Configuration section '{key}' is required but was not found.");
```

#### Connection String (Required)
```csharp
this.{fieldName} = {configVar}.GetConnectionString("{name}")
    ?? throw new global::System.InvalidOperationException("Connection string '{name}' is required but was not found.");
```

#### Connection String (Optional)
```csharp
this.{fieldName} = {configVar}.GetConnectionString("{name}");
```

### Appendix B: Variable Naming Convention

The generator uses specific naming conventions to avoid conflicts:

| Scenario | Variable Name | Example |
|----------|--------------|---------|
| Auto-injected IConfiguration | `__configuration` | `Microsoft.Extensions.Configuration.IConfiguration __configuration` |
| Explicit IConfiguration field | Uses field name | `this.configuration` |
| Temporary parsing variable | `__{fieldName}Value` | `__maxRetriesValue`, `__timeoutValue` |

### Appendix C: Inheritance Scenarios

#### Scenario 1: Base class has [Config], derived class adds more

```csharp
public partial class BaseService
{
    [Config("Service:Name")]
    protected readonly string serviceName;
}

[Register(typeof(IMyService), ServiceLifetime.Scoped)]
public partial class MyService : BaseService, IMyService
{
    [Config("MyService:Timeout")]
    private readonly int timeout;
}
```

**Generated for BaseService:**
```csharp
public partial class BaseService
{
    public BaseService(Microsoft.Extensions.Configuration.IConfiguration __configuration)
    {
        this.serviceName = __configuration["Service:Name"] ?? throw new ...;
    }
}
```

**Generated for MyService:**
```csharp
public partial class MyService
{
    public MyService(Microsoft.Extensions.Configuration.IConfiguration __configuration)
        : base(__configuration)
    {
        this.timeout = int.Parse(__configuration["MyService:Timeout"] ?? throw new ...);
    }
}
```

#### Scenario 2: Base class has [Inject] IConfiguration

```csharp
public partial class ConfigurableBase
{
    [Inject] protected readonly IConfiguration configuration;
}

public partial class DerivedService : ConfigurableBase
{
    [Config("Derived:Value")]
    private readonly string value;
}
```

**Generated for DerivedService:**
```csharp
public partial class DerivedService
{
    public DerivedService(Microsoft.Extensions.Configuration.IConfiguration configuration)
        : base(configuration)
    {
        // Reuses 'configuration' from base, doesn't add new parameter
        this.value = configuration["Derived:Value"] ?? throw new ...;
    }
}
```

### Appendix D: Edge Cases

#### D.1 Generic Classes

```csharp
public partial class Repository<T> where T : class
{
    [Config("Repository:ConnectionString")]
    private readonly string connectionString;

    [Inject]
    private readonly ILogger<Repository<T>> logger;
}
```

Works correctly - generic type parameters are preserved in generated code.

#### D.2 Nested Classes

```csharp
public partial class Outer
{
    public partial class Inner
    {
        [Config("Inner:Value")]
        private readonly string value;
    }
}
```

Generated with proper nesting:
```csharp
public partial class Outer
{
    public partial class Inner
    {
        public Inner(Microsoft.Extensions.Configuration.IConfiguration __configuration)
        {
            this.value = __configuration["Inner:Value"] ?? throw new ...;
        }
    }
}
```

#### D.3 Record Types

```csharp
public partial record ConfiguredRecord
{
    [Config("Record:Id")]
    private readonly Guid id;
}
```

Generates a constructor for the partial record.

---

## Success Criteria

1. **No duplicate IConfiguration**: When `[Inject] IConfiguration` exists, it's reused for all config operations
2. **Compile-time safety**: Invalid attribute usage produces clear diagnostic errors
3. **Zero boilerplate**: Developers only need attributes, no manual parsing code
4. **Full type support**: All common .NET types parsed correctly
5. **Seamless integration**: Works with existing `[Inject]`, `[PostConstruct]`, `[Register]`, `[RegisterFactory]`
6. **Performance**: No runtime reflection; all code generated at compile time
7. **Options support**: Full integration with IOptions/IOptionsSnapshot/IOptionsMonitor pattern
8. **Discoverability**: Clear error messages guide developers to correct usage

---

## Revision History

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0 | 2026-01-25 | - | Initial PRD |
