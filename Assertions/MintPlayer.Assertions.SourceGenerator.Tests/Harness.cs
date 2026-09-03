namespace MintPlayer.Assertions.SourceGenerator.Tests;

/// <summary>
/// The one <see cref="GeneratorHarness"/> every test in this project uses.
/// </summary>
/// <remarks>
/// Shared and static because loading the component assembly and resolving the BCL reference set
/// are the expensive parts of a run; the harness itself is immutable, so sharing it is safe.
///
/// <c>MintPlayer.Assertions</c> has to be in the reference set (via the non-generic overload:
/// <c>AssertionExtensions</c> is static, and C# forbids a static class as a type argument): every fixture calls
/// <c>.Should()</c>, and without it the analyzers see a compilation full of CS0103 and find
/// nothing to report — a failure that looks like the analyzer being broken.
/// </remarks>
internal static class Harness
{
    public static readonly GeneratorHarness Instance = GeneratorHarness
        .ForAssembly("MintPlayer.Assertions.SourceGenerator")
        .AddReferences(typeof(AssertionExtensions));
}
