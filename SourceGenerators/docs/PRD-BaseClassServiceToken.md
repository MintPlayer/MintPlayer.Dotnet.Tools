# PRD: Support Base Class as Service Token in [Register] Attribute

## 1. Problem Statement

The `ServiceRegistrationsGenerator` currently only supports **interfaces** as custom service tokens (injection tokens) in `[Register(typeof(T), ...)]`. When a user specifies a **base class** as the service type, the registration is **silently ignored** because the validation logic searches only `AllInterfaces` and never checks the base type hierarchy.

### Current Behavior

Given this code:
```csharp
[Register(typeof(WebhookEventProcessor), ServiceLifetime.Scoped)]
internal class MyWebhookEventProcessor : WebhookEventProcessor { }
```

**Result**: No extension method is generated. The registration is silently dropped.

### Root Cause

In `ServiceRegistrationsGenerator.cs`, the non-generic validation (line 124) checks:
```csharp
if (namedTypeSymbol.AllInterfaces.All(i => !SymbolEqualityComparer.Default.Equals(i, interfaceTypeSymbol)))
    return default;
```

`AllInterfaces` only contains interfaces the class implements. A base class like `WebhookEventProcessor` will never appear in this collection — it exists in the `BaseType` chain instead.

The same limitation exists in the generic path (line 97), which also searches only `AllInterfaces`:
```csharp
var matchingInterface = namedTypeSymbol.AllInterfaces
    .FirstOrDefault(i => SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, interfaceTypeSymbol.OriginalDefinition));
```

### Desired Behavior

The generator should produce:
```csharp
public static IServiceCollection AddMyAssembly(this IServiceCollection services)
{
    return services
        .AddScoped<global::WebhookEventProcessor, global::MyWebhookEventProcessor>();
}
```

This is fully supported by `Microsoft.Extensions.DependencyInjection` — registering a base class as the service type and a derived class as the implementation is a standard pattern.

---

## 2. Use Cases

### 2.1 Non-Generic Base Class Registration

The most common case — registering a class against its base class (e.g., overriding a framework-provided default implementation).

```csharp
[Register(typeof(WebhookEventProcessor), ServiceLifetime.Scoped)]
internal class MyWebhookEventProcessor : WebhookEventProcessor
{
    // Custom webhook processing logic
}
```

Generated:
```csharp
.AddScoped<global::Octokit.Webhooks.WebhookEventProcessor, global::MyApp.MyWebhookEventProcessor>()
```

### 2.2 Abstract Base Class Registration

Registering against an abstract base class.

```csharp
public abstract class BaseNotificationService
{
    public abstract Task SendAsync(string message);
}

[Register(typeof(BaseNotificationService), ServiceLifetime.Singleton)]
internal class EmailNotificationService : BaseNotificationService
{
    public override Task SendAsync(string message) => /* ... */;
}
```

Generated:
```csharp
.AddSingleton<global::MyApp.BaseNotificationService, global::MyApp.EmailNotificationService>()
```

### 2.3 Deep Inheritance Chain

The specified base class may be an ancestor several levels up, not just the direct parent.

```csharp
public class GrandparentService { }
public class ParentService : GrandparentService { }

[Register(typeof(GrandparentService), ServiceLifetime.Scoped)]
internal class ChildService : ParentService { }
```

Generated:
```csharp
.AddScoped<global::MyApp.GrandparentService, global::MyApp.ChildService>()
```

### 2.4 Multiple Registrations (Interface + Base Class)

A class registered against both an interface and a base class.

```csharp
public interface IProcessor { }
public class BaseProcessor : IProcessor { }

[Register(typeof(IProcessor), ServiceLifetime.Scoped)]
[Register(typeof(BaseProcessor), ServiceLifetime.Scoped)]
internal class MyProcessor : BaseProcessor { }
```

Generated:
```csharp
.AddScoped<global::MyApp.IProcessor, global::MyApp.MyProcessor>()
.AddScoped<global::MyApp.BaseProcessor, global::MyApp.MyProcessor>()
```

### 2.5 Generic Base Class Registration

Registering against an open generic base class.

```csharp
public class BaseRepository<TEntity> where TEntity : class
{
    // Base implementation
}

[Register(typeof(BaseRepository<>), ServiceLifetime.Scoped, "Repositories")]
internal class ExtendedRepository<TEntity> : BaseRepository<TEntity> where TEntity : class
{
    // Extended implementation
}
```

Generated:
```csharp
public static IServiceCollection AddRepositories<TEntity>(this IServiceCollection services)
    where TEntity : class
{
    return services
        .AddScoped<global::MyApp.BaseRepository<TEntity>, global::MyApp.ExtendedRepository<TEntity>>();
}
```

### 2.6 Factory Method with Base Class Token

A class with a `[RegisterFactory]` method that returns the base class type.

```csharp
[Register(typeof(WebhookEventProcessor), ServiceLifetime.Singleton)]
internal class MyWebhookEventProcessor : WebhookEventProcessor
{
    [RegisterFactory]
    public static WebhookEventProcessor Create(IServiceProvider sp)
        => new MyWebhookEventProcessor(sp.GetRequiredService<ILogger>());
}
```

Generated:
```csharp
.AddSingleton<global::Octokit.Webhooks.WebhookEventProcessor>(MyWebhookEventProcessor.Create)
```

---

## 3. Technical Design

### 3.1 Non-Generic Base Class Validation

**File**: `ServiceRegistrationsGenerator.cs`, class-level processing, non-generic branch (~line 121-137)

Replace the interface-only check with a combined interface + base type check:

```csharp
// Current (interface only):
if (namedTypeSymbol.AllInterfaces.All(i => !SymbolEqualityComparer.Default.Equals(i, interfaceTypeSymbol)))
    return default;

// Proposed (interface OR base class):
bool implementsInterface = namedTypeSymbol.AllInterfaces.Any(i =>
    SymbolEqualityComparer.Default.Equals(i, interfaceTypeSymbol));
bool extendsBaseClass = IsOrExtendsType(namedTypeSymbol, interfaceTypeSymbol);

if (!implementsInterface && !extendsBaseClass)
    return default;
```

The helper `IsOrExtendsType` walks the `BaseType` chain:

```csharp
private static bool IsOrExtendsType(INamedTypeSymbol derived, INamedTypeSymbol candidate)
{
    var current = derived.BaseType;
    while (current is not null)
    {
        if (SymbolEqualityComparer.Default.Equals(current, candidate))
            return true;
        current = current.BaseType;
    }
    return false;
}
```

### 3.2 Generic Base Class Matching

**File**: `ServiceRegistrationsGenerator.cs`, class-level processing, generic branch (~line 94-120)

Extend the open generic matching to also search the base type chain:

```csharp
// Current (interfaces only):
var matchingInterface = namedTypeSymbol.AllInterfaces
    .FirstOrDefault(i => SymbolEqualityComparer.Default.Equals(
        i.OriginalDefinition, interfaceTypeSymbol.OriginalDefinition));

// Proposed (interfaces + base type chain):
INamedTypeSymbol? matchingType = namedTypeSymbol.AllInterfaces
    .FirstOrDefault(i => SymbolEqualityComparer.Default.Equals(
        i.OriginalDefinition, interfaceTypeSymbol.OriginalDefinition));

if (matchingType is null)
{
    // Search the base type chain for a matching generic base class
    var current = namedTypeSymbol.BaseType;
    while (current is not null)
    {
        if (current.IsGenericType && SymbolEqualityComparer.Default.Equals(
            current.OriginalDefinition, interfaceTypeSymbol.OriginalDefinition))
        {
            matchingType = current;
            break;
        }
        current = current.BaseType;
    }
}

if (matchingType is null) return default;
```

The variable `matchingInterface` should be renamed to `matchingType` (or `matchingServiceType`) in this section since it may now refer to either an interface or a base class.

### 3.3 Factory Method Matching for Base Class Tokens

The existing factory matching logic (line 133) compares factory return types against the `interfaceTypeSymbol`:

```csharp
FactoryNames = factories
    .Where(f => SymbolEqualityComparer.Default.Equals(f.ReturnType, interfaceTypeSymbol))
    .Select(f => f.Name).ToArray(),
```

This already works correctly for base class tokens — a factory method returning `WebhookEventProcessor` will match `typeof(WebhookEventProcessor)`. No change needed here.

### 3.4 No Model Changes Required

The `ServiceRegistration` model already stores `ServiceTypeName` and `ImplementationTypeName` as fully qualified strings. Whether the service type is an interface or a base class is transparent — the generated DI call is identical:

```csharp
.AddScoped<ServiceType, ImplementationType>()
```

No changes needed to `ServiceRegistration.cs`, `GenericTypeInfo.cs`, or `ServiceRegistrationsGenerator.Producer.cs`.

### 3.5 Attribute Naming Consideration

The second constructor parameter is currently named `interfaceType`:

```csharp
public RegisterAttribute(Type interfaceType, ServiceLifetime lifetime, ...) { }
```

For semantic accuracy, this should be renamed to `serviceType` to reflect that it now accepts both interfaces and base classes. This is a **source-compatible** change (callers use positional arguments, not named arguments), but it is a **binary-compatible** change as well since attribute constructor parameter names are not part of the binary contract.

The XML doc comment should also be updated:
```csharp
/// <summary>
/// Registers a class as an implementation of a service type (interface or base class).
/// Use on class declarations.
/// </summary>
public RegisterAttribute(Type serviceType, ServiceLifetime lifetime, ...) { }
```

---

## 4. Affected Files

| File | Change |
|------|--------|
| `ServiceRegistrationsGenerator.cs` | Add base type chain lookup for both non-generic and generic paths |
| `RegisterAttribute.cs` | Rename `interfaceType` → `serviceType`; update XML docs |
| `ServiceRegistrationsGenerator.Producer.cs` | No changes needed |
| `ServiceRegistration.cs` | No changes needed |
| `GenericTypeInfo.cs` | No changes needed |
| `ServiceRegistrationsGenerator.Rules.cs` | No changes needed |
| `PRD-RegisterAttributeUsage.md` | Update Pattern 2 description to mention base classes |

---

## 5. Diagnostic Rules

### No New Diagnostics Needed

The existing silent-ignore behavior (returning `default`) will remain for the case where the specified type is **neither** an implemented interface **nor** an ancestor base class. This is consistent with how the generator handles other invalid configurations.

### Updated Error Messages

No existing diagnostic messages reference "interface" specifically in a way that would exclude base classes.

---

## 6. Edge Cases

| Scenario | Behavior |
|----------|----------|
| Service type is `object` | Technically valid (all classes extend `object`), generates `.AddScoped<object, MyClass>()`. Unusual but not an error. |
| Service type is the class itself | Already handled by the base type walk — `BaseType` won't match self. Falls through to existing self-registration via Pattern 1 or returns `default`. This is correct; users should use Pattern 1 for self-registration. |
| Service type is a sealed class | Would require the decorated class to extend it, which the compiler already prevents. No generator-level handling needed. |
| Service type is a struct/value type | `BaseType` chain won't match. Returns `default` (silent ignore). Correct behavior — DI doesn't support this. |
| Service type is an interface the class doesn't implement | Handled by existing `AllInterfaces` check. Returns `default`. |
| Service type is a base class the class doesn't extend | Handled by new `IsOrExtendsType` check. Returns `default`. |
| Generic base class with partially closed type parameters | E.g., `typeof(BaseRepo<>)` on `class MyRepo : BaseRepo<string>` — the class is non-generic but extends a constructed generic. Should return `default` for the open generic path (class must be generic too), and is not supported in the non-generic path since the attribute passes the unbound generic. This is the same constraint as with interfaces. |

---

## 7. Implementation Plan

### Phase 1: Non-Generic Base Class Support
1. Add `IsOrExtendsType` helper method to `ServiceRegistrationsGenerator`
2. Update the non-generic validation to check both interfaces and base types
3. Rename `interfaceType` → `serviceType` in `RegisterAttribute.cs`
4. Add test cases for non-generic base class registration

### Phase 2: Generic Base Class Support
1. Extend the open generic matching to search the `BaseType` chain
2. Rename local variable `matchingInterface` → `matchingType`
3. Add test cases for generic base class registration

### Phase 3: Documentation & Cleanup
1. Update `PRD-RegisterAttributeUsage.md` to document base class support
2. Update README examples
3. Verify all existing tests still pass

---

## 8. Test Cases

### Valid Cases (should generate registrations)

```csharp
// Non-generic base class
[Register(typeof(WebhookEventProcessor), ServiceLifetime.Scoped)]
internal class MyWebhookEventProcessor : WebhookEventProcessor { }

// Abstract base class
[Register(typeof(BaseNotificationService), ServiceLifetime.Singleton)]
internal class EmailNotificationService : BaseNotificationService { }

// Deep inheritance
[Register(typeof(GrandparentService), ServiceLifetime.Scoped)]
internal class ChildService : ParentService { }  // ParentService : GrandparentService

// Multiple registrations: interface + base class
[Register(typeof(IProcessor), ServiceLifetime.Scoped)]
[Register(typeof(BaseProcessor), ServiceLifetime.Scoped)]
internal class MyProcessor : BaseProcessor { }  // BaseProcessor : IProcessor

// Generic base class
[Register(typeof(BaseRepository<>), ServiceLifetime.Scoped, "Repositories")]
internal class ExtendedRepository<TEntity> : BaseRepository<TEntity> where TEntity : class { }

// Factory method with base class token
[Register(typeof(WebhookEventProcessor), ServiceLifetime.Singleton)]
internal class MyWebhookEventProcessor : WebhookEventProcessor
{
    [RegisterFactory]
    public static WebhookEventProcessor Create(IServiceProvider sp)
        => new MyWebhookEventProcessor();
}
```

### Invalid Cases (should silently ignore / return default)

```csharp
// Class does not extend the specified base class
[Register(typeof(UnrelatedClass), ServiceLifetime.Scoped)]
internal class MyService { }  // No relationship to UnrelatedClass

// Struct as service type
[Register(typeof(MyStruct), ServiceLifetime.Scoped)]
internal class MyService { }
```

### Backward Compatibility Cases (must continue to work)

```csharp
// Pattern 1: Self-registration (unchanged)
[Register(ServiceLifetime.Scoped)]
public class MyService { }

// Pattern 2: Interface registration (unchanged)
[Register(typeof(IMyService), ServiceLifetime.Scoped)]
public class MyService : IMyService { }

// Generic interface registration (unchanged)
[Register(typeof(IGenericRepository<,>), ServiceLifetime.Scoped, "GenericRepository")]
public class GenericRepository<TEntity, TKey> : IGenericRepository<TEntity, TKey>
    where TEntity : EntityBase<TKey>
    where TKey : IEquatable<TKey> { }
```

---

## 9. Success Criteria

### Core Functionality
- [ ] `[Register(typeof(BaseClass), ...)]` generates correct `.Add<BaseClass, DerivedClass>()` registration
- [ ] Abstract base classes work as service tokens
- [ ] Deep inheritance chains are traversed correctly
- [ ] Generic base classes work with open generic registration (`typeof(BaseClass<>)`)
- [ ] Factory methods returning base class types are correctly matched

### Backward Compatibility
- [ ] All existing interface-based registrations continue to work unchanged
- [ ] All existing generic interface registrations continue to work unchanged
- [ ] All existing self-registrations (Pattern 1) continue to work unchanged
- [ ] All existing assembly-level registrations (Patterns 3 & 4) continue to work unchanged
- [ ] No new diagnostics are emitted for previously valid code

### Code Quality
- [ ] `interfaceType` parameter renamed to `serviceType` in attribute
- [ ] Local variables renamed from `interfaceTypeSymbol`/`matchingInterface` to more accurate names
- [ ] XML documentation updated to reflect interface-or-base-class semantics
