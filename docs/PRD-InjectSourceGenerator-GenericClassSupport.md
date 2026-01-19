# PRD: Generic Class Support for Inject Source Generator

## Overview

The Inject source generator currently generates constructors for partial classes with `[Inject]` fields. However, it fails to handle generic classes because the generated partial class declaration is missing the generic type parameters and where constraints. This feature adds support for generating proper partial class declarations with full generic type signatures.

## Problem Statement

When using the `[Inject]` attribute on a generic class like:

```csharp
internal partial class MustChangePasswordService<TUser, TKey> : IMustChangePasswordService<TUser, TKey>
    where TUser : IdentityUser<TKey>
    where TKey : IEquatable<TKey>
{
    [Inject] private readonly UserManager<TUser> userManager;
    [Inject] private readonly IHttpContextAccessor httpContextAccessor;
}
```

The current generator produces:

```csharp
partial class MustChangePasswordService  // Missing <TUser, TKey>
{
    public MustChangePasswordService(global::Microsoft.AspNetCore.Identity.UserManager<TUser> userManager, ...)
    // ...
}
```

This causes a compilation error because:
1. The partial class declaration is missing the generic type parameters `<TUser, TKey>`
2. The where constraints are missing
3. The constructor references `TUser` which is undefined without the type parameters

## Goals

1. Generate partial class declarations with full generic type parameters
2. Include all where constraints on the generated partial class
3. Support nested generic classes (parent types may also be generic)
4. Maintain backward compatibility with non-generic classes

## Non-Goals

1. Changing how type parameters are used in constructor parameters (already works correctly)
2. Supporting variance (`in`/`out`) on type parameters (not applicable to classes)
3. Generic method support (separate feature)

---

## Requirements

### R1: Extract Generic Type Parameters

The source generator must extract the generic type parameters from the class declaration, including:
- Type parameter names (e.g., `TUser`, `TKey`)
- Type parameter order

**Example Input:**
```csharp
public partial class GenericRepository<TEntity, TKey>
```

**Extracted:**
- Type parameters: `["TEntity", "TKey"]`

### R2: Extract Type Constraints

The source generator must extract all type constraints for each type parameter:

| Constraint Type | Example | Generated |
|-----------------|---------|-----------|
| Base class | `where T : BaseClass` | `where T : BaseClass` |
| Interface | `where T : IDisposable` | `where T : IDisposable` |
| `class` | `where T : class` | `where T : class` |
| `struct` | `where T : struct` | `where T : struct` |
| `notnull` | `where T : notnull` | `where T : notnull` |
| `unmanaged` | `where T : unmanaged` | `where T : unmanaged` |
| `new()` | `where T : new()` | `where T : new()` |
| Multiple | `where T : class, IDisposable, new()` | `where T : class, IDisposable, new()` |

### R3: Generate Partial Class with Type Parameters

The generated partial class must include the full generic signature:

**Source:**
```csharp
internal partial class MustChangePasswordService<TUser, TKey>
    where TUser : IdentityUser<TKey>
    where TKey : IEquatable<TKey>
{
    [Inject] private readonly UserManager<TUser> userManager;
}
```

**Generated:**
```csharp
partial class MustChangePasswordService<TUser, TKey>
    where TUser : global::Microsoft.AspNetCore.Identity.IdentityUser<TKey>
    where TKey : global::System.IEquatable<TKey>
{
    public MustChangePasswordService(global::Microsoft.AspNetCore.Identity.UserManager<TUser> userManager)
    {
        this.userManager = userManager;
    }
}
```

### R4: Support Nested Generic Classes

The PathSpec system must also include generic type parameters for parent types:

**Source:**
```csharp
public partial class OuterClass<TOuter>
{
    public partial class InnerClass<TInner>
    {
        [Inject] private readonly IService<TOuter, TInner> service;
    }
}
```

**Generated:**
```csharp
partial class OuterClass<TOuter>
{
    partial class InnerClass<TInner>
    {
        public InnerClass(global::IService<TOuter, TInner> service)
        {
            this.service = service;
        }
    }
}
```

### R5: Fully Qualified Constraint Types

Type constraints should use fully qualified type names to avoid namespace conflicts:

- `IdentityUser<TKey>` → `global::Microsoft.AspNetCore.Identity.IdentityUser<TKey>`
- `IEquatable<TKey>` → `global::System.IEquatable<TKey>`

---

## Technical Design

### Model Changes

#### ClassWithBaseDependenciesAndInjectFields

Add properties for generic type information:

```csharp
[AutoValueComparer]
public partial class ClassWithBaseDependenciesAndInjectFields
{
    // Existing properties...
    public string FileName { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    // ...

    // NEW: Generic type parameters
    public string? GenericTypeParameters { get; set; }  // e.g., "<TUser, TKey>"
    public string? GenericConstraints { get; set; }     // e.g., "where TUser : ... where TKey : ..."
}
```

#### PathSpecElement

Add generic type information for nested classes:

```csharp
public class PathSpecElement
{
    public string? Name { get; set; }
    public EPathSpecType Type { get; set; }
    public bool IsPartial { get; set; }

    // NEW: Generic type parameters for parent types
    public string? GenericTypeParameters { get; set; }  // e.g., "<TOuter>"
    public string? GenericConstraints { get; set; }
}
```

### Generator Changes

#### InjectSourceGenerator.cs

Extract generic information from the class declaration:

```csharp
// After line 40: var className = classDeclaration.Identifier.Text;
var (genericParams, genericConstraints) = GetGenericTypeInfo(classDeclaration, context2.SemanticModel);

// In the return statement:
return new Models.ClassWithBaseDependenciesAndInjectFields
{
    FileName = classDeclaration.Identifier.Text,
    ClassName = className,
    GenericTypeParameters = genericParams,      // NEW
    GenericConstraints = genericConstraints,    // NEW
    // ...
};
```

New helper method:

```csharp
private static (string? TypeParameters, string? Constraints) GetGenericTypeInfo(
    ClassDeclarationSyntax classDeclaration,
    SemanticModel semanticModel)
{
    if (classDeclaration.TypeParameterList == null)
        return (null, null);

    var typeParams = classDeclaration.TypeParameterList.ToString();

    var constraints = classDeclaration.ConstraintClauses
        .Select(c => GetFullyQualifiedConstraint(c, semanticModel))
        .ToList();

    var constraintStr = constraints.Count > 0
        ? string.Join(" ", constraints)
        : null;

    return (typeParams, constraintStr);
}
```

#### InjectProducer.cs

Update the class declaration generation:

```csharp
// Line 46: Change from
using (writer.OpenBlock($"partial class {classInfo.ClassName}"))

// To:
var classDeclaration = $"partial class {classInfo.ClassName}{classInfo.GenericTypeParameters ?? string.Empty}";
if (!string.IsNullOrEmpty(classInfo.GenericConstraints))
{
    writer.WriteLine(classDeclaration);
    writer.IndentSingleLine(classInfo.GenericConstraints);
    using (writer.OpenBlock(string.Empty))
    {
        // ... constructor code ...
    }
}
else
{
    using (writer.OpenBlock(classDeclaration))
    {
        // ... constructor code ...
    }
}
```

#### SymbolExtensions.cs (PathSpec)

Update `GetPathSpec` to include generic information for parent types:

```csharp
// In the parents.Add call:
parents.Add(new PathSpecElement
{
    Name = namedTypeSymbol.Name,
    GenericTypeParameters = GetGenericTypeParameters(namedTypeSymbol),  // NEW
    GenericConstraints = GetGenericConstraints(namedTypeSymbol),        // NEW
    IsPartial = ...,
    Type = ...
});
```

Update `OpenPathSpec` to include generic parameters:

```csharp
// Change from:
stack.Push(writer.OpenBlock($"partial {keyword} {parent.Name}"));

// To:
var declaration = $"partial {keyword} {parent.Name}{parent.GenericTypeParameters ?? string.Empty}";
// Handle constraints similarly to main class
```

---

## Test Scenarios

### Valid Usage

| Scenario | Description |
|----------|-------------|
| T1 | Simple generic class with one type parameter |
| T2 | Generic class with multiple type parameters |
| T3 | Generic class with base class constraint |
| T4 | Generic class with interface constraint |
| T5 | Generic class with `class`/`struct`/`new()` constraints |
| T6 | Generic class with multiple constraints on same parameter |
| T7 | Generic class with constraints referencing other type parameters |
| T8 | Nested generic classes (parent and child both generic) |
| T9 | Generic class inheriting from generic base with `[Inject]` |
| T10 | Generic class with PostConstruct method |

### Example Test Cases

```csharp
// T1: Simple generic class
public partial class Repository<TEntity>
{
    [Inject] private readonly IDbContext<TEntity> context;
}

// T2: Multiple type parameters
public partial class KeyedRepository<TEntity, TKey>
{
    [Inject] private readonly IStore<TEntity, TKey> store;
}

// T5: Multiple constraint types
public partial class GenericService<T>
    where T : class, IEntity, new()
{
    [Inject] private readonly IFactory<T> factory;
}

// T7: Constraints referencing other type parameters
public partial class PasswordService<TUser, TKey>
    where TUser : IdentityUser<TKey>
    where TKey : IEquatable<TKey>
{
    [Inject] private readonly UserManager<TUser> userManager;
}

// T8: Nested generic classes
public partial class OuterService<TOuter>
{
    public partial class InnerService<TInner>
    {
        [Inject] private readonly ICombined<TOuter, TInner> combined;
    }
}
```

---

## Migration / Backward Compatibility

- **Fully backward compatible:** Non-generic classes generate identical output
- **No breaking changes:** Existing code continues to work
- **Automatic enhancement:** Generic classes that previously failed will now work

---

## Acceptance Criteria

1. [ ] Generic type parameters extracted from class declarations
2. [ ] Type constraints extracted and fully qualified
3. [ ] Generated partial class includes `<T1, T2, ...>` after class name
4. [ ] Generated partial class includes `where` clauses
5. [ ] Nested generic parent classes include their type parameters
6. [ ] Constructor parameter types using type parameters work correctly
7. [ ] All test scenarios pass
8. [ ] Existing non-generic tests continue to pass

---

## Version

This feature will be included in version **10.13.0** of `MintPlayer.SourceGenerators`.
