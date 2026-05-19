# PRD: PostConstructAttribute for Inject Source Generator

## Overview

The Inject source generator currently generates constructors that inject values for fields decorated with `[Inject]`. However, consumers cannot execute additional initialization logic after field assignments. This feature adds a `[PostConstruct]` attribute that allows marking a method to be called automatically at the end of the generated constructor.

## Problem Statement

When using the `[Inject]` attribute, the generated constructor only performs field assignments. Users who need to:
- Validate injected dependencies
- Initialize derived state from injected services
- Perform post-injection setup logic

...must currently create a separate initialization method and remember to call it manually, or avoid using the source generator entirely.

## Goals

1. Allow consumers to define a method that is automatically called after all field assignments in the generated constructor
2. Provide clear diagnostics when the attribute is misused
3. Maintain backward compatibility with existing code
4. Support inheritance scenarios (each class in the hierarchy can have its own PostConstruct method)

## Non-Goals

1. Supporting multiple PostConstruct methods per class
2. Supporting PostConstruct methods with parameters
3. Automatic exception handling in PostConstruct calls
4. Async PostConstruct support (may be considered in future)

---

## Requirements

### R1: PostConstructAttribute Definition

Create a new attribute `PostConstructAttribute` in the `MintPlayer.SourceGenerators.Attributes` package.

```csharp
namespace MintPlayer.SourceGenerators.Attributes;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public class PostConstructAttribute : Attribute
{
}
```

**Constraints:**
- Targets methods only (`AttributeTargets.Method`)
- Cannot be applied multiple times (`AllowMultiple = false`)
- Not inherited by derived classes (`Inherited = false`)

### R2: Method Validation Rules

The method decorated with `[PostConstruct]` must satisfy:

| Rule | Description | Diagnostic ID |
|------|-------------|---------------|
| R2.1 | Method must be parameterless | `INJECT001` |
| R2.2 | Only one `[PostConstruct]` method per class (not counting nested classes) | `INJECT002` |
| R2.3 | Method must be accessible (private, protected, internal, or public) | N/A (always valid) |
| R2.4 | Method cannot be static | `INJECT003` |
| R2.5 | Method must be in a class that uses `[Inject]` | `INJECT004` |

**Note:** Async methods (`Task`/`ValueTask` return types) are allowed but will be called without `await`. A warning may be considered for future versions.

### R3: Diagnostics

#### INJECT001: PostConstruct method must be parameterless

- **ID:** `INJECT001`
- **Severity:** Error
- **Category:** `MintPlayer.SourceGenerators`
- **Title:** PostConstruct method must be parameterless
- **Message Format:** `Method '{0}' marked with [PostConstruct] must be parameterless`

#### INJECT002: Only one PostConstruct method allowed per class

- **ID:** `INJECT002`
- **Severity:** Error
- **Category:** `MintPlayer.SourceGenerators`
- **Title:** Only one PostConstruct method allowed per class
- **Message Format:** `Class '{0}' has multiple methods marked with [PostConstruct]. Only one is allowed per class.`

#### INJECT003: PostConstruct method cannot be static

- **ID:** `INJECT003`
- **Severity:** Error
- **Category:** `MintPlayer.SourceGenerators`
- **Title:** PostConstruct method cannot be static
- **Message Format:** `Method '{0}' marked with [PostConstruct] cannot be static`

#### INJECT004: PostConstruct requires Inject attribute usage

- **ID:** `INJECT004`
- **Severity:** Warning
- **Category:** `MintPlayer.SourceGenerators`
- **Title:** PostConstruct method in class without injected members
- **Message Format:** `Method '{0}' is marked with [PostConstruct] but class '{1}' has no members marked with [Inject]`

### R4: Code Generation

#### Current Generated Code (without PostConstruct)

```csharp
namespace MyNamespace
{
    partial class MyService
    {
        public MyService(global::ILogger logger, global::IRepository repository)
        {
            this.logger = logger;
            this.repository = repository;
        }
    }
}
```

#### New Generated Code (with PostConstruct)

```csharp
namespace MyNamespace
{
    partial class MyService
    {
        public MyService(global::ILogger logger, global::IRepository repository)
        {
            this.logger = logger;
            this.repository = repository;
            OnPostConstruct();
        }
    }
}
```

### R5: Inheritance Behavior

Each class in an inheritance hierarchy can have its own `[PostConstruct]` method. The call order follows natural constructor execution order:

```csharp
public partial class BaseService
{
    [Inject] private readonly ILogger logger;

    [PostConstruct]
    private void InitializeBase() { /* called first */ }
}

public partial class DerivedService : BaseService
{
    [Inject] private readonly IRepository repository;

    [PostConstruct]
    private void InitializeDerived() { /* called second */ }
}
```

**Generated code:**

```csharp
partial class BaseService
{
    public BaseService(global::ILogger logger)
    {
        this.logger = logger;
        InitializeBase();
    }
}

partial class DerivedService : BaseService
{
    public DerivedService(global::ILogger logger, global::IRepository repository)
        : base(logger)
    {
        this.repository = repository;
        InitializeDerived();
    }
}
```

### R6: Nested Class Support

The `[PostConstruct]` validation must be scoped to the immediate containing class, not parent classes in a nested hierarchy:

```csharp
public partial class OuterClass
{
    [Inject] private readonly IServiceA serviceA;

    [PostConstruct]
    private void InitOuter() { }  // Valid - one per OuterClass

    public partial class InnerClass
    {
        [Inject] private readonly IServiceB serviceB;

        [PostConstruct]
        private void InitInner() { }  // Valid - one per InnerClass (separate scope)
    }
}
```

---

## Technical Design

### Model Changes

Extend `ClassWithBaseDependenciesAndInjectFields` model:

```csharp
[AutoValueComparer]
public partial class ClassWithBaseDependenciesAndInjectFields
{
    public string FileName { get; set; }
    public string ClassName { get; set; }
    public string? ClassNamespace { get; set; }
    public PathSpec? PathSpec { get; set; }
    public IList<InjectField> BaseDependencies { get; set; }
    public IList<InjectField> InjectFields { get; set; }
    public string? PostConstructMethodName { get; set; }  // NEW
}
```

### Generator Changes

1. **Syntax Predicate:** Update to also check for `[PostConstruct]` decorated methods
2. **Transform:** Extract PostConstruct method name when processing class
3. **Validation:** Report diagnostics for invalid PostConstruct usage
4. **Producer:** Append `PostConstructMethodName()` call after field assignments

### Diagnostic Reporting

Implement `IDiagnosticReporter` interface for PostConstruct validation errors:

```csharp
internal class PostConstructDiagnosticReporter : IDiagnosticReporter
{
    private readonly IEnumerable<PostConstructDiagnostic> diagnostics;

    public void ReportDiagnostics(SourceProductionContext context)
    {
        foreach (var diagnostic in diagnostics)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                diagnostic.Rule,
                diagnostic.Location,
                diagnostic.MessageArgs));
        }
    }
}
```

---

## Test Scenarios

### Valid Usage

| Scenario | Description |
|----------|-------------|
| T1 | Simple class with `[Inject]` fields and `[PostConstruct]` method |
| T2 | Inheritance: base and derived both have `[PostConstruct]` |
| T3 | Nested classes each with their own `[PostConstruct]` |
| T4 | Private, protected, internal, and public `[PostConstruct]` methods |
| T5 | `[PostConstruct]` method with void return type |
| T6 | `[PostConstruct]` method with non-void return type (Task, object, etc.) |

### Invalid Usage (Diagnostics Expected)

| Scenario | Expected Diagnostic |
|----------|---------------------|
| T7 | Method with parameters | `INJECT001` |
| T8 | Multiple `[PostConstruct]` methods in same class | `INJECT002` |
| T9 | Static method marked with `[PostConstruct]` | `INJECT003` |
| T10 | `[PostConstruct]` in class without `[Inject]` members | `INJECT004` |

---

## Migration / Backward Compatibility

- **Fully backward compatible:** Existing code without `[PostConstruct]` generates identical output
- **Opt-in feature:** Only affects classes that explicitly use the new attribute
- **No breaking changes** to existing API or generated code structure

---

## Future Considerations

1. **Async PostConstruct:** Support `async Task OnPostConstructAsync()` with factory pattern
2. **PreConstruct:** Method called before field assignments (limited use case)
3. **Ordering:** Explicit order attribute for multiple initialization phases
4. **Conditional PostConstruct:** Only call if certain conditions are met

---

## Acceptance Criteria

1. [ ] `PostConstructAttribute` exists in `MintPlayer.SourceGenerators.Attributes`
2. [ ] Generator detects `[PostConstruct]` methods in classes with `[Inject]` fields
3. [ ] Generated constructor includes call to PostConstruct method after assignments
4. [ ] All four diagnostics implemented and tested
5. [ ] Inheritance works correctly (each class calls its own PostConstruct)
6. [ ] Nested classes validated independently
7. [ ] Unit tests cover all valid and invalid scenarios
8. [ ] Documentation updated

---

## Version

This feature will be included in version **10.11.0** of `MintPlayer.SourceGenerators` and `MintPlayer.SourceGenerators.Attributes`.
