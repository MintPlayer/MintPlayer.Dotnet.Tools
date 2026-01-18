using Microsoft.Extensions.DependencyInjection;
using MintPlayer.SourceGenerators.Attributes;

namespace Testje;

public partial class BaseClass
{
    //private readonly string code;
    [Inject] private readonly ITestService1 testService1;
    //public BaseClass(ITestService1 testService1)
    //{
    //    //this.code = code;
    //    this.testService1 = testService1;
    //}

    [PostConstruct]
    private void OnBaseInitialized()
    {
        // This method will be called after testService1 is injected
        Console.WriteLine("BaseClass initialized!");
    }
}

//[BaseConstructorParameter<string>("code", "FR")]
public partial class Class1 : BaseClass
{
    [Inject] private readonly ITestService1 testService1;
    [Inject] private readonly ITestService2 testService2;

    [PostConstruct]
    private void OnClass1Initialized()
    {
        // This method will be called after testService1 and testService2 are injected
        Console.WriteLine("Class1 initialized!");
    }
}

public partial class Class2
{
    [Inject] private readonly ITestService3 testService3;
    [Inject] private readonly ITestService4 testService4;

    void Test()
    {
        var message = testService3.GetMessage();
    }
}

public partial class Class3 : Class1
{
    [Inject] private readonly ITestService3 testService3;
    [Inject] private readonly ITestService4 testService4;
    void a()
    {
        new ServiceCollection().AddScoped<ITestService1, TestService1>();
    }
}

// Test case: PostConstruct with different return types (should work)
public partial class ClassWithTaskPostConstruct
{
    [Inject] private readonly ITestService1 testService1;

    [PostConstruct]
    private Task OnInitializedAsync()
    {
        Console.WriteLine("ClassWithTaskPostConstruct initialized!");
        return Task.CompletedTask;
    }
}

// Test case: PostConstruct with internal access modifier (should work)
public partial class ClassWithInternalPostConstruct
{
    [Inject] private readonly ITestService1 testService1;

    [PostConstruct]
    internal void OnInitialized()
    {
        Console.WriteLine("ClassWithInternalPostConstruct initialized!");
    }
}
