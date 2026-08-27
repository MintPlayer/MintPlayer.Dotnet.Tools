using System.Reflection;
using Microsoft.CodeAnalysis;

namespace MintPlayer.SourceGenerators.Tests._Infrastructure;

/// <summary>
/// Layer 2: compile the generator's output to a real assembly, load it, and run it. This is
/// the only layer that catches a generator whose output looks right and behaves wrong — a
/// service registered with the wrong lifetime, a mapper that drops a property, a service graph
/// that cannot actually be resolved.
/// </summary>
internal static class EmitAndLoad
{
    /// <summary>
    /// Emits <paramref name="run"/>'s updated compilation and loads it.
    /// </summary>
    /// <remarks>
    /// Deliberately <see cref="Assembly.Load(byte[])"/> into the DEFAULT context, never a
    /// collectible AssemblyLoadContext. A collectible ALC needs the
    /// [MethodImpl(NoInlining)] + explicit-GC dance to actually unload, and gets flaky when it
    /// does not; a test process emits a few dozen small fixtures and exits, so the leak is
    /// bounded and irrelevant. If two fixtures ever need the same assembly name in one
    /// process, give them distinct names instead of reaching for an ALC.
    /// </remarks>
    public static Assembly Emit(this GeneratorRun run)
    {
        var errors = run.UpdatedCompilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if (errors.Count > 0)
            throw new InvalidOperationException(
                "Cannot emit: the generated code does not compile." + Environment.NewLine +
                string.Join(Environment.NewLine, errors.Select(d => d.ToString())));

        using var stream = new MemoryStream();
        var result = run.UpdatedCompilation.Emit(stream);

        if (!result.Success)
            throw new InvalidOperationException(
                "Emit failed." + Environment.NewLine +
                string.Join(Environment.NewLine, result.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.ToString())));

        return Assembly.Load(stream.ToArray());
    }

    /// <summary>Finds a generated type by its full name, with a message that lists what IS there.</summary>
    public static Type GetGeneratedType(this Assembly assembly, string fullName)
        => assembly.GetType(fullName)
           ?? throw new InvalidOperationException(
               $"'{fullName}' was not generated. Types present: " +
               string.Join(", ", assembly.GetTypes().Select(t => t.FullName)));

    /// <summary>Finds a generated static method anywhere in the assembly.</summary>
    public static MethodInfo GetGeneratedMethod(this Assembly assembly, string methodName)
    {
        var method = assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .FirstOrDefault(m => m.Name == methodName);

        return method ?? throw new InvalidOperationException(
            $"No public static method '{methodName}' was generated. Methods present: " +
            string.Join(", ", assembly.GetTypes()
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
                .Select(m => $"{m.DeclaringType?.Name}.{m.Name}")));
    }
}
