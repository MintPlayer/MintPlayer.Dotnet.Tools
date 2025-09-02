// See https://aka.ms/new-console-template for more information
using Microsoft.Extensions.DependencyInjection;
using MintPlayer.SourceGenerators.Attributes;

Console.WriteLine("Hello, World!");
//var services = new ServiceCollection()
//    .AddSingleton<ITestService3, TestService3>(TestService3.Factory);


public interface IBaseTestService1 { }
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

[Register(typeof(ITestService3), ServiceLifetime.Singleton, "CoreData", nameof(FactoryMain))]
[Register(typeof(ITestService3), ServiceLifetime.Singleton, "CoreData", nameof(FactoryDev))]
[Register(typeof(ITestService3), ServiceLifetime.Singleton, "CoreData", nameof(FactoryFeature))]
public class TestService3 : ITestService3
{
    public TestService3(EBranchType branchType) { }

    public string GetMessage() => "Hello world";

    public static ITestService3 FactoryMain(IServiceProvider serviceProvider)
        => new TestService3(EBranchType.Main);
    public static ITestService3 FactoryDev(IServiceProvider serviceProvider)
        => new TestService3(EBranchType.Dev);
    public static ITestService3 FactoryFeature(IServiceProvider serviceProvider)
        => new TestService3(EBranchType.Feature);
}

public interface ITestService4 { }
[Register(typeof(ITestService4), ServiceLifetime.Scoped)]
public class TestService4 : ITestService4 { }


public enum EBranchType
{
    Main,
    Dev,
    Feature
}