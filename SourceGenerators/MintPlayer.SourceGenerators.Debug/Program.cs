using MintPlayer.SourceGenerators.Generators;

namespace MintPlayer.SourceGenerators.Debug;

internal class Program
{
    private static void Main(string[] args)
    {
        Console.WriteLine("Hello, World!");
        Class1.SomeGenericMethod<int, string>(5, "Test");
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
}