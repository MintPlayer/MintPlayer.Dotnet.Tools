using MintPlayer.SourceGenerators.Attributes;

namespace Testje;

// NOTE: The following test cases are commented out because they intentionally produce errors.
// Uncomment to test that diagnostics are correctly reported.

//// INJECT001: PostConstruct method with parameters (should produce diagnostic)
//public partial class ClassWithParameterizedPostConstruct
//{
//    [Inject] private readonly ITestService1 testService1;
//
//    [PostConstruct]
//    private void OnInitialized(string message) // Error: must be parameterless
//    {
//        Console.WriteLine(message);
//    }
//}

//// INJECT002: Multiple PostConstruct methods (should produce diagnostic)
//public partial class ClassWithMultiplePostConstruct
//{
//    [Inject] private readonly ITestService1 testService1;
//
//    [PostConstruct]
//    private void OnInitialized1() // Error: only one allowed
//    {
//        Console.WriteLine("First");
//    }
//
//    [PostConstruct]
//    private void OnInitialized2() // Error: only one allowed
//    {
//        Console.WriteLine("Second");
//    }
//}

//// INJECT003: Static PostConstruct method (should produce diagnostic)
//public partial class ClassWithStaticPostConstruct
//{
//    [Inject] private readonly ITestService1 testService1;
//
//    [PostConstruct]
//    private static void OnInitialized() // Error: cannot be static
//    {
//        Console.WriteLine("Static PostConstruct");
//    }
//}

//// INJECT004: PostConstruct without Inject fields (should produce warning)
//public partial class ClassWithPostConstructNoInject
//{
//    private readonly ITestService1 testService1;
//
//    public ClassWithPostConstructNoInject(ITestService1 testService1)
//    {
//        this.testService1 = testService1;
//    }
//
//    [PostConstruct]
//    private void OnInitialized() // Warning: no [Inject] members
//    {
//        Console.WriteLine("No inject members");
//    }
//}

// Valid: Nested class with its own PostConstruct (should NOT produce diagnostic)
public partial class OuterClassWithPostConstruct
{
    [Inject] private readonly ITestService1 testService1;

    [PostConstruct]
    private void OnOuterInitialized()
    {
        Console.WriteLine("Outer initialized!");
    }

    public partial class InnerClassWithPostConstruct
    {
        [Inject] private readonly ITestService2 testService2;

        [PostConstruct]
        private void OnInnerInitialized()
        {
            Console.WriteLine("Inner initialized!");
        }
    }
}
