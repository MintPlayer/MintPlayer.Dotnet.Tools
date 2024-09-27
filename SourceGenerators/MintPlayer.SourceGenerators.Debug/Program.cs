using MintPlayer.SourceGenerators.Attributes;
using MintPlayer.SourceGenerators.Debug;
using MintPlayer.SourceGenerators.Generators;

namespace MintPlayer.SourceGenerators.Debug
{
    public interface ITestService1 { }
    public class TestService1 : ITestService1 { }

    public interface ITestService2 { }
    public class TestService2 : ITestService2 { }

    public interface ITestService3 { }
    public class TestService3 : ITestService3 { }

    public interface ITestService4 { }
    public class TestService4 : ITestService4 { }
}

namespace MintPlayer.SourceGenerators.Debug1
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            Console.WriteLine("Hello, World!");
            //Class1.SomeGenericMethod<int, string>(5, "Test");
            Console.ReadKey();
        }
    }

    public partial class Class1
    {
        [GenericMethod(Count = 5)]
        private static void SomeGenericMethod(params object[] parameters)
        {
            var parameterString = string.Join(", ", parameters);
            Console.WriteLine($"Method was called with parameters: {parameterString}");
        }

        [Inject] private readonly ITestService1 testService1;
        [Inject] private readonly ITestService2 testService2;
    }
}

namespace MintPlayer.SourceGenerators.Debug2
{
    public partial class Class2
    {
        [Inject] private readonly ITestService3 testService3;
        [Inject] private readonly ITestService4 testService4;
    }
}