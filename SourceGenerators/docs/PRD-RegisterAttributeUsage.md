# PRD: Register Attribute Usage Patterns

## Overview

The `[Register]` attribute supports registering services for dependency injection. This document defines the valid usage patterns and the diagnostic errors that should be reported for invalid usage.

## Valid Usage Patterns

### Pattern 1: Class-level, self-registration
```csharp
[Register(ServiceLifetime.Scoped)]
public class MyService { }
```
- **Applied to**: Class
- **Service type**: The class itself (`MyService`)
- **Implementation type**: The class itself (`MyService`)
- **Generated**: `.AddScoped<MyService>()`

### Pattern 2: Class-level, interface registration
```csharp
[Register(typeof(IMyService), ServiceLifetime.Scoped)]
public class MyService : IMyService { }
```
- **Applied to**: Class
- **Service type**: The specified interface (`IMyService`)
- **Implementation type**: The class itself (`MyService`)
- **Generated**: `.AddScoped<IMyService, MyService>()`

### Pattern 3: Assembly-level, self-registration (for third-party types)
```csharp
[assembly: Register(typeof(ThirdPartyClass), ServiceLifetime.Scoped)]
```
- **Applied to**: Assembly
- **Service type**: The specified type (`ThirdPartyClass`)
- **Implementation type**: The same type (`ThirdPartyClass`)
- **Generated**: `.AddScoped<ThirdPartyClass>()`

### Pattern 4: Assembly-level, interface registration (for third-party types)
```csharp
[assembly: Register(typeof(IThirdPartyService), typeof(ThirdPartyService), ServiceLifetime.Scoped)]
```
- **Applied to**: Assembly
- **Service type**: The specified interface (`IThirdPartyService`)
- **Implementation type**: The specified class (`ThirdPartyService`)
- **Generated**: `.AddScoped<IThirdPartyService, ThirdPartyService>()`

## Attribute Constructors

The `RegisterAttribute` should have the following constructors:

```csharp
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Assembly, AllowMultiple = true)]
public class RegisterAttribute : Attribute
{
    // Pattern 1: Class-level self-registration
    // [Register(ServiceLifetime.Scoped)]
    public RegisterAttribute(
        ServiceLifetime lifetime,
        string methodNameHint = default,
        EGeneratedAccessibility accessibility = EGeneratedAccessibility.Unspecified) { }

    // Pattern 2: Class-level interface registration
    // [Register(typeof(IMyService), ServiceLifetime.Scoped)]
    public RegisterAttribute(
        Type serviceType,
        ServiceLifetime lifetime,
        string methodNameHint = default,
        EGeneratedAccessibility accessibility = EGeneratedAccessibility.Unspecified) { }

    // Pattern 3: Assembly-level self-registration (NEW)
    // [assembly: Register(typeof(ThirdPartyClass), ServiceLifetime.Scoped)]
    // NOTE: This uses the same signature as Pattern 2, differentiated by context (assembly vs class)

    // Pattern 4: Assembly-level interface + implementation registration
    // [assembly: Register(typeof(IService), typeof(Implementation), ServiceLifetime.Scoped)]
    public RegisterAttribute(
        Type serviceType,
        Type implementationType,
        ServiceLifetime lifetime,
        string methodNameHint = default,
        EGeneratedAccessibility accessibility = EGeneratedAccessibility.Unspecified) { }
}
```

## Constructor Parameter Counts

| Pattern | Formal Params | First Param Type | Context |
|---------|---------------|------------------|---------|
| 1       | 3             | ServiceLifetime  | Class   |
| 2       | 4             | Type             | Class   |
| 3       | 4             | Type             | Assembly|
| 4       | 5             | Type             | Assembly|

## Diagnostic Errors

### REGISTER001: Invalid assembly-level [Register] usage
- **Condition**: `[Register]` applied to assembly with 3 formal parameters (Pattern 1 constructor)
- **Message**: "When applied to assembly, [Register] must specify at least the implementation type. Use [assembly: Register(typeof(Implementation), ServiceLifetime.Scoped)] or [assembly: Register(typeof(IService), typeof(Implementation), ServiceLifetime.Scoped)]"
- **Severity**: Error

### REGISTER002: Invalid class-level [Register] usage
- **Condition**: `[Register]` applied to class with 5 formal parameters (Pattern 4 constructor)
- **Message**: "When applied to a class, [Register] should not specify implementation type. Use [Register(ServiceLifetime.Scoped)] or [Register(typeof(IService), ServiceLifetime.Scoped)]"
- **Severity**: Error

## Implementation Changes Required

### 1. Update `RegisterAttribute.cs`
- Update XML documentation to reflect all 4 patterns
- No constructor changes needed (Pattern 3 reuses Pattern 2's constructor)

### 2. Update `ServiceRegistrationsGenerator.cs`

#### Class-level processing (existing):
- 3 formal params (ServiceLifetime first) → Pattern 1 ✓
- 4 formal params (Type first) → Pattern 2 ✓
- 5 formal params → Error REGISTER002 ✓

#### Assembly-level processing (needs update):
- 3 formal params → Error REGISTER001 (class-level constructor used on assembly)
- 4 formal params (Type first) → Pattern 3 (NEW - treat as self-registration)
- 5 formal params → Pattern 4 ✓

### 3. Update `ServiceRegistrationsGenerator.Rules.cs`
- Update REGISTER001 message to reflect valid assembly-level options

### 4. Update `AnalyzerReleases.Unshipped.md`
- Update diagnostic descriptions

## Test Cases

### Valid Cases (should compile without errors)
```csharp
// Pattern 1
[Register(ServiceLifetime.Scoped)]
public class Service1 { }

// Pattern 2
[Register(typeof(IService2), ServiceLifetime.Scoped)]
public class Service2 : IService2 { }

// Pattern 3
[assembly: Register(typeof(ThirdPartyClass), ServiceLifetime.Singleton)]

// Pattern 4
[assembly: Register(typeof(IThirdPartyService), typeof(ThirdPartyService), ServiceLifetime.Scoped)]
```

### Invalid Cases (should report errors)
```csharp
// REGISTER001: Using Pattern 1 constructor on assembly
[assembly: Register(ServiceLifetime.Scoped)]  // Error!

// REGISTER002: Using Pattern 4 constructor on class
[Register(typeof(IService), typeof(Service), ServiceLifetime.Scoped)]  // Error!
public class Service : IService { }
```

## Generated Code Examples

### Pattern 3 Generated Code
```csharp
// Input:
[assembly: Register(typeof(ThirdPartyClass), ServiceLifetime.Singleton)]

// Generated:
public static IServiceCollection AddMyAssembly(this IServiceCollection services)
{
    return services
        .AddSingleton<ThirdPartyClass>();
}
```

### Pattern 4 Generated Code
```csharp
// Input:
[assembly: Register(typeof(IThirdPartyService), typeof(ThirdPartyService), ServiceLifetime.Scoped)]

// Generated:
public static IServiceCollection AddMyAssembly(this IServiceCollection services)
{
    return services
        .AddScoped<IThirdPartyService, ThirdPartyService>();
}
```
