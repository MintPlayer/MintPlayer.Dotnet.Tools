// See https://aka.ms/new-console-template for more information
using MintPlayer.SourceGenerators.Attributes;

Console.WriteLine("Hello, World!");

public partial class Class1
{
    [Inject] private readonly ITestService1 testService1;
    [Inject] private readonly ITestService2 testService2;
}

public interface ITestService1 { }
public class TestService1 : ITestService1 { }

public interface ITestService2 { }
public class TestService2 : ITestService2 { }

public interface ITestService3 { }
public class TestService3 : ITestService3 { }

public interface ITestService4 { }
public class TestService4 : ITestService4 { }