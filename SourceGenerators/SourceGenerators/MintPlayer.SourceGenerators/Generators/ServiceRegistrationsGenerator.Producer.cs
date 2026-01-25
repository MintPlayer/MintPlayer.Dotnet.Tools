using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using MintPlayer.SourceGenerators.Attributes;
using MintPlayer.SourceGenerators.Extensions;
using MintPlayer.SourceGenerators.Models;
using MintPlayer.SourceGenerators.Tools;
using System.CodeDom.Compiler;
using System.Text;

namespace MintPlayer.SourceGenerators.Generators;

public class RegistrationsProducer : Producer, IDiagnosticReporter
{
    private const string MethodPrefix = "Add";
    private const string DefaultMethodNameFallback = "Services";

    private readonly IEnumerable<ServiceRegistration> serviceRegistrations;
    private readonly bool knowsDependencyInjectionAbstractions;
    private readonly AssemblyRegistrationConfig assemblyConfig;
    private readonly Dictionary<ServiceLifetime, string> lifetimeNames = new()
    {
        { ServiceLifetime.Singleton, nameof(ServiceLifetime.Singleton) },
        { ServiceLifetime.Scoped, nameof(ServiceLifetime.Scoped) },
        { ServiceLifetime.Transient, nameof(ServiceLifetime.Transient) },
    };

    public RegistrationsProducer(IEnumerable<ServiceRegistration> serviceRegistrations, bool knowsDependencyInjectionAbstractions, string rootNamespace, AssemblyRegistrationConfig assemblyConfig) : base(rootNamespace, "ServiceMethods.g.cs")
    {
        this.serviceRegistrations = serviceRegistrations;
        this.knowsDependencyInjectionAbstractions = knowsDependencyInjectionAbstractions;
        this.assemblyConfig = assemblyConfig;
    }

    public IEnumerable<Diagnostic> GetDiagnostics(Compilation compilation)
    {
        return serviceRegistrations.Where(r => r.HasError)
            .Select(registration => registration.AppliedOn switch
            {
                ERegistrationAppliedOn.Class => DiagnosticRules.RegisterAttributeClassRequiresLifetime.Create(registration.Location?.ToLocation(compilation)),
                ERegistrationAppliedOn.Assembly => DiagnosticRules.RegisterAttributeAssemblyRequiresType.Create(registration.Location?.ToLocation(compilation)),
                _ => null,
            })
            .NotNull();
    }

    protected override void ProduceSource(IndentedTextWriter writer, CancellationToken cancellationToken)
    {
        writer.WriteLine(Header);
        writer.WriteLine();

        if (knowsDependencyInjectionAbstractions)
        {
            writer.WriteLine($"using Microsoft.Extensions.DependencyInjection;");
            writer.WriteLine();
            using (writer.OpenBlock($"namespace {RootNamespace}"))
            {
                using (writer.OpenBlock("public static class DependencyInjectionExtensionMethods"))
                {
                    foreach (var methodGroup in serviceRegistrations.Where(sr => sr is not null && !sr.HasError).GroupBy(sr => sr.MethodNameHint))
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        // Resolve method name with precedence: explicit hint > assembly config > assembly name
                        var methodName = methodGroup.Key.NullIfEmpty()
                            ?? assemblyConfig.DefaultMethodName.NullIfEmpty()
                            ?? SanitizeAssemblyName(assemblyConfig.AssemblyName);

                        // Enforce "Add" prefix
                        methodName = methodName.StartsWith(MethodPrefix) ? methodName : $"{MethodPrefix}{methodName}";

                        // Resolve accessibility with precedence: explicit registration > assembly config > default (Public)
                        var accessibility = methodGroup.Select(m => m.Accessibility).Where(a => a is not EGeneratedAccessibility.Unspecified).ToArray() is { Length: > 0 } items
                            ? items.First()
                            : assemblyConfig.DefaultAccessibility is not EGeneratedAccessibility.Unspecified
                                ? assemblyConfig.DefaultAccessibility
                                : EGeneratedAccessibility.Public;
                        var accessibilityString = accessibility switch
                        {
                            EGeneratedAccessibility.Internal => "internal",
                            _ => "public",
                        };

                        // Separate non-generic and generic services
                        var nonGenericServices = methodGroup.Where(s => !s.IsGeneric).ToList();
                        var genericServices = methodGroup.Where(s => s.IsGeneric).ToList();

                        // 1. Generate non-generic method if any non-generic services exist
                        if (nonGenericServices.Count > 0)
                        {
                            GenerateNonGenericMethod(writer, methodName, accessibilityString, nonGenericServices, cancellationToken);
                        }

                        // 2. Generate generic methods
                        if (genericServices.Count > 0)
                        {
                            GenerateGenericMethods(writer, methodName, accessibilityString, genericServices, cancellationToken);
                        }
                    }
                }
            }
        }
        else
        {
            writer.WriteLine("// Cannot generate service registration methods because the project does not reference Microsoft.Extensions.DependencyInjection.Abstractions");
        }
    }

    /// <summary>
    /// Generates a non-generic extension method for the given services.
    /// </summary>
    private void GenerateNonGenericMethod(IndentedTextWriter writer, string methodName, string accessibilityString, List<ServiceRegistration> services, CancellationToken cancellationToken)
    {
        using (writer.OpenBlock($"{accessibilityString} static global::Microsoft.Extensions.DependencyInjection.IServiceCollection {methodName}(this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)"))
        {
            writer.WriteLine("return services");
            writer.Indent++;

            var currentIndex = 0;
            var total = services.Count;
            foreach (var svc in services)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (svc.ServiceTypeName is null)
                {
                    if (svc.FactoryNames.Length > 0)
                    {
                        var currentFactoryIndex = 0;
                        foreach (var factoryName in svc.FactoryNames)
                        {
                            writer.Write($".Add{lifetimeNames[svc.Lifetime]}<{svc.ImplementationTypeName}>({svc.ImplementationTypeName}.{factoryName})");
                            if (++currentFactoryIndex != svc.FactoryNames.Length)
                                writer.WriteLine();
                        }
                    }
                    else
                    {
                        writer.Write($".Add{lifetimeNames[svc.Lifetime]}<{svc.ImplementationTypeName}>()");
                    }
                }
                else
                {
                    if (svc.FactoryNames.Length > 0)
                    {
                        var currentFactoryIndex = 0;
                        foreach (var factoryName in svc.FactoryNames)
                        {
                            writer.Write($".Add{lifetimeNames[svc.Lifetime]}<{svc.ServiceTypeName}>({svc.ImplementationTypeName}.{factoryName})");
                            if (++currentFactoryIndex != svc.FactoryNames.Length)
                                writer.WriteLine();
                        }
                    }
                    else
                    {
                        writer.Write($".Add{lifetimeNames[svc.Lifetime]}<{svc.ServiceTypeName}, {svc.ImplementationTypeName}>()");
                    }
                }

                if (++currentIndex == total)
                    writer.Write(";");

                writer.WriteLine();
            }

            writer.Indent--;
        }
    }

    /// <summary>
    /// Generates generic extension methods for the given services.
    /// Groups by arity (type parameter count), then by constraint signature.
    /// </summary>
    private void GenerateGenericMethods(IndentedTextWriter writer, string methodName, string accessibilityString, List<ServiceRegistration> genericServices, CancellationToken cancellationToken)
    {
        // Group by type parameter count (arity)
        var byArity = genericServices
            .GroupBy(s => s.GenericInfo!.TypeParameterNames.Length)
            .OrderBy(g => g.Key);

        foreach (var arityGroup in byArity)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Within same arity, group by constraint signature
            var byConstraints = arityGroup
                .GroupBy(s => GetConstraintSignature(s.GenericInfo!))
                .ToList();

            if (byConstraints.Count == 1)
            {
                // All services at this arity have compatible constraints - generate single method
                var services = byConstraints[0].ToList();
                GenerateSingleGenericMethod(writer, methodName, accessibilityString, services, cancellationToken);
            }
            else
            {
                // Multiple incompatible constraint groups at same arity
                // First group gets the clean name, rest get suffixes
                bool isFirst = true;
                foreach (var constraintGroup in byConstraints)
                {
                    var services = constraintGroup.ToList();
                    if (isFirst)
                    {
                        GenerateSingleGenericMethod(writer, methodName, accessibilityString, services, cancellationToken);
                        isFirst = false;
                    }
                    else
                    {
                        // Use first implementation type name as suffix
                        var suffix = services[0].GenericInfo!.ImplementationSimpleName;
                        GenerateSingleGenericMethod(writer, $"{methodName}_{suffix}", accessibilityString, services, cancellationToken);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Generates a single generic extension method for services with compatible type parameters.
    /// </summary>
    private void GenerateSingleGenericMethod(IndentedTextWriter writer, string methodName, string accessibilityString, List<ServiceRegistration> services, CancellationToken cancellationToken)
    {
        // Use the first service's generic info for the method signature
        var genericInfo = services[0].GenericInfo!;
        var typeParamList = $"<{string.Join(", ", genericInfo.TypeParameterNames)}>";

        // Build method signature
        var methodSignature = $"{accessibilityString} static global::Microsoft.Extensions.DependencyInjection.IServiceCollection {methodName}{typeParamList}(this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)";

        // Add constraint clauses
        if (genericInfo.ConstraintClauses.Length > 0)
        {
            writer.WriteLine(methodSignature);
            writer.Indent++;
            foreach (var constraint in genericInfo.ConstraintClauses)
            {
                writer.WriteLine(constraint);
            }
            writer.Indent--;
            writer.WriteLine("{");
        }
        else
        {
            writer.WriteLine(methodSignature);
            writer.WriteLine("{");
        }

        writer.Indent++;
        writer.WriteLine("return services");
        writer.Indent++;

        var currentIndex = 0;
        var total = services.Count;
        foreach (var svc in services)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = svc.GenericInfo!;
            writer.Write($".Add{lifetimeNames[svc.Lifetime]}<{info.GenericServiceTypeName}, {info.GenericImplementationTypeName}>()");

            if (++currentIndex == total)
                writer.Write(";");

            writer.WriteLine();
        }

        writer.Indent--;
        writer.Indent--;
        writer.WriteLine("}");
    }

    /// <summary>
    /// Gets a constraint signature string for grouping services with compatible constraints.
    /// Services with the same constraint signature can be combined into a single method.
    /// </summary>
    private static string GetConstraintSignature(GenericTypeInfo info)
    {
        // Combine arity and constraints into a signature
        // Format: "2|where T1 : class|where T2 : IEquatable<T1>"
        var arity = info.TypeParameterNames.Length;
        var constraints = string.Join("|", info.ConstraintClauses.OrderBy(c => c));
        return $"{arity}|{constraints}";
    }

    /// <summary>
    /// Sanitizes an assembly name for use as a method name suffix.
    /// Splits by dots, capitalizes each part, joins them, and removes invalid characters.
    /// </summary>
    private static string SanitizeAssemblyName(string assemblyName)
    {
        if (string.IsNullOrEmpty(assemblyName))
            return DefaultMethodNameFallback;

        // Split by dots, capitalize each part, join
        var parts = assemblyName.Split('.')
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => char.ToUpperInvariant(p[0]) + p.Substring(1));

        var result = string.Join("", parts);

        // Remove invalid identifier characters
        var sanitized = new StringBuilder();
        foreach (var c in result)
        {
            if (char.IsLetterOrDigit(c) || c == '_')
                sanitized.Append(c);
        }

        // Ensure doesn't start with digit
        if (sanitized.Length > 0 && char.IsDigit(sanitized[0]))
            sanitized.Insert(0, '_');

        return sanitized.Length > 0 ? sanitized.ToString() : DefaultMethodNameFallback;
    }
}
