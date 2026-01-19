using MintPlayer.SourceGenerators.Attributes;

namespace BaseDependencyForwardingTests;

// ============================================================================
// Test Case 1: Simple inheritance - derived class with no [Inject] fields
// Expected: DerivedClassNoInject should get a constructor that forwards to base
// ============================================================================

public partial class BaseWithInject
{
    [Inject] private readonly ITestService1 testService1;
    [Inject] private readonly ITestService2 testService2;

    public void UseServices()
    {
        Console.WriteLine($"Service1: {testService1}, Service2: {testService2}");
    }
}

/// <summary>
/// This class extends BaseWithInject but has no [Inject] fields of its own.
/// Currently, no constructor is generated for this class, causing a compilation error.
/// Expected behavior: A constructor should be generated that forwards to base(testService1, testService2)
/// </summary>
public partial class DerivedClassNoInject : BaseWithInject
{
    // No [Inject] fields, but should still get a constructor:
    // public DerivedClassNoInject(ITestService1 testService1, ITestService2 testService2)
    //     : base(testService1, testService2) { }

    public void DoSomething()
    {
        Console.WriteLine("Derived class doing something");
    }
}

// ============================================================================
// Test Case 2: Multi-level inheritance where intermediate classes have no [Inject]
// Expected: Each level should get a constructor forwarding to its base
// ============================================================================

public partial class RootClassWithInject
{
    [Inject] private readonly ITestService1 rootService;

    [PostConstruct]
    private void OnRootInitialized()
    {
        Console.WriteLine("Root initialized");
    }
}

/// <summary>
/// Middle level class - no [Inject] fields
/// Expected: public MiddleLevelNoInject(ITestService1 rootService) : base(rootService) { }
/// </summary>
public partial class MiddleLevelNoInject : RootClassWithInject
{
    // No [Inject] fields
    public void MiddleMethod() { }
}

/// <summary>
/// Leaf level class - no [Inject] fields
/// Expected: public LeafLevelNoInject(ITestService1 rootService) : base(rootService) { }
/// </summary>
public partial class LeafLevelNoInject : MiddleLevelNoInject
{
    // No [Inject] fields
    public void LeafMethod() { }
}

// ============================================================================
// Test Case 3: Derived class with PostConstruct but no [Inject] fields
// Expected: Constructor with base call AND PostConstruct method invocation
// ============================================================================

public partial class BaseForPostConstructTest
{
    [Inject] private readonly ITestService1 baseService;
}

/// <summary>
/// Has [PostConstruct] but no [Inject] fields.
/// Expected constructor:
/// public DerivedWithPostConstructOnly(ITestService1 baseService) : base(baseService)
/// {
///     OnDerivedInitialized();
/// }
/// </summary>
public partial class DerivedWithPostConstructOnly : BaseForPostConstructTest
{
    // No [Inject] fields, but has PostConstruct
    [PostConstruct]
    private void OnDerivedInitialized()
    {
        Console.WriteLine("Derived initialized (PostConstruct only, no Inject)");
    }
}

// ============================================================================
// Test Case 4: Mixed scenario - base has [Inject], middle has none, leaf has [Inject]
// Expected: Middle gets forwarding constructor, Leaf gets full constructor
// ============================================================================

public partial class MixedBase
{
    [Inject] private readonly ITestService1 mixedBaseService;
}

/// <summary>
/// No [Inject] fields, but extends class with [Inject]
/// Expected: public MixedMiddle(ITestService1 mixedBaseService) : base(mixedBaseService) { }
/// </summary>
public partial class MixedMiddle : MixedBase
{
    // No [Inject] fields
}

/// <summary>
/// Has its own [Inject] field AND inherits dependencies
/// Expected: public MixedLeaf(ITestService2 ownService, ITestService1 mixedBaseService)
///              : base(mixedBaseService)
///           { this.ownService = ownService; }
/// </summary>
public partial class MixedLeaf : MixedMiddle
{
    [Inject] private readonly ITestService2 ownService;
}

// ============================================================================
// Test Case 5: Non-partial class extending class with [Inject] (diagnostic test)
// This is expected to fail compilation or emit a diagnostic warning
// Uncomment to test diagnostic reporting
// ============================================================================

// UNCOMMENT TO TEST DIAGNOSTIC:
// public class NonPartialDerived : BaseWithInject
// {
//     // This should emit INJECT005 warning/error:
//     // "Class 'NonPartialDerived' extends a class with [Inject] dependencies
//     //  but is not marked as partial. Add the 'partial' modifier to enable
//     //  constructor generation."
// }

// ============================================================================
// Test Case 6: Nested partial class extending outer class with [Inject]
// Expected: Constructor forwarding works for nested classes too
// ============================================================================

public partial class OuterWithInject
{
    [Inject] private readonly ITestService1 outerService;

    public partial class InnerNoInject : OuterWithInject
    {
        // No [Inject] fields
        // Expected: public InnerNoInject(ITestService1 outerService) : base(outerService) { }
    }
}

// ============================================================================
// Test Case 7: Abstract base class with [Inject]
// Expected: Concrete derived class gets forwarding constructor
// ============================================================================

public abstract partial class AbstractBaseWithInject
{
    [Inject] private readonly ITestService1 abstractService;

    public abstract void DoWork();
}

public partial class ConcreteFromAbstract : AbstractBaseWithInject
{
    // No [Inject] fields
    // Expected: public ConcreteFromAbstract(ITestService1 abstractService) : base(abstractService) { }

    public override void DoWork()
    {
        Console.WriteLine("Concrete implementation");
    }
}

// ============================================================================
// Test Case 8: Inheritance tree with manual constructor (no [Inject] anywhere)
// Expected: Constructors SHOULD be generated for derived classes to forward
//           the base constructor parameters
// ============================================================================

public partial class PlainBaseClass
{
    private readonly ITestService1 testService;

    // Manual constructor (not generated by [Inject])
    public PlainBaseClass(ITestService1 testService)
    {
        this.testService = testService;
    }

    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Extends PlainBaseClass which has a manual constructor requiring parameters.
/// Expected: public PlainMiddleClass(ITestService1 testService) : base(testService) { }
/// </summary>
public partial class PlainMiddleClass : PlainBaseClass
{
    // No [Inject] fields, no manual constructor
    public int Value { get; set; }
}

/// <summary>
/// Extends PlainMiddleClass (which should get a generated constructor).
/// Expected: public PlainLeafClass(ITestService1 testService) : base(testService) { }
/// </summary>
public partial class PlainLeafClass : PlainMiddleClass
{
    // No [Inject] fields, no manual constructor
    public bool IsActive { get; set; }

    public void DoSomething()
    {
        Console.WriteLine($"Name: {Name}, Value: {Value}, Active: {IsActive}");
    }
}

// ============================================================================
// Test Case 9: Inheritance tree with parameterless constructor
// Expected: NO constructors should be generated (base has no required params)
// ============================================================================

public partial class ParameterlessBaseClass
{
    public ParameterlessBaseClass() { }  // Parameterless constructor
    public string Name { get; set; } = string.Empty;
}

public partial class ParameterlessDerivedClass : ParameterlessBaseClass
{
    // No [Inject] fields, base has parameterless constructor
    // Expected: NO constructor generated (not needed)
    public int Value { get; set; }
}

// ============================================================================
// Test Case 10: Class already has a constructor with same signature
// Expected: NO constructor should be generated (would cause duplicate error)
// ============================================================================

public partial class BaseWithInjectForDuplicateTest
{
    [Inject] private readonly ITestService1 service1;
}

/// <summary>
/// This class already has a constructor matching what would be generated.
/// The generator should NOT generate a duplicate constructor.
/// </summary>
public partial class DerivedWithExistingConstructor : BaseWithInjectForDuplicateTest
{
    // Manual constructor that matches the signature we would generate
    public DerivedWithExistingConstructor(ITestService1 service1) : base(service1)
    {
        Console.WriteLine("Manual constructor called");
    }
}
