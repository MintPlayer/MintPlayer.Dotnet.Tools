# PRD: Generic Type Support for Service Registration Source Generator

## 1. Problem Statement

The current `ServiceRegistrationsGenerator` does not support registering generic services with type constraints. When users mark a generic class with `[Register(typeof(IGenericRepository<,>), ServiceLifetime.Scoped)]`, the generator silently ignores it because:

1. The unbound generic type `typeof(IGenericRepository<,>)` doesn't match the constructed generic interface `IGenericRepository<TEntity, TKey>` implemented by the class
2. There's no logic to extract type parameters or constraints from the implementation class
3. The generated extension method signature doesn't support generic type parameters

### Current Behavior

Given this code:
```csharp
public interface IGenericRepository<TEntity, TKey>
    where TEntity : EntityBase<TKey>
    where TKey : IEquatable<TKey>
{
    TEntity? GetById(TKey id);
}

[Register(typeof(IGenericRepository<,>), ServiceLifetime.Scoped, "GenericRepository")]
public class GenericRepository<TEntity, TKey> : IGenericRepository<TEntity, TKey>
    where TEntity : EntityBase<TKey>
    where TKey : IEquatable<TKey>
{
    public TEntity? GetById(TKey id) => throw new NotImplementedException();
}
```

**Result**: No extension method is generated for this service (silently ignored).

### Desired Behavior

The generator should produce:
```csharp
public static IServiceCollection AddGenericRepository<TEntity, TKey>(this IServiceCollection services)
    where TEntity : EntityBase<TKey>
    where TKey : IEquatable<TKey>
{
    return services.AddScoped<IGenericRepository<TEntity, TKey>, GenericRepository<TEntity, TKey>>();
}
```

Usage:
```csharp
services.AddGenericRepository<UserEntity, Guid>();
services.AddGenericRepository<ProductEntity, int>();
```

---

## 2. Use Cases

### 2.1 Single Generic Service
The most common case - a single generic service/implementation pair.

```csharp
[Register(typeof(IRepository<>), ServiceLifetime.Scoped, "Repositories")]
public class Repository<T> : IRepository<T> where T : class
```

Generated:
```csharp
public static IServiceCollection AddRepositories<T>(this IServiceCollection services)
    where T : class
{
    return services.AddScoped<IRepository<T>, Repository<T>>();
}
```

### 2.2 Multiple Type Parameters with Constraints
Services with multiple related type parameters.

```csharp
[Register(typeof(IGenericRepository<,>), ServiceLifetime.Scoped, "GenericRepository")]
public class GenericRepository<TEntity, TKey> : IGenericRepository<TEntity, TKey>
    where TEntity : EntityBase<TKey>
    where TKey : IEquatable<TKey>
```

Generated:
```csharp
public static IServiceCollection AddGenericRepository<TEntity, TKey>(this IServiceCollection services)
    where TEntity : EntityBase<TKey>
    where TKey : IEquatable<TKey>
{
    return services.AddScoped<IGenericRepository<TEntity, TKey>, GenericRepository<TEntity, TKey>>();
}
```

### 2.3 Multiple Generic Services with Same MethodNameHint (COMPLEX CASE)

When multiple generic services share the same `methodNameHint`, the generator must determine if they can be combined. This depends on **C# method overloading rules**:

**C# Method Overloading Facts:**
- ✅ Methods CAN be overloaded by **different number of type parameters** (arity)
- ❌ Methods CANNOT be overloaded by **different constraints alone** (same arity)

This gives us clear rules for what's possible.

---

**Scenario A: Compatible Type Parameters (CAN be combined)**
```csharp
[Register(typeof(IGenericRepository<,>), ServiceLifetime.Scoped, "GenericRepository")]
public class GenericRepository<TEntity, TKey> : IGenericRepository<TEntity, TKey>
    where TEntity : EntityBase<TKey>
    where TKey : IEquatable<TKey>

[Register(typeof(IGenericCache<,>), ServiceLifetime.Singleton, "GenericRepository")]
public class GenericCache<TEntity, TKey> : IGenericCache<TEntity, TKey>
    where TEntity : EntityBase<TKey>
    where TKey : IEquatable<TKey>
```

If both have identical type parameter count and compatible constraints, they CAN be combined:
```csharp
public static IServiceCollection AddGenericRepository<TEntity, TKey>(this IServiceCollection services)
    where TEntity : EntityBase<TKey>
    where TKey : IEquatable<TKey>
{
    return services
        .AddScoped<IGenericRepository<TEntity, TKey>, GenericRepository<TEntity, TKey>>()
        .AddSingleton<IGenericCache<TEntity, TKey>, GenericCache<TEntity, TKey>>();
}
```

---

**Scenario B: Different Arity (CAN be overloaded - valid C#)**
```csharp
[Register(typeof(IGenericRepository<,>), ServiceLifetime.Scoped, "GenericRepository")]
public class GenericRepository<TEntity, TKey> : IGenericRepository<TEntity, TKey>
    where TEntity : EntityBase<TKey>
    where TKey : IEquatable<TKey>

[Register(typeof(ISimpleService<>), ServiceLifetime.Transient, "GenericRepository")]
public class SimpleService<T> : ISimpleService<T>
    where T : class, new()
```

Different type parameter counts = valid method overloads:
```csharp
// Two type parameters
public static IServiceCollection AddGenericRepository<TEntity, TKey>(this IServiceCollection services)
    where TEntity : EntityBase<TKey>
    where TKey : IEquatable<TKey>
{
    return services.AddScoped<IGenericRepository<TEntity, TKey>, GenericRepository<TEntity, TKey>>();
}

// One type parameter - valid overload!
public static IServiceCollection AddGenericRepository<T>(this IServiceCollection services)
    where T : class, new()
{
    return services.AddTransient<ISimpleService<T>, SimpleService<T>>();
}
```

---

**Scenario C: Same Arity, Incompatible Constraints (CANNOT be same method)**
```csharp
[Register(typeof(IGenericRepository<,>), ServiceLifetime.Scoped, "GenericRepository")]
public class GenericRepository<TEntity, TKey> : IGenericRepository<TEntity, TKey>
    where TEntity : EntityBase<TKey>
    where TKey : IEquatable<TKey>

[Register(typeof(IKeyValueStore<,>), ServiceLifetime.Scoped, "GenericRepository")]
public class KeyValueStore<TKey, TValue> : IKeyValueStore<TKey, TValue>
    where TKey : notnull
    where TValue : class
```

Both have 2 type parameters but different constraints. C# does NOT allow this as overloads.

**Solution**: Generate with type name suffix:
```csharp
// First group with compatible constraints
public static IServiceCollection AddGenericRepository<TEntity, TKey>(this IServiceCollection services)
    where TEntity : EntityBase<TKey>
    where TKey : IEquatable<TKey>
{
    return services.AddScoped<IGenericRepository<TEntity, TKey>, GenericRepository<TEntity, TKey>>();
}

// Second group - must use suffix to avoid conflict
public static IServiceCollection AddGenericRepository_KeyValueStore<TKey, TValue>(this IServiceCollection services)
    where TKey : notnull
    where TValue : class
{
    return services.AddScoped<IKeyValueStore<TKey, TValue>, KeyValueStore<TKey, TValue>>();
}
```

### 2.4 Mixed Generic and Non-Generic Services with Same MethodNameHint

```csharp
[Register(typeof(ISimpleService), ServiceLifetime.Scoped, "MyServices")]
public class SimpleService : ISimpleService { }

[Register(typeof(IGenericService<>), ServiceLifetime.Scoped, "MyServices")]
public class GenericService<T> : IGenericService<T> where T : class { }
```

**Proposed Solution**: Generate TWO separate methods:
```csharp
// Non-generic method for non-generic services
public static IServiceCollection AddMyServices(this IServiceCollection services)
{
    return services.AddScoped<ISimpleService, SimpleService>();
}

// Generic method for generic services
public static IServiceCollection AddMyServices<T>(this IServiceCollection services)
    where T : class
{
    return services.AddScoped<IGenericService<T>, GenericService<T>>();
}
```

C# method overloading allows this because the signatures are different (one is generic, one isn't).

---

## 3. Technical Design

### 3.1 Detection of Open Generic Types

Modify `ServiceRegistrationsGenerator.cs` to detect when the interface type argument is an unbound generic type:

```csharp
// In the attribute parsing section
if (args.ElementAtOrDefault(0).Value is INamedTypeSymbol interfaceTypeSymbol)
{
    // Check if this is an unbound generic type (e.g., IGenericRepository<,>)
    bool isOpenGeneric = interfaceTypeSymbol.IsUnboundGenericType;

    if (isOpenGeneric)
    {
        // Find the matching constructed generic interface from class's implemented interfaces
        var matchingInterface = namedTypeSymbol.AllInterfaces
            .FirstOrDefault(i => SymbolEqualityComparer.Default.Equals(
                i.OriginalDefinition,
                interfaceTypeSymbol.OriginalDefinition));

        if (matchingInterface != null)
        {
            // Extract type parameters and constraints from the class
            var typeParameters = namedTypeSymbol.TypeParameters;
            // ... process generic registration
        }
    }
}
```

### 3.2 Type Constraint Extraction

Create a helper to extract type constraints in the correct format:

```csharp
public static string FormatTypeConstraints(ImmutableArray<ITypeParameterSymbol> typeParameters)
{
    var constraints = new List<string>();

    foreach (var tp in typeParameters)
    {
        var constraintParts = new List<string>();

        // class/struct/unmanaged/notnull constraints (must come first)
        if (tp.HasReferenceTypeConstraint)
            constraintParts.Add("class");
        if (tp.HasValueTypeConstraint)
            constraintParts.Add("struct");
        if (tp.HasUnmanagedTypeConstraint)
            constraintParts.Add("unmanaged");
        if (tp.HasNotNullConstraint)
            constraintParts.Add("notnull");

        // Type constraints (base class, interfaces)
        foreach (var constraintType in tp.ConstraintTypes)
        {
            constraintParts.Add(constraintType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        }

        // new() constraint (must come last)
        if (tp.HasConstructorConstraint)
            constraintParts.Add("new()");

        if (constraintParts.Count > 0)
        {
            constraints.Add($"where {tp.Name} : {string.Join(", ", constraintParts)}");
        }
    }

    return string.Join("\n    ", constraints);
}
```

### 3.3 Model Changes

Extend `ServiceRegistration.cs`:

```csharp
[AutoValueComparer]
public partial class ServiceRegistration
{
    public string? ServiceTypeName { get; set; }
    public string? ImplementationTypeName { get; set; }
    public ServiceLifetime Lifetime { get; set; }
    public string? MethodNameHint { get; set; } = string.Empty;
    public string[] FactoryNames { get; set; } = [];
    public Attributes.EGeneratedAccessibility Accessibility { get; set; }

    // NEW: Generic type support
    public bool IsGeneric { get; set; }
    public GenericTypeInfo? GenericInfo { get; set; }
}

[AutoValueComparer]
public partial class GenericTypeInfo
{
    /// <summary>Type parameter names (e.g., ["TEntity", "TKey"])</summary>
    public string[] TypeParameterNames { get; set; } = [];

    /// <summary>Full constraint clauses (e.g., ["where TEntity : EntityBase<TKey>", "where TKey : IEquatable<TKey>"])</summary>
    public string[] ConstraintClauses { get; set; } = [];

    /// <summary>Service type with type parameters (e.g., "IGenericRepository<TEntity, TKey>")</summary>
    public string GenericServiceTypeName { get; set; } = string.Empty;

    /// <summary>Implementation type with type parameters (e.g., "GenericRepository<TEntity, TKey>")</summary>
    public string GenericImplementationTypeName { get; set; } = string.Empty;
}
```

### 3.4 Producer Changes

Modify `ServiceRegistrationsGenerator.Producer.cs` to handle generic registrations:

```csharp
// Group services by methodNameHint
foreach (var methodGroup in serviceRegistrations.GroupBy(sr => sr.MethodNameHint))
{
    var methodName = GetMethodName(methodGroup.Key);
    var genericServices = methodGroup.Where(s => s.IsGeneric).ToList();
    var nonGenericServices = methodGroup.Where(s => !s.IsGeneric).ToList();

    // 1. Generate non-generic method if any non-generic services exist
    if (nonGenericServices.Any())
    {
        GenerateNonGenericMethod(writer, methodName, nonGenericServices);
    }

    // 2. Group generic services by TYPE PARAMETER COUNT (arity)
    var byArity = genericServices
        .GroupBy(s => s.GenericInfo!.TypeParameterNames.Length)
        .OrderBy(g => g.Key);

    foreach (var arityGroup in byArity)
    {
        // 3. Within same arity, group by CONSTRAINT SIGNATURE
        var byConstraints = arityGroup
            .GroupBy(s => new ConstraintSignature(s.GenericInfo!.ConstraintClauses))
            .ToList();

        if (byConstraints.Count == 1)
        {
            // All services at this arity have compatible constraints
            // Generate single method (valid overload by arity)
            GenerateGenericMethod(writer, methodName, byConstraints[0].ToList());
        }
        else
        {
            // Multiple incompatible constraint groups at same arity
            // First group gets the clean name, rest get suffixes
            bool isFirst = true;
            foreach (var constraintGroup in byConstraints)
            {
                if (isFirst)
                {
                    GenerateGenericMethod(writer, methodName, constraintGroup.ToList());
                    isFirst = false;
                }
                else
                {
                    // Use implementation type name as suffix
                    var suffix = GetImplementationTypeSuffix(constraintGroup.First());
                    GenerateGenericMethod(writer, $"{methodName}_{suffix}", constraintGroup.ToList());
                }
            }
        }
    }
}
```

**Algorithm Summary:**

```
For each methodNameHint group:
├─ Generate non-generic method (if any non-generic services)
└─ Group generic services by arity (type parameter count)
    └─ For each arity:
        └─ Group by constraint signature
            ├─ If 1 group: generate method with base name
            └─ If multiple groups:
                ├─ First group: base name
                └─ Other groups: base name + "_" + implementation type suffix
```

### 3.5 Generated Code Template

For generic services:

```csharp
public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection AddGenericRepository<TEntity, TKey>(this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)
    where TEntity : global::MintPlayer.SourceGenerators.Debug.GenericTests.EntityBase<TKey>
    where TKey : global::System.IEquatable<TKey>
{
    return services
        .AddScoped<global::MintPlayer.SourceGenerators.Debug.GenericTests.IGenericRepository<TEntity, TKey>, global::MintPlayer.SourceGenerators.Debug.GenericTests.GenericRepository<TEntity, TKey>>();
}
```

---

## 4. Diagnostic Rules

### 4.1 New Diagnostic: SRVC001 - Incompatible Generic Services

**ID**: SRVC001
**Severity**: Error
**Message**: "Generic services '{0}' and '{1}' have incompatible type parameters but share the same methodNameHint '{2}'. Use different methodNameHint values or ensure type parameters match."

### 4.2 New Diagnostic: SRVC002 - Open Generic Without Implementation

**ID**: SRVC002
**Severity**: Error
**Message**: "Class '{0}' is marked with [Register] for open generic type '{1}', but does not implement this interface."

---

## 5. Edge Cases

| Scenario | Current Behavior | Proposed Behavior |
|----------|------------------|-------------------|
| `typeof(IRepo<>)` on non-generic class | Silently ignored | Report SRVC002 error |
| `typeof(IRepo<>)` on generic class not implementing it | Silently ignored | Report SRVC002 error |
| Multiple generic services, same methodNameHint, same type params | N/A | Combine into single generic method |
| Multiple generic services, same methodNameHint, different type params | N/A | Report SRVC001 error OR generate separate methods |
| Mixed generic + non-generic, same methodNameHint | N/A | Generate overloaded methods |
| Factory method with generic service | Not handled | Support if factory returns constructed type |

---

## 6. Implementation Plan

### Phase 1: Core Generic Support
1. Modify `ServiceRegistrationsGenerator.cs` to detect open generic types
2. Extract type parameters and constraints from implementation class
3. Extend `ServiceRegistration` model with generic info
4. Update `RegistrationsProducer` to generate generic extension methods
5. Add test cases for single generic services

### Phase 2: Multiple Generic Services
1. Implement grouping logic for generic services with same methodNameHint
2. Detect compatible vs incompatible type parameter signatures
3. Generate combined methods for compatible services
4. Add diagnostic SRVC001 for incompatible services
5. Add test cases for multiple generic services

### Phase 3: Mixed Scenarios
1. Handle mixed generic/non-generic with same methodNameHint
2. Add diagnostic SRVC002 for invalid configurations
3. Comprehensive test coverage

### Phase 4: Factory Support (Optional)
1. Support factory methods for generic services
2. Handle constructed generic return types in factories

---

## 7. Open Questions

1. **Should we support partial type parameter matching?**
   - E.g., `IRepo<TEntity, int>` where only one type parameter is open
   - Recommendation: Phase 2 feature, not in initial implementation

2. **Should generic methods support factory methods?**
   - Factories typically return constructed types, not open generics
   - Recommendation: Support if factory return type matches service interface (Phase 2)

3. **Type parameter name normalization?**
   - If ServiceA uses `<T1, T2>` and ServiceB uses `<TKey, TValue>` but same constraints
   - Should we rename to common names or require exact match?
   - Recommendation: Require same constraint structure; type parameter names from first service

4. **Warning for suffixed methods?**
   - Should we emit a warning when we have to suffix a method name due to constraint conflicts?
   - Recommendation: Yes, emit informational diagnostic so users are aware

---

## 8. Success Criteria

### Core Functionality
- [ ] Single generic service generates correct extension method with constraints
- [ ] Multiple type parameters with interdependent constraints work correctly (e.g., `where TEntity : EntityBase<TKey>`)
- [ ] All constraint types supported: `class`, `struct`, `notnull`, `unmanaged`, `new()`, type constraints
- [ ] Fully qualified type names used throughout (`global::` prefix)

### Grouping & Overloading
- [ ] Compatible generic services (same arity + same constraints) combine into single method
- [ ] Different arity services generate valid method overloads with same name
- [ ] Incompatible constraints (same arity) generate separate methods with suffix
- [ ] Mixed generic/non-generic services generate overloaded methods
- [ ] Non-generic services remain in their own non-generic method

### Test Cases from GenericServicesTest.cs
- [ ] `AddGenericRepository<TEntity, TKey>()` - combines GenericRepository + GenericCache
- [ ] `AddGenericRepository<T>()` - overload for SimpleGenericService
- [ ] `AddGenericRepository_KeyValueStore<TKey, TValue>()` - suffixed due to constraint conflict
- [ ] `AddAuditServices<TAuditEntry>()` - separate methodNameHint

### Backward Compatibility
- [ ] All existing non-generic tests continue to pass
- [ ] Existing `[Register]` attributes without generic types work unchanged
