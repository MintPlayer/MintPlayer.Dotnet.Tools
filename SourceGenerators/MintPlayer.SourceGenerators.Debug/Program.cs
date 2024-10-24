// See https://aka.ms/new-console-template for more information
using Microsoft.Extensions.DependencyInjection;
using MintPlayer.SourceGenerators.Attributes;
using MintPlayer.SourceGenerators.Generators;

Console.WriteLine("Hello, World!");

//namespace Hello
//{
    public partial class Class1
    {
        [Inject] private readonly ITestService1 testService1;
        [Inject] private readonly ITestService2 testService2;
    }

    public interface ITestService1 { }
    [Register(typeof(ITestService1), ServiceLifetime.Scoped, "TestServices")]
    public class TestService1 : ITestService1 { }

    public interface ITestService2 { }
    [Register(typeof(ITestService2), ServiceLifetime.Transient, "TestServices")]
    public class TestService2 : ITestService2 { }

    public interface ITestService3 { }
    [Register(typeof(ITestService3), ServiceLifetime.Singleton, "TestServices")]
    public class TestService3 : ITestService3 { }

    public interface ITestService4 { }
    [Register(typeof(ITestService4), ServiceLifetime.Scoped, "TestServices")]
    public class TestService4 : ITestService4 { }

    public class TestClass
    {
        [GenericMethod(Count = 5)]
        public string TestMethod() => "Test";
    }
//}