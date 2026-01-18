// See https://aka.ms/new-console-template for more information
using Microsoft.Extensions.DependencyInjection;
using MintPlayer.SourceGenerators.Attributes;

Console.WriteLine("=== PostConstruct Demo ===");
Console.WriteLine();

// Build service provider
var services = new ServiceCollection()
    .AddScoped<ITestService1, TestService1>()
    .AddScoped<ITestService2, TestService2>()
    .AddScoped<PostConstructDemo>()
    .BuildServiceProvider();

// Resolve the demo service - this will trigger the constructor and PostConstruct
Console.WriteLine("Resolving PostConstructDemo...");
var demo = services.GetRequiredService<PostConstructDemo>();
Console.WriteLine($"Demo service resolved. IsInitialized = {demo.IsInitialized}");
Console.WriteLine();

Console.WriteLine("=== End Demo ===");
Console.WriteLine();


/// <summary>
/// Some base service.
/// Add more comments here.
/// </summary>
public partial interface IBaseTestService1 { }
public interface ITestService1 : IBaseTestService1 { }
[Register(typeof(ITestService1), ServiceLifetime.Scoped)]
public class TestService1 : ITestService1 { }

public interface ITestService2 { }
[Register(typeof(ITestService2), ServiceLifetime.Transient)]
public class TestService2 : ITestService2 { }

public interface ITestService3
{
    string GetMessage();
}

[Register(typeof(ITestService3), ServiceLifetime.Singleton, "CoreData")]
[Register(typeof(ITestService4), ServiceLifetime.Scoped, "CoreData")]
public class TestService34 : ITestService3, ITestService4
{
    public TestService34(EBranchType branchType) { }

    public string GetMessage() => "Hello world";

    [RegisterFactory]
    public static ITestService3 FactoryMain(IServiceProvider serviceProvider)
        => new TestService34(EBranchType.Main);

    [RegisterFactory]
    public static ITestService3 FactoryDev(IServiceProvider serviceProvider)
        => new TestService34(EBranchType.Dev);

    [RegisterFactory]
    public static ITestService3 FactoryFeature(IServiceProvider serviceProvider)
        => new TestService34(EBranchType.Feature);

    [RegisterFactory]
    public static ITestService4 FactoryFeature4(IServiceProvider serviceProvider)
        => new TestService34(EBranchType.Feature);
}

public interface ITestService4 { }


public enum EBranchType
{
    Main,
    Dev,
    Feature
}

/// <summary>
/// Test
/// </summary>
public partial struct NestedStruct1
{
    public partial class NestedClass1
    {
        public partial struct NestedStruct2
        {
            /// <summary>
            /// This is nested class with xml comments
            /// </summary>
            public partial class NestedClass2
            {
            }
        }
    }
}

/// <summary>
/// Demonstrates the [PostConstruct] attribute functionality.
/// The OnInitialized method will be called automatically after
/// all injected fields are assigned in the generated constructor.
/// </summary>
public partial class PostConstructDemo
{
    [Inject] private readonly ITestService1 testService1;
    [Inject] private readonly ITestService2 testService2;

    public bool IsInitialized { get; private set; }

    [PostConstruct]
    private void OnInitialized()
    {
        Console.WriteLine("  [PostConstruct] OnInitialized() called!");
        Console.WriteLine($"  - testService1 is null: {testService1 is null}");
        Console.WriteLine($"  - testService2 is null: {testService2 is null}");

        // Both services should be available at this point
        IsInitialized = testService1 is not null && testService2 is not null;
        Console.WriteLine($"  - IsInitialized set to: {IsInitialized}");
    }
}