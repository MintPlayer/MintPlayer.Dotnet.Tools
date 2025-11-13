// See https://aka.ms/new-console-template for more information
using Microsoft.Extensions.DependencyInjection;
using MintPlayer.SourceGenerators.Attributes;

Console.WriteLine("Hello, World!");
//var services = new ServiceCollection()
//    .AddSingleton<ITestService3, TestService3>(TestService3.Factory);


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