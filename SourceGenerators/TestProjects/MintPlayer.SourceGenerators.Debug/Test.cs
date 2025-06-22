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
}

//[BaseConstructorParameter<string>("code", "FR")]
public partial class Class1 : BaseClass
{
    [Inject] private readonly ITestService1 testService1;
    [Inject] private readonly ITestService2 testService2;
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
