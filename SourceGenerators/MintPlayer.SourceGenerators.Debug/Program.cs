// See https://aka.ms/new-console-template for more information
using Microsoft.Extensions.DependencyInjection;
using MintPlayer.SourceGenerators.Attributes;

Console.WriteLine("Hello, World!");

public interface IBaseTestService1 { }

// ========================================================================================== //

public interface ITestService1 : IBaseTestService1 { }

// ========================================================================================== //

[Register(typeof(IBaseTestService1), ServiceLifetime.Scoped)]
public partial class TestService1 : ITestService1 { }

// ========================================================================================== //

public interface ITestService2 { }

[Register(typeof(ITestService2), ServiceLifetime.Transient)]
public partial class TestService2 : ITestService2
{
    [Inject] private readonly ITestService1 testService1;
}

// ========================================================================================== //

public interface ITestService3
{
    string GetMessage();
}

[Register(typeof(ITestService3), ServiceLifetime.Singleton, "CoreData")]
public partial class TestService3 : ITestService3
{
    [Inject] private readonly ITestService2 testService2;
    public string GetMessage() => "Hello world";
}

// ========================================================================================== //

public interface ITestService4
{
}

[Register(typeof(ITestService4), ServiceLifetime.Scoped)]
public partial class TestService4 : ITestService4
{
    [Inject] private readonly ITestService3 testService3;
}
