// See https://aka.ms/new-console-template for more information
using Microsoft.Extensions.DependencyInjection;
using MintPlayer.SourceGenerators.Attributes;

Console.WriteLine("Hello, World!");

public partial class Class1
{
    [Inject] private readonly ITestService1 testService1;
    [Inject] private readonly ITestService2 testService2;
}

public interface ITestService1 { }
[Register(typeof(ITestService1), ServiceLifetime.Scoped)]
public class TestService1 : ITestService1 { }

public interface ITestService2 { }
[Register(typeof(ITestService2), ServiceLifetime.Transient)]
public class TestService2 : ITestService2 { }

public interface ITestService3 { }
[Register(typeof(ITestService3), ServiceLifetime.Singleton)]
public class TestService3 : ITestService3 { }

public interface ITestService4 { }
[Register(typeof(ITestService4), ServiceLifetime.Scoped)]
public class TestService4 : ITestService4 { }