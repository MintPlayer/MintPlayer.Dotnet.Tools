using System.Reflection;
using MintPlayer.SourceGenerators.Tests._Infrastructure;

namespace MintPlayer.SourceGenerators.Tests.Guards;

/// <summary>
/// No value that flows through a generator's incremental pipeline holds a Roslyn object (PRD-TypeKindRobustness M8).
/// </summary>
/// <remarks>
/// <para>
/// A <c>Location</c>, a symbol, a syntax node or a <c>DiagnosticDescriptor</c> in a cached value keeps the compilation
/// it came from alive, and compares by reference, so the step after it re-runs on every keystroke. Diagnostics travel
/// as an id-based <c>DiagnosticInfo</c> with a <c>LocationKey</c> instead; the Tools types themselves are pinned by
/// <c>DiscoveryTests.PipelineValuesHoldNoRoslynObjects</c> in the Tools tests.
/// </para>
/// <para>
/// MINT001 checks the properties of every <c>[GenerateEquality]</c> model at compile time. This scan also covers
/// what MINT001 cannot see: fields (including private ones), the producers that carry the models to
/// <c>ProduceCode</c>, and models without the attribute, in every generator assembly this project tests.
/// </para>
/// </remarks>
public class PipelineModelGuardTests
{
    private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    public static TheoryData<string> GeneratorAssemblies()
    {
        var data = new TheoryData<string>();
        foreach (var name in GeneratorHarness.AllGenerators().Select(g => g.Assembly).Distinct())
            data.Add(name);
        return data;
    }

    [Theory]
    [MemberData(nameof(GeneratorAssemblies))]
    public void PipelineValuesHoldNoRoslynObjects(string assemblyName)
    {
        var assembly = GeneratorHarness.AllGenerators().First(g => g.Assembly == assemblyName).Generator.Assembly;
        var roots = assembly.GetTypes().Where(IsPipelineValue).ToList();

        // Guard: a scan that finds nothing to scan passes vacuously.
        roots.Should().NotBeEmpty($"{assemblyName} has models and producers; the scan must find them");

        var offenders = new List<string>();
        var visited = new HashSet<Type>();
        foreach (var root in roots)
            Scan(root, root.FullName!, assembly, visited, offenders);

        offenders.Should().BeEmpty($"pipeline values in {assemblyName} must hold no Microsoft.CodeAnalysis type");
    }

    /// <summary>
    /// A model (in a <c>Models</c> namespace, or decorated with <c>[GenerateEquality]</c>) or a producer.
    /// </summary>
    private static bool IsPipelineValue(Type type)
    {
        if (type.IsEnum || type.IsInterface || IsCompilerGenerated(type)) return false;
        if (type.Namespace?.Split('.').Contains("Models") == true) return true;
        if (type.GetCustomAttributesData().Any(a => a.AttributeType.Name == "GenerateEqualityAttribute")) return true;
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.FullName == "MintPlayer.SourceGenerators.Tools.Producer") return true;
        }
        return false;
    }

    private static bool IsCompilerGenerated(Type type)
        => type.GetCustomAttributesData().Any(a => a.AttributeType.FullName == "System.Runtime.CompilerServices.CompilerGeneratedAttribute");

    private static void Scan(Type type, string path, Assembly owner, HashSet<Type> visited, List<string> offenders)
    {
        if (type.IsArray || type.IsByRef || type.IsPointer)
        {
            Scan(type.GetElementType()!, path + "[]", owner, visited, offenders);
            return;
        }

        if (IsRoslyn(type))
        {
            offenders.Add($"{path}: {type.FullName ?? type.Name}");
            return;
        }

        if (type.IsGenericParameter || !visited.Add(type)) return;

        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
                Scan(argument, $"{path}<{argument.Name}>", owner, visited, offenders);
        }

        // Fields of the generator's own types and of the Tools types it builds on (EquatableArray, Discovered,
        // DiagnosticInfo). The BCL is not walked: a string or an ImmutableArray holds nothing of Roslyn's beyond its
        // type arguments, which are scanned above.
        var walk = type.Assembly == owner || type.Assembly.GetName().Name == "MintPlayer.SourceGenerators.Tools";
        if (!walk) return;

        for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
        {
            foreach (var field in current.GetFields(InstanceFields))
                Scan(field.FieldType, $"{path}.{field.Name}", owner, visited, offenders);
        }
    }

    private static bool IsRoslyn(Type type)
        => type.Namespace is "Microsoft.CodeAnalysis" || type.Namespace?.StartsWith("Microsoft.CodeAnalysis.", StringComparison.Ordinal) == true;
}
